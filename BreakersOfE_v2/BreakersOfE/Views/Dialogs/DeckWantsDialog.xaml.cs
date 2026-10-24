using System.Collections.Generic;
using System.Linq;
using System.Windows;
using BreakersOfE.Services;
using Wpf.Ui.Controls;

namespace BreakersOfE.Views.Dialogs
{
    /// <summary>
    /// A deck's Want List rows, one tick each: ticked stays on the Want List
    /// for the Collection, unticked is removed. Select All / Select None.
    /// Tear Down asks with it (Cancel stops the tear-down); the Want List page
    /// asks about rows for decks that are already gone.
    /// </summary>
    public partial class DeckWantsDialog : FluentWindow
    {
        private readonly List<DeckWant> _wants;

        private DeckWantsDialog(string header, string note, string okText, List<DeckWant> wants, bool showDeck)
        {
            InitializeComponent();
            _wants = wants;
            HeaderText.Text = header;
            NoteText.Text = note;
            BtnOk.Content = okText;
            ForColumn.Visibility = showDeck ? Visibility.Visible : Visibility.Collapsed;
            WantGrid.ItemsSource = _wants;
            ShowCount();
        }

        /// <summary>True when OK (the ticks are in each DeckWant.Keep); false when cancelled.</summary>
        public static bool Ask(Window? owner, string header, string note, string okText, List<DeckWant> wants, bool showDeck = false)
        {
            var dlg = new DeckWantsDialog(header, note, okText, wants, showDeck) { Owner = owner };
            return dlg.ShowDialog() == true;
        }

        private void SetAll(bool keep)
        {
            foreach (var w in _wants) w.Keep = keep;
            WantGrid.ItemsSource = null;               // plain objects: re-bind to show the change
            WantGrid.ItemsSource = _wants;
            ShowCount();
        }

        private void All_Click(object sender, RoutedEventArgs e) => SetAll(true);
        private void None_Click(object sender, RoutedEventArgs e) => SetAll(false);
        private void Keep_Click(object sender, RoutedEventArgs e) => ShowCount();

        private void ShowCount()
        {
            int keep = _wants.Where(w => w.Keep).Sum(w => w.Quantity);
            int drop = _wants.Where(w => !w.Keep).Sum(w => w.Quantity);
            CountText.Text = $"Keeping {keep} · removing {drop}";
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
