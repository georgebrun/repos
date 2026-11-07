using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BreakersOfE.Playtest
{
    /// <summary>
    /// A printed * in a creature's power or toughness, counted from its own rules text
    /// and the table as it is now: "~'s power and toughness are each equal to the number
    /// of creature cards in all graveyards", "~'s power is equal to the number of cards in
    /// your hand and its toughness is equal to that number plus 1", "… equal to your life
    /// total", Tarmogoyf's card types. Null where the wording isn't one the table reads.
    /// </summary>
    public static class Stars
    {
        private const RegexOptions I = RegexOptions.IgnoreCase;

        public static (int? Power, int? Toughness) Count(GameCard c, Seat seat, IReadOnlyList<Seat> seats)
        {
            string text = Abilities.Tilde(c.ShownText, c.ShownName).Replace("\n", " ");
            Match m;
            if ((m = Regex.Match(text, @"~'s power and toughness are each equal to (.+?)(?:\.|$)", I)).Success)
            {
                int? both = Of(m.Groups[1].Value, seat, seats);
                return (both, both);
            }
            int? p = null, t = null;
            if ((m = Regex.Match(text, @"~'s power is equal to (.+?)(?: and its toughness is equal to (.+?))?(?:\.|$)", I)).Success)
            {
                p = Of(m.Groups[1].Value, seat, seats);
                if (m.Groups[2].Success)
                {
                    // "that number plus 1" is the printed "1+*": the * is the same count (the 1 is printed).
                    t = Regex.IsMatch(m.Groups[2].Value, @"^that number(?: plus \w+)?$", I) ? p : Of(m.Groups[2].Value, seat, seats);
                }
            }
            if ((m = Regex.Match(text, @"~'s toughness is equal to (.+?)(?:\.|$)", I)).Success) t = Of(m.Groups[1].Value, seat, seats);
            return (p, t);
        }

        /// <summary>The cards in one place: "exile" (yours), "your graveyard", "your hand", "your library", "all graveyards".</summary>
        private static IEnumerable<GameCard> Zone(string where, Seat seat, IReadOnlyList<Seat> seats) => where.ToLowerInvariant() switch
        {
            "exile" => seat.Exile,
            "your graveyard" => seat.Graveyard,
            "your hand" => seat.Hand,
            "your library" => seat.Library,
            "all graveyards" => seats.SelectMany(s => s.Graveyard),
            _ => Enumerable.Empty<GameCard>(),
        };

        private static readonly string[] CardTypes =
            { "Artifact", "Battle", "Creature", "Enchantment", "Instant", "Kindred", "Land", "Planeswalker", "Sorcery", "Tribal" };

        /// <summary>"For each Elf you control", "for each card in your graveyard": the count, or null when the table can't count it.</summary>
        public static int? Phrase(string phrase, Seat seat, IReadOnlyList<Seat> seats)
        {
            string q = phrase.Trim().TrimEnd('.');
            // "each other artifact you control", "creature … with power 4 or greater": not counted (it would be wrong, not just short).
            if (Regex.IsMatch(q, @"\bother\b|\bwith\b|\bthat\b|\bamong\b", I)) return null;
            return Of(q, seat, seats);
        }

        /// <summary>What a phrase counts: "the number of creatures you control", "your life total", "1 plus the number of …".</summary>
        private static int? Of(string phrase, Seat seat, IReadOnlyList<Seat> seats)
        {
            string q = phrase.Trim();
            var plus = Regex.Match(q, @"^(\w+) plus (.+)$", I);
            if (plus.Success && Abilities.Number(plus.Groups[1].Value) is int add && add >= 0)
                return Of(plus.Groups[2].Value, seat, seats) is int rest ? rest + add : null;
            q = Regex.Replace(q, @"^the (?:total )?number of ", "", I);
            // "instant and sorcery cards you own in exile and in your graveyard" (Crackling Drake): one count over several places.
            var places = Regex.Match(q, @"^(.+?) (?:you own )?in ((?:exile|your graveyard|your hand|your library|all graveyards)(?:,? and (?:in )?(?:exile|your graveyard|your hand|your library|all graveyards))+)$", I);
            if (places.Success)
            {
                string filter = Abilities.FilterFor(places.Groups[1].Value);
                int total = 0;
                foreach (var where in Regex.Split(places.Groups[2].Value, @",?\s+and\s+(?:in\s+)?", I))
                    total += Zone(where.Trim(), seat, seats).Count(o => Abilities.Fits(o, filter));
                return total;
            }
            var one = Regex.Match(q, @"^(.+?) you own in (exile|your graveyard|your hand)$", I);
            if (one.Success) return Zone(one.Groups[2].Value, seat, seats).Count(o => Abilities.Fits(o, Abilities.FilterFor(one.Groups[1].Value)));
            if (Regex.IsMatch(q, @"^your life total$", I)) return seat.Life;
            if (Regex.IsMatch(q, @"^card types among cards in all graveyards$", I))
                return CardTypes.Count(type => seats.Any(s => s.Graveyard.Any(g => g.Card.TypeLine.Contains(type, StringComparison.OrdinalIgnoreCase))));
            if (Regex.IsMatch(q, @"^cards in your hand$", I)) return seat.Hand.Count;
            if (Regex.IsMatch(q, @"^attacking creatures?$", I)) return seats.Sum(s => s.Battlefield.Count(o => o.Attacking));
            Match m;
            static bool OnField(GameCard o) => o.MergedInto == 0;
            if ((m = Regex.Match(q, @"^(.+?) you control$", I)).Success)
                return seat.Battlefield.Count(o => OnField(o) && Abilities.Fits(o, Abilities.FilterFor(m.Groups[1].Value)));
            if ((m = Regex.Match(q, @"^(.+?) your opponents control$", I)).Success)
                return seats.Where(s => s.Index != seat.Index).Sum(s => s.Battlefield.Count(o => OnField(o) && Abilities.Fits(o, Abilities.FilterFor(m.Groups[1].Value))));
            if ((m = Regex.Match(q, @"^(.+?) on the battlefield$", I)).Success)
                return seats.Sum(s => s.Battlefield.Count(o => OnField(o) && Abilities.Fits(o, Abilities.FilterFor(m.Groups[1].Value))));
            if ((m = Regex.Match(q, @"^(.+?) in your graveyard$", I)).Success)
                return seat.Graveyard.Count(o => Abilities.Fits(o, Abilities.FilterFor(m.Groups[1].Value)));
            if ((m = Regex.Match(q, @"^(.+?) in all graveyards$", I)).Success)
                return seats.Sum(s => s.Graveyard.Count(o => Abilities.Fits(o, Abilities.FilterFor(m.Groups[1].Value))));
            if ((m = Regex.Match(q, @"^(.+?) in your hand$", I)).Success)
                return seat.Hand.Count(o => Abilities.Fits(o, Abilities.FilterFor(m.Groups[1].Value)));
            if ((m = Regex.Match(q, @"^(.+?) in exile$", I)).Success)
                return seats.Sum(s => s.Exile.Count(o => Abilities.Fits(o, Abilities.FilterFor(m.Groups[1].Value))));
            return null;
        }
    }
}
