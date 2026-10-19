using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace BreakersOfE.Services
{
    /// <summary>One oracle tag on a card ("ramp", "removal", "card-advantage" …).</summary>
    public sealed record CardTag(string Slug, string Label, string Description);

    /// <summary>One combo (a Commander Spellbook "variant").</summary>
    public sealed class Combo
    {
        public string Id { get; init; } = "";
        /// <summary>The combo's colors ("WUB", "C" for colorless).</summary>
        public string Identity { get; init; } = "";
        /// <summary>Card names, in Spellbook's order.</summary>
        public List<string> Cards { get; init; } = new();
        /// <summary>Other requirements ("A creature with power 3 or greater").</summary>
        public List<string> Requires { get; init; } = new();
        /// <summary>What it does ("Infinite colorless mana", "Win the game").</summary>
        public List<string> Results { get; init; } = new();
        public string ManaNeeded { get; init; } = "";
        public string Prerequisites { get; init; } = "";
        public string Steps { get; init; } = "";
        /// <summary>Decks on EDHREC that run it (Spellbook's popularity), 0 when unknown.</summary>
        public int Popularity { get; init; }
        /// <summary>Spellbook's bracket tag: R Ruthless, S Spicy, P Powerful, O Oddball, C Core, E Exhibition, B Banned.</summary>
        public string BracketTag { get; init; } = "";
        public bool CommanderLegal { get; init; }
        /// <summary>The combo's page on commanderspellbook.com.</summary>
        public string Url => $"https://commanderspellbook.com/combo/{Id}/";
    }

    /// <summary>
    /// Deck-building data downloaded with the full Database Update, each in a
    /// file of its own (like rulings.db), swapped in only when complete:
    /// <list type="bullet">
    /// <item><b>tags.db</b> — Scryfall's oracle tags (Tagger): what each card does
    /// (ramp, removal, card draw …), joined to cards by oracle id.</item>
    /// <item><b>combos.db</b> — Commander Spellbook's combos, from its bulk file
    /// (the site asks for the bulk file, not page-by-page API calls).</item>
    /// </list>
    /// </summary>
    public static class DeckDataService
    {
        public static string TagsPath => AppFolderService.TagsDatabasePath;
        public static string CombosPath => AppFolderService.CombosDatabasePath;

        private const string ScryfallBulkList = "https://api.scryfall.com/bulk-data";
        private const string SpellbookBulk = "https://json.commanderspellbook.com/variants.json.gz";

        private static readonly Lazy<HttpClient> _http = new(() =>
        {
            var h = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            // Both sites ask callers to name themselves.
            h.DefaultRequestHeaders.Add("User-Agent", "BreakersOfE/2.1 (+https://github.com/georgebrun/repos)");
            h.DefaultRequestHeaders.Add("Accept", "application/json;q=0.9,*/*;q=0.8");
            return h;
        });

        // ══ Scryfall oracle tags ═══════════════════════════════════════════

        /// <summary>Download Scryfall's oracle tags into tags.db. Returns (tags, card links).</summary>
        public static async Task<(int Tags, int Links)> DownloadTagsAsync(IProgress<ImportProgress> progress, CancellationToken ct)
        {
            progress.Report(new ImportProgress { Percentage = 5, Step = "Card tags: finding Scryfall's file…" });
            string url = await FindScryfallBulkUrlAsync("oracle_tags", ct)
                         ?? throw new InvalidOperationException("Scryfall doesn't list an oracle tags file right now.");

            string temp = Path.Combine(Path.GetTempPath(), "boe_oracle_tags.download");
            try
            {
                progress.Report(new ImportProgress { Percentage = 15, Step = "Card tags: downloading…" });
                await DownloadToFileAsync(url, temp, ct);

                progress.Report(new ImportProgress { Percentage = 40, Step = "Card tags: saving…" });
                var stage = DatabaseStaging.Begin(TagsPath);
                int tags = 0, links = 0;
                try
                {
                    using (var con = Open(stage.WorkingPath))
                    {
                        Exec(con, @"
                            CREATE TABLE IF NOT EXISTS Tags (Slug TEXT PRIMARY KEY, Label TEXT NOT NULL, Description TEXT NOT NULL);
                            CREATE TABLE IF NOT EXISTS CardTags (OracleId TEXT NOT NULL, Slug TEXT NOT NULL);
                            CREATE TABLE IF NOT EXISTS Info (Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
                            DELETE FROM Tags; DELETE FROM CardTags;
                            DROP INDEX IF EXISTS IX_CardTags_Oracle; DROP INDEX IF EXISTS IX_CardTags_Slug;");
                        using var tx = con.BeginTransaction();
                        using var addTag = Prepare(con, tx, "INSERT OR REPLACE INTO Tags (Slug, Label, Description) VALUES ($a, $b, $c)", 3);
                        using var addLink = Prepare(con, tx, "INSERT INTO CardTags (OracleId, Slug) VALUES ($a, $b)", 2);

                        using var reader = new StreamReader(OpenMaybeGzip(temp), Encoding.UTF8);
                        string? line;
                        while ((line = await reader.ReadLineAsync(ct)) != null)
                        {
                            if (line.Length == 0) continue;
                            using var doc = JsonDocument.Parse(line);
                            var t = doc.RootElement;
                            if (t.ValueKind != JsonValueKind.Object || !t.TryGetProperty("taggings", out var taggings)) continue;
                            string slug = Str(t, "slug");
                            if (slug.Length == 0) continue;
                            Run(addTag, slug, Str(t, "label") is { Length: > 0 } l ? l : slug, Str(t, "description"));
                            tags++;
                            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var g in taggings.EnumerateArray())
                            {
                                string oid = Str(g, "oracle_id");
                                if (oid.Length == 0 || !seen.Add(oid)) continue;
                                Run(addLink, oid, slug);
                                links++;
                            }
                            if (tags % 200 == 0)
                                progress.Report(new ImportProgress
                                { Percentage = 40 + Math.Min(50, tags / 100), Step = "Card tags: saving…", Detail = $"{tags:N0} tags" });
                        }
                        if (tags == 0) throw new InvalidOperationException("The tags file was empty. Your card tags were not changed.");
                        SetInfo(con, tx, "updated", DateTime.Now.ToString("s"));
                        SetInfo(con, tx, "tags", tags.ToString());
                        tx.Commit();
                        Exec(con, "CREATE INDEX IX_CardTags_Oracle ON CardTags (OracleId); CREATE INDEX IX_CardTags_Slug ON CardTags (Slug);");
                    }
                    ct.ThrowIfCancellationRequested();
                    stage.Commit();
                }
                finally { stage.Abort(); }
                return (tags, links);
            }
            finally { TryDelete(temp); }
        }

        // ══ Commander Spellbook combos ═════════════════════════════════════

        /// <summary>Download Commander Spellbook's combos into combos.db. Returns the number of combos.</summary>
        public static async Task<int> DownloadCombosAsync(IProgress<ImportProgress> progress, CancellationToken ct)
        {
            string temp = Path.Combine(Path.GetTempPath(), "boe_spellbook_variants.json.gz");
            try
            {
                progress.Report(new ImportProgress { Percentage = 5, Step = "Combos: downloading from Commander Spellbook…" });
                await DownloadToFileAsync(SpellbookBulk, temp, ct);

                progress.Report(new ImportProgress { Percentage = 40, Step = "Combos: saving…" });
                var stage = DatabaseStaging.Begin(CombosPath);
                int combos = 0;
                try
                {
                    using (var con = Open(stage.WorkingPath))
                    {
                        Exec(con, @"
                            CREATE TABLE IF NOT EXISTS Combos (
                                Id TEXT PRIMARY KEY, Identity TEXT NOT NULL, ManaNeeded TEXT NOT NULL,
                                Results TEXT NOT NULL, Requires TEXT NOT NULL, Prerequisites TEXT NOT NULL,
                                Steps TEXT NOT NULL, Popularity INTEGER NOT NULL, BracketTag TEXT NOT NULL,
                                CommanderLegal INTEGER NOT NULL, CardCount INTEGER NOT NULL);
                            CREATE TABLE IF NOT EXISTS ComboCards (
                                ComboId TEXT NOT NULL, OracleId TEXT NOT NULL, Name TEXT NOT NULL,
                                FrontName TEXT NOT NULL COLLATE NOCASE, Quantity INTEGER NOT NULL, MustBeCommander INTEGER NOT NULL);
                            CREATE TABLE IF NOT EXISTS Info (Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
                            DELETE FROM Combos; DELETE FROM ComboCards;
                            DROP INDEX IF EXISTS IX_ComboCards_Oracle; DROP INDEX IF EXISTS IX_ComboCards_Front; DROP INDEX IF EXISTS IX_ComboCards_Combo;");
                        using var tx = con.BeginTransaction();
                        using var addCombo = Prepare(con, tx, @"INSERT OR REPLACE INTO Combos
                            (Id, Identity, ManaNeeded, Results, Requires, Prerequisites, Steps, Popularity, BracketTag, CommanderLegal, CardCount)
                            VALUES ($a, $b, $c, $d, $e, $f, $g, $h, $i, $j, $k)", 11);
                        using var addCard = Prepare(con, tx,
                            "INSERT INTO ComboCards (ComboId, OracleId, Name, FrontName, Quantity, MustBeCommander) VALUES ($a, $b, $c, $d, $e, $f)", 6);

                        using var input = OpenMaybeGzip(temp);
                        await JsonArrayStream.ReadArrayAsync(input, "variants", v =>
                        {
                            if (!SaveVariant(v, addCombo, addCard)) return;
                            combos++;
                            if (combos % 2000 == 0)
                                progress.Report(new ImportProgress
                                { Percentage = 40 + Math.Min(50, combos / 2000), Step = "Combos: saving…", Detail = $"{combos:N0} combos" });
                        }, ct);

                        if (combos == 0) throw new InvalidOperationException("The combo file had no combos. Your combos were not changed.");
                        SetInfo(con, tx, "updated", DateTime.Now.ToString("s"));
                        SetInfo(con, tx, "combos", combos.ToString());
                        tx.Commit();
                        Exec(con, @"CREATE INDEX IX_ComboCards_Oracle ON ComboCards (OracleId);
                                    CREATE INDEX IX_ComboCards_Front ON ComboCards (FrontName);
                                    CREATE INDEX IX_ComboCards_Combo ON ComboCards (ComboId);");
                    }
                    ct.ThrowIfCancellationRequested();
                    stage.Commit();
                }
                finally { stage.Abort(); }
                return combos;
            }
            finally { TryDelete(temp); }
        }

        /// <summary>One variant → Combos + ComboCards. False when it isn't a published combo.</summary>
        private static bool SaveVariant(JsonElement v, SqliteCommand addCombo, SqliteCommand addCard)
        {
            string id = Str(v, "id");
            if (id.Length == 0) return false;
            string status = Str(v, "status");
            if (status.Length > 0 && status != "OK" && status != "E") return false;   // drafts, not-working …

            var cards = new List<(string Oid, string Name, int Qty, bool Cmdr)>();
            if (v.TryGetProperty("uses", out var uses) && uses.ValueKind == JsonValueKind.Array)
                foreach (var u in uses.EnumerateArray())
                {
                    if (!u.TryGetProperty("card", out var card)) continue;
                    string name = Str(card, "name");
                    if (name.Length == 0) continue;
                    cards.Add((Str(card, "oracleId"), name, Int(u, "quantity", 1), Bool(u, "mustBeCommander")));
                }
            if (cards.Count == 0) return false;

            var requires = Names(v, "requires", "template");
            var results = Names(v, "produces", "feature");
            string prereq = string.Join("\n", new[] { Str(v, "easyPrerequisites"), Str(v, "notablePrerequisites") }
                                                .Where(s => s.Length > 0));
            bool cmdrLegal = v.TryGetProperty("legalities", out var leg) && Bool(leg, "commander");

            Run(addCombo, id, Str(v, "identity"), Str(v, "manaNeeded"), string.Join("|", results), string.Join("|", requires),
                prereq, Str(v, "description"), Int(v, "popularity", 0), Str(v, "bracketTag"), cmdrLegal ? 1 : 0, cards.Count);
            foreach (var c in cards)
                Run(addCard, id, c.Oid, c.Name, FrontOf(c.Name), c.Qty, c.Cmdr ? 1 : 0);
            return true;
        }

        /// <summary>"Fire // Ice" → "Fire" (how deck lists and BoE match two-part cards).</summary>
        private static string FrontOf(string name) => name.Split(" // ")[0].Trim();

        /// <summary>A card's front-face name ("Delver of Secrets // Insectile Aberration" → "Delver of Secrets").</summary>
        public static string FrontName(string name) => FrontOf(name ?? "");

        /// <summary>True once the Database Update has downloaded the card tags.</summary>
        public static bool HasTags => File.Exists(TagsPath);

        /// <summary>True once the Database Update has downloaded the combos.</summary>
        public static bool HasCombos => File.Exists(CombosPath);

        /// <summary>Commander Spellbook's name for a bracket tag letter.</summary>
        public static string BracketTagName(string tag) => tag switch
        {
            "R" => "Ruthless",
            "S" => "Spicy",
            "P" => "Powerful",
            "O" => "Oddball",
            "C" => "Core",
            "E" => "Exhibition",
            "B" => "Banned",
            _ => "",
        };

        /// <summary>True when the combo makes something infinite or wins / ends the game.</summary>
        public static bool IsInfiniteOrWin(Combo combo) =>
            combo.Results.Any(r => r.Contains("infinite", StringComparison.OrdinalIgnoreCase) ||
                                   r.Contains("win the game", StringComparison.OrdinalIgnoreCase) ||
                                   r.Contains("lose the game", StringComparison.OrdinalIgnoreCase) ||
                                   r.Contains("loses the game", StringComparison.OrdinalIgnoreCase));

        private static List<string> Names(JsonElement v, string list, string inner)
        {
            var names = new List<string>();
            if (v.TryGetProperty(list, out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var item in arr.EnumerateArray())
                    if (item.TryGetProperty(inner, out var o) && Str(o, "name") is { Length: > 0 } n) names.Add(n);
            return names;
        }

        // ══ Reading (for the card details, Statistics and the builder) ═════

        /// <summary>A card's tags, A→Z by label (empty when tags.db isn't downloaded yet).</summary>
        public static List<CardTag> TagsFor(string oracleId)
        {
            var list = new List<CardTag>();
            if (string.IsNullOrEmpty(oracleId) || !File.Exists(TagsPath)) return list;
            try
            {
                using var con = OpenRead(TagsPath);
                using var cmd = con.CreateCommand();
                cmd.CommandText = @"SELECT t.Slug, t.Label, t.Description FROM CardTags c JOIN Tags t ON t.Slug = c.Slug
                                    WHERE c.OracleId = $o ORDER BY t.Label COLLATE NOCASE";
                cmd.Parameters.AddWithValue("$o", oracleId);
                using var r = cmd.ExecuteReader();
                while (r.Read()) list.Add(new CardTag(r.GetString(0), r.GetString(1), r.GetString(2)));
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Tags: {ex.Message}"); }
            return list;
        }

        /// <summary>
        /// Every combo whose cards are all in <paramref name="cardNames"/>
        /// (front-face names are fine), most popular first. Requirements like
        /// "a creature" aren't checked; they're shown with the combo.
        /// </summary>
        public static List<Combo> CombosWithin(IEnumerable<string> cardNames) => CombosNear(cardNames, 0);

        /// <summary>
        /// Combos missing at most <paramref name="missing"/> of their cards from
        /// <paramref name="cardNames"/> (0 = complete), with at least one card in the list.
        /// </summary>
        public static List<Combo> CombosNear(IEnumerable<string> cardNames, int missing)
        {
            var result = new List<Combo>();
            if (!File.Exists(CombosPath)) return result;
            var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in cardNames)
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                have.Add(FrontOf(n));
            }
            if (have.Count == 0) return result;
            try
            {
                using var con = OpenRead(CombosPath);
                Exec(con, "CREATE TEMP TABLE IF NOT EXISTS Have (Name TEXT PRIMARY KEY COLLATE NOCASE); DELETE FROM Have;");
                using (var tx = con.BeginTransaction())
                using (var add = Prepare(con, tx, "INSERT OR IGNORE INTO Have (Name) VALUES ($a)", 1))
                {
                    foreach (var n in have) Run(add, n);
                    tx.Commit();
                }
                using var cmd = con.CreateCommand();
                cmd.CommandText = @"
                    SELECT c.Id, c.Identity, c.ManaNeeded, c.Results, c.Requires, c.Prerequisites, c.Steps,
                           c.Popularity, c.BracketTag, c.CommanderLegal,
                           (SELECT group_concat(cc.Name, '|') FROM ComboCards cc WHERE cc.ComboId = c.Id) AS Names
                    FROM Combos c
                    JOIN (SELECT cc.ComboId,
                                 SUM(CASE WHEN cc.FrontName IN (SELECT Name FROM Have) THEN 1 ELSE 0 END) AS Got,
                                 COUNT(*) AS Total
                          FROM ComboCards cc
                          WHERE cc.ComboId IN (SELECT x.ComboId FROM ComboCards x WHERE x.FrontName IN (SELECT Name FROM Have))
                          GROUP BY cc.ComboId) m ON m.ComboId = c.Id
                    WHERE m.Total - m.Got <= $miss AND m.Got > 0
                    ORDER BY (m.Total - m.Got), c.Popularity DESC";
                cmd.Parameters.AddWithValue("$miss", Math.Max(0, missing));
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    result.Add(new Combo
                    {
                        Id = r.GetString(0),
                        Identity = r.GetString(1),
                        ManaNeeded = r.GetString(2),
                        Results = Split(r.GetString(3)),
                        Requires = Split(r.GetString(4)),
                        Prerequisites = r.GetString(5),
                        Steps = r.GetString(6),
                        Popularity = r.GetInt32(7),
                        BracketTag = r.GetString(8),
                        CommanderLegal = r.GetInt32(9) != 0,
                        Cards = Split(r.IsDBNull(10) ? "" : r.GetString(10)),
                    });
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Combos: {ex.Message}"); }
            return result;
        }

        /// <summary>When each file was last downloaded and how much it holds ("" / 0 when never).</summary>
        public static (string Updated, int Count) InfoOf(string path, string countKey)
        {
            if (!File.Exists(path)) return ("", 0);
            try
            {
                using var con = OpenRead(path);
                using var cmd = con.CreateCommand();
                cmd.CommandText = "SELECT Key, Value FROM Info WHERE Key IN ('updated', $k)";
                cmd.Parameters.AddWithValue("$k", countKey);
                string updated = ""; int count = 0;
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    if (r.GetString(0) == "updated") updated = r.GetString(1);
                    else int.TryParse(r.GetString(1), out count);
                }
                return (updated, count);
            }
            catch { return ("", 0); }
        }

        // ══ Helpers ═════════════════════════════════════════════════════════

        private static async Task<string?> FindScryfallBulkUrlAsync(string type, CancellationToken ct)
        {
            string json = await _http.Value.GetStringAsync(ScryfallBulkList, ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return null;
            foreach (var item in data.EnumerateArray())
            {
                if (Str(item, "type") != type) continue;
                string url = Str(item, "jsonl_download_uri");
                return url.Length > 0 ? url : Str(item, "download_uri");
            }
            return null;
        }

        private static async Task DownloadToFileAsync(string url, string path, CancellationToken ct)
        {
            using var resp = await _http.Value.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true);
            await src.CopyToAsync(dst, ct);
        }

        /// <summary>The file, unzipped on the fly when it starts with the gzip mark.</summary>
        private static Stream OpenMaybeGzip(string path)
        {
            var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            int b1 = fs.ReadByte(), b2 = fs.ReadByte();
            fs.Position = 0;
            return b1 == 0x1f && b2 == 0x8b ? new GZipStream(fs, CompressionMode.Decompress) : fs;
        }

        private static SqliteConnection Open(string path)
        {
            var con = new SqliteConnection($"Data Source={path};Pooling=False");
            con.Open();
            Exec(con, "PRAGMA journal_mode = DELETE; PRAGMA synchronous = OFF;");
            return con;
        }

        private static SqliteConnection OpenRead(string path)
        {
            // No pooling: a pooled connection keeps the file open, and the next
            // Database Update couldn't swap in the new file.
            var con = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
            con.Open();
            return con;
        }

        private static void Exec(SqliteConnection con, string sql)
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        /// <summary>A prepared INSERT with parameters $a, $b, $c …</summary>
        private static SqliteCommand Prepare(SqliteConnection con, SqliteTransaction tx, string sql, int parameters)
        {
            var cmd = con.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            for (int i = 0; i < parameters; i++) cmd.Parameters.Add(new SqliteParameter("$" + (char)('a' + i), null));
            cmd.Prepare();
            return cmd;
        }

        private static void Run(SqliteCommand cmd, params object?[] values)
        {
            for (int i = 0; i < values.Length; i++) cmd.Parameters[i].Value = values[i] ?? DBNull.Value;
            cmd.ExecuteNonQuery();
        }

        private static void SetInfo(SqliteConnection con, SqliteTransaction tx, string key, string value)
        {
            using var cmd = Prepare(con, tx, "INSERT OR REPLACE INTO Info (Key, Value) VALUES ($a, $b)", 2);
            Run(cmd, key, value);
        }

        private static string Str(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString() ?? "" : "";

        private static int Int(JsonElement e, string name, int fallback) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number
            && p.TryGetInt32(out int n) ? n : fallback;

        private static bool Bool(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

        private static List<string> Split(string joined) =>
            joined.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    /// <summary>
    /// Reads the items of one top-level array property ("variants") from a
    /// large JSON document one item at a time, without loading the whole file.
    /// </summary>
    internal static class JsonArrayStream
    {
        public static async Task ReadArrayAsync(Stream input, string property, Action<JsonElement> onItem, CancellationToken ct)
        {
            byte[] buffer = new byte[1 << 20];
            int length = 0;
            bool final = false;
            var walk = new Walk { State = new JsonReaderState(new JsonReaderOptions { MaxDepth = 256 }) };
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (!final)
                {
                    if (length == buffer.Length) Array.Resize(ref buffer, buffer.Length * 2);   // one item bigger than the buffer
                    int n = await input.ReadAsync(buffer.AsMemory(length), ct);
                    if (n == 0) final = true; else length += n;
                }
                int consumed = Process(buffer.AsSpan(0, length), final, property, onItem, ref walk);
                Buffer.BlockCopy(buffer, consumed, buffer, 0, length - consumed);
                length -= consumed;
                if (final) return;
            }
        }

        private struct Walk
        {
            public JsonReaderState State;
            public string? LastProperty;
            public bool InArray;
        }

        /// <summary>Read as far as the data goes; returns the bytes fully used.</summary>
        private static int Process(ReadOnlySpan<byte> data, bool final, string property, Action<JsonElement> onItem, ref Walk walk)
        {
            var reader = new Utf8JsonReader(data, final, walk.State);
            while (true)
            {
                var before = reader;                                       // to step back when an item isn't all here yet
                if (!reader.Read())
                {
                    walk.State = reader.CurrentState;
                    return (int)reader.BytesConsumed;
                }
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName when reader.CurrentDepth == 1:
                        walk.LastProperty = reader.GetString();
                        break;
                    case JsonTokenType.StartArray when reader.CurrentDepth == 1:
                        walk.InArray = walk.LastProperty == property;
                        break;
                    case JsonTokenType.EndArray when reader.CurrentDepth == 1:
                        walk.InArray = false;
                        break;
                    case JsonTokenType.StartObject when walk.InArray && reader.CurrentDepth == 2:
                        var probe = reader;
                        if (!probe.TrySkip())
                        {
                            walk.State = before.CurrentState;               // come back to this item with more data
                            return (int)before.BytesConsumed;
                        }
                        using (var doc = JsonDocument.ParseValue(ref reader)) onItem(doc.RootElement);
                        break;
                }
            }
        }
    }
}
