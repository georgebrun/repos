using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BreakersOfE.Models;
using BreakersOfE.Services;
using static BreakersOfE.Views.Pages.EditPageKit;

namespace BreakersOfE.Views.Pages
{
    /// <summary>
    /// Edit → Lists → Collection → Trade Binder (cards only). Your card
    /// collection on top, the Trade Binder below — the same tables the View
    /// section uses (embedded, with their own filters, layouts and zoom).
    ///
    /// Binder copies come from one exact collection row (printing, finish,
    /// language, condition) and count as Used there, so no deck can take
    /// them. Trade Value = market price × Trade % (a setting, 70% to start).
    ///
    /// Every change goes through <see cref="Run"/>: snapshot for Undo →
    /// CollectionEditService → <see cref="AfterEdit"/>, which refreshes both tables.
    ///
    /// Keys: top table Enter = add to the binder; bottom table Delete =
    /// remove Qty from the binder, Shift+Delete = all of the selected rows;
    /// Ctrl+Q = Qty box; Ctrl+Z = Undo. Double-click Asking or Notes in the
    /// binder to change them.
    /// </summary>
    public partial class EditTradeBinderPage : Page
    {
        private readonly PoolPage _top = new();       // card collection
        private readonly PoolPage _bottom = new();    // Trade Binder

        private PoolPage? _active;
        private object? _current;

        private readonly ContextMenu _topMenu = new();
        private readonly ContextMenu _bottomMenu = new();

        private const int UndoSteps = 20;
        private readonly List<(ClaimSnapshot Snap, string Text)> _undo = new();

        public EditTradeBinderPage()
        {
            InitializeComponent();
            new WindowKeyHook(this, Page_PreviewKeyDown);   // Ctrl+Z / Ctrl+Q from anywhere in the window

            _top.SetEmbedded(galleryByDefault: false);
            _bottom.SetEmbedded(galleryByDefault: false);
            TopFrame.Content = _top;
            BottomFrame.Content = _bottom;

            _top.SelectedCardChanged += card => OnSelected(_top, card);
            _bottom.SelectedCardChanged += card => OnSelected(_bottom, card);

            // Gallery tiles: + puts Qty in the binder, − takes Qty out.
            _top.GalleryAdd += (card, _) => { _active = _top; SetCurrent(card); DoAdd(_top); };
            _top.GalleryRemove += (card, _) => { _active = _top; SetCurrent(card); DoRemove(_top, all: false); };
            _bottom.GalleryAdd += (card, _) => { _active = _bottom; SetCurrent(card); DoAdd(_bottom); };
            _bottom.GalleryRemove += (card, _) => { _active = _bottom; SetCurrent(card); DoRemove(_bottom, all: false); };

            // A table re-read after an edit: its rows are new objects — the
            // card shown is the row now selected there (none if it's gone).
            foreach (var t in new[] { _top, _bottom })
            {
                var table = t;
                table.ItemsReloaded += () =>
                {
                    if (table != _active) return;
                    _current = table.SelectedCard;
                    UpdateButtons();
                };
            }

            _top.GridPreviewKeyDown += Top_GridPreviewKeyDown;
            _bottom.GridPreviewKeyDown += Bottom_GridPreviewKeyDown;

            BuildMenus();

            // Double-click Asking or Notes on a binder row to change it in place.
            _bottom.CellDoubleClickHandler = (row, header, cell) =>
            {
                if (row is not TradeBinderEntry) return false;
                switch (header)
                {
                    case "Asking": EditAskingInPlace(row, cell); return true;
                    case "Notes": EditNotesInPlace(row, cell); return true;
                    default: return false;
                }
            };

            ShowTradePercent();
            UpdateButtons();
            UpdateUndo();
        }

        /// <summary>Open the page: the card collection and the Trade Binder.</summary>
        public void Start()
        {
            _active = null;
            _current = null;
            Detail.ShowCard(null);
            ShowTradePercent();
            _top.LoadPool(CollectionEditService.CardsTable);
            _bottom.LoadPool(CollectionEditService.BinderTable);
            UpdateButtons();
            ShowStatus("", false);
        }

        private void ShowTradePercent() =>
            TradePercentText.Text = $"Trade Value = market × {Math.Clamp(AppSettingsService.Current.TradePercent, 1, 100)}% (Trade %, a setting)";

        // ══════════════════════════════════════════════════════════════════
        // CURRENT CARD
        // ══════════════════════════════════════════════════════════════════
        private void OnSelected(PoolPage table, object? card)
        {
            if (card == null) return;
            _active = table;
            SetCurrent(card);
        }

        private void SetCurrent(object card)
        {
            _current = card;
            Detail.ShowCard(card);
            UpdateButtons();
        }

        private static bool IsBinderRow(object r) => r is TradeBinderEntry;
        private static bool IsCollectionRow(object r) => r is CollectionEntry;

        private void UpdateButtons()
        {
            int selected = _active?.SelectedCount ?? 0;
            bool many = selected > 1;
            bool top = _active == _top;
            int qty = _current == null ? 0 : Int(_current, "Quantity");
            int free = _current != null && IsCollectionRow(_current) ? Int(_current, "AvailableCount") : 0;

            // Add: a collection row with free copies, or a binder row (more of the same row).
            BtnAdd.IsEnabled = many || (_current != null && (IsBinderRow(_current) || free > 0));
            // Remove / Traded: a binder row, or a collection row that has copies in the binder.
            BtnRemove.IsEnabled = BtnTraded.IsEnabled =
                many || (_current != null && (IsBinderRow(_current) || Int(_current, "UsedCount") > 0));

            if (_current == null)
                ModeText.Text = "Collection → Trade Binder · Cards. Select a card in either table " +
                                "(Ctrl+click or Shift+click for several).";
            else if (many)
                ModeText.Text = $"{selected} rows selected in the {(top ? "collection" : "Trade Binder")} table.";
            else if (IsBinderRow(_current))
            {
                var b = (TradeBinderEntry)_current;
                string asking = b.AskingPrice.HasValue ? $"asking ${b.AskingPrice.Value:F2}" : "no asking price (market applies)";
                ModeText.Text = $"{CardText(_current)} · {KeyOf(_current).Text}  ·  {qty} in the binder  ·  " +
                                $"market {b.PriceDisplay}, trade value {b.TradeValueDisplay}, {asking}";
            }
            else
                ModeText.Text = $"{CardText(_current)} · {KeyOf(_current).Text}  ·  this row: {qty} owned, {free} free" +
                                (Int(_current, "UsedCount") > 0 ? $", {Int(_current, "UsedCount")} in use (decks or the binder)" : "");
        }

        // ══════════════════════════════════════════════════════════════════
        // ACTIONS — every gesture ends up here
        // ══════════════════════════════════════════════════════════════════
        private int Qty => EditPageKit.ReadQty(QtyBox);

        private const int MaxRows = 1000;

        /// <summary>The rows an action works on: the selected rows of that table.</summary>
        private List<object> Targets(PoolPage? table)
        {
            table ??= _active;
            if (table != null)
            {
                var rows = table.SelectedCards.Where(r => IsBinderRow(r) || IsCollectionRow(r)).ToList();
                if (rows.Count > 0) return rows;
            }
            if (_current == null) return new List<object>();
            bool fits = table == null || (table == _bottom) == IsBinderRow(_current);
            return fits ? new List<object> { _current } : new List<object>();
        }

        private bool TooMany(List<object> rows) => EditPageKit.TooMany(rows.Count, MaxRows, ShowStatus);

        /// <summary>Each row's key once (two selected rows of the same key act once).</summary>
        private static List<(RowKey Key, string Name)> KeysOf(IEnumerable<object> rows)
        {
            var list = new List<(RowKey, string)>();
            var seen = new HashSet<RowKey>();
            foreach (var r in rows)
            {
                var k = KeyOf(r);
                if (seen.Add(k)) list.Add((k, Str(r, "Name")));
            }
            return list;
        }

        /// <summary>Snapshot (for Undo) → do the work → one status line → refresh.</summary>
        private void Run(string what, IEnumerable<string> sids, Func<List<EditResult>> work)
        {
            var printings = sids.Where(s => s.Length > 0).Distinct().ToList();
            var snap = CollectionEditService.TakeBinderSnapshot(printings);
            var result = Combine(what, work());
            if (result.Changed > 0)
            {
                if (snap != null)
                {
                    CollectionEditService.MarkClaimsAfter(snap);
                    if (CollectionEditService.ClaimsChanged(snap)) PushUndo(snap, result.Message);
                }
                else
                {
                    result = new EditResult
                    {
                        Changed = result.Changed, Warning = true, Touched = result.Touched,
                        Message = result.Message + "  (This change can't be undone.)",
                    };
                }
            }
            AfterEdit(result, printings);
        }

        private bool Confirm(string text, string title) => EditPageKit.Confirm(this, text, title);

        private static string Copies(int n) => n == 1 ? "copy" : "copies";

        // ── Add to the binder ───────────────────────────────────────────
        private void DoAdd(PoolPage? table = null)
        {
            var rows = Targets(table ?? _active);
            if (rows.Count == 0) { ShowStatus("Select a card first.", true); return; }
            if (TooMany(rows)) return;
            int qty = Qty;
            var plan = KeysOf(rows);
            if (plan.Count > 1 &&
                !Confirm($"You have selected {plan.Count} rows: {FinishBreakdown(plan.Select(p => p.Key.Finish))}.\n\n" +
                         $"Put {qty} {Copies(qty)} of each in the Trade Binder, in that row's own finish?",
                         "Add to the Trade Binder"))
                return;
            Run($"Added {qty} to the Trade Binder", plan.Select(p => p.Key.ScryfallId), () =>
                plan.Select(p => CollectionEditService.AddToBinder(p.Key, qty, p.Name)).ToList());
        }

        // ── Remove from the binder (back to Available) ──────────────────
        private void DoRemove(PoolPage? table, bool all)
        {
            var rows = Targets(table ?? _active);
            if (rows.Count == 0) { ShowStatus("Select a card first.", true); return; }
            if (TooMany(rows)) return;
            int qty = all ? int.MaxValue : Qty;
            var plan = KeysOf(rows);
            if (all || plan.Count > 1)
            {
                string what = all ? "every binder copy" : $"{qty} {Copies(qty)}";
                string of = plan.Count == 1 ? $"{plan[0].Name} ({plan[0].Key.Text})" : $"each of the {plan.Count} selected rows ({FinishBreakdown(plan.Select(p => p.Key.Finish))})";
                if (!Confirm($"Take {what} of {of} out of the Trade Binder?\n\nThey go back to Available in your collection.",
                             "Remove from the Trade Binder"))
                    return;
            }
            Run(all ? "Removed from the Trade Binder" : $"Removed {qty} from the Trade Binder", plan.Select(p => p.Key.ScryfallId), () =>
                plan.Select(p => CollectionEditService.RemoveFromBinder(p.Key, qty, p.Name, traded: false)).ToList());
        }

        // ── Traded / Sold (gone from the binder and the collection) ─────
        private void DoTraded(PoolPage? table, bool all)
        {
            var rows = Targets(table ?? _active);
            if (rows.Count == 0) { ShowStatus("Select a card first.", true); return; }
            if (TooMany(rows)) return;
            int qty = all ? int.MaxValue : Qty;
            var plan = KeysOf(rows);
            string what = all ? "every binder copy" : $"{qty} {Copies(qty)}";
            string of = plan.Count == 1 ? $"{plan[0].Name} ({plan[0].Key.Text})" : $"each of the {plan.Count} selected rows ({FinishBreakdown(plan.Select(p => p.Key.Finish))})";
            if (!Confirm($"Traded or sold {what} of {of}?\n\n" +
                         "They leave the Trade Binder AND your collection. (Undo can bring them back.)",
                         "Traded / Sold"))
                return;
            Run("Traded/Sold", plan.Select(p => p.Key.ScryfallId), () =>
                plan.Select(p => CollectionEditService.RemoveFromBinder(p.Key, qty, p.Name, traded: true)).ToList());
        }

        // ── Asking price and notes ──────────────────────────────────────
        private void EditAskingInPlace(object row, DataGridCell? cell)
        {
            SetCurrent(row);
            var b = (TradeBinderEntry)row;
            int id = b.TradeBinderEntryId;
            string current = b.AskingPrice.HasValue ? b.AskingPrice.Value.ToString("F2", CultureInfo.CurrentCulture) : "";
            EditPageKit.ShowCellEditor(cell, current, 90, TextAlignment.Right, digitsOnly: false,
                $"Asking price per copy (market {b.PriceDisplay}, trade value {b.TradeValueDisplay}). Empty = none. Enter to save, Esc to cancel",
                text =>
                {
                    if (!EditPageKit.TryParsePrice(text, out var price))
                    {
                        ShowStatus("Enter a price like 4.50 (or leave it empty for none).", true);
                        return;
                    }
                    if (price == b.AskingPrice) return;
                    Run("Asking price", new[] { b.ScryfallId }, () =>
                        new List<EditResult> { CollectionEditService.SetAskingPrice(id, price) });
                });
        }

        private void EditNotesInPlace(object row, DataGridCell? cell)
        {
            SetCurrent(row);
            var b = (TradeBinderEntry)row;
            int id = b.TradeBinderEntryId;
            string current = b.Notes ?? "";
            EditPageKit.ShowCellEditor(cell, current, 280, TextAlignment.Left, digitsOnly: false,
                "Notes for this binder row — Enter to save, Esc to cancel", text =>
                {
                    if (text.Trim() == current.Trim()) return;
                    Run("Notes", new[] { b.ScryfallId }, () =>
                        new List<EditResult> { CollectionEditService.SetBinderNotes(id, text) });
                });
        }

        /// <summary>Right-click → Set Asking Price… / Edit Notes…: the editor over that cell (or at the mouse).</summary>
        private void EditBinderCell(string header)
        {
            if (Targets(_bottom).FirstOrDefault(IsBinderRow) is not { } row)
            {
                ShowStatus("Select a row in the Trade Binder table first.", true);
                return;
            }
            // After the right-click menu has closed, or it would close the editor at once.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var cell = _bottom.CellFor(row, header);
                if (header == "Asking") EditAskingInPlace(row, cell);
                else EditNotesInPlace(row, cell);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void ClearAsking()
        {
            var rows = Targets(_bottom).OfType<TradeBinderEntry>().Where(b => b.AskingPrice.HasValue).ToList();
            if (rows.Count == 0) { ShowStatus("No asking price to clear on the selected binder rows.", false); return; }
            Run("Asking price cleared", rows.Select(b => b.ScryfallId), () =>
                rows.Select(b => CollectionEditService.SetAskingPrice(b.TradeBinderEntryId, null)).ToList());
        }

        // ── Undo ────────────────────────────────────────────────────────
        private void PushUndo(ClaimSnapshot snap, string text)
        {
            _undo.Add((snap, text));
            if (_undo.Count > UndoSteps) _undo.RemoveAt(0);
            UpdateUndo();
        }

        private void DoUndo()
        {
            if (_undo.Count == 0) { ShowStatus("Nothing to undo.", false); return; }
            var (snap, text) = _undo[^1];
            _undo.RemoveAt(_undo.Count - 1);
            var result = CollectionEditService.RestoreClaims(snap, text);
            UpdateUndo();
            AfterEdit(result, snap.Printings.ToList());
        }

        private void UpdateUndo()
        {
            BtnUndo.IsEnabled = _undo.Count > 0;
            BtnUndo.ToolTip = _undo.Count == 0
                ? "Nothing to undo (Ctrl+Z)"
                : $"Undo: {_undo[^1].Text}  (Ctrl+Z · {_undo.Count} of the last {UndoSteps} changes can be undone)";
        }

        // ── After every change ──────────────────────────────────────────
        /// <summary>
        /// The status line, then both tables re-read (same filters and sort)
        /// with the edited rows selected (or a row of the same card).
        /// </summary>
        private void AfterEdit(EditResult result, List<string> sids)
        {
            ShowStatus(result.Message, result.Warning);
            if (result.Changed <= 0) { UpdateButtons(); return; }

            var keys = new HashSet<RowKey>(result.Touched);
            var printings = new HashSet<string>(sids);
            _top.ReloadRows(r => keys.Contains(KeyOf(r)), r => printings.Contains(Str(r, "ScryfallId")));
            _bottom.ReloadRows(r => keys.Contains(KeyOf(r)), r => printings.Contains(Str(r, "ScryfallId")));
            UpdateButtons();
        }

        private void ShowStatus(string text, bool warning) => EditPageKit.ShowStatus(ActionText, text, warning);

        // ── Buttons ─────────────────────────────────────────────────────
        private void BtnAdd_Click(object sender, RoutedEventArgs e) => DoAdd();
        private void BtnRemove_Click(object sender, RoutedEventArgs e) => DoRemove(null, all: false);
        private void BtnTraded_Click(object sender, RoutedEventArgs e) => DoTraded(null, all: false);
        private void BtnUndo_Click(object sender, RoutedEventArgs e) => DoUndo();

        // ══════════════════════════════════════════════════════════════════
        // RIGHT-CLICK MENUS (same actions as the buttons)
        // ══════════════════════════════════════════════════════════════════
        private readonly List<EditMenuEntry> _menuEntries = new();
        private MenuItem? _undoTop, _undoBottom;

        private void BuildMenus()
        {
            MenuItem Item(ContextMenu menu, string header, string keys, Action act, Func<bool>? enabled = null, bool qty = false)
            {
                var mi = new MenuItem { Header = header, InputGestureText = keys };
                mi.Click += (_, _) => act();
                menu.Items.Add(mi);
                _menuEntries.Add(new EditMenuEntry(mi, header, enabled ?? (() => true), qty));
                return mi;
            }

            // Top: the collection.
            Item(_topMenu, "Add to Binder", "Enter", () => DoAdd(_top), () => BtnAdd.IsEnabled, true);
            Item(_topMenu, "Remove from Binder", "", () => DoRemove(_top, all: false), () => BtnRemove.IsEnabled, true);
            Item(_topMenu, "Traded / Sold…", "", () => DoTraded(_top, all: false), () => BtnTraded.IsEnabled, true);
            _topMenu.Items.Add(new Separator());
            _undoTop = Item(_topMenu, "Undo", "Ctrl+Z", DoUndo, () => _undo.Count > 0);

            // Bottom: the Trade Binder.
            Item(_bottomMenu, "Add more from the collection", "", () => DoAdd(_bottom), () => BtnAdd.IsEnabled, true);
            Item(_bottomMenu, "Remove from Binder", "Delete", () => DoRemove(_bottom, all: false), () => BtnRemove.IsEnabled, true);
            Item(_bottomMenu, "Remove all of the selected rows", "Shift+Delete", () => DoRemove(_bottom, all: true), () => BtnRemove.IsEnabled);
            _bottomMenu.Items.Add(new Separator());
            Item(_bottomMenu, "Traded / Sold…", "", () => DoTraded(_bottom, all: false), () => BtnTraded.IsEnabled, true);
            Item(_bottomMenu, "Traded / Sold — all of the selected rows…", "", () => DoTraded(_bottom, all: true), () => BtnTraded.IsEnabled);
            _bottomMenu.Items.Add(new Separator());
            Item(_bottomMenu, "Set Asking Price…", "", () => EditBinderCell("Asking"));
            Item(_bottomMenu, "Clear Asking Price", "", ClearAsking);
            Item(_bottomMenu, "Edit Notes…", "", () => EditBinderCell("Notes"));
            _bottomMenu.Items.Add(new Separator());
            _undoBottom = Item(_bottomMenu, "Undo", "Ctrl+Z", DoUndo, () => _undo.Count > 0);

            _topMenu.Opened += (_, _) => MenuOpened(_top);
            _bottomMenu.Opened += (_, _) => MenuOpened(_bottom);
            _top.SetRowContextMenu(_topMenu);
            _bottom.SetRowContextMenu(_bottomMenu);
        }

        private void MenuOpened(PoolPage table)
        {
            _active = table;
            if (table.SelectedCard is { } card) SetCurrent(card);
            else UpdateButtons();

            int q = Qty;
            foreach (var entry in _menuEntries)
            {
                entry.Item.Header = entry.ShowsQty && q > 1 ? $"{entry.Header} ×{q}" : entry.Header;
                entry.Item.IsEnabled = entry.Enabled();
            }
            string undoText = _undo.Count > 0 ? $"Undo: {Shorten(_undo[^1].Text)}" : "Undo";
            if (_undoTop != null) _undoTop.Header = undoText;
            if (_undoBottom != null) _undoBottom.Header = undoText;
        }

        // ══════════════════════════════════════════════════════════════════
        // KEYS
        // ══════════════════════════════════════════════════════════════════
        private void Top_GridPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter && e.Key != Key.Return) return;
            _active = _top;
            if (_top.SelectedCard is { } card) SetCurrent(card);
            DoAdd(_top);
            e.Handled = true;           // don't let Enter move to the next row
        }

        private void Bottom_GridPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Delete) return;
            _active = _bottom;
            DoRemove(_bottom, all: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e) =>
            AppSettingsService.Changed -= OnSettingsChanged;

        private void Page_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            if (e.Key == Key.Q)
            {
                QtyBox.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Z && (Keyboard.FocusedElement is not TextBox || ReferenceEquals(Keyboard.FocusedElement, QtyBox)))
            {
                DoUndo();                  // a text box keeps its own Ctrl+Z (not the Qty box: undoing a number is no use)
                e.Handled = true;
            }
        }

        private void QtyBox_PreviewTextInput(object sender, TextCompositionEventArgs e) => EditPageKit.DigitsOnly(e);

        private void QtyBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                DoAdd();
                e.Handled = true;
            }
        }

        private void QtyBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
            QtyBox.SelectAll();

        /// <summary>Trade % changed in Settings: new label, and the binder's Trade Value column re-read.</summary>
        private void OnSettingsChanged() => Dispatcher.BeginInvoke(new Action(() =>
        {
            ShowTradePercent();
            _bottom.ReloadRows(null);
        }));

        // ── Helpers ─────────────────────────────────────────────────────

        /// <summary>A row's finish as shown (v1 foil of an etched-only printing = etched).</summary>
        private static string FinishOf(object row) =>
            CardFinish.Normalize(Str(row, "ShownFinish") is { Length: > 0 } shown ? shown : Str(row, "Finish"));

        /// <summary>The key of a collection or binder row (same rules as the service).</summary>
        private static RowKey KeyOf(object row) =>
            RowKey.Of(Str(row, "ScryfallId"), FinishOf(row), Str(row, "Language"), Str(row, "Condition"));

        private static string CardText(object card) =>
            $"{Str(card, "Name")} ({Str(card, "SetCode").ToUpperInvariant()} #{Str(card, "CollectorNumber")})";

        // ── Splitters: sizes are remembered (all Edit pages share them) ──
        private void TableSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) =>
            EditPageKit.SaveTopShare(TopRow, BottomRow);

        private void DetailSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) =>
            EditPageKit.SaveDetailWidth(DetailColumn);

        // NavigationView wraps pages in a ScrollViewer → infinite height →
        // virtualization defeated → freeze. Same fix as PoolPage.
        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            AppSettingsService.Changed -= OnSettingsChanged;
            AppSettingsService.Changed += OnSettingsChanged;
            EditPageKit.RestoreSplitters(TopRow, BottomRow, DetailColumn);
            EditPageKit.DisableHostScroll(this);
        }
    }
}
