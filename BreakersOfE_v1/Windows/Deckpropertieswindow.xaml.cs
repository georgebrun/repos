using BreakersOfE.Models;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BreakersOfE.Windows
{
    public partial class DeckPropertiesWindow : Window
    {
        private readonly Deck _deck;

        public DeckPropertiesWindow(Deck deck)
        {
            InitializeComponent();
            _deck = deck;
            Loaded += DeckPropertiesWindow_Loaded;
        }

        private void DeckPropertiesWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Title = $"Deck Properties — {_deck.Name}";
            PopulateGeneral();
            PopulateComputed();
            PopulateArchetype();
            PopulatePowerOverride();
        }

        // ── General section ───────────────────────────────────────────────────
        private void PopulateGeneral()
        {
            TxtName.Text = _deck.Name;
            TxtDescription.Text = _deck.Description;
            TxtFileName.Text = string.IsNullOrEmpty(_deck.FilePath)
                ? "(not saved yet)"
                : _deck.FilePath;
        }

        // ── Computed section ──────────────────────────────────────────────────
        private void PopulateComputed()
        {
            // Counts
            LblDeckCount.Text = _deck.MainboardCount.ToString();
            LblSideboardCount.Text = _deck.SideboardCount.ToString();
            LblLandCount.Text = _deck.LandCount.ToString();
            LblCreatureCount.Text = _deck.CreatureCount.ToString();
            LblSpellCount.Text = _deck.SpellCount.ToString();
            LblAvgCmc.Text = _deck.AverageCmc.ToString("F1");
            LblValue.Text = _deck.TotalValueDisplay;

            // Colors
            PopulateColors();

            // Aggression
            AggressionBar.Value = _deck.AggressionScore;
            LblAggression.Text = _deck.AggressionLabel;

            // Aggression bar color
            AggressionBar.Foreground = _deck.AggressionScore >= 65
                ? Brushes.OrangeRed
                : _deck.AggressionScore >= 45
                    ? Brushes.Orange
                    : Brushes.SteelBlue;

            // Power level
            LblCalculatedPower.Text =
                $"Calculated: {_deck.CalculatedPowerLevel}/10";

            // Use calculated checkbox
            ChkUseCalculated.IsChecked = _deck.UseCalculatedPower;

            // Legality
            PopulateLegality();
        }

        private void PopulateColors()
        {
            ColorsPanel.Children.Clear();
            string identity = _deck.DeckColorIdentity;

            if (string.IsNullOrEmpty(identity))
            {
                ColorsPanel.Children.Add(new TextBlock
                {
                    Text = "Colorless",
                    FontSize = 13,
                    Foreground = Brushes.Gray
                });
                return;
            }

            var colorMap = new[]
            {
                ('W', "#F9FAF4", "White"),
                ('U', "#0E68AB", "Blue"),
                ('B', "#150B00", "Black"),
                ('R', "#D3202A", "Red"),
                ('G', "#00733E", "Green")
            };

            foreach (var (symbol, hex, name) in colorMap)
            {
                if (!identity.Contains(symbol)) continue;

                var border = new Border
                {
                    Width = 24,
                    Height = 24,
                    CornerRadius = new CornerRadius(12),
                    Background = new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString(hex)),
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 4, 0),
                    ToolTip = name
                };
                var tb = new TextBlock
                {
                    Text = symbol.ToString(),
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = symbol == 'W'
                        ? Brushes.Black : Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                border.Child = tb;
                ColorsPanel.Children.Add(border);
            }
        }

        private void PopulateLegality()
        {
            LegalityPanel.Children.Clear();

            var formats = new[]
            {
                ("S",  "Standard",  "standard"),
                ("P",  "Pioneer",   "pioneer"),
                ("M",  "Modern",    "modern"),
                ("L",  "Legacy",    "legacy"),
                ("V",  "Vintage",   "vintage"),
                ("C",  "Commander", "commander"),
                ("Pa", "Pauper",    "pauper"),
            };

            var mainCards = _deck.Cards
                .Where(c => c.Category == DeckCardCategory.Mainboard ||
                            c.Category == DeckCardCategory.Commander)
                .ToList();

            foreach (var (abbr, name, formatKey) in formats)
            {
                // Check every card is legal in this format
                bool allLegal = mainCards.Count > 0 &&
                    mainCards.All(c =>
                    {
                        if (string.IsNullOrWhiteSpace(c.LegalitiesJson))
                            return false;
                        try
                        {
                            using var doc = System.Text.Json.JsonDocument
                                .Parse(c.LegalitiesJson);
                            if (doc.RootElement.TryGetProperty(
                                    formatKey, out var val))
                                return val.GetString() == "legal";
                        }
                        catch { }
                        return false;
                    });

                // Find any illegal cards for tooltip
                var illegal = mainCards
                    .Where(c =>
                    {
                        if (string.IsNullOrWhiteSpace(c.LegalitiesJson))
                            return true;
                        try
                        {
                            using var doc = System.Text.Json.JsonDocument
                                .Parse(c.LegalitiesJson);
                            if (doc.RootElement.TryGetProperty(
                                    formatKey, out var val))
                                return val.GetString() != "legal";
                        }
                        catch { }
                        return true;
                    })
                    .Select(c => c.Name)
                    .Distinct()
                    .Take(5)
                    .ToList();

                string tooltip = allLegal
                    ? $"{name}: Legal"
                    : illegal.Count > 0
                        ? $"{name}: Illegal — {string.Join(", ", illegal)}"
                        : $"{name}: Unknown";

                var border = new Border
                {
                    Width = 28,
                    Height = 22,
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(0, 0, 4, 0),
                    Background = allLegal
                        ? new SolidColorBrush(Color.FromRgb(0, 120, 60))
                        : new SolidColorBrush(Color.FromRgb(180, 40, 40)),
                    ToolTip = tooltip
                };
                border.Child = new TextBlock
                {
                    Text = abbr,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                LegalityPanel.Children.Add(border);
            }
        }

        // ── Archetype combo ───────────────────────────────────────────────────
        private void PopulateArchetype()
        {
            CmbArchetype.Items.Clear();
            foreach (DeckArchetype arch in Enum.GetValues<DeckArchetype>())
                CmbArchetype.Items.Add(arch.ToString());

            CmbArchetype.SelectedIndex = (int)_deck.Archetype;
        }

        // ── Power override combo ──────────────────────────────────────────────
        private void PopulatePowerOverride()
        {
            CmbPowerOverride.Items.Clear();
            for (int i = 1; i <= 10; i++)
                CmbPowerOverride.Items.Add(i.ToString());

            CmbPowerOverride.SelectedIndex =
                (_deck.UserPowerLevel ?? _deck.CalculatedPowerLevel) - 1;

            CmbPowerOverride.IsEnabled = !_deck.UseCalculatedPower;
        }

        private void ChkUseCalculated_Changed(object sender, RoutedEventArgs e)
        {
            bool useCalc = ChkUseCalculated.IsChecked == true;
            CmbPowerOverride.IsEnabled = !useCalc;
        }

        // ── OK / Cancel ───────────────────────────────────────────────────────
        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            // Save editable fields back to deck
            string name = TxtName.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Deck name cannot be empty.",
                    "Validation", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                TxtName.Focus();
                return;
            }

            _deck.Name = name;
            _deck.Description = TxtDescription.Text.Trim();
            _deck.Archetype = (DeckArchetype)CmbArchetype.SelectedIndex;
            _deck.UseCalculatedPower = ChkUseCalculated.IsChecked == true;

            if (!_deck.UseCalculatedPower &&
                CmbPowerOverride.SelectedIndex >= 0)
                _deck.UserPowerLevel = CmbPowerOverride.SelectedIndex + 1;

            _deck.IsModified = true;

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}