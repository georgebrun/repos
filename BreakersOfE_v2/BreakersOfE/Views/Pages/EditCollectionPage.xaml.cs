using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BreakersOfE.Models;
using BreakersOfE.Services;

namespace BreakersOfE.Views.Pages
{
    /// <summary>
    /// Edit → Pool → Collection. Two tables like v1: the pool table on top,
    /// the matching collection table below. Both are the same PoolPage the
    /// View section uses (embedded, with their own filters, layouts and zoom).
    ///
    /// Every add and remove — buttons, keys, right-click — goes through
    /// <see cref="DoAdd"/> / <see cref="DoRemove"/> → CollectionEditService,
    /// then <see cref="AfterEdit"/> refreshes both tables in one place.
    ///
    /// Keys: top table Enter = add default finish, Shift+Enter = foil,
    /// Ctrl+Enter = etched; bottom table Delete = remove Qty of that row's
    /// finish, Shift+Delete = remove all of that row's finish; Ctrl+Q = Qty box.
    /// </summary>
    public partial class EditCollectionPage : Page
    {
        private readonly PoolPage _top = new();
        private readonly PoolPage _bottom = new();
        private string _poolTag = "Cards";
        private string CollTag => CollectionEditService.CollectionTagFor(_poolTag);

        // The card the buttons act on: the one selected last, in either table.
        private object? _current;
        private object? _currentPool;      // its pool card (for adding), if found
        private bool _currentEtchedOnly;
        private FinishCounts _counts = new();

        private readonly ContextMenu _topMenu = new();
        private readonly ContextMenu _bottomMenu = new();

        public EditCollectionPage()
        {
            InitializeComponent();

            _top.SetEmbedded();
            _bottom.SetEmbedded();
            TopFrame.Content = _top;
            BottomFrame.Content = _bottom;

            _top.SelectedCardChanged += card => { if (card != null) SetCurrent(card); };
            _bottom.SelectedCardChanged += card => { if (card != null) SetCurrent(card); };

            _top.GridPreviewKeyDown += Top_GridPreviewKeyDown;
            _bottom.GridPreviewKeyDown += Bottom_GridPreviewKeyDown;

            BuildMenus();

            // Double-click the Qty cell of a collection row to type a new quantity.
            _bottom.CellDoubleClickHandler = (row, header, cell) =>
            {
                if (header != "Qty") return false;
                EditQuantityInPlace(row, cell);
                return true;
            };
            UpdateButtons();
        }

        /// <summary>Pool table tag → its collection table tag.</summary>
        public static string CollectionTagFor(string poolTag) => CollectionEditService.CollectionTagFor(poolTag);

        private static string DisplayName(string poolTag) => poolTag switch
        {
            "ArtSeries" => "Art Series",
            "" => "Cards",
            _ => poolTag,
        };

        /// <summary>Show one pool table and its collection table (e.g. "Tokens").</summary>
        public void LoadPair(string poolTag)
        {
            if (string.IsNullOrEmpty(poolTag)) poolTag = "Cards";
            _poolTag = poolTag;
            _current = null;
            _currentPool = null;
            Detail.ShowCard(null);
            _top.LoadPool(poolTag);
            _bottom.LoadPool(CollTag);
            UpdateButtons();
            ShowStatus("", false);
        }

        // ══════════════════════════════════════════════════════════════════
        // CURRENT CARD
        // ══════════════════════════════════════════════════════════════════
        private void SetCurrent(object card)
        {
            _current = card;
            Detail.ShowCard(card);

            string sid = Str(card, "ScryfallId");
            // Adding needs the pool printing: the card itself (top table) or
            // looked up by its ScryfallId (a collection row, bottom table).
            _currentPool = card is IOwnedCard ? card : CollectionEditService.FindPoolCard(CollTag, sid);
            if (_currentPool != null)
            {
                var (_, foil, etched) = CollectionEditService.FinishesOf(_currentPool);
                _currentEtchedOnly = etched && !foil;
            }
            else
            {
                _currentEtchedOnly = card is IFinishRow && Bool(card, "PrintingEtchedOnly");
            }
            RefreshCounts();
        }

        private void RefreshCounts()
        {
            _counts = _current == null
                ? new FinishCounts()
                : CollectionEditService.Counts(CollTag, Str(_current, "ScryfallId"), _currentEtchedOnly);
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            var (nf, f, e) = _currentPool != null
                ? CollectionEditService.FinishesOf(_currentPool) : (false, false, false);
            BtnAddNonFoil.IsEnabled = nf;
            BtnAddFoil.IsEnabled = f;
            BtnAddEtched.IsEnabled = e;
            BtnRemoveNonFoil.IsEnabled = _counts.NonFoil > 0;
            BtnRemoveFoil.IsEnabled = _counts.Foil > 0;
            BtnRemoveEtched.IsEnabled = _counts.Etched > 0;
            BtnRemoveAll.IsEnabled = _counts.Total > 0;

            ModeText.Text = _current == null
                ? $"Pool → Collection · {DisplayName(_poolTag)}. Select a card in either table."
                : $"{CardText(_current)}  ·  you own {OwnedText()}";
        }

        private string OwnedText()
        {
            if (_counts.Total == 0) return "none";
            var parts = new System.Collections.Generic.List<string>();
            if (_counts.NonFoil > 0) parts.Add($"{_counts.NonFoil} Non-Foil");
            if (_counts.Foil > 0) parts.Add($"{_counts.Foil} Foil");
            if (_counts.Etched > 0) parts.Add($"{_counts.Etched} Etched");
            int used = _counts.UsedNonFoil + _counts.UsedFoil + _counts.UsedEtched;
            return string.Join(", ", parts) + (used > 0 ? $"  ({used} in use by decks)" : "");
        }

        // ══════════════════════════════════════════════════════════════════
        // ACTIONS — every gesture ends up here
        // ══════════════════════════════════════════════════════════════════
        private int Qty
        {
            get
            {
                int q = int.TryParse(QtyBox.Text, out var n) ? n : 1;
                q = Math.Clamp(q, 1, 999);
                QtyBox.Text = q.ToString();
                return q;
            }
        }

        /// <summary>Default finish for Enter: non-foil, else foil, else etched.</summary>
        private string DefaultFinish()
        {
            if (_currentPool == null) return CardFinish.NonFoil;
            var (nf, f, _) = CollectionEditService.FinishesOf(_currentPool);
            return nf ? CardFinish.NonFoil : f ? CardFinish.Foil : CardFinish.Etched;
        }

        private void DoAdd(string finish)
        {
            if (_currentPool == null)
            {
                ShowStatus("Select a card first (this printing isn't in the card pool).", true);
                return;
            }
            var result = CollectionEditService.Add(CollTag, _currentPool, finish, Qty);
            AfterEdit(result, Str(_currentPool, "ScryfallId"), finish);
        }

        private void DoRemove(string finish, bool all)
        {
            if (_current == null) { ShowStatus("Select a card first.", true); return; }
            string sid = Str(_current, "ScryfallId");
            var result = CollectionEditService.Remove(CollTag, sid, Str(_current, "Name"), finish,
                all ? int.MaxValue : Qty, _currentEtchedOnly);
            AfterEdit(result, sid, finish);
        }

        private void DoRemoveAllFinishes()
        {
            if (_current == null) { ShowStatus("Select a card first.", true); return; }
            int free = _counts.Total - (_counts.UsedNonFoil + _counts.UsedFoil + _counts.UsedEtched);
            if (free <= 0)
            {
                ShowStatus($"{CardText(_current)}: every copy is in use by decks — nothing removed.", true);
                return;
            }
            var answer = System.Windows.MessageBox.Show(Window.GetWindow(this),
                $"Remove all {free} unused {(free == 1 ? "copy" : "copies")} of {CardText(_current)}?\n\n" +
                "Copies used by decks or the Trade Binder are kept.",
                "Remove All", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK) return;

            string sid = Str(_current, "ScryfallId");
            string name = Str(_current, "Name");
            int removed = 0;
            foreach (var finish in new[] { CardFinish.NonFoil, CardFinish.Foil, CardFinish.Etched })
            {
                if (_counts.Owned(finish) == 0) continue;
                removed += CollectionEditService.Remove(CollTag, sid, name, finish, int.MaxValue, _currentEtchedOnly).Changed;
            }
            AfterEdit(new EditResult { Changed = removed, Message = $"Removed {removed} × {name} (all unused copies)." },
                      sid, null);
        }

        /// <summary>
        /// One place for everything an edit changes on screen: the status line,
        /// the pool row's Owned count (live), the collection table (reloaded,
        /// same filters/sort, the edited row re-selected), buttons and counts.
        /// </summary>
        private void AfterEdit(EditResult result, string sid, string? finish)
        {
            ShowStatus(result.Message, result.Warning);
            if (result.Changed <= 0) { RefreshCounts(); return; }

            // Pool row(s) for this printing: Owned updates in place.
            if (_top.FindLoaded(r => Str(r, "ScryfallId") == sid) is IOwnedCard poolRow)
                OwnedCountService.Fill(_poolTag, new object[] { poolRow });

            // Collection table: re-read and re-select the edited row (or any
            // row of the same printing if that one is gone).
            _bottom.ReloadRows(r =>
                Str(r, "ScryfallId") == sid &&
                (finish == null || r is not IFinishRow ||
                 Str(r, "ShownFinish") == finish || Str(r, "Finish") == finish));

            RefreshCounts();
        }

        private void ShowStatus(string text, bool warning)
        {
            ActionText.Text = text;
            // ISA-101: color only for something that needs attention.
            ActionText.Foreground = warning
                ? new SolidColorBrush(Color.FromRgb(0xE8, 0xA3, 0x17))
                : (Brush)FindResource("TextFillColorSecondaryBrush");
        }

        // ── Buttons ─────────────────────────────────────────────────────
        private void BtnAddNonFoil_Click(object sender, RoutedEventArgs e) => DoAdd(CardFinish.NonFoil);
        private void BtnAddFoil_Click(object sender, RoutedEventArgs e) => DoAdd(CardFinish.Foil);
        private void BtnAddEtched_Click(object sender, RoutedEventArgs e) => DoAdd(CardFinish.Etched);
        private void BtnRemoveNonFoil_Click(object sender, RoutedEventArgs e) => DoRemove(CardFinish.NonFoil, all: false);
        private void BtnRemoveFoil_Click(object sender, RoutedEventArgs e) => DoRemove(CardFinish.Foil, all: false);
        private void BtnRemoveEtched_Click(object sender, RoutedEventArgs e) => DoRemove(CardFinish.Etched, all: false);
        private void BtnRemoveAll_Click(object sender, RoutedEventArgs e) => DoRemoveAllFinishes();

        // ── Right-click menus (same actions as the buttons) ─────────────
        private void BuildMenus()
        {
            MenuItem Item(string header, string keys, Action act)
            {
                var mi = new MenuItem { Header = header, InputGestureText = keys };
                mi.Click += (_, _) => act();
                return mi;
            }

            _topMenu.Items.Add(Item("Add Non-Foil", "Enter", () => DoAdd(CardFinish.NonFoil)));
            _topMenu.Items.Add(Item("Add Foil", "Shift+Enter", () => DoAdd(CardFinish.Foil)));
            _topMenu.Items.Add(Item("Add Etched", "Ctrl+Enter", () => DoAdd(CardFinish.Etched)));
            _topMenu.Items.Add(new Separator());
            _topMenu.Items.Add(Item("Remove Non-Foil", "", () => DoRemove(CardFinish.NonFoil, false)));
            _topMenu.Items.Add(Item("Remove Foil", "", () => DoRemove(CardFinish.Foil, false)));
            _topMenu.Items.Add(Item("Remove Etched", "", () => DoRemove(CardFinish.Etched, false)));
            _topMenu.Opened += (_, _) => UpdateMenu(_topMenu);

            _bottomMenu.Items.Add(Item("Add Non-Foil", "", () => DoAdd(CardFinish.NonFoil)));
            _bottomMenu.Items.Add(Item("Add Foil", "", () => DoAdd(CardFinish.Foil)));
            _bottomMenu.Items.Add(Item("Add Etched", "", () => DoAdd(CardFinish.Etched)));
            _bottomMenu.Items.Add(new Separator());
            _bottomMenu.Items.Add(Item("Remove Non-Foil", "", () => DoRemove(CardFinish.NonFoil, false)));
            _bottomMenu.Items.Add(Item("Remove Foil", "", () => DoRemove(CardFinish.Foil, false)));
            _bottomMenu.Items.Add(Item("Remove Etched", "", () => DoRemove(CardFinish.Etched, false)));
            _bottomMenu.Items.Add(new Separator());
            _bottomMenu.Items.Add(Item("Remove Qty of this row", "Delete", () => RemoveRow(all: false)));
            _bottomMenu.Items.Add(Item("Remove all of this row", "Shift+Delete", () => RemoveRow(all: true)));
            _bottomMenu.Items.Add(Item("Remove All (every finish)", "", DoRemoveAllFinishes));
            _bottomMenu.Opened += (_, _) => UpdateMenu(_bottomMenu);

            _top.SetRowContextMenu(_topMenu);
            _bottom.SetRowContextMenu(_bottomMenu);
        }

        /// <summary>Menu items follow the buttons (on/off) and show the Qty.</summary>
        private void UpdateMenu(ContextMenu menu)
        {
            int q = Qty;
            foreach (var o in menu.Items)
            {
                if (o is not MenuItem mi) continue;
                string h = (mi.Header as string ?? "").Split(" ×")[0];
                bool qtyAction = h.StartsWith("Add ") || (h.StartsWith("Remove ") && h != "Remove all of this row" && !h.StartsWith("Remove All"));
                mi.Header = qtyAction && q > 1 ? $"{h} ×{q}" : h;
                mi.IsEnabled = h switch
                {
                    "Add Non-Foil" => BtnAddNonFoil.IsEnabled,
                    "Add Foil" => BtnAddFoil.IsEnabled,
                    "Add Etched" => BtnAddEtched.IsEnabled,
                    "Remove Non-Foil" => BtnRemoveNonFoil.IsEnabled,
                    "Remove Foil" => BtnRemoveFoil.IsEnabled,
                    "Remove Etched" => BtnRemoveEtched.IsEnabled,
                    _ => BtnRemoveAll.IsEnabled,
                };
            }
        }

        /// <summary>Bottom table: remove from the selected row's own finish.</summary>
        private void RemoveRow(bool all)
        {
            if (_bottom.SelectedCard is not { } row) return;
            SetCurrent(row);
            string finish = Str(row, "ShownFinish") is { Length: > 0 } shown ? shown : Str(row, "Finish");
            if (all)
            {
                int n = _counts.Owned(finish);
                var answer = System.Windows.MessageBox.Show(Window.GetWindow(this),
                    $"Remove all unused {CardFinish.Display(finish)} copies of {CardText(row)} ({n} owned)?\n\n" +
                    "Copies used by decks or the Trade Binder are kept.",
                    "Remove all of this row", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                if (answer != MessageBoxResult.OK) return;
            }
            DoRemove(finish, all);
        }

        // ── Qty cell: double-click → type the new quantity ──────────────
        /// <summary>
        /// A small box over the Qty cell: Enter or clicking away sets the row's
        /// quantity (never below the copies in use), Esc cancels.
        /// </summary>
        private void EditQuantityInPlace(object row, DataGridCell cell)
        {
            SetCurrent(row);
            string finish = Str(row, "ShownFinish") is { Length: > 0 } shown ? shown : Str(row, "Finish");
            int current = row.GetType().GetProperty("Quantity")?.GetValue(row) is int q ? q : 0;

            // Same look on every table (dark grid, white rows, pink/blue
            // status rows): a white box, dark bold digits, a blue edge, and a
            // light-blue selection the digits still read through. The plain
            // WPF text box style is used on purpose — the app's Fluent style
            // repaints the background when focused, which is what made the
            // number hard to see.
            var accent = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4));
            accent.Freeze();
            var box = new TextBox
            {
                Style = new Style(typeof(TextBox)),
                Text = current.ToString(),
                MinWidth = Math.Max(48, cell.ActualWidth),
                // Room for the text so the digits are never cut off.
                MinHeight = 0,
                Height = Math.Max(30, cell.ActualHeight),
                Padding = new Thickness(4, 0, 4, 0),
                Background = System.Windows.Media.Brushes.White,
                Foreground = System.Windows.Media.Brushes.Black,
                CaretBrush = System.Windows.Media.Brushes.Black,
                BorderBrush = accent,
                BorderThickness = new Thickness(2),
                SelectionBrush = accent,
                SelectionOpacity = 0.35,
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                TextAlignment = TextAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "New quantity — Enter to save, Esc to cancel",
            };
            var popup = new System.Windows.Controls.Primitives.Popup
            {
                PlacementTarget = cell,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Relative,
                StaysOpen = false,
                AllowsTransparency = true,
                Child = box,
            };

            bool done = false;
            void Commit(bool save)
            {
                if (done) return;
                done = true;
                popup.IsOpen = false;
                if (!save) return;
                if (!int.TryParse(box.Text, out int n) || n < 0)
                {
                    ShowStatus("Enter a whole number (0 or more).", true);
                    return;
                }
                if (n == current) return;
                var result = CollectionEditService.SetQuantity(CollTag, Str(row, "ScryfallId"),
                    Str(row, "Name"), finish, n, _currentEtchedOnly);
                AfterEdit(result, Str(row, "ScryfallId"), finish);
            }

            box.PreviewTextInput += (_, e) =>
            {
                foreach (char c in e.Text)
                    if (!char.IsDigit(c)) { e.Handled = true; return; }
            };
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

        // ── Keys ────────────────────────────────────────────────────────
        private void Top_GridPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter && e.Key != Key.Return) return;
            if (_top.SelectedCard is { } card) SetCurrent(card);
            var mods = Keyboard.Modifiers;
            if (mods.HasFlag(ModifierKeys.Control)) DoAdd(CardFinish.Etched);
            else if (mods.HasFlag(ModifierKeys.Shift)) DoAdd(CardFinish.Foil);
            else DoAdd(DefaultFinish());
            e.Handled = true;           // don't let Enter move to the next row
        }

        private void Bottom_GridPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Delete) return;
            RemoveRow(all: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
        }

        private void Page_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Q && Keyboard.Modifiers == ModifierKeys.Control)
            {
                QtyBox.Focus();
                e.Handled = true;
            }
        }

        // ── Qty box: digits only; Enter adds the default finish ──────────
        private void QtyBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            foreach (char c in e.Text)
                if (!char.IsDigit(c)) { e.Handled = true; return; }
        }

        private void QtyBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                DoAdd(DefaultFinish());
                e.Handled = true;
            }
        }

        private void QtyBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
            QtyBox.SelectAll();

        // ── Helpers ─────────────────────────────────────────────────────
        private static string Str(object o, string prop) =>
            o.GetType().GetProperty(prop)?.GetValue(o) as string ?? "";

        private static bool Bool(object o, string prop) =>
            o.GetType().GetProperty(prop)?.GetValue(o) is bool b && b;

        private static string CardText(object card) =>
            $"{Str(card, "Name")} ({Str(card, "SetCode").ToUpperInvariant()} #{Str(card, "CollectorNumber")})";

        // NavigationView wraps pages in a ScrollViewer → infinite height →
        // virtualization defeated → freeze. Same fix as PoolPage.
        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            DependencyObject current = this;
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
}