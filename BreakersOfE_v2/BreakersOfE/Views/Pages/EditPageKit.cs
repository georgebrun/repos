using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using BreakersOfE.Models;
using BreakersOfE.Services;

namespace BreakersOfE.Views.Pages
{
    /// <summary>
    /// What the Edit pages (Pool → Collection, Decks, Trade Binder, Want List)
    /// share: the Qty box, the status line, confirm boxes, the in-place cell
    /// editor, price parsing, splitter memory and the page-hosting fixes.
    /// One copy here, so a fix applies to every Edit page.
    /// </summary>
    internal static class EditPageKit
    {
        // ── Reading row fields by name (rows are several model types) ────
        // The property lookup is done once per model type and name (these run
        // inside loops over whole tables).
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Type, string), System.Reflection.PropertyInfo?> _props = new();

        private static object? Read(object o, string prop) =>
            _props.GetOrAdd((o.GetType(), prop), k => k.Item1.GetProperty(k.Item2))?.GetValue(o);

        public static string Str(object o, string prop) => Read(o, prop) as string ?? "";

        public static int Int(object o, string prop) => Read(o, prop) is int i ? i : 0;

        public static bool Bool(object o, string prop) => Read(o, prop) is bool b && b;

        /// <summary>Menu text: at most 60 characters.</summary>
        public static string Shorten(string s) => s.Length <= 60 ? s : s[..57] + "…";

        // ── Status line, confirm box, Qty box ───────────────────────────
        /// <summary>The page's "what just happened" line — amber only when something needs attention (ISA-101).</summary>
        public static void ShowStatus(TextBlock target, string text, bool warning)
        {
            target.Text = text;
            if (warning) target.SetResourceReference(TextBlock.ForegroundProperty, "BoeWarningBrush");          // Settings → Appearance
            else target.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");   // follows Light / Dark
        }

        /// <summary>An OK / Cancel question over the page's window.</summary>
        public static bool Confirm(DependencyObject page, string text, string title) =>
            System.Windows.MessageBox.Show(Window.GetWindow(page), text, title,
                MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

        /// <summary>The Qty box as a number (1–999; anything else becomes 1), written back tidy.</summary>
        public static int ReadQty(TextBox box)
        {
            int q = int.TryParse(box.Text, out var n) ? n : 1;
            q = Math.Clamp(q, 1, 999);
            box.Text = q.ToString();
            return q;
        }

        /// <summary>A text box's PreviewTextInput: digits only.</summary>
        public static void DigitsOnly(TextCompositionEventArgs e)
        {
            foreach (char c in e.Text)
                if (!char.IsDigit(c)) { e.Handled = true; return; }
        }

        /// <summary>
        /// "2 Non-Foil, 1 Foil" — how many of the selected rows are each finish
        /// (finishes with none are left out).
        /// </summary>
        public static string FinishBreakdown(IEnumerable<string> finishes)
        {
            var list = finishes.Select(CardFinish.Normalize).ToList();
            var parts = new[] { CardFinish.NonFoil, CardFinish.Foil, CardFinish.Etched }
                .Select(f => (Finish: f, N: list.Count(x => x == f)))
                .Where(p => p.N > 0)
                .Select(p => $"{p.N} {CardFinish.Display(p.Finish)}");
            return string.Join(", ", parts);
        }

        /// <summary>
        /// The confirm text for adding to (or removing from) several rows that
        /// each keep their own finish: "You have selected 3 rows: 2 Non-Foil, 1 Foil. …"
        /// </summary>
        public static string OwnFinishQuestion(string verb, int qty, IReadOnlyCollection<string> finishes, string where) =>
            $"You have selected {finishes.Count} rows: {FinishBreakdown(finishes)}.\n\n" +
            $"{verb} {qty} {(qty == 1 ? "copy" : "copies")} {where} each row, in that row's own finish?";

        /// <summary>Too many rows for one change (Ctrl+A on the pool would be 100,000): says so and returns true.</summary>
        public static bool TooMany(int count, int max, Action<string, bool> showStatus)
        {
            if (count <= max) return false;
            showStatus($"{count:N0} rows are selected — select {max:N0} or fewer for one change.", true);
            return true;
        }

        /// <summary>
        /// Several results as one status line ("Added 2: 3 of 4 rows changed; 1 skipped — e.g. …").
        /// One result is returned as it is.
        /// </summary>
        public static EditResult Combine(string what, List<EditResult> results)
        {
            if (results.Count == 1) return results[0];
            int done = results.Count(r => r.Changed > 0);
            var problems = results.Where(r => r.Warning).ToList();             // refused or partly done
            int unchanged = results.Count(r => r.Changed <= 0 && !r.Warning);  // nothing to do
            string msg = $"{what}: {done} of {results.Count} rows changed";
            if (unchanged > 0) msg += $", {unchanged} already that way";
            msg += problems.Count > 0
                ? $"; {problems.Count} skipped or partly done — e.g. {problems[0].Message}"
                : ".";
            return new EditResult
            {
                Changed = results.Sum(r => r.Changed),
                Message = msg,
                Warning = problems.Count > 0,
                Touched = results.SelectMany(r => r.Touched).ToList(),
            };
        }

        /// <summary>
        /// A price typed in a cell ("4.50", "$4.50"); empty = none. False (with
        /// <paramref name="price"/> null) when it isn't a price of 0 or more.
        /// </summary>
        public static bool TryParsePrice(string text, out decimal? price)
        {
            price = null;
            string t = (text ?? "").Trim().Replace("$", "");
            if (t.Length == 0) return true;
            if (!decimal.TryParse(t, NumberStyles.Number, CultureInfo.CurrentCulture, out var p) || p < 0) return false;
            price = p;
            return true;
        }

        // ── In-place cell editor (double-click Qty / Notes / Asking / Offer …) ──
        /// <summary>
        /// A white box with dark text and a blue edge over a cell (same look on
        /// every row color). Enter or clicking away saves, Esc cancels. With no
        /// cell (the column is hidden) it opens at the mouse.
        /// </summary>
        public static void ShowCellEditor(DataGridCell? cell, string text, double minWidth,
                                          TextAlignment align, bool digitsOnly, string tip, Action<string> save)
        {
            // The plain WPF text box style on purpose: the app's Fluent style
            // repaints the background when focused, which made the text hard to see.
            var accent = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));
            accent.Freeze();
            var box = new TextBox
            {
                Style = new Style(typeof(TextBox)),
                Text = text,
                MinWidth = Math.Max(minWidth, cell?.ActualWidth ?? 0),
                MinHeight = 0,
                Height = Math.Max(30, cell?.ActualHeight ?? 0),      // room for the text, never cut off
                Padding = new Thickness(4, 0, 4, 0),
                Background = Brushes.White,
                Foreground = Brushes.Black,
                CaretBrush = Brushes.Black,
                BorderBrush = accent,
                BorderThickness = new Thickness(2),
                SelectionBrush = accent,
                SelectionOpacity = 0.35,
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                TextAlignment = align,
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = tip,
            };
            var popup = new Popup
            {
                PlacementTarget = cell,
                Placement = cell != null ? PlacementMode.Relative : PlacementMode.MousePoint,
                StaysOpen = false,
                AllowsTransparency = true,
                Child = box,
            };

            bool done = false;
            void Commit(bool keep)
            {
                if (done) return;
                done = true;
                popup.IsOpen = false;
                if (keep) save(box.Text);
            }

            if (digitsOnly) box.PreviewTextInput += (_, e) => DigitsOnly(e);
            box.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter || e.Key == Key.Return) { Commit(true); e.Handled = true; }
                else if (e.Key == Key.Escape) { Commit(false); e.Handled = true; }
            };
            popup.Closed += (_, _) => Commit(true);          // clicking away saves
            popup.Opened += (_, _) =>
            {
                box.Focus();
                Keyboard.Focus(box);
                box.SelectAll();
            };
            popup.IsOpen = true;
        }

        // ── Splitters: sizes are remembered (all Edit pages share them) ──
        private const string TopShareKey = "Edit:TopShare";
        private const string DetailWidthKey = "Edit:DetailWidth";

        public static void SaveTopShare(RowDefinition top, RowDefinition bottom)
        {
            double total = top.ActualHeight + bottom.ActualHeight;
            if (total > 0) GridLayoutService.SetNumber(TopShareKey, top.ActualHeight / total);
        }

        public static void SaveDetailWidth(ColumnDefinition detail) =>
            GridLayoutService.SetNumber(DetailWidthKey, detail.ActualWidth);

        public static void RestoreSplitters(RowDefinition top, RowDefinition bottom, ColumnDefinition detail)
        {
            if (GridLayoutService.GetNumber(TopShareKey) is double share && share > 0.05 && share < 0.95)
            {
                top.Height = new GridLength(share, GridUnitType.Star);
                bottom.Height = new GridLength(1 - share, GridUnitType.Star);
            }
            if (GridLayoutService.GetNumber(DetailWidthKey) is double w && w >= detail.MinWidth)
                detail.Width = new GridLength(Math.Min(w, detail.MaxWidth));
        }

        /// <summary>
        /// NavigationView wraps pages in a ScrollViewer → infinite height →
        /// virtualization defeated → freeze. Turn the host's scrolling off
        /// (same fix as PoolPage).
        /// </summary>
        public static void DisableHostScroll(DependencyObject page)
        {
            DependencyObject? current = page;
            while (current != null)
            {
                current = VisualTreeHelper.GetParent(current);
                if (current is ScrollViewer sv)
                {
                    sv.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                    sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                    break;
                }
            }
        }
    }

    /// <summary>A right-click menu item and when it's on; Qty items show "×Qty".</summary>
    internal sealed record EditMenuEntry(MenuItem Item, string Header, Func<bool> Enabled, bool ShowsQty);

    /// <summary>
    /// Ctrl+Z / Ctrl+Q from anywhere in the window while an Edit page is on
    /// screen: after a change the tables are re-read and the row that had the
    /// keyboard focus is gone, so focus falls back to the window — outside the
    /// page — and the first press would never reach it. Hooks itself to the
    /// page's Loaded / Unloaded; a dialog in front keeps its own keys.
    /// </summary>
    internal sealed class WindowKeyHook
    {
        private readonly FrameworkElement _page;
        private readonly KeyEventHandler _handler;
        private Window? _window;

        public WindowKeyHook(FrameworkElement page, KeyEventHandler handler)
        {
            _page = page;
            _handler = handler;
            page.Loaded += (_, _) => Attach();
            page.Unloaded += (_, _) => Detach();
        }

        private void Attach()
        {
            if (Window.GetWindow(_page) is not { } win || ReferenceEquals(win, _window)) return;
            Detach();
            _window = win;
            win.PreviewKeyDown += OnKey;
        }

        private void Detach()
        {
            if (_window != null) _window.PreviewKeyDown -= OnKey;
            _window = null;
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (!_page.IsLoaded || !_page.IsVisible || e.Handled) return;
            if (sender is Window w && !w.IsActive) return;
            _handler(sender, e);
        }
    }
}
