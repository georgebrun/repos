using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using BreakersOfE.Models;
using BreakersOfE.Services;
using static BreakersOfE.Views.Pages.EditPageKit;
using BreakersOfE.Views.Dialogs;

namespace BreakersOfE.Views.Pages
{
    /// <summary>
    /// Edit → Pool → Collection. Two tables like v1: the pool table on top,
    /// the matching collection table below. Both are the same PoolPage the
    /// View section uses (embedded, with their own filters, layouts and zoom).
    ///
    /// A collection row is one printing + finish + language + condition.
    /// Cards added from the pool table go in with the Language and Condition
    /// picked in the bar (English · Near Mint to start); adding to a selected
    /// collection row keeps that row's language and condition.
    ///
    /// Every change — buttons, keys, right-click, cell double-clicks — goes
    /// through <see cref="Run"/>: snapshot for Undo → CollectionEditService →
    /// <see cref="AfterEdit"/>, which refreshes both tables in one place.
    /// Several selected rows (Ctrl/Shift+click) are all acted on, after asking.
    ///
    /// Keys: top table Enter = add default finish, Shift+Enter = foil,
    /// Ctrl+Enter = etched; bottom table Delete = remove Qty from the selected
    /// rows, Shift+Delete = remove all of them; Ctrl+Q = Qty box; Ctrl+Z = Undo.
    /// </summary>
    public partial class EditCollectionPage : Page
    {
        private readonly PoolPage _top = new();
        private readonly PoolPage _bottom = new();
        private string _poolTag = "Cards";
        private string CollTag => CollectionEditService.CollectionTagFor(_poolTag);
        /// <summary>Online → Collection (MTGO or Arena): no language, condition or etched.</summary>
        private bool IsOnline => CollectionEditService.IsOnline(CollTag);
        private bool IsArena => _poolTag == "ArenaCards";

        // The table the user worked in last, and the card shown / counted.
        private PoolPage? _active;
        private object? _current;
        private object? _currentPool;      // its pool card (for adding), if found
        private bool _currentEtchedOnly;
        private FinishCounts _counts = new();

        private readonly ContextMenu _topMenu = new();
        private readonly ContextMenu _bottomMenu = new();

        // Undo: the rows as they were before each change (newest last).
        private const int UndoSteps = 20;
        private readonly List<(EditSnapshot Snap, string Text)> _undo = new();

        public EditCollectionPage()
        {
            InitializeComponent();
            new WindowKeyHook(this, Page_PreviewKeyDown);   // Ctrl+Z / Ctrl+Q from anywhere in the window

            // Pool on top starts as card pictures (+ / − on each), the collection
            // below as a grid; each has its own Grid / Gallery button, remembered.
            _top.SetEmbedded(galleryByDefault: true);
            _bottom.SetEmbedded(galleryByDefault: false);
            TopFrame.Content = _top;
            BottomFrame.Content = _bottom;

            _top.SelectedCardChanged += card => OnSelected(_top, card);
            _bottom.SelectedCardChanged += card => OnSelected(_bottom, card);

            // Gallery tiles: + adds Qty (Shift = foil, Ctrl = etched), − removes.
            _top.GalleryAdd += (card, keys) => { _active = _top; SetCurrent(card); DoAdd(FinishFromKeys(keys), _top); };
            _top.GalleryRemove += (card, keys) =>
            {
                _active = _top;
                SetCurrent(card);
                DoRemove(FinishFromKeys(keys) ?? OwnedFinish(), _top);
            };
            _bottom.GalleryAdd += (card, keys) => { _active = _bottom; SetCurrent(card); DoAdd(FinishFromKeys(keys), _bottom); };
            _bottom.GalleryRemove += (card, keys) =>
            {
                _active = _bottom;
                SetCurrent(card);
                if (FinishFromKeys(keys) is { } f) DoRemove(f, _bottom);
                else RemoveRows(all: false);           // − on a collection tile: that row itself
            };

            _top.GridPreviewKeyDown += Top_GridPreviewKeyDown;
            _bottom.GridPreviewKeyDown += Bottom_GridPreviewKeyDown;

            LanguageBox.ItemsSource = CardLanguage.All;
            LanguageBox.SelectedItem = CardLanguage.Default;
            ConditionBox.ItemsSource = CardCondition.All;
            ConditionBox.SelectedItem = CardCondition.Default;

            BuildMenus();

            // Double-click a cell of a collection row to change it in place.
            _bottom.CellDoubleClickHandler = (row, header, cell) =>
            {
                switch (header)
                {
                    case "Qty": EditQuantityInPlace(row, cell); return true;
                    case "Notes": EditTextInPlace(row, cell, TextField.Notes); return true;
                    case "Storage": EditTextInPlace(row, cell, TextField.Storage); return true;
                    case "Fav": ToggleFavorite(); return true;
                    case "Language":
                        ShowChoices(cell, CardLanguage.All, CardLanguage.Normalize(Str(row, "Language")),
                                    v => ChangeRows(null, v, null, $"Language → {v}"));
                        return true;
                    case "Condition":
                        ShowChoices(cell, CardCondition.All, CardCondition.Normalize(Str(row, "Condition")),
                                    v => ChangeRows(null, null, v, $"Condition → {v}"));
                        return true;
                    case "Finish":
                        ShowChoices(cell, FinishChoices(), CardFinish.Display(FinishOf(row)),
                                    v => ChangeRows(FinishFromDisplay(v), null, null, $"Finish → {v}"));
                        return true;
                    default: return false;
                }
            };
            UpdateButtons();
            UpdateUndo();
        }

        /// <summary>Pool table tag → its collection table tag.</summary>
        public static string CollectionTagFor(string poolTag) => CollectionEditService.CollectionTagFor(poolTag);

        private static string DisplayName(string poolTag) => poolTag switch
        {
            "ArtSeries" => "Art Series",
            "MtgoCards" => "MTGO",
            "ArenaCards" => "Arena",
            "" => "Cards",
            _ => poolTag,
        };

        /// <summary>Show one pool table and its collection table (e.g. "Tokens").</summary>
        public void LoadPair(string poolTag)
        {
            if (string.IsNullOrEmpty(poolTag)) poolTag = "Cards";
            _poolTag = poolTag;
            _active = null;
            _current = null;
            _currentPool = null;
            Detail.ShowCard(null);
            ShowModeControls();
            _top.LoadPool(poolTag);
            _bottom.LoadPool(CollTag);
            RefreshCounts();
            ShowStatus("", false);
        }

        /// <summary>
        /// Online pages have no language, condition, storage or etched; Arena
        /// has no foils either. Those controls and menu items are hidden there.
        /// </summary>
        private void ShowModeControls()
        {
            var paper = IsOnline ? Visibility.Collapsed : Visibility.Visible;
            var foil = IsArena ? Visibility.Collapsed : Visibility.Visible;
            LanguageLabel.Visibility = LanguageBox.Visibility = paper;
            ConditionLabel.Visibility = ConditionBox.Visibility = paper;
            BtnAddEtched.Visibility = BtnRemoveEtched.Visibility = paper;
            BtnAddFoil.Visibility = BtnRemoveFoil.Visibility = foil;

            foreach (var entry in _menuEntries)
            {
                string h = entry.Header;
                entry.Item.Visibility =
                    (h.Contains("Etched") || h == "Edit Storage…") ? paper :
                    h.Contains("Foil") && !h.Contains("Non-Foil") && !h.Contains("Favorite") ? foil :
                    Visibility.Visible;
            }
            if (_languageMenu != null) _languageMenu.Visibility = paper;
            if (_conditionMenu != null) _conditionMenu.Visibility = paper;
            if (_finishMenu != null)
            {
                _finishMenu.Visibility = foil;         // Arena: one finish, nothing to change to
                foreach (MenuItem mi in _finishMenu.Items)
                    mi.Visibility = (string)mi.Tag == CardFinish.Etched ? paper : Visibility.Visible;
            }
        }

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

            string sid = Str(card, "ScryfallId");
            // Adding needs the pool printing: the card itself (top table) or
            // looked up by its ScryfallId (a collection row, bottom table).
            _currentPool = card is IOwnedCard ? card : CollectionEditService.FindPoolCard(CollTag, sid);
            _currentEtchedOnly = EtchedOnly(card, _currentPool);
            RefreshCounts();
        }

        private void RefreshCounts()
        {
            _counts = _current == null
                ? new FinishCounts()
                : CollectionEditService.Counts(CollTag, Str(_current, "ScryfallId"), _currentEtchedOnly);
            UpdateButtons();
        }

        private bool IsCollectionRow(object card) => card is not IOwnedCard;

        private void UpdateButtons()
        {
            int selected = _active?.SelectedCount ?? 0;
            bool many = selected > 1;
            var (nf, f, e) = _currentPool != null
                ? CollectionEditService.FinishesOf(CollTag, _currentPool) : (false, false, false);

            // Several rows: every button is on (rows that can't take the
            // action are skipped and reported).
            // One collection row: only its own finish (a row is one finish).
            // Several rows: each adds / removes its own finish (the confirm lists them).
            string? rowFin = !many && _current != null && IsCollectionRow(_current) ? FinishOf(_current) : null;
            bool Own(string fin) => rowFin == null || rowFin == fin;
            BtnAddNonFoil.IsEnabled = many ? SomeSelectedComesIn(CardFinish.NonFoil) : nf && Own(CardFinish.NonFoil);
            BtnAddFoil.IsEnabled = many ? SomeSelectedComesIn(CardFinish.Foil) : f && Own(CardFinish.Foil);
            BtnAddEtched.IsEnabled = many ? SomeSelectedComesIn(CardFinish.Etched) : e && Own(CardFinish.Etched);
            BtnRemoveNonFoil.IsEnabled = many || (_counts.NonFoil > 0 && Own(CardFinish.NonFoil));
            BtnRemoveFoil.IsEnabled = many || (_counts.Foil > 0 && Own(CardFinish.Foil));
            BtnRemoveEtched.IsEnabled = many || (_counts.Etched > 0 && Own(CardFinish.Etched));
            BtnRemoveAll.IsEnabled = many || _counts.Total > 0;

            if (_current == null)
                ModeText.Text = $"{(IsOnline ? "Online" : "Pool")} → Collection · {DisplayName(_poolTag)}. Select a card in either table " +
                                "(Ctrl+click or Shift+click for several).";
            else if (many)
                ModeText.Text = $"{selected} rows selected in the {(_active == _bottom ? "collection" : "pool")} table" +
                                (IsOnline ? "" : $"  ·  adds go in as {AddsAs()}");
            else
                ModeText.Text = $"{CardText(_current)}  ·  you own {OwnedText()}" +
                                (IsOnline ? "" : $"  ·  adds go in as {AddsAs()}");
        }

        /// <summary>
        /// Several rows selected: can an Add button of this finish do anything?
        /// Collection rows add their own finish (always fine); pool cards only
        /// if the printing comes in that finish.
        /// </summary>
        private bool SomeSelectedComesIn(string finish)
        {
            var rows = _active?.SelectedCards ?? new List<object>();
            if (rows.Count > MaxRows) return true;            // refused later with a message
            return rows.Any(r => IsCollectionRow(r) || ComesIn(r, finish));
        }

        /// <summary>Does this pool printing come in this finish (this table's game)?</summary>
        private bool ComesIn(object pool, string finish)
        {
            var (nf, f, e) = CollectionEditService.FinishesOf(CollTag, pool);
            return CollectionEditService.FinishExists(finish, nf, f, e);
        }

        /// <summary>"English · Near Mint" — or the selected collection row's own.</summary>
        private string AddsAs()
        {
            if (_active == _bottom && _current != null && IsCollectionRow(_current))
                return $"{CardLanguage.Normalize(Str(_current, "Language"))} · " +
                       $"{CardCondition.Normalize(Str(_current, "Condition"))} (this row's)";
            return $"{AddLanguage} · {AddCondition}";
        }

        private string OwnedText()
        {
            if (_counts.Total == 0) return "none";
            var parts = new List<string>();
            if (_counts.NonFoil > 0) parts.Add($"{_counts.NonFoil} Non-Foil");
            if (_counts.Foil > 0) parts.Add($"{_counts.Foil} Foil");
            if (_counts.Etched > 0) parts.Add($"{_counts.Etched} Etched");
            string rows = _counts.Rows > 1 ? $" in {_counts.Rows} rows" : "";
            return string.Join(", ", parts) + rows + (_counts.Used > 0 ? $"  ({_counts.Used} in use by decks)" : "");
        }

        // Online rows have no language or condition ("" → the defaults, as the service keys them).
        private string AddLanguage => IsOnline ? "" : LanguageBox.SelectedItem as string ?? CardLanguage.Default;
        private string AddCondition => IsOnline ? "" : ConditionBox.SelectedItem as string ?? CardCondition.Default;

        private void Defaults_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

        // ══════════════════════════════════════════════════════════════════
        // ACTIONS — every gesture ends up here
        // ══════════════════════════════════════════════════════════════════
        private int Qty => EditPageKit.ReadQty(QtyBox);

        /// <summary>At most this many rows per action (Ctrl+A on the pool would be 100,000).</summary>
        private const int MaxRows = 1000;

        /// <summary>The rows an action works on: the selected rows of that table.</summary>
        private List<object> Targets(PoolPage? table)
        {
            table ??= _active;
            if (table != null)
            {
                var rows = table.SelectedCards;
                if (rows.Count > 0) return rows;
            }
            // Nothing selected there: the current card, but only if it is from that table.
            if (_current == null) return new List<object>();
            bool fits = table == null || (table == _bottom) == IsCollectionRow(_current);
            return fits ? new List<object> { _current } : new List<object>();
        }

        private bool TooMany(List<object> rows) => EditPageKit.TooMany(rows.Count, MaxRows, ShowStatus);

        /// <summary>
        /// Snapshot (for Undo) → do the work → one status line → refresh.
        /// <paramref name="what"/> starts the summary when several rows were done.
        /// </summary>
        private void Run(string what, IEnumerable<string> sids, Func<List<EditResult>> work)
        {
            var printings = sids.Where(s => s.Length > 0).Distinct().ToList();
            var snap = CollectionEditService.Snapshot(CollTag, printings);
            var results = work();
            var result = Combine(what, results);
            if (result.Changed > 0)
            {
                if (snap != null)
                {
                    CollectionEditService.MarkAfter(snap);
                    PushUndo(snap, result.Message);
                }
                else
                {
                    result = new EditResult
                    {
                        Changed = result.Changed,
                        Warning = true,
                        Touched = result.Touched,
                        Message = result.Message + "  (This change can't be undone.)",
                    };
                }
            }
            AfterEdit(result, printings);
        }

        private bool Confirm(string text, string title) => EditPageKit.Confirm(this, text, title);

        // ── Add ─────────────────────────────────────────────────────────
        /// <summary>
        /// Add Qty copies to each target. <paramref name="finish"/> null = each
        /// card's default finish (non-foil, else foil, else etched).
        /// </summary>
        private void DoAdd(string? finish, PoolPage? table = null)
        {
            table ??= _active;
            var rows = Targets(table);
            if (rows.Count == 0) { ShowStatus("Select a card first.", true); return; }
            if (TooMany(rows)) return;
            int qty = Qty;
            string finishText = finish == null ? "" : CardFinish.Display(finish) + " ";

            // What each selected row adds to. Two selected rows that land on
            // the same collection row (same printing, finish, language,
            // condition) add once, not twice.
            var plan = new List<(RowKey Key, object? Pool, string Name)>();
            var cannot = new List<string>();                // pool cards that don't come in the finish
            foreach (var row in rows)
            {
                bool fromCollection = IsCollectionRow(row);
                var pool = fromCollection ? CollectionEditService.FindPoolCard(CollTag, Str(row, "ScryfallId")) : row;
                // A collection row adds its own finish; a pool card the button's (or its default).
                string fin = fromCollection ? FinishOf(row) : finish ?? DefaultFinish(pool);
                if (!fromCollection && !ComesIn(row, fin))
                {
                    cannot.Add(CardText(row));
                    continue;
                }
                var key = RowKey.Of(Str(row, "ScryfallId"), fin,
                                    fromCollection ? Str(row, "Language") : AddLanguage,
                                    fromCollection ? Str(row, "Condition") : AddCondition);
                if (!plan.Any(p => p.Key == key)) plan.Add((key, pool, Str(row, "Name")));
            }

            string skipped = cannot.Count == 0 ? ""
                : $"{cannot.Count} of the selected cards {(cannot.Count == 1 ? "doesn't" : "don't")} come in " +
                  $"{(finish == null ? "any finish" : CardFinish.Display(finish))} and {(cannot.Count == 1 ? "is" : "are")} skipped: " +
                  string.Join(", ", cannot.Take(5)) + (cannot.Count > 5 ? $" and {cannot.Count - 5} more" : "") + ".";
            if (plan.Count == 0)
            {
                ShowStatus(skipped.Length > 0 ? skipped : "Nothing to add.", true);
                return;
            }
            bool ownFinish = rows.Any(IsCollectionRow);
            if (ownFinish) finishText = "";
            if ((plan.Count > 1 || cannot.Count > 0) &&
                !Confirm((ownFinish
                            ? OwnFinishQuestion("Add", qty, plan.Select(p => p.Key.Finish).ToList(), "to")
                            : $"Add {qty} {finishText}{(qty == 1 ? "copy" : "copies")} to each of {plan.Count} {(plan.Count == 1 ? "card" : "cards")}?") +
                         (skipped.Length > 0 ? "\n\n" + skipped : ""),
                         "Add to several rows"))
                return;

            Run($"Added {qty} {finishText}".TrimEnd(), plan.Select(p => p.Key.ScryfallId), () =>
                plan.Select(p => p.Pool == null
                    ? new EditResult { Message = $"{p.Name}: not in the card pool.", Warning = true }
                    : CollectionEditService.Add(CollTag, p.Pool, p.Key.Finish, qty, p.Key.Language, p.Key.Condition))
                .ToList());
            if (skipped.Length > 0) ShowStatus(ActionText.Text + "  " + skipped, true);
        }

        /// <summary>Default finish for Enter: non-foil, else foil, else etched.</summary>
        private string DefaultFinish(object? pool)
        {
            if (pool == null) return CardFinish.NonFoil;
            var (nf, f, _) = CollectionEditService.FinishesOf(CollTag, pool);
            return nf ? CardFinish.NonFoil : f ? CardFinish.Foil : CardFinish.Etched;
        }

        // ── Remove ──────────────────────────────────────────────────────
        /// <summary>
        /// Remove Qty copies of one finish from each target. Pool rows use the
        /// Language and Condition in the bar; collection rows use their own
        /// (if that exact row doesn't exist, the finish's only row is used).
        /// </summary>
        private void DoRemove(string finish, PoolPage? table = null)
        {
            table ??= _active;
            var rows = Targets(table);
            if (rows.Count == 0) { ShowStatus("Select a card first.", true); return; }
            if (TooMany(rows)) return;
            int qty = Qty;

            // Each selected row → the row it removes from (once per row, even
            // when two selected rows point at the same one).
            var plan = new List<(RowKey Key, object Row, bool FromCollection)>();
            foreach (var row in rows)
            {
                bool fromCollection = IsCollectionRow(row);
                // A collection row removes from itself (its own finish); a pool card the button's finish.
                var key = RowKey.Of(Str(row, "ScryfallId"), fromCollection ? FinishOf(row) : finish,
                                    fromCollection ? Str(row, "Language") : AddLanguage,
                                    fromCollection ? Str(row, "Condition") : AddCondition);
                if (!plan.Any(p => p.Key == key)) plan.Add((key, row, fromCollection));
            }

            bool ownFinish = plan.Any(p => p.FromCollection);
            if (plan.Count > 1 &&
                !Confirm((ownFinish
                            ? OwnFinishQuestion("Remove", qty, plan.Select(p => p.Key.Finish).ToList(), "from")
                            : $"Remove {qty} {CardFinish.Display(finish)} {(qty == 1 ? "copy" : "copies")} from each of {plan.Count} rows?") +
                         "\n\nCopies used by decks or the Trade Binder are kept.",
                         "Remove from several rows"))
                return;

            // From a collection row: exactly that language and condition. From
            // the pool: the bar's, or the finish's only row if that one doesn't exist.
            Run(ownFinish ? $"Removed {qty}" : $"Removed {qty} {CardFinish.Display(finish)}", plan.Select(p => p.Key.ScryfallId), () =>
                plan.Select(p => CollectionEditService.Remove(CollTag, p.Key.ScryfallId, Str(p.Row, "Name"),
                    p.Key.Finish, qty, EtchedOnly(p.Row, p.FromCollection ? null : p.Row),
                    p.Key.Language, p.Key.Condition, allowFallback: !p.FromCollection)).ToList());
        }

        /// <summary>Collection table: remove Qty (or all unused) from each selected row itself.</summary>
        private void RemoveRows(bool all)
        {
            var rows = Targets(_bottom).Where(IsCollectionRow).ToList();
            if (rows.Count == 0) { ShowStatus("Select a row in the collection table first.", true); return; }
            if (TooMany(rows)) return;
            int qty = Qty;
            if (all || rows.Count > 1)
            {
                string what = all ? "all unused copies" : $"{qty} {(qty == 1 ? "copy" : "copies")}";
                string of = rows.Count == 1 ? $"{CardText(rows[0])} ({RowText(rows[0])}, {Int(rows[0], "Quantity")} owned)"
                                            : $"each of the {rows.Count} selected rows";
                if (!Confirm($"Remove {what} of {of}?\n\nCopies used by decks or the Trade Binder are kept.",
                             all ? "Remove all of the row" : "Remove from several rows"))
                    return;
            }
            Run(all ? "Removed all unused copies" : $"Removed {qty}", rows.Select(r => Str(r, "ScryfallId")), () =>
                rows.Select(row => CollectionEditService.RemoveFromRow(CollTag, CollectionEditService.RowId(row),
                    all ? int.MaxValue : qty, EtchedOnly(row, null))).ToList());
        }

        /// <summary>Remove All: every unused copy of each selected printing (all finishes, languages, conditions).</summary>
        private void DoRemoveAllOfPrintings(PoolPage? table = null)
        {
            var rows = Targets(table ?? _active);
            if (rows.Count == 0) { ShowStatus("Select a card first.", true); return; }
            if (TooMany(rows)) return;
            var printings = rows.GroupBy(r => Str(r, "ScryfallId")).Select(g => g.First()).ToList();
            string of = printings.Count == 1 ? CardText(printings[0]) : $"the {printings.Count} selected printings";
            if (!Confirm($"Remove every unused copy of {of} — all finishes, languages and conditions?\n\n" +
                         "Copies used by decks or the Trade Binder are kept.", "Remove All"))
                return;

            Run("Removed all unused copies", printings.Select(r => Str(r, "ScryfallId")), () =>
                printings.Select(r => CollectionEditService.RemoveAllOfPrinting(CollTag, Str(r, "ScryfallId"),
                    Str(r, "Name"), EtchedOnly(r, IsCollectionRow(r) ? null : r))).ToList());
        }

        // ── Change finish / language / condition (moves copies) ────────
        /// <summary>
        /// Move copies of the selected collection rows to another finish,
        /// language or condition (null = keep). One row with several copies →
        /// asks how many; several rows → all unused copies of each, after asking.
        /// </summary>
        private void ChangeRows(string? finish, string? language, string? condition, string what)
        {
            var rows = Targets(_bottom).Where(IsCollectionRow).ToList();
            if (rows.Count == 0) { ShowStatus("Select a row in the collection table first.", true); return; }
            if (TooMany(rows)) return;

            int count = int.MaxValue;
            if (rows.Count == 1)
            {
                var row = rows[0];
                int owned = Int(row, "Quantity");
                int used = Math.Min(Int(row, "UsedCount"), owned);
                if (owned - used > 1)
                {
                    var n = MoveCopiesDialog.Ask(Window.GetWindow(this), what,
                        $"{CardText(row)} · {RowText(row)}", owned, used);
                    if (n == null) return;
                    count = n.Value;
                }
            }
            else if (!Confirm($"{what} for the {rows.Count} selected rows?\n\n" +
                              "All unused copies of each row change; copies used by decks stay as they are.",
                              "Change several rows"))
                return;

            Run(what, rows.Select(r => Str(r, "ScryfallId")), () =>
                rows.Select(row => CollectionEditService.Move(CollTag, CollectionEditService.RowId(row), count,
                    finish, language, condition)).ToList());
        }

        // ── Notes, storage and favorite ─────────────────────────────────
        private enum TextField { Notes, Storage }

        /// <summary>Right-click → Edit Notes… / Edit Storage…: the editor over that cell.</summary>
        private void EditText(TextField field)
        {
            if (Targets(_bottom).FirstOrDefault(IsCollectionRow) is not { } row)
            {
                ShowStatus("Select a row in the collection table first.", true);
                return;
            }
            string header = field == TextField.Storage ? "Storage" : "Notes";
            // After the right-click menu has closed, or it would close the editor at once.
            Dispatcher.BeginInvoke(new Action(() => EditTextInPlace(row, _bottom.CellFor(row, header), field)),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>Favorite on/off for the selected rows (all on unless every one already is).</summary>
        private void ToggleFavorite()
        {
            var rows = Targets(_bottom).Where(IsCollectionRow).ToList();
            if (rows.Count == 0) { ShowStatus("Select a row in the collection table first.", true); return; }
            if (TooMany(rows)) return;
            bool mark = !rows.All(r => Bool(r, "IsFavorite"));
            Run(mark ? "Marked as favorite" : "Favorite removed", rows.Select(r => Str(r, "ScryfallId")), () =>
                rows.Select(row => CollectionEditService.SetFavorite(CollTag, CollectionEditService.RowId(row),
                    mark, EtchedOnly(row, null))).ToList());
        }

        // ── Undo ────────────────────────────────────────────────────────
        private void PushUndo(EditSnapshot snap, string text)
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

            // Refused when those cards changed since (it can never apply then).
            var result = CollectionEditService.Restore(snap, text);
            UpdateUndo();

            if (snap.Table != CollTag)
            {
                // Undone in another collection table (not on screen now).
                ShowStatus(result.Message, result.Warning);
                return;
            }
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
        /// One place for everything an edit changes on screen: the status line,
        /// the pool rows' Owned counts (live), the collection table (reloaded,
        /// same filters/sort, the edited rows re-selected), buttons and counts.
        /// </summary>
        private void AfterEdit(EditResult result, List<string> sids)
        {
            ShowStatus(result.Message, result.Warning);
            if (result.Changed <= 0) { RefreshCounts(); return; }

            // Pool rows of these printings: Owned updates in place.
            var wanted = new HashSet<string>(sids);
            var poolRows = _top.FindAllLoaded(r => r is IOwnedCard && wanted.Contains(Str(r, "ScryfallId"))).ToArray();
            if (poolRows.Length > 0)
            {
                OwnedCountService.Fill(_poolTag, poolRows);
                _top.RefreshGalleryCards(poolRows);      // gallery tiles show the new counts
            }

            // Collection table: re-read, re-select the rows the edit ended in
            // (or a row of the same printing if those are gone).
            var keys = new HashSet<RowKey>(result.Touched);
            var printings = new HashSet<string>(sids);
            _bottom.ReloadRows(r => keys.Contains(KeyOf(r)), r => printings.Contains(Str(r, "ScryfallId")));

            RefreshCounts();
        }

        private void ShowStatus(string text, bool warning) => EditPageKit.ShowStatus(ActionText, text, warning);

        // ── Buttons ─────────────────────────────────────────────────────
        private void BtnAddNonFoil_Click(object sender, RoutedEventArgs e) => DoAdd(CardFinish.NonFoil);
        private void BtnAddFoil_Click(object sender, RoutedEventArgs e) => DoAdd(CardFinish.Foil);
        private void BtnAddEtched_Click(object sender, RoutedEventArgs e) => DoAdd(CardFinish.Etched);
        private void BtnRemoveNonFoil_Click(object sender, RoutedEventArgs e) => DoRemove(CardFinish.NonFoil);
        private void BtnRemoveFoil_Click(object sender, RoutedEventArgs e) => DoRemove(CardFinish.Foil);
        private void BtnRemoveEtched_Click(object sender, RoutedEventArgs e) => DoRemove(CardFinish.Etched);
        private void BtnRemoveAll_Click(object sender, RoutedEventArgs e) => DoRemoveAllOfPrintings();
        private void BtnUndo_Click(object sender, RoutedEventArgs e) => DoUndo();

        // ══════════════════════════════════════════════════════════════════
        // RIGHT-CLICK MENUS (same actions as the buttons, plus row changes)
        // ══════════════════════════════════════════════════════════════════
        private readonly List<EditMenuEntry> _menuEntries = new();
        private MenuItem? _favoriteItem;
        private MenuItem? _undoTop, _undoBottom;
        private MenuItem? _finishMenu, _languageMenu, _conditionMenu;

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

            // Top: the pool table.
            Item(_topMenu, "Add Non-Foil", "Enter", () => DoAdd(CardFinish.NonFoil, _top), () => BtnAddNonFoil.IsEnabled, true);
            Item(_topMenu, "Add Foil", "Shift+Enter", () => DoAdd(CardFinish.Foil, _top), () => BtnAddFoil.IsEnabled, true);
            Item(_topMenu, "Add Etched", "Ctrl+Enter", () => DoAdd(CardFinish.Etched, _top), () => BtnAddEtched.IsEnabled, true);
            _topMenu.Items.Add(new Separator());
            Item(_topMenu, "Remove Non-Foil", "", () => DoRemove(CardFinish.NonFoil, _top), () => BtnRemoveNonFoil.IsEnabled, true);
            Item(_topMenu, "Remove Foil", "", () => DoRemove(CardFinish.Foil, _top), () => BtnRemoveFoil.IsEnabled, true);
            Item(_topMenu, "Remove Etched", "", () => DoRemove(CardFinish.Etched, _top), () => BtnRemoveEtched.IsEnabled, true);
            Item(_topMenu, "Remove All (every finish, language, condition)", "", () => DoRemoveAllOfPrintings(_top), () => BtnRemoveAll.IsEnabled);
            _topMenu.Items.Add(new Separator());
            _undoTop = Item(_topMenu, "Undo", "Ctrl+Z", DoUndo, () => _undo.Count > 0);

            // Bottom: the collection table.
            Item(_bottomMenu, "Add Non-Foil", "", () => DoAdd(CardFinish.NonFoil, _bottom), () => BtnAddNonFoil.IsEnabled, true);
            Item(_bottomMenu, "Add Foil", "", () => DoAdd(CardFinish.Foil, _bottom), () => BtnAddFoil.IsEnabled, true);
            Item(_bottomMenu, "Add Etched", "", () => DoAdd(CardFinish.Etched, _bottom), () => BtnAddEtched.IsEnabled, true);
            _bottomMenu.Items.Add(new Separator());
            Item(_bottomMenu, "Remove Qty from the selected rows", "Delete", () => RemoveRows(all: false), qty: true);
            Item(_bottomMenu, "Remove all of the selected rows", "Shift+Delete", () => RemoveRows(all: true));
            Item(_bottomMenu, "Set quantity…", "", SetQuantityOfSelected);
            Item(_bottomMenu, "Remove All (every finish, language, condition)", "", () => DoRemoveAllOfPrintings(_bottom), () => BtnRemoveAll.IsEnabled);
            _bottomMenu.Items.Add(new Separator());

            _finishMenu = new MenuItem { Header = "Change Finish" };
            foreach (var f in new[] { CardFinish.NonFoil, CardFinish.Foil, CardFinish.Etched })
            {
                string fin = f;
                var mi = new MenuItem { Header = CardFinish.Display(fin), Tag = fin };
                mi.Click += (_, _) => ChangeRows(fin, null, null, $"Finish → {CardFinish.Display(fin)}");
                _finishMenu.Items.Add(mi);
            }
            _bottomMenu.Items.Add(_finishMenu);

            _languageMenu = new MenuItem { Header = "Change Language" };
            foreach (var l in CardLanguage.All)
            {
                string lang = l;
                var mi = new MenuItem { Header = lang, Tag = lang };
                mi.Click += (_, _) => ChangeRows(null, lang, null, $"Language → {lang}");
                _languageMenu.Items.Add(mi);
            }
            _bottomMenu.Items.Add(_languageMenu);

            _conditionMenu = new MenuItem { Header = "Change Condition" };
            foreach (var c in CardCondition.All)
            {
                string cond = c;
                var mi = new MenuItem { Header = cond, Tag = cond };
                mi.Click += (_, _) => ChangeRows(null, null, cond, $"Condition → {cond}");
                _conditionMenu.Items.Add(mi);
            }
            _bottomMenu.Items.Add(_conditionMenu);

            Item(_bottomMenu, "Edit Notes…", "", () => EditText(TextField.Notes));
            Item(_bottomMenu, "Edit Storage…", "", () => EditText(TextField.Storage));
            _favoriteItem = Item(_bottomMenu, "Mark as Favorite", "", ToggleFavorite);
            _bottomMenu.Items.Add(new Separator());
            _undoBottom = Item(_bottomMenu, "Undo", "Ctrl+Z", DoUndo, () => _undo.Count > 0);

            _topMenu.Opened += (_, _) => MenuOpened(_top);
            _bottomMenu.Opened += (_, _) => MenuOpened(_bottom);
            _top.SetRowContextMenu(_topMenu);
            _bottom.SetRowContextMenu(_bottomMenu);
        }

        /// <summary>The menu acts on its own table: sync the current card, then items follow the buttons.</summary>
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

            if (table != _bottom) return;
            var rows = Targets(_bottom).Where(IsCollectionRow).ToList();
            bool single = rows.Count == 1;
            object? row = single ? rows[0] : null;

            if (_favoriteItem != null)
                _favoriteItem.Header = rows.Count > 0 && rows.All(r => Bool(r, "IsFavorite"))
                    ? "Remove Favorite" : "Mark as Favorite";

            // Tick the row's current values; finishes the printing lacks are off.
            var (nf, f, e) = _currentPool != null && single
                ? CollectionEditService.FinishesOf(CollTag, _currentPool) : (true, true, true);
            foreach (MenuItem mi in _finishMenu!.Items)
            {
                string fin = (string)mi.Tag;
                mi.IsChecked = row != null && FinishOf(row) == fin;
                mi.IsEnabled = fin switch { CardFinish.Foil => f, CardFinish.Etched => e, _ => nf };
            }
            foreach (MenuItem mi in _languageMenu!.Items)
                mi.IsChecked = row != null && CardLanguage.Normalize(Str(row, "Language")) == (string)mi.Tag;
            foreach (MenuItem mi in _conditionMenu!.Items)
                mi.IsChecked = row != null && CardCondition.Normalize(Str(row, "Condition")) == (string)mi.Tag;
        }

        /// <summary>A list of values at a cell (double-click Language / Condition / Finish).</summary>
        private static void ShowChoices(UIElement target, IEnumerable<string> options, string current, Action<string> pick)
        {
            var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom };
            foreach (var o in options)
            {
                string value = o;
                var mi = new MenuItem { Header = value, IsChecked = value == current };
                mi.Click += (_, _) => { if (value != current) pick(value); };
                menu.Items.Add(mi);
            }
            // Open once the double-click has finished, or the grid's own mouse
            // handling could close the list straight away.
            target.Dispatcher.BeginInvoke(new Action(() => menu.IsOpen = true),
                System.Windows.Threading.DispatcherPriority.Input);
        }

        private IEnumerable<string> FinishChoices()
        {
            var (nf, f, e) = _currentPool != null ? CollectionEditService.FinishesOf(CollTag, _currentPool) : (true, true, true);
            if (nf) yield return CardFinish.Display(CardFinish.NonFoil);
            if (f) yield return CardFinish.Display(CardFinish.Foil);
            if (e) yield return CardFinish.Display(CardFinish.Etched);
        }

        private static string FinishFromDisplay(string display) => display switch
        {
            "Foil" => CardFinish.Foil,
            "Etched" => CardFinish.Etched,
            _ => CardFinish.NonFoil,
        };

        // ══════════════════════════════════════════════════════════════════
        // IN-PLACE EDITORS (double-click Qty / Notes / Storage)
        // ══════════════════════════════════════════════════════════════════

        /// <summary>Right-click → Set quantity… (grid or gallery): the Qty editor over the cell, or at the mouse.</summary>
        private void SetQuantityOfSelected()
        {
            if (Targets(_bottom).FirstOrDefault(IsCollectionRow) is not { } row)
            {
                ShowStatus("Select a row in the collection table first.", true);
                return;
            }
            // After the right-click menu has closed, or it would close the editor at once.
            Dispatcher.BeginInvoke(new Action(() => EditQuantityInPlace(row, _bottom.CellFor(row, "Qty"))),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>A tile button's keys: Shift = foil, Ctrl = etched, none = the default.</summary>
        private static string? FinishFromKeys(ModifierKeys keys) =>
            keys.HasFlag(ModifierKeys.Control) ? CardFinish.Etched :
            keys.HasFlag(ModifierKeys.Shift) ? CardFinish.Foil : null;

        /// <summary>
        /// − on a pool tile: the finish you own of the current card (the only
        /// one, else non-foil, foil, etched in that order).
        /// </summary>
        private string OwnedFinish()
        {
            if (_counts.NonFoil > 0 && _counts.Foil == 0 && _counts.Etched == 0) return CardFinish.NonFoil;
            if (_counts.Foil > 0 && _counts.NonFoil == 0 && _counts.Etched == 0) return CardFinish.Foil;
            if (_counts.Etched > 0 && _counts.NonFoil == 0 && _counts.Foil == 0) return CardFinish.Etched;
            return _counts.NonFoil > 0 ? CardFinish.NonFoil : _counts.Foil > 0 ? CardFinish.Foil
                 : _counts.Etched > 0 ? CardFinish.Etched : CardFinish.NonFoil;
        }

        /// <summary>Qty cell: type the row's exact quantity (never below the copies in use).</summary>
        private void EditQuantityInPlace(object row, DataGridCell? cell)
        {
            SetCurrent(row);
            int current = Int(row, "Quantity");
            int id = CollectionEditService.RowId(row);
            bool etchedOnly = EtchedOnly(row, null);
            EditPageKit.ShowCellEditor(cell, current.ToString(), 48, TextAlignment.Center, digitsOnly: true,
                "New quantity — Enter to save, Esc to cancel", text =>
                {
                    if (!int.TryParse(text, out int n) || n < 0)
                    {
                        ShowStatus("Enter a whole number (0 or more).", true);
                        return;
                    }
                    if (n == current) return;
                    Run("Quantity", new[] { Str(row, "ScryfallId") }, () =>
                        new List<EditResult> { CollectionEditService.SetQuantity(CollTag, id, n, etchedOnly) });
                });
        }

        /// <summary>Notes or Storage cell: type the row's text.</summary>
        private void EditTextInPlace(object row, DataGridCell? cell, TextField field)
        {
            SetCurrent(row);
            bool storage = field == TextField.Storage;
            string current = Str(row, storage ? "StorageLocation" : "Notes");
            int id = CollectionEditService.RowId(row);
            bool etchedOnly = EtchedOnly(row, null);
            EditPageKit.ShowCellEditor(cell, current, storage ? 200 : 280, TextAlignment.Left, digitsOnly: false,
                (storage ? "Where these cards are kept" : "Notes for this row") + " — Enter to save, Esc to cancel", text =>
                {
                    if (text.Trim() == current.Trim()) return;
                    Run(storage ? "Storage" : "Notes", new[] { Str(row, "ScryfallId") }, () =>
                        new List<EditResult>
                        {
                            storage ? CollectionEditService.SetStorage(CollTag, id, text, etchedOnly)
                                    : CollectionEditService.SetNotes(CollTag, id, text, etchedOnly),
                        });
                });
        }

        // ══════════════════════════════════════════════════════════════════
        // KEYS
        // ══════════════════════════════════════════════════════════════════
        private void Top_GridPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter && e.Key != Key.Return) return;
            _active = _top;
            if (_top.SelectedCard is { } card) SetCurrent(card);
            var mods = Keyboard.Modifiers;
            if (mods.HasFlag(ModifierKeys.Control)) DoAdd(CardFinish.Etched, _top);
            else if (mods.HasFlag(ModifierKeys.Shift)) DoAdd(CardFinish.Foil, _top);
            else DoAdd(null, _top);
            e.Handled = true;           // don't let Enter move to the next row
        }

        private void Bottom_GridPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Delete) return;
            _active = _bottom;
            RemoveRows(all: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
        }

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

        // ── Qty box: digits only; Enter adds the default finish ──────────
        private void QtyBox_PreviewTextInput(object sender, TextCompositionEventArgs e) => EditPageKit.DigitsOnly(e);

        private void QtyBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                DoAdd(null);
                e.Handled = true;
            }
        }

        private void QtyBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
            QtyBox.SelectAll();

        // ── Helpers ─────────────────────────────────────────────────────

        /// <summary>A collection row's finish as shown (v1 foil of an etched-only printing = etched).</summary>
        private static string FinishOf(object row) =>
            CardFinish.Normalize(Str(row, "ShownFinish") is { Length: > 0 } shown ? shown : Str(row, "Finish"));

        /// <summary>Etched-only printing? From the pool card when known, else the row's flag.</summary>
        private bool EtchedOnly(object row, object? pool)
        {
            if (IsOnline) return false;         // online rows are stored as they are
            if (pool != null)
            {
                var (_, foil, etched) = CollectionEditService.FinishesOf(CollTag, pool);
                return etched && !foil;
            }
            return row is IFinishRow && Bool(row, "PrintingEtchedOnly");
        }

        /// <summary>The key of a collection-table row (same rules as the service).</summary>
        private static RowKey KeyOf(object row) =>
            RowKey.Of(Str(row, "ScryfallId"), FinishOf(row), Str(row, "Language"), Str(row, "Condition"));

        /// <summary>"Foil · English · Near Mint".</summary>
        private string RowText(object row) =>
            IsOnline ? CardFinish.Display(FinishOf(row)) : KeyOf(row).Text;     // online: no language/condition

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
            EditPageKit.RestoreSplitters(TopRow, BottomRow, DetailColumn);
            EditPageKit.DisableHostScroll(this);
        }
    }
}