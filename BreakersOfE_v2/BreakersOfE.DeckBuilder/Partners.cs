namespace BreakersOfE.DeckBuilder
{
    /// <summary>
    /// Two commanders: which cards can pair up. Partner + Partner, "Partner
    /// with X" + X, "Partner—Kind" + the same kind, Friends forever + Friends
    /// forever, Choose a Background + a Background, Doctor's companion + a
    /// Time Lord Doctor. (The same rules as Format Check.)
    /// </summary>
    public static class Partners
    {
        /// <summary>The card can have a second commander at all (or is a Background / Doctor that goes with one).</summary>
        public static bool CanHaveSecond(string typeLine, string oracleText) =>
            PartnerWith(oracleText) != null || Variant(oracleText) != null || Has(oracleText, "Partner") ||
            Has(oracleText, "Friends forever") || Has(oracleText, "Choose a Background") ||
            Has(oracleText, "Doctor's companion") || FrontIs(typeLine, "Background") || FrontIs(typeLine, "Time Lord Doctor");

        /// <summary>"Partner", "Choose a Background" … — what the first commander pairs with (for the panel).</summary>
        public static string Describe(string typeLine, string oracleText)
        {
            if (PartnerWith(oracleText) is { } named) return $"Partner with {named}";
            if (Variant(oracleText) is { } kind) return $"Partner—{kind}";
            if (Has(oracleText, "Partner")) return "Partner";
            if (Has(oracleText, "Friends forever")) return "Friends forever";
            if (Has(oracleText, "Choose a Background")) return "Choose a Background";
            if (Has(oracleText, "Doctor's companion")) return "Doctor's companion";
            if (FrontIs(typeLine, "Time Lord Doctor")) return "a Doctor (pairs with a Doctor's companion)";
            if (FrontIs(typeLine, "Background")) return "a Background (pairs with Choose a Background)";
            return "";
        }

        /// <summary>The two can be commanders together.</summary>
        public static bool CanPair(string nameA, string typeA, string textA, string nameB, string typeB, string textB) =>
            !nameA.Equals(nameB, StringComparison.OrdinalIgnoreCase) &&
            Pair(typeA, textA, nameB, typeB, textB) && Pair(typeB, textB, nameA, typeA, textA);

        private static bool Pair(string typeX, string textX, string nameY, string typeY, string textY)
        {
            if (PartnerWith(textX) is { } named)
                return nameY.Equals(named, StringComparison.OrdinalIgnoreCase) ||
                       nameY.StartsWith(named + " //", StringComparison.OrdinalIgnoreCase);
            if (Variant(textX) is { } kind) return Variant(textY) == kind;
            if (Has(textX, "Partner"))
                return Has(textY, "Partner") && PartnerWith(textY) == null && Variant(textY) == null;
            if (Has(textX, "Friends forever")) return Has(textY, "Friends forever");
            if (Has(textX, "Choose a Background")) return FrontIs(typeY, "Background");
            if (FrontIs(typeX, "Background")) return Has(textY, "Choose a Background");
            if (Has(textX, "Doctor's companion")) return FrontIs(typeY, "Time Lord Doctor");
            if (FrontIs(typeX, "Time Lord Doctor")) return Has(textY, "Doctor's companion");
            return false;
        }

        private static IEnumerable<string> Lines(string text) => (text ?? "").Split('\n').Select(l => l.Trim());

        /// <summary>A keyword ability at the start of a rules-text line.</summary>
        private static bool Has(string text, string ability) =>
            Lines(text).Any(l => l.StartsWith(ability, StringComparison.OrdinalIgnoreCase));

        /// <summary>"Partner with Pir, Imaginative Rascal" → "Pir, Imaginative Rascal".</summary>
        private static string? PartnerWith(string text)
        {
            foreach (var line in Lines(text))
            {
                if (!line.StartsWith("Partner with ", StringComparison.OrdinalIgnoreCase)) continue;
                string name = line[13..];
                int cut = name.IndexOf(" (", StringComparison.Ordinal);
                return (cut > 0 ? name[..cut] : name).Trim();
            }
            return null;
        }

        /// <summary>"Partner—Survivors" → "survivors" (null for plain Partner or none).</summary>
        private static string? Variant(string text)
        {
            foreach (var line in Lines(text))
            {
                if (!line.StartsWith("Partner", StringComparison.OrdinalIgnoreCase) || line.Length < 9) continue;
                char sep = line[7];
                if (sep != '—' && sep != '-') continue;
                string kind = line[8..];
                int cut = kind.IndexOf(" (", StringComparison.Ordinal);
                return (cut > 0 ? kind[..cut] : kind).Trim().ToLowerInvariant();
            }
            return null;
        }

        private static bool FrontIs(string typeLine, string type)
        {
            string front = typeLine ?? "";
            int i = front.IndexOf(" // ", StringComparison.Ordinal);
            if (i >= 0) front = front[..i];
            return front.Contains(type, StringComparison.OrdinalIgnoreCase);
        }
    }
}
