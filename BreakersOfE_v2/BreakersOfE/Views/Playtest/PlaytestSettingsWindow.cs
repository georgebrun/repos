using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BreakersOfE.Models;
using BreakersOfE.Playtest;
using BreakersOfE.Services;
using BreakersOfE.Views.Dialogs;

namespace BreakersOfE.Views.Playtest
{
    /// <summary>
    /// ⚙ Table: the playtest table's own settings, apart from BoE's Settings
    /// page. Sleeves and the playmat can be your defaults (every deck) or
    /// this deck's own. Each change is saved at once
    /// (Tabletop\PlaytestSettings.json) and shows on the table when this closes.
    /// </summary>
    internal sealed class PlaytestSettingsWindow : Window
    {
        private static PlaytestSettings S => PlaytestSettings.Current;

        private readonly Deck _deck;
        private bool _forDeck;
        private bool _loading;

        private readonly ComboBox _sleeveBox = new() { Width = 300, Margin = new Thickness(0, 0, 0, 6) };
        private readonly ComboBox _matBox = new() { Width = 300, Margin = new Thickness(0, 0, 0, 6) };
        private readonly ComboBox _badgeBox = new() { Width = 300, Margin = new Thickness(0, 0, 0, 6) };
        private readonly Image _badgePreview = new() { Width = 72, Height = 72, Stretch = Stretch.Fill };
        private List<string?> _badgeValues = new();
        private readonly ComboBox _oppBadgeBox = new() { Width = 300, Margin = new Thickness(0, 0, 0, 6) };
        private readonly Image _oppBadgePreview = new() { Width = 56, Height = 56, Stretch = Stretch.Fill };
        private List<string?> _oppBadgeValues = new();
        private readonly Image _sleevePreview = new() { Width = 72, Height = 72 * 680.0 / 488.0, Stretch = Stretch.Fill };
        private readonly Image _matPreview = new() { Width = 150, Height = 96, Stretch = Stretch.UniformToFill };
        private readonly Border _matShade = new() { Background = Brushes.Black, IsHitTestVisible = false };
        private readonly Border _matBox2 = new() { Width = 150, Height = 96, CornerRadius = new CornerRadius(5), ClipToBounds = true };
        private readonly Slider _dim = new() { Minimum = 0, Maximum = 0.8, SmallChange = 0.05, LargeChange = 0.1, Width = 180 };
        private readonly WrapPanel _swatches = new() { Margin = new Thickness(0, 4, 0, 0) };
        private readonly TextBlock _scopeHint = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };

        /// <summary>Picture choices in the boxes: null = the deck uses your default, "" = none.</summary>
        private List<string?> _sleeveValues = new();
        private List<string?> _matValues = new();

        private static readonly (string Name, string Hex)[] Colors =
        {
            ("Felt (the default)", PlaytestSettings.DefaultTableColor),
            ("Charcoal", "#2A2B2E"),
            ("Slate", "#2C3440"),
            ("Navy", "#1E2A3E"),
            ("Forest", "#1F3A26"),
            ("Burgundy", "#3E2228"),
            ("Walnut", "#3A2D22"),
            ("Midnight", "#121316"),
        };

        private readonly double _fieldAspect;

        internal PlaytestSettingsWindow(Window owner, Deck deck, double fieldAspect)
        {
            _deck = deck;
            _fieldAspect = Math.Clamp(fieldAspect, 0.5, 4);
            Owner = owner;
            Title = "Table Settings";
            Width = 640;
            Height = 640;                                              // one size for every tab (no jumping as you switch)
            MinWidth = 520;
            MinHeight = 420;
            ResizeMode = ResizeMode.CanResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
            SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
            KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

            var root = new DockPanel { Margin = new Thickness(18, 14, 18, 14) };
            var top = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            top.Children.Add(new TextBlock { Text = "Table Settings", FontSize = 18, FontWeight = FontWeights.SemiBold });
            top.Children.Add(Hint("These belong to the playtest table only; BoE's own Settings don't change them. Saved as you go."));
            DockPanel.SetDock(top, Dock.Top);
            root.Children.Add(top);

            // One tab per kind of setting (the second player's seat will get its own place here).
            var lookTab = TabPage();      // sleeves, playmat, table color
            var playerTab = TabPage();    // you and player 2: names and badges
            var tableTab = TabPage();     // how the table shows things
            var rulesTab = TabPage();     // the game's rules, as you play them
            var assistTab = TabPage();    // what the table does for you
            var gamesTab = TabPage();     // unfinished games

            // ── Whose look ──
            lookTab.Children.Add(Heading("SLEEVES AND PLAYMAT FOR"));
            var scope = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            var all = new RadioButton { Content = "Every deck (my defaults)", IsChecked = true, Margin = new Thickness(0, 0, 18, 0), GroupName = "scope" };
            var mine = new RadioButton { Content = $"This deck only: {deck.Name}", GroupName = "scope" };
            all.Checked += (_, _) => { _forDeck = false; FillPictures(); };
            mine.Checked += (_, _) => { _forDeck = true; FillPictures(); };
            scope.Children.Add(all);
            scope.Children.Add(mine);
            if (string.IsNullOrEmpty(deck.DeckId)) mine.IsEnabled = false;
            lookTab.Children.Add(scope);
            _scopeHint.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            lookTab.Children.Add(_scopeHint);

            // ── Sleeves ──
            lookTab.Children.Add(Heading("SLEEVES"));
            lookTab.Children.Add(Hint("The back of your cards: your library and face-down cards."));
            var sleeveRow = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var sleeveFrame = new Border { CornerRadius = new CornerRadius(5), ClipToBounds = true, Child = _sleevePreview, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top };
            DockPanel.SetDock(sleeveFrame, Dock.Left);
            sleeveRow.Children.Add(sleeveFrame);
            var sleeveRight = new StackPanel();
            sleeveRight.Children.Add(_sleeveBox);
            var sleeveButtons = new WrapPanel();
            sleeveButtons.Children.Add(Btn("Fit…", () => Fit(sleeve: true)));
            sleeveButtons.Children.Add(Btn("Add a Picture…", () => AddPicture(AppFolderService.SleeveImagesFolder, "A picture for your sleeves", SetSleeve)));
            sleeveButtons.Children.Add(Btn("Open Sleeves Folder", () => OpenFolder(AppFolderService.SleeveImagesFolder)));
            sleeveRight.Children.Add(sleeveButtons);
            sleeveRow.Children.Add(sleeveRight);
            lookTab.Children.Add(sleeveRow);
            _sleeveBox.SelectionChanged += (_, _) => { if (!_loading) SetSleeve(Pick(_sleeveBox, _sleeveValues)); };

            // ── Playmat ──
            lookTab.Children.Add(Heading("PLAYMAT"));
            lookTab.Children.Add(Hint("A picture under your battlefield, filled edge to edge (cropped, never stretched). None: just the table color."));
            var matRow = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var matGrid = new Grid();
            matGrid.Children.Add(_matPreview);
            matGrid.Children.Add(_matShade);
            _matBox2.Child = matGrid;
            _matBox2.Margin = new Thickness(0, 0, 14, 0);
            _matBox2.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(_matBox2, Dock.Left);
            matRow.Children.Add(_matBox2);
            var matRight = new StackPanel();
            matRight.Children.Add(_matBox);
            var matButtons = new WrapPanel();
            matButtons.Children.Add(Btn("Fit…", () => Fit(sleeve: false)));
            matButtons.Children.Add(Btn("Add a Picture…", () => AddPicture(AppFolderService.PlaymatImagesFolder, "A picture for your playmat", SetPlaymat)));
            matButtons.Children.Add(Btn("Open Playmats Folder", () => OpenFolder(AppFolderService.PlaymatImagesFolder)));
            matRight.Children.Add(matButtons);
            var dimRow = new StackPanel { Orientation = Orientation.Horizontal };
            dimRow.Children.Add(new TextBlock { Text = "Darken it", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            dimRow.Children.Add(_dim);
            var dimValue = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            dimRow.Children.Add(dimValue);
            matRight.Children.Add(dimRow);
            matRow.Children.Add(matRight);
            lookTab.Children.Add(matRow);
            _matBox.SelectionChanged += (_, _) => { if (!_loading) SetPlaymat(Pick(_matBox, _matValues)); };
            _dim.Value = S.PlaymatDim;
            dimValue.Text = $"{S.PlaymatDim:P0}";
            _dim.ValueChanged += (_, e) =>
            {
                S.PlaymatDim = Math.Round(e.NewValue, 2);
                dimValue.Text = $"{S.PlaymatDim:P0}";
                Save();
            };

            // ── Badge ──
            playerTab.Children.Add(Heading("BADGE"));
            playerTab.Children.Add(Hint("You, the player: the picture beside your life on the table, whichever deck you play. None: a plain person icon."));
            var badgeRow = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var badgeFrame = new Border
            {
                CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = _badgePreview, Margin = new Thickness(0, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Top, Background = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, 255, 255, 255)), BorderThickness = new Thickness(1),
            };
            DockPanel.SetDock(badgeFrame, Dock.Left);
            badgeRow.Children.Add(badgeFrame);
            var badgeRight = new StackPanel();
            badgeRight.Children.Add(_badgeBox);
            var badgeButtons = new WrapPanel();
            badgeButtons.Children.Add(Btn("Fit…", FitBadge));
            badgeButtons.Children.Add(Btn("Add a Picture…", () => AddPicture(AppFolderService.BadgeImagesFolder, "A picture for your badge", SetBadge)));
            badgeButtons.Children.Add(Btn("Open Badges Folder", () => OpenFolder(AppFolderService.BadgeImagesFolder)));
            badgeRight.Children.Add(badgeButtons);
            badgeRow.Children.Add(badgeRight);
            playerTab.Children.Add(badgeRow);
            _badgeBox.SelectionChanged += (_, _) => { if (!_loading) SetBadge(Pick(_badgeBox, _badgeValues)); };

            // ── Player 2 ──
            playerTab.Children.Add(Heading("PLAYER 2"));
            playerTab.Children.Add(Hint("Across the table, when New Game ▾ plays against another deck. Their sleeves and playmat are that deck's (Look → This deck only). A new name shows from the next New Game."));
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 4) };
            nameRow.Children.Add(new TextBlock { Text = "Name", Width = 86, VerticalAlignment = VerticalAlignment.Center });
            var oppName = new TextBox { Width = 230, Text = S.OpponentName, Padding = new Thickness(6, 3, 6, 3) };
            oppName.LostFocus += (_, _) =>
            {
                S.OpponentName = oppName.Text.Trim();
                oppName.Text = S.OpponentNameOrDefault();
                S.OpponentName = oppName.Text;
                Save();
            };
            nameRow.Children.Add(oppName);
            playerTab.Children.Add(nameRow);
            var oppBadgeRow = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var oppBadgeFrame = new Border
            {
                CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = _oppBadgePreview, Margin = new Thickness(0, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Top, Background = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, 255, 255, 255)), BorderThickness = new Thickness(1),
            };
            DockPanel.SetDock(oppBadgeFrame, Dock.Left);
            oppBadgeRow.Children.Add(oppBadgeFrame);
            var oppBadgeRight = new StackPanel();
            oppBadgeRight.Children.Add(_oppBadgeBox);
            var oppBadgeButtons = new WrapPanel();
            oppBadgeButtons.Children.Add(Btn("Fit…", FitOppBadge));
            oppBadgeButtons.Children.Add(Btn("Add a Picture…", () => AddPicture(AppFolderService.BadgeImagesFolder, "A picture for player 2's badge", SetOppBadge)));
            oppBadgeRight.Children.Add(oppBadgeButtons);
            oppBadgeRow.Children.Add(oppBadgeRight);
            playerTab.Children.Add(oppBadgeRow);
            _oppBadgeBox.SelectionChanged += (_, _) => { if (!_loading) SetOppBadge(Pick(_oppBadgeBox, _oppBadgeValues)); };

            // ── Table color ──
            lookTab.Children.Add(Heading("TABLE COLOR"));
            lookTab.Children.Add(Hint("Behind the playmat, the zones and your hand."));
            lookTab.Children.Add(_swatches);

            // ── The table ──
            tableTab.Children.Add(Heading("CARDS"));
            var sizeRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var sizeLabel = new TextBlock { Text = "Card size", Width = 150, VerticalAlignment = VerticalAlignment.Center };
            var sizeValue = new TextBlock { Width = 50, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
            var slider = new Slider { Minimum = 0.6, Maximum = 1.8, Value = S.CardScale, SmallChange = 0.05, LargeChange = 0.1, VerticalAlignment = VerticalAlignment.Center };
            sizeValue.Text = $"{S.CardScale:P0}";
            slider.ValueChanged += (_, e) => { S.CardScale = Math.Round(e.NewValue, 2); sizeValue.Text = $"{S.CardScale:P0}"; Save(); };
            DockPanel.SetDock(sizeLabel, Dock.Left);
            DockPanel.SetDock(sizeValue, Dock.Right);
            sizeRow.Children.Add(sizeLabel);
            sizeRow.Children.Add(sizeValue);
            sizeRow.Children.Add(slider);
            tableTab.Children.Add(sizeRow);
            tableTab.Children.Add(Check("Grow the card under the mouse, big enough to read (the hand lifts its own)", S.ShowCardPanel, v => S.ShowCardPanel = v));
            tableTab.Children.Add(Check("Show keywords on creatures (flying, trample …) and summoning sickness", S.ShowKeywords, v => S.ShowKeywords = v));
            tableTab.Children.Add(Heading("MESSAGES"));
            tableTab.Children.Add(Check("Show the message bar (the game log) along the bottom", S.ShowLog, v => S.ShowLog = v));

            // ── The rules ──
            rulesTab.Children.Add(Heading("STARTING THE GAME"));
            var lifeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            lifeRow.Children.Add(new TextBlock { Text = "Starting life", Width = 150, VerticalAlignment = VerticalAlignment.Center });
            var life = new ComboBox { Width = 230 };
            var lives = new[] { (0, "By the deck's format"), (20, "20"), (25, "25"), (30, "30"), (40, "40") };
            foreach (var (_, label) in lives) life.Items.Add(label);
            life.SelectedIndex = Math.Max(0, Array.FindIndex(lives, l => l.Item1 == S.StartingLife));
            life.SelectionChanged += (_, _) => { S.StartingLife = lives[Math.Max(0, life.SelectedIndex)].Item1; Save(); };
            lifeRow.Children.Add(life);
            rulesTab.Children.Add(lifeRow);
            rulesTab.Children.Add(Hint("By the format: 40 in Commander, 30 Pauper Commander, 25 Brawl, 20 the rest. Takes effect with the next New Game."));
            rulesTab.Children.Add(Check("The first mulligan is free (Commander-style decks)", S.FreeFirstMulligan, v => S.FreeFirstMulligan = v));
            rulesTab.Children.Add(Heading("EACH TURN"));
            rulesTab.Children.Add(Check("Next Turn draws a card", S.DrawOnNewTurn, v => S.DrawOnNewTurn = v));
            var handRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 4) };
            handRow.Children.Add(new TextBlock { Text = "Maximum hand size", Width = 150, VerticalAlignment = VerticalAlignment.Center });
            var hand = new ComboBox { Width = 230 };
            var hands = new[] { (7, "7 (the rules)"), (8, "8"), (10, "10"), (0, "No maximum") };
            foreach (var (_, label) in hands) hand.Items.Add(label);
            hand.SelectedIndex = Math.Max(0, Array.FindIndex(hands, h => h.Item1 == S.MaxHandSize));
            hand.SelectionChanged += (_, _) => { S.MaxHandSize = hands[Math.Max(0, hand.SelectedIndex)].Item1; Save(); };
            handRow.Children.Add(hand);
            rulesTab.Children.Add(handRow);
            rulesTab.Children.Add(Hint("At Next Turn, a bigger hand asks you to discard down first (Skip is always there)."));

            var manaRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 4) };
            manaRow.Children.Add(new TextBlock { Text = "Unspent mana empties", Width = 150, VerticalAlignment = VerticalAlignment.Center });
            var empties = new ComboBox { Width = 230 };
            var emptyChoices = new[] { (ManaEmptying.EndOfTurn, "At the end of the turn"), (ManaEmptying.EveryStep, "Every step (the rules)"), (ManaEmptying.Never, "Never (I empty it)") };
            foreach (var (_, label) in emptyChoices) empties.Items.Add(label);
            empties.SelectedIndex = Math.Max(0, Array.FindIndex(emptyChoices, x => x.Item1 == S.ManaEmpties));
            empties.SelectionChanged += (_, _) => { S.ManaEmpties = emptyChoices[Math.Max(0, empties.SelectedIndex)].Item1; Save(); };
            manaRow.Children.Add(empties);
            rulesTab.Children.Add(manaRow);
            rulesTab.Children.Add(Hint("The rules empty it as every step ends; Omnath, Leyline Tyrant, Upwelling and Kruphix keep theirs whichever you pick."));
            rulesTab.Children.Add(Check("Spells go on the stack first, lands don't (Resolve or R finishes them)", S.StackFirst, v => S.StackFirst = v));

            // ── What the table does for you ──
            assistTab.Children.Add(Heading("MANA"));
            assistTab.Children.Add(Check("Tapping a land or mana stone adds its mana to my pool (a choice asks which)", S.TapForMana, v => S.TapForMana = v));
            assistTab.Children.Add(Heading("REMINDERS"));
            assistTab.Children.Add(Check("Remind me of \"at the beginning of …\" cards as the steps come", S.ShowReminders, v => S.ShowReminders = v));
            assistTab.Children.Add(Check("Tell me when one of the deck's combos is ready", S.ComboAlerts, v => S.ComboAlerts = v));
            assistTab.Children.Add(Heading("CARD ABILITIES"));
            var abilityRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 4) };
            abilityRow.Children.Add(new TextBlock { Text = "Card abilities", Width = 150, VerticalAlignment = VerticalAlignment.Center });
            var abilityBox = new ComboBox { Width = 230 };
            var abilityChoices = new[] { (AbilityUse.Do, "Do what they say"), (AbilityUse.Remind, "Just remind me"), (AbilityUse.Off, "Off") };
            foreach (var (_, label) in abilityChoices) abilityBox.Items.Add(label);
            abilityBox.SelectedIndex = Math.Max(0, Array.FindIndex(abilityChoices, a => a.Item1 == S.UseAbilities));
            abilityBox.SelectionChanged += (_, _) => { S.UseAbilities = abilityChoices[Math.Max(0, abilityBox.SelectedIndex)].Item1; Save(); };
            abilityRow.Children.Add(abilityBox);
            var coverage = new Button { Content = "Coverage…", Padding = new Thickness(10, 2, 10, 3), Margin = new Thickness(8, 0, 0, 0),
                                        ToolTip = "How many of your card pool's abilities the table can read, and the wordings it can't yet" };
            coverage.Click += (_, _) => new AbilityCoverageWindow(this).ShowDialog();
            abilityRow.Children.Add(coverage);
            assistTab.Children.Add(abilityRow);
            assistTab.Children.Add(Hint("Ability buttons on the action bar and in the right-click menu: each is a \"you may\". Do what they say pays the costs and does what the table can read; the log tells you the rest."));

            // ── Unfinished games ──
            gamesTab.Children.Add(Heading("UNFINISHED GAMES"));
            gamesTab.Children.Add(Check("Keep an unfinished game when the table closes, and offer it next time", S.KeepGames, v => S.KeepGames = v));
            int saved = SavedGames.Count();
            var clear = new Button
            {
                Content = saved > 0 ? $"Clear Saved Games ({saved})" : "Clear Saved Games", Padding = new Thickness(10, 2, 10, 3),
                Margin = new Thickness(24, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = saved > 0,
                ToolTip = "Delete every unfinished game kept for your decks (Tabletop\\Games). The game on the table now is kept again when the table closes, unless you start a new one.",
            };
            clear.Click += (_, _) =>
            {
                var r = System.Windows.MessageBox.Show(this, $"Delete the {SavedGames.Count()} saved game(s)? This can't be undone.\n\nThe game on the table now is kept again when the table closes (unless you start a new one or turn keeping off).",
                    "Clear Saved Games", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                if (r != MessageBoxResult.OK) return;
                int gone = SavedGames.DeleteAll();
                clear.Content = $"Cleared ({gone})";
                clear.IsEnabled = false;
            };
            gamesTab.Children.Add(clear);

            var close = new Button { Content = "Close", Padding = new Thickness(16, 4, 16, 4), IsCancel = true, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            close.Click += (_, _) => Close();
            DockPanel.SetDock(close, Dock.Bottom);
            root.Children.Add(close);

            var tabs = new TabControl();
            tabs.Items.Add(Tab("Look", lookTab));
            tabs.Items.Add(Tab("Player", playerTab));
            tabs.Items.Add(Tab("Table", tableTab));
            tabs.Items.Add(Tab("Rules", rulesTab));
            tabs.Items.Add(Tab("Assists", assistTab));
            tabs.Items.Add(Tab("Saved Games", gamesTab));
            tabs.SelectedIndex = Math.Clamp(_lastTab, 0, tabs.Items.Count - 1);       // back on the tab you left
            tabs.SelectionChanged += (_, e) => { if (e.OriginalSource == tabs) _lastTab = tabs.SelectedIndex; };
            root.Children.Add(tabs);
            Content = root;

            // Opened on a deck that has its own look: start on that.
            var ownLook = S.LookOf(deck.DeckId);
            if (ownLook != null && (ownLook.Sleeve != null || ownLook.Playmat != null)) mine.IsChecked = true;
            FillPictures();
            FillSwatches();
        }

        // ── Pictures ─────────────────────────────────────────────────────
        /// <summary>Fill both boxes for the chosen scope (defaults, or this deck), with the current choices selected.</summary>
        private void FillPictures()
        {
            _loading = true;
            var lookNow = S.LookOf(_deck.DeckId);
            _scopeHint.Text = _forDeck
                ? "This deck plays in these; \"Use my default\" follows your defaults."
                : "Every deck plays in these unless it has its own (choose \"This deck only\").";

            _sleeveValues = new List<string?>();
            _sleeveBox.Items.Clear();
            if (_forDeck) { _sleeveValues.Add(null); _sleeveBox.Items.Add($"Use my default ({SleeveLabel(S.Sleeve)})"); }
            _sleeveValues.Add(""); _sleeveBox.Items.Add("The Magic card back");
            foreach (var f in PlaytestSettings.Pictures(AppFolderService.SleeveImagesFolder)) { _sleeveValues.Add(f); _sleeveBox.Items.Add(f); }
            Select(_sleeveBox, _sleeveValues, _forDeck ? lookNow?.Sleeve : S.Sleeve);

            _matValues = new List<string?>();
            _matBox.Items.Clear();
            if (_forDeck) { _matValues.Add(null); _matBox.Items.Add($"Use my default ({MatLabel(S.Playmat)})"); }
            _matValues.Add(""); _matBox.Items.Add("No playmat (the table color)");
            foreach (var f in PlaytestSettings.Pictures(AppFolderService.PlaymatImagesFolder)) { _matValues.Add(f); _matBox.Items.Add(f); }
            Select(_matBox, _matValues, _forDeck ? lookNow?.Playmat : S.Playmat);

            // The badge is the player's, not the deck's: the same list whichever scope is chosen above.
            _badgeValues = new List<string?>();
            _badgeBox.Items.Clear();
            _badgeValues.Add(""); _badgeBox.Items.Add("None (a plain person icon)");
            foreach (var f in PlaytestSettings.Pictures(AppFolderService.BadgeImagesFolder)) { _badgeValues.Add(f); _badgeBox.Items.Add(f); }
            Select(_badgeBox, _badgeValues, S.Badge);
            _oppBadgeValues = new List<string?>();
            _oppBadgeBox.Items.Clear();
            _oppBadgeValues.Add(""); _oppBadgeBox.Items.Add("None (a plain person icon)");
            foreach (var f in PlaytestSettings.Pictures(AppFolderService.BadgeImagesFolder)) { _oppBadgeValues.Add(f); _oppBadgeBox.Items.Add(f); }
            Select(_oppBadgeBox, _oppBadgeValues, S.OpponentBadge);
            _loading = false;
            ShowPreviews();
        }

        private static string SleeveLabel(string f) => string.IsNullOrEmpty(f) ? "the Magic card back" : f;
        private static string MatLabel(string f) => string.IsNullOrEmpty(f) ? "no playmat" : f;

        private static void Select(ComboBox box, List<string?> values, string? value)
        {
            int i = values.FindIndex(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));
            box.SelectedIndex = i >= 0 ? i : 0;
        }

        private static string? Pick(ComboBox box, List<string?> values) =>
            box.SelectedIndex >= 0 && box.SelectedIndex < values.Count ? values[box.SelectedIndex] : "";

        private PlaytestSettings.DeckLook DeckLook()
        {
            if (!S.Decks.TryGetValue(_deck.DeckId, out var l)) S.Decks[_deck.DeckId] = l = new PlaytestSettings.DeckLook();
            return l;
        }

        private void TidyDeckLook()
        {
            if (S.Decks.TryGetValue(_deck.DeckId, out var l) && l.Sleeve == null && l.Playmat == null)
                S.Decks.Remove(_deck.DeckId);
        }

        private void SetSleeve(string? value)
        {
            if (_forDeck) { DeckLook().Sleeve = value; TidyDeckLook(); }
            else S.Sleeve = value ?? "";
            Save();                                                    // (the deck boxes' "Use my default (…)" refresh when shown)
        }

        private void SetPlaymat(string? value)
        {
            if (_forDeck) { DeckLook().Playmat = value; TidyDeckLook(); }
            else S.Playmat = value ?? "";
            Save();
        }

        private void SetBadge(string? value)
        {
            S.Badge = value ?? "";
            Save();
        }

        private void SetOppBadge(string? value)
        {
            S.OpponentBadge = value ?? "";
            Save();
        }

        /// <summary>Fit… for player 2's badge (each picture keeps one fit, whoever wears it).</summary>
        private void FitOppBadge() => FitBadgeFile(S.OpponentBadge, S.OpponentBadgePath());

        /// <summary>Fit…: which part of the badge picture shows in its square.</summary>
        private void FitBadge() => FitBadgeFile(S.Badge, S.BadgePath());

        private void FitBadgeFile(string file, string path)
        {
            if (path.Length == 0)
            {
                System.Windows.MessageBox.Show(this, "Pick a badge picture first.", "Table Settings",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string key = PlaytestSettings.BadgeKey(file);
            var result = FitPictureWindow.Ask(this, path, 1.0, S.FramingOf(key).Clone(), "Fit the badge", Brushes.Black);
            if (result == null) return;
            if (result.IsDefault) S.Framings.Remove(key); else S.Framings[key] = result;
            Save();
        }

        private string? ScopeDeck => _forDeck ? _deck.DeckId : null;

        private void ShowPreviews()
        {
            string sp = S.SleevePath(ScopeDeck);
            var sb = sp.Length > 0 ? ImageCacheService.LoadBitmap(sp, 400) : null;
            _sleevePreview.Source = sb != null
                ? PlaytestWindow.Framed(sb, S.FramingOf(PlaytestSettings.SleeveKey(S.SleeveFor(ScopeDeck))), PlaytestWindow.CardAspect, Brushes.Black)
                : CardPictures.CardBack;
            string mp = S.PlaymatPath(ScopeDeck);
            var mb = mp.Length > 0 ? ImageCacheService.LoadBitmap(mp, 800) : null;
            _matBox2.Height = Math.Clamp(_matBox2.Width / _fieldAspect, 50, 140);
            _matPreview.Height = _matBox2.Height;
            _matPreview.Stretch = Stretch.Fill;
            _matPreview.Source = mb != null
                ? PlaytestWindow.Framed(mb, S.FramingOf(PlaytestSettings.PlaymatKey(S.PlaymatFor(ScopeDeck))), _fieldAspect)
                : null;
            _matShade.Opacity = _matPreview.Source != null ? S.PlaymatDim : 0;
            _matBox2.Background = PlaytestWindow.TableBrush(S.TableColor);
            string bp = S.BadgePath();
            var bb = bp.Length > 0 ? ImageCacheService.LoadBitmap(bp, 300) : null;
            _badgePreview.Source = bb != null ? PlaytestWindow.Framed(bb, S.FramingOf(PlaytestSettings.BadgeKey(S.Badge)), 1.0, Brushes.Black) : null;
            string ob = S.OpponentBadgePath();
            var obb = ob.Length > 0 ? ImageCacheService.LoadBitmap(ob, 300) : null;
            _oppBadgePreview.Source = obb != null ? PlaytestWindow.Framed(obb, S.FramingOf(PlaytestSettings.BadgeKey(S.OpponentBadge)), 1.0, Brushes.Black) : null;
        }

        /// <summary>Fit…: choose which part of the sleeve or playmat picture shows (saved for that picture).</summary>
        private void Fit(bool sleeve)
        {
            string file = sleeve ? S.SleeveFor(ScopeDeck) : S.PlaymatFor(ScopeDeck);
            string path = sleeve ? S.SleevePath(ScopeDeck) : S.PlaymatPath(ScopeDeck);
            if (path.Length == 0)
            {
                System.Windows.MessageBox.Show(this, sleeve ? "Pick a sleeve picture first (the Magic card back is already a perfect fit)."
                                                            : "Pick a playmat picture first.",
                    "Table Settings", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string key = sleeve ? PlaytestSettings.SleeveKey(file) : PlaytestSettings.PlaymatKey(file);
            var result = FitPictureWindow.Ask(this, path, sleeve ? PlaytestWindow.CardAspect : _fieldAspect,
                                              S.FramingOf(key).Clone(), sleeve ? "Fit the sleeve" : "Fit the playmat",
                                              sleeve ? Brushes.Black : PlaytestWindow.TableBrush(S.TableColor));
            if (result == null) return;
            if (result.IsDefault) S.Framings.Remove(key); else S.Framings[key] = result;
            Save();
        }

        /// <summary>Add a Picture…: copied into the Sleeves, Playmats or Badges folder, then chosen.</summary>
        private void AddPicture(string folder, string title, Action<string?> choose)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = title,
                Filter = "Pictures (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files (*.*)|*.*",
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                string name = Path.GetFileName(dlg.FileName);
                string target = Path.Combine(folder, name);
                bool already = string.Equals(Path.GetFullPath(dlg.FileName), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase);
                if (!already)
                {
                    for (int i = 2; File.Exists(target); i++)
                        target = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(name)} ({i}){Path.GetExtension(name)}");
                    File.Copy(dlg.FileName, target);
                }
                if (ImageCacheService.LoadBitmap(target, 200) == null)
                {
                    if (!already) File.Delete(target);
                    System.Windows.MessageBox.Show(this, "That file couldn't be read as a picture.", "Table Settings",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                choose(Path.GetFileName(target));
                FillPictures();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, $"Couldn't use that picture: {ex.Message}", "Table Settings",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ── Table color ──────────────────────────────────────────────────
        private void FillSwatches()
        {
            _swatches.Children.Clear();
            foreach (var (name, hex) in Colors)
                _swatches.Children.Add(Swatch(name, hex));
            bool custom = !Colors.Any(c => string.Equals(c.Hex, S.TableColor, StringComparison.OrdinalIgnoreCase));
            var other = Btn(custom ? $"Custom: {S.TableColor}…" : "Custom…", () =>
            {
                string? hex = TextPromptDialog.Ask(this, "Table color", "A color as #RRGGBB (for example #2B3A55):", S.TableColor);
                if (string.IsNullOrWhiteSpace(hex)) return;
                hex = hex.Trim();
                if (!hex.StartsWith('#')) hex = "#" + hex;
                try
                {
                    if (ColorConverter.ConvertFromString(hex) is not Color) throw new FormatException();
                }
                catch
                {
                    System.Windows.MessageBox.Show(this, "That isn't a color BoE can read. Try #RRGGBB, like #2B3A55.", "Table Settings",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                S.TableColor = hex.ToUpperInvariant();
                Save();
                FillSwatches();
            });
            other.VerticalAlignment = VerticalAlignment.Center;
            other.Margin = new Thickness(4, 0, 0, 6);
            _swatches.Children.Add(other);
        }

        private FrameworkElement Swatch(string name, string hex)
        {
            bool on = string.Equals(hex, S.TableColor, StringComparison.OrdinalIgnoreCase);
            var b = new Border
            {
                Width = 44, Height = 30, Margin = new Thickness(0, 0, 8, 6), CornerRadius = new CornerRadius(5),
                Background = PlaytestWindow.TableBrush(hex), Cursor = Cursors.Hand, ToolTip = name,
                BorderThickness = new Thickness(on ? 3 : 1),
                BorderBrush = on ? Brushes.Gold : new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
            };
            b.MouseLeftButtonUp += (_, _) => { S.TableColor = hex; Save(); FillSwatches(); };
            return b;
        }

        // ── Bits ─────────────────────────────────────────────────────────
        /// <summary>The tab you were on, for the next time this opens (while BoE runs).</summary>
        private static int _lastTab;

        /// <summary>A tab's page: its settings stacked, scrolled when they don't fit.</summary>
        private static StackPanel TabPage() => new() { Margin = new Thickness(6, 0, 12, 12) };

        private static TabItem Tab(string header, StackPanel page) => new()
        {
            Header = header,
            Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
        };
        private static TextBlock Heading(string text) => new()
        {
            Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 2), Opacity = 0.8,
        };

        private static TextBlock Hint(string text)
        {
            var t = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            t.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            return t;
        }

        private static Button Btn(string text, Action act)
        {
            var b = new Button { Content = text, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 6) };
            b.Click += (_, _) => act();
            return b;
        }

        private CheckBox Check(string text, bool value, Action<bool> set)
        {
            var c = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 6, 0, 0) };
            c.Click += (_, _) => { set(c.IsChecked == true); Save(); };
            return c;
        }

        private void Save()
        {
            PlaytestSettings.Save();
            ShowPreviews();
        }

        private static void OpenFolder(string folder)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            }
            catch
            {
                // No Explorer: nothing to do.
            }
        }
    }
}
