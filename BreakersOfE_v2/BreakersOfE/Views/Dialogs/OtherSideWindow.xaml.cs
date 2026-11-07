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
    /// Collection → Tokens, Two-Sided… (or right-click → Other Side…): make
    /// two-sided cards of this single token and the token on its back. The
    /// card pool's tokens of its release to pick the back from, with your free
    /// single copies of each. Pair makes the cards (one row of their own,
    /// "Officer // Treasure"): with your single copies of the back when you
    /// have them free, else from the pool. It goes through the page, so one
    /// Undo takes it back. <see cref="Changed"/> tells the page something changed.
    /// </summary>
    public partial class OtherSideWindow : FluentWindow
    {
        private readonly TokenCollectionEntry _row;
        private readonly Func<TokenCollectionEntry, TokenCard, int, EditResult> _pair;
        private List<TokenChoice> _all = new();
        private bool _loadedAll;

        /// <summary>Two-sided cards were made.</summary>
        public bool Changed { get; private set; }

        public OtherSideWindow(Window? owner, TokenCollectionEntry row,
                               Func<TokenCollectionEntry, TokenCard, int, EditResult> pair)
        {
            InitializeComponent();
            if (owner != null) Owner = owner;
            _row = row;
            _pair = pair;
            WindowTitleBar.Title = Title = $"Other Side — {row.Name}";
            var (_, release) = CollectionEditService.ReleaseOfSet(row.SetCode);
            SameSetsCheck.Content = $"Only {release} (all its token sets)";
            KeyDown += (_, e) => { if (e.Key == Key.F1) HelpWindow.Open("two-sided-tokens"); };
            Load();
        }

        /// <summary>Read it all again: this row's copies, its cards and the list (after a change).</summary>
        private void Load()
        {
            var live = CollectionEditService.TokenRow(CollectionEditService.TokenKey(_row));   // null: every copy became a card
            var row = live ?? _row;
            var key = CollectionEditService.TokenKey(row);
            int qty = live?.Quantity ?? 0;
            int free = live == null ? 0 : CollectionEditService.UnpairedCopies(live);
            HeaderText.Text = $"{row.Name} ({row.SetCode.ToUpperInvariant()} #{row.CollectorNumber})";
            SubText.Text = $"{key.Text} · {qty} single cop{(qty == 1 ? "y" : "ies")}, {free} free (not in decks). " +
                           "Pick the token printed on the back of these cards — from your tokens or the card pool. " +
                           "Pair makes them two-sided cards: one row of their own (\"Officer // Treasure\"), " +
                           "taken off this row and, when you have them free, off your single copies of the back.";

            var cards = CollectionEditService.PairRowsWith(row.ScryfallId);
            PairedText.Text = cards.Count == 0
                ? "No two-sided cards with this token yet."
                : "Your two-sided cards with it: " + string.Join(", ", cards.GroupBy(c => c.Name).Select(g => $"{g.Key} ×{g.Sum(c => c.Quantity)}")) +
                  ". (Right-click one in the collection → Unpair to take it apart.)";

            LoadChoices();
            if (free == 0)
                StatusText.Text = "No free single copies of this token: add copies, or free some from a deck.";
        }

        private void LoadChoices()
        {
            bool same = SameSetsCheck.IsChecked == true;
            try
            {
                _all = CollectionEditService.TokenChoices(_row, same);
                _loadedAll = !same;
            }
            catch (Exception ex)
            {
                _all = new List<TokenChoice>();
                StatusText.Text = $"Couldn't read the card pool's tokens: {ex.Message}";
            }
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string find = FindBox.Text.Trim();
            bool mine = MineCheck.IsChecked == true;
            var list = _all.Where(c =>
                    (!mine || c.OffPair > 0) &&
                    (find.Length == 0 ||
                     c.Name.Contains(find, StringComparison.OrdinalIgnoreCase) ||
                     c.SetCode.Contains(find, StringComparison.OrdinalIgnoreCase) ||
                     c.Number.Equals(find, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(c => c.OffPair > 0 ? 0 : c.Owned > 0 ? 1 : 2)          // yours not paired first, then yours, then the pool
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.SetCode).ThenBy(c => c.NumberSort)
                .ToList();
            ChoiceGrid.ItemsSource = list;
            if (list.Count == 0)
                StatusText.Text = SameSetsCheck.IsChecked == true
                    ? "No tokens of this release match. Untick the release box to see every token."
                    : "No tokens match.";
            else if (StatusText.Text.StartsWith("No tokens"))
                StatusText.Text = "";
            UpdatePair();
        }

        private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilter();

        private void Release_Changed(object sender, RoutedEventArgs e)
        {
            if (SameSetsCheck.IsChecked == true || !_loadedAll) LoadChoices();
            else ApplyFilter();
        }

        private int RowFree => CollectionEditService.TokenRow(CollectionEditService.TokenKey(_row)) is { } r ? CollectionEditService.UnpairedCopies(r) : 0;

        private void UpdatePair() =>
            BtnPair.IsEnabled = ChoiceGrid.SelectedItem is TokenChoice && RowFree > 0;

        private void ChoiceGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdatePair();
            if (ChoiceGrid.SelectedItem is not TokenChoice c) return;
            int most = RowFree;
            if (most <= 0) { StatusText.Text = "No free single copies of this token."; return; }
            if (!int.TryParse(CountBox.Text, out int n) || n < 1 || n > most) CountBox.Text = "1";
            StatusText.Text = c.OffPair > 0
                ? $"Pair with your single {c.Name} ({c.SetCode} #{c.Number}): {c.OffPair} free."
                : $"You have no free single {c.Name} ({c.SetCode} #{c.Number}): Pair makes the cards from this token and the pool's {c.Name}.";
        }

        private void ChoiceGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (BtnPair.IsEnabled) Pair_Click(sender, e);
        }

        private void Pair_Click(object sender, RoutedEventArgs e)
        {
            if (ChoiceGrid.SelectedItem is not TokenChoice c) return;
            if (!int.TryParse(CountBox.Text, out int n) || n < 1)
            {
                StatusText.Text = "Enter how many cards (1 or more).";
                return;
            }
            var row = CollectionEditService.TokenRow(CollectionEditService.TokenKey(_row)) ?? _row;
            if (CollectionEditService.RowToPairWith(row, c.Card.ScryfallId, n) == null &&
                System.Windows.MessageBox.Show(this,
                    $"You have no {n} free single {c.Name} ({c.SetCode} #{c.Number}) in {CardFinish.Display(row.Finish)}.\n\n" +
                    $"Make {n} two-sided card{(n == 1 ? "" : "s")} from {n} × {row.Name} and the pool's {c.Name}? " +
                    "(The token's copies become the cards; nothing else is added.)\n\nUndo on the page takes it back.",
                    "Two-Sided Cards", System.Windows.MessageBoxButton.OKCancel,
                    System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.OK)
                return;
            var r = _pair(row, c.Card, n);
            if (r.Changed > 0)
            {
                Changed = true;
                Load();
            }
            StatusText.Text = r.Message;
        }
    }
}
