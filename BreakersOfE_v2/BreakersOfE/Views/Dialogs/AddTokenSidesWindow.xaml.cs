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
    /// Adding a token on Edit → Pool → Collection → Tokens: the card pool's
    /// tokens of its release (all its token sets) to pick the other side
    /// from. Add to Collection: <see cref="OtherSide"/> is the pick (both go
    /// in, paired). Single-Sided: <see cref="Single"/>. Cancel: neither.
    /// The token it was paired with before comes picked.
    /// </summary>
    public partial class AddTokenSidesWindow : FluentWindow
    {
        private readonly string _sid, _setCode, _releasedAt;
        private List<TokenChoice> _all = new();
        private bool _loadedAll;

        /// <summary>The token on the back (Add to Collection), or null.</summary>
        public TokenCard? OtherSide { get; private set; }
        /// <summary>Single-Sided was picked.</summary>
        public bool Single { get; private set; }

        private AddTokenSidesWindow(Window? owner, object token, int copies, string rowText)
        {
            InitializeComponent();
            if (owner != null) Owner = owner;
            _sid = CardDetailText.Get(token, "ScryfallId");
            _setCode = CardDetailText.Get(token, "SetCode");
            _releasedAt = CardDetailText.Get(token, "ReleasedAt");
            string name = CardDetailText.Get(token, "Name");
            WindowTitleBar.Title = Title = $"Other Side — {name}";
            HeaderText.Text = $"Adding {copies} × {name} ({_setCode.ToUpperInvariant()} #{CardDetailText.Get(token, "CollectorNumber")}) · {rowText}";
            SameSetsCheck.Content = $"Only {CollectionEditService.ReleaseOfSet(_setCode).Name} (all its token sets)";
            KeyDown += (_, e) => { if (e.Key == Key.F1) HelpWindow.Open("two-sided-tokens"); };
            LoadChoices();

            // Adding more of a token paired before: that side comes picked.
            if (CollectionEditService.LastPartnerOf(_sid) is { } last &&
                ChoiceGrid.ItemsSource is List<TokenChoice> list &&
                list.FirstOrDefault(c => string.Equals(c.Card.ScryfallId, last, StringComparison.OrdinalIgnoreCase)) is { } prev)
            {
                ChoiceGrid.SelectedItem = prev;
                ChoiceGrid.ScrollIntoView(prev);
                StatusText.Text = $"Paired with {prev.Name} ({prev.SetCode} #{prev.Number}) before: Add to Collection (Enter) pairs these the same way.";
                BtnSingle.IsDefault = false;
                BtnAdd.IsDefault = true;
            }
        }

        /// <summary>
        /// Ask what's on the other side of a token being added. Null: cancelled.
        /// Otherwise the window (Single, or OtherSide).
        /// </summary>
        public static AddTokenSidesWindow? Ask(Window? owner, object token, int copies, string rowText)
        {
            var dlg = new AddTokenSidesWindow(owner, token, copies, rowText);
            return dlg.ShowDialog() == true ? dlg : null;
        }

        private void LoadChoices()
        {
            bool same = SameSetsCheck.IsChecked == true;
            try
            {
                _all = CollectionEditService.TokenChoices(_sid, _setCode, _releasedAt, same);
                _loadedAll = !same;
            }
            catch (Exception ex)
            {
                _all = new List<TokenChoice>();
                StatusText.Text = $"Couldn't read the card pool's tokens: {ex.Message}";
            }
            // Nothing of these in your collection yet: no Owned / Paired columns.
            bool any = _all.Any(c => c.Owned > 0);
            OwnedColumn.Visibility = PairedColumn.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string find = FindBox.Text.Trim();
            var keep = ChoiceGrid.SelectedItem as TokenChoice;
            var list = _all.Where(c =>
                    find.Length == 0 ||
                    c.Name.Contains(find, StringComparison.OrdinalIgnoreCase) ||
                    c.SetCode.Contains(find, StringComparison.OrdinalIgnoreCase) ||
                    c.Number.Equals(find, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.SetCode).ThenBy(c => c.NumberSort)
                .ToList();
            ChoiceGrid.ItemsSource = list;
            if (keep != null && list.Contains(keep)) ChoiceGrid.SelectedItem = keep;
            if (list.Count == 0)
                StatusText.Text = SameSetsCheck.IsChecked == true
                    ? "No tokens of this release match. Untick the release box to see every token."
                    : "No tokens match.";
            else if (StatusText.Text.StartsWith("No tokens"))
                StatusText.Text = "";
            BtnAdd.IsEnabled = ChoiceGrid.SelectedItem is TokenChoice;
        }

        private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilter();

        private void Release_Changed(object sender, RoutedEventArgs e)
        {
            if (SameSetsCheck.IsChecked == true || !_loadedAll) LoadChoices();
            else ApplyFilter();
        }

        private void ChoiceGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            BtnAdd.IsEnabled = ChoiceGrid.SelectedItem is TokenChoice;
            if (ChoiceGrid.SelectedItem is TokenChoice c)
                StatusText.Text = $"Other side: {c.Name} ({c.SetCode} #{c.Number}). Add to Collection adds both, paired.";
        }

        private void ChoiceGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ChoiceGrid.SelectedItem is TokenChoice) Add_Click(sender, e);
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            if (ChoiceGrid.SelectedItem is not TokenChoice c) return;
            OtherSide = c.Card;
            DialogResult = true;
        }

        private void Single_Click(object sender, RoutedEventArgs e)
        {
            Single = true;
            DialogResult = true;
        }
    }
}
