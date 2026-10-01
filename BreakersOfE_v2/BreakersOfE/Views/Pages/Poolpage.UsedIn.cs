using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BreakersOfE.Services;

namespace BreakersOfE.Views.Pages
{
    // "Used in": which decks claim a collection row's copies (Edit → Decks
    // claims, collection.db). Part of PoolPage (same class).
    //
    //  • The Decks column's ▸ (rows with copies in use) opens a small table
    //    under the row: Deck · Type · Part · Copies. Double-click a deck to
    //    open it in Edit → Decks → Deck → Collection.
    //  • Hovering the Used cell lists the same decks.
    //
    // Rows start collapsed; a table is built only when opened, so big
    // collections stay fast (virtualized rows are recycled: which rows are
    // open is kept here and re-applied as rows are loaded).
    public partial class PoolPage
    {
        /// <summary>Collection rows whose "Used in" table is open.</summary>
        private readonly HashSet<object> _usedInOpen = new(ReferenceEqualityComparer.Instance);

        private void InitUsedIn()
        {
            // The table itself follows the row's card through a binding
            // (UsedInTableConverter), so a recycled row never shows another card's.
            PoolGrid.LoadingRow += (_, e) =>
                e.Row.DetailsVisibility = e.Row.Item != null && _usedInOpen.Contains(e.Row.Item)
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>▸ on a collection row: open or close its "Used in" table.</summary>
        private void UsedInToggle_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not { } item) return;
            bool open = !_usedInOpen.Contains(item);
            if (open) _usedInOpen.Add(item); else _usedInOpen.Remove(item);
            if (PoolGrid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow row)
            {
                row.DetailsVisibility = open ? Visibility.Visible : Visibility.Collapsed;
                // Opened again: re-read (claims may have changed meanwhile).
                if (open)
                {
                    row.UpdateLayout();
                    if (FindVisualChild<System.Windows.Controls.Primitives.DataGridDetailsPresenter>(row) is { } details &&
                        FindVisualChild<ContentControl>(details) is { } cc)
                        cc.GetBindingExpression(ContentControl.ContentProperty)?.UpdateTarget();
                }
            }
            e.Handled = true;
        }

        /// <summary>The "Used in" table for one collection row.</summary>
        internal static UIElement BuildUsedIn(object item)
        {
            var uses = CollectionEditService.UsesOfRow(item);
            var ink = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));
            var dim = new SolidColorBrush(Color.FromRgb(0x55, 0x5B, 0x66));
            var panel = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xF3, 0xF8)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0xC7, 0xD8)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 6, 10, 6),
            };

            if (uses.Count == 0)
            {
                panel.Child = new System.Windows.Controls.TextBlock
                {
                    Text = "No deck claims these copies.",
                    Foreground = dim,
                    FontStyle = FontStyles.Italic,
                };
                return panel;
            }

            var grid = new Grid();
            foreach (var w in new[] { 240.0, 170.0, 200.0, 60.0 })
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });

            void Cell(int r, int c, string text, bool header = false, RowUse? use = null)
            {
                var tb = new System.Windows.Controls.TextBlock
                {
                    Text = text,
                    Foreground = header ? dim : ink,
                    FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal,
                    FontSize = header ? 11 : 12,
                    Margin = new Thickness(0, 1, 12, 1),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = c == 3 ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                };
                if (use != null && use.DeckPath.Length > 0)
                {
                    tb.Cursor = Cursors.Hand;
                    tb.ToolTip = $"Double-click to open {use.DeckName} in Edit → Decks → Deck → Collection";
                    tb.MouseLeftButtonDown += (_, e) =>
                    {
                        if (e.ClickCount != 2) return;
                        e.Handled = true;
                        (Window.GetWindow(tb) as MainWindow)?.OpenDeckToCollection(use.DeckPath);
                    };
                }
                Grid.SetRow(tb, r);
                Grid.SetColumn(tb, c);
                grid.Children.Add(tb);
            }

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Cell(0, 0, "USED IN DECK", true);
            Cell(0, 1, "TYPE", true);
            Cell(0, 2, "PART", true);
            Cell(0, 3, "COPIES", true);
            int row = 1;
            foreach (var u in uses)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Cell(row, 0, u.DeckName, use: u);
                Cell(row, 1, u.DeckType, use: u);
                Cell(row, 2, u.Part, use: u);
                Cell(row, 3, u.Copies.ToString(), use: u);
                row++;
            }
            panel.Child = grid;
            return panel;
        }

        /// <summary>Hover the Used cell: the decks using the copies (nothing when none are used).</summary>
        private void UsedCell_ToolTipOpening(object sender, ToolTipEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not { } item ||
                item.GetType().GetProperty("UsedCount")?.GetValue(item) is not int used || used <= 0)
            {
                e.Handled = true;           // no tip
                return;
            }
            var uses = CollectionEditService.UsesOfRow(item);
            fe.ToolTip = uses.Count == 0
                ? $"{used} in use"
                : "In decks:\n" + string.Join("\n", uses.Select(u => u.Text));
        }
    }
}

namespace BreakersOfE.Views.Pages
{
    /// <summary>A collection row → its "Used in" table (the grid's row details).</summary>
    public sealed class UsedInTableConverter : System.Windows.Data.IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            value == null ? null : PoolPage.BuildUsedIn(value);

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
