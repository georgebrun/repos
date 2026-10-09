using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BreakersOfE.Services
{
    /// <summary>One keyword's entry in the Comprehensive Rules (701.x keyword actions, 702.x keyword abilities).</summary>
    public sealed class RulesEntry
    {
        /// <summary>"702.36"</summary>
        public string Number { get; init; } = "";
        /// <summary>"Horsemanship"</summary>
        public string Title { get; init; } = "";
        /// <summary>True for 701.x (keyword actions), false for 702.x (keyword abilities).</summary>
        public bool IsAction { get; init; }
        /// <summary>The rule's sub-rules, one paragraph each ("702.36a Horsemanship is an evasion ability." …).</summary>
        public List<string> Paragraphs { get; } = new();
        public string Text => string.Join("\n\n", Paragraphs);
    }

    /// <summary>
    /// Wizards' Comprehensive Rules — the official text behind every keyword.
    /// Downloaded during Update Database (or from the Keyword Dictionary page)
    /// to Documents\Breakers of E\comprehensive-rules.txt and read from there.
    /// Wizards renames the file with every rules update, so the current link
    /// is read from their rules page. Anything failing leaves the last good
    /// copy in place (or none: the dictionary then uses its own definitions).
    /// </summary>
    public static class ComprehensiveRulesService
    {
        public const string RulesPageUrl = "https://magic.wizards.com/en/rules";

        public static string RulesPath => Path.Combine(AppFolderService.RootFolder, "comprehensive-rules.txt");

        private static readonly object _lock = new();
        private static Dictionary<string, RulesEntry>? _entries;
        private static string _effective = "";

        public static bool IsDownloaded => File.Exists(RulesPath);

        /// <summary>"September 25, 2026" (the rules' own "effective as of" date), or "".</summary>
        public static string EffectiveDate => Load().Effective;

        /// <summary>Every 701/702 entry, by title (case ignored). Empty when not downloaded.</summary>
        public static IReadOnlyDictionary<string, RulesEntry> Entries => Load().Entries;

        /// <summary>Forget the parsed copy (after a new download).</summary>
        public static void Reset()
        {
            lock (_lock) { _entries = null; _effective = ""; }
        }

        /// <summary>The parsed rules (read from the file the first time), taken together inside the lock.</summary>
        private static (Dictionary<string, RulesEntry> Entries, string Effective) Load()
        {
            lock (_lock)
            {
                if (_entries != null) return (_entries, _effective);
                _entries = new Dictionary<string, RulesEntry>(StringComparer.OrdinalIgnoreCase);
                _effective = "";
                try
                {
                    if (File.Exists(RulesPath))
                    {
                        var (entries, effective) = Parse(File.ReadAllText(RulesPath, Encoding.UTF8));
                        _entries = entries;
                        _effective = effective;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Comprehensive Rules: {ex.Message}");
                }
                return (_entries, _effective);
            }
        }

        // ── Find a keyword's rule ────────────────────────────────────────
        /// <summary>
        /// The rule for a keyword name: exact title, else the rule its name
        /// belongs to ("Islandwalk" → Landwalk, "Plainscycling" → Cycling,
        /// "Commander ninjutsu" → Ninjutsu, "Partner with" → Partner, "Hexproof from" → Hexproof).
        /// </summary>
        public static RulesEntry? Find(string keyword)
        {
            var all = Entries;
            if (all.Count == 0 || string.IsNullOrWhiteSpace(keyword)) return null;
            string k = keyword.Trim();
            if (all.TryGetValue(k, out var exact)) return exact;
            if (k.EndsWith("walk", StringComparison.OrdinalIgnoreCase) && all.TryGetValue("Landwalk", out var walk)) return walk;
            // "Partner with" → Partner, "Hexproof from" → Hexproof, "Protection from" → Protection.
            foreach (string tail in new[] { " with", " from" })
                if (k.EndsWith(tail, StringComparison.OrdinalIgnoreCase) && all.TryGetValue(k[..^tail.Length], out var head)) return head;
            // "Commander ninjutsu" → Ninjutsu, "Plainscycling" → Cycling: a title the name ends with.
            return all.Values.Where(e => e.Title.Length >= 5 && k.EndsWith(e.Title, StringComparison.OrdinalIgnoreCase))
                             .OrderByDescending(e => e.Title.Length).FirstOrDefault();
        }

        // ── Parse ────────────────────────────────────────────────────────
        private static readonly Regex TitleRx = new(@"^(?<sec>70[12])\.(?<n>\d+)\.\s+(?<t>.+)$", RegexOptions.Compiled);
        private static readonly Regex SubRx = new(@"^(?<sec>70[12])\.(?<n>\d+)(?<l>[a-z]+)\s+(?<t>.+)$", RegexOptions.Compiled);
        private static readonly Regex AnyRuleRx = new(@"^\d{1,3}\.(\d+[a-z]*\.?)?\s", RegexOptions.Compiled);   // "703.1.", "702.5a", or a heading "703. …"
        private static readonly Regex EffectiveRx = new(@"effective as of (?<d>[A-Z][a-z]+ \d{1,2}, \d{4})", RegexOptions.Compiled);

        /// <summary>The keyword entries (701.x and 702.x) of a Comprehensive Rules text file.</summary>
        public static (Dictionary<string, RulesEntry> Entries, string Effective) Parse(string text)
        {
            var entries = new Dictionary<string, RulesEntry>(StringComparer.OrdinalIgnoreCase);
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            var eff = EffectiveRx.Match(text);
            string effective = eff.Success ? eff.Groups["d"].Value : "";

            RulesEntry? current = null;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;

                var t = TitleRx.Match(line);
                if (t.Success)
                {
                    current = null;
                    string title = t.Groups["t"].Value.Trim();
                    // "702.1. Keyword abilities are …" is a general rule, not a keyword.
                    if (int.Parse(t.Groups["n"].Value) >= 2 && title.Length <= 60 && !title.EndsWith("."))
                    {
                        current = new RulesEntry
                        {
                            Number = $"{t.Groups["sec"].Value}.{t.Groups["n"].Value}",
                            Title = title,
                            IsAction = t.Groups["sec"].Value == "701",
                        };
                        entries.TryAdd(title, current);       // the first (the rules body; the contents list has no 701.x / 702.x lines)
                    }
                    continue;
                }
                if (current == null) continue;

                var s = SubRx.Match(line);
                if (s.Success && $"{s.Groups["sec"].Value}.{s.Groups["n"].Value}" == current.Number)
                {
                    current.Paragraphs.Add(line);
                    continue;
                }
                if (AnyRuleRx.IsMatch(line) || line.Equals("Glossary", StringComparison.OrdinalIgnoreCase))
                {
                    current = null;                          // the next rule (or the glossary) begins
                    continue;
                }
                // "Example: …" lines belong to the sub-rule above them.
                if (current.Paragraphs.Count > 0) current.Paragraphs[^1] += "\n" + line;
            }
            return (entries, effective);
        }

        // ── Download ─────────────────────────────────────────────────────
        private static readonly Regex TxtLinkRx = new(
            @"https://media\.wizards\.com/[^""'<>]*?MagicCompRules[^""'<>]*?\.txt", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Find the current rules file on Wizards' rules page and save it.
        /// Returns the rules' effective date. Throws when it can't (the old
        /// copy, if any, is kept).
        /// </summary>
        public static async Task<string> DownloadAsync(HttpClient http, CancellationToken ct = default)
        {
            string page = await http.GetStringAsync(RulesPageUrl, ct);
            var m = TxtLinkRx.Match(page);
            if (!m.Success) throw new InvalidOperationException("Wizards' rules page has no rules text file link (the page may have changed).");
            string url = System.Net.WebUtility.HtmlDecode(m.Value).Replace(" ", "%20");

            byte[] bytes = await http.GetByteArrayAsync(url, ct);
            string text;
            try { text = new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException)
            {
                // Some years' files were Windows-1252 (curly quotes as single bytes).
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                text = Encoding.GetEncoding(1252).GetString(bytes);
            }
            text = text.TrimStart('\uFEFF');

            var (entries, effective) = Parse(text);
            if (entries.Count < 100)
                throw new InvalidOperationException($"The rules file didn't look right ({entries.Count} keyword rules found) — kept the old copy.");

            string tmp = RulesPath + ".download";
            await File.WriteAllTextAsync(tmp, text, new UTF8Encoding(false), ct);
            File.Move(tmp, RulesPath, overwrite: true);
            Reset();
            return effective;
        }

        private static HttpClient? _ownHttp;

        /// <summary>Download with BoE's own web client (Update Database, and the Keyword Dictionary page's button).</summary>
        public static Task<string> DownloadAsync(CancellationToken ct = default)
        {
            _ownHttp ??= CreateClient();
            return DownloadAsync(_ownHttp, ct);
        }

        private static HttpClient CreateClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            // A browser-style name: Wizards' site may turn away unknown programs.
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) BreakersOfE/2.0");
            return http;
        }
    }
}
