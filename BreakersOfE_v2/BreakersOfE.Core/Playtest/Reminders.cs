using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BreakersOfE.Playtest
{
    /// <summary>A card asking to be remembered at a step: "Phyrexian Arena: At the beginning of your upkeep, …".</summary>
    public sealed record Reminder(GameCard Card, Phase Phase, string Text);

    /// <summary>
    /// "At the beginning of …" reminders, read from the cards' own rules
    /// text, so new sets work by themselves. Only reminders: what to do (and
    /// whether to) stays the player's call.
    /// </summary>
    public static class Reminders
    {
        private static readonly Dictionary<Phase, string[]> Phrases = new()
        {
            [Phase.Upkeep] = new[]
            {
                "at the beginning of your upkeep", "at the beginning of each upkeep", "at the beginning of each player's upkeep",
                "at the beginning of the upkeep", "cumulative upkeep", "echo", "vanishing", "fading",
            },
            [Phase.Draw] = new[]
            {
                "at the beginning of your draw step", "at the beginning of each player's draw step", "at the beginning of each draw step",
            },
            [Phase.Main1] = new[]
            {
                "at the beginning of your precombat main phase", "at the beginning of your first main phase",
                "at the beginning of each player's precombat main phase", "at the beginning of each precombat main phase",
            },
            [Phase.Combat] = new[]
            {
                "at the beginning of combat on your turn", "at the beginning of each combat", "at the beginning of combat",
            },
            [Phase.Main2] = new[]
            {
                "at the beginning of your postcombat main phase", "at the beginning of your second main phase",
            },
            [Phase.End] = new[]
            {
                "at the beginning of your end step", "at the beginning of each end step", "at the beginning of the end step",
                "at the beginning of each player's end step", "at the beginning of the next end step", "at end of turn",
            },
        };

        /// <summary>
        /// The reminders for these steps: from the battlefield (face up), and
        /// from the graveyard when the line talks about the graveyard (Ichorid, Bloodghast …).
        /// </summary>
        public static List<Reminder> For(Seat seat, IEnumerable<Phase> phases)
        {
            var list = new List<Reminder>();
            foreach (var p in phases.Distinct())
            {
                foreach (var c in seat.Battlefield.Where(c => !c.FaceDown))
                    foreach (var line in Lines(c.ShownText, p))
                        list.Add(new Reminder(c, p, line));
                foreach (var c in seat.Graveyard)
                    foreach (var line in Lines(c.ShownText, p).Where(l => l.Contains("graveyard", StringComparison.OrdinalIgnoreCase)))
                        list.Add(new Reminder(c, p, line));
            }
            return list;
        }

        /// <summary>The lines of a card's text that start something at this step (whole lines, as printed).</summary>
        public static IEnumerable<string> Lines(string text, Phase p)
        {
            if (string.IsNullOrWhiteSpace(text)) yield break;
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                string lower = line.ToLowerInvariant();
                if (Phrases[p].Any(ph => StartsWord(lower, ph)))
                    yield return line;
            }
        }

        // ── Combat: "whenever … attacks / blocks / dies" ─────────────────
        /// <summary>The card's own lines: a trigger ("When…", "Whenever…") or a keyword line ("Annihilator 2", "Flying, exalted").</summary>
        private static IEnumerable<string> TextLines(GameCard c) =>
            string.IsNullOrWhiteSpace(c.ShownText) ? Enumerable.Empty<string>()
                : c.ShownText.Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);

        private static bool IsTrigger(string line) => line.StartsWith("When", StringComparison.OrdinalIgnoreCase);

        /// <summary>The line speaks of the card itself: its name, "this creature", or "~".</summary>
        private static bool AboutItself(string line, GameCard c)
        {
            string name = c.FaceName(c.BackUp ? 1 : 0);
            string front = name.Split(',')[0];                                   // "Krenko, Mob Boss" is "Krenko" in its text
            return line.Contains("this creature", StringComparison.OrdinalIgnoreCase) || line.Contains("this vehicle", StringComparison.OrdinalIgnoreCase) ||
                   line.Contains('~') || (name.Length > 0 && line.Contains(name, StringComparison.OrdinalIgnoreCase)) ||
                   (front.Length > 2 && line.Contains(front, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>A keyword line (no reminder text needed) holding one of these keywords: "Flying, exalted" has exalted.</summary>
        private static bool HasKeywordLine(string line, params string[] keywords) =>
            !IsTrigger(line) && keywords.Any(k => Regex.IsMatch(Regex.Replace(line, @"\([^)]*\)", ""), $@"(^|[,;]\s*){Regex.Escape(k)}\b", RegexOptions.IgnoreCase));

        private static void AddOnce(List<Reminder> list, GameCard c, string line)
        {
            if (!list.Any(r => r.Card.Id == c.Id && r.Text == line)) list.Add(new Reminder(c, Phase.Combat, line));
        }

        /// <summary>
        /// Attackers declared: each attacker's "Whenever ~ attacks" (and annihilator, battle cry, melee, myriad, mentor, training,
        /// dethrone …); the attacking player's "whenever a creature you control attacks" and exalted (one attacker alone);
        /// the defending player's "whenever a creature attacks you".
        /// </summary>
        public static List<Reminder> Attacks(Seat attacker, IEnumerable<Seat> seats, IReadOnlyList<GameCard> attackers)
        {
            var list = new List<Reminder>();
            foreach (var a in attackers)
                foreach (var line in TextLines(a))
                {
                    if (IsTrigger(line) && Regex.IsMatch(line, @"\battacks?\b", RegexOptions.IgnoreCase) && AboutItself(line, a) &&
                        !Regex.IsMatch(line, @"\battacks? you\b", RegexOptions.IgnoreCase)) AddOnce(list, a, line);
                    else if (HasKeywordLine(line, "Annihilator", "Battle cry", "Melee", "Myriad", "Mentor", "Training", "Dethrone")) AddOnce(list, a, line);
                }
            var ids = attackers.Select(a => a.Id).ToHashSet();
            foreach (var s in seats)
                foreach (var c in s.Battlefield.Where(c => !c.FaceDown && c.MergedInto == 0))
                    foreach (var line in TextLines(c))
                    {
                        string l = line.ToLowerInvariant();
                        if (s.Index == attacker.Index)
                        {
                            if (IsTrigger(line) && (l.Contains("you control attack") || l.Contains("whenever you attack") ||
                                                    l.Contains("whenever a creature attacks") || l.Contains("creatures attack")) &&
                                !(ids.Contains(c.Id) && list.Any(r => r.Card.Id == c.Id && r.Text == line))) AddOnce(list, c, line);
                            else if (attackers.Count == 1 && HasKeywordLine(line, "Exalted")) AddOnce(list, c, line + " (attacking alone)");
                        }
                        else if (IsTrigger(line) && (l.Contains("attacks you") || l.Contains("attack you") || l.Contains("an opponent attacks") ||
                                                     l.Contains("whenever a creature attacks") || l.Contains("an opponent controls attack")))
                            AddOnce(list, c, line);
                    }
            return list;
        }

        /// <summary>A block: the blocker's "Whenever ~ blocks", the attacker's "Whenever ~ becomes blocked" (and bushido, flanking, rampage, afflict).</summary>
        public static List<Reminder> Blocks(GameCard blocker, GameCard attacker, bool firstBlock)
        {
            var list = new List<Reminder>();
            foreach (var line in TextLines(blocker))
            {
                if (IsTrigger(line) && Regex.IsMatch(line, @"\bblocks\b", RegexOptions.IgnoreCase) && AboutItself(line, blocker)) AddOnce(list, blocker, line);
                else if (HasKeywordLine(line, "Bushido")) AddOnce(list, blocker, line);
            }
            if (firstBlock)                                                          // "becomes blocked" triggers once, however many block it
                foreach (var line in TextLines(attacker))
                {
                    if (IsTrigger(line) && line.Contains("becomes blocked", StringComparison.OrdinalIgnoreCase) && AboutItself(line, attacker)) AddOnce(list, attacker, line);
                    else if (HasKeywordLine(line, "Bushido", "Rampage", "Afflict")) AddOnce(list, attacker, line);
                    else if (HasKeywordLine(line, "Flanking") && !Keywords.Printed(blocker.ShownText).Contains("Flanking")) AddOnce(list, attacker, line);
                }
            return list;
        }

        /// <summary>Creatures died: their own "When ~ dies", and every "whenever a creature / another creature dies" on the table (Blood Artist).</summary>
        public static List<Reminder> Deaths(IEnumerable<Seat> seats, IReadOnlyList<GameCard> died)
        {
            var list = new List<Reminder>();
            if (died.Count == 0) return list;
            bool AnyCreature(string l) => Regex.IsMatch(l, @"\b(a|another|one or more|each)\b[^.,]*\bcreatures?\b[^.]*\bdies?\b", RegexOptions.IgnoreCase) ||
                                          Regex.IsMatch(l, @"\bcreatures?\b[^.]*\b(you control|an opponent controls|your opponents control)\b[^.]*\bdies?\b", RegexOptions.IgnoreCase);
            foreach (var d in died)
                foreach (var line in TextLines(d))
                    if (IsTrigger(line) && Regex.IsMatch(line, @"\bdies?\b", RegexOptions.IgnoreCase) && (AboutItself(line, d) || AnyCreature(line)))
                        AddOnce(list, d, line);
            var gone = died.Select(d => d.Id).ToHashSet();
            foreach (var s in seats)
                foreach (var c in s.Battlefield.Where(c => !c.FaceDown && c.MergedInto == 0 && !gone.Contains(c.Id)))
                    foreach (var line in TextLines(c))
                        if (IsTrigger(line) && AnyCreature(line)) AddOnce(list, c, line + (died.Count > 1 ? $" ({died.Count} died)" : ""));
            return list;
        }

        /// <summary>The phrase is in the line, as words (so "echo" doesn't match "echoing" in flavor-like text, and reminder text in parentheses still counts).</summary>
        private static bool StartsWord(string line, string phrase)
        {
            int i = line.IndexOf(phrase, StringComparison.Ordinal);
            while (i >= 0)
            {
                bool before = i == 0 || !char.IsLetter(line[i - 1]);
                int end = i + phrase.Length;
                bool after = end >= line.Length || !char.IsLetter(line[end]);
                if (before && after) return true;
                i = line.IndexOf(phrase, i + 1, StringComparison.Ordinal);
            }
            return false;
        }
    }
}
