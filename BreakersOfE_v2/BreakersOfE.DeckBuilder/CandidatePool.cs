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
                else if (!card.ColorIdentity.ToUpperInvariant().All(identity.Contains)) why = OffColor;
                else if (!card.Legal) why = NotLegal;
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
                Legal = g.All(x => x.Legal), IsGameChanger = g.Any(x => x.IsGameChanger),
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
