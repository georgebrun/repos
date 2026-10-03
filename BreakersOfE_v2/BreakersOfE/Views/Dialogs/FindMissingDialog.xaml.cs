using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Wpf.Ui.Controls;

namespace BreakersOfE.Views.Dialogs
{
    /// <summary>One line of the Find Missing dialog (a printing to swap in, or a Want List entry).</summary>
    public sealed class FindMissingItem
    {
        public bool IsChecked { get; set; }
        public string Title { get; init; } = "";
        public string Detail { get; init; } = "";
        /// <summary>What the line stands for (the page's own object).</summary>
        public object? Tag { get; init; }
    }

    /// <summary>
    /// Edit → Decks → Deck → Collection → "Find Missing…". Lists other
    /// printings you own of the missing cards (unticked) and what to put on
    /// the Want List (ticked), each with prices. Nothing changes until Apply.
    /// </summary>
    public partial class FindMissingDialog : FluentWindow
    {
        private readonly List<FindMissingItem> _swaps, _wants;

        private FindMissingDialog(string header, string note, List<FindMissingItem> swaps, List<FindMissingItem> wants)
        {
            InitializeComponent();
            _swaps = swaps;
            _wants = wants;
            HeaderText.Text = header;
            NoteText.Text = note;
            NoteText.Visibility = note.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            SwapList.ItemsSource = swaps;
            WantList.ItemsSource = wants;
            SwapSection.Visibility = swaps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            WantSection.Visibility = wants.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Show the lists; true when Apply was pressed with something ticked
        /// (the items' IsChecked say what).
        /// </summary>
        public static bool Ask(Window? owner, string header, string note,
                               List<FindMissingItem> swaps, List<FindMissingItem> wants)
        {
            var dlg = new FindMissingDialog(header, note, swaps, wants) { Owner = owner };
            return dlg.ShowDialog() == true;
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (!_swaps.Any(i => i.IsChecked) && !_wants.Any(i => i.IsChecked)) return;   // nothing ticked: stay open
            DialogResult = true;
        }
    }
}