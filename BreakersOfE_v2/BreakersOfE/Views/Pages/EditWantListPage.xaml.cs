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
using BreakersOfE.Views.Dialogs;
using static BreakersOfE.Views.Pages.EditPageKit;

namespace BreakersOfE.Views.Pages
{
    /// <summary>
    /// Edit → Lists → Pool → Want List (cards). The card pool on top, the
    /// Want List below — the same tables the View section uses (embedded,
    /// with their own filters, layouts and zoom).
    ///
    /// A want row is one printing in one finish. Got It moves copies into
    /// your collection (with the Language and Condition picked in the bar)
    /// and off the want list. An offer above the market price shows in amber.
    ///
    /// Every change goes through <see cref="Run"/>: snapshot for Undo (the
    /// want list AND the collection) → CollectionEditService → <see cref="AfterEdit"/>.
    ///
    /// Keys: top table Enter = add the default finish, Shift+Enter = foil,
    /// Ctrl+Enter = etched; bottom table Delete = remove Qty, Shift+Delete =
    /// all of the selected rows; Ctrl+Q = Qty box; Ctrl+Z = Undo.
    /// Double-click Offer or Notes in the want list to change them.
    /// </summary>
    public partial class EditWantListPage : Page
    {
        private readonly PoolPage _top = new();       // card pool
        private readonly PoolPage _bottom = new();    // Want List
        private const string PoolTag = "Cards";

        private PoolPage? _active;
        private object? _current;
        private object? _currentPool;                 // its pool card, if found
        private FinishCounts _owned = new();
        private Dictionary<string, int> _wanted = new();

        private readonly ContextMenu _topMenu = new();
        private readonly ContextMenu _bottomMenu = new();

        private const int UndoSteps = 20;
        private readonly UndoStack<UndoStep> _undo = new(UndoSteps);

        /// <summary>One Undo step: the rows (want list, and the collection for Got It), plus every deck's
        /// claims on those cards when Got It claimed copies for decks.</summary>
        private sealed record UndoStep(List<EditSnapshot> Snaps, UsageSnapshot? Usages);

        /// <summary>One choice in the For box / menu: a deck (its id) or the Collection ("").</summary>
        private sealed record ForChoice(string Id, string Name);

        public EditWantListPage()
        {
            InitializeComponent();
            new WindowKeyHook(this, (_, e) => EditKeys(e, QtyBox, DoUndo));   // Ctrl+Z / Ctrl+Q from anywhere in the window
            RememberSizes(this, TopRow, BottomRow, DetailColumn);
            WireQtyBox(QtyBox, () => DoAdd(null));                           // Enter adds the default finish

            // Pool on top starts as card pictures (+ / − on each), the want list below as a grid.
            _top.SetEmbedded(galleryByDefault: true);
            _bottom.SetEmbedded(galleryByDefault: false);
            TopFrame.Content = _top;
            BottomFrame.Content = _bottom;

            _top.SelectedCardChanged += card => OnSelected(_top, card);
            _bottom.SelectedCardChanged += card => OnSelected(_bottom, card);
            // Both tables as pictures: the boxes under each picture replace the finish buttons.
            _top.ViewModeChanged += ShowFinishButtons;
            _bottom.ViewModeChanged += ShowFinishButtons;
            Loaded += (_, _) => ShowFinishButtons();

            // A table re-read after an edit: its rows are new objects.
            _bottom.ItemsReloaded += () =>
            {
                if (_active != _bottom) return;
                if (_bottom.SelectedCard is { } sel) SetCurrent(sel);
                else { _current = null; _currentPool = null; RefreshCounts(); }
            };

            // Gallery tiles: + adds Qty (Shift = foil, Ctrl = etched), − removes Qty.
            _top.GalleryAdd += (card, keys) => { _active = _top; SetCurrent(card); DoAdd(FinishFromKeys(keys), _top); };
            _top.GalleryRemove += (card, _) => { _active = _top; SetCurrent(card); DoRemove(_top, all: false); };
            _bottom.GalleryAdd += (card, _) => { _active = _bottom; SetCurrent(card); DoAdd(null, _bottom); };
            _bottom.GalleryRemove += (card, _) => { _active = _bottom; SetCurrent(card); DoRemove(_bottom, all: false); };

            _top.GridPreviewKeyDown += Top_GridPreviewKeyDown;
            _bottom.GridPreviewKeyDown += Bottom_GridPreviewKeyDown;

            LanguageBox.ItemsSource = CardLanguage.All;
            LanguageBox.SelectedItem = AppSettingsService.Current.DefaultLanguage;      // Settings → Collection defaults
            ConditionBox.ItemsSource = CardCondition.All;
            ConditionBox.SelectedItem = AppSettingsService.Current.DefaultCondition;

            BuildMenus();

            // Double-click Offer or Notes on a want row to change it in place.
            _bottom.CellDoubleClickHandler = (row, header, cell) =>
            {
                if (row is not WantListEntry) return false;
                switch (header)
                {
                    case "Offer": EditOfferInPlace(row, cell); return true;
                    case "Notes": EditNotesInPlace(row, cell); return true;
                    default: return false;
                }
            };

            UpdateButtons();
            UpdateUndo();
        }

        /// <summary>Open the page: the card pool and the Want List.</summary>
        public void Start()
        {
            _active = null;
            _current = null;
            _currentPool = null;
            Detail.ShowCard(null);
            // Wants from before the For column ("For: Deck" in Notes) become wants for that deck.
            int converted = CollectionEditService.ConvertForNotes();
            CollectionEditService.RefreshWantDeckNames();
            FillForChoices();
            _top.LoadPool(PoolTag);
            _bottom.LoadPool(CollectionEditService.WantTable);
            RefreshCounts();
            ShowStatus(converted > 0 ? $"{converted} want(s) with \"For: deck\" in their notes are now wants for that deck (see the For column)." : "", false);
        }

        // ══════════════════════════════════════════════════════════════════
        // FOR: the Collection, or a deck
        // ══════════════════════════════════════════════════════════════════
        private List<ForChoice> _forChoices = new();

        private void FillForChoices()
        {
            var keep = (ForBox.SelectedItem as ForChoice)?.Id ?? "";
            _forChoices = new List<ForChoice> { new("", "Collection") };
            _forChoices.AddRange(DeckIndexService.AllDecks()
                .Where(d => !string.IsNullOrEmpty(d.Deck.DeckId))
                .GroupBy(d => d.Deck.DeckId).Select(g => g.First())
                .Select(d => new ForChoice(d.Deck.DeckId, string.IsNullOrWhiteSpace(d.Deck.Name)
                    ? System.IO.Path.GetFileNameWithoutExtension(d.Path) : d.Deck.Name))
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase));
            ForBox.ItemsSource = _forChoices;
            ForBox.SelectedItem = _forChoices.FirstOrDefault(c => c.Id == keep) ?? _forChoices[0];
        }

        private ForChoice AddFor => ForBox.SelectedItem as ForChoice ?? new ForChoice("", "Collection");

        private static string ForOf(object row) => row is WantListEntry w ? w.ForDeckId ?? "" : "";

        /// <summary>Right-click → For ▸: move the selected want rows to the Collection or a deck.</summary>
        private void SetFor(ForChoice choice)
        {
            var rows = Targets(_bottom).OfType<WantListEntry>().ToList();
            if (rows.Count == 0) { ShowStatus("Select rows in the Want List table first.", true); return; }
            Run($"For {choice.Name}", rows.Select(w => w.ScryfallId), () =>
                new List<EditResult> { CollectionEditService.SetWantFor(rows.Select(w => w.WantListEntryId).ToList(), choice.Id, choice.Name) });
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
            _currentPool = IsWantRow(card) ? CollectionEditService.FindPoolCard(CollectionEditService.CardsTable, Str(card, "ScryfallId")) : card;
            RefreshCounts();
        }

        private void RefreshCounts()
        {
            if (_current == null)
            {
                _owned = new FinishCounts();
                _wanted = new Dictionary<string, int>();
            }
            else
            {
                string sid = Str(_current, "ScryfallId");
                bool eo = EtchedOnly(_current);
                _owned = CollectionEditService.Counts(CollectionEditService.CardsTable, sid, eo);
                _wanted = CollectionEditService.WantedCopies(sid, eo);
            }
            UpdateButtons();
        }

        private static bool IsWantRow(object r) => r is WantListEntry;

        private void UpdateButtons()
        {
            int selected = _active?.SelectedCount ?? 0;
            bool many = selected > 1;
            var (nf, f, e) = _currentPool != null
                ? CollectionEditService.FinishesOf(CollectionEditService.CardsTable, _currentPool) : (false, false, false);
            int wanted = _wanted.Values.Sum();

            // One want row: only its own finish. Several rows: each adds its own (the confirm lists them).
            string? rowFin = !many && _current != null && IsWantRow(_current) ? FinishOf(_current) : null;
            BtnAddNonFoil.IsEnabled = many ? SomeSelectedComesIn(CardFinish.NonFoil) : nf && (rowFin == null || rowFin == CardFinish.NonFoil);
            BtnAddFoil.IsEnabled = many ? SomeSelectedComesIn(CardFinish.Foil) : f && (rowFin == null || rowFin == CardFinish.Foil);
            BtnAddEtched.IsEnabled = many ? SomeSelectedComesIn(CardFinish.Etched) : e && (rowFin == null || rowFin == CardFinish.Etched);
            BtnRemove.IsEnabled = BtnGotIt.IsEnabled = many || wanted > 0;

            if (_current == null)
                ModeText.Text = "Pool → Want List · Cards. Select a card in either table (Ctrl+click or Shift+click for several).";
            else if (many)
                ModeText.Text = $"{selected} rows selected in the {(_active == _bottom ? "Want List" : "pool")} table" +
                                $"  ·  Got It adds them as {AddLanguage} · {AddCondition}";
            else
            {
                string wantText = wanted == 0 ? "not on the Want List"
                    : "wanted: " + string.Join(", ", _wanted.Where(kv => kv.Value > 0).Select(kv => $"{kv.Value} {CardFinish.Display(kv.Key)}"));
                string ownText = _owned.Total == 0 ? "you own none"
                    : $"you own {_owned.Total}" + (_owned.Used > 0 ? $" ({_owned.Used} in use)" : "");
                ModeText.Text = $"{CardText(_current)}  ·  {wantText}  ·  {ownText}  ·  Got It adds them as {AddLanguage} · {AddCondition}";
            }
        }

        private void Defaults_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsInitialized) UpdateButtons();
        }

        /// <summary>Several rows selected: want rows add their own finish; pool cards only if they come in it.</summary>
        private bool SomeSelectedComesIn(string finish)
        {
            if (_active == null) return false;
            if (_active.SelectedRowCount > MaxRows) return true;
            return _active.SelectedUnordered.Any(r => IsWantRow(r) || ComesIn(r, finish));
        }

        /// <summary>Add Non-Foil / Foil / Etched and Remove: hidden when both tables show pictures.</summary>
        private void ShowFinishButtons()
        {
            var v = _top.ShowingGallery && _bottom.ShowingGallery ? Visibility.Collapsed : Visibility.Visible;
            BtnAddNonFoil.Visibility = BtnAddFoil.Visibility = BtnAddEtched.Visibility = BtnRemove.Visibility = v;
        }

        private static bool ComesIn(object pool, string finish)
        {
            var (nf, f, e) = CollectionEditService.FinishesOf(CollectionEditService.CardsTable, pool);
            return CollectionEditService.FinishExists(finish, nf, f, e);
        }

        private string AddLanguage => LanguageBox.SelectedItem as string ?? CardLanguage.Default;
        private string AddCondition => ConditionBox.SelectedItem as string ?? CardCondition.Default;

        // ══════════════════════════════════════════════════════════════════
        // ACTIONS — every gesture ends up here
        // ══════════════════════════════════════════════════════════════════
        private int Qty => EditPageKit.ReadQty(QtyBox);

        private const int MaxRows = 1000;

        private List<object> Targets(PoolPage? table)
        {
            table ??= _active;
            if (table != null)
            {
                var rows = table.SelectedCards;
                if (rows.Count > 0) return rows;
            }
            if (_current == null) return new List<object>();
            bool fits = table == null || (table == _bottom) == IsWantRow(_current);
            return fits ? new List<object> { _current } : new List<object>();
        }

        private bool TooMany(List<object> rows) => EditPageKit.TooMany(rows.Count, MaxRows, ShowStatus);

        /// <summary>
        /// The want rows (printing + finish) the selected rows stand for: a want
        /// row is itself; a pool card is its want row when only one finish of it
        /// is wanted. Pool cards that don't resolve are reported.
        /// </summary>
        /// <remarks>For: a want row's own (deck or Collection); a pool card's is null (every want of that finish).</remarks>
        private List<(string Sid, string Finish, string Name, string? For)> WantKeysOf(List<object> rows, out string? problem)
        {
            problem = null;
            var list = new List<(string Sid, string Finish, string Name, string? For)>();
            foreach (var r in rows)
            {
                string sid = Str(r, "ScryfallId");
                string name = Str(r, "Name");
                if (IsWantRow(r))
                {
                    var k = (sid, FinishOf(r), name, (string?)ForOf(r));
                    if (!list.Any(p => p.Sid == k.sid && p.Finish == k.Item2 && p.For == k.Item4)) list.Add(k);
                    continue;
                }
                var wanted = CollectionEditService.WantedCopies(sid, EtchedOnly(r)).Where(kv => kv.Value > 0).ToList();
                if (wanted.Count == 1)
                {
                    if (!list.Any(p => p.Sid == sid && p.Finish == wanted[0].Key)) list.Add((sid, wanted[0].Key, name, null));
                }
                else
                    problem ??= wanted.Count == 0
                        ? $"{CardText(r)} isn't on the Want List."
                        : $"{CardText(r)} is wanted in {wanted.Count} finishes — select its row in the Want List.";
            }
            return list;
        }

        /// <summary>
        /// Snapshot (for Undo: the want list, plus the collection when
        /// <paramref name="collection"/> — Got It) → do the work → status → refresh.
        /// </summary>
        private void Run(string what, IEnumerable<string> sids, Func<List<EditResult>> work, bool collection = false)
        {
            var printings = sids.Where(s => s.Length > 0).Distinct().ToList();
            var wants = CollectionEditService.Snapshot(CollectionEditService.WantTable, printings);
            var cards = collection ? CollectionEditService.Snapshot(CollectionEditService.CardsTable, printings) : null;
            // Got It can claim copies for decks: every deck's claims on these cards too.
            var usages = collection ? CollectionEditService.TakeUsageSnapshot(printings) : null;
            var result = Combine(what, work());
            if (result.Changed > 0)
            {
                if (wants != null && (!collection || (cards != null && usages != null)))
                {
                    var snaps = new List<EditSnapshot> { wants };
                    if (cards != null) snaps.Add(cards);
                    foreach (var s in snaps) CollectionEditService.MarkAfter(s);
                    if (usages != null) CollectionEditService.MarkUsagesAfter(usages);
                    PushUndo(new UndoStep(snaps, usages), result.Message);
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

        private static string Copies(int n) => n == 1 ? "copy" : "copies";

        // ── Add ─────────────────────────────────────────────────────────
        /// <summary>Add Qty to each target. <paramref name="finish"/> null = a pool card's default finish, a want row's own.</summary>
        private void DoAdd(string? finish, PoolPage? table = null)
        {
            var rows = Targets(table ?? _active);
            if (rows.Count == 0) { ShowStatus("Select a card first.", true); return; }
            if (TooMany(rows)) return;
            int qty = Qty;

            var plan = new List<(object? Pool, string Sid, string Finish, string Name, string ForId, string ForName)>();
            var cannot = new List<string>();                // pool cards that don't come in the finish
            foreach (var r in rows)
            {
                string sid = Str(r, "ScryfallId");
                var pool = IsWantRow(r) ? CollectionEditService.FindPoolCard(CollectionEditService.CardsTable, sid) : r;
                // A want row adds more of its own finish; a pool card the button's (or its default).
                string fin = IsWantRow(r) ? FinishOf(r) : finish ?? DefaultFinish(pool);
                if (!IsWantRow(r) && !ComesIn(r, fin))
                {
                    cannot.Add(CardText(r));
                    continue;
                }
                // A want row adds to its own want (its deck or the Collection); a pool card to the For box's.
                string forId = IsWantRow(r) ? ForOf(r) : AddFor.Id;
                string forName = IsWantRow(r) ? Str(r, "ForDeckName") : AddFor.Name;
                if (!plan.Any(p => p.Sid == sid && p.Finish == fin && p.ForId == forId)) plan.Add((pool, sid, fin, Str(r, "Name"), forId, forName));
            }
            string skipped = cannot.Count == 0 ? ""
                : $"{cannot.Count} of the selected cards {(cannot.Count == 1 ? "doesn't" : "don't")} come in " +
                  $"{(finish == null ? "any finish" : CardFinish.Display(finish))} and {(cannot.Count == 1 ? "is" : "are")} skipped: " +
                  string.Join(", ", cannot.Take(5)) + (cannot.Count > 5 ? $" and {cannot.Count - 5} more" : "") + ".";
            if (plan.Count == 0) { ShowStatus(skipped.Length > 0 ? skipped : "Nothing to add.", true); return; }
            bool ownFinish = rows.Any(IsWantRow);
            string finishText = finish == null || ownFinish ? "" : CardFinish.Display(finish) + " ";
            if ((plan.Count > 1 || cannot.Count > 0) &&
                !Confirm((ownFinish
                            ? OwnFinishQuestion("Add", qty, plan.Select(p => p.Finish).ToList(), "to")
                            : $"Put {qty} {finishText}{Copies(qty)} of each of the {plan.Count} selected cards on the Want List, for {(AddFor.Id.Length == 0 ? "the Collection" : AddFor.Name)}?") +
                         (skipped.Length > 0 ? "\n\n" + skipped : ""),
                         "Add to the Want List"))
                return;

            Run($"Added {qty} {finishText}to the Want List", plan.Select(p => p.Sid), () =>
                plan.Select(p => p.Pool == null
                    ? new EditResult { Message = $"{p.Name}: not in the card pool.", Warning = true }
                    : CollectionEditService.AddToWantList(p.Pool, p.Finish, qty, p.ForId, p.ForName)).ToList());
            if (skipped.Length > 0) ShowStatus(ActionText.Text + "  " + skipped, true);
        }

        private static string DefaultFinish(object? pool)
        {
            if (pool == null) return CardFinish.NonFoil;
            var (nf, f, _) = CollectionEditService.FinishesOf(CollectionEditService.CardsTable, pool);
            return nf ? CardFinish.NonFoil : f ? CardFinish.Foil : CardFinish.Etched;
        }

        // ── Remove ──────────────────────────────────────────────────────
        private void DoRemove(PoolPage? table, bool all)
        {
            var rows = Targets(table ?? _active);
            if (rows.Count == 0) { ShowStatus("Select a card first.", true); return; }
            if (TooMany(rows)) return;
            var plan = WantKeysOf(rows, out var problem);
            if (plan.Count == 0) { ShowStatus(problem ?? "Nothing on the Want List to remove.", true); return; }
            int qty = all ? int.MaxValue : Qty;
            if (all || plan.Count > 1)
            {
                string what = all ? "every wanted copy" : $"{qty} {Copies(qty)}";
                string of = plan.Count == 1 ? $"{plan[0].Name} ({CardFinish.Display(plan[0].Finish)})"
                    : $"each of the {plan.Count} selected rows ({FinishBreakdown(plan.Select(p => p.Finish))}), in each row's own finish";
                if (!Confirm($"Take {what} of {of} off the Want List?", "Remove from the Want List")) return;
            }
            Run(all ? "Removed from the Want List" : $"Removed {qty} from the Want List", plan.Select(p => p.Sid), () =>
                plan.Select(p => CollectionEditService.RemoveFromWantList(p.Sid, p.Finish, qty, p.Name, p.For)).ToList());
            if (problem != null && plan.Count > 0 && rows.Count > plan.Count) ShowStatus(ActionText.Text + "  " + problem, true);
        }

        // ── Got It (into the collection, claimed for the decks it was for) ──
        /// <summary>
        /// One printing + finish (one row, or a pool card): the Got It window —
        /// copies received, and a tick per want (decks first, then the Collection).
        /// Several rows, or "all": each row's copies go to its own want.
        /// </summary>
        private void DoGotIt(PoolPage? table, bool all)
        {
            var rows = Targets(table ?? _active);
            if (rows.Count == 0) { ShowStatus("Select a card first.", true); return; }
            if (TooMany(rows)) return;
            var plan = WantKeysOf(rows, out var problem);
            if (plan.Count == 0) { ShowStatus(problem ?? "Nothing on the Want List.", true); return; }
            string lang = AddLanguage, cond = AddCondition;

            // One card (in one finish), not "all": ask how many arrived and which wants they fill.
            var printings = plan.Select(p => (p.Sid, p.Finish)).Distinct().ToList();
            if (!all && printings.Count == 1)
            {
                var (sid, finish) = printings[0];
                var wants = CollectionEditService.WantsFor(sid, finish);
                if (wants.Count == 0) { ShowStatus($"{plan[0].Name} isn't on the Want List.", true); return; }
                // A want row picked: its want first, the rest after (still decks before the Collection).
                string? picked = plan.Count == 1 ? plan[0].For : null;
                if (picked != null)
                    wants = wants.OrderBy(w => w.ForDeckId == picked ? 0 : 1).ToList();
                var dlg = GotItDialog.Ask(Window.GetWindow(this),
                    $"{plan[0].Name} ({CardFinish.Display(finish)}) · into your collection as {lang} · {cond}",
                    wants, Math.Max(Qty, 1));
                if (dlg == null) return;
                Run("Got it", new[] { sid }, () => new List<EditResult>
                {
                    CollectionEditService.GotIt(sid, finish, dlg.Received, dlg.Assign, plan[0].Name, lang, cond),
                }, collection: true);
                return;
            }

            // Several rows: each row (its own deck or the Collection) gets Qty copies, or all it wants.
            int qty = all ? int.MaxValue : Qty;
            string what = all ? "every wanted copy" : $"{qty} {Copies(qty)}";
            if (!Confirm($"Got {what} of each of the {plan.Count} selected wants ({FinishBreakdown(plan.Select(p => p.Finish))})?\n\n" +
                         $"They go into your collection as {lang} · {cond}, are claimed for the decks they're for, " +
                         "and come off the Want List.", "Got It"))
                return;
            Run("Got it", plan.Select(p => p.Sid), () =>
                plan.Select(p =>
                {
                    var wants = CollectionEditService.WantsFor(p.Sid, p.Finish)
                        .Where(w => p.For == null || w.ForDeckId == p.For).ToList();
                    int wanted = wants.Sum(w => w.Wanted);
                    if (wanted <= 0) return new EditResult { Message = $"{p.Name}: no longer on the Want List.", Warning = true };
                    int received = Math.Min(qty, wanted);
                    var assign = new List<(WantNeed, int)>();
                    int left = received;
                    foreach (var w in wants)
                    {
                        int give = Math.Min(left, w.Wanted);
                        if (give > 0) assign.Add((w, give));
                        left -= give;
                    }
                    return CollectionEditService.GotIt(p.Sid, p.Finish, received, assign, p.Name, lang, cond);
                }).ToList(),
                collection: true);
            if (problem != null && rows.Count > plan.Count) ShowStatus(ActionText.Text + "  " + problem, true);
        }

        // ── Offer and notes ─────────────────────────────────────────────
        private void EditOfferInPlace(object row, DataGridCell? cell)
        {
            SetCurrent(row);
            var w = (WantListEntry)row;
            int id = w.WantListEntryId;
            EditPriceInPlace(cell, w.OfferPrice, $"Your offer per copy (market {w.PriceDisplay}).", ShowStatus,
                price => Run("Offer", new[] { w.ScryfallId }, () =>
                    new List<EditResult> { CollectionEditService.SetOfferPrice(id, price) }));
        }

        private void EditNotesInPlace(object row, DataGridCell? cell)
        {
            SetCurrent(row);
            var w = (WantListEntry)row;
            int id = w.WantListEntryId;
            EditPageKit.EditNotesInPlace(cell, w.Notes, "want", text => Run("Notes", new[] { w.ScryfallId }, () =>
                new List<EditResult> { CollectionEditService.SetWantNotes(id, text) }));
        }

        /// <summary>Right-click → Set Offer… / Edit Notes…: the editor over that cell (or at the mouse).</summary>
        private void EditWantCell(string header)
        {
            if (Targets(_bottom).FirstOrDefault(IsWantRow) is not { } row)
            {
                ShowStatus("Select a row in the Want List table first.", true);
                return;
            }
            AfterMenuCloses(this, () =>
            {
                var cell = _bottom.CellFor(row, header);
                if (header == "Offer") EditOfferInPlace(row, cell);
                else EditNotesInPlace(row, cell);
            });
        }

        private void ClearOffer()
        {
            var rows = Targets(_bottom).OfType<WantListEntry>().Where(w => w.OfferPrice.HasValue).ToList();
            if (rows.Count == 0) { ShowStatus("No offer to clear on the selected want rows.", false); return; }
            Run("Offer cleared", rows.Select(w => w.ScryfallId), () =>
                rows.Select(w => CollectionEditService.SetOfferPrice(w.WantListEntryId, null)).ToList());
        }

        // ── Undo ────────────────────────────────────────────────────────
        private void PushUndo(UndoStep step, string text)
        {
            _undo.Push(step, text);
            UpdateUndo();
        }

        private void DoUndo()
        {
            if (!_undo.TryPop(out var step, out var text)) { ShowStatus("Nothing to undo.", false); return; }
            var result = step.Usages != null
                ? CollectionEditService.RestoreWithUsages(step.Usages, step.Snaps, text)
                : CollectionEditService.RestoreAll(step.Snaps, text);
            UpdateUndo();
            AfterEdit(result, step.Snaps.SelectMany(s => s.Printings).Distinct().ToList());
        }

        private void UpdateUndo()
        {
            _undo.ShowOn(BtnUndo);
        }

        // ── After every change ──────────────────────────────────────────
        /// <summary>
        /// The status line, the pool rows' Owned counts (in place), and the
        /// want list re-read with the edited rows selected.
        /// </summary>
        private void AfterEdit(EditResult result, List<string> sids)
        {
            ShowStatus(result.Message, result.Warning);
            if (result.Changed <= 0) { RefreshCounts(); return; }

            var wanted = new HashSet<string>(sids);
            var poolRows = _top.FindAllLoaded(r => r is IOwnedCard && wanted.Contains(Str(r, "ScryfallId"))).ToArray();
            if (poolRows.Length > 0)
            {
                OwnedCountService.Fill(PoolTag, poolRows);
                _top.RefreshGalleryCards(poolRows);
            }

            var keys = new HashSet<RowKey>(result.Touched);
            _bottom.ReloadRows(r => keys.Contains(WantKeyOf(r)), r => wanted.Contains(Str(r, "ScryfallId")));
            RefreshCounts();
        }

        private void ShowStatus(string text, bool warning) => EditPageKit.ShowStatus(ActionText, text, warning);

        // ── Buttons ─────────────────────────────────────────────────────
        private void BtnAddNonFoil_Click(object sender, RoutedEventArgs e) => DoAdd(CardFinish.NonFoil);
        private void BtnAddFoil_Click(object sender, RoutedEventArgs e) => DoAdd(CardFinish.Foil);
        private void BtnAddEtched_Click(object sender, RoutedEventArgs e) => DoAdd(CardFinish.Etched);
        private void BtnRemove_Click(object sender, RoutedEventArgs e) => DoRemove(null, all: false);
        private void BtnGotIt_Click(object sender, RoutedEventArgs e) => DoGotIt(null, all: false);
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

            // Top: the pool.
            Item(_topMenu, "Add Non-Foil", "Enter", () => DoAdd(CardFinish.NonFoil, _top), () => BtnAddNonFoil.IsEnabled, true);
            Item(_topMenu, "Add Foil", "Shift+Enter", () => DoAdd(CardFinish.Foil, _top), () => BtnAddFoil.IsEnabled, true);
            Item(_topMenu, "Add Etched", "Ctrl+Enter", () => DoAdd(CardFinish.Etched, _top), () => BtnAddEtched.IsEnabled, true);
            Item(_topMenu, "Remove from the Want List", "", () => DoRemove(_top, all: false), () => BtnRemove.IsEnabled, true);
            _topMenu.Items.Add(new Separator());
            Item(_topMenu, "Got It", "", () => DoGotIt(_top, all: false), () => BtnGotIt.IsEnabled, true);
            _topMenu.Items.Add(new Separator());
            _undoTop = Item(_topMenu, "Undo", "Ctrl+Z", DoUndo, () => _undo.Count > 0);

            // Bottom: the Want List.
            Item(_bottomMenu, "Add more", "", () => DoAdd(null, _bottom), qty: true);
            Item(_bottomMenu, "Remove", "Delete", () => DoRemove(_bottom, all: false), () => BtnRemove.IsEnabled, true);
            Item(_bottomMenu, "Remove all of the selected rows", "Shift+Delete", () => DoRemove(_bottom, all: true), () => BtnRemove.IsEnabled);
            _bottomMenu.Items.Add(new Separator());
            Item(_bottomMenu, "Got It", "", () => DoGotIt(_bottom, all: false), () => BtnGotIt.IsEnabled, true);
            Item(_bottomMenu, "Got It — all of the selected rows", "", () => DoGotIt(_bottom, all: true), () => BtnGotIt.IsEnabled);
            _bottomMenu.Items.Add(new Separator());
            _forMenu = new MenuItem { Header = "For" };
            _bottomMenu.Items.Add(_forMenu);
            _bottomMenu.Items.Add(new Separator());
            Item(_bottomMenu, "Set Offer…", "", () => EditWantCell("Offer"));
            Item(_bottomMenu, "Clear Offer", "", ClearOffer);
            Item(_bottomMenu, "Edit Notes…", "", () => EditWantCell("Notes"));
            _bottomMenu.Items.Add(new Separator());
            _undoBottom = Item(_bottomMenu, "Undo", "Ctrl+Z", DoUndo, () => _undo.Count > 0);

            _topMenu.Opened += (_, _) => MenuOpened(_top);
            _bottomMenu.Opened += (_, _) => MenuOpened(_bottom);
            _top.SetRowContextMenu(_topMenu);
            _bottom.SetRowContextMenu(_bottomMenu);
        }

        private MenuItem? _forMenu;

        /// <summary>For ▸ Collection / each deck (the current one ticked).</summary>
        private void FillForMenu()
        {
            if (_forMenu == null) return;
            _forMenu.Items.Clear();
            var rows = Targets(_bottom).OfType<WantListEntry>().ToList();
            _forMenu.IsEnabled = rows.Count > 0;
            var current = rows.Select(w => w.ForDeckId ?? "").Distinct().ToList();
            foreach (var choice in _forChoices)
            {
                var mi = new MenuItem
                {
                    Header = choice.Name,
                    IsCheckable = false,
                    IsChecked = current.Count == 1 && current[0] == choice.Id,
                };
                var c = choice;
                mi.Click += (_, _) => SetFor(c);
                _forMenu.Items.Add(mi);
                if (choice.Id.Length == 0 && _forChoices.Count > 1) _forMenu.Items.Add(new Separator());
            }
        }

        private void MenuOpened(PoolPage table)
        {
            _active = table;
            if (table == _bottom) FillForMenu();
            if (table.SelectedCard is { } card) SetCurrent(card);
            else UpdateButtons();

            int q = Qty;
            foreach (var entry in _menuEntries)
            {
                entry.Item.Header = entry.ShowsQty && q > 1 ? $"{entry.Header} ×{q}" : entry.Header;
                entry.Item.IsEnabled = entry.Enabled();
            }
            string undoText = _undo.MenuText;
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
            DoRemove(_bottom, all: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
        }

        // ── Helpers ─────────────────────────────────────────────────────

        /// <summary>The key of a want row (printing + finish); pool rows have none.</summary>
        private static RowKey WantKeyOf(object row) =>
            CollectionEditService.WantKey(Str(row, "ScryfallId"), IsWantRow(row) ? FinishOf(row) : "");

        /// <summary>Etched-only printing? From the pool card when it is one, else the want row's flag.</summary>
        private static bool EtchedOnly(object row)
        {
            if (row is IOwnedCard)
            {
                var (_, foil, etched) = CollectionEditService.FinishesOf(CollectionEditService.CardsTable, row);
                return etched && !foil;
            }
            return Bool(row, "PrintingEtchedOnly");
        }
    }
}