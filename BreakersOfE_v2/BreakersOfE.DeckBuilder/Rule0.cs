using System.Text;

namespace BreakersOfE.DeckBuilder
{
    /// <summary>
    /// The Rule 0 sheet: what to tell the table before a game — the
    /// commander, the bracket aimed for and the bracket the cards show, Game
    /// Changers, combos, extra turns, land denial, tutors, how it wins, and
    /// the saltiest cards. Plain text, to copy or save.
    /// </summary>
    public static class Rule0
    {
        public static string Sheet(DraftDeck d, BuildConstraints c, string deckName)
        {
            var cards = d.Picks.Where(p => !p.IsCommander).ToList();
            var commanders = d.Picks.Where(p => p.IsCommander).Select(p => p.Card).ToList();
            var all = d.Picks.Select(p => p.Card).ToList();
            static string Names(IEnumerable<Candidate> list) => string.Join(", ", list.Select(x => x.Name).OrderBy(n => n));

            var gameChangers = all.Where(x => x.IsGameChanger).ToList();
            var extraTurns = all.Where(Builder.GivesExtraTurns).ToList();
            var landDenial = all.Where(x => Builder.LandDenial.IsMatch(x.OracleText)).ToList();
            var twoCard = d.Combos.Where(k => k.TwoCardInfinite).ToList();
            var otherCombos = d.Combos.Where(k => !k.TwoCardInfinite).ToList();
            List<Candidate> InRole(Role r) => cards.Where(p => p.Roles.Contains(r)).Select(p => p.Card).ToList();

            // The lowest bracket the cards allow (the same checks as Deck Statistics → Bracket).
            int min = 1;
            var because = new List<string>();
            if (gameChangers.Count > 3) { min = 4; because.Add($"{gameChangers.Count} Game Changers"); }
            else if (gameChangers.Count > 0) { min = 3; because.Add($"{gameChangers.Count} Game Changer{(gameChangers.Count == 1 ? "" : "s")}"); }
            if (extraTurns.Count > 2 && min < 3) { min = 3; because.Add($"{extraTurns.Count} extra-turn cards"); }
            if (twoCard.Count > 0 && min < 3) { min = 3; because.Add($"{twoCard.Count} two-card combo{(twoCard.Count == 1 ? "" : "s")}"); }

            var sb = new StringBuilder();
            void Line(string label, string text) => sb.AppendLine($"{label,-20}{text}");

            sb.AppendLine($"RULE 0 — {deckName}");
            sb.AppendLine(new string('═', Math.Max(20, Math.Min(60, deckName.Length + 9))));
            Line("Commander:", commanders.Count == 0 ? "—" : string.Join(" + ", commanders.Select(x => x.Name)));
            Line("Colors:", c.ColorIdentity.Length == 0 ? "Colorless" : string.Join(" ", c.ColorIdentity.Select(ColorWord)));
            var themes = Builder.CommanderThemes(commanders);
            if (themes.Count > 0) Line("Plan:", string.Join(", ", themes));
            Line("Bracket aimed for:", BuildConstraints.BracketName(c.Bracket));
            Line("Cards show:", (min == 1 ? "Bracket 1–2" : min == 3 ? "at least Bracket 3" : "at least Bracket 4") +
                                (because.Count > 0 ? $" ({string.Join(", ", because)})" : " (nothing pushes it higher)"));
            sb.AppendLine();

            Line("Game Changers:", gameChangers.Count == 0 ? "None" : $"{gameChangers.Count} — {Names(gameChangers)}");
            Line("Two-card combos:", twoCard.Count == 0 ? "None" : "");
            foreach (var k in twoCard) sb.AppendLine($"  • {string.Join(" + ", k.Cards)}{(k.Results.Length > 0 ? " — " + k.Results : "")}");
            if (otherCombos.Count > 0)
            {
                Line("Other combos:", $"{otherCombos.Count}");
                foreach (var k in otherCombos.Take(5)) sb.AppendLine($"  • {string.Join(" + ", k.Cards)}{(k.Results.Length > 0 ? " — " + k.Results : "")}");
                if (otherCombos.Count > 5) sb.AppendLine($"  … and {otherCombos.Count - 5} more");
            }
            Line("Extra turns:", extraTurns.Count == 0 ? "None" : $"{extraTurns.Count} — {Names(extraTurns)}");
            Line("Land denial:", landDenial.Count == 0 ? "None" : $"{landDenial.Count} (check) — {Names(landDenial)}");
            var tutors = InRole(Role.Tutor);
            Line("Tutors:", tutors.Count == 0 ? "None" : $"{tutors.Count} — {Names(tutors)}");
            Line("Interaction:", $"{InRole(Role.Removal).Count} removal · {InRole(Role.BoardWipe).Count} board wipes · {InRole(Role.Counterspell).Count} counterspells");
            var finishers = InRole(Role.Finisher);
            Line("How it wins:", finishers.Count == 0 && d.Combos.Count == 0 ? "Combat and the deck's plan"
                : string.Join("; ", new[] { finishers.Count > 0 ? Names(finishers) : "", d.Combos.Count > 0 ? "combos above" : "" }.Where(x => x.Length > 0)));

            var salty = all.Where(x => x.Salt is >= 2).OrderByDescending(x => x.Salt).Take(5).ToList();
            if (salty.Count > 0) Line("Saltiest cards:", string.Join(", ", salty.Select(x => $"{x.Name} ({x.Salt:0.00})")));
            var spells = cards.Where(p => !p.Card.IsLand).ToList();
            if (spells.Count > 0)
                Line("Average cost:", $"{spells.Sum(p => p.Card.ManaValue * p.Copies) / spells.Sum(p => p.Copies):0.00} mana (spells)");
            Line("Cards:", $"{d.CardCount} · worth about ${d.Value:N2}");
            sb.AppendLine();
            sb.AppendLine("Talk it over with the table before the game: the bracket is about how the deck plays, not just its cards.");
            return sb.ToString();
        }

        private static string ColorWord(char ch) => ch switch
        {
            'W' => "White", 'U' => "Blue", 'B' => "Black", 'R' => "Red", 'G' => "Green", _ => ch.ToString(),
        };
    }
}
