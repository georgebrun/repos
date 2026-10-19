using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BreakersOfE.Services
{
    /// <summary>One card EDHREC recommends for a commander.</summary>
    public sealed class EdhrecCard
    {
        public string Name { get; init; } = "";
        /// <summary>The list it's in on EDHREC ("High Synergy Cards", "Creatures" …).</summary>
        public string Section { get; init; } = "";
        /// <summary>Decks with this commander that run the card.</summary>
        public int Decks { get; init; }
        /// <summary>Decks with this commander that could run it (its colors, its release).</summary>
        public int PotentialDecks { get; init; }
        /// <summary>How much more this commander's decks run it than other decks of its colors (−1 … 1).</summary>
        public double Synergy { get; init; }
        /// <summary>Share of this commander's decks that run it (0 … 100), −1 when unknown.</summary>
        public double InclusionPercent => PotentialDecks > 0 ? Decks * 100.0 / PotentialDecks : -1;
    }

    /// <summary>A commander's EDHREC page, as BoE keeps it.</summary>
    public sealed class EdhrecPage
    {
        public string Slug { get; init; } = "";
        /// <summary>When it was downloaded (local time).</summary>
        public DateTime Fetched { get; init; }
        /// <summary>Roughly how many decks EDHREC has for this commander.</summary>
        public int Decks { get; init; }
        /// <summary>Every recommended card once (its first list), in EDHREC's order.</summary>
        public List<EdhrecCard> Cards { get; init; } = new();
        /// <summary>The section names, in EDHREC's order.</summary>
        public List<string> Sections { get; init; } = new();
        /// <summary>Set when the page couldn't be refreshed and this is an older saved copy.</summary>
        public string Note { get; init; } = "";
        public string Url => EdhrecService.CommanderUrl(Slug);
    }

    /// <summary>
    /// EDHREC (edhrec.com): recommendations per commander, and card links.
    /// EDHREC has no official API: its pages load JSON files (json.edhrec.com),
    /// which this reads. Each commander's file is saved under the data
    /// folder's EDHREC folder and used for 7 days, so the site is called only
    /// when a page is opened the first time, is a week old, or Refresh is
    /// clicked. If EDHREC changes or is down, callers get a message, never a crash.
    /// </summary>
    public static class EdhrecService
    {
        public static readonly TimeSpan KeepFor = TimeSpan.FromDays(7);

        private const string JsonBase = "https://json.edhrec.com/pages/";
        private const string SiteBase = "https://edhrec.com/";

        private static readonly Lazy<HttpClient> _http = new(() =>
        {
            var h = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            h.DefaultRequestHeaders.Add("User-Agent", "BreakersOfE/2.1 (+https://github.com/georgebrun/repos)");
            h.DefaultRequestHeaders.Add("Accept", "application/json");
            return h;
        });

        public static string CacheFolder =>
            Directory.CreateDirectory(Path.Combine(AppFolderService.RootFolder, "EDHREC", "commanders")).FullName;

        // ══ Names → EDHREC addresses ═══════════════════════════════════════

        /// <summary>
        /// EDHREC's name for a card in its addresses: lower case, accents and
        /// punctuation dropped, spaces to dashes ("Atraxa, Praetors' Voice" →
        /// "atraxa-praetors-voice"). Two-sided cards use the front name, except
        /// split cards ("Fire // Ice" → "fire-ice").
        /// </summary>
        public static string Slug(string name, string layout = "")
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            bool bothHalves = layout is "split" or "aftermath";
            string n = bothHalves ? name.Replace("//", " ") : name.Split(" // ")[0];

            var sb = new StringBuilder();
            foreach (char ch in n.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
                char c = char.ToLowerInvariant(ch);
                if (c is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(c);
                else if (c is ' ' or '-' or '_') sb.Append('-');
                // everything else (' , . ! : " & …) is dropped
            }
            string slug = sb.ToString();
            while (slug.Contains("--")) slug = slug.Replace("--", "-");
            return slug.Trim('-');
        }

        /// <summary>The commander page's slug: one commander, or partners joined A→Z.</summary>
        public static string CommanderSlug(IEnumerable<string> commanderNames) =>
            string.Join("-", commanderNames.Select(n => Slug(n)).Where(s => s.Length > 0)
                                           .Distinct().OrderBy(s => s, StringComparer.Ordinal));

        public static string CommanderUrl(string slug) => SiteBase + "commanders/" + slug;

        /// <summary>A card's page on EDHREC.</summary>
        public static string CardUrl(string name, string layout = "") => SiteBase + "cards/" + Slug(name, layout);

        // ══ Salt scores (EDHREC's yearly vote: how much players dislike facing a card) ══

        /// <summary>Pages of EDHREC's salt list to download (100 cards each, saltiest first).</summary>
        private const int SaltPages = 30;
        /// <summary>Stop once a page's cards are below this: the rest are mild.</summary>
        private const double SaltFloor = 0.5;

        public static string SaltPath => Path.Combine(AppFolderService.RootFolder, "EDHREC", "salt.json");
        public static bool HasSalt => File.Exists(SaltPath);

        private sealed class SaltFile
        {
            public string Updated { get; set; } = "";
            /// <summary>The lowest score kept; cards not listed are below it.</summary>
            public double Lowest { get; set; }
            /// <summary>Saltiest first: [name, score].</summary>
            public List<SaltEntry> Cards { get; set; } = new();
        }

        private sealed class SaltEntry
        {
            public string N { get; set; } = "";
            public double S { get; set; }
        }

        /// <summary>A card's salt: its score (0 … 4), its rank on EDHREC's list, and the list's lowest score.</summary>
        public readonly record struct SaltInfo(double Score, int Rank, double Lowest);

        private static Dictionary<string, SaltInfo>? _salt;
        private static double _saltLowest;
        private static readonly object _saltLock = new();

        /// <summary>
        /// Download EDHREC's salt list (the saltiest cards first, page by page,
        /// until the scores drop below 0.5) into EDHREC\salt.json. Called by the
        /// full Database Update. Returns how many cards were saved.
        /// </summary>
        public static async Task<int> DownloadSaltAsync(IProgress<ImportProgress> progress, CancellationToken ct)
        {
            var file = new SaltFile { Updated = DateTime.Now.ToString("s", CultureInfo.InvariantCulture) };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? next = "top/salt";
            for (int page = 0; page < SaltPages && next != null; page++)
            {
                ct.ThrowIfCancellationRequested();
                progress.Report(new ImportProgress { Step = "Downloading salt scores (EDHREC)…", Percentage = 99,
                                                     Detail = $"Page {page + 1} · {file.Cards.Count:N0} cards" });
                string? json = await FetchAsync(next, ct);
                if (json == null) break;
                double lowest = double.MaxValue;
                using (var doc = JsonDocument.Parse(json))
                {
                    var views = FindArray(doc.RootElement, "cardviews");
                    if (views == null) break;
                    foreach (var v in views.Value.EnumerateArray())
                    {
                        string name = Str(v, "name");
                        double salt = Dbl(v, "salt");
                        if (name.Length == 0) continue;
                        lowest = Math.Min(lowest, salt);
                        if (seen.Add(name)) file.Cards.Add(new SaltEntry { N = name, S = Math.Round(salt, 2) });
                    }
                    next = FindString(doc.RootElement, "more");
                    if (next != null) next = next.Trim('/').Replace(".json", "");
                }
                if (lowest < SaltFloor) break;
                await Task.Delay(250, ct);                                      // be kind to EDHREC
            }
            if (file.Cards.Count == 0) throw new InvalidOperationException("EDHREC's salt list was empty.");
            file.Lowest = file.Cards.Min(c => c.S);
            Directory.CreateDirectory(Path.GetDirectoryName(SaltPath)!);
            SafeFile.WriteAllText(SaltPath, JsonSerializer.Serialize(file));
            lock (_saltLock) _salt = null;                                       // read again on next use
            return file.Cards.Count;
        }

        /// <summary>A card's salt, or null when it isn't on the downloaded list (milder than <see cref="SaltLowest"/>) or there's no list.</summary>
        public static SaltInfo? SaltOf(string name)
        {
            var map = SaltMap();
            if (map == null || string.IsNullOrWhiteSpace(name)) return null;
            if (map.TryGetValue(name, out var s)) return s;
            if (map.TryGetValue(name.Split(" // ")[0].Trim(), out s)) return s;
            return null;
        }

        /// <summary>The lowest score on the downloaded list (0 when there's none).</summary>
        public static double SaltLowest { get { SaltMap(); return _saltLowest; } }

        private static Dictionary<string, SaltInfo>? SaltMap()
        {
            lock (_saltLock)
            {
                if (_salt != null) return _salt;
                if (!File.Exists(SaltPath)) return null;
                try
                {
                    var file = JsonSerializer.Deserialize<SaltFile>(File.ReadAllText(SaltPath));
                    var map = new Dictionary<string, SaltInfo>(StringComparer.OrdinalIgnoreCase);
                    if (file != null)
                    {
                        for (int i = 0; i < file.Cards.Count; i++)
                        {
                            var c = file.Cards[i];
                            var info = new SaltInfo(c.S, i + 1, file.Lowest);
                            map.TryAdd(c.N, info);
                            map.TryAdd(c.N.Split(" // ")[0].Trim(), info);
                        }
                        _saltLowest = file.Lowest;
                    }
                    return _salt = map;
                }
                catch { return null; }
            }
        }

        /// <summary>The first array with this name anywhere in the document (EDHREC nests the first page deeper).</summary>
        private static JsonElement? FindArray(JsonElement e, string name)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in e.EnumerateObject())
                {
                    if (p.NameEquals(name) && p.Value.ValueKind == JsonValueKind.Array) return p.Value;
                    var inner = FindArray(p.Value, name);
                    if (inner != null) return inner;
                }
            }
            else if (e.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in e.EnumerateArray())
                {
                    var inner = FindArray(item, name);
                    if (inner != null) return inner;
                }
            }
            return null;
        }

        /// <summary>The first string property with this name anywhere in the document.</summary>
        private static string? FindString(JsonElement e, string name)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in e.EnumerateObject())
                {
                    if (p.NameEquals(name) && p.Value.ValueKind == JsonValueKind.String) return p.Value.GetString();
                    if (p.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        var inner = FindString(p.Value, name);
                        if (inner != null) return inner;
                    }
                }
            }
            else if (e.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in e.EnumerateArray())
                {
                    var inner = FindString(item, name);
                    if (inner != null) return inner;
                }
            }
            return null;
        }

        // ══ A commander's recommendations ══════════════════════════════════

        /// <summary>
        /// The saved page if it's under a week old (and not <paramref name="refresh"/>),
        /// else a fresh download (saved for next time). If the download fails,
        /// an older saved copy comes back with a Note; with none, an exception
        /// whose message can be shown as is.
        /// </summary>
        public static async Task<EdhrecPage> GetCommanderAsync(IReadOnlyList<string> commanderNames, bool refresh, CancellationToken ct)
        {
            if (commanderNames.Count == 0) throw new InvalidOperationException("This deck has no commander.");

            // Partners: EDHREC's order isn't certain, so try A→Z and then the other way.
            var slugs = new List<string> { CommanderSlug(commanderNames) };
            if (commanderNames.Count > 1)
                slugs.Add(string.Join("-", commanderNames.Select(n => Slug(n)).OrderByDescending(s => s, StringComparer.Ordinal)));

            foreach (var slug in slugs)
            {
                var saved = Load(slug, note: "");
                if (saved != null && !refresh && DateTime.Now - saved.Fetched < KeepFor) return saved;
            }

            Exception? last = null;
            foreach (var slug in slugs)
            {
                try
                {
                    string? json = await FetchAsync("commanders/" + slug, ct);
                    if (json == null) continue;                                 // not found: try the other order
                    string path = Path.Combine(CacheFolder, slug + ".json");
                    SafeFile.WriteAllText(path, json);
                    return Parse(slug, json, DateTime.Now, note: "");
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) { last = ex; }
            }

            // Couldn't download: an older saved copy is better than nothing.
            foreach (var slug in slugs)
            {
                var old = Load(slug, note: $"Couldn't reach EDHREC ({last?.Message ?? "page not found"}), so this is the copy saved");
                if (old != null) return old;
            }
            if (last != null)
                throw new InvalidOperationException($"Couldn't get this commander from EDHREC: {last.Message}", last);
            throw new InvalidOperationException(
                $"EDHREC has no page for {string.Join(" + ", commanderNames)} (looked for \"{slugs[0]}\").");
        }

        /// <summary>A saved page, or null.</summary>
        private static EdhrecPage? Load(string slug, string note)
        {
            string path = Path.Combine(CacheFolder, slug + ".json");
            if (!File.Exists(path)) return null;
            try
            {
                var when = File.GetLastWriteTime(path);
                return Parse(slug, File.ReadAllText(path), when,
                             note.Length > 0 ? $"{note} {when:d}." : "");
            }
            catch { return null; }
        }

        /// <summary>One JSON page; null when EDHREC has no such page. Follows EDHREC's own redirects once.</summary>
        private static async Task<string?> FetchAsync(string path, CancellationToken ct, int hops = 0)
        {
            using var response = await _http.Value.GetAsync(JsonBase + path + ".json", ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync(ct);

            // A renamed page answers { "redirect": "/commanders/new-name" }.
            using (var doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("redirect", out var to) && to.ValueKind == JsonValueKind.String)
                {
                    if (hops > 0) return null;
                    string next = to.GetString()!.Trim('/');
                    return await FetchAsync(next, ct, hops + 1);
                }
            }
            return json;
        }

        /// <summary>
        /// Read the card lists from a commander page:
        /// container.json_dict.cardlists[] → { header, cardviews[] → { name, inclusion,
        /// num_decks, potential_decks, synergy } }. Missing parts are skipped.
        /// </summary>
        internal static EdhrecPage Parse(string slug, string json, DateTime fetched, string note)
        {
            using var doc = JsonDocument.Parse(json);
            var cards = new List<EdhrecCard>();
            var sections = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int maxPotential = 0;

            if (TryPath(doc.RootElement, out var lists, "container", "json_dict", "cardlists") &&
                lists.ValueKind == JsonValueKind.Array)
            {
                foreach (var list in lists.EnumerateArray())
                {
                    string header = Str(list, "header");
                    if (!list.TryGetProperty("cardviews", out var views) || views.ValueKind != JsonValueKind.Array) continue;
                    bool any = false;
                    foreach (var v in views.EnumerateArray())
                    {
                        string name = Str(v, "name");
                        if (name.Length == 0) continue;
                        int potential = Int(v, "potential_decks");
                        maxPotential = Math.Max(maxPotential, potential);
                        if (!seen.Add(name)) continue;                          // listed once, in its first section
                        int decks = Int(v, "inclusion");
                        if (decks == 0) decks = Int(v, "num_decks");
                        cards.Add(new EdhrecCard
                        {
                            Name = name,
                            Section = header,
                            Decks = decks,
                            PotentialDecks = potential,
                            Synergy = Dbl(v, "synergy"),
                        });
                        any = true;
                    }
                    if (any && header.Length > 0 && !sections.Contains(header)) sections.Add(header);
                }
            }

            int deckCount = 0;
            if (TryPath(doc.RootElement, out var cmd, "container", "json_dict", "card")) deckCount = Int(cmd, "num_decks");
            if (deckCount == 0) deckCount = maxPotential;

            return new EdhrecPage
            {
                Slug = slug, Fetched = fetched, Decks = deckCount,
                Cards = cards, Sections = sections, Note = note,
            };
        }

        // ── JSON helpers (EDHREC's numbers can arrive as numbers or strings) ──
        private static bool TryPath(JsonElement e, out JsonElement found, params string[] path)
        {
            found = e;
            foreach (var p in path)
            {
                if (found.ValueKind != JsonValueKind.Object || !found.TryGetProperty(p, out found)) return false;
            }
            return true;
        }

        private static string Str(JsonElement e, string p) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? "" : "";

        private static int Int(JsonElement e, string p)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(p, out var v)) return 0;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return (int)Math.Round(d);
            if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                return (int)Math.Round(d);
            return 0;
        }

        private static double Dbl(JsonElement e, string p)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(p, out var v)) return 0;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
            if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                return d;
            return 0;
        }
    }

    /// <summary>Web pages for a card: EDHREC, Scryfall and Commander Spellbook.</summary>
    public static class CardLinks
    {
        public static string Edhrec(string name, string layout = "") => EdhrecService.CardUrl(name, layout);

        /// <summary>The printing's Scryfall page when the set and number are known, else a search for the name.</summary>
        public static string Scryfall(string name, string setCode = "", string collectorNumber = "")
        {
            if (setCode.Length > 0 && collectorNumber.Length > 0)
                return $"https://scryfall.com/card/{Uri.EscapeDataString(setCode.ToLowerInvariant())}/{Uri.EscapeDataString(collectorNumber)}";
            return "https://scryfall.com/search?q=" + Uri.EscapeDataString($"!\"{name}\"");
        }

        /// <summary>Commander Spellbook's combos with this card.</summary>
        public static string Spellbook(string name) =>
            "https://commanderspellbook.com/search/?q=" + Uri.EscapeDataString($"card:\"{name.Split(" // ")[0]}\"");

        /// <summary>Open a web page in the default browser. Never throws.</summary>
        public static void Open(string url)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Open {url}: {ex.Message}"); }
        }
    }
}
