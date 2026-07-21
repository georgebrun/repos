using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace BreakersOfE.Windows
{
    // ── Help topic data model ─────────────────────────────────────────────────
    public class HelpTopic
    {
        public string Title { get; init; } = string.Empty;
        public string Subtitle { get; init; } = string.Empty;
        public List<HelpSection> Sections { get; init; } = new();
        public List<HelpTopic> Children { get; init; } = new();
    }

    public class HelpSection
    {
        public string? Heading { get; init; }
        public List<HelpBlock> Blocks { get; init; } = new();
    }

    public record HelpBlock(HelpBlockType Type, string Text,
        string? Extra = null);

    public enum HelpBlockType
    {
        Paragraph, BulletItem, KeyValue, Tip, Warning, Shortcut, Code
    }

    public partial class HelpWindow : Window
    {
        private List<HelpTopic> _allTopics = new();
        private HelpTopic? _selected = null;

        public HelpWindow(string? startTopic = null)
        {
            InitializeComponent();
            _allTopics = BuildTopics();
            PopulateTree(_allTopics, null);

            // Select first topic or requested one
            SelectTopicByTitle(startTopic ?? "Getting Started");
        }

        // ════════════════════════════════════════════════════════════════════
        // TOPIC TREE
        // ════════════════════════════════════════════════════════════════════

        private void PopulateTree(List<HelpTopic> topics,
            TreeViewItem? parent)
        {
            foreach (var topic in topics)
            {
                var item = new TreeViewItem
                {
                    Header = topic.Title,
                    Tag = topic,
                    Padding = new Thickness(2)
                };
                item.Selected += TopicItem_Selected;

                if (topic.Children.Count > 0)
                    PopulateTree(topic.Children, item);

                if (parent == null) TopicTree.Items.Add(item);
                else parent.Items.Add(item);
            }
        }

        private void TopicItem_Selected(object sender, RoutedEventArgs e)
        {
            if (sender is TreeViewItem item && item.Tag is HelpTopic topic)
            {
                _selected = topic;
                RenderTopic(topic);
                e.Handled = true;
            }
        }

        private void TopicTree_SelectionChanged(object sender,
            RoutedPropertyChangedEventArgs<object> e)
        { }

        private void TxtSearch_Changed(object sender, TextChangedEventArgs e)
        {
            string q = TxtSearch.Text.Trim().ToLower();
            FilterTree(TopicTree.Items, q);
        }

        private bool FilterTree(ItemCollection items, string q)
        {
            bool anyVisible = false;
            foreach (TreeViewItem item in items)
            {
                bool titleMatch = string.IsNullOrEmpty(q) ||
                    item.Header.ToString()!.ToLower().Contains(q);
                bool childMatch = item.Items.Count > 0 &&
                    FilterTree(item.Items, q);
                bool visible = titleMatch || childMatch;
                item.Visibility = visible
                    ? Visibility.Visible : Visibility.Collapsed;
                if (visible && !string.IsNullOrEmpty(q))
                    item.IsExpanded = childMatch;
                anyVisible |= visible;
            }
            return anyVisible;
        }

        private void SelectTopicByTitle(string title)
        {
            SelectInItems(TopicTree.Items, title);
        }

        private bool SelectInItems(ItemCollection items, string title)
        {
            foreach (TreeViewItem item in items)
            {
                if (item.Header.ToString() == title)
                {
                    item.IsSelected = true;
                    item.BringIntoView();
                    return true;
                }
                if (SelectInItems(item.Items, title)) return true;
            }
            return false;
        }

        // ════════════════════════════════════════════════════════════════════
        // CONTENT RENDERING
        // ════════════════════════════════════════════════════════════════════

        private void RenderTopic(HelpTopic topic)
        {
            TopicTitle.Text = topic.Title;
            TopicSubtitle.Text = topic.Subtitle;
            ContentPanel.Children.Clear();

            foreach (var section in topic.Sections)
            {
                if (!string.IsNullOrEmpty(section.Heading))
                    ContentPanel.Children.Add(MakeHeading(section.Heading));

                foreach (var block in section.Blocks)
                    ContentPanel.Children.Add(MakeBlock(block));
            }
        }

        private static UIElement MakeHeading(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = SystemColors.HighlightBrush,
                Margin = new Thickness(0, 14, 0, 6)
            };
        }

        private static UIElement MakeBlock(HelpBlock block)
        {
            switch (block.Type)
            {
                case HelpBlockType.Paragraph:
                    return new TextBlock
                    {
                        Text = block.Text,
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 12,
                        Margin = new Thickness(0, 0, 0, 8),
                        LineHeight = 20
                    };

                case HelpBlockType.BulletItem:
                    var bullet = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Margin = new Thickness(8, 2, 0, 2)
                    };
                    bullet.Children.Add(new TextBlock
                    {
                        Text = "•",
                        FontSize = 14,
                        Margin = new Thickness(0, 0, 8, 0),
                        Foreground = SystemColors.HighlightBrush
                    });
                    bullet.Children.Add(new TextBlock
                    {
                        Text = block.Text,
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 580
                    });
                    return bullet;

                case HelpBlockType.KeyValue:
                    var kv = new Grid { Margin = new Thickness(8, 2, 0, 2) };
                    kv.ColumnDefinitions.Add(new ColumnDefinition
                    { Width = new GridLength(160) });
                    kv.ColumnDefinitions.Add(new ColumnDefinition
                    { Width = new GridLength(1, GridUnitType.Star) });
                    var key = new TextBlock
                    {
                        Text = block.Extra ?? "",
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold
                    };
                    var val = new TextBlock
                    {
                        Text = block.Text,
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap
                    };
                    Grid.SetColumn(key, 0);
                    Grid.SetColumn(val, 1);
                    kv.Children.Add(key);
                    kv.Children.Add(val);
                    return kv;

                case HelpBlockType.Shortcut:
                    var sc = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Margin = new Thickness(8, 3, 0, 3)
                    };
                    var badge = new Border
                    {
                        Background = new SolidColorBrush(
                            Color.FromRgb(0x33, 0x33, 0x33)),
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(6, 2, 6, 2),
                        Margin = new Thickness(0, 0, 10, 0)
                    };
                    badge.Child = new TextBlock
                    {
                        Text = block.Extra ?? "",
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 11,
                        Foreground = Brushes.White
                    };
                    sc.Children.Add(badge);
                    sc.Children.Add(new TextBlock
                    {
                        Text = block.Text,
                        FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    return sc;

                case HelpBlockType.Tip:
                    return new Border
                    {
                        Background = new SolidColorBrush(
                            Color.FromArgb(30, 0, 120, 80)),
                        BorderBrush = new SolidColorBrush(
                            Color.FromRgb(0, 120, 80)),
                        BorderThickness = new Thickness(3, 0, 0, 0),
                        Padding = new Thickness(10, 6, 10, 6),
                        Margin = new Thickness(0, 4, 0, 8),
                        Child = new TextBlock
                        {
                            Text = "💡  " + block.Text,
                            FontSize = 12,
                            TextWrapping = TextWrapping.Wrap
                        }
                    };

                case HelpBlockType.Warning:
                    return new Border
                    {
                        Background = new SolidColorBrush(
                            Color.FromArgb(30, 200, 80, 0)),
                        BorderBrush = new SolidColorBrush(
                            Color.FromRgb(200, 80, 0)),
                        BorderThickness = new Thickness(3, 0, 0, 0),
                        Padding = new Thickness(10, 6, 10, 6),
                        Margin = new Thickness(0, 4, 0, 8),
                        Child = new TextBlock
                        {
                            Text = "⚠  " + block.Text,
                            FontSize = 12,
                            TextWrapping = TextWrapping.Wrap
                        }
                    };

                case HelpBlockType.Code:
                    return new Border
                    {
                        Background = new SolidColorBrush(
                            Color.FromRgb(0xF0, 0xF0, 0xF0)),
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(8, 4, 8, 4),
                        Margin = new Thickness(0, 2, 0, 8),
                        Child = new TextBlock
                        {
                            Text = block.Text,
                            FontFamily = new FontFamily("Consolas"),
                            FontSize = 11
                        }
                    };

                default:
                    return new TextBlock { Text = block.Text };
            }
        }

        // Helpers
        static HelpBlock P(string t) => new(HelpBlockType.Paragraph, t);
        static HelpBlock B(string t) => new(HelpBlockType.BulletItem, t);
        static HelpBlock KV(string k, string v) => new(HelpBlockType.KeyValue, v, k);
        static HelpBlock Tip(string t) => new(HelpBlockType.Tip, t);
        static HelpBlock Warn(string t) => new(HelpBlockType.Warning, t);
        static HelpBlock SK(string key, string desc)
            => new(HelpBlockType.Shortcut, desc, key);

        static HelpSection S(string? heading, params HelpBlock[] blocks)
            => new() { Heading = heading, Blocks = blocks.ToList() };

        // ════════════════════════════════════════════════════════════════════
        // HELP CONTENT
        // ════════════════════════════════════════════════════════════════════

        private static List<HelpTopic> BuildTopics() => new()
        {
            // ── GETTING STARTED ───────────────────────────────────────────
            new HelpTopic
            {
                Title    = "Getting Started",
                Subtitle = "First steps with Breakers of E",
                Sections = new()
                {
                    S(null,
                        P("Welcome to Breakers of E — a Magic: The Gathering collection manager, deck builder, and tabletop simulator."),
                        P("On first launch your card pool will be empty. Follow the steps below to get up and running.")),
                    S("Step 1 — Download the Card Database",
                        P("Go to Tools → Update Card Database. Click Download to fetch all cards from Scryfall (~98,000 cards). This only needs to be done once and takes a few minutes."),
                        Tip("After the download completes, run Tools → Download Missing Card Images to download card artwork for your collection.")),
                    S("Step 2 — Add Cards to Your Collection",
                        P("Switch to Pool → Collection mode using the View Mode dropdown. Select a card in the top table and double-click it (or press Enter) to add it to your collection. Hold Shift to add a foil copy."),
                        P("Or import an existing collection from MTG Studio, Moxfield, TCGPlayer, Deckbox, or Dragon Shield via File → Import.")),
                    S("Step 3 — Build a Deck",
                        P("Switch to Pool → Deck or Collection → Deck mode. Create a new deck with File → New Deck (Ctrl+N). Double-click cards in the top table to add them. Right-click a deck tab to set the deck type or mark commanders."),
                        P("To switch a deck between Standard and Commander, right-click any card in the deck table and choose Switch to Commander/Standard.")),
                    S("Step 4 — Play a Game",
                        P("Open the Tabletop Simulator from Tools → Tabletop Simulator. Select your deck in the New Game dialog and start playing. Commander, Standard, and other formats are supported."))
                }
            },

            // ── MAIN WINDOW ───────────────────────────────────────────────
            new HelpTopic
            {
                Title    = "Main Window",
                Subtitle = "Understanding the main interface",
                Sections = new()
                {
                    S(null,
                        P("The main window has four areas: the menu bar at top, the toolbar, the main content area (left panel + two card tables), and the deck tabs at the bottom.")),
                    S("View Modes",
                        P("The View Mode dropdown controls what appears in the two card tables. The top table is always the source; the bottom is the destination."),
                        KV("Pool → Collection",         "Browse all cards and manage your collection"),
                        KV("Pool → Deck",               "Build a deck from the full card pool"),
                        KV("Collection → Deck",         "Build a deck using only cards you own"),
                        KV("Deck → Collection",         "Move cards from a deck into your collection"),
                        KV("Pool → Want List",          "Mark cards you want to acquire"),
                        KV("Collection → Trade Binder", "Mark owned cards as available to trade"),
                        KV("Pool → Tokens",             "Browse token cards"),
                        KV("Pool → Planechase",         "Browse Plane cards"),
                        KV("Pool → Archenemy",          "Browse Scheme cards"),
                        KV("Pool → Vanguard",           "Browse Vanguard cards"),
                        KV("Pool → Conspiracy",         "Browse Conspiracy cards"),
                        KV("Pool → Art Series",         "Browse Art Series cards")),
                    S("Left Panel — Card Details",
                        P("Selecting any card shows its full details in the left panel: name, mana cost, type, set, collector number, rarity, P/T, oracle text, flavor text, artist, prices, format legality, and card image."),
                        P("For double-faced cards a 🔄 Show Back Face button appears below the image. Click it to flip to the back face.")),
                    S("Deck Tabs",
                        P("Open decks appear as tabs at the bottom. Click a tab to make that deck active. Right-click a tab for options including switching deck type and marking commanders."))
                }
            },

            // ── TOOLBAR ───────────────────────────────────────────────────
            new HelpTopic
            {
                Title    = "Toolbar Reference",
                Subtitle = "Every toolbar button explained",
                Sections = new()
                {
                    S("Group 1 — Deck File",
                        KV("New Deck",        "Create a new empty deck"),
                        KV("Open Deck",       "Open an existing .deck file"),
                        KV("Save Deck",       "Save the active deck"),
                        KV("Save All Decks",  "Save all open decks"),
                        KV("Close Deck",      "Close the active deck"),
                        KV("Close All Decks", "Close all open decks"),
                        KV("Deck Properties", "Edit deck name, type, description, and author"),
                        KV("Deck Legality",   "Check format legality for the active deck"),
                        KV("Deck Statistics", "Full deck analysis across 11 tabs")),
                    S("Group 2 — Deck Cards",
                        KV("Add 1 to Deck",      "Add one copy of the selected card"),
                        KV("Add 4 to Deck",      "Add four copies (Standard playset)"),
                        KV("Add 1 Foil to Deck", "Add one foil copy"),
                        KV("Remove from Deck",   "Remove one copy from the deck"),
                        KV("+ / − Qty",          "Increase or decrease deck quantity")),
                    S("Group 3 — Collection",
                        KV("Add to Collection",          "Add the selected card to your collection"),
                        KV("Add Foil to Collection",     "Add a foil copy to your collection"),
                        KV("Import Deck to Collection",  "Add all cards in the active deck to your collection"),
                        KV("Remove from Collection",     "Remove the selected row from your collection")),
                    S("Group 4 — Filter & Search",
                        KV("Filter",          "Open the quick column filter panel"),
                        KV("Advanced Filter", "Open the expression-builder filter window"),
                        KV("Search",          "Search for a card by name"),
                        KV("Find Next/Prev",  "Jump to the next or previous search match"),
                        KV("Clear Search",    "Clear search and return to full list"),
                        KV("Toggle Legality", "Show or hide the format legality columns"),
                        KV("Column Chooser",  "Choose which columns are visible in the tables"),
                        KV("Keyword Search",  "Open the Keyword Search window")),
                    S("Group 5 — View",
                        KV("Update Database", "Download the latest card data from Scryfall"),
                        KV("Toggle Theme",    "Switch between light and dark mode"))
                }
            },

            // ── KEYBOARD SHORTCUTS ────────────────────────────────────────
            new HelpTopic
            {
                Title    = "Keyboard Shortcuts",
                Subtitle = "Work faster with the keyboard",
                Sections = new()
                {
                    S("Main Window",
                        SK("Enter",         "Add selected top-table card to deck or collection"),
                        SK("Shift+Enter",   "Add as foil"),
                        SK("Delete",        "Remove selected bottom-table card from deck or collection"),
                        SK("Ctrl+N",        "New Deck"),
                        SK("Ctrl+O",        "Open Deck"),
                        SK("Ctrl+S",        "Save active deck"),
                        SK("Ctrl+F",        "Focus the search box"),
                        SK("F3",            "Find next search match"),
                        SK("Shift+F3",      "Find previous search match"),
                        SK("Escape",        "Clear search / close popup"),
                        SK("F1",            "Open this Help window")),
                    S("Trade Binder",
                        SK("← / →",       "Navigate to previous/next page"),
                        SK("Page Up/Down", "Navigate pages"),
                        SK("Home",         "Go to first page")),
                    S("Filter Window",
                        SK("Enter",  "Apply filter and close"),
                        SK("Escape", "Cancel and close")),
                    S("Tabletop Simulator",
                        SK("← / →",       "Navigate hand cards"),
                        SK("Page Up/Down", "Navigate zone browser pages"))
                }
            },

            // ── COLLECTION ────────────────────────────────────────────────
            new HelpTopic
            {
                Title    = "Collection",
                Subtitle = "Managing your card collection",
                Children = new()
                {
                    new HelpTopic
                    {
                        Title    = "Adding Cards",
                        Subtitle = "How to add cards to your collection",
                        Sections = new()
                        {
                            S(null,
                                P("Switch to Pool → Collection view mode. The top table shows the full card pool; the bottom shows your collection."),
                                P("To add a card: select it in the top table and double-click, press Enter, or click the + Add to Collection toolbar button."),
                                P("To add a foil copy: hold Shift while double-clicking, press Shift+Enter, or click the ✨ Add Foil button.")),
                            S("Editing Collection Entries",
                                P("Click any cell in the bottom table to edit it directly. You can change Quantity, Foil Qty, Condition, Language, Storage Location, Buy At, Sell At, Notes, and Group.")),
                            S("Import from Other Apps",
                                P("Use File → Import to bring in collections from other apps. Supported formats:"),
                                B("MTG Studio CSV — best option, uses ScryfallId for exact matching"),
                                B("Moxfield CSV — quantities, conditions, foil status"),
                                B("TCGPlayer CSV — quantities and conditions"),
                                B("Deckbox CSV — quantities, foil, language"),
                                B("Dragon Shield CSV — quantities, finish, conditions"),
                                Tip("Always run Update Card Database before importing. Cards are matched against your pool by name and set code."))
                        }
                    },
                    new HelpTopic
                    {
                        Title    = "Conditions & Language",
                        Subtitle = "Tracking card condition and language",
                        Sections = new()
                        {
                            S(null,
                                P("Each collection entry tracks Condition and Language. These are set per-row in the collection table."),
                                KV("Near Mint (NM)",         "Perfect or near-perfect condition"),
                                KV("Lightly Played (LP)",    "Minor wear, fully playable"),
                                KV("Moderately Played (MP)", "Noticeable wear but tournament-legal"),
                                KV("Heavily Played (HP)",    "Significant wear"),
                                KV("Damaged (D)",            "Severely worn, marked, or bent"),
                                KV("Unknown",                "Condition not assessed")),
                            S("Effect on Value",
                                P("Condition affects the computed market value. Near Mint = 100% of market; Damaged ≈ 25%."))
                        }
                    },
                    new HelpTopic
                    {
                        Title    = "Pricing",
                        Subtitle = "Working with card prices",
                        Sections = new()
                        {
                            S(null,
                                P("Prices come from Scryfall and are stored when you run Update Card Database or Update Prices Only."),
                                KV("Foil Price",    "Scryfall USD foil price"),
                                KV("Buy At",        "Your personal buy price (what you'd pay)"),
                                KV("Sell At",       "Your personal sell price (what you'd charge)"),
                                KV("Price High",    "Highest recent price point"),
                                KV("Sell At Value", "Calculated: Sell At × Quantity")),
                            S("Updating Prices",
                                P("Go to Tools → Update Prices Only to refresh market prices without re-downloading all card data."),
                                P("When prices are updated, they automatically propagate to your collection, Trade Binder, and Want List entries."),
                                Tip("Your personal Buy At and Sell At values are never overwritten by price updates.")),
                            S("Updating Deck Prices",
                                P("Go to Tools → Update Deck Prices to refresh prices in all currently open decks."),
                                P("A prompt asks whether to update pool prices from Scryfall first:"),
                                KV("Yes",    "Downloads latest prices, updates pool + collection + binder + want list, then updates open decks"),
                                KV("No",     "Updates open decks from current pool prices only (faster)"),
                                KV("Cancel", "Does nothing"),
                                Tip("Deck prices are stored in each deck file. Only open decks are updated — closed decks retain their last-saved prices until opened and updated."))
                        }
                    }
                }
            },

            // ── DECK BUILDER ─────────────────────────────────────────────
            new HelpTopic
            {
                Title    = "Deck Builder",
                Subtitle = "Creating and managing decks",
                Children = new()
                {
                    new HelpTopic
                    {
                        Title    = "Creating a Deck",
                        Subtitle = "Start a new deck from scratch",
                        Sections = new()
                        {
                            S(null,
                                P("Go to File → New Deck (Ctrl+N). Enter a name, choose Standard or Commander, and optionally set the author."),
                                P("Switch to Pool → Deck or Collection → Deck mode. Double-click cards in the top table to add them.")),
                            S("Deck Types",
                                KV("Standard",  "60-card minimum, up to 4 copies of any non-basic card"),
                                KV("Commander", "100-card singleton with a commander. Starting life is 40.")),
                            S("Switching Deck Type",
                                P("Right-click any card in the deck table → Switch to Commander Deck (or Standard). The app validates card counts and warns about any violations."),
                                Tip("You can switch deck type at any time — even mid-session. The deck auto-saves after switching.")),
                            S("Adding Cards",
                                B("Double-click a pool card to add 1 copy"),
                                B("Shift+double-click to add a foil copy"),
                                B("Press Enter to add the selected card"),
                                B("Click Add 4 to Deck for a full playset"),
                                Tip("In Collection → Deck mode, if you only have foil copies, the app automatically marks them as foil.")),
                            S("Setting Commanders",
                                P("Right-click a card in the deck table → Set as Commander. Commander cards appear in a separate zone in the Tabletop Simulator. Only valid in Commander decks."))
                        }
                    },
                    new HelpTopic
                    {
                        Title    = "Deck Statistics",
                        Subtitle = "Analyzing your deck",
                        Sections = new()
                        {
                            S(null,
                                P("Click the Deck Statistics toolbar button or use the Deck menu. Eleven analysis tabs:"),
                                KV("Overview",      "Total cards, lands, creatures, spells, avg CMC, color breakdown"),
                                KV("Mana Curve",    "Bar chart of cards by mana cost"),
                                KV("Color Pie",     "Visual color distribution"),
                                KV("Card Types",    "Creatures, instants, sorceries, enchantments, etc."),
                                KV("Lands",         "Land count, basic vs non-basic, mana production"),
                                KV("Creatures",     "P/T distribution, keyword breakdown"),
                                KV("Rarity",        "Common / Uncommon / Rare / Mythic counts"),
                                KV("Value",         "Estimated total value of the deck"),
                                KV("Legality",      "Format legality check against all banned/restricted lists"),
                                KV("Suggestions",   "Mana curve suggestions and improvement tips"),
                                KV("Card Usage",    "Which collection cards are used across all your decks"))
                        }
                    },
                    new HelpTopic
                    {
                        Title    = "Import & Export Decks",
                        Subtitle = "Moving decks in and out",
                        Sections = new()
                        {
                            S("Importing Decks",
                                P("Go to File → Import. Select Deck as the target. All CSV formats are supported as well as MTG Studio .deck XML."),
                                KV("MTG Studio .deck XML", "Most complete — includes sideboard"),
                                KV("Moxfield CSV",         "Card list with foil status"),
                                KV("TCGPlayer CSV",        "Card list with quantities"),
                                KV("Deckbox CSV",          "Card list with foil and language"),
                                KV("Dragon Shield CSV",    "Card list with finish"),
                                Warn("MTG Studio .deck XML does not carry foil or commander data. Mark foil cards and commanders manually after import, or use Breakers of E JSON for full round-trips.")),
                            S("Exporting Decks",
                                P("Go to File → Export. Select Deck, browse for the source .deck file (or leave blank to use the active deck), choose your format, and save."),
                                Tip("Use Breakers of E JSON export for a full-fidelity backup that preserves foil quantities, commanders, sideboard, and all metadata."))
                        }
                    }
                }
            },

            // ── FILTERING ─────────────────────────────────────────────────
            new HelpTopic
            {
                Title    = "Filtering Cards",
                Subtitle = "Finding the cards you need",
                Children = new()
                {
                    new HelpTopic
                    {
                        Title    = "Quick Search",
                        Subtitle = "Fast name-based search",
                        Sections = new()
                        {
                            S(null,
                                P("The search box in the toolbar (or Ctrl+F) searches the current table by card name as you type."),
                                SK("F3",         "Jump to next match"),
                                SK("Shift+F3",   "Jump to previous match"),
                                SK("Escape",     "Clear search and show all cards"),
                                Tip("The search also matches set codes — type 'MH3' to see all Modern Horizons 3 cards."))
                        }
                    },
                    new HelpTopic
                    {
                        Title    = "Column Filters",
                        Subtitle = "Excel-style sort and filter on any column",
                        Sections = new()
                        {
                            S(null,
                                P("Hover over any column header to reveal a funnel icon. Click it to open the column filter popup.")),
                            S("Sort",
                                B("Sort A → Z and Sort Z → A buttons at the top sort the entire table by that column immediately.")),
                            S("Values Tab",
                                B("Shows every unique value in the column with a checkbox next to each."),
                                B("When other column filters are active, only values that exist in the already-filtered data are shown (cascading)."),
                                B("Use the search box to narrow the list — non-matching values are hidden."),
                                B("(Select All) when unchecked clears ALL values (visible and hidden) so you can start fresh."),
                                B("(Select All) when checked selects only the visible search results."),
                                B("The (Select All) checkbox shows ■ (indeterminate) when some but not all values are selected.")),
                            S("Text Filters Tab",
                                B("Apply text-based conditions: Contains, Begins With, Ends With, Equals, Does Not Contain, Is Blank, Is Not Blank, and numeric comparisons.")),
                            S("Buttons",
                                KV("OK",           "Commits the current selection and closes the popup"),
                                KV("Cancel",       "Reverts to the state before the popup was opened"),
                                KV("Clear Filter", "Removes this column's filter and closes")),
                            S("Combining Filters",
                                P("Multiple column filters combine with AND logic — a card must match all active column filters to appear."),
                                P("Column filters work alongside the Advanced Filter and Quick Search simultaneously."),
                                Tip("Active column filters show a blue funnel icon that stays visible on the column header."))
                        }
                    },
                    new HelpTopic
                    {
                        Title    = "Advanced Filter",
                        Subtitle = "Multi-condition expression filter",
                        Sections = new()
                        {
                            S(null,
                                P("Click the Advanced Filter button to open the full filter window. Three tabs:"),
                                KV("Blocks/Formats/Editions", "Filter by set or block"),
                                KV("Cards",   "Build complex conditions with AND/OR logic"),
                                KV("Options", "Color identity filter, price filter, search options")),
                            S("Building Conditions",
                                P("Each condition row has a Field, Operator, and Value. Rows are joined with AND by default — click AND to toggle to OR."),
                                Tip("To find Flying blue creatures: Type Contains 'Creature' AND Color Contains 'U' AND Oracle Text Contains 'flying'.")),
                            S("Color Identity Filter (Options Tab)",
                                KV("At Most (Commander)", "Cards whose color identity fits within selected colors — ideal for Commander deck building"),
                                KV("Includes",            "Cards that contain all selected colors"),
                                KV("Exactly",             "Cards that are exactly the selected colors, no more"))
                        }
                    },
                    new HelpTopic
                    {
                        Title    = "Keyword Search",
                        Subtitle = "Search by card abilities",
                        Sections = new()
                        {
                            S(null,
                                P("Open Keyword Search from Tools → Keyword Search or the toolbar button. Find cards by their rules keywords."),
                                P("Keywords are grouped by category: Evasion, Combat, Protection, Triggered, Activated, Static, Spell, Cost/Payment, Replacement, Format-Specific, and Other."),
                                P("Click a card in the results to see its image and full details in the right panel.")),
                            S("Combining Filters",
                                B("Check multiple keywords — use AND to require all, OR to require any"),
                                B("Add color identity and legality filters to narrow results"),
                                B("Switch between Card Pool and Collection as the source")),
                            S("Action Buttons",
                                KV("+ Add to Deck",        "Add the selected card to your active deck"),
                                KV("+ Add to Collection",  "Add the selected card to your collection"),
                                KV("♡ Add to Want List",   "Add the selected card to your want list"),
                                KV("📖 Keyword Dictionary", "Open the full keyword glossary"))
                        }
                    }
                }
            },

            // ── TRADE BINDER ─────────────────────────────────────────────
            new HelpTopic
            {
                Title    = "Trade Binder & Want List",
                Subtitle = "Managing trades",
                Sections = new()
                {
                    S(null,
                        P("The Trade Binder tracks cards you want to trade away (Have List) and cards you want to acquire (Want List)."),
                        P("Open the visual binder from Tools → Trade Binder. It shows your cards in a 3×3 pocket page layout — like a real binder.")),
                    S("Have List (Trade Binder)",
                        P("Switch to Collection → Trade Binder mode. Select a card from your collection and add it with +. These are cards you own and want to trade."),
                        P("Each entry tracks: Quantity, Foil/Non-Foil, Condition (auto-pulled from your collection), and Asking Price (defaults to market value)."),
                        Warn("A warning appears if you try to add a card that is currently used in a deck.")),
                    S("Want List",
                        P("Switch to Pool → Want List mode. Add any card from the pool to your want list."),
                        P("Each entry tracks: Quantity, Foil/Non-Foil, and Offer Price.")),
                    S("Visual Binder",
                        P("Cards are shown in a 3×3 pocket grid per page. Foil cards have a rainbow triangle badge in the top-right corner. Quantity is shown as ×N in the bottom-left corner."),
                        B("Click a card to select it"),
                        B("Double-click to see the enlarged card view with full details"),
                        B("Right-click to remove a card from the binder"),
                        B("Use ◀ ▶ buttons or arrow keys to navigate pages"),
                        Tip("Click the Have / Want tab at the top to switch between your Trade Binder and Want List."))
                }
            },

            // ── IMPORT / EXPORT ───────────────────────────────────────────
            new HelpTopic
            {
                Title    = "Import & Export",
                Subtitle = "Moving data in and out of Breakers of E",
                Sections = new()
                {
                    S(null,
                        P("Open the Import/Export window from File → Import or File → Export.")),
                    S("Importing Collections",
                        P("On the Import tab: choose Collection, Deck, or Want List as the target. Select the format (Auto-detect is recommended). Choose Merge or Replace for duplicate handling, then browse for your file."),
                        KV("Merge",   "Keep whichever quantity is higher — safe to re-import without doubling"),
                        KV("Replace", "Overwrite existing quantities exactly"),
                        Tip("Import auto-detects the format from the file header in most cases. Manually select the format only if auto-detect fails.")),
                    S("Supported Import Formats",
                        KV("MTG Studio CSV",    "Best option — uses ScryfallId for exact matching, preserves prices and notes"),
                        KV("MTG Studio .deck",  "Deck XML with sideboard. Does not carry foil or commander data."),
                        KV("Moxfield CSV",      "Quantities, conditions, foil status, purchase price"),
                        KV("TCGPlayer CSV",     "Quantities and conditions"),
                        KV("Deckbox CSV",       "Quantities, foil, language"),
                        KV("Dragon Shield CSV", "Quantities, finish (Foil/Normal/Etched), conditions")),
                    S("Exporting",
                        P("On the Export tab: choose what to export (Collection, Deck, Want List, or Trade Binder), select the format, and browse for a save location."),
                        KV("MTG Studio CSV",     "Full column set including ScryfallId — best for re-import"),
                        KV("MTG Studio .deck",   "XML deck format — deck export only"),
                        KV("Moxfield CSV",       "Correct format for Moxfield import"),
                        KV("TCGPlayer CSV",      "For TCGPlayer buylist or collection sync"),
                        KV("Deckbox CSV",        "Deckbox collection format"),
                        KV("Dragon Shield CSV",  "Dragon Shield app format"),
                        KV("Breakers of E JSON", "Native format — full backup including all personal data")),
                    S("Log & Troubleshooting",
                        P("The log panel shows the result of every import or export. Cards that couldn't be matched are listed as warnings with their name and set code."),
                        B("📋 Copy Log — copies the full log to your clipboard"),
                        B("💾 Save Log — saves the log to a timestamped .txt file in your Exports folder"),
                        Tip("If many cards are not found, make sure you have run Tools → Update Card Database first. Cards are matched by name and set code."))
                }
            },

            // ── KEYWORD DICTIONARY ────────────────────────────────────────
            new HelpTopic
            {
                Title    = "Keyword Dictionary",
                Subtitle = "MTG keyword rules reference",
                Sections = new()
                {
                    S(null,
                        P("Open from Help → Keyword Dictionary or click 📖 in the Keyword Search window."),
                        P("~200 MTG keyword abilities with full rules definitions, organized by category."),
                        KV("Evasion",         "Flying, Menace, Shadow, Fear, Skulk, Intimidate, Landwalk, Horsemanship"),
                        KV("Combat",          "First Strike, Double Strike, Trample, Vigilance, Haste, Deathtouch, Lifelink, Reach, Infect, Wither, Battle Cry, Exalted"),
                        KV("Protection",      "Hexproof, Shroud, Indestructible, Ward, Protection from, Phasing"),
                        KV("Triggered",       "Cascade, Landfall, Prowess, Magecraft, Morbid, Revolt, Enrage, Constellation, Mentor"),
                        KV("Activated",       "Equip, Cycling, Morph, Unearth, Crew, Level Up, Monstrosity"),
                        KV("Static",          "Flash, Defender, Convoke, Delve, Storm, Bestow, Overload, Cipher"),
                        KV("Spell",           "Flashback, Jump-start, Retrace, Rebound, Buyback, Kicker, Fuse"),
                        KV("Cost/Payment",    "Casualty, Evoke, Madness, Escape, Foretell, Blitz, Dash, Mutate, Cleave"),
                        KV("Replacement",     "Undying, Persist, Regenerate, Dredge, Embalm, Eternalize, Scavenge"),
                        KV("Format-Specific", "Partner, Eminence, Monarch, The Initiative, Myriad, Background"),
                        KV("Other",           "Transform, Meld, Saga, Class, Decayed, Toxic, The Ring, Saddle, Gift, Offspring")),
                    S(null,
                        P("Type in the search box to filter keywords by name or definition. Click any row to see the full definition at the bottom."))
                }
            },

            // ── TABLETOP SIMULATOR ────────────────────────────────────────
            new HelpTopic
            {
                Title    = "Tabletop Simulator",
                Subtitle = "Playing Magic: The Gathering",
                Children = new()
                {
                    new HelpTopic
                    {
                        Title    = "Starting a Game",
                        Subtitle = "Setting up a new game",
                        Sections = new()
                        {
                            S(null,
                                P("Open from Tools → Tabletop Simulator. The New Game Setup dialog appears."),
                                P("Select decks for both players (leave opponent's blank for goldfish testing). Starting life is auto-detected — 40 for Commander, 20 for Standard."),
                                P("The game opens with a Mulligan phase using the London Mulligan rule.")),
                            S("Board Layout",
                                KV("Top half",    "Opponent: library, hand, battlefield, exile, graveyard"),
                                KV("Center rail", "Phase indicator, Stack, modern mechanics, Pass Turn button"),
                                KV("Bottom half", "Your zones: library, hand, battlefield, exile, graveyard"),
                                KV("Left panel",  "Life totals, poison, commander damage, energy, mana pool"))
                        }
                    },
                    new HelpTopic
                    {
                        Title    = "Game Actions",
                        Subtitle = "What you can do during a game",
                        Sections = new()
                        {
                            S("Cards in Hand",
                                B("Right-click → Play to Battlefield / as Land / Face-Down / Reveal / Discard / Return to Library"),
                                B("Double-click to view enlarged")),
                            S("Battlefield Cards",
                                B("Click to tap/untap"),
                                B("Right-click → Move to (any zone), Add Counter, Add Temp Effect, Transform, View Card"),
                                B("Drag to reposition on the battlefield")),
                            S("Commander Zone",
                                B("Your commander starts in the Command Zone"),
                                B("Right-click the commander to Cast, Return to Command Zone, or move to another zone"),
                                B("Commander tax is tracked automatically"),
                                B("The commander card shows a ⭐ badge for easy identification")),
                            S("Drawing Cards",
                                B("Click Draw for one card"),
                                B("Right-click Draw for 2, 3, 4, 5, 7, or custom count")),
                            S("Life & Counters",
                                B("Click +/− to adjust life or poison by 1"),
                                B("Double-click a life total to type an exact value"),
                                B("Commander damage tracked bidirectionally")),
                            S("Special Mechanics",
                                B("Monarch 👑 — click to claim or give"),
                                B("Initiative ⚡ — click to take or give"),
                                B("Energy — add/remove counters for either player"),
                                B("The Ring 💍 — advance through levels 1–4"))
                        }
                    },
                    new HelpTopic
                    {
                        Title    = "Library Actions",
                        Subtitle = "Searching and manipulating your library",
                        Sections = new()
                        {
                            S(null,
                                P("Right-click your library stack to access:"),
                                KV("Search Library",  "Find a card and send to hand, battlefield, or library top/bottom"),
                                KV("Surveil N",       "Look at top N cards, send each to graveyard or keep on top"),
                                KV("Look at Top N",   "View top N cards without moving them"),
                                KV("Reveal Top Card", "Flip the top card face-up"),
                                KV("Shuffle",         "Cryptographic shuffle"),
                                KV("Draw X",          "Draw multiple cards at once"))
                        }
                    },
                    new HelpTopic
                    {
                        Title    = "Tabletop Settings",
                        Subtitle = "Customizing the experience",
                        Sections = new()
                        {
                            S(null,
                                P("Click ⚙ in the tabletop toolbar to open Tabletop Settings."),
                                KV("Default Starting Life",  "Override auto-detected life total"),
                                KV("Auto-draw on Pass Turn", "Draw automatically when passing turn"),
                                KV("Table Color",            "Choose from 6 felt colors"),
                                KV("Blur Opponent Hand",     "Obscure opponent's hand cards"),
                                KV("Card Sleeve Image",      "Upload a custom sleeve image"),
                                KV("Your Playmat",           "Upload a custom playmat for your side"),
                                KV("Opponent Playmat",       "Upload a custom playmat for the opponent"))
                        }
                    },
                    new HelpTopic
                    {
                        Title    = "Save & Restore",
                        Subtitle = "Saving game state",
                        Sections = new()
                        {
                            S(null,
                                P("The tabletop auto-saves when you close the window. On next open, a Restore prompt appears."),
                                P("State saved includes: life totals, poison, commander damage, library order, hand, graveyard, exile, battlefield positions, tap states, counters, transform states, temp effects, Monarch, Initiative, energy, and The Ring."),
                                Warn("Game state is tied to the deck files used to start the game. If deck files are deleted, restore may fail."))
                        }
                    }
                }
            },

            // ── PREFERENCES ───────────────────────────────────────────────
            new HelpTopic
            {
                Title    = "Preferences",
                Subtitle = "Configuring Breakers of E",
                Sections = new()
                {
                    S(null,
                        P("Open from File → Preferences. All settings save immediately when you click Save.")),
                    S("Identity",
                        KV("Your Name", "Used as the author name in new decks")),
                    S("Startup",
                        KV("Default View", "Which view mode opens at startup"),
                        KV("Theme",        "Light or Dark mode")),
                    S("Cards",
                        KV("Default Condition", "Condition assigned when adding cards to your collection"),
                        KV("Default Language",  "Language assigned when adding cards")),
                    S("Pricing",
                        KV("Currency Display", "Show prices in USD, EUR, or TIX")),
                    S("Tabletop",
                        KV("Default Starting Life", "Override auto-detected starting life for new games")),
                    S("File Locations",
                        KV("Card Images Folder",  "Where card images are cached (default: Documents\\Breakers of E\\CardImages)"),
                        KV("Decks Folder",         "Where deck files are saved (default: Documents\\Breakers of E\\Decks)"),
                        KV("Open App Data Folder", "Opens the Breakers of E data folder in Explorer"))
                }
            },

            // ── TROUBLESHOOTING ───────────────────────────────────────────
            new HelpTopic
            {
                Title    = "Troubleshooting",
                Subtitle = "Common issues and solutions",
                Sections = new()
                {
                    S("Card Pool is Empty",
                        B("Go to Tools → Update Card Database"),
                        B("Click Download and wait (~98,000 cards)"),
                        B("This only needs to be done once")),
                    S("Card Images Not Showing",
                        B("Go to Tools → Download Missing Card Images"),
                        B("Images are cached in Documents\\Breakers of E\\CardImages"),
                        Tip("Cards with unreleased or withheld art show Scryfall's purple 'Image coming right up!' placeholder — this is normal.")),
                    S("Import Not Finding Cards",
                        B("Run Update Card Database first — cards must be in your pool to be matched"),
                        B("MTG Studio CSV is the most reliable format — it uses ScryfallId for exact matching"),
                        B("Other formats match by Name + Set Code; set codes must match Scryfall's codes exactly"),
                        B("Use 📋 Copy Log or 💾 Save Log after import to review all unmatched cards")),
                    S("Collection Data Migration",
                        P("If you upgraded from a version before v1.1.0, your collection entries need card data embedded. The app detects this automatically on first launch and prompts you to migrate."),
                        P("You can also run this manually from Tools → Migrate Collection Card Data."),
                        B("Creates a brand-new collection.db with all card data stored directly in each entry"),
                        B("Your original collection is backed up as collection_pre_v2.db and never modified"),
                        B("The migration uses ScryfallId to match collection entries to the current card pool"),
                        Tip("After migration, your collection no longer depends on pool ID matching. Rebuilding the card pool will never break your collection display.")),
                    S("Deck Type Issues",
                        B("To switch between Standard and Commander: right-click any deck card → Switch to Commander/Standard Deck"),
                        B("To mark a commander: right-click the card → Set as Commander"),
                        B("Commander decks show a ⭐ badge on the commander card in the Tabletop Simulator")),
                    S("App Data Location",
                        P("All Breakers of E data is stored under:"),
                        new HelpBlock(HelpBlockType.Code, "My Documents\\Breakers of E\\"),
                        KV("CardImages\\",        "Card images cached from Scryfall"),
                        KV("Decks\\",             "Your saved .deck files"),
                        KV("Exports\\",           "Files exported from the app (including import logs)"),
                        KV("Imports\\",           "Import staging area"),
                        KV("Filters\\",           "Saved filter presets"),
                        KV("Tabletop\\",          "Sleeve and playmat images"),
                        KV("Collection\\",        "collection.db — your collection, binder, want list"),
                        KV("breakersofe.db",      "Card pool database (~98,000 cards from Scryfall)"))
                }
            }
        };
    }
}