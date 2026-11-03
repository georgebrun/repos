using System;
using System.Collections.Generic;
using System.Linq;

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
