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
    /// Collection → Tokens, Two-Sided… (or right-click → Other Side…): what
    /// this token row is paired with (Unpair), and the card pool's tokens of
    /// its release to pick the back from — with what you own of each, how
    /// much isn't paired, in use and paired with. Picking one you don't have
    /// (enough of) adds it to your collection first, through the page so
    /// Undo can take it back. <see cref="Changed"/> tells the page to reload.
    /// </summary>
    public partial class OtherSideWindow : FluentWindow
    {
        private readonly TokenCollectionEntry _row;
        private readonly Func<TokenCard, string, int, string, string, EditResult> _add;
        private List<TokenChoice> _all = new();
        private bool _loadedAll;

        /// <summary>A pair was added or removed, or copies were added.</summary>
        public bool Changed { get; private set; }

        public OtherSideWindow(Window? owner, TokenCollectionEntry row,
                               Func<TokenCard, string, int, string, string, EditResult> add)
        {
            InitializeComponent();
            if (owner != null) Owner = owner;
            _row = row;
            _add = add;
            WindowTitleBar.Title = Title = $"Other Side — {row.Name}";
            var (_, release) = CollectionEditService.ReleaseOfSet(row.SetCode);
            SameSetsCheck.Content = $"Only {release} (all its token sets)";
            KeyDown += (_, e) => { if (e.Key == Key.F1) HelpWindow.Open("two-sided-tokens"); };
            Load();
        }

        /// <summary>Read it all again: this row's pairs and the list (after a change).</summary>
        private void Load()
        {
            CollectionEditService.PairsChanged();
            var row = CollectionEditService.TokenRow(CollectionEditService.TokenKey(_row)) ?? _row;
            var key = CollectionEditService.TokenKey(row);
            int qty = row.Quantity;
            int free = CollectionEditService.UnpairedCopies(row);
            HeaderText.Text = $"{row.Name} ({row.SetCode.ToUpperInvariant()} #{row.CollectorNumber})";
            SubText.Text = $"{key.Text} · {qty} cop{(qty == 1 ? "y" : "ies")}, {free} not paired. " +
                           "Pick the token printed on the back of these cards — from your tokens or the card pool " +
                           "(one you don't have is added to your collection). Each side keeps its own row; " +
                           "a deck that takes one side takes the card.";

            var paired = CollectionEditService.OtherSidesOf(key);
            PairedGrid.ItemsSource = paired;
            PairedGrid.Visibility = BtnUnpair.Visibility = paired.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            NoPairText.Visibility = paired.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            if (paired.Count == 1) PairedGrid.SelectedIndex = 0;

            LoadChoices();
            if (free == 0)
                StatusText.Text = "Every copy of this row is paired already. Unpair one, or add copies first.";
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

        private int RowFree => CollectionEditService.UnpairedCopies(CollectionEditService.TokenRow(CollectionEditService.TokenKey(_row)) ?? _row);

        private void UpdatePair() =>
            BtnPair.IsEnabled = ChoiceGrid.SelectedItem is TokenChoice && RowFree > 0;

        private void ChoiceGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdatePair();
            if (ChoiceGrid.SelectedItem is not TokenChoice c) return;
            int most = RowFree;
            if (most <= 0) { StatusText.Text = "Every copy of this row is paired already."; return; }
            if (!int.TryParse(CountBox.Text, out int n) || n < 1 || n > most) CountBox.Text = "1";
            StatusText.Text = c.OffPair > 0
                ? $"Pair with your {c.Name} ({c.SetCode} #{c.Number}): {c.OffPair} not paired."
                : $"You have no {c.Name} ({c.SetCode} #{c.Number}) that isn't paired: Pair adds it to your collection, then pairs it.";
        }

        private void ChoiceGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (BtnPair.IsEnabled) Pair_Click(sender, e);
        }

        private void PairedGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
            BtnUnpair.IsEnabled = PairedGrid.SelectedItem is OtherSide;

        private void Pair_Click(object sender, RoutedEventArgs e)
        {
            if (ChoiceGrid.SelectedItem is not TokenChoice c) return;
            if (!int.TryParse(CountBox.Text, out int n) || n < 1)
            {
                StatusText.Text = "Enter how many cards (1 or more).";
                return;
            }
            var row = CollectionEditService.TokenRow(CollectionEditService.TokenKey(_row)) ?? _row;
            if (CollectionEditService.RowToPairWith(row, c.Card.ScryfallId, n) == null)
            {
                string fin = CollectionEditService.FinishToAdd(row, c.Card);
                if (System.Windows.MessageBox.Show(this,
                        $"Add {n} × {c.Name} ({c.SetCode} #{c.Number}) to your collection " +
                        $"({CardFinish.Display(fin)} · {CardLanguage.Normalize(row.Language)} · {CardCondition.Normalize(row.Condition)}) " +
                        $"and pair {(n == 1 ? "it" : "them")} with {row.Name}?\n\nUndo on the page takes the copies back.",
                        "Add the Other Side", System.Windows.MessageBoxButton.OKCancel,
                        System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.OK)
                    return;
            }
            var r = CollectionEditService.PairWithPrinting(row, c.Card, n, _add);
            if (r.Changed > 0 || r.Message.StartsWith("Added"))
            {
                Changed = true;
                Load();
            }
            StatusText.Text = r.Message;
        }

        private void Unpair_Click(object sender, RoutedEventArgs e)
        {
            if (PairedGrid.SelectedItem is not OtherSide s) return;
            if (System.Windows.MessageBox.Show(this,
                    $"Unpair {_row.Name} // {s.Name}?\n\nEach side becomes its own card again. Decks keep what they claimed.",
                    "Unpair", System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.OK)
                return;
            var r = CollectionEditService.Unpair(s.PairId);
            if (r.Changed > 0)
            {
                Changed = true;
                Load();
            }
            StatusText.Text = r.Message;
        }
    }
}
