using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BreakersOfE.Playtest
{
    /// <summary>
    /// A creature's keywords as it sits on the battlefield: its own (the
    /// keyword lines of its rules text), what its Auras and Equipment give it,
    /// what the battlefield gives ("Creatures you control have haste"), and
    /// keyword counters (a flying counter). Reminders only: the player plays it.
    /// </summary>
    public static class Keywords
    {
        /// <summary>The keywords shown, in this order.</summary>
        public static readonly string[] Known =
        {
            "Flying", "Reach", "Trample", "Haste", "Vigilance", "Deathtouch", "Lifelink", "First strike", "Double strike",
            "Menace", "Indestructible", "Hexproof", "Shroud", "Ward", "Defender", "Infect", "Protection",
            // Until end of turn, from abilities (not read from rules text as keywords):
            "Can't be blocked", "Can't block", "Regenerate",
        };

        /// <summary>The keyword a piece of text starts with ("ward {2}" → "Ward"; "protection from red" → "Protection"), or null.</summary>
        private static string? KeywordOf(string piece)
        {
            string p = piece.Trim().TrimEnd('.').Trim();
            foreach (var k in Known)
            {
                if (!p.StartsWith(k, StringComparison.OrdinalIgnoreCase)) continue;
                string rest = p[k.Length..].Trim();
                if (rest.Length == 0 || rest.StartsWith("{") || rest.StartsWith("—") || rest.StartsWith("-") ||
                    (k is "Protection" or "Hexproof" && rest.StartsWith("from", StringComparison.OrdinalIgnoreCase)) ||
                    (k is "Ward" && Regex.IsMatch(rest, @"^[—-]|^\{|^\d")))
                    return k;
            }
            return null;
        }

        /// <summary>A list of keywords in text: "flying and trample", "flying, vigilance, and lifelink".</summary>
        public static IEnumerable<string> ListOf(string text)
        {
            foreach (var piece in Regex.Split(text, @",\s*(?:and\s+)?|\s+and\s+"))
                if (KeywordOf(piece) is { } k) yield return k;
        }

        /// <summary>The keyword lines of rules text: lines made of keywords ("Flying, vigilance", "Ward {2}").</summary>
        public static List<string> Printed(string rulesText)
        {
            var result = new List<string>();
            foreach (var raw in rulesText.Replace("\r", "").Split('\n'))
            {
                string line = Regex.Replace(raw, @"\([^)]*\)", "").Trim();
                if (line.Length == 0) continue;
                var pieces = Regex.Split(line, @"[,;]\s*");
                if (KeywordOf(pieces[0]) == null) continue;
                foreach (var piece in pieces)
                    if (KeywordOf(piece) is { } k && !result.Contains(k)) result.Add(k);
            }
            return result;
        }

        /// <summary>Everything the creature has now, in <see cref="Known"/> order.</summary>
        public static List<string> For(GameCard c, Seat owner, IEnumerable<Seat> seats)
        {
            var has = new HashSet<string>(Printed(c.ShownText), StringComparer.OrdinalIgnoreCase);
            foreach (var k in c.TempKeywords) has.Add(k);                                   // "gains flying until end of turn"
            foreach (var (kind, n) in c.Counters)
                if (n > 0 && KeywordOf(kind) is { } k) has.Add(k);
            foreach (var a in owner.Battlefield.Where(o => o.AttachedTo == c.Id))
                foreach (Match m in Regex.Matches(Flat(a.ShownText), @"\b(?:Enchanted|Equipped) creature (?:gets [+-]\d+/[+-]\d+ and )?has ([^.""]+)", RegexOptions.IgnoreCase))
                    foreach (var k in ListOf(m.Groups[1].Value)) has.Add(k);
            foreach (var s in seats)
                foreach (var o in s.Battlefield.Where(o => !o.FaceDown && o.MergedInto == 0))
                {
                    bool mine = s == owner;
                    foreach (Match m in Regex.Matches(Flat(o.ShownText),
                                 @"\b(Other )?(?:(?:nontoken )?creatures|creature tokens)( you control)? (?:get [+-]\d+/[+-]\d+ and )?have ([^.""]+)", RegexOptions.IgnoreCase))
                    {
                        if (m.Groups[1].Success && o.Id == c.Id) continue;                  // "Other creatures": not itself
                        if (m.Groups[2].Success && !mine) continue;                          // "you control": its owner's only
                        if (m.Value.Contains("tokens", StringComparison.OrdinalIgnoreCase) && !c.IsToken) continue;
                        foreach (var k in ListOf(m.Groups[3].Value)) has.Add(k);
                    }
                    foreach (Match m in Regex.Matches(Flat(o.ShownText), @"\bAll creatures have ([^.""]+)", RegexOptions.IgnoreCase))
                        foreach (var k in ListOf(m.Groups[1].Value)) has.Add(k);
                }
            return Known.Where(has.Contains).ToList();
        }

        private static string Flat(string text) => text.Replace("\r", " ").Replace("\n", " ");

        /// <summary>
        /// Summoning sick: a creature that came under your control this turn
        /// (it can't attack or use its {T} abilities until your next turn) and
        /// doesn't have haste.
        /// </summary>
        public static bool IsSick(GameCard c, int turn, IReadOnlyCollection<string> keywords) =>
            c.IsCreatureNow && c.EnteredTurn == turn && turn > 0 && !keywords.Contains("Haste");
    }
}
