using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using BreakersOfE.Filtering;
using BreakersOfE.ViewModels;


namespace BreakersOfE.Views.Pages
{
    // Column sets per table, saved layouts, column drag/auto-scroll, legality columns.
    // Part of PoolPage (split from Poolpage.xaml.cs; same class, no behavior change).
    public partial class PoolPage
    {
        // ══════════════════════════════════════════════════════════════════
        // COLUMN LAYOUT — per table (Cards, Tokens, …, Collection). Order,
        // visibility, and width are saved separately for each table in
        // GridLayouts.json. Drag a header to reorder, drag an edge to resize,
        // the Columns button to show/hide. Name can't be hidden.
        // ══════════════════════════════════════════════════════════════════
        private readonly Dictionary<string, (DataGridLength width, int index)> _defaultColumns = new();
        private string _layoutTable = "";     // table whose layout the grid shows now
        private bool _applyingLayout;         // true while code (not the user) moves columns
        private System.Windows.Threading.DispatcherTimer? _layoutSaveTimer;

        private void InitColumnLayout()
        {
            var widthDescriptor = DependencyPropertyDescriptor.FromProperty(
                DataGridColumn.WidthProperty, typeof(DataGridColumn));

            foreach (var col in PoolGrid.Columns)
            {
                // Default position = the column's order in the XAML. (DisplayIndex
                // is still -1 here: the grid hasn't assigned positions yet.)
                _defaultColumns[ColumnHeader(col)] = (col.Width, PoolGrid.Columns.IndexOf(col));
                widthDescriptor?.AddValueChanged(col, (_, _) => RequestColumnLayoutSave());
            }

            PoolGrid.ColumnReordered += (_, _) => RequestColumnLayoutSave();

            // Dragging a header toward the grid's left/right edge scrolls the table.
            PoolGrid.ColumnHeaderDragStarted += (_, _) => StartDragAutoScroll();
            PoolGrid.ColumnHeaderDragCompleted += (_, _) => StopDragAutoScroll();

            // Column virtualization discards off-screen headers, and discarding
            // the header being dragged cancels the drag. So while the mouse is
            // down on a header, build all columns; restore after the drop.
            PoolGrid.PreviewMouseLeftButtonDown += PoolGrid_HeaderPressCheck;
            PoolGrid.PreviewMouseLeftButtonUp += (_, _) => RestoreColumnVirtualization();

            // Don't lose a pending change when leaving the page or closing the app.
            Unloaded += (_, _) => SaveColumnLayoutNow();
            if (Application.Current != null)
                Application.Current.Exit += (_, _) => SaveColumnLayoutNow();
        }

        // ── Auto-scroll while dragging a column header ──────────────────
        private System.Windows.Threading.DispatcherTimer? _dragScrollTimer;
        private const double DragScrollEdge = 60;     // px from the edge where scrolling starts

        private void StartDragAutoScroll()
        {
            _dragScrollTimer ??= CreateDragScrollTimer();
            _dragScrollTimer.Start();
        }

        private void StopDragAutoScroll() => _dragScrollTimer?.Stop();

        private void PoolGrid_HeaderPressCheck(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject d) return;
            // A header press that could start a drag — not the funnel button
            // and not the resize grip.
            if (FindAncestor<System.Windows.Controls.Primitives.DataGridColumnHeader>(d) == null) return;
            if (FindAncestor<Button>(d) != null) return;
            if (FindAncestor<System.Windows.Controls.Primitives.Thumb>(d) != null) return;

            PoolGrid.EnableColumnVirtualization = false;
        }

        private void RestoreColumnVirtualization()
        {
            if (PoolGrid.EnableColumnVirtualization) return;
            // After the drop has been processed (mouse-up reaches the header
            // after this preview event), switch back to on-screen-only columns.
            Dispatcher.BeginInvoke(new Action(() => PoolGrid.EnableColumnVirtualization = true),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>Nearest ancestor of type T (works from text runs too).</summary>
        private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
        {
            while (d != null)
            {
                if (d is T t) return t;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(d)
                    : LogicalTreeHelper.GetParent(d);
            }
            return null;
        }

        private System.Windows.Threading.DispatcherTimer CreateDragScrollTimer()
        {
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(30)
            };
            timer.Tick += (_, _) =>
            {
                // Safety: the drag ended somewhere we didn't hear about.
                if (System.Windows.Input.Mouse.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
                {
                    timer.Stop();
                    RestoreColumnVirtualization();
                    return;
                }

                var sv = FindVisualChild<ScrollViewer>(PoolGrid);
                if (sv == null) return;

                double x = System.Windows.Input.Mouse.GetPosition(PoolGrid).X;
                double width = PoolGrid.ActualWidth;

                // Faster the closer to (or further past) the edge.
                double step = 0;
                if (x < DragScrollEdge)
                    step = -(4 + (DragScrollEdge - x) * 0.6);
                else if (x > width - DragScrollEdge)
                    step = 4 + (x - (width - DragScrollEdge)) * 0.6;

                if (step != 0)
                    sv.ScrollToHorizontalOffset(sv.HorizontalOffset + Math.Clamp(step, -80, 80));
            };
            return timer;
        }

        private static string ColumnHeader(DataGridColumn col) => col.Header?.ToString() ?? "";

        private DataGridColumn? FindColumn(string header) =>
            PoolGrid.Columns.FirstOrDefault(c => ColumnHeader(c) == header);

        /// <summary>Does this column exist for this kind of table?</summary>
        private static bool ColumnApplies(string header, TableKind kind) =>
            !ColumnKinds.TryGetValue(header, out var kinds) || Array.IndexOf(kinds, kind) >= 0;

        /// <summary>
        /// Show a table's layout: defaults first (XAML widths and order, plus the
        /// table's column set), then that table's saved order/visibility/width.
        /// </summary>
        private void ApplyColumnLayout(string table)
        {
            _applyingLayout = true;
            try
            {
                var kind = KindOf(table);

                // 1. Defaults. Set positions in ascending order so each
                //    assignment lands where intended.
                foreach (var kv in _defaultColumns.OrderBy(k => k.Value.index))
                {
                    var col = FindColumn(kv.Key);
                    if (col == null) continue;
                    col.Width = kv.Value.width;
                    col.DisplayIndex = kv.Value.index;
                    col.Visibility = DefaultHiddenColumns.Contains(kv.Key)   // defaults show every
                        ? Visibility.Collapsed : Visibility.Visible;          // column but the hidden-by-default ones
                }
                ApplyColumnSet(kind);                      // then hide what this table doesn't have

                // 2. This table's saved layout, if any.
                var saved = Services.GridLayoutService.Get(LayoutKey(table));
                if (saved != null)
                {
                    foreach (var cl in saved.OrderBy(c => c.DisplayIndex))
                    {
                        var col = FindColumn(cl.Header);
                        if (col == null) continue;       // column no longer exists

                        if (cl.Width > 0 && col.CanUserResize)
                            col.Width = new DataGridLength(cl.Width);
                        if (cl.DisplayIndex >= 0 && cl.DisplayIndex < PoolGrid.Columns.Count)
                            col.DisplayIndex = cl.DisplayIndex;

                        // Only user choices: a column this table doesn't have
                        // stays hidden, and Name always shows.
                        if (ColumnApplies(cl.Header, kind) && cl.Header != "Name")
                            col.Visibility = cl.Visible ? Visibility.Visible : Visibility.Collapsed;
                    }
                }

                _layoutTable = table;
            }
            finally
            {
                _applyingLayout = false;
            }

            // This table's grid zoom (saved with its layout key).
            ApplyZoom(Services.GridLayoutService.GetZoom(LayoutKey(table)), save: false);

            // Headers may be rebuilt: funnel colors must match the filters.
            Dispatcher.BeginInvoke(new Action(RefreshFunnelIcons),
                System.Windows.Threading.DispatcherPriority.Loaded);
            QueueTotalsSync();
        }

        /// <summary>Save shortly after the user stops dragging (one write, not hundreds).</summary>
        private void RequestColumnLayoutSave()
        {
            if (_applyingLayout || string.IsNullOrEmpty(_layoutTable)) return;
            _layoutSaveTimer ??= new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _layoutSaveTimer.Tick -= LayoutSaveTimer_Tick;
            _layoutSaveTimer.Tick += LayoutSaveTimer_Tick;
            _layoutSaveTimer.Stop();
            _layoutSaveTimer.Start();
        }

        private void LayoutSaveTimer_Tick(object? sender, EventArgs e) => SaveColumnLayoutNow();

        /// <summary>Write the current table's layout (all columns) right now.</summary>
        private void SaveColumnLayoutNow()
        {
            _layoutSaveTimer?.Stop();
            if (_applyingLayout || string.IsNullOrEmpty(_layoutTable)) return;

            var layout = PoolGrid.Columns.Select(c => new Services.ColumnLayout
            {
                Header = ColumnHeader(c),
                DisplayIndex = c.DisplayIndex,
                Visible = c.Visibility == Visibility.Visible,
                Width = c.Width.IsAbsolute ? c.Width.Value : c.ActualWidth,
            }).ToList();

            Services.GridLayoutService.Set(LayoutKey(_layoutTable), layout);
        }

        /// <summary>Columns button: checklist of this table's regular columns.</summary>
        private void BtnColumns_Click(object sender, RoutedEventArgs e) =>
            ShowColumnChecklist((FrameworkElement)sender, legality: false);

        /// <summary>Legality button: checklist of the format legality columns.</summary>
        private void BtnLegality_Click(object sender, RoutedEventArgs e) =>
            ShowColumnChecklist((FrameworkElement)sender, legality: true);

        /// <summary>
        /// A scrolling checklist under the button. Columns lists the regular
        /// columns (in their current order); Legality lists the formats (in
        /// LegalityInfo order) with Show all / Hide all. Both have Reset.
        /// Either way they're ordinary grid columns: drag to move them anywhere.
        /// </summary>
        private void ShowColumnChecklist(FrameworkElement anchor, bool legality)
        {
            if (string.IsNullOrEmpty(_layoutTable)) return;
            var kind = KindOf(_layoutTable);

            // Which columns this list covers
            IEnumerable<DataGridColumn> columns = legality
                ? Models.LegalityInfo.Formats
                    .Select(f => FindColumn(f.Header))
                    .OfType<DataGridColumn>()           // skips missing columns (no nulls)
                : PoolGrid.Columns
                    .OrderBy(c => c.DisplayIndex)
                    .Where(c => !LegalityHeaders.Contains(ColumnHeader(c)));
            columns = columns.Where(c => ColumnApplies(ColumnHeader(c), kind)).ToList();

            var list = new StackPanel { Margin = new Thickness(10, 8, 10, 8) };
            var boxes = new List<(CheckBox box, DataGridColumn col)>();

            foreach (var col in columns)
            {
                string header = ColumnHeader(col);
                var box = new CheckBox
                {
                    Content = header,
                    IsChecked = col.Visibility == Visibility.Visible,
                    IsEnabled = header != "Name",      // Name is always shown
                    Margin = new Thickness(0, 2, 0, 2),
                };
                var column = col;
                box.Click += (_, _) =>
                {
                    column.Visibility = box.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
                    AfterChecklistChange();
                };
                list.Children.Add(box);
                boxes.Add((box, col));
            }

            var scroll = new ScrollViewer
            {
                Content = list,
                MaxHeight = 460,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };

            var popup = new System.Windows.Controls.Primitives.Popup
            {
                PlacementTarget = anchor,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
            };

            var footer = new WrapPanel { Margin = new Thickness(10, 4, 10, 10) };
            if (legality)
            {
                footer.Children.Add(ChecklistButton("Show all", () => SetAll(true)));
                footer.Children.Add(ChecklistButton("Hide all", () => SetAll(false)));
            }
            footer.Children.Add(ChecklistButton("Reset to default", () =>
            {
                Services.GridLayoutService.Remove(LayoutKey(_layoutTable));
                ApplyColumnLayout(_layoutTable);
                popup.IsOpen = false;
            }));

            var root = new StackPanel();
            root.Children.Add(scroll);
            root.Children.Add(new Separator { Margin = new Thickness(0, 2, 0, 2) });
            root.Children.Add(footer);

            popup.Child = new Border
            {
                Child = root,
                MinWidth = 230,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Background = TryFindResource("SolidBackgroundFillColorBaseBrush") as Brush
                             ?? TryFindResource("ApplicationBackgroundBrush") as Brush
                             ?? new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)),
                BorderBrush = TryFindResource("ControlStrokeColorDefaultBrush") as Brush
                              ?? Brushes.Gray,
            };
            popup.IsOpen = true;

            void SetAll(bool visible)
            {
                foreach (var (box, col) in boxes)
                {
                    if (!box.IsEnabled) continue;
                    box.IsChecked = visible;
                    col.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                }
                AfterChecklistChange();
            }
        }

        private static Button ChecklistButton(string text, Action onClick)
        {
            var b = new Button { Content = text, Padding = new Thickness(10, 3, 10, 3),
                                 Margin = new Thickness(0, 0, 6, 0) };
            b.Click += (_, _) => onClick();
            return b;
        }

        private void AfterChecklistChange()
        {
            SaveColumnLayoutNow();
            Dispatcher.BeginInvoke(new Action(RefreshFunnelIcons),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // ── Legality columns ────────────────────────────────────────────
        /// <summary>
        /// One colored-chip column per format, inserted right after Rarity (v1's
        /// spot). Hidden until the Collection shows them; filterable (Legal /
        /// Ban / Res / No) and sortable by severity from the funnel popup.
        /// </summary>
        private void AddLegalityColumns()
        {
            var rarity = FindColumn("Rarity");
            int insertAt = rarity != null ? PoolGrid.Columns.IndexOf(rarity) + 1 : PoolGrid.Columns.Count;
            var headerTemplate = (DataTemplate)FindResource("FilterableHeader");

            foreach (var fmt in Models.LegalityInfo.Formats)
            {
                var col = new DataGridTemplateColumn
                {
                    Header = fmt.Header,
                    HeaderTemplate = headerTemplate,
                    Width = new DataGridLength(Math.Max(74, fmt.Header.Length * 8 + 50)),
                    CellTemplate = CreateLegalityCellTemplate(fmt.Key),
                    CanUserSort = false,               // sort from the funnel popup
                    Visibility = Visibility.Collapsed,
                };
                PoolGrid.Columns.Insert(insertAt++, col);
            }

            // Deck "Legal" column right after Name (v1's spot).
            var name = FindColumn("Name");
            int legalAt = name != null ? PoolGrid.Columns.IndexOf(name) + 1 : 0;
            PoolGrid.Columns.Insert(legalAt, new DataGridTemplateColumn
            {
                Header = DeckLegalHeader,
                HeaderTemplate = headerTemplate,
                Width = new DataGridLength(90),
                CellTemplate = CreateLegalityCellTemplate(Models.LegalityAccessor.DeckFormatKey),
                CanUserSort = false,               // sort from the funnel popup
                Visibility = Visibility.Collapsed,
            });
        }

        /// <summary>The colored status chip for one format (same look as v1).</summary>
        private static DataTemplate CreateLegalityCellTemplate(string formatKey)
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetValue(Border.MarginProperty, new Thickness(3, 1, 3, 1));
            border.SetValue(Border.PaddingProperty, new Thickness(6, 1, 6, 1));
            border.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            border.SetValue(Border.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.SetBinding(Border.BackgroundProperty,
                new Binding($"Legality[{formatKey}].Background"));

            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty,
                new Binding($"Legality[{formatKey}].Text"));
            text.SetBinding(TextBlock.ForegroundProperty,
                new Binding($"Legality[{formatKey}].Foreground"));
            text.SetValue(TextBlock.FontSizeProperty, 11.0);
            text.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            text.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);

            border.AppendChild(text);
            return new DataTemplate { VisualTree = border };
        }

        /// <summary>
        /// Open a popup window just under <paramref name="anchor"/>, shifted
        /// left/up as needed so it stays inside the main window (works when
        /// maximized, on any monitor, at any display scaling).
        /// </summary>
        private void PlaceInsideWindow(Window popup, FrameworkElement anchor)
        {
            var src = PresentationSource.FromVisual(anchor);
            Point ToDip(Point p) =>
                src?.CompositionTarget != null ? src.CompositionTarget.TransformFromDevice.Transform(p) : p;

            var pt = ToDip(anchor.PointToScreen(new Point(0, anchor.ActualHeight)));
            double left = pt.X, top = pt.Y;

            if (Window.GetWindow(this) is Window owner && owner.Content is FrameworkElement content)
            {
                var topLeft = ToDip(content.PointToScreen(new Point(0, 0)));
                double right = topLeft.X + content.ActualWidth;
                double bottom = topLeft.Y + content.ActualHeight;

                if (left + popup.Width > right) left = right - popup.Width;
                if (top + popup.Height > bottom) top = bottom - popup.Height;
                left = Math.Max(left, topLeft.X);
                top = Math.Max(top, topLeft.Y);
            }

            popup.Left = left;
            popup.Top = top;
        }
    }
}
