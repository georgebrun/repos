using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace BreakersOfE.Views.Controls
{
    /// <summary>One choice in a panel list: what's shown, and its value (null = any).</summary>
    public sealed record FilterChoice(string Label, string? Value)
    {
        public override string ToString() => Label;
    }

    /// <summary>Everything set in the Filters panel for one table (kept per table).</summary>
    public sealed class FilterPanelState
    {
        public HashSet<string> Colors { get; set; } = new(StringComparer.Ordinal);
        public HashSet<string> Rarities { get; set; } = new(StringComparer.Ordinal);
        public string? Type { get; set; }
        public string? Format { get; set; }
        public string? Finish { get; set; }
        public string Artist { get; set; } = "";
        public string? Collection { get; set; }
        public string? Set { get; set; }
        public string? Deck { get; set; }
        public string? List { get; set; }
        public string Text { get; set; } = "";
        public string PriceMin { get; set; } = "";
        public string PriceMax { get; set; } = "";
        public string MvMin { get; set; } = "";
        public string MvMax { get; set; } = "";
        public string PowMin { get; set; } = "";
        public string PowMax { get; set; } = "";
        public string TouMin { get; set; } = "";
        public string TouMax { get; set; } = "";
        public string OwnMin { get; set; } = "";
        public string OwnMax { get; set; } = "";

        public FilterPanelState Clone()
        {
            var c = (FilterPanelState)MemberwiseClone();
            c.Colors = new HashSet<string>(Colors, StringComparer.Ordinal);
            c.Rarities = new HashSet<string>(Rarities, StringComparer.Ordinal);
            return c;
        }
    }

    /// <summary>The lists the page fills in for the table on screen.</summary>
    public sealed class FilterPanelOptions
    {
        public List<FilterChoice> Collection { get; init; } = new();
        public List<FilterChoice> Sets { get; init; } = new();
        public List<FilterChoice> Decks { get; init; } = new();
        public List<FilterChoice> Lists { get; init; } = new();
        public List<FilterChoice> Formats { get; init; } = new();
        public string PriceLabel { get; init; } = "Price";
        public string OwnedLabel { get; init; } = "Owned";
        /// <summary>The rows have a color (tokens, planes… don't): color chips on.</summary>
        public bool HasColor { get; init; } = true;
        /// <summary>The rows have format legality: "Legal in" on.</summary>
        public bool HasLegality { get; init; } = true;
    }

    /// <summary>
    /// The Filters panel (above grid and gallery). It only collects choices;
    /// the page turns them into filters (<see cref="Changed"/>). Typing waits
    /// a moment before filtering so 100,000 cards aren't re-filtered per key.
    /// </summary>
    public partial class FilterPanel : UserControl
    {
        /// <summary>Something changed: read <see cref="Read"/> and apply it.</summary>
        public event Action? Changed;
        /// <summary>"Clear panel" was pressed.</summary>
        public event Action? ClearRequested;

        private bool _loading;
        private readonly DispatcherTimer _typing = new() { Interval = TimeSpan.FromMilliseconds(400) };

        private static readonly List<FilterChoice> Types = new()
        {
            new("Any", null), new("Artifact", "Artifact"), new("Battle", "Battle"), new("Creature", "Creature"),
            new("Enchantment", "Enchantment"), new("Instant", "Instant"), new("Land", "Land"),
            new("Planeswalker", "Planeswalker"), new("Sorcery", "Sorcery"), new("Legendary", "Legendary"),
            new("Basic land", "Basic"),
        };

        private static readonly List<FilterChoice> Finishes = new()
        {
            new("Any", null), new("Non-Foil", Models.CardFinish.NonFoil),
            new("Foil", Models.CardFinish.Foil), new("Etched", Models.CardFinish.Etched),
        };

        private IEnumerable<ToggleButton> ColorChips => new[] { ColW, ColU, ColB, ColR, ColG, ColM, ColN };
        private IEnumerable<ToggleButton> RarityChips => new[] { RarC, RarU, RarR, RarM, RarS };
        private IEnumerable<TextBox> TextBoxes => new[]
        {
            ArtistBox, FullTextBox, PriceMin, PriceMax, MvMin, MvMax, PowMin, PowMax, TouMin, TouMax, OwnMin, OwnMax,
        };
        private IEnumerable<TextBox> NumberBoxes => new[]
        {
            PriceMin, PriceMax, MvMin, MvMax, PowMin, PowMax, TouMin, TouMax, OwnMin, OwnMax,
        };
        private IEnumerable<ComboBox> Combos => new[] { TypeBox, FormatBox, FinishBox, CollectionBox, SetBox, DeckBox, ListsBox };

        public FilterPanel()
        {
            InitializeComponent();

            foreach (var chip in ColorChips.Concat(RarityChips)) chip.Click += (_, _) => Raise();
            foreach (var box in TextBoxes) box.TextChanged += (_, _) => { if (!_loading) { _typing.Stop(); _typing.Start(); } };
            foreach (var box in NumberBoxes) box.PreviewTextInput += Num_PreviewTextInput;
            foreach (var combo in Combos)
            {
                combo.DisplayMemberPath = nameof(FilterChoice.Label);
                TextSearch.SetTextPath(combo, nameof(FilterChoice.Label));
                combo.SelectionChanged += (_, _) => Raise();
            }
            _typing.Tick += (_, _) => { _typing.Stop(); Raise(); };

            _loading = true;
            TypeBox.ItemsSource = Types;
            FinishBox.ItemsSource = Finishes;
            TypeBox.SelectedIndex = 0;
            FinishBox.SelectedIndex = 0;
            _loading = false;
        }

        private void Raise()
        {
            if (_loading) return;
            Changed?.Invoke();
        }

        /// <summary>
        /// The page's lists for this table (sets in it, decks, what "Collection"
        /// can mean…). What's chosen now stays chosen when it's still in the list.
        /// </summary>
        public void SetOptions(FilterPanelOptions o)
        {
            _loading = true;
            try
            {
                void Fill(ComboBox box, List<FilterChoice> items)
                {
                    string? keep = Value(box);
                    box.ItemsSource = items;
                    Select(box, keep);
                }
                Fill(CollectionBox, o.Collection);
                Fill(SetBox, o.Sets);
                Fill(DeckBox, o.Decks);
                Fill(ListsBox, o.Lists);
                Fill(FormatBox, o.Formats);
                PriceLabel.Text = o.PriceLabel;
                OwnedLabel.Text = o.OwnedLabel;
                foreach (var c in ColorChips) c.IsEnabled = o.HasColor;
                FormatBox.IsEnabled = o.HasLegality;
            }
            finally
            {
                _loading = false;
            }
        }

        /// <summary>Drop a filter that's still waiting for typing to stop (the table is changing).</summary>
        public void CancelPending() => _typing.Stop();

        /// <summary>Show a table's saved choices (no filtering happens).</summary>
        public void Load(FilterPanelState s)
        {
            _loading = true;
            try
            {
                foreach (var c in ColorChips) c.IsChecked = s.Colors.Contains((string)c.Tag);
                foreach (var c in RarityChips) c.IsChecked = s.Rarities.Contains((string)c.Tag);
                Select(TypeBox, s.Type);
                Select(FormatBox, s.Format);
                Select(FinishBox, s.Finish);
                Select(CollectionBox, s.Collection);
                Select(SetBox, s.Set);
                Select(DeckBox, s.Deck);
                Select(ListsBox, s.List);
                ArtistBox.Text = s.Artist;
                FullTextBox.Text = s.Text;
                PriceMin.Text = s.PriceMin; PriceMax.Text = s.PriceMax;
                MvMin.Text = s.MvMin; MvMax.Text = s.MvMax;
                PowMin.Text = s.PowMin; PowMax.Text = s.PowMax;
                TouMin.Text = s.TouMin; TouMax.Text = s.TouMax;
                OwnMin.Text = s.OwnMin; OwnMax.Text = s.OwnMax;
                _typing.Stop();
            }
            finally
            {
                _loading = false;
            }
        }

        /// <summary>The choices as they are on screen now.</summary>
        public FilterPanelState Read() => new()
        {
            Colors = new HashSet<string>(ColorChips.Where(c => c.IsChecked == true).Select(c => (string)c.Tag), StringComparer.Ordinal),
            Rarities = new HashSet<string>(RarityChips.Where(c => c.IsChecked == true).Select(c => (string)c.Tag), StringComparer.Ordinal),
            Type = Value(TypeBox),
            Format = Value(FormatBox),
            Finish = Value(FinishBox),
            Collection = Value(CollectionBox),
            Set = Value(SetBox),
            Deck = Value(DeckBox),
            List = Value(ListsBox),
            Artist = ArtistBox.Text.Trim(),
            Text = FullTextBox.Text.Trim(),
            PriceMin = PriceMin.Text.Trim(), PriceMax = PriceMax.Text.Trim(),
            MvMin = MvMin.Text.Trim(), MvMax = MvMax.Text.Trim(),
            PowMin = PowMin.Text.Trim(), PowMax = PowMax.Text.Trim(),
            TouMin = TouMin.Text.Trim(), TouMax = TouMax.Text.Trim(),
            OwnMin = OwnMin.Text.Trim(), OwnMax = OwnMax.Text.Trim(),
        };

        /// <summary>"3 panel filters on: Type: Creature · Price 1–5 · …" (or a hint).</summary>
        public void SetActiveText(IReadOnlyList<string> labels)
        {
            ActiveText.Text = labels.Count == 0
                ? "No panel filters. Colors, rarity and set here are the same as the Color, Rarity and Edition column filters."
                : $"{labels.Count} on: {string.Join(" · ", labels)}";
        }

        private static string? Value(ComboBox box) => (box.SelectedItem as FilterChoice)?.Value;

        private static void Select(ComboBox box, string? value)
        {
            if (box.ItemsSource is not IEnumerable<FilterChoice> items) { box.SelectedIndex = -1; return; }
            var list = items.ToList();
            var hit = list.FirstOrDefault(i => i.Value == value) ?? list.FirstOrDefault(i => i.Value == null);
            box.SelectedItem = hit;
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e) => ClearRequested?.Invoke();

        /// <summary>Number boxes: digits and one decimal point.</summary>
        private void Num_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            foreach (char c in e.Text)
                if (!char.IsDigit(c) && c != '.') { e.Handled = true; return; }
        }
    }
}
