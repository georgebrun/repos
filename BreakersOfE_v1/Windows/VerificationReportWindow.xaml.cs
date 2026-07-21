using BreakersOfE.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BreakersOfE.Windows
{
    public partial class VerificationReportWindow : Window
    {
        public VerificationReportWindow(ImportResult result)
        {
            InitializeComponent();
            BuildReport(result);
        }

        private void BuildReport(ImportResult result)
        {
            // ── Card tables ──────────────────────────────────────────────────
            AddSectionHeader("CARD TABLES");
            AddRow("Pool Cards", result.PoolCardsImported);
            AddRow("Token Cards", result.TokenCardsImported);
            AddRow("Planar Cards", result.PlanarCardsImported);
            AddRow("Scheme Cards", result.SchemeCardsImported);
            AddRow("Vanguard Cards", result.VanguardCardsImported);
            AddRow("Art Series Cards", result.ArtSeriesCardsImported);
            AddRow("Conspiracy Cards", result.ConspiracyCardsImported);
            AddRow("Skipped", result.SkippedCount);
            AddDivider();
            AddRow("Total Imported", result.TotalImported, bold: true);

            // ── Color breakdown ──────────────────────────────────────────────
            AddSectionHeader("BY COLOR  (Pool Cards)");
            AddColorRow("White", result.WhiteCount, Color.FromRgb(0xF8, 0xF4, 0xC2));
            AddColorRow("Blue", result.BlueCount, Color.FromRgb(0x14, 0x6B, 0xD5));
            AddColorRow("Black", result.BlackCount, Color.FromRgb(0x44, 0x44, 0x44));
            AddColorRow("Red", result.RedCount, Color.FromRgb(0xD3, 0x21, 0x2D));
            AddColorRow("Green", result.GreenCount, Color.FromRgb(0x00, 0x73, 0x3E));
            AddColorRow("Multicolor", result.MulticolorCount, Color.FromRgb(0xD4, 0xAF, 0x37));
            AddColorRow("Colorless", result.ColorlessCount, Color.FromRgb(0xC0, 0xBE, 0xB5));
            AddColorRow("Land", result.LandCount, Color.FromRgb(0x8B, 0x6E, 0x4E));
            AddCheckRow("Color totals match", result.ColorCountMatchesTotal);

            // ── Rarity breakdown ─────────────────────────────────────────────
            AddSectionHeader("BY RARITY  (Pool Cards)");
            AddRow("C   Common", result.CommonCount);
            AddRow("U   Uncommon", result.UncommonCount);
            AddRow("R   Rare", result.RareCount);
            AddRow("M   Mythic", result.MythicCount);
            AddRow("?   Other", result.OtherRarityCount);
            AddCheckRow("Rarity totals match", result.RarityCountMatchesTotal);

            // ── Symbols ──────────────────────────────────────────────────────
            AddSectionHeader("SYMBOLS");
            AddRow("Mana Symbols", result.ManaSymbolsDownloaded);
            AddRow("Set Symbols", result.SetSymbolsDownloaded);

            // ── Keywords ─────────────────────────────────────────────────────
            AddSectionHeader("KEYWORD DICTIONARY");
            AddRow("Keyword Abilities", result.KeywordAbilitiesCount);
            AddRow("Keyword Actions", result.KeywordActionsCount);
            AddRow("Ability Words", result.AbilityWordsCount);
            AddRow("New Keywords Discovered", result.NewKeywordsDiscovered);

            // ── Basic verification ───────────────────────────────────────────
            AddSectionHeader("BASIC VERIFICATION");
            AddRow("Scryfall Total", result.ScryfallReportedTotal);
            AddRow("Database Total", result.DatabaseTotal);
            AddDivider();
            AddMatchRow(result.ScryfallReportedTotal == result.DatabaseTotal);

            // ── Deep verification ────────────────────────────────────────────
            AddSectionHeader("DEEP VERIFICATION");
            AddRow("Duplicate ScryfallIds", result.DuplicateScryfallIds);
            AddRow("Cards with no image URL", result.CardsWithNoImageUrl,
                note: "(will show placeholder image)");
            AddRow("Cards with empty name", result.CardsWithEmptyName);
            AddRow("Cards with empty set code", result.CardsWithEmptySetCode);
            AddCheckRow("Color count sanity", result.ColorCountMatchesTotal);
            AddCheckRow("Rarity count sanity", result.RarityCountMatchesTotal);
        }

        // ── Section header ────────────────────────────────────────────────────
        private void AddColorRow(string label, int value, Color swatchColor)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition
            { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            { Width = new GridLength(90) });

            var labelPanel = new StackPanel { Orientation = Orientation.Horizontal };
            labelPanel.Children.Add(new Rectangle
            {
                Width = 12,
                Height = 12,
                Fill = new SolidColorBrush(swatchColor),
                Stroke = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
                StrokeThickness = 0.5,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            labelPanel.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });

            var valueText = new TextBlock
            {
                Text = $"{value:N0}",
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(labelPanel, 0);
            Grid.SetColumn(valueText, 1);
            grid.Children.Add(labelPanel);
            grid.Children.Add(valueText);
            ReportPanel.Children.Add(grid);
        }

        private void AddSectionHeader(string text)
        {
            ReportPanel.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["AccentBrush"],
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Margin = new Thickness(0, 14, 0, 4)
            });
        }

        // ── Data row ─────────────────────────────────────────────────────────
        private void AddRow(string label, int value,
            bool bold = false, string note = "")
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(90)
            });

            // Label (with optional note)
            var labelPanel = new StackPanel { Orientation = Orientation.Horizontal };
            labelPanel.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["PrimaryTextBrush"],
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                FontSize = 12
            });

            if (!string.IsNullOrEmpty(note))
            {
                labelPanel.Children.Add(new TextBlock
                {
                    Text = $"  {note}",
                    Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["MutedTextBrush"],
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            var valueBlock = new TextBlock
            {
                Text = $"{value:N0}",
                Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["PrimaryTextBrush"],
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                FontSize = 12,
                TextAlignment = TextAlignment.Right,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            Grid.SetColumn(labelPanel, 0);
            Grid.SetColumn(valueBlock, 1);
            grid.Children.Add(labelPanel);
            grid.Children.Add(valueBlock);
            ReportPanel.Children.Add(grid);
        }

        // ── Check row (pass/fail) ─────────────────────────────────────────────
        private void AddCheckRow(string label, bool passed)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(90)
            });

            var labelBlock = new TextBlock
            {
                Text = label,
                Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["PrimaryTextBrush"],
                FontSize = 12
            };

            var valueBlock = new TextBlock
            {
                Text = passed ? "✅" : "⚠️",
                FontSize = 12,
                TextAlignment = TextAlignment.Right,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            Grid.SetColumn(labelBlock, 0);
            Grid.SetColumn(valueBlock, 1);
            grid.Children.Add(labelBlock);
            grid.Children.Add(valueBlock);
            ReportPanel.Children.Add(grid);
        }

        // ── Match row ─────────────────────────────────────────────────────────
        private void AddMatchRow(bool match)
        {
            ReportPanel.Children.Add(new TextBlock
            {
                Text = match
                    ? "✅  Counts match perfectly"
                    : "ℹ️  Count difference — Scryfall total includes digital-only cards (MTGO/Arena) which are excluded from your pool. This is expected.",
                Foreground = match
                    ? new SolidColorBrush(Color.FromRgb(78, 201, 78))
                    : new SolidColorBrush(Color.FromRgb(100, 160, 220)),
                FontWeight = FontWeights.Normal,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
        }

        // ── Divider ───────────────────────────────────────────────────────────
        private void AddDivider()
        {
            ReportPanel.Children.Add(new Border
            {
                Height = 1,
                Background = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["BorderBrush"],
                Margin = new Thickness(0, 6, 0, 6)
            });
        }

        private void AddSpacer()
        {
            ReportPanel.Children.Add(new Border
            {
                Height = 6
            });
        }

        // ── Close ─────────────────────────────────────────────────────────────
        private void CloseButton_Click(object sender, RoutedEventArgs e) =>
            Close();
    }
}