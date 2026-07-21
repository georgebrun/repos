using BreakersOfE.Data;
using BreakersOfE.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace BreakersOfE.Windows
{
    public partial class DeckImportPreviewWindow : Window
    {
        // ── Preview row model ─────────────────────────────────────────────────
        public class PreviewRow : INotifyPropertyChanged
        {
            private bool _include = true;
            public bool Include
            {
                get => _include;
                set { _include = value; OnPropertyChanged(); }
            }

            public string Name { get; set; } = string.Empty;
            public string SetCode { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
            public int NonFoilQty { get; set; }
            public int FoilQty { get; set; }
            public string InCollection { get; set; } = "No";
            public int CollNonFoil { get; set; }
            public int CollFoil { get; set; }

            // Reference back to source DeckCard
            public DeckCard Source { get; set; } = null!;

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged(
                [CallerMemberName] string? name = null)
                => PropertyChanged?.Invoke(this,
                    new PropertyChangedEventArgs(name));
        }

        private readonly Deck _deck;
        private readonly ObservableCollection<PreviewRow> _rows = new();
        private bool _busy = false;

        // Result — cards the user chose to import
        public List<DeckCard> SelectedCards { get; private set; } = new();

        public DeckImportPreviewWindow(Deck deck)
        {
            InitializeComponent();
            _deck = deck;
            LblDeckName.Text = $"Deck: {deck.Name}  ({deck.DeckType})";
            LoadRows();
            PreviewGrid.ItemsSource = _rows;
            UpdateSummary();

            // Update summary when any checkbox changes
            foreach (var r in _rows)
                r.PropertyChanged += (_, _) => UpdateSummary();
        }

        private void LoadRows()
        {
            using var db = new CollectionDbContext();

            // Build a lookup of current collection quantities by ScryfallId
            var collLookup = db.CollectionEntries
                .Where(c => c.Quantity > 0 || c.FoilQuantity > 0)
                .GroupBy(c => c.ScryfallId)
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var card in _deck.Cards
                .OrderBy(c => c.IsCommander ? 0 : 1)
                .ThenBy(c => c.Category)
                .ThenBy(c => c.Name))
            {
                if (string.IsNullOrEmpty(card.ScryfallId) && card.PoolId <= 0) continue;

                CollectionEntry? cEntry = null;
                if (!string.IsNullOrEmpty(card.ScryfallId))
                    collLookup.TryGetValue(card.ScryfallId, out cEntry);

                _rows.Add(new PreviewRow
                {
                    Source = card,
                    Name = card.Name,
                    SetCode = card.SetCode,
                    Category = card.CategoryDisplay,
                    NonFoilQty = card.Quantity,
                    FoilQty = card.FoilQuantity,
                    InCollection = cEntry != null ? "Yes" : "No",
                    CollNonFoil = cEntry?.Quantity ?? 0,
                    CollFoil = cEntry?.FoilQuantity ?? 0
                });
            }
        }

        private void UpdateSummary()
        {
            int selected = _rows.Count(r => r.Include);
            int totalNF = _rows.Where(r => r.Include).Sum(r => r.NonFoilQty);
            int totalFoil = _rows.Where(r => r.Include).Sum(r => r.FoilQty);
            int newRows = _rows.Count(r => r.Include && r.InCollection == "No");
            int mergeRows = _rows.Count(r => r.Include && r.InCollection == "Yes");

            LblSummary.Text =
                $"Selected: {selected} card types   " +
                $"Non-Foil: {totalNF}   Foil: {totalFoil}   " +
                $"New rows: {newRows}   Merging: {mergeRows}";
        }

        private void ChkSelectAll_Changed(object sender,
            RoutedEventArgs e)
        {
            if (_busy) return;
            _busy = true;
            bool check = ChkSelectAll.IsChecked == true;
            foreach (var r in _rows)
                r.Include = check;
            _busy = false;
            UpdateSummary();
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            SelectedCards = _rows
                .Where(r => r.Include)
                .Select(r => r.Source)
                .ToList();

            if (SelectedCards.Count == 0)
            {
                MessageBox.Show(
                    "No cards selected. Please check at least one card to import.",
                    "Nothing Selected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;
    }
}