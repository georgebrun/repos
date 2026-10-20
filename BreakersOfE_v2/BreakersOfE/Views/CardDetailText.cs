using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Media;

namespace BreakersOfE.Views
{
    /// <summary>
    /// What the two card detail views (the side panel and the pop-out window)
    /// say about a card, worked out once so they always agree: the power /
    /// loyalty line, finishes, prices, the set symbol and the flip button.
    /// Works for any card type (PoolCard, TokenCard, DeckCard …) by property name.
    /// </summary>
    internal static class CardDetailText
    {
        public const string ShowBack = "🔄 Show Back Face";
        public const string ShowFront = "🔄 Show Front Face";

        private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> Props = new();

        /// <summary>A card property as text ("" when the card doesn't have it).</summary>
        public static string Get(object? card, string prop)
        {
            if (card == null) return "";
            var info = Props.GetOrAdd((card.GetType(), prop), k => k.Item1.GetProperty(k.Item2));
            return info?.GetValue(card)?.ToString() ?? "";
        }

        private static bool Flag(object card, string prop) =>
            bool.TryParse(Get(card, prop), out bool v) && v;

        // ── One side at a time (cards with a back picture) ────────────────
        // Transforming and modal double-faced cards show like a regular card:
        // the front side's name, type, cost, stats and text, and the back
        // side's when flipped. Split, adventure and flip cards have both
        // halves on one face, so they show the whole card (side -1).

        /// <summary>What a detail view shows for one side of a card.</summary>
        public sealed record FaceInfo(string Name, string TypeLine, string ManaCost,
                                      (string Label, string Value)? Stats, string OracleText, string FlavorText);

        /// <summary>The side to open on: the front for a card with a back picture, else the whole card.</summary>
        public static int FirstSide(object card) => HasBack(card) ? 0 : -1;

        /// <summary>One side (0 = front, 1 = back) or the whole card (-1).</summary>
        public static FaceInfo Face(object card, int side)
        {
            if (side < 0)
                return new FaceInfo(Get(card, "Name"), Get(card, "TypeLine"), Get(card, "ManaCost"),
                                    Stats(card), Get(card, "OracleText"), Get(card, "FlavorText"));

            string Part(string prop) => Models.CardFaces.Side(Models.CardFaces.Sides(Get(card, prop)), side);
            string Text(string prop) => Models.CardFaces.Side(Models.CardFaces.TextSides(Get(card, prop)), side);
            string name = Part("Name");
            return new FaceInfo(name.Length > 0 ? name : Get(card, "Name"), Part("TypeLine"), Part("ManaCost"),
                                Stats(card, side), Text("OracleText"), Text("FlavorText"));
        }

        /// <summary>
        /// "POWER / TOUGHNESS" 2/3, "LOYALTY / DEFENSE" 4, or null when the card has
        /// neither. The whole of a two-sided card (side -1): 1/2 // 5/5, 6/6 // —,
        /// or POWER / LOYALTY 0/2 // 5 for a creature that turns into a planeswalker.
        /// One side (0 / 1): that side's own, as on a regular card.
        /// </summary>
        public static (string Label, string Value)? Stats(object card, int side = -1)
        {
            const string None = Models.CardFaces.None;
            var pt = Models.CardFaces.Sides(Models.CardFaces.PowerToughness(Get(card, "Power"), Get(card, "Toughness")));
            var loy = Models.CardFaces.Sides(Get(card, "LoyaltyOrDefense"));
            static bool Has(string s) => s.Length > 0 && s != None;

            int n = Math.Max(pt.Length, loy.Length);
            var sides = new string[n];
            bool anyPt = false, anyLoy = false;
            for (int i = 0; i < n; i++)
            {
                string a = i < pt.Length ? pt[i] : "", b = i < loy.Length ? loy[i] : "";
                if (Has(a)) { sides[i] = a; anyPt = true; }
                else if (Has(b)) { sides[i] = b; anyLoy = true; }
                else sides[i] = None;
            }
            if (!anyPt && !anyLoy) return null;
            if (side >= 0)
            {
                if (side >= n || sides[side] == None) return null;
                bool isPt = side < pt.Length && Has(pt[side]);
                return (isPt ? "POWER / TOUGHNESS" : "LOYALTY / DEFENSE", sides[side]);
            }
            string label = anyPt && anyLoy ? "POWER / LOYALTY" : anyPt ? "POWER / TOUGHNESS" : "LOYALTY / DEFENSE";
            return (label, string.Join(Models.CardFaces.Separator, sides));
        }

        /// <summary>The finishes the printing exists in (Scryfall "finishes").</summary>
        public static string Finishes(object card)
        {
            // Want List and Trade Binder rows: their IsFoil says which finish the ROW is, not
            // which finishes the printing comes in — ask the card pool (their own flags if it's not there).
            if (card is Models.WantListEntry or Models.TradeBinderEntry)
            {
                var pool = Services.CollectionEditService.FindPoolCard(Services.CollectionEditService.CardsTable, Get(card, "ScryfallId"));
                if (pool != null) card = pool;
                else
                {
                    bool etched = Flag(card, "IsEtched");
                    return Models.CardFinish.AvailableText(Flag(card, "IsNonFoilAvailable"),
                        Flag(card, "IsFoilAvailable") && !Flag(card, "PrintingEtchedOnly"), etched);
                }
            }
            return Models.CardFinish.AvailableText(Flag(card, "IsNonFoil"), Flag(card, "IsFoil"), Flag(card, "IsEtched"));
        }

        /// <summary>One price per finish: "Non-Foil: $1.23", "Foil: …", "Etched: …", "MTGO: 0.05 tix" (online cards).</summary>
        public static List<string> Prices(object card)
        {
            var lines = new List<string>();
            void Usd(string label, string prop)
            {
                string v = Get(card, prop);
                if (v.Length > 0) lines.Add($"{label}: ${v}");
            }
            Usd("Non-Foil", "PriceUsd");
            Usd("Foil", "PriceUsdFoil");
            Usd("Etched", "PriceUsdEtched");
            string tix = Tix(card);
            if (tix.Length > 0) lines.Add($"MTGO: {tix} tix");
            return lines;
        }

        /// <summary>The MTGO price in tickets, for online cards only ("" otherwise).</summary>
        public static string Tix(object card) => Flag(card, "IsOnMtgo") ? Get(card, "PriceTix") : "";

        /// <summary>The set symbol tinted by rarity (common's black symbol swapped for the one that reads on the window).</summary>
        public static ImageSource? SetSymbol(object card)
        {
            string path = Get(card, "SetSymbolPath");
            if (path.Length == 0) return null;
            string rarity = Get(card, "Rarity");
            return new Services.ImageSourceConverter().Convert(
                new object[] { path, string.Equals(rarity, "common", StringComparison.OrdinalIgnoreCase) ? "ondark" : rarity },
                typeof(ImageSource), null!, System.Globalization.CultureInfo.CurrentCulture) as ImageSource;
        }

        /// <summary>The card has a back face picture (double-faced): show the flip button.</summary>
        public static bool HasBack(object card) => Get(card, "ImageBackUrl").Length > 0;

        /// <summary>The empty "decks" line, with how many decks were checked so "none" can be trusted.</summary>
        public static string NotInDecks(bool otherDecksOnly)
        {
            int scanned = Services.DeckIndexService.DeckCount - (otherDecksOnly ? 1 : 0);
            return otherDecksOnly
                ? $"Not in any of your other {scanned:N0} decks."
                : $"Not in any of your {scanned:N0} decks.";
        }

        // ── Links: the card on EDHREC, Scryfall and Commander Spellbook ──

        /// <summary>The card's page: 0 EDHREC, 1 Scryfall (this printing), 2 Commander Spellbook.</summary>
        public static string LinkFor(object card, int site)
        {
            string name = Get(card, "Name");
            return site switch
            {
                0 => Services.CardLinks.Edhrec(name, Get(card, "Layout")),
                1 => Services.CardLinks.Scryfall(name, Get(card, "SetCode"), Get(card, "CollectorNumber")),
                _ => Services.CardLinks.Spellbook(name),
            };
        }

        /// <summary>Fill a text block with "EDHREC · Scryfall · Commander Spellbook" links (empty for no card).</summary>
        public static void FillLinks(System.Windows.Controls.TextBlock target, object? card)
        {
            target.Inlines.Clear();
            if (card == null || Get(card, "Name").Length == 0) return;
            string[] labels = { "EDHREC", "Scryfall", "Commander Spellbook" };
            for (int i = 0; i < labels.Length; i++)
            {
                if (i > 0) target.Inlines.Add(new System.Windows.Documents.Run("  ·  "));
                int site = i;
                var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(labels[i]))
                {
                    ToolTip = LinkFor(card, site),
                };
                link.Click += (_, _) => Services.CardLinks.Open(LinkFor(card, site));
                target.Inlines.Add(link);
            }
        }

        // ── Salt (EDHREC's vote, downloaded with Update Database) ──

        /// <summary>"2.98 of 4 · #2 saltiest on EDHREC", "Low (under 0.5)", or how to get the scores.</summary>
        public static string SaltText(object? card)
        {
            if (card == null) return "";
            if (!Services.EdhrecService.HasSalt) return "Run Update Database to get salt scores.";
            var s = Services.EdhrecService.SaltOf(Get(card, "Name"));
            return s is { } info
                ? $"{info.Score:0.00} of 4 · #{info.Rank:N0} saltiest on EDHREC"
                : $"Low (under {Services.EdhrecService.SaltLowest:0.0} of 4)";
        }

        // ── Card tags (Scryfall's Tagger, downloaded with Update Database) ──

        private static readonly ConcurrentDictionary<string, string> OracleByName = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The card's tags (ramp, removal, card draw …). Cards without an
        /// oracle id of their own (deck lines) are looked up in the pool by name.
        /// </summary>
        public static List<Services.CardTag> Tags(object card)
        {
            string oid = Get(card, "OracleId");
            if (oid.Length == 0) oid = OracleIdOf(Get(card, "Name"));
            return Services.DeckDataService.TagsFor(oid);
        }

        private static string OracleIdOf(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            if (OracleByName.TryGetValue(name, out var known)) return known;
            string oid = "";
            try
            {
                using var db = new Data.AppDbContext();
                oid = db.PoolCards.Where(p => p.Name == name && p.OracleId != "")
                                  .Select(p => p.OracleId).FirstOrDefault() ?? "";
            }
            catch { /* no pool yet */ }
            OracleByName[name] = oid;
            return oid;
        }

        /// <summary>
        /// Fill a panel with the card's tags as small chips (the description
        /// as the tooltip). Returns the hint to show when there are none.
        /// </summary>
        public static string FillTags(System.Windows.Controls.Panel panel, object? card, double fontSize = 11)
        {
            panel.Children.Clear();
            if (card == null) return "";
            if (!Services.DeckDataService.HasTags) return "Run Update Database to get card tags.";
            var tags = Tags(card);
            if (tags.Count == 0) return "No tags for this card.";
            foreach (var t in tags)
            {
                var text = new System.Windows.Controls.TextBlock { Text = t.Label, FontSize = fontSize };
                text.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
                panel.Children.Add(new System.Windows.Controls.Border
                {
                    Child = text,
                    CornerRadius = new System.Windows.CornerRadius(8),
                    Padding = new System.Windows.Thickness(7, 1, 7, 2),
                    Margin = new System.Windows.Thickness(0, 0, 4, 4),
                    Background = new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80)),
                    ToolTip = string.IsNullOrWhiteSpace(t.Description) ? t.Label : $"{t.Label}: {t.Description}",
                });
            }
            return "";
        }
    }
}
