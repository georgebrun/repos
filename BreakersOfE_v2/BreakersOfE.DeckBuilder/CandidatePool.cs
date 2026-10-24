namespace BreakersOfE.DeckBuilder
{
    /// <summary>A candidate the builder may pick, with its roles worked out.</summary>
    public sealed class Eligible
    {
        public Candidate Card { get; init; } = new();
        public HashSet<Role> Roles { get; init; } = new();
        /// <summary>Roles came from Scryfall's tags (else read from the rules text).</summary>
        public bool RolesFromTags { get; init; }
        public override string ToString() => $"{Card.Name} [{string.Join(", ", Roles.Select(RoleClassifier.Name))}]";
    }

    /// <summary>Why cards were left out (counts per reason, with a few examples).</summary>
    public sealed class Exclusions
    {
        public Dictionary<string, List<string>> ByReason { get; } = new();

        public void Add(string reason, string card)
        {
            if (!ByReason.TryGetValue(reason, out var list)) ByReason[reason] = list = new();
            list.Add(card);
        }

        public int Count(string reason) => ByReason.TryGetValue(reason, out var l) ? l.Count : 0;
    }

    /// <summary>
    /// Step 1 of a build: the cards the builder may use under these
    /// constraints — in the commander's colors, legal, not excluded, and from
    /// the allowed sources (your free copies; cards to get only when the
    /// sources and spend limit allow). Each keeps its roles.
    /// </summary>
    public static class CandidatePool
    {
        public const string OffColor = "Outside the commander's colors";
        public const string OffColorChosen = "Outside the deck's colors";
        public const string NeedsCommander = "Only works with a commander";
        public const string NoMulticolor = "Multicolored (M is off)";
        public const string NoColorless = "Colorless (N is off)";
        public const string NotLegal = "Not legal in the format";
        public const string NotFree = "No free copy in your collection";
        public const string OverBudget = "Costs more than the spend limit";
        public const string NoPrice = "No price to judge against the spend limit";
        public const string ExcludedByYou = "Excluded by you";
        public const string IsCommander = "It's the commander";

        public static (List<Eligible> Eligible, Exclusions Left) Build(IEnumerable<Candidate> candidates, BuildConstraints c)
        {
            var eligible = new List<Eligible>();
            var left = new Exclusions();
            var identity = new HashSet<char>(c.ColorIdentity.ToUpperInvariant());
            var commanders = new HashSet<string>(c.Commanders, StringComparer.OrdinalIgnoreCase);
            var excluded = new HashSet<string>(c.Excluded, StringComparer.OrdinalIgnoreCase);
            var locked = new HashSet<string>(c.Locked, StringComparer.OrdinalIgnoreCase);

            foreach (var card in candidates.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(Merge))
            {
                string why = "";
                if (commanders.Contains(card.Name)) why = IsCommander;
                else if (excluded.Contains(card.Name)) why = ExcludedByYou;
                else if (c.IsConstructed ? !FitsColors(card, identity) : !card.ColorIdentity.ToUpperInvariant().All(identity.Contains))
                    why = c.IsConstructed ? OffColorChosen : OffColor;
                else if (!card.Legal) why = NotLegal;
                else if (c.IsConstructed && NeedsACommander(card)) why = NeedsCommander;
                else if (c.IsConstructed && !card.IsLand && !c.AllowMulticolor && ColorCount(card) >= 2) why = NoMulticolor;
                else if (c.IsConstructed && !card.IsLand && !c.AllowColorless && ColorCount(card) == 0) why = NoColorless;
                else if (card.FreeCopies <= 0 && !card.IsBasicLand && !locked.Contains(card.Name))
                {
                    if (c.Sources == SourceMode.CollectionOnly) why = NotFree;
                    else if (card.Price is not { } p) why = NoPrice;
                    else if (p > c.SpendLimit) why = OverBudget;
                }
                if (why.Length > 0) { left.Add(why, card.Name); continue; }

                var (roles, fromTags) = RoleClassifier.Classify(card);
                eligible.Add(new Eligible { Card = card, Roles = roles, RolesFromTags = fromTags });
            }
            return (eligible, left);
        }

        /// <summary>
        /// 60-card decks: can the deck's colors cast it? Spells by their mana
        /// cost (a hybrid symbol needs one of its colors, a Phyrexian one can
        /// be paid with life); lands and cards with no cost by color identity.
        /// </summary>
        public static bool FitsColors(Candidate card, ISet<char> colors)
        {
            if (card.IsLand || string.IsNullOrWhiteSpace(card.ManaCost))
                return card.ColorIdentity.ToUpperInvariant().Where(ch => "WUBRG".Contains(ch)).All(colors.Contains);
            foreach (var face in card.ManaCost.Split("//"))            // split / adventure: every half castable
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(face, @"\{([^}]+)\}"))
                {
                    var parts = m.Groups[1].Value.ToUpperInvariant().Split('/');
                    if (parts.Contains("P")) continue;                              // Phyrexian: life will do
                    var need = parts.Where(x => x.Length == 1 && "WUBRG".Contains(x[0])).Select(x => x[0]).ToList();
                    if (need.Count == 0) continue;                                  // generic, X, colorless, snow
                    if (parts.Length > 1 && parts.Any(x => int.TryParse(x, out _))) continue;   // {2/W}: 2 generic will do
                    if (!need.Any(colors.Contains)) return false;
                }
            return true;
        }

        /// <summary>A spell's colors: the color letters in its mana cost (hybrid and Phyrexian count); a card with no cost by its color identity.</summary>
        public static int ColorCount(Candidate card)
        {
            if (string.IsNullOrWhiteSpace(card.ManaCost))
                return card.ColorIdentity.ToUpperInvariant().Where(ch => "WUBRG".Contains(ch)).Distinct().Count();
            return System.Text.RegularExpressions.Regex.Matches(card.ManaCost, @"\{([^}]+)\}")
                .SelectMany(m => m.Groups[1].Value.ToUpperInvariant()).Where(ch => "WUBRG".Contains(ch)).Distinct().Count();
        }

        /// <summary>A card whose text only works with a commander (Command Tower, Arcane Signet, Path of Ancestry …).</summary>
        public static bool NeedsACommander(Candidate card) =>
            card.OracleText.Contains("commander's color identity", StringComparison.OrdinalIgnoreCase) ||
            card.OracleText.Contains("your commander", StringComparison.OrdinalIgnoreCase) ||
            card.OracleText.Contains("commander you own", StringComparison.OrdinalIgnoreCase) ||
            card.OracleText.Contains("command zone", StringComparison.OrdinalIgnoreCase);

        /// <summary>Several rows of one card (printings): one candidate — copies added up, the lowest price.</summary>
        private static Candidate Merge(IGrouping<string, Candidate> g)
        {
            var first = g.First();
            if (g.Count() == 1) return first;
            return new Candidate
            {
                Name = first.Name, OracleId = first.OracleId, TypeLine = first.TypeLine, OracleText = first.OracleText,
                ManaCost = first.ManaCost, ManaValue = first.ManaValue, ColorIdentity = first.ColorIdentity,
                Keywords = first.Keywords, Tags = g.SelectMany(x => x.Tags).Distinct().ToList(),
                Legal = g.All(x => x.Legal), Restricted = g.Any(x => x.Restricted), IsGameChanger = g.Any(x => x.IsGameChanger),
                OwnedCopies = g.Sum(x => x.OwnedCopies), FreeCopies = g.Sum(x => x.FreeCopies),
                Price = g.Where(x => x.Price.HasValue).Select(x => x.Price).DefaultIfEmpty(null).Min(),
                EdhrecInclusion = g.Max(x => x.EdhrecInclusion), EdhrecSynergy = g.Max(x => x.EdhrecSynergy),
                Salt = g.Max(x => x.Salt),
            };
        }

        /// <summary>How many eligible cards fill each role (a card counts in each of its roles).</summary>
        public static Dictionary<Role, int> RoleCounts(IEnumerable<Eligible> eligible)
        {
            var counts = Enum.GetValues<Role>().ToDictionary(r => r, _ => 0);
            foreach (var e in eligible)
                foreach (var r in e.Roles) counts[r]++;
            return counts;
        }
    }
}
