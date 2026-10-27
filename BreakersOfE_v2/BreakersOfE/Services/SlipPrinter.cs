using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using BreakersOfE.Models;

namespace BreakersOfE.Services
{
    /// <summary>
    /// One printed slip: a plain cut-out to sleeve over a card (a token you
    /// don't own, instead of a die). Text only: no art, no frame, no set or
    /// artist; the empty picture box shows the color (W/U/B/R/G/M/N).
    /// </summary>
    public sealed class Slip
    {
        public string Name { get; init; } = "";
        public string ManaCost { get; init; } = "";
        public string TypeLine { get; init; } = "";
        public string Text { get; init; } = "";
        public string PowerToughness { get; init; } = "";
        /// <summary>The card's colors, e.g. "GW" ("" = colorless).</summary>
        public string Colors { get; init; } = "";
        /// <summary>"Side 1 of 2" for a two-sided token; "" otherwise.</summary>
        public string Side { get; init; } = "";
        /// <summary>
        /// A stand-in for a real card (not a token): marked PROXY on the slip,
        /// as the Magic Tournament Rules ask ("clearly and conspicuously marked").
        /// </summary>
        public bool IsProxy { get; init; }

        /// <summary>BoE's Color column: W, U, B, R, G, M (two or more) or N (none).</summary>
        public string ColorCode
        {
            get
            {
                var distinct = (Colors ?? "").Where(c => "WUBRG".Contains(c)).Distinct().ToList();
                return distinct.Count == 0 ? "N" : distinct.Count > 1 ? "M" : distinct[0].ToString();
            }
        }

        /// <summary>The slips for a token: one, or one per side for a two-sided token.</summary>
        public static List<Slip> FromToken(TokenCard t) => FromCard(t);

        /// <summary>
        /// A token and, when you've paired it (two Scryfall tokens on one card),
        /// the token on its other side: this side first, then the other
        /// ("Side 1 of 2", "Side 2 of 2"). <paramref name="thisSideOnly"/>: just this one.
        /// Anything else: its own slips.
        /// </summary>
        public static List<Slip> WithOtherSides(object card, bool thisSideOnly = false)
        {
            var slips = FromCard(card);
            bool token = card is TokenCard || card is TokenCollectionEntry || (card is DeckCard d && d.IsTokenLine);
            if (thisSideOnly || !token || slips.Count != 1) return slips;     // a real double-faced token prints both faces already
            List<CollectionEditService.OtherSideRef> others;
            try { others = CollectionEditService.OtherSideRefs(card); }
            catch { return slips; }
            if (others.Count == 0) return slips;
            var list = new List<Slip>();
            int n = 1 + others.Count;
            list.Add(Renumber(slips[0], 1, n));
            int i = 2;
            foreach (var o in others)
            {
                var pool = CollectionEditService.FindPoolCard(CollectionEditService.TokensTable, o.ScryfallId);
                if (pool == null) continue;
                foreach (var sl in FromCard(pool)) list.Add(Renumber(sl, i, n));
                i++;
            }
            return list;
        }

        private static Slip Renumber(Slip s, int side, int of) => new()
        {
            Name = s.Name, ManaCost = s.ManaCost, TypeLine = s.TypeLine, Text = s.Text,
            PowerToughness = s.PowerToughness, Colors = s.Colors, IsProxy = s.IsProxy,
            Side = $"Side {side} of {of}",
        };

        /// <summary>
        /// Layouts printed as one slip per side (cards with two real faces).
        /// Split and adventure cards are one card, so they stay one slip.
        /// </summary>
        private static readonly HashSet<string> TwoSidedLayouts = new(StringComparer.OrdinalIgnoreCase)
        { "transform", "modal_dfc", "double_faced_token", "reversible_card", "flip", "meld", "battle" };

        /// <summary>
        /// The slips for any card row (pool, collection, deck, token, special
        /// tables): read by property name, so every table works the same.
        /// </summary>
        public static List<Slip> FromCard(object card)
        {
            string name = Get(card, "Name"), cost = Get(card, "ManaCost"), type = Get(card, "TypeLine"),
                   text = Get(card, "OracleText"), power = Get(card, "Power"), tough = Get(card, "Toughness"),
                   loyalty = Get(card, "LoyaltyOrDefense"), layout = Get(card, "Layout");
            if (loyalty.Length == 0) loyalty = Get(card, "Loyalty");
            string colors = Get(card, "Colors");
            if (colors.Length == 0)                                      // e.g. deck rows: from the cost
                colors = string.Concat("WUBRG".Where(c => cost.Contains(c)));

            // Tokens (token tables, a deck's Tokens part) are free to print; anything else is a proxy.
            bool proxy = !(card is TokenCard || (card is DeckCard d && d.IsTokenLine) ||
                           type.Contains("Token", StringComparison.OrdinalIgnoreCase));

            var names = CardFaces.Sides(name);
            bool split = names.Length > 1 && TwoSidedLayouts.Contains(layout);
            int sides = split ? names.Length : 1;
            var list = new List<Slip>();
            for (int i = 0; i < sides; i++)
            {
                string Pick(string value) => sides == 1 ? value : CardFaces.Side(CardFaces.Sides(value), i);
                string p = Pick(power), th = Pick(tough), loy = Pick(loyalty);
                string stats = !string.IsNullOrWhiteSpace(p) && !string.IsNullOrWhiteSpace(th) ? $"{p}/{th}"
                             : !string.IsNullOrWhiteSpace(loy) ? (Pick(type).Contains("Battle") ? $"Defense {loy}" : $"Loyalty {loy}")
                             : "";
                list.Add(new Slip
                {
                    Name = sides == 1 ? name : CardFaces.Side(names, i),
                    ManaCost = Pick(cost),
                    TypeLine = Pick(type),
                    Text = sides == 1 ? text : CardFaces.Side(CardFaces.TextSides(text), i),
                    PowerToughness = stats,
                    Colors = colors,
                    Side = sides == 1 ? "" : $"Side {i + 1} of {sides}",
                    IsProxy = proxy,
                });
            }
            return list;
        }

        private static string Get(object o, string prop) =>
            o.GetType().GetProperty(prop)?.GetValue(o)?.ToString() ?? "";
    }

    /// <summary>
    /// Prints slips on US Letter (8.5 × 11 in), 3 × 3 to a page, each
    /// 61 × 86 mm (1 mm under card size, so it sits in a sleeve without
    /// catching). The thin grey outline is the cut line. Printing goes through
    /// the Windows print window, so "Microsoft Print to PDF" makes a PDF.
    /// </summary>
    public static class SlipPrinter
    {
        private const double Dip = 96.0;                       // device-independent pixels per inch
        private const double PageW = 8.5 * Dip, PageH = 11 * Dip;
        private const double SlipW = 61 / 25.4 * Dip, SlipH = 86 / 25.4 * Dip;
        private const int Cols = 3, Rows = 3, PerPage = Cols * Rows;
        private const string Copyright = "© Wizards of the Coast LLC";

        /// <summary>Ask for a printer and print the slips. Returns a message for the status line ("" when cancelled).</summary>
        public static string Print(Window? owner, IReadOnlyList<Slip> slips)
        {
            if (slips.Count == 0) return "";
            var pd = new PrintDialog();
            // Start on the printer used last time (so a switched-off default printer doesn't get in the way).
            var last = FindPrinter(AppSettingsService.Current.SlipPrinter);
            if (last != null)
            {
                try { pd.PrintQueue = last; } catch { /* gone or unreachable: Windows' default */ }
            }
            try
            {
                pd.PrintTicket.PageMediaSize = new System.Printing.PageMediaSize(
                    System.Printing.PageMediaSizeName.NorthAmericaLetter, PageW, PageH);
                pd.PrintTicket.PageOrientation = System.Printing.PageOrientation.Portrait;
            }
            catch { /* a printer without a ticket: keep its defaults */ }
            if (pd.ShowDialog() != true) return "";

            // Remember the printer picked.
            try
            {
                string picked = pd.PrintQueue?.FullName ?? "";
                if (picked.Length > 0 && picked != AppSettingsService.Current.SlipPrinter)
                {
                    AppSettingsService.Current.SlipPrinter = picked;
                    AppSettingsService.Save(AppSettingsService.Current);
                }
            }
            catch { /* not worth stopping the print for */ }

            var doc = BuildDocument(slips);
            int pages = doc.Pages.Count;
            pd.PrintDocument(doc.DocumentPaginator, "Breakers of E — slips");
            return $"Sent {slips.Count} slip{(slips.Count == 1 ? "" : "s")} to the printer ({pages} page{(pages == 1 ? "" : "s")}).";
        }

        /// <summary>An installed printer by its full name; null when the name is empty or the printer is gone.</summary>
        private static System.Printing.PrintQueue? FindPrinter(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName)) return null;
            try
            {
                var server = new System.Printing.LocalPrintServer();   // not disposed: the queue found belongs to it
                var types = new[]
                {
                    System.Printing.EnumeratedPrintQueueTypes.Local,
                    System.Printing.EnumeratedPrintQueueTypes.Connections,
                };
                return server.GetPrintQueues(types)
                             .FirstOrDefault(q => string.Equals(q.FullName, fullName, StringComparison.OrdinalIgnoreCase));
            }
            catch { return null; }
        }

        /// <summary>The pages, 9 slips each.</summary>
        public static FixedDocument BuildDocument(IReadOnlyList<Slip> slips)
        {
            var doc = new FixedDocument();
            doc.DocumentPaginator.PageSize = new Size(PageW, PageH);
            double left = (PageW - Cols * SlipW) / 2, top = (PageH - Rows * SlipH) / 2;

            for (int start = 0; start < slips.Count; start += PerPage)
            {
                var page = new FixedPage { Width = PageW, Height = PageH, Background = Brushes.White };
                for (int i = 0; i < PerPage && start + i < slips.Count; i++)
                {
                    var slip = BuildSlip(slips[start + i]);
                    FixedPage.SetLeft(slip, left + (i % Cols) * SlipW);
                    FixedPage.SetTop(slip, top + (i / Cols) * SlipH);
                    page.Children.Add(slip);
                }
                page.Measure(new Size(PageW, PageH));
                page.Arrange(new Rect(0, 0, PageW, PageH));
                page.UpdateLayout();
                doc.Pages.Add(new PageContent { Child = page });
            }
            return doc;
        }

        // ── One slip ─────────────────────────────────────────────────────────

        private static readonly Brush Ink = Brushes.Black;
        private static readonly Brush Faint = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70));
        private static readonly Brush CutLine = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));

        /// <summary>Pale tints (easy on ink); the letter carries it on a black-and-white printer.</summary>
        private static Brush Tint(string code) => new SolidColorBrush(code switch
        {
            "W" => Color.FromRgb(0xF6, 0xF2, 0xE0),
            "U" => Color.FromRgb(0xDC, 0xE9, 0xF7),
            "B" => Color.FromRgb(0xE2, 0xDF, 0xE6),
            "R" => Color.FromRgb(0xF8, 0xDD, 0xD6),
            "G" => Color.FromRgb(0xDD, 0xEF, 0xDA),
            "M" => Color.FromRgb(0xF3, 0xE6, 0xC0),
            _ => Color.FromRgb(0xEC, 0xEC, 0xEC),
        });

        private static FrameworkElement BuildSlip(Slip s)
        {
            const double pad = 9;
            double innerW = SlipW - 2 * pad;

            var grid = new Grid { Width = innerW - 1, Height = SlipH - 2 * pad - 1 };   // 1 = the cut line on both sides
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                     // name + cost
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(SlipH * 0.36) });         // picture box
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                     // type line
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // text
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                     // P/T
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                     // copyright

            // Name, with the mana cost on the right
            var top = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 4) };
            var cost = SymbolRow(s.ManaCost, 12);
            DockPanel.SetDock(cost, Dock.Right);
            top.Children.Add(cost);
            top.Children.Add(new TextBlock
            {
                Text = s.Name, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Ink,
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetRow(top, 0);
            grid.Children.Add(top);

            // The empty picture box: black border, pale color tint, the color letter
            var letter = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            if (s.IsProxy)
                letter.Children.Add(new TextBlock
                {
                    Text = "PROXY", FontSize = 22, FontWeight = FontWeights.Black, Foreground = Ink,
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, -4),
                });
            letter.Children.Add(new TextBlock
            {
                Text = s.ColorCode, FontSize = s.IsProxy ? 32 : 40, FontWeight = FontWeights.Bold, Foreground = Faint,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            if (s.ColorCode == "M")
            {
                string symbols = string.Concat("WUBRG".Where(c => s.Colors.Contains(c)).Select(c => $"{{{c}}}"));
                var row = SymbolRow(symbols, 11);
                row.HorizontalAlignment = HorizontalAlignment.Center;
                letter.Children.Add(row);
            }
            var picture = new Border
            {
                BorderBrush = Ink, BorderThickness = new Thickness(1.5), Background = Tint(s.ColorCode),
                Child = letter, Margin = new Thickness(0, 0, 0, 4),
            };
            Grid.SetRow(picture, 1);
            grid.Children.Add(picture);

            // Type line
            var type = new TextBlock
            {
                Text = s.TypeLine, FontSize = 9.5, FontWeight = FontWeights.SemiBold, Foreground = Ink,
                TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 4),
            };
            Grid.SetRow(type, 2);
            grid.Children.Add(type);

            // Rules text with its symbols; shrinks to fit when it's long
            var text = RulesText(s.Text, innerW - 1);
            var fit = new Viewbox
            {
                Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Child = text,
            };
            Grid.SetRow(fit, 3);
            grid.Children.Add(fit);

            // Power / toughness
            if (s.PowerToughness.Length > 0)
            {
                var pt = new TextBlock
                {
                    Text = s.PowerToughness, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Ink,
                    HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 0),
                };
                Grid.SetRow(pt, 4);
                grid.Children.Add(pt);
            }

            // In place of artist, set and number
            var foot = new TextBlock
            {
                Text = string.Join("  ·  ", new[]
                {
                    s.Side,
                    s.IsProxy ? "Proxy · Not for tournament play" : "",
                    Copyright,
                }.Where(t => t.Length > 0)),
                FontSize = 6.5, Foreground = Faint, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0),
            };
            Grid.SetRow(foot, 5);
            grid.Children.Add(foot);

            // The cut line
            return new Border
            {
                Width = SlipW, Height = SlipH, BorderBrush = CutLine, BorderThickness = new Thickness(0.5),
                Background = Brushes.White, Padding = new Thickness(pad), Child = grid,
            };
        }

        /// <summary>A mana cost as symbols ("{2}{G}{W}"); a symbol with no picture shows as its text.</summary>
        private static StackPanel SymbolRow(string? cost, double size)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (string token in Symbols(cost))
            {
                var img = ManaCostConverter.SymbolImage(token);
                row.Children.Add(img != null
                    ? new Image { Source = img, Width = size, Height = size, Margin = new Thickness(0.5, 0, 0.5, 0) }
                    : new TextBlock { Text = token, FontSize = size * 0.8, Foreground = Ink });
            }
            return row;
        }

        /// <summary>Rules text, with {T}, {G} and the like drawn as symbols in the line.</summary>
        private static TextBlock RulesText(string? text, double width)
        {
            var tb = new TextBlock { Width = width, FontSize = 8.5, Foreground = Ink, TextWrapping = TextWrapping.Wrap };
            string s = (text ?? "").Replace("\r\n", "\n");
            int i = 0;
            while (i < s.Length)
            {
                int open = s.IndexOf('{', i);
                int close = open >= 0 ? s.IndexOf('}', open) : -1;
                if (open < 0 || close < 0)
                {
                    AddText(tb, s[i..]);
                    break;
                }
                if (open > i) AddText(tb, s[i..open]);
                string token = s[open..(close + 1)];
                var img = ManaCostConverter.SymbolImage(token);
                if (img != null)
                    tb.Inlines.Add(new InlineUIContainer(new Image { Source = img, Width = 9, Height = 9 })
                    { BaselineAlignment = BaselineAlignment.Center });
                else
                    tb.Inlines.Add(new Run(token));
                i = close + 1;
            }
            return tb;
        }

        private static void AddText(TextBlock tb, string part)
        {
            var lines = part.Split('\n');
            for (int k = 0; k < lines.Length; k++)
            {
                if (k > 0) tb.Inlines.Add(new LineBreak());
                if (lines[k].Length > 0) tb.Inlines.Add(new Run(lines[k]));
            }
        }

        private static IEnumerable<string> Symbols(string? cost)
        {
            if (string.IsNullOrEmpty(cost)) yield break;
            int i = 0;
            while (i < cost.Length)
            {
                int open = cost.IndexOf('{', i);
                if (open < 0) yield break;
                int close = cost.IndexOf('}', open);
                if (close < 0) yield break;
                yield return cost[open..(close + 1)];
                i = close + 1;
            }
        }
    }
}
