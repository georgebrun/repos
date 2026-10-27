using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BreakersOfE.Services
{
    /// <summary>
    /// Which set belongs to which release, from Scryfall's list of sets (saved
    /// by Update Database as sets.json in the data folder). "Forgotten Realms
    /// Commander Display Commanders" → "Forgotten Realms Commander" →
    /// "Adventures in the Forgotten Realms": that last one is the release.
    /// No file yet (before the first update with it): every set is its own
    /// release, so the Sets page looks as it always did.
    /// </summary>
    public static class SetFamilies
    {
        /// <summary>One set as Scryfall lists it.</summary>
        public sealed record SetInfo(string Code, string Name, string SetType, string Parent, string ReleasedAt);

        private static Dictionary<string, SetInfo>? _sets;
        private static DateTime _loadedStamp;

        /// <summary>Documents\Breakers of E\sets.json.</summary>
        public static string ListPath => Path.Combine(AppFolderService.RootFolder, "sets.json");

        /// <summary>Is the family list there (an update has saved it)?</summary>
        public static bool Available => Sets().Count > 0;

        /// <summary>Save Scryfall's set list (the raw /sets answer). Bad or empty text is ignored.</summary>
        public static void Save(string json)
        {
            try
            {
                using (var doc = JsonDocument.Parse(json))
                    if (!doc.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0) return;
                string tmp = ListPath + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, ListPath, overwrite: true);
                _sets = null;
            }
            catch
            {
                /* the old list (if any) stays */
            }
        }

        /// <summary>A set's entry, or null when Scryfall's list doesn't have it.</summary>
        public static SetInfo? Info(string? code) =>
            code != null && Sets().TryGetValue(code, out var s) ? s : null;

        /// <summary>
        /// The release a set belongs to: its parent's parent's … top set (the
        /// set itself when it has no parent, or the list isn't there).
        /// </summary>
        public static string ReleaseOf(string code)
        {
            var sets = Sets();
            string current = code;
            for (int hops = 0; hops < 10; hops++)           // guards a loop in the data
            {
                if (!sets.TryGetValue(current, out var s) || s.Parent.Length == 0 ||
                    string.Equals(s.Parent, current, StringComparison.OrdinalIgnoreCase))
                    break;
                current = s.Parent;
            }
            return current;
        }

        /// <summary>
        /// Every set of a release (the release itself and all its sub-sets:
        /// token sets, Commander, promos …), lower case. Empty when the list
        /// isn't there.
        /// </summary>
        public static List<string> SetsOfRelease(string release) =>
            Sets().Keys.Where(c => string.Equals(ReleaseOf(c), release, StringComparison.OrdinalIgnoreCase))
                .Select(c => c.ToLowerInvariant()).ToList();

        private static Dictionary<string, SetInfo> Sets()
        {
            try
            {
                var stamp = File.Exists(ListPath) ? File.GetLastWriteTimeUtc(ListPath) : DateTime.MinValue;
                if (_sets != null && stamp == _loadedStamp) return _sets;

                var map = new Dictionary<string, SetInfo>(StringComparer.OrdinalIgnoreCase);
                if (stamp != DateTime.MinValue)
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(ListPath));
                    if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                        foreach (var s in data.EnumerateArray())
                        {
                            string code = Str(s, "code");
                            if (code.Length == 0) continue;
                            map[code] = new SetInfo(code, Str(s, "name"), Str(s, "set_type"),
                                                    Str(s, "parent_set_code"), Str(s, "released_at"));
                        }
                }
                _sets = map;
                _loadedStamp = stamp;
                return map;
            }
            catch
            {
                return _sets = new Dictionary<string, SetInfo>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static string Str(JsonElement el, string prop) =>
            el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    }
}
