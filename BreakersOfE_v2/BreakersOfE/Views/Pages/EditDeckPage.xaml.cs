using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BreakersOfE.Models;
using BreakersOfE.Services;
using BreakersOfE.Views.Dialogs;

namespace BreakersOfE.Views.Pages
{
    /// <summary>Which Edit → Decks item the page is.</summary>
    public enum DeckEditMode
    {
        /// <summary>Pool → Deck: build from the card pool (nothing claimed).</summary>
        Pool,
        /// <summary>Collection → Deck: build from your collection (copies claimed).</summary>
        Collection,
        /// <summary>Deck → Collection: the deck on top; claim or add its copies.</summary>
        DeckToCollection,
    }

    /// <summary>
    /// Edit → Decks: Pool → Deck, Collection → Deck and Deck → Collection. Two
    /// tables (the same as View, embedded) and the card panel on the left:
    /// the source (card pool or collection) and the deck — the deck is on top
    /// in Deck → Collection, below otherwise.
    ///
    /// The deck file is a pure card list. Decks CLAIM collection copies
    /// (collection.db only — CollectionEditService.Claims): Collection → Deck
    /// claims the row a card comes from; Deck → Collection claims copies you
    /// own or adds new ones. A deck never claims more than it lists: after
    /// every change the extra claims are freed. Each deck line is one printing
    /// in one part of the deck (command zone, main deck, sideboard, tokens)
    /// with a count per finish (Non-Foil, Foil, Etched).
    ///
    /// Every change goes through <see cref="Edit"/>: the deck file as it was
    /// (for Undo) → DeckEditService → save at once → the deck table re-read,
    /// the changed lines selected. Adding checks the deck's rules first
    /// (<see cref="DeckRulesService.AddWarnings"/>: copies by card NAME across
    /// every printing, legality, colors, size) and asks "Add anyway?" — a
    /// warning, never a block.
    ///
    /// Keys: pool table Enter = add the default finish, Shift+Enter = foil,
    /// Ctrl+Enter = etched; deck table Delete = remove Qty, Shift+Delete =
    /// remove the card(s); Ctrl+Q = Qty box; Ctrl+Z = Undo.
    /// </summary>
    public partial class EditDeckPage : Page
    {
        // The tables. The card pool has its own (loaded once — 100,000 cards),
        // shown on top in Pool → Deck. The other two swap roles with the mode:
        // the deck is the upper one in Deck → Collection, the lower otherwise.
        private readonly PoolPage _poolPage = new();
        private readonly PoolPage _upper = new();
        private readonly PoolPage _lower = new();
        private DeckEditMode _mode = DeckEditMode.Pool;
        private PoolPage _src => _mode switch
        {
            DeckEditMode.Pool => _poolPage,
            DeckEditMode.DeckToCollection => _lower,
            _ => _upper,
        };
        private PoolPage _deckT => _mode == DeckEditMode.DeckToCollection ? _upper : _lower;
        /// <summary>The source table is your collection (copies are claimed).</summary>
        private bool FromCollection => _mode != DeckEditMode.Pool;
        private bool _started;

        // The deck being edited (as read from its file after the last change).
        private Deck? _deck;
        private string? _deckPath;
        private bool _fillingDecks;

        // The table the user worked in last, and the card shown.
        private PoolPage? _active;
        private object? _current;

        private readonly ContextMenu _srcMenu = new();
        private readonly ContextMenu _deckMenu = new();

        // Undo, per deck file: the file before and after each change, and the
        // deck's collection claims (with the rows they touch) before it.
        private const int UndoSteps = 30;
        private sealed record UndoStep(string Before, string After, string Text, ClaimSnapshot? Claims);
        private static readonly Dictionary<string, List<UndoStep>> _undo = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>One deck in the Deck list.</summary>
        private sealed record DeckChoice(string Path, string Name, string Label)
        {
            public override string ToString() => Label;
        }

        public EditDeckPage()
        {
            InitializeComponent();

            // The pool and upper tables start as card pictures (+ / − on each),
            // the lower as a grid; each table has its own Grid / Gallery button, remembered.
            _poolPage.SetEmbedded(galleryByDefault: true);
            _upper.SetEmbedded(galleryByDefault: true);
            _lower.SetEmbedded(galleryByDefault: false);
            TopFrame.Content = _poolPage;
            BottomFrame.Content = _lower;
            // Switching the top table (Pool ↔ the others) navigates the frame: keep
            // no history, so Back (mouse button, Alt+Left) can't bring the other table back.
            TopFrame.Navigated += (_, _) => { while (TopFrame.CanGoBack) TopFrame.RemoveBackEntry(); };
            TopFrame.Navigating += (_, e) =>
            {
                if (e.NavigationMode != System.Windows.Navigation.NavigationMode.New) e.Cancel = true;
            };

            foreach (var table in new[] { _poolPage, _upper, _lower })
            {
                var t = table;
                t.SelectedCardChanged += card => OnSelected(t, card);
                // Gallery tiles: + adds Qty (Shift = foil, Ctrl = etched), − removes.
                t.GalleryAdd += (card, keys) => { _active = t; SetCurrent(card); TileAdd(t, keys); };
                t.GalleryRemove += (card, keys) => { _active = t; SetCurrent(card); TileRemove(t, keys); };
                t.GridPreviewKeyDown += (s, e) =>
                {
                    if (t == _src) Src_GridPreviewKeyDown(s, e);
                    else Deck_GridPreviewKeyDown(s, e);
                };
                // Double-click a Non-Foil / Foil / Etched cell of a deck line to type its count.
                t.CellDoubleClickHandler = DeckCellDoubleClick;
                // A collection table re-read after a claim: the row shown is a new object now.
                t.ItemsReloaded += () =>
                {
                    if (t != _src || _current == null || !IsCollectionRow(_current)) return;
                    var key = RowKeyOf(_current);
                    var now = t.SelectedCard is { } sel && IsCollectionRow(sel) && RowKeyOf(sel) == key ? sel
                            : t.FindLoaded(r => IsCollectionRow(r) && RowKeyOf(r) == key);
                    if (now != null && !ReferenceEquals(now, _current)) { _current = now; UpdateButtons(); }
                };
            }

            AddToBox.ItemsSource = new[] { "Main deck", "Sideboard" };
            AddToBox.SelectedIndex = 0;
            LanguageBox.ItemsSource = CardLanguage.All;
            LanguageBox.SelectedItem = CardLanguage.Default;
            ConditionBox.ItemsSource = CardCondition.All;
            ConditionBox.SelectedItem = CardCondition.Default;

            BuildMenus();
            UpdateButtons();
            UpdateUndo();
        }

        /// <summary>Double-click a Non-Foil / Foil / Etched cell of a deck line (not in Deck → Collection).</summary>
        private bool DeckCellDoubleClick(object row, string header, DataGridCell cell)
        {
            if (row is not DeckCard line || _mode == DeckEditMode.DeckToCollection) return false;
            string? finish = header switch
            {
                "Non-Foil" => CardFinish.NonFoil,
                "Foil" => CardFinish.Foil,
                "Etched" => CardFinish.Etched,
                _ => null,
            };
            if (finish == null) return false;
            EditCountInPlace(line, cell, finish);
            return true;
        }

        /// <summary>The source table's tag: the card or token pool, or collection.</summary>
        private string SourceTag => (FromCollection, PoolBox.SelectedIndex == 1) switch
        {
            (true, true) => CollectionEditService.TokensTable,
            (true, false) => CollectionEditService.CardsTable,
            (false, true) => "Tokens",
            _ => "Cards",
        };

        /// <summary>
        /// Called each time the page is shown (Edit → Decks → one of the three).
        /// Always starts with no deck open: nothing can be added, removed or
        /// claimed by accident until a deck is picked.
        /// </summary>
        public void Start(DeckEditMode mode, string? openDeckPath = null)
        {
            bool first = !_started;
            _mode = mode;
            if (first)
            {
                _started = true;
                PoolBox.ItemsSource = new[] { "Cards", "Tokens" };
                _fillingPool = true;
                PoolBox.SelectedIndex = 0;
                _fillingPool = false;
            }
            _active = null;
            SetCurrentNone();
            ShowStatus("", false);
            ShowModeControls();
            TopFrame.Content = _mode == DeckEditMode.Pool ? _poolPage : _upper;
            SetMenus();
            // The pool loads once (100,000 cards; again only for Cards ↔ Tokens);
            // a collection is re-read each time (it may have changed on another page).
            if (FromCollection || _src.CurrentTag != SourceTag) _src.LoadPool(SourceTag);
            FillDecks(openDeckPath);          // a deck asked for (from a "Used in" table), else none
        }

        /// <summary>What each mode shows: its buttons, and where the source comes from.</summary>
        private void ShowModeControls()
        {
            bool claim = _mode == DeckEditMode.DeckToCollection;
            EditBar.Visibility = claim ? Visibility.Collapsed : Visibility.Visible;
            ClaimBar.Visibility = claim ? Visibility.Visible : Visibility.Collapsed;
            // Token suggestions only where the collection is involved (the cards are yours).
            BtnSuggestTokens.Visibility = FromCollection ? Visibility.Visible : Visibility.Collapsed;
            PoolLabel.Text = FromCollection ? "Collection" : "Pool";
        }

        // ══════════════════════════════════════════════════════════════════
        // THE DECK
        // ══════════════════════════════════════════════════════════════════
        private DeckFormatRule Rule => _deck != null ? DeckFormats.For(_deck) : DeckFormats.For(DeckType.Standard);

        /// <summary>The Deck list: every deck file, grouped by type (DeckFormats order), A→Z.</summary>
        private void FillDecks(string? select)
        {
            var order = DeckFormats.All.ToList();
            var choices = DeckIndexService.AllDecks()
                .Select(d =>
                {
                    var rule = DeckFormats.For(d.Deck.DeckType);
                    string name = string.IsNullOrWhiteSpace(d.Deck.Name) ? Path.GetFileNameWithoutExtension(d.Path) : d.Deck.Name;
                    string type = rule.Type == DeckType.Standard
                        ? $"Constructed — {DeckFormats.Constructed(d.Deck.ConstructedFormat).Name}" : rule.Name;
                    return (order: order.IndexOf(rule), choice: new DeckChoice(d.Path, name, $"{name}   ·  {type}"));
                })
                .OrderBy(x => x.order)
                .ThenBy(x => x.choice.Name, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.choice)
                .ToList();
            // First entry: no deck open (nothing can be added or removed).
            choices.Insert(0, NoDeck);

            _fillingDecks = true;
            try
            {
                DeckBox.ItemsSource = choices;
                TextSearch.SetTextPath(DeckBox, nameof(DeckChoice.Name));
                DeckBox.SelectedItem = choices.FirstOrDefault(c => select != null &&
                    string.Equals(c.Path, select, StringComparison.OrdinalIgnoreCase)) ?? NoDeck;
            }
            finally
            {
                _fillingDecks = false;
            }

            if (DeckBox.SelectedItem is DeckChoice picked && picked != NoDeck) OpenDeck(picked.Path);
            else ShowNoDeck(choices.Count == 1
                ? "No decks yet — click New Deck… to start one."
                : "No deck open. Pick a deck above, or click New Deck… to start one.");
        }

        /// <summary>The Deck list's blank entry.</summary>
        private static readonly DeckChoice NoDeck = new("", "(No deck)", "(No deck)");

        /// <summary>Close Deck: back to no deck open. Everything is already saved.</summary>
        private void BtnCloseDeck_Click(object sender, RoutedEventArgs e)
        {
            string? name = _deck?.Name;
            _fillingDecks = true;
            try { DeckBox.SelectedItem = NoDeck; }
            finally { _fillingDecks = false; }
            ShowNoDeck("No deck open. Pick a deck above, or click New Deck… to start one.");
            if (name != null) ShowStatus($"Closed \"{name}\" (it was already saved).", false);
        }

        private void DeckBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_fillingDecks) return;
            if (ReferenceEquals(DeckBox.SelectedItem, NoDeck))
            {
                BtnCloseDeck_Click(sender, e);
                return;
            }
            if (DeckBox.SelectedItem is not DeckChoice c || OpenDeck(c.Path)) return;

            // Couldn't open it (the status line says why): the list goes back
            // to the deck still open, so no edit lands in the wrong deck.
            _fillingDecks = true;
            try
            {
                DeckBox.SelectedItem = (DeckBox.ItemsSource as IEnumerable<DeckChoice>)?.FirstOrDefault(d =>
                    _deckPath != null && string.Equals(d.Path, _deckPath, StringComparison.OrdinalIgnoreCase)) ?? NoDeck;
            }
            finally
            {
                _fillingDecks = false;
            }
        }

        private void ShowNoDeck(string message)
        {
            _deck = null;
            _deckPath = null;
            SetCurrentNone();
            _deckT.ShowNoDeck(message);
            ShowAddTo();
            UpdateButtons();
            UpdateUndo();
        }

        /// <summary>
        /// Read the deck from its file and show it. After an edit,
        /// <paramref name="select"/> picks the lines to select again.
        /// </summary>
        private bool OpenDeck(string path, Func<object, bool>? select = null, Func<object, bool>? fallback = null)
        {
            Deck? deck;
            try
            {
                deck = DeckService.Load(path);
            }
            catch (Exception ex)
            {
                ShowStatus($"Could not open the deck: {ex.Message}", true);
                return false;
            }
            if (deck == null) { ShowStatus("Could not open the deck.", true); return false; }
            // Claims are keyed on the deck's id: an older deck gets one now.
            if (string.IsNullOrWhiteSpace(deck.DeckId))
            {
                try { deck.FilePath = path; DeckService.Save(deck); }
                catch (Exception ex) { ShowStatus($"Could not save the deck: {ex.Message}", true); return false; }
            }
            // A deck never claims more than it lists (the file may have changed elsewhere).
            CollectionEditService.SyncClaims(deck);
            // Lines without a ScryfallId (very old files) can't be told apart: not editable here.
            int unkeyed = deck.Cards.Count(c => string.IsNullOrEmpty(c.ScryfallId));

            bool other = !string.Equals(path, _deckPath, StringComparison.OrdinalIgnoreCase);
            _deck = deck;
            _deckPath = path;
            if (other)
            {
                // Another deck: the card shown belonged to the old one (unless it's a pool card).
                if (_current is DeckCard) SetCurrentNone();
                ShowStatus("", false);
            }
            else if (_current is DeckCard old)
            {
                // Same deck re-read: point at the new copy of the line shown —
                // or another line of that printing if it was removed or merged.
                var now = DeckEditService.FindLine(deck, DeckLineKey.Of(old))
                          ?? deck.Cards.FirstOrDefault(c => !string.IsNullOrEmpty(old.ScryfallId) &&
                                 string.Equals(c.ScryfallId, old.ScryfallId, StringComparison.OrdinalIgnoreCase));
                if (now != null) { _current = now; Detail.ShowCard(now); }
                else SetCurrentNone();
            }
            ShowAddTo();
            _deckT.ShowDeck(deck, path, select, fallback);
            UpdateButtons();
            UpdateUndo();
            if (unkeyed > 0 && other)
                ShowStatus($"{unkeyed} line(s) in this deck have no Scryfall ID (an old file) — they can't be changed here.", true);
            return true;
        }

        /// <summary>"Add to": main deck or sideboard — only for formats that have a sideboard.</summary>
        private void ShowAddTo()
        {
            var rule = Rule;
            bool side = _deck != null && _mode != DeckEditMode.DeckToCollection &&
                        (rule.SideboardMax != null || rule.Type == DeckType.Limited);
            AddToLabel.Visibility = AddToBox.Visibility = side ? Visibility.Visible : Visibility.Collapsed;
            BtnDeckSettings.IsEnabled = BtnCloseDeck.IsEnabled = BtnSuggestTokens.IsEnabled =
                BtnTearDown.IsEnabled = BtnWholeOwned.IsEnabled = BtnWholeNew.IsEnabled = _deck != null;
        }

        private DeckCardCategory AddSection =>
            AddToBox.Visibility == Visibility.Visible && AddToBox.SelectedIndex == 1
                ? DeckCardCategory.Sideboard : DeckCardCategory.Mainboard;

        private void AddToBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

        // ── New deck / settings ─────────────────────────────────────────
        private void BtnNewDeck_Click(object sender, RoutedEventArgs e)
        {
            var dlg = DeckSettingsDialog.AskNew(Window.GetWindow(this));
            if (dlg == null) return;
            var deck = DeckService.CreateNew(dlg.DeckName, dlg.DeckType);
            deck.ConstructedFormat = dlg.ConstructedFormat;
            deck.Description = dlg.Description;
            deck.FilePath = DeckService.NewDeckPath(dlg.DeckName);
            try
            {
                DeckService.Save(deck);
            }
            catch (Exception ex)
            {
                ShowStatus($"Could not create the deck: {ex.Message}", true);
                return;
            }
            FillDecks(deck.FilePath);
            ShowStatus($"New deck \"{deck.Name}\" ({DeckFormats.For(deck).Name}) — saved as {Path.GetFileName(deck.FilePath)}.", false);
        }

        private void BtnDeckSettings_Click(object sender, RoutedEventArgs e)
        {
            if (_deck == null || _deckPath == null) return;
            var dlg = DeckSettingsDialog.AskEdit(Window.GetWindow(this), _deck);
            if (dlg == null) return;

            string before = ReadFile(_deckPath) ?? DeckService.ToJson(_deck);
            string oldPath = _deckPath;
            bool renamed = dlg.DeckName != _deck.Name;
            _deck.Name = dlg.DeckName;
            _deck.DeckType = dlg.DeckType;
            _deck.ConstructedFormat = dlg.ConstructedFormat;
            _deck.Description = dlg.Description;

            // A new name renames the file too (same folder), unless that name is taken.
            string path = oldPath;
            if (renamed)
            {
                string dir = Path.GetDirectoryName(oldPath) ?? AppFolderService.DecksFolder;
                string wanted = Path.Combine(dir, AppFolderService.SafeFileName(dlg.DeckName) + ".deck");
                if (!File.Exists(wanted)) path = wanted;
            }
            try
            {
                _deck.FilePath = path;
                DeckService.Save(_deck);
                if (!string.Equals(path, oldPath, StringComparison.OrdinalIgnoreCase)) File.Delete(oldPath);
            }
            catch (Exception ex)
            {
                ShowStatus($"Could not save the deck: {ex.Message}", true);
                FillDecks(oldPath);
                return;
            }

            // Undo follows the deck to its new file name.
            if (_undo.Remove(oldPath, out var stack)) _undo[path] = stack;
            if (ReadFile(path) is { } after) PushUndo(path, new UndoStep(before, after, "Deck settings", null));
            _deckPath = path;
            FillDecks(path);
            ShowStatus($"Deck settings saved: {_deck?.Name} — {Rule.Name}.", false);
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
            UpdateButtons();
        }

        private void SetCurrentNone()
        {
            _current = null;
            Detail.ShowCard(null);
        }

        // Pool printings by table + ScryfallId (finishes and legality), looked up once each.
        private readonly Dictionary<(bool Token, string Sid), object?> _poolCache = new();

        /// <summary>The pool printing (PoolCard, or TokenCard when <paramref name="token"/>).</summary>
        private object? PoolFor(string sid, bool token = false)
        {
            if (string.IsNullOrEmpty(sid)) return null;
            if (_poolCache.TryGetValue((token, sid), out var hit)) return hit;
            var found = CollectionEditService.FindPoolCard(
                token ? CollectionEditService.TokensTable : CollectionEditService.CardsTable, sid);
            if (_poolCache.Count > 5000) _poolCache.Clear();
            return _poolCache[(token, sid)] = found;
        }

        /// <summary>A row of your collection (cards or tokens) in the source table.</summary>
        private static bool IsCollectionRow(object row) => row is CollectionEntry || row is TokenCollectionEntry;

        /// <summary>A collection row's finish as shown (v1 foil of an etched-only printing = etched).</summary>
        private static string RowFinish(object row) =>
            CardFinish.Normalize(Str(row, "ShownFinish") is { Length: > 0 } shown ? shown : Str(row, "Finish"));

        private static int Int(object o, string prop) =>
            o.GetType().GetProperty(prop)?.GetValue(o) is int i ? i : 0;

        /// <summary>Free copies of a collection row (owned, not claimed by any deck).</summary>
        private static int FreeOf(object row) => Math.Max(0, Int(row, "Quantity") - Math.Max(0, Int(row, "UsedCount")));

        /// <summary>
        /// What a row adds to the deck: a pool card becomes a deck card; a deck
        /// line adds more of itself (finishes and legality from the pool when found).
        /// </summary>
        private DeckCard? TemplateFor(object row)
        {
            if (row is PoolCard p) return DeckService.FromPoolCard(p);
            if (row is TokenCard t) return DeckService.FromTokenCard(t);
            if (IsCollectionRow(row))
                return PoolFor(Str(row, "ScryfallId"), row is TokenCollectionEntry) switch
                {
                    PoolCard pc => DeckService.FromPoolCard(pc),
                    TokenCard tc => DeckService.FromTokenCard(tc),
                    _ => null,
                };
            if (row is DeckCard line)
                return PoolFor(line.ScryfallId, line.IsTokenLine) switch
                {
                    PoolCard pc => DeckService.FromPoolCard(pc),
                    TokenCard tc => DeckService.FromTokenCard(tc),
                    _ => line,
                };
            return null;
        }

        /// <summary>A deck line's own finish for + / − / Delete: the one it has (non-foil first).</summary>
        private static string LineFinish(DeckCard line) =>
            line.Quantity > 0 ? CardFinish.NonFoil : line.FoilQuantity > 0 ? CardFinish.Foil
            : line.EtchedQuantity > 0 ? CardFinish.Etched : CardFinish.NonFoil;

        /// <summary>Default finish of a printing: non-foil, else foil, else etched.</summary>
        private static string DefaultFinish(DeckCard card) =>
            card.IsNonFoil ? CardFinish.NonFoil : card.IsFoil ? CardFinish.Foil
            : card.IsEtched ? CardFinish.Etched : CardFinish.NonFoil;

        /// <summary>The deck's lines of one printing.</summary>
        private List<DeckCard> LinesOf(string sid) =>
            _deck?.Cards.Where(c => string.Equals(c.ScryfallId, sid, StringComparison.OrdinalIgnoreCase)).ToList()
            ?? new List<DeckCard>();

        private void UpdateButtons()
        {
            bool haveDeck = _deck != null;
            int selected = _active?.SelectedCount ?? 0;
            bool many = selected > 1;
            var tmpl = _current != null ? TemplateFor(_current) : null;
            var lines = _current != null ? LinesOf(Str(_current, "ScryfallId")) : new List<DeckCard>();
            if (_current is DeckCard cur) lines = lines.Where(l => DeckLineKey.Of(l) == DeckLineKey.Of(cur)).ToList();

            // A collection row is one finish (and only its free copies can go in).
            bool row = _current != null && IsCollectionRow(_current);
            string rowFin = row ? RowFinish(_current!) : "";
            bool rowFree = row && FreeOf(_current!) > 0;
            BtnAddNonFoil.IsEnabled = haveDeck && (many || (row ? rowFree && rowFin == CardFinish.NonFoil : tmpl?.IsNonFoil == true));
            BtnAddFoil.IsEnabled = haveDeck && (many || (row ? rowFree && rowFin == CardFinish.Foil : tmpl?.IsFoil == true));
            BtnAddEtched.IsEnabled = haveDeck && (many || (row ? rowFree && rowFin == CardFinish.Etched : tmpl?.IsEtched == true));
            BtnRemoveNonFoil.IsEnabled = haveDeck && (many || lines.Any(l => l.Quantity > 0));
            BtnRemoveFoil.IsEnabled = haveDeck && (many || lines.Any(l => l.FoilQuantity > 0));
            BtnRemoveEtched.IsEnabled = haveDeck && (many || lines.Any(l => l.EtchedQuantity > 0));
            BtnRemoveLine.IsEnabled = haveDeck && (many || lines.Count > 0);

            // Deck → Collection: claim buttons (the deck's selected lines, or one collection row).
            var deckLines = _mode == DeckEditMode.DeckToCollection ? SelectedDeckLines() : new List<DeckCard>();
            BtnUseOwned.IsEnabled = BtnAddNew.IsEnabled = haveDeck && deckLines.Any(l => l.ClaimedCount < l.TotalQuantity);
            BtnFreeCopies.IsEnabled = haveDeck && deckLines.Any(l => l.ClaimedCount > 0);
            BtnUseRow.IsEnabled = haveDeck && _current != null && IsCollectionRow(_current) && FreeOf(_current) > 0;

            string title = _mode switch
            {
                DeckEditMode.Collection => "Collection → Deck",
                DeckEditMode.DeckToCollection => "Deck → Collection",
                _ => "Pool → Deck",
            };
            string where = _mode == DeckEditMode.Pool ? "pool" : "collection";
            if (!haveDeck)
                ModeText.Text = _mode switch
                {
                    DeckEditMode.Collection => $"{title}. Pick a deck (or make a new one), then add cards from your collection — the copies are claimed for the deck.",
                    DeckEditMode.DeckToCollection => $"{title}. Pick a deck, then claim copies you own for it, or add its cards to your collection as new copies.",
                    _ => $"{title}. Pick a deck (or make a new one), then add cards from the pool table.",
                };
            else if (_current == null)
                ModeText.Text = $"{title} · {_deck!.Name} ({Rule.Name}) · {ClaimSummary()}. Select a card in either table " +
                                "(Ctrl+click or Shift+click for several).";
            else if (many)
                ModeText.Text = $"{selected} rows selected in the {(_active == _deckT ? "deck" : where)} table" +
                                (_active == _src && _mode != DeckEditMode.DeckToCollection
                                    ? $"  ·  adds go to the {DeckEditService.SectionName(AddSection)}" : "");
            else
                ModeText.Text = $"{CardText(_current)}  ·  {InDeckText(_current)}" +
                                (IsCollectionRow(_current) ? $"  ·  this row: {Int(_current, "Quantity")} owned, {FreeOf(_current)} free" : "");
        }

        /// <summary>
        /// "Claimed from your collection: Main 60 of 60 · Sideboard 15 of 15 ·
        /// Tokens 0 of 4" — each part apart (the command zone counts with the
        /// main deck; Sideboard and Tokens only when the deck has them).
        /// </summary>
        private string ClaimSummary()
        {
            if (_deck == null) return "";
            string Part(string name, List<DeckCard> lines) =>
                $"{name} {lines.Sum(c => c.ClaimedCount)} of {lines.Sum(c => c.TotalQuantity)}";
            var main = _deck.Cards.Where(c => !c.IsTokenLine && c.Category != DeckCardCategory.Sideboard).ToList();
            var side = _deck.Cards.Where(c => c.Category == DeckCardCategory.Sideboard).ToList();
            var tokens = _deck.Cards.Where(c => c.IsTokenLine).ToList();
            var parts = new List<string> { Part("Main", main) };
            if (side.Count > 0) parts.Add(Part("Sideboard", side));
            if (tokens.Count > 0) parts.Add(Part("Tokens", tokens));
            return "Claimed from your collection: " + string.Join("  ·  ", parts);
        }

        /// <summary>The deck table's selected lines (or the current one).</summary>
        private List<DeckCard> SelectedDeckLines() => Targets(_deckT).OfType<DeckCard>().ToList();

        /// <summary>
        /// "in this deck: 2 Non-Foil (main deck) · 4 named Lightning Bolt, any
        /// printing (Constructed allows 4)".
        /// </summary>
        private string InDeckText(object card)
        {
            if (_deck == null) return "";
            var lines = LinesOf(Str(card, "ScryfallId"));
            string printing = lines.Count == 0
                ? "not in this deck"
                : "in this deck: " + string.Join(", ", lines.Select(l =>
                    $"{FinishCounts(l)} ({DeckEditService.SectionName(DeckEditService.SectionOf(l))})"));

            if (IsToken(card)) return $"{printing}  ·  tokens aren't part of the deck (no rules apply)";

            var rule = Rule;
            string name = Str(card, "Name");
            string key = DeckIndexService.CardKey(name);
            var play = _deck.PlayCards;
            var counted = rule.SideboardMax == null
                ? play.Where(c => c.Category != DeckCardCategory.Sideboard)
                : play;
            int byName = counted.Where(c => string.Equals(DeckIndexService.CardKey(c.Name), key, StringComparison.OrdinalIgnoreCase))
                                .Sum(c => c.TotalQuantity);
            string limit = rule.CopyLimit == 0 ? "no copy limit"
                : $"{rule.Name} allows {(rule.CopyLimit == 1 ? "1" : rule.CopyLimit.ToString())}";
            return $"{printing}  ·  {byName} named {key}, any printing ({limit})";
        }

        private static string FinishCounts(DeckCard l)
        {
            var parts = new List<string>();
            if (l.Quantity > 0) parts.Add($"{l.Quantity} Non-Foil");
            if (l.FoilQuantity > 0) parts.Add($"{l.FoilQuantity} Foil");
            if (l.EtchedQuantity > 0) parts.Add($"{l.EtchedQuantity} Etched");
            return parts.Count == 0 ? "0" : string.Join(" + ", parts);
        }

        // ══════════════════════════════════════════════════════════════════
        // ACTIONS — every gesture ends up in Edit()
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

        /// <summary>At most this many rows per action (Ctrl+A on the pool would be 100,000).</summary>
        private const int MaxRows = 250;

        /// <summary>The rows an action works on: the selected rows of that table.</summary>
        private List<object> Targets(PoolPage? table)
        {
            table ??= _active;
            if (table != null)
            {
                var rows = table.SelectedCards;
                if (rows.Count > 0) return rows;
            }
            if (_current == null) return new List<object>();
            bool fits = table == null || (table == _deckT) == (_current is DeckCard);
            return fits ? new List<object> { _current } : new List<object>();
        }

        private bool Ready(List<object> rows)
        {
            if (_deck == null) { ShowStatus("Pick a deck first (or click New Deck…).", true); return false; }
            if (rows.Count == 0) { ShowStatus("Select a card first.", true); return false; }
            if (rows.Count > MaxRows)
            {
                ShowStatus($"{rows.Count:N0} rows are selected — select {MaxRows:N0} or fewer for one change.", true);
                return false;
            }
            return true;
        }

        private bool Confirm(string text, string title) =>
            MessageBox.Show(Window.GetWindow(this), text, title,
                MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

        /// <summary>
        /// The deck file as it is (for Undo) → the change (on the deck in
        /// memory) → save → Undo step → the deck table re-read. A null list
        /// from <paramref name="work"/> means "cancelled": the deck is re-read
        /// from its file and nothing changes.
        /// </summary>
        private void Edit(string what, Func<List<DeckEditResult>?> work,
                          IEnumerable<(string Table, string Sid)>? claims = null)
        {
            if (_deck == null || _deckPath == null) return;
            string before = ReadFile(_deckPath) ?? DeckService.ToJson(_deck);
            // The deck's claims and the collection rows they touch, for Undo.
            var claimSnap = CollectionEditService.TakeClaimSnapshot(_deck,
                claims ?? Enumerable.Empty<(string, string)>());

            var results = work();
            if (results == null)
            {
                OpenDeck(_deckPath);                     // drop anything done in memory
                ShowStatus("Nothing was changed.", false);
                return;
            }
            var result = Combine(what, results);
            bool claimsChanged = false;
            if (result.Changed > 0)
            {
                try
                {
                    _deck.FilePath = _deckPath;
                    DeckService.Save(_deck);
                }
                catch (Exception ex)
                {
                    OpenDeck(_deckPath);
                    ShowStatus($"Could not save the deck: {ex.Message}", true);
                    return;
                }
                // A deck never claims more than it lists: free the extra copies.
                int freed = CollectionEditService.SyncClaims(_deck);
                if (freed > 0)
                    result = new DeckEditResult
                    {
                        Changed = result.Changed,
                        Warning = result.Warning,
                        Touched = result.Touched,
                        Message = $"{result.Message}  ({freed} claimed {(freed == 1 ? "copy" : "copies")} freed in the collection.)",
                    };
                if (claimSnap != null) CollectionEditService.MarkClaimsAfter(claimSnap);
                claimsChanged = freed > 0 || (claimSnap != null && CollectionEditService.ClaimsChanged(claimSnap));
                if (ReadFile(_deckPath) is { } after)
                    PushUndo(_deckPath, new UndoStep(before, after, result.Message, claimsChanged ? claimSnap : null));
            }
            AfterEdit(result, claimsChanged);
        }

        private static DeckEditResult Combine(string what, List<DeckEditResult> results)
        {
            if (results.Count == 1) return results[0];
            int done = results.Count(r => r.Changed > 0);
            var problems = results.Where(r => r.Warning).ToList();
            string msg = $"{what}: {done} of {results.Count} cards changed";
            msg += problems.Count > 0 ? $"; {problems.Count} skipped or partly done — e.g. {problems[0].Message}" : ".";
            return new DeckEditResult
            {
                Changed = results.Sum(r => r.Changed),
                Message = msg,
                Warning = problems.Count > 0,
                Touched = results.SelectMany(r => r.Touched).Distinct().ToList(),
            };
        }

        /// <summary>Collection → Deck / Deck → Collection: re-read the collection (Used / Available changed).</summary>
        private void ReloadSource()
        {
            if (!FromCollection || _src.CurrentTag != SourceTag) return;
            RowKey? shown = _current != null && IsCollectionRow(_current) ? RowKeyOf(_current) : null;
            _src.ReloadRows(shown == null ? null : r => IsCollectionRow(r) && RowKeyOf(r) == shown);
        }

        /// <summary>After every change: status line, then the deck re-read with the changed lines selected.</summary>
        private void AfterEdit(DeckEditResult result, bool collectionChanged = false)
        {
            ShowStatus(result.Message, result.Warning);
            if (result.Changed <= 0 || _deckPath == null) { UpdateButtons(); return; }

            if (collectionChanged) ReloadSource();

            var keys = new HashSet<DeckLineKey>(result.Touched);
            string sid = _current != null ? Str(_current, "ScryfallId") : "";
            OpenDeck(_deckPath,
                keys.Count > 0 ? r => r is DeckCard dc && keys.Contains(DeckLineKey.Of(dc)) : null,
                sid.Length > 0 ? r => r is DeckCard dc && string.Equals(dc.ScryfallId, sid, StringComparison.OrdinalIgnoreCase) : null);
        }

        // ── Add ─────────────────────────────────────────────────────────
        /// <summary>One card to add; <paramref name="Claim"/>: the collection row its copies are claimed from.</summary>
        private sealed record AddPlan(DeckCard Card, string Finish, DeckCardCategory Section, int Qty,
                                      RowKey? Claim = null, string Table = "", string Note = "");

        /// <summary>The plan's note ("only 2 free…") on the end of the result's message.</summary>
        private static DeckEditResult WithNote(DeckEditResult r, AddPlan p) =>
            p.Note.Length == 0 ? r
                : new DeckEditResult
                {
                    Changed = r.Changed,
                    Warning = true,
                    Touched = r.Touched,
                    Message = r.Message.TrimEnd('.') + p.Note + "."
                };

        /// <summary>Add to the deck, and claim the copies when they come from a collection row.</summary>
        private DeckEditResult AddOne(AddPlan p)
        {
            var added = WithNote(DeckEditService.Add(_deck!, p.Card, p.Finish, p.Qty, p.Section), p);
            if (p.Claim == null || added.Changed <= 0) return added;
            var claimed = CollectionEditService.ClaimRow(p.Table, _deck!, p.Claim, p.Qty, p.Card.Name);
            return claimed.Warning
                ? new DeckEditResult
                {
                    Changed = added.Changed,
                    Warning = true,
                    Touched = added.Touched,
                    Message = $"{added.Message} {claimed.Message}"
                }
                : new DeckEditResult
                {
                    Changed = added.Changed,
                    Touched = added.Touched,
                    Message = $"{added.Message} Claimed from your collection ({p.Claim.Text})."
                };
        }

        private static RowKey RowKeyOf(object row) =>
            RowKey.Of(Str(row, "ScryfallId"), RowFinish(row), Str(row, "Language"), Str(row, "Condition"));

        private static string TableOfRow(object row) =>
            row is TokenCollectionEntry ? CollectionEditService.TokensTable : CollectionEditService.CardsTable;

        /// <summary>"Foil · English · Near Mint".</summary>
        private static string RowText(object row) => RowKeyOf(row).Text;

        /// <summary>
        /// Add Qty copies of each selected card. <paramref name="finish"/> null =
        /// each card's default (a deck line: its own finish). Pool cards go to
        /// the "Add to" part of the deck, deck lines to their own part;
        /// <paramref name="section"/> overrides (Add as Commander: 1 copy).
        /// </summary>
        private void DoAdd(string? finish, PoolPage? table = null, DeckCardCategory? section = null)
        {
            table ??= _active;
            var rows = Targets(table);
            if (!Ready(rows)) return;
            if (_mode == DeckEditMode.DeckToCollection)
            {
                // Deck → Collection: + on the deck claims owned copies; on the collection, uses that row.
                if (table == _deckT) ClaimLines(useOwned: true);
                else UseThisRow();
                return;
            }
            bool leader = section == DeckCardCategory.Commander;
            int qty = leader ? 1 : Qty;

            var plan = new List<AddPlan>();
            var refused = new List<DeckEditResult>();
            foreach (var row in rows)
            {
                var card = TemplateFor(row);
                if (card == null)
                {
                    refused.Add(DeckEditResult.Refused($"{CardText(row)}: not in the card pool."));
                    continue;
                }
                var line = row as DeckCard;
                bool fromRow = IsCollectionRow(row);
                // A collection row is one finish; it gives only its free copies.
                if (fromRow && finish != null && finish != RowFinish(row))
                {
                    refused.Add(DeckEditResult.Refused($"{CardText(row)}: this collection row is {CardFinish.Display(RowFinish(row))}."));
                    continue;
                }
                string fin = fromRow ? RowFinish(row) : finish ?? (line != null ? LineFinish(line) : DefaultFinish(card));
                int want = qty;
                string note = "";
                if (fromRow)
                {
                    int free = FreeOf(row);
                    if (free <= 0)
                    {
                        refused.Add(DeckEditResult.Refused($"{CardText(row)} ({RowText(row)}): no free copies — all are in decks."));
                        continue;
                    }
                    if (free < want)
                    {
                        note = $" (only {free} free in that collection row)";
                        want = free;
                    }
                }
                var sec = IsToken(row) ? DeckCardCategory.Tokens
                        : section ?? (line != null ? DeckEditService.SectionOf(line) : AddSection);
                if (sec == DeckCardCategory.Tokens && section == DeckCardCategory.Commander)
                {
                    refused.Add(DeckEditResult.Refused($"{DeckEditService.CardText(card)} is a token — it can't lead the deck."));
                    continue;
                }
                if (!DeckEditService.HasFinish(card, fin))
                {
                    refused.Add(DeckEditResult.Refused($"{DeckEditService.CardText(card)} doesn't come in {CardFinish.Display(fin)}."));
                    continue;
                }
                if (!plan.Any(p => p.Card.ScryfallId == card.ScryfallId && p.Finish == fin && p.Section == sec &&
                                   p.Claim == (fromRow ? RowKeyOf(row) : null)))
                    plan.Add(new AddPlan(card, fin, sec, want,
                        fromRow ? RowKeyOf(row) : null, fromRow ? TableOfRow(row) : "", note));
            }
            if (plan.Count == 0)
            {
                AfterEdit(refused.Count > 0 ? Combine("Added", refused) : DeckEditResult.Refused("Nothing to add."));
                return;
            }
            string finishText = finish == null ? "" : CardFinish.Display(finish) + " ";
            if (plan.Count > 1 &&
                !Confirm($"Add {qty} {finishText}{(qty == 1 ? "copy" : "copies")} of each of {plan.Count} cards to {_deck!.Name}?",
                         "Add several cards"))
                return;

            Edit($"Added {qty} {finishText}".TrimEnd(), () =>
            {
                var rule = Rule;
                // Check each card against the deck as it will be when it's
                // added (two printings of one name add up), and add it.
                var warnings = new List<string>();
                var warned = new HashSet<AddPlan>();
                var results = new List<DeckEditResult>(refused);
                var added = new List<AddPlan>();
                foreach (var p in plan)
                {
                    var w = DeckRulesService.AddWarnings(_deck!, rule, p.Card, p.Section, p.Qty);
                    if (p.Section == DeckCardCategory.Commander && DeckRulesService.LeaderProblem(rule, p.Card) is { } why)
                        w.Insert(0, why);
                    if (w.Count > 0) { warned.Add(p); warnings.AddRange(w); }
                    var r = DeckEditService.Add(_deck!, p.Card, p.Finish, p.Qty, p.Section);
                    results.Add(WithNote(r, p));
                    if (r.Changed > 0) added.Add(p);
                }
                // Claims only once it's settled what's added (the dialog below can cancel).
                void Claim(IEnumerable<AddPlan> these)
                {
                    foreach (var p in these.Where(p => p.Claim != null))
                    {
                        var c = CollectionEditService.ClaimRow(p.Table, _deck!, p.Claim!, p.Qty, p.Card.Name);
                        if (c.Warning) results.Add(DeckEditResult.Refused(c.Message));
                    }
                }
                if (warned.Count == 0) { Claim(added); return results; }
                string header = plan.Count == 1
                    ? $"Add {DeckEditService.CardText(plan[0].Card)} to {_deck!.Name} anyway?"
                    : $"{warned.Count} of the {plan.Count} cards break a {rule.Name} rule. Add anyway?";
                var choice = AddWarningsDialog.Ask(Window.GetWindow(this), header,
                    warnings.Distinct().ToList(), warned.Count, plan.Count - warned.Count);
                if (choice == AddWarningsChoice.AddAll) { Claim(added); return results; }
                if (choice == AddWarningsChoice.Cancel) return null;

                // Only the cards without a warning: start again from the file.
                Deck? fresh;
                try { fresh = DeckService.Load(_deckPath!); }
                catch { return null; }
                if (fresh == null) return null;
                _deck = fresh;
                var some = new List<DeckEditResult>(refused);
                foreach (var p in plan.Where(p => !warned.Contains(p)))
                    some.Add(AddOne(p));
                return some;
            }, plan.Where(p => p.Claim != null).Select(p => (p.Table, p.Card.ScryfallId)));
        }

        // ── Tokens ──────────────────────────────────────────────────────
        /// <summary>A token (from the Tokens pool, or a line in the deck's Tokens part).</summary>
        private static bool IsToken(object row) =>
            row is TokenCard || row is TokenCollectionEntry || row is DeckCard { IsTokenLine: true };

        private bool _fillingPool;

        /// <summary>The source table: cards or tokens (pool or collection).</summary>
        private void PoolBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_fillingPool || !_started) return;
            if (_current != null && _current is not DeckCard) SetCurrentNone();   // the old pool's card
            _src.LoadPool(SourceTag);
            UpdateButtons();
        }

        /// <summary>
        /// "Suggested Tokens…" (the whole deck) or right-click → "Add its
        /// tokens…" (the selected deck cards): the tokens Scryfall links to
        /// those cards; the ticked ones go to the deck's Tokens part.
        /// </summary>
        private void SuggestTokens(bool selectedOnly)
        {
            if (_deck == null) { ShowStatus("Pick a deck first (or click New Deck…).", true); return; }
            var makers = selectedOnly
                ? Targets(_deckT).OfType<DeckCard>().Where(c => !c.IsTokenLine).ToList()
                : _deck.PlayCards;
            if (selectedOnly && makers.Count == 0) { ShowStatus("Select a card (not a token) in the deck table first.", true); return; }

            string header = selectedOnly
                ? makers.Count == 1 ? $"Suggested tokens — {makers[0].Name}" : $"Suggested tokens — {makers.Count} selected cards"
                : $"Suggested tokens — {_deck.Name}";
            const string anyToken = "Older cards, and cards that copy something, may not be linked — " +
                                    "you can still add any token from the Tokens pool.";

            List<TokenSuggestion> list = new();
            bool noData = false;
            if (makers.Count > 0)
            {
                try
                {
                    list = TokenSuggestionService.Suggest(_deck, makers, out noData);
                }
                catch (Exception ex)
                {
                    ShowStatus($"Could not read the token links: {ex.Message}", true);
                    return;
                }
            }

            // Why the list is empty (shown instead of the list).
            string empty =
                makers.Count == 0 ? "This deck has no cards yet." :
                noData ? "The card pool doesn't have token links yet. Run a Full Database Update once, then try again." :
                selectedOnly && makers.Count == 1 ? $"{makers[0].Name} makes no tokens that Scryfall links. {anyToken}" :
                selectedOnly ? $"None of the {makers.Count} selected cards make tokens that Scryfall links. {anyToken}" :
                $"None of this deck's cards make tokens (as far as Scryfall links them). {anyToken}";
            var dlg = TokenSuggestionsDialog.Ask(Window.GetWindow(this), header, list, empty);
            if (dlg == null) return;

            int copies = dlg.Copies;
            var picked = dlg.Picked;
            // The tokens go to the deck's Tokens part; the copies you own are claimed.
            Edit($"Added {copies} of each token", () => picked.Select(s =>
            {
                var t = DeckService.FromTokenCard(s.Token);
                string fin = DefaultFinish(t);
                var added = DeckEditService.Add(_deck!, t, fin, copies, DeckCardCategory.Tokens);
                if (added.Changed <= 0 || s.Owned == 0) return added;
                var claimed = CollectionEditService.ClaimOwned(CollectionEditService.TokensTable, _deck!,
                                                               t.ScryfallId, fin, copies, t.Name);
                return claimed.Changed > 0
                    ? new DeckEditResult
                    {
                        Changed = added.Changed,
                        Touched = added.Touched,
                        Warning = claimed.Warning,
                        Message = $"{added.Message} {claimed.Message}"
                    }
                    : added;
            }).ToList(), picked.Select(s => (CollectionEditService.TokensTable, s.Token.ScryfallId)));
        }

        private void BtnSuggestTokens_Click(object sender, RoutedEventArgs e) => SuggestTokens(selectedOnly: false);

        // ══════════════════════════════════════════════════════════════════
        // DECK → COLLECTION: claim copies you own, or add new ones
        // ══════════════════════════════════════════════════════════════════
        private string AddLanguage => LanguageBox.SelectedItem as string ?? CardLanguage.Default;
        private string AddCondition => ConditionBox.SelectedItem as string ?? CardCondition.Default;

        /// <summary>Etched-only printing (its foil copies are etched)?</summary>
        private bool EtchedOnly(string sid, bool token) =>
            PoolFor(sid, token) is { } p && p.GetType().GetProperty("IsEtched")?.GetValue(p) is true &&
            p.GetType().GetProperty("IsFoil")?.GetValue(p) is not true;

        /// <summary>What the deck still needs, per printing + finish (optionally only these lines' printings).</summary>
        private List<ClaimLine> Needs(IEnumerable<DeckCard>? only = null)
        {
            var status = CollectionEditService.ClaimStatus(_deck!);
            if (only != null)
            {
                // The selected lines' printings, in the finishes those lines list.
                var keys = new HashSet<(string, string, string)>(only.Where(l => !string.IsNullOrEmpty(l.ScryfallId)).SelectMany(l =>
                    new[] { CardFinish.NonFoil, CardFinish.Foil, CardFinish.Etched }
                        .Where(f => l.CountOf(f) > 0)
                        .Select(f => (CollectionEditService.TableOf(l), l.ScryfallId.ToLowerInvariant(), f))));
                status = status.Where(s => keys.Contains((s.Table, s.ScryfallId.ToLowerInvariant(), s.Finish))).ToList();
            }
            return status;
        }

        private static DeckEditResult FromEdit(EditResult r) =>
            new() { Changed = r.Changed, Message = r.Message, Warning = r.Warning };

        /// <summary>
        /// The selected deck lines: claim free copies you own (<paramref name="useOwned"/>),
        /// or add new copies to the collection (Language · Condition from the bar), for what they still need.
        /// </summary>
        private void ClaimLines(bool useOwned)
        {
            if (_deck == null) { ShowStatus("Pick a deck first.", true); return; }
            var lines = SelectedDeckLines();
            if (lines.Count == 0) { ShowStatus("Select a card in the deck table first.", true); return; }
            var needs = Needs(lines).Where(n => n.Needed > 0).ToList();
            if (needs.Count == 0) { ShowStatus("Every copy of the selected cards is already claimed.", false); return; }
            if (!useOwned && needs.Sum(n => n.Needed) > 1 &&
                !Confirm($"Add {needs.Sum(n => n.Needed)} new copies to your collection as {AddLanguage} · {AddCondition}?\n\n" +
                         string.Join("\n", needs.Take(12).Select(n => $"{n.Needed} × {n.Text}")) +
                         (needs.Count > 12 ? $"\n… and {needs.Count - 12} more" : ""),
                         "Add as new copies"))
                return;
            RunClaims(useOwned, needs);
        }

        /// <summary>Whole deck: preview what would be claimed (or added), then do it.</summary>
        private void WholeDeck(bool useOwned)
        {
            if (_deck == null) { ShowStatus("Pick a deck first.", true); return; }
            var needs = Needs().Where(n => n.Needed > 0).ToList();
            if (needs.Count == 0) { ShowStatus($"Every copy in {_deck.Name} is already claimed from your collection.", false); return; }

            var preview = new List<string>();
            int will = 0, missing = 0;
            foreach (var n in needs.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (useOwned)
                {
                    bool token = n.Table == CollectionEditService.TokensTable;
                    var counts = CollectionEditService.Counts(n.Table, n.ScryfallId, EtchedOnly(n.ScryfallId, token));
                    int used = n.Finish switch
                    {
                        CardFinish.Foil => counts.UsedFoil,
                        CardFinish.Etched => counts.UsedEtched,
                        _ => counts.UsedNonFoil,
                    };
                    int free = Math.Max(0, counts.Owned(n.Finish) - used);
                    int take = Math.Min(free, n.Needed);
                    will += take;
                    missing += n.Needed - take;
                    preview.Add(take == n.Needed ? $"✓  {n.Needed} × {n.Text}"
                        : take == 0 ? $"✗  {n.Text}: need {n.Needed} — none free"
                        : $"◐  {n.Text}: need {n.Needed} — {take} free, {n.Needed - take} missing");
                }
                else
                {
                    will += n.Needed;
                    preview.Add($"+  {n.Needed} × {n.Text}");
                }
            }
            string header = useOwned
                ? $"Use copies you own for {_deck.Name}: {will} to claim" + (missing > 0 ? $", {missing} missing" : "")
                : $"Add {_deck.Name}'s cards to your collection: {will} new copies as {AddLanguage} · {AddCondition}";
            string note = useOwned
                ? "Same printing and finish only — English first, then the worst condition first. Missing copies stay unclaimed."
                : "Every copy the deck still needs goes into your collection as a new copy, claimed for this deck.";
            if (will == 0) { ShowStatus($"{header} — nothing to claim.", true); return; }
            if (!ListPreviewDialog.Ask(Window.GetWindow(this), header, note, preview, useOwned ? "Claim them" : "Add them"))
                return;
            RunClaims(useOwned, needs);
        }

        private void RunClaims(bool useOwned, List<ClaimLine> needs)
        {
            string lang = AddLanguage, cond = AddCondition;
            Edit(useOwned ? "Used copies you own" : "Added to your collection", () => needs.Select(n =>
            {
                if (useOwned)
                    return FromEdit(CollectionEditService.ClaimOwned(n.Table, _deck!, n.ScryfallId, n.Finish, n.Needed, n.Name));
                var pool = PoolFor(n.ScryfallId, n.Table == CollectionEditService.TokensTable);
                return pool == null
                    ? DeckEditResult.Refused($"{n.Text}: not in the card pool.")
                    : FromEdit(CollectionEditService.AddAndClaim(n.Table, _deck!, pool, n.Finish, n.Needed, lang, cond));
            }).ToList(), needs.Select(n => (n.Table, n.ScryfallId)));
        }

        /// <summary>The selected collection row: claim its free copies for what the deck still needs of it.</summary>
        private void UseThisRow()
        {
            if (_deck == null) { ShowStatus("Pick a deck first.", true); return; }
            if (_current == null || !IsCollectionRow(_current))
            {
                ShowStatus("Select a row in the collection table first.", true);
                return;
            }
            var row = _current;
            string table = TableOfRow(row), sid = Str(row, "ScryfallId"), fin = RowFinish(row);
            var need = Needs().FirstOrDefault(n => n.Table == table && n.Finish == fin &&
                                                   string.Equals(n.ScryfallId, sid, StringComparison.OrdinalIgnoreCase));
            if (need == null || need.Needed == 0)
            {
                ShowStatus(need == null
                    ? $"{CardText(row)} ({CardFinish.Display(fin)}) isn't in {_deck.Name}."
                    : $"{CardText(row)} ({CardFinish.Display(fin)}): every copy the deck lists is already claimed.", true);
                return;
            }
            int n = Math.Min(need.Needed, FreeOf(row));
            if (n <= 0) { ShowStatus($"{CardText(row)} ({RowText(row)}): no free copies in this row.", true); return; }
            var key = RowKeyOf(row);
            Edit("Used this row", () => new List<DeckEditResult>
            {
                FromEdit(CollectionEditService.ClaimRow(table, _deck!, key, n, Str(row, "Name"))),
            }, new[] { (table, sid) });
        }

        /// <summary>The selected deck lines: free their claimed copies (the cards stay in the deck).</summary>
        private void FreeCopies()
        {
            if (_deck == null) { ShowStatus("Pick a deck first.", true); return; }
            var lines = SelectedDeckLines();
            var claimed = Needs(lines).Where(n => n.Claimed > 0).ToList();
            if (claimed.Count == 0) { ShowStatus("None of the selected cards have claimed copies.", false); return; }
            Edit("Freed copies", () => claimed.Select(n =>
                FromEdit(CollectionEditService.Release(_deck!, n.Table, n.ScryfallId, n.Finish, n.Claimed, n.Name))).ToList(),
                claimed.Select(n => (n.Table, n.ScryfallId)));
        }

        /// <summary>
        /// Tear down: every copy the deck claims goes back to Available, and the
        /// deck file moves to "Deleted Decks" (kept, so it can be recovered).
        /// </summary>
        private void BtnTearDown_Click(object sender, RoutedEventArgs e)
        {
            if (_deck == null || _deckPath == null) return;
            int claimed = _deck.Cards.Sum(c => c.ClaimedCount);
            string what = claimed > 0
                ? $"The {claimed} claimed {(claimed == 1 ? "copy goes" : "copies go")} back to Available in your collection, and the deck is deleted."
                : "The deck is deleted (none of its copies are claimed from your collection).";
            if (!Confirm($"Tear down \"{_deck.Name}\"?\n\n{what}\n\nThe deck file is moved to the \"Deleted Decks\" folder, " +
                         "so it can be recovered by hand. This can't be undone here.", "Tear down deck"))
                return;

            string name = _deck.Name, path = _deckPath;
            int freed = CollectionEditService.ReleaseAll(_deck.DeckId);
            if (freed < 0)
            {
                // The claims couldn't be freed: keep the deck, so its copies aren't left "used" by nothing.
                ShowStatus($"Could not free {name}'s claimed copies in the collection — the deck was NOT deleted. Try again.", true);
                return;
            }
            string moved;
            try
            {
                string dir = Path.Combine(AppFolderService.RootFolder, "Deleted Decks");
                Directory.CreateDirectory(dir);
                string target = Path.Combine(dir, Path.GetFileName(path));
                string stem = Path.GetFileNameWithoutExtension(path);
                for (int i = 2; File.Exists(target); i++) target = Path.Combine(dir, $"{stem} ({i}).deck");
                File.Move(path, target);
                moved = target;
            }
            catch (Exception ex)
            {
                ShowStatus($"Freed {freed} copies, but could not move the deck file: {ex.Message}", true);
                FillDecks(null);
                return;
            }
            _undo.Remove(path);
            FillDecks(null);
            ReloadSource();
            ShowStatus($"Tore down \"{name}\": {freed} {(freed == 1 ? "copy" : "copies")} back to Available; " +
                       $"the deck file is in {Path.GetDirectoryName(moved)}.", false);
        }

        private void BtnUseOwned_Click(object sender, RoutedEventArgs e) => ClaimLines(useOwned: true);
        private void BtnAddNew_Click(object sender, RoutedEventArgs e) => ClaimLines(useOwned: false);
        private void BtnUseRow_Click(object sender, RoutedEventArgs e) => UseThisRow();
        private void BtnFreeCopies_Click(object sender, RoutedEventArgs e) => FreeCopies();
        private void BtnWholeOwned_Click(object sender, RoutedEventArgs e) => WholeDeck(useOwned: true);
        private void BtnWholeNew_Click(object sender, RoutedEventArgs e) => WholeDeck(useOwned: false);

        /// <summary>A tile's +: add (or, in Deck → Collection, claim).</summary>
        private void TileAdd(PoolPage t, ModifierKeys keys)
        {
            if (_mode == DeckEditMode.DeckToCollection)
            {
                if (t == _deckT) ClaimLines(useOwned: true);
                else UseThisRow();
                return;
            }
            DoAdd(FinishFromKeys(keys), t);
        }

        /// <summary>A tile's −: remove (or, in Deck → Collection, free the deck card's claimed copies).</summary>
        private void TileRemove(PoolPage t, ModifierKeys keys)
        {
            if (_mode == DeckEditMode.DeckToCollection)
            {
                if (t == _deckT) FreeCopies();
                else ShowStatus("To free copies, use − on the deck's card (or Free Copies).", false);
                return;
            }
            DoRemove(FinishFromKeys(keys), t);
        }

        // ── Remove ──────────────────────────────────────────────────────
        /// <summary>
        /// The deck line a row stands for: a deck line itself; for a pool card,
        /// its line in the "Add to" part, else the main deck, sideboard,
        /// command zone — the first that has <paramref name="finish"/> (any
        /// finish when null).
        /// </summary>
        private DeckCard? LineFor(object row, string? finish)
        {
            if (row is DeckCard line) return line;
            var lines = LinesOf(Str(row, "ScryfallId"));
            var order = IsToken(row)
                ? new[] { DeckCardCategory.Tokens }
                : new[] { AddSection, DeckCardCategory.Mainboard, DeckCardCategory.Sideboard, DeckCardCategory.Commander };
            foreach (var sec in order)
            {
                var hit = lines.FirstOrDefault(l => DeckEditService.SectionOf(l) == sec &&
                                                    (finish == null ? l.TotalQuantity > 0 : l.CountOf(finish) > 0));
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>Remove Qty copies of one finish (null = each line's own) from each selected card.</summary>
        private void DoRemove(string? finish, PoolPage? table = null)
        {
            table ??= _active;
            var rows = Targets(table);
            if (!Ready(rows)) return;
            int qty = Qty;

            var plan = new List<(DeckLineKey Key, string Finish)>();
            var missing = new List<DeckEditResult>();
            foreach (var row in rows)
            {
                var line = LineFor(row, finish);
                if (line == null)
                {
                    missing.Add(DeckEditResult.Refused(
                        $"{CardText(row)}: no {(finish == null ? "" : CardFinish.Display(finish) + " ")}copies in this deck."));
                    continue;
                }
                var key = DeckLineKey.Of(line);
                string fin = finish ?? LineFinish(line);
                if (!plan.Contains((key, fin))) plan.Add((key, fin));
            }
            if (plan.Count == 0) { AfterEdit(Combine("Removed", missing)); return; }
            if (plan.Count > 1 &&
                !Confirm($"Remove {qty} {(qty == 1 ? "copy" : "copies")} of each of {plan.Count} cards from {_deck!.Name}?",
                         "Remove several cards"))
                return;

            Edit($"Removed {qty}", () =>
                missing.Concat(plan.Select(p => DeckEditService.Remove(_deck!, p.Key, p.Finish, qty))).ToList());
        }

        /// <summary>Take the selected cards out of the deck: every copy of each line.</summary>
        private void RemoveLines(PoolPage? table = null)
        {
            table ??= _active;
            var rows = Targets(table);
            if (!Ready(rows)) return;
            var keys = rows.Select(r => LineFor(r, null)).Where(l => l != null)
                           .Select(l => DeckLineKey.Of(l!)).Distinct().ToList();
            if (keys.Count == 0) { ShowStatus("Those cards aren't in this deck.", true); return; }
            if (keys.Count > 1 && !Confirm($"Take {keys.Count} cards out of {_deck!.Name} (every copy of each)?", "Remove cards"))
                return;
            Edit("Removed", () => keys.Select(k => DeckEditService.RemoveLine(_deck!, k)).ToList());
        }

        // ── Command zone and sideboard ──────────────────────────────────
        /// <summary>Deck table: put the selected card in the command zone (asks when the format says it can't lead).</summary>
        private void MakeLeader()
        {
            if (_deck == null) return;
            if (Targets(_deckT).OfType<DeckCard>().FirstOrDefault() is not { } line)
            {
                ShowStatus("Select a card in the deck table first.", true);
                return;
            }
            var rule = Rule;
            string leaderName = DeckRulesService.LeaderName(rule);
            if (DeckRulesService.LeaderProblem(rule, line) is { } why &&
                !Confirm($"{why}\n\nMake it the {leaderName} anyway?", $"Set as {leaderName}"))
                return;
            var key = DeckLineKey.Of(line);
            Edit($"Set as {leaderName}", () => new List<DeckEditResult> { DeckEditService.MakeLeader(_deck!, key, leaderName) });
        }

        /// <summary>Deck table: move the selected lines to another part of the deck.</summary>
        private void MoveLines(DeckCardCategory to, string what)
        {
            if (_deck == null) return;
            // Tokens stay in the deck's Tokens part.
            var keys = Targets(_deckT).OfType<DeckCard>().Where(l => !l.IsTokenLine)
                                       .Select(DeckLineKey.Of).Distinct().ToList();
            if (keys.Count == 0) { ShowStatus("Select a card (not a token) in the deck table first.", true); return; }
            Edit(what, () => keys.Select(k => DeckEditService.MoveLine(_deck!, k, to)).ToList());
        }

        // ── Set a count (double-click a Non-Foil / Foil / Etched cell) ──
        private void EditCountInPlace(DeckCard line, DataGridCell? cell, string finish)
        {
            SetCurrent(line);
            int current = line.CountOf(finish);
            var key = DeckLineKey.Of(line);
            EditCollectionPage.ShowCellEditor(cell, current.ToString(), 48, TextAlignment.Center, digitsOnly: true,
                $"{CardFinish.Display(finish)} copies — Enter to save, Esc to cancel", text =>
                {
                    if (!int.TryParse(text, out int n) || n < 0)
                    {
                        ShowStatus("Enter a whole number (0 or more).", true);
                        return;
                    }
                    if (n == current) return;
                    if (n > current)
                    {
                        // More copies: the same rule check as adding.
                        var tmpl = TemplateFor(line) ?? line;
                        var w = DeckRulesService.AddWarnings(_deck!, Rule, tmpl, key.Section, n - current);
                        if (w.Count > 0 &&
                            AddWarningsDialog.Ask(Window.GetWindow(this),
                                $"Set {DeckEditService.CardText(line)} to {n} {CardFinish.Display(finish)} anyway?",
                                w, 1, 0) != AddWarningsChoice.AddAll)
                            return;
                    }
                    Edit("Count", () => new List<DeckEditResult> { DeckEditService.SetCount(_deck!, key, finish, n) });
                });
        }

        /// <summary>Right-click → Set count: the editor over that cell, or at the mouse.</summary>
        private void SetCountOfSelected(string finish)
        {
            if (Targets(_deckT).OfType<DeckCard>().FirstOrDefault() is not { } line)
            {
                ShowStatus("Select a card in the deck table first.", true);
                return;
            }
            string header = finish switch { CardFinish.Foil => "Foil", CardFinish.Etched => "Etched", _ => "Non-Foil" };
            // After the right-click menu has closed, or it would close the editor at once.
            Dispatcher.BeginInvoke(new Action(() => EditCountInPlace(line, _deckT.CellFor(line, header), finish)),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        // ── Undo ────────────────────────────────────────────────────────
        private List<UndoStep> UndoStack =>
            _deckPath != null && _undo.TryGetValue(_deckPath, out var s) ? s : new();

        private void PushUndo(string path, UndoStep step)
        {
            if (step.Before == step.After && step.Claims == null) return;
            if (!_undo.TryGetValue(path, out var stack)) _undo[path] = stack = new();
            stack.Add(step);
            if (stack.Count > UndoSteps) stack.RemoveAt(0);
            UpdateUndo();
        }

        private void DoUndo()
        {
            var stack = UndoStack;
            if (_deckPath == null || stack.Count == 0) { ShowStatus("Nothing to undo.", false); return; }
            var step = stack[^1];
            var (before, after, text) = (step.Before, step.After, step.Text);
            stack.RemoveAt(stack.Count - 1);

            // Only when the file is still as this page left it (it can't apply otherwise).
            if (ReadFile(_deckPath) != after)
            {
                stack.Clear();
                UpdateUndo();
                ShowStatus("Can't undo: the deck file was changed outside this page since.", true);
                return;
            }
            // The collection first (claims and rows); refused when they changed since.
            if (step.Claims != null)
            {
                var r = CollectionEditService.RestoreClaims(step.Claims, text);
                if (r.Warning)
                {
                    stack.Clear();
                    UpdateUndo();
                    ShowStatus(r.Message, true);
                    return;
                }
            }
            try
            {
                string tmp = _deckPath + ".saving";
                File.WriteAllText(tmp, before);
                File.Move(tmp, _deckPath, overwrite: true);
            }
            catch (Exception ex)
            {
                stack.Add(step with { Claims = null });  // the deck wasn't undone (its claims were)
                UpdateUndo();
                ShowStatus($"Could not undo: {ex.Message}", true);
                return;
            }
            // Deck Settings may have changed the name, type or format: refresh the list too.
            FillDecks(_deckPath);
            if (step.Claims != null) ReloadSource();
            UpdateUndo();
            ShowStatus($"Undone: {text}", false);
        }

        private void UpdateUndo()
        {
            var stack = UndoStack;
            BtnUndo.IsEnabled = stack.Count > 0;
            BtnUndo.ToolTip = stack.Count == 0
                ? "Nothing to undo (Ctrl+Z)"
                : $"Undo: {stack[^1].Text}  (Ctrl+Z · {stack.Count} of the last {UndoSteps} changes to this deck can be undone)";
        }

        private static string? ReadFile(string path)
        {
            try { return File.ReadAllText(path); }
            catch { return null; }
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
        private void BtnRemoveNonFoil_Click(object sender, RoutedEventArgs e) => DoRemove(CardFinish.NonFoil);
        private void BtnRemoveFoil_Click(object sender, RoutedEventArgs e) => DoRemove(CardFinish.Foil);
        private void BtnRemoveEtched_Click(object sender, RoutedEventArgs e) => DoRemove(CardFinish.Etched);
        private void BtnRemoveLine_Click(object sender, RoutedEventArgs e) => RemoveLines();
        private void BtnUndo_Click(object sender, RoutedEventArgs e) => DoUndo();

        // ══════════════════════════════════════════════════════════════════
        // RIGHT-CLICK MENUS
        // ══════════════════════════════════════════════════════════════════
        private sealed record MenuEntry(MenuItem Item, string Header, Func<bool> Enabled, bool ShowsQty);
        private readonly List<MenuEntry> _menuEntries = new();
        private MenuItem? _addTokensItem, _addTokensItem2;
        private readonly ContextMenu _claimSrcMenu = new();
        private readonly ContextMenu _claimDeckMenu = new();
        private readonly List<MenuItem> _undoItems = new();
        private MenuItem? _undoSrc, _undoDeck, _addLeaderItem, _makeLeaderItem, _unLeaderItem,
                          _toSideItem, _toMainItem;

        private void BuildMenus()
        {
            MenuItem Item(ContextMenu menu, string header, string keys, Action act, Func<bool>? enabled = null, bool qty = false)
            {
                var mi = new MenuItem { Header = header, InputGestureText = keys };
                mi.Click += (_, _) => act();
                menu.Items.Add(mi);
                _menuEntries.Add(new MenuEntry(mi, header, enabled ?? (() => _deck != null), qty));
                return mi;
            }

            // The source table: the card pool, or your collection.
            Item(_srcMenu, "Add Non-Foil", "Enter", () => DoAdd(CardFinish.NonFoil, _src), () => BtnAddNonFoil.IsEnabled, true);
            Item(_srcMenu, "Add Foil", "Shift+Enter", () => DoAdd(CardFinish.Foil, _src), () => BtnAddFoil.IsEnabled, true);
            Item(_srcMenu, "Add Etched", "Ctrl+Enter", () => DoAdd(CardFinish.Etched, _src), () => BtnAddEtched.IsEnabled, true);
            _addLeaderItem = Item(_srcMenu, "Add as Commander", "", () => DoAdd(null, _src, DeckCardCategory.Commander));
            _srcMenu.Items.Add(new Separator());
            Item(_srcMenu, "Remove Non-Foil", "", () => DoRemove(CardFinish.NonFoil, _src), () => BtnRemoveNonFoil.IsEnabled, true);
            Item(_srcMenu, "Remove Foil", "", () => DoRemove(CardFinish.Foil, _src), () => BtnRemoveFoil.IsEnabled, true);
            Item(_srcMenu, "Remove Etched", "", () => DoRemove(CardFinish.Etched, _src), () => BtnRemoveEtched.IsEnabled, true);
            Item(_srcMenu, "Remove Card (every copy)", "", () => RemoveLines(_src), () => BtnRemoveLine.IsEnabled);
            _srcMenu.Items.Add(new Separator());
            _undoSrc = Item(_srcMenu, "Undo", "Ctrl+Z", DoUndo, () => UndoStack.Count > 0);

            // The deck table.
            Item(_deckMenu, "Add Non-Foil", "", () => DoAdd(CardFinish.NonFoil, _deckT), () => BtnAddNonFoil.IsEnabled, true);
            Item(_deckMenu, "Add Foil", "", () => DoAdd(CardFinish.Foil, _deckT), () => BtnAddFoil.IsEnabled, true);
            Item(_deckMenu, "Add Etched", "", () => DoAdd(CardFinish.Etched, _deckT), () => BtnAddEtched.IsEnabled, true);
            _deckMenu.Items.Add(new Separator());
            Item(_deckMenu, "Remove Non-Foil", "", () => DoRemove(CardFinish.NonFoil, _deckT), () => BtnRemoveNonFoil.IsEnabled, true);
            Item(_deckMenu, "Remove Foil", "", () => DoRemove(CardFinish.Foil, _deckT), () => BtnRemoveFoil.IsEnabled, true);
            Item(_deckMenu, "Remove Etched", "", () => DoRemove(CardFinish.Etched, _deckT), () => BtnRemoveEtched.IsEnabled, true);
            Item(_deckMenu, "Remove Card (every copy)", "Shift+Delete", () => RemoveLines(_deckT), () => BtnRemoveLine.IsEnabled);

            var setCount = new MenuItem { Header = "Set count" };
            foreach (var f in new[] { CardFinish.NonFoil, CardFinish.Foil, CardFinish.Etched })
            {
                string fin = f;
                var mi = new MenuItem { Header = CardFinish.Display(fin) + "…", Tag = fin };
                mi.Click += (_, _) => SetCountOfSelected(fin);
                setCount.Items.Add(mi);
            }
            _deckMenu.Items.Add(setCount);
            _deckMenu.Items.Add(new Separator());

            _addTokensItem = Item(_deckMenu, "Add its tokens…", "", () => SuggestTokens(selectedOnly: true));
            _deckMenu.Items.Add(new Separator());
            _makeLeaderItem = Item(_deckMenu, "Set as Commander", "", MakeLeader);
            _unLeaderItem = Item(_deckMenu, "Move out of the command zone", "",
                () => MoveLines(DeckCardCategory.Mainboard, "Moved to the main deck"));
            _toSideItem = Item(_deckMenu, "Move to Sideboard", "",
                () => MoveLines(DeckCardCategory.Sideboard, "Moved to the sideboard"));
            _toMainItem = Item(_deckMenu, "Move to Main Deck", "",
                () => MoveLines(DeckCardCategory.Mainboard, "Moved to the main deck"));
            _deckMenu.Items.Add(new Separator());
            _undoDeck = Item(_deckMenu, "Undo", "Ctrl+Z", DoUndo, () => UndoStack.Count > 0);

            // Deck → Collection: the deck's own menu and the collection's.
            Item(_claimDeckMenu, "Use Copies I Own", "Enter", () => ClaimLines(useOwned: true), () => BtnUseOwned.IsEnabled);
            Item(_claimDeckMenu, "Add as New Copies", "", () => ClaimLines(useOwned: false), () => BtnAddNew.IsEnabled);
            Item(_claimDeckMenu, "Free Claimed Copies", "Delete", FreeCopies, () => BtnFreeCopies.IsEnabled);
            _claimDeckMenu.Items.Add(new Separator());
            _addTokensItem2 = Item(_claimDeckMenu, "Add its tokens…", "", () => SuggestTokens(selectedOnly: true));
            _claimDeckMenu.Items.Add(new Separator());
            _undoItems.Add(Item(_claimDeckMenu, "Undo", "Ctrl+Z", DoUndo, () => UndoStack.Count > 0));
            Item(_claimSrcMenu, "Use This Row for the Deck", "Enter", UseThisRow, () => BtnUseRow.IsEnabled);
            _claimSrcMenu.Items.Add(new Separator());
            _undoItems.Add(Item(_claimSrcMenu, "Undo", "Ctrl+Z", DoUndo, () => UndoStack.Count > 0));

            _srcMenu.Opened += (_, _) => MenuOpened(_src);
            _deckMenu.Opened += (_, _) => MenuOpened(_deckT);
            _claimSrcMenu.Opened += (_, _) => MenuOpened(_src);
            _claimDeckMenu.Opened += (_, _) => MenuOpened(_deckT);
        }

        /// <summary>Each table's right-click menu for the mode.</summary>
        private void SetMenus()
        {
            bool claim = _mode == DeckEditMode.DeckToCollection;
            _src.SetRowContextMenu(claim ? _claimSrcMenu : _srcMenu);
            _deckT.SetRowContextMenu(claim ? _claimDeckMenu : _deckMenu);
        }

        /// <summary>The menu acts on its own table: sync the current card, then items follow the buttons and the format.</summary>
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

            var stack = UndoStack;
            string undoText = stack.Count > 0 ? $"Undo: {Shorten(stack[^1].Text)}" : "Undo";
            if (_undoSrc != null) _undoSrc.Header = undoText;
            if (_undoDeck != null) _undoDeck.Header = undoText;
            foreach (var u in _undoItems) u.Header = undoText;

            // Command zone and sideboard items only where the format has them.
            var rule = Rule;
            var leaderVis = _deck != null && rule.HasLeader ? Visibility.Visible : Visibility.Collapsed;
            string leaderName = DeckRulesService.LeaderName(rule);
            _addLeaderItem!.Visibility = PoolBox.SelectedIndex == 1 ? Visibility.Collapsed : leaderVis;   // not for tokens
            _addLeaderItem.Header = $"Add as {leaderName}";
            var lines = Targets(_deckT).OfType<DeckCard>().ToList();
            bool anyLeader = lines.Any(l => DeckEditService.SectionOf(l) == DeckCardCategory.Commander);
            bool anySide = lines.Any(l => l.Category == DeckCardCategory.Sideboard);
            bool anyMain = lines.Any(l => DeckEditService.SectionOf(l) == DeckCardCategory.Mainboard);
            bool hasSide = rule.SideboardMax != null || rule.Type == DeckType.Limited;

            _makeLeaderItem!.Header = $"Set as {leaderName}";
            _makeLeaderItem.Visibility = leaderVis;
            _makeLeaderItem.IsEnabled = lines.Count == 1 && !anyLeader && !lines[0].IsTokenLine;
            _addTokensItem!.IsEnabled = _addTokensItem2!.IsEnabled = lines.Any(l => !l.IsTokenLine);
            _addTokensItem.Visibility = _addTokensItem2.Visibility = FromCollection ? Visibility.Visible : Visibility.Collapsed;
            _unLeaderItem!.Visibility = anyLeader ? Visibility.Visible : Visibility.Collapsed;
            _toSideItem!.Visibility = hasSide ? Visibility.Visible : Visibility.Collapsed;
            _toSideItem.IsEnabled = anyMain || anyLeader;
            _toMainItem!.Visibility = hasSide || anySide ? Visibility.Visible : Visibility.Collapsed;
            _toMainItem.IsEnabled = anySide;
        }

        private static string Shorten(string s) => s.Length <= 60 ? s : s[..57] + "…";

        /// <summary>A tile button's keys: Shift = foil, Ctrl = etched, none = the default.</summary>
        private static string? FinishFromKeys(ModifierKeys keys) =>
            keys.HasFlag(ModifierKeys.Control) ? CardFinish.Etched :
            keys.HasFlag(ModifierKeys.Shift) ? CardFinish.Foil : null;

        // ══════════════════════════════════════════════════════════════════
        // KEYS
        // ══════════════════════════════════════════════════════════════════
        private void Src_GridPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter && e.Key != Key.Return) return;
            _active = _src;
            if (_src.SelectedCard is { } card) SetCurrent(card);
            if (_mode == DeckEditMode.DeckToCollection)
            {
                UseThisRow();              // Deck → Collection: Enter uses the selected collection row
                e.Handled = true;
                return;
            }
            var mods = Keyboard.Modifiers;
            if (mods.HasFlag(ModifierKeys.Control)) DoAdd(CardFinish.Etched, _src);
            else if (mods.HasFlag(ModifierKeys.Shift)) DoAdd(CardFinish.Foil, _src);
            else DoAdd(null, _src);
            e.Handled = true;           // don't let Enter move to the next row
        }

        private void Deck_GridPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_mode == DeckEditMode.DeckToCollection)
            {
                // Deck → Collection: Enter claims copies you own, Delete frees the claimed ones.
                if (e.Key is Key.Enter or Key.Return) { _active = _deckT; ClaimLines(useOwned: true); e.Handled = true; }
                else if (e.Key == Key.Delete) { _active = _deckT; FreeCopies(); e.Handled = true; }
                return;
            }
            if (e.Key != Key.Delete) return;
            _active = _deckT;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) RemoveLines(_deckT);
            else DoRemove(null, _deckT);
            e.Handled = true;
        }

        private Window? _keysWindow;

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!IsLoaded || !IsVisible || e.Handled) return;
            // Another window in front (a dialog) keeps its own keys.
            if (sender is Window w && !w.IsActive) return;
            Page_PreviewKeyDown(sender, e);
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_keysWindow != null) _keysWindow.PreviewKeyDown -= Window_PreviewKeyDown;
            _keysWindow = null;
        }

        private void Page_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            if (e.Key == Key.Q && _mode != DeckEditMode.DeckToCollection)
            {
                QtyBox.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Z && Keyboard.FocusedElement is not TextBox)
            {
                DoUndo();                  // a text box keeps its own Ctrl+Z
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
                if (_mode != DeckEditMode.DeckToCollection) DoAdd(null);
                e.Handled = true;
            }
        }

        private void QtyBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
            QtyBox.SelectAll();

        // ── Helpers ─────────────────────────────────────────────────────
        private static string Str(object o, string prop) =>
            o.GetType().GetProperty(prop)?.GetValue(o) as string ?? "";

        private static string CardText(object card) =>
            $"{Str(card, "Name")} ({Str(card, "SetCode").ToUpperInvariant()} #{Str(card, "CollectorNumber")})";

        // ── Splitters: sizes are remembered (shared with the other Edit pages) ──
        private void TableSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            double total = TopRow.ActualHeight + BottomRow.ActualHeight;
            if (total > 0) GridLayoutService.SetNumber("Edit:TopShare", TopRow.ActualHeight / total);
        }

        private void DetailSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) =>
            GridLayoutService.SetNumber("Edit:DetailWidth", DetailColumn.ActualWidth);

        private void RestoreSplitters()
        {
            if (GridLayoutService.GetNumber("Edit:TopShare") is double share && share > 0.05 && share < 0.95)
            {
                TopRow.Height = new GridLength(share, GridUnitType.Star);
                BottomRow.Height = new GridLength(1 - share, GridUnitType.Star);
            }
            if (GridLayoutService.GetNumber("Edit:DetailWidth") is double w && w >= DetailColumn.MinWidth)
                DetailColumn.Width = new GridLength(Math.Min(w, DetailColumn.MaxWidth));
        }

        // NavigationView wraps pages in a ScrollViewer → infinite height →
        // virtualization defeated → freeze. Same fix as PoolPage.
        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // Ctrl+Z / Ctrl+Q from anywhere in the window while this page is on
            // screen: after a change the table is re-read and the row that had
            // the keyboard focus is gone, so focus falls back to the window —
            // outside this page — and the first press never reached it.
            if (Window.GetWindow(this) is { } win && !ReferenceEquals(win, _keysWindow))
            {
                if (_keysWindow != null) _keysWindow.PreviewKeyDown -= Window_PreviewKeyDown;
                _keysWindow = win;
                win.PreviewKeyDown += Window_PreviewKeyDown;
            }
            RestoreSplitters();
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