using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using BreakersOfE.ViewModels;
using Wpf.Ui.Controls;

namespace BreakersOfE.Views
{
    /// <summary>
    /// Set completion table: every set with owned / total printings, % complete,
    /// set value, and the value of what's missing. Read-only. Sort by any
    /// column; double-click a set to open it (the host decides how).
    /// </summary>
    public partial class SetCompletionWindow : FluentWindow
    {
        private readonly List<SetTile> _all;

        /// <summary>Double-click on a set.</summary>
        public event Action<SetTile>? SetOpened;

        public SetCompletionWindow(IEnumerable<SetTile> sets, Window? owner = null)
        {
            InitializeComponent();
            if (owner != null) Owner = owner;
            _all = sets.ToList();

            KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape) Close();
            };

            ShowRows();
        }

        private void ShowRows()
        {
            bool ownedOnly = ChkOwnedOnly.IsChecked == true;
            var rows = ownedOnly ? _all.Where(s => s.OwnedCount > 0).ToList() : _all;
            SetsGrid.ItemsSource = rows;

            // Default order: most complete first, then name.
            var view = CollectionViewSource.GetDefaultView(SetsGrid.ItemsSource);
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(nameof(SetTile.CompletionPercent), ListSortDirection.Descending));
            view.SortDescriptions.Add(new SortDescription(nameof(SetTile.Name), ListSortDirection.Ascending));

            int setsStarted = _all.Count(s => s.OwnedCount > 0);
            int setsComplete = _all.Count(s => s.CardCount > 0 && s.OwnedCount >= s.CardCount);
            int owned = _all.Sum(s => s.OwnedCount);
            SummaryText.Text =
                $"{owned:N0} printings owned across {setsStarted:N0} of {_all.Count:N0} sets · " +
                $"{setsComplete:N0} complete";
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void ChkOwnedOnly_Changed(object sender, RoutedEventArgs e)
        {
            if (IsLoaded) ShowRows();
        }

        private void SetsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (SetsGrid.SelectedItem is SetTile tile)
                SetOpened?.Invoke(tile);
        }
    }
}