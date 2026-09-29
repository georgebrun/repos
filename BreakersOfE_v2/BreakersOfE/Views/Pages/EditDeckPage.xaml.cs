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
    /// <summary>
    /// Edit → Decks → Pool → Deck. The card pool on top, the deck below (the
    /// same tables as View, embedded), the card panel on the left.
    ///
    /// The deck is a pure card list: nothing here touches the collection
    /// (Collection → Deck comes later). Each line is one printing in one part
    /// of the deck (command zone, main deck, sideboard) with a count per
    /// finish (Non-Foil, Foil, Etched).
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
        private readonly PoolPage _top = new();
        private readonly PoolPage _bottom = new();
        private bool _started;

        // The deck being edited (as read from its file after the last change).
        private Deck? _deck;
        private string? _deckPath;
        private bool _fillingDecks;

        // The table the user worked in last, and the card shown.
        private PoolPage? _active;
        private object? _current;

        private readonly ContextMenu _topMenu = new();
        private readonly ContextMenu _bottomMenu = new();

        // Undo, per deck file: the file before and after each change (newest last).
        private const int UndoSteps = 30;
        private static readonly Dictionary<string, List<(string Before, string After, string Text)>> _undo =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>One deck in the Deck list.</summary>
        private sealed record DeckChoice(string Path, string Name, string Label)
        {
            public override string ToString() => Label;
        }

        public EditDeckPage()
        {
            InitializeComponent();

            // Pool on top starts as card pictures (+ / − on each), the deck
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
                DoRemove(FinishFromKeys(keys), _top);
            };
            _bottom.GalleryAdd += (card, keys) => { _active = _bottom; SetCurrent(card); DoAdd(FinishFromKeys(keys), _bottom); };
            _bottom.GalleryRemove += (card, keys) =>
            {
                _active = _bottom;
                SetCurrent(card);
                DoRemove(FinishFromKeys(keys), _bottom);
            };

            _top.GridPreviewKeyDown += Top_GridPreviewKeyDown;
            _bottom.GridPreviewKeyDown += Bottom_GridPreviewKeyDown;

            AddToBox.ItemsSource = new[] { "Main deck", "Sideboard" };
            AddToBox.SelectedIndex = 0;

            BuildMenus();

            // Double-click a Non-Foil / Foil / Etched cell of a deck line to type its count.
            _bottom.CellDoubleClickHandler = (row, header, cell) =>
            {
                if (row is not DeckCard line) return false;
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
            };

            UpdateButtons();
            UpdateUndo();
        }

        /// <summary>Called each time the page is shown (Edit → Decks → Pool → Deck).</summary>
        public void Start()
        {
            if (!_started)
            {
                _started = true;
                _top.LoadPool("Cards");
            }
            // Always starts with no deck open: nothing can be added to or
            // removed from a deck by accident until one is picked.
            FillDecks(null);
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
            if (DeckBox.SelectedItem == NoDeck)
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
            _bottom.ShowNoDeck(message);
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
            _bottom.ShowDeck(deck, path, select, fallback);
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
            bool side = _deck != null && (rule.SideboardMax != null || rule.Type == DeckType.Limited);
            AddToLabel.Visibility = AddToBox.Visibility = side ? Visibility.Visible : Visibility.Collapsed;
            BtnDeckSettings.IsEnabled = BtnCloseDeck.IsEnabled = _deck != null;
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
            if (ReadFile(path) is { } after) PushUndo(path, before, after, "Deck settings");
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

        // Pool cards by ScryfallId (finishes and legality for deck lines), built once.
        private Dictionary<string, PoolCard>? _poolById;
        private int _poolCount = -1;

        private PoolCard? PoolFor(string sid)
        {
            if (_poolById == null || _poolCount != _top.LoadedCount)
            {
                _poolCount = _top.LoadedCount;
                var rows = _top.FindAllLoaded(r => r is PoolCard);
                if (rows.Count == 0) return null;             // still loading: try again later
                _poolById = new Dictionary<string, PoolCard>(StringComparer.OrdinalIgnoreCase);
                foreach (PoolCard p in rows) _poolById.TryAdd(p.ScryfallId, p);
            }
            return _poolById.TryGetValue(sid, out var pc) ? pc : null;
        }

        /// <summary>
        /// What a row adds to the deck: a pool card becomes a deck card; a deck
        /// line adds more of itself (finishes and legality from the pool when found).
        /// </summary>
        private DeckCard? TemplateFor(object row)
        {
            if (row is PoolCard p) return DeckService.FromPoolCard(p);
            if (row is DeckCard line)
            {
                var pool = PoolFor(line.ScryfallId);
                return pool != null ? DeckService.FromPoolCard(pool) : line;
            }
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

            BtnAddNonFoil.IsEnabled = haveDeck && (many || tmpl?.IsNonFoil == true);
            BtnAddFoil.IsEnabled = haveDeck && (many || tmpl?.IsFoil == true);
            BtnAddEtched.IsEnabled = haveDeck && (many || tmpl?.IsEtched == true);
            BtnRemoveNonFoil.IsEnabled = haveDeck && (many || lines.Any(l => l.Quantity > 0));
            BtnRemoveFoil.IsEnabled = haveDeck && (many || lines.Any(l => l.FoilQuantity > 0));
            BtnRemoveEtched.IsEnabled = haveDeck && (many || lines.Any(l => l.EtchedQuantity > 0));
            BtnRemoveLine.IsEnabled = haveDeck && (many || lines.Count > 0);

            if (!haveDeck)
                ModeText.Text = "Pool → Deck. Pick a deck (or make a new one), then add cards from the pool table.";
            else if (_current == null)
                ModeText.Text = $"Pool → Deck · {_deck!.Name} ({Rule.Name}). Select a card in either table " +
                                "(Ctrl+click or Shift+click for several).";
            else if (many)
                ModeText.Text = $"{selected} rows selected in the {(_active == _bottom ? "deck" : "pool")} table" +
                                (_active == _top ? $"  ·  adds go to the {DeckEditService.SectionName(AddSection)}" : "");
            else
                ModeText.Text = $"{CardText(_current)}  ·  {InDeckText(_current)}";
        }

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

            var rule = Rule;
            string name = Str(card, "Name");
            string key = DeckIndexService.CardKey(name);
            var counted = rule.SideboardMax == null
                ? _deck.Cards.Where(c => c.Category != DeckCardCategory.Sideboard)
                : _deck.Cards;
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
            bool fits = table == null || (table == _bottom) == (_current is DeckCard);
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
        private void Edit(string what, Func<List<DeckEditResult>?> work)
        {
            if (_deck == null || _deckPath == null) return;
            string before = ReadFile(_deckPath) ?? DeckService.ToJson(_deck);

            var results = work();
            if (results == null)
            {
                OpenDeck(_deckPath);                     // drop anything done in memory
                ShowStatus("Nothing was changed.", false);
                return;
            }
            var result = Combine(what, results);
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
                if (ReadFile(_deckPath) is { } after) PushUndo(_deckPath, before, after, result.Message);
            }
            AfterEdit(result);
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

        /// <summary>After every change: status line, then the deck re-read with the changed lines selected.</summary>
        private void AfterEdit(DeckEditResult result)
        {
            ShowStatus(result.Message, result.Warning);
            if (result.Changed <= 0 || _deckPath == null) { UpdateButtons(); return; }

            var keys = new HashSet<DeckLineKey>(result.Touched);
            string sid = _current != null ? Str(_current, "ScryfallId") : "";
            OpenDeck(_deckPath,
                keys.Count > 0 ? r => r is DeckCard dc && keys.Contains(DeckLineKey.Of(dc)) : null,
                sid.Length > 0 ? r => r is DeckCard dc && string.Equals(dc.ScryfallId, sid, StringComparison.OrdinalIgnoreCase) : null);
        }

        // ── Add ─────────────────────────────────────────────────────────
        private sealed record AddPlan(DeckCard Card, string Finish, DeckCardCategory Section, int Qty);

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
            bool leader = section == DeckCardCategory.Commander;
            int qty = leader ? 1 : Qty;

            var plan = new List<AddPlan>();
            var refused = new List<DeckEditResult>();
            foreach (var row in rows)
            {
                var card = TemplateFor(row);
                if (card == null) continue;
                var line = row as DeckCard;
                string fin = finish ?? (line != null ? LineFinish(line) : DefaultFinish(card));
                var sec = section ?? (line != null ? DeckEditService.SectionOf(line) : AddSection);
                if (!DeckEditService.HasFinish(card, fin))
                {
                    refused.Add(DeckEditResult.Refused($"{DeckEditService.CardText(card)} doesn't come in {CardFinish.Display(fin)}."));
                    continue;
                }
                if (!plan.Any(p => p.Card.ScryfallId == card.ScryfallId && p.Finish == fin && p.Section == sec))
                    plan.Add(new AddPlan(card, fin, sec, qty));
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
                foreach (var p in plan)
                {
                    var w = DeckRulesService.AddWarnings(_deck!, rule, p.Card, p.Section, p.Qty);
                    if (p.Section == DeckCardCategory.Commander && DeckRulesService.LeaderProblem(rule, p.Card) is { } why)
                        w.Insert(0, why);
                    if (w.Count > 0) { warned.Add(p); warnings.AddRange(w); }
                    results.Add(DeckEditService.Add(_deck!, p.Card, p.Finish, p.Qty, p.Section));
                }
                if (warned.Count == 0) return results;

                string header = plan.Count == 1
                    ? $"Add {DeckEditService.CardText(plan[0].Card)} to {_deck!.Name} anyway?"
                    : $"{warned.Count} of the {plan.Count} cards break a {rule.Name} rule. Add anyway?";
                var choice = AddWarningsDialog.Ask(Window.GetWindow(this), header,
                    warnings.Distinct().ToList(), warned.Count, plan.Count - warned.Count);
                if (choice == AddWarningsChoice.AddAll) return results;
                if (choice == AddWarningsChoice.Cancel) return null;

                // Only the cards without a warning: start again from the file.
                Deck? fresh;
                try { fresh = DeckService.Load(_deckPath!); }
                catch { return null; }
                if (fresh == null) return null;
                _deck = fresh;
                var some = new List<DeckEditResult>(refused);
                foreach (var p in plan.Where(p => !warned.Contains(p)))
                    some.Add(DeckEditService.Add(_deck, p.Card, p.Finish, p.Qty, p.Section));
                return some;
            });
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
            var order = new[] { AddSection, DeckCardCategory.Mainboard, DeckCardCategory.Sideboard, DeckCardCategory.Commander };
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
            if (Targets(_bottom).OfType<DeckCard>().FirstOrDefault() is not { } line)
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
            var keys = Targets(_bottom).OfType<DeckCard>().Select(DeckLineKey.Of).Distinct().ToList();
            if (keys.Count == 0) { ShowStatus("Select a card in the deck table first.", true); return; }
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
            if (Targets(_bottom).OfType<DeckCard>().FirstOrDefault() is not { } line)
            {
                ShowStatus("Select a card in the deck table first.", true);
                return;
            }
            string header = finish switch { CardFinish.Foil => "Foil", CardFinish.Etched => "Etched", _ => "Non-Foil" };
            // After the right-click menu has closed, or it would close the editor at once.
            Dispatcher.BeginInvoke(new Action(() => EditCountInPlace(line, _bottom.CellFor(line, header), finish)),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        // ── Undo ────────────────────────────────────────────────────────
        private List<(string Before, string After, string Text)> UndoStack =>
            _deckPath != null && _undo.TryGetValue(_deckPath, out var s) ? s : new();

        private void PushUndo(string path, string before, string after, string text)
        {
            if (before == after) return;
            if (!_undo.TryGetValue(path, out var stack)) _undo[path] = stack = new();
            stack.Add((before, after, text));
            if (stack.Count > UndoSteps) stack.RemoveAt(0);
            UpdateUndo();
        }

        private void DoUndo()
        {
            var stack = UndoStack;
            if (_deckPath == null || stack.Count == 0) { ShowStatus("Nothing to undo.", false); return; }
            var (before, after, text) = stack[^1];
            stack.RemoveAt(stack.Count - 1);

            // Only when the file is still as this page left it (it can't apply otherwise).
            if (ReadFile(_deckPath) != after)
            {
                stack.Clear();
                UpdateUndo();
                ShowStatus("Can't undo: the deck file was changed outside this page since.", true);
                return;
            }
            try
            {
                string tmp = _deckPath + ".saving";
                File.WriteAllText(tmp, before);
                File.Move(tmp, _deckPath, overwrite: true);
            }
            catch (Exception ex)
            {
                stack.Add((before, after, text));        // nothing was undone: keep the step
                UpdateUndo();
                ShowStatus($"Could not undo: {ex.Message}", true);
                return;
            }
            // Deck Settings may have changed the name, type or format: refresh the list too.
            FillDecks(_deckPath);
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
        private MenuItem? _undoTop, _undoBottom, _addLeaderItem, _makeLeaderItem, _unLeaderItem,
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

            // Top: the card pool.
            Item(_topMenu, "Add Non-Foil", "Enter", () => DoAdd(CardFinish.NonFoil, _top), () => BtnAddNonFoil.IsEnabled, true);
            Item(_topMenu, "Add Foil", "Shift+Enter", () => DoAdd(CardFinish.Foil, _top), () => BtnAddFoil.IsEnabled, true);
            Item(_topMenu, "Add Etched", "Ctrl+Enter", () => DoAdd(CardFinish.Etched, _top), () => BtnAddEtched.IsEnabled, true);
            _addLeaderItem = Item(_topMenu, "Add as Commander", "", () => DoAdd(null, _top, DeckCardCategory.Commander));
            _topMenu.Items.Add(new Separator());
            Item(_topMenu, "Remove Non-Foil", "", () => DoRemove(CardFinish.NonFoil, _top), () => BtnRemoveNonFoil.IsEnabled, true);
            Item(_topMenu, "Remove Foil", "", () => DoRemove(CardFinish.Foil, _top), () => BtnRemoveFoil.IsEnabled, true);
            Item(_topMenu, "Remove Etched", "", () => DoRemove(CardFinish.Etched, _top), () => BtnRemoveEtched.IsEnabled, true);
            Item(_topMenu, "Remove Card (every copy)", "", () => RemoveLines(_top), () => BtnRemoveLine.IsEnabled);
            _topMenu.Items.Add(new Separator());
            _undoTop = Item(_topMenu, "Undo", "Ctrl+Z", DoUndo, () => UndoStack.Count > 0);

            // Bottom: the deck.
            Item(_bottomMenu, "Add Non-Foil", "", () => DoAdd(CardFinish.NonFoil, _bottom), () => BtnAddNonFoil.IsEnabled, true);
            Item(_bottomMenu, "Add Foil", "", () => DoAdd(CardFinish.Foil, _bottom), () => BtnAddFoil.IsEnabled, true);
            Item(_bottomMenu, "Add Etched", "", () => DoAdd(CardFinish.Etched, _bottom), () => BtnAddEtched.IsEnabled, true);
            _bottomMenu.Items.Add(new Separator());
            Item(_bottomMenu, "Remove Non-Foil", "", () => DoRemove(CardFinish.NonFoil, _bottom), () => BtnRemoveNonFoil.IsEnabled, true);
            Item(_bottomMenu, "Remove Foil", "", () => DoRemove(CardFinish.Foil, _bottom), () => BtnRemoveFoil.IsEnabled, true);
            Item(_bottomMenu, "Remove Etched", "", () => DoRemove(CardFinish.Etched, _bottom), () => BtnRemoveEtched.IsEnabled, true);
            Item(_bottomMenu, "Remove Card (every copy)", "Shift+Delete", () => RemoveLines(_bottom), () => BtnRemoveLine.IsEnabled);

            var setCount = new MenuItem { Header = "Set count" };
            foreach (var f in new[] { CardFinish.NonFoil, CardFinish.Foil, CardFinish.Etched })
            {
                string fin = f;
                var mi = new MenuItem { Header = CardFinish.Display(fin) + "…", Tag = fin };
                mi.Click += (_, _) => SetCountOfSelected(fin);
                setCount.Items.Add(mi);
            }
            _bottomMenu.Items.Add(setCount);
            _bottomMenu.Items.Add(new Separator());

            _makeLeaderItem = Item(_bottomMenu, "Set as Commander", "", MakeLeader);
            _unLeaderItem = Item(_bottomMenu, "Move out of the command zone", "",
                () => MoveLines(DeckCardCategory.Mainboard, "Moved to the main deck"));
            _toSideItem = Item(_bottomMenu, "Move to Sideboard", "",
                () => MoveLines(DeckCardCategory.Sideboard, "Moved to the sideboard"));
            _toMainItem = Item(_bottomMenu, "Move to Main Deck", "",
                () => MoveLines(DeckCardCategory.Mainboard, "Moved to the main deck"));
            _bottomMenu.Items.Add(new Separator());
            _undoBottom = Item(_bottomMenu, "Undo", "Ctrl+Z", DoUndo, () => UndoStack.Count > 0);

            _topMenu.Opened += (_, _) => MenuOpened(_top);
            _bottomMenu.Opened += (_, _) => MenuOpened(_bottom);
            _top.SetRowContextMenu(_topMenu);
            _bottom.SetRowContextMenu(_bottomMenu);
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
            if (_undoTop != null) _undoTop.Header = undoText;
            if (_undoBottom != null) _undoBottom.Header = undoText;

            // Command zone and sideboard items only where the format has them.
            var rule = Rule;
            var leaderVis = _deck != null && rule.HasLeader ? Visibility.Visible : Visibility.Collapsed;
            string leaderName = DeckRulesService.LeaderName(rule);
            _addLeaderItem!.Visibility = leaderVis;
            _addLeaderItem.Header = $"Add as {leaderName}";
            var lines = Targets(_bottom).OfType<DeckCard>().ToList();
            bool anyLeader = lines.Any(l => DeckEditService.SectionOf(l) == DeckCardCategory.Commander);
            bool anySide = lines.Any(l => l.Category == DeckCardCategory.Sideboard);
            bool anyMain = lines.Any(l => DeckEditService.SectionOf(l) == DeckCardCategory.Mainboard);
            bool hasSide = rule.SideboardMax != null || rule.Type == DeckType.Limited;

            _makeLeaderItem!.Header = $"Set as {leaderName}";
            _makeLeaderItem.Visibility = leaderVis;
            _makeLeaderItem.IsEnabled = lines.Count == 1 && !anyLeader;
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
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) RemoveLines(_bottom);
            else DoRemove(null, _bottom);
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
                DoAdd(null);
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