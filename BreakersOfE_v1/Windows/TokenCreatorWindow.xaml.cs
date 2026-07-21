using BreakersOfE.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BreakersOfE.Windows
{
    public partial class TokenCreatorWindow : Window
    {
        public List<DeckCard> CreatedTokens { get; private set; } = new();
        public bool ToManaZone { get; private set; } = false;

        private static DeckCard? _lastToken;

        public TokenCreatorWindow()
        {
            InitializeComponent();
        }

        // ================================================================
        // PRESET DROPDOWN
        // ================================================================
        private void CmbPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbPreset.SelectedItem is not ComboBoxItem item) return;
            string tag = item.Tag?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(tag)) return;

            if (tag == "__COPY__")
            {
                if (_lastToken == null)
                {
                    MessageBox.Show("No token created yet.", "Copy Token",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    CmbPreset.SelectedIndex = 0;
                    return;
                }
                TxtName.Text = _lastToken.Name;
                TxtPower.Text = _lastToken.Power;
                TxtToughness.Text = _lastToken.Toughness;
                TxtType.Text = _lastToken.TypeLine;
                TxtKeywords.Text = _lastToken.OracleText;
                SetColorChecks(_lastToken.ColorIdentity);
                CmbPreset.SelectedIndex = 0;
                return;
            }

            var parts = tag.Split('|');
            if (parts.Length < 5) return;

            TxtName.Text = parts[0];
            TxtPower.Text = parts[1];
            TxtToughness.Text = parts[2];
            TxtType.Text = parts[3];
            TxtKeywords.Text = parts.Length > 5 ? parts[5] : string.Empty;
            SetColorChecks(parts[4]);
            CmbPreset.SelectedIndex = 0; // reset dropdown
        }

        private void SetColorChecks(string colorIdentity)
        {
            ChkWhite.IsChecked = colorIdentity.Contains("W");
            ChkBlue.IsChecked = colorIdentity.Contains("U");
            ChkBlack.IsChecked = colorIdentity.Contains("B");
            ChkRed.IsChecked = colorIdentity.Contains("R");
            ChkGreen.IsChecked = colorIdentity.Contains("G");
            ChkColorless.IsChecked = colorIdentity.Contains("C")
                && !colorIdentity.Any(c => "WUBRG".Contains(c));
        }

        private string GetColorIdentity()
        {
            var colors = new List<string>();
            if (ChkWhite.IsChecked == true) colors.Add("W");
            if (ChkBlue.IsChecked == true) colors.Add("U");
            if (ChkBlack.IsChecked == true) colors.Add("B");
            if (ChkRed.IsChecked == true) colors.Add("R");
            if (ChkGreen.IsChecked == true) colors.Add("G");
            if (ChkColorless.IsChecked == true && colors.Count == 0)
                colors.Add("C");

            return colors.Count switch
            {
                0 => "C",
                1 => colors[0],
                _ => "M" // multicolor
            };
        }

        // ================================================================
        // CREATE
        // ================================================================
        private void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(TxtQty.Text.Trim(), out int qty) || qty < 1) qty = 1;
            if (qty > 20) qty = 20;

            string name = TxtName.Text.Trim();
            string power = TxtPower.Text.Trim();
            string toughness = TxtToughness.Text.Trim();
            string typeLine = TxtType.Text.Trim();
            string keywords = TxtKeywords.Text.Trim();
            string colorId = GetColorIdentity();

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Please enter a token name.", "Create Token",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var token = new DeckCard
            {
                Name = name,
                Power = power,
                Toughness = toughness,
                TypeLine = typeLine,
                OracleText = keywords,
                ColorIdentity = colorId,
                IsToken = true,
                Quantity = 1
            };

            _lastToken = token;
            ToManaZone = (CmbZone.SelectedIndex == 1);

            for (int i = 0; i < qty; i++)
                CreatedTokens.Add(new DeckCard
                {
                    Name = token.Name,
                    Power = token.Power,
                    Toughness = token.Toughness,
                    TypeLine = token.TypeLine,
                    OracleText = token.OracleText,
                    ColorIdentity = token.ColorIdentity,
                    IsToken = true,
                    Quantity = 1
                });

            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;
    }
}