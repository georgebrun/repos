using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using BreakersOfE.Services;
using Wpf.Ui.Controls;

namespace BreakersOfE.Views.Dialogs
{
    /// <summary>
    /// Got It for one printing + finish: copies received, and a checkbox per
    /// want (decks first, then the Collection). The copies go to the ticked
    /// wants in that order, each up to what it wants; anything left over stays
    /// free in the collection. K.I.S.S.: no priorities, just ticks.
    /// </summary>
    public partial class GotItDialog : FluentWindow
    {
        private readonly List<Line> _lines;

        /// <summary>Copies received (after OK).</summary>
        public int Received { get; private set; }

        /// <summary>The ticked wants and how many copies each gets (after OK).</summary>
        public List<(WantNeed Want, int Copies)> Assign { get; } = new();

        public GotItDialog(string card, IReadOnlyList<WantNeed> wants, int received)
        {
            InitializeComponent();
            CardText.Text = card;
            _lines = wants.Select(w => new Line(w)).ToList();
            foreach (var l in _lines) l.IsChecked = true;
            WantList.ItemsSource = _lines;
            int wanted = wants.Sum(w => w.Wanted);
            WantedText.Text = $"{wanted} wanted in all";
            CountBox.Text = Math.Max(1, received).ToString();
            Loaded += (_, _) => { CountBox.Focus(); CountBox.SelectAll(); };
            Recalc();
        }

        /// <summary>Ask; null when cancelled.</summary>
        public static GotItDialog? Ask(Window? owner, string card, IReadOnlyList<WantNeed> wants, int received)
        {
            var dlg = new GotItDialog(card, wants, received) { Owner = owner };
            return dlg.ShowDialog() == true ? dlg : null;
        }

        private int Count => int.TryParse(CountBox.Text, out int n) && n > 0 ? n : 0;

        /// <summary>Give the copies to the ticked wants, in order, each up to what it wants.</summary>
        private void Recalc()
        {
            if (_lines == null) return;
            int left = Count;
            foreach (var l in _lines)
            {
                int give = l.IsChecked ? Math.Min(left, l.Want.Wanted) : 0;
                l.Gets = give;
                left -= give;
            }
            var parts = _lines.Where(l => l.Gets > 0).Select(l => $"{l.Gets} {(l.Want.IsCollection ? "for the Collection" : "claimed for " + l.Want.ForName)}").ToList();
            if (left > 0) parts.Add($"{left} extra, free in your collection");
            SummaryText.Text = Count == 0 ? "Enter how many copies you got."
                : "All go into your collection: " + string.Join(", ", parts) + "." +
                  (_lines.Any(l => !l.IsChecked) ? " Unticked wants stay on the Want List." : "");
        }

        private void Want_Click(object sender, RoutedEventArgs e) => Recalc();
        private void CountBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => Recalc();

        private void CountBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            foreach (char c in e.Text)
                if (!char.IsDigit(c)) { e.Handled = true; return; }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (Count < 1) { CountBox.Focus(); CountBox.SelectAll(); return; }
            Recalc();
            Received = Count;
            Assign.Clear();
            foreach (var l in _lines.Where(l => l.Gets > 0)) Assign.Add((l.Want, l.Gets));
            DialogResult = true;
        }

        /// <summary>One want in the list.</summary>
        public sealed class Line : INotifyPropertyChanged
        {
            public Line(WantNeed want) { Want = want; }
            public WantNeed Want { get; }

            public string Label
            {
                get
                {
                    if (Want.IsCollection) return $"Collection — wants {Want.Wanted}";
                    string deck = $"{Want.ForName} — wants {Want.Wanted}";
                    if (Want.DeckGone) return deck + " (deck file not found)";
                    if (Want.DeckMissing == 0) return deck + " (deck isn't missing any)";
                    return Want.DeckMissing > 0 ? deck + $", deck missing {Want.DeckMissing}" : deck;
                }
            }

            public string Tip => Want.IsCollection
                ? "Copies for the Collection go in free (no deck claims them)."
                : "Copies for a deck are claimed for it (up to what the deck still misses).";

            private bool _checked;
            public bool IsChecked
            {
                get => _checked;
                set { _checked = value; Changed(nameof(IsChecked)); }
            }

            private int _gets;
            public int Gets
            {
                get => _gets;
                set { _gets = value; Changed(nameof(Gets)); Changed(nameof(GetsText)); }
            }

            public string GetsText => Gets > 0 ? $"gets {Gets}" : "";

            public event PropertyChangedEventHandler? PropertyChanged;
            private void Changed(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
        }
    }
}
