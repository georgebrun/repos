using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace BreakersOfE.ViewModels
{
    /// <summary>
    /// One deck tile in the deck browser. Built by reading the deck file
    /// (no pool lookups), so the browser opens fast.
    /// </summary>
    public class DeckTile : INotifyPropertyChanged
    {
        public string Name { get; init; } = "";
        public string FilePath { get; init; } = "";
        public bool IsCommander { get; init; }
        public string CommanderName { get; init; } = "";
        /// <summary>Subfolder under the Decks folder ("" when at the top).</summary>
        public string Folder { get; init; } = "";
        /// <summary>Color identity as mana symbols, e.g. "{W}{B}{R}" ("{C}" if colorless).</summary>
        public string IdentitySymbols { get; init; } = "";
        public int CardCount { get; init; }
        public decimal TotalValue { get; init; }

        public string CountText => CardCount.ToString("N0");

        public string Summary
        {
            get
            {
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(CommanderName)) parts.Add(CommanderName);
                parts.Add(TotalValue > 0
                    ? TotalValue.ToString("C2", CultureInfo.GetCultureInfo("en-US"))
                    : "—");
                if (!string.IsNullOrEmpty(Folder)) parts.Add($"in {Folder}");
                return string.Join(" · ", parts);
            }
        }

        private bool _isHighlighted;
        public bool IsHighlighted
        {
            get => _isHighlighted;
            set { if (_isHighlighted != value) { _isHighlighted = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>
    /// One virtualized row in the deck browser: a section header
    /// ("Commander  12") or a row of N deck tiles.
    /// </summary>
    public class DeckBrowserRow
    {
        public bool IsHeader { get; init; }
        public bool IsTileRow => !IsHeader;
        public string HeaderText { get; init; } = "";
        public string HeaderCount { get; init; } = "";
        public List<DeckTile> Tiles { get; init; } = new();
    }
}