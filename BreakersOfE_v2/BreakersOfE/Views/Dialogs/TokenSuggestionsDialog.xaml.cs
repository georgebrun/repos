using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using BreakersOfE.Models;
using BreakersOfE.Services;
using Wpf.Ui.Controls;

namespace BreakersOfE.Views.Dialogs
{
    /// <summary>
    /// Edit → Decks: "Suggested Tokens…" (whole deck) and right-click → "Add
    /// its tokens…" (the selected cards). Lists the tokens Scryfall links to
    /// the cards; the ticked ones go to the deck's Tokens part.
    /// </summary>
    public partial class TokenSuggestionsDialog : FluentWindow
    {
        private readonly List<TokenSuggestion> _items;

        /// <summary>The ticked tokens (after Add).</summary>
        public List<TokenSuggestion> Picked { get; private set; } = new();
        /// <summary>Copies of each (after Add).</summary>
        public int Copies { get; private set; } = 1;

        private TokenSuggestionsDialog(string header, List<TokenSuggestion> items, string emptyMessage, bool allowAdd = true)
        {
            InitializeComponent();
            _items = items;
            HeaderText.Text = header;
            TokenList.ItemsSource = items;

            // Prefer: shown when some token is yours both single-sided and two-sided.
            PreferPanel.Visibility = items.Any(s => s.OwnedSingle != null && s.OwnedTwoSided != null)
                ? Visibility.Visible : Visibility.Collapsed;
            _loading = true;
            PreferBox.SelectedIndex = AppSettingsService.Current.PreferTwoSidedTokens ? 1 : 0;
            _loading = false;

            if (items.Count == 0)
            {
                NoteText.Text = emptyMessage;
                QtyPanel.Visibility = Visibility.Collapsed;
                TickPanel.Visibility = Visibility.Collapsed;
                BtnAdd.Visibility = Visibility.Collapsed;
                BtnPrint.Visibility = Visibility.Collapsed;
                BtnCancel.Content = "Close";
            }
            else
            {
                NoteText.Text = "Tokens Scryfall links to these cards — ticked unless the deck already has them. " +
                                "When you own a token, your printing is the one offered. " +
                                "They go to the deck's Tokens part (never counted as part of the deck).";
                if (!allowAdd)
                {
                    // The Build page's draft: not a deck yet, so look and print only.
                    BtnAdd.Visibility = Visibility.Collapsed;
                    BtnCancel.Content = "Close";
                    NoteText.Text = "Tokens Scryfall links to the draft's cards. Print slips now if you like; " +
                                    "you can add them to the deck when you Accept the draft.";
                }
            }
        }

        /// <summary>Show the list (or <paramref name="emptyMessage"/> when there's nothing); null when cancelled or nothing was added.</summary>
        public static TokenSuggestionsDialog? Ask(Window? owner, string header, List<TokenSuggestion> items, string emptyMessage,
                                                  bool allowAdd = true)
        {
            var dlg = new TokenSuggestionsDialog(header, items, emptyMessage, allowAdd) { Owner = owner };
            return dlg.ShowDialog() == true && dlg.Picked.Count > 0 ? dlg : null;
        }

        private bool _loading;

        /// <summary>Prefer changed: offer the other kind of your printings, and remember it.</summary>
        private void PreferBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_loading || !IsInitialized) return;
            bool two = PreferBox.SelectedIndex == 1;
            var settings = AppSettingsService.Current;
            if (settings.PreferTwoSidedTokens != two)
            {
                settings.PreferTwoSidedTokens = two;
                AppSettingsService.Save(settings);
            }
            foreach (var s in _items) s.Prefer(two);
            TokenSuggestionService.LinkShared(_items);
            Rebind();
        }

        private void Rebind()
        {
            TokenList.ItemsSource = null;          // plain objects: re-bind to show the change
            TokenList.ItemsSource = _items;
        }

        /// <summary>
        /// The tokens to print or add, as picked. One card of yours that's both
        /// tokens the deck makes is used for both (one side at a time): both go
        /// in the deck, and it prints once (both sides).
        /// </summary>
        private List<(TokenSuggestion S, TokenCard Card, bool Print)> Resolve()
        {
            var list = new List<(TokenSuggestion S, TokenCard Card, bool Print)>();
            var done = new HashSet<TokenSuggestion>();
            foreach (var s in _items.Where(s => s.IsChecked))
            {
                var other = s.SharedWith;
                bool partnerTicked = other != null && other.IsChecked;
                if (other != null && partnerTicked && done.Contains(other))
                {
                    // The second half of a shared card.
                    list.Add((s, s.Token, false));
                }
                else list.Add((s, s.Token, true));
                done.Add(s);
            }
            return list;
        }

        private void SetAll(bool on)
        {
            foreach (var s in _items) s.IsChecked = on;
            TokenList.ItemsSource = null;          // plain objects: re-bind to show the change
            TokenList.ItemsSource = _items;
        }

        private void All_Click(object sender, RoutedEventArgs e) => SetAll(true);

        /// <summary>Only the tokens you own that the deck doesn't have yet.</summary>
        private void Owned_Click(object sender, RoutedEventArgs e)
        {
            foreach (var s in _items) s.IsChecked = s.Owned > 0 && s.InDeck == 0;
            TokenList.ItemsSource = null;
            TokenList.ItemsSource = _items;
        }
        private void None_Click(object sender, RoutedEventArgs e) => SetAll(false);

        /// <summary>Only the tokens you don't own (the ones to print).</summary>
        private void NotOwned_Click(object sender, RoutedEventArgs e)
        {
            foreach (var s in _items) s.IsChecked = s.Owned == 0;
            TokenList.ItemsSource = null;
            TokenList.ItemsSource = _items;
        }

        /// <summary>Paper slips of the ticked tokens, Copies of each. The window stays open (print, then add, or both).</summary>
        private void Print_Click(object sender, RoutedEventArgs e)
        {
            var ticked = Resolve();
            if (ticked.Count == 0) { ShowPrintStatus("Tick the tokens to print first."); return; }
            int copies = Math.Clamp(int.TryParse(QtyBox.Text, out int n) ? n : 1, 1, 99);
            bool oneSide = OneSideCheck.IsChecked == true;
            var slips = new List<Slip>();
            foreach (var (_, card, print) in ticked)
            {
                if (!print) continue;                  // the other half of a card already printed (both sides)
                var sides = Slip.WithOtherSides(card, oneSide);
                for (int c = 0; c < copies; c++) slips.AddRange(sides);
            }
            try
            {
                string done = SlipPrinter.Print(this, slips);
                if (done.Length > 0) ShowPrintStatus(done);
            }
            catch (Exception ex)
            {
                ShowPrintStatus($"Could not print: {ex.Message}");
            }
        }

        private void ShowPrintStatus(string text)
        {
            PrintStatus.Text = text;
            PrintStatus.Visibility = Visibility.Visible;
        }

        private void QtyBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            foreach (char c in e.Text)
                if (!char.IsDigit(c)) { e.Handled = true; return; }
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            var resolved = Resolve();
            if (resolved.Count == 0) return;               // nothing ticked: stay open
            foreach (var (sug, card, _) in resolved) sug.Token = card;   // two cards: the second half's own printing
            Picked = resolved.Select(r => r.S).ToList();
            Copies = Math.Clamp(int.TryParse(QtyBox.Text, out int n) ? n : 1, 1, 99);
            DialogResult = true;
        }
    }
}