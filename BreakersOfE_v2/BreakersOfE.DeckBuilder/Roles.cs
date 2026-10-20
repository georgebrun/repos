using System.Text.RegularExpressions;

namespace BreakersOfE.DeckBuilder
{
    /// <summary>What a card does for a deck (a card can have several).</summary>
    public enum Role
    {
        Land,
        Ramp,
        Draw,
        Removal,
        BoardWipe,
        Counterspell,
        Protection,
        Tutor,
        Recursion,
        Finisher,
    }

    /// <summary>
    /// Sorts cards into roles. Scryfall's card tags come first (people tag
    /// cards by what they do); a card without tags is read from its rules
    /// text instead (a best guess). Both are shown as the card's reason.
    /// </summary>
    public static class RoleClassifier
    {
        public static string Name(Role r) => r switch
        {
            Role.Land => "Land",
            Role.Ramp => "Ramp",
            Role.Draw => "Card draw",
            Role.Removal => "Removal",
            Role.BoardWipe => "Board wipe",
            Role.Counterspell => "Counterspell",
            Role.Protection => "Protection",
            Role.Tutor => "Tutor",
            Role.Recursion => "Recursion",
            Role.Finisher => "Finisher",
            _ => r.ToString(),
        };

        // Scryfall oracle tag slugs → role (prefix match: "removal-creature" counts as removal).
        private static readonly (string Tag, Role Role)[] TagRoles =
        {
            ("ramp", Role.Ramp), ("mana-rock", Role.Ramp), ("mana-dork", Role.Ramp), ("land-ramp", Role.Ramp),
            ("extra-land", Role.Ramp), ("cost-reducer", Role.Ramp), ("ritual", Role.Ramp),
            ("draw", Role.Draw), ("card-advantage", Role.Draw), ("cantrip", Role.Draw), ("wheel", Role.Draw),
            ("impulse", Role.Draw),
            ("removal", Role.Removal), ("spot-removal", Role.Removal), ("creature-removal", Role.Removal),
            ("artifact-removal", Role.Removal), ("enchantment-removal", Role.Removal), ("planeswalker-removal", Role.Removal),
            ("burn-creature", Role.Removal), ("bounce", Role.Removal), ("edict", Role.Removal),
            ("sweeper", Role.BoardWipe), ("board-wipe", Role.BoardWipe), ("boardwipe", Role.BoardWipe), ("wrath", Role.BoardWipe),
            ("mass-removal", Role.BoardWipe),
            ("counterspell", Role.Counterspell), ("counter", Role.Counterspell),
            ("protection", Role.Protection), ("gives-hexproof", Role.Protection), ("gives-indestructible", Role.Protection),
            ("gives-shroud", Role.Protection), ("gives-protection", Role.Protection), ("phasing", Role.Protection),
            ("tutor", Role.Tutor),
            ("recursion", Role.Recursion), ("reanimate", Role.Recursion), ("reanimation", Role.Recursion),
            ("graveyard-recursion", Role.Recursion), ("regrowth", Role.Recursion),
            ("win-condition", Role.Finisher), ("wincon", Role.Finisher), ("alternate-win", Role.Finisher),
            ("overrun", Role.Finisher), ("extra-combat", Role.Finisher), ("extra-turn", Role.Finisher),
        };

        private static Regex Rx(string p) => new(p, RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Rules-text fallback (cards without tags).
        private static readonly Regex AddsMana = Rx(@"\badd \{|\badd (one|two|three) mana|\badd mana\b|mana of any (one )?(color|type)");
        private static readonly Regex FetchesLand = Rx(@"search your library for (up to (one|two|three) )?(a |an )?(basic )?(land|forest|plains|island|swamp|mountain)[^.]*?(onto the battlefield|put (it|them) onto)|put (a|up to one) land card[^.]* onto the battlefield|play an additional land");
        private static readonly Regex Draws = Rx(@"\bdraws? (a|two|three|four|x|that many|cards?)\b|draw cards equal|\bdraw (an additional|additional) card");
        private static readonly Regex SingleRemoval = Rx(@"(destroy|exile) target (creature|artifact|enchantment|planeswalker|permanent|nonland permanent|nonland|attacking|tapped|creature or)(?!\w* card)(?! you control)|deals? \S+ damage to (any target|target creature|target planeswalker|another target)|return target (creature|nonland permanent|permanent)(?! you control)[^.]* to its owner's hand|target (player|opponent) sacrifices|fights? (another )?target creature|gets? -\S+/-\S+ until end of turn");
        private static readonly Regex Wipe = Rx(@"(destroy|exile) (all|each) (creatures|creature|nonland permanents|artifacts|enchantments|permanents|other creatures)|all creatures get -|deals? \S+ damage to each creature|return all (creatures|nonland permanents)");
        private static readonly Regex Counter = Rx(@"counter target (spell|activated|ability|noncreature|creature spell|instant|sorcery)");
        private static readonly Regex Protects = Rx(@"(gain|gains|have|has) (hexproof|indestructible|shroud|protection from)|phase out|can't be (countered|the target)");
        private static readonly Regex Tutors = Rx(@"search your library for (a|an|up to (one|two)) (card|creature|instant|sorcery|artifact|enchantment|legendary|planeswalker|land card with|nonland|aura|equipment)(?![^.]*basic land)");
        private static readonly Regex Recurs = Rx(@"return (target|up to \S+ target|a|all) [^.]*cards? from (your|a) graveyard to (your hand|the battlefield)|return target [^.]*from your graveyard|put (target|a) [^.]*card from (a|your) graveyard onto the battlefield");
        private static readonly Regex Wins = Rx(@"you win the game|each opponent loses the game|extra turn|additional combat phase|creatures you control get \+\S+/\+\S+ and gain trample|double strike and trample");

        /// <summary>The card's roles, and whether they came from tags (true) or rules text (false).</summary>
        public static (HashSet<Role> Roles, bool FromTags) Classify(Candidate c)
        {
            var roles = new HashSet<Role>();
            if (c.IsLand) roles.Add(Role.Land);

            if (c.Tags.Count > 0)
            {
                foreach (var t in c.Tags)
                    foreach (var (tag, role) in TagRoles)
                        if (t.Equals(tag, StringComparison.OrdinalIgnoreCase) ||
                            t.StartsWith(tag + "-", StringComparison.OrdinalIgnoreCase))
                            roles.Add(role);
                // Lands that only make mana aren't "ramp" (they're the land count).
                if (c.IsLand) roles.Remove(Role.Ramp);
                if (roles.Count > (c.IsLand ? 1 : 0)) return (roles, true);
            }

            string text = c.OracleText ?? "";
            if (!c.IsLand && (AddsMana.IsMatch(text) || FetchesLand.IsMatch(text))) roles.Add(Role.Ramp);
            if (Draws.IsMatch(text)) roles.Add(Role.Draw);
            if (Wipe.IsMatch(text)) roles.Add(Role.BoardWipe);
            else if (SingleRemoval.IsMatch(text)) roles.Add(Role.Removal);
            if (Counter.IsMatch(text)) roles.Add(Role.Counterspell);
            if (Protects.IsMatch(text) && !c.IsLand) roles.Add(Role.Protection);
            if (Tutors.IsMatch(text)) roles.Add(Role.Tutor);
            if (Recurs.IsMatch(text)) roles.Add(Role.Recursion);
            if (Wins.IsMatch(text)) roles.Add(Role.Finisher);
            return (roles, false);
        }
    }
}
