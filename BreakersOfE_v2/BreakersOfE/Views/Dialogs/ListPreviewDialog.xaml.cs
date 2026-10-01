using System.Collections.Generic;
using System.Windows;
using Wpf.Ui.Controls;

namespace BreakersOfE.Views.Dialogs
{
    /// <summary>
    /// "Here's what will happen — go ahead?" for whole-deck actions
    /// (Edit → Decks → Deck → Collection). One line per card.
    /// </summary>
    public partial class ListPreviewDialog : FluentWindow
    {
        private ListPreviewDialog(string header, string note, IReadOnlyList<string> lines, string okText)
        {
            InitializeComponent();
            HeaderText.Text = header;
            NoteText.Text = note;
            LineList.ItemsSource = lines;
            BtnOk.Content = okText;
        }

        /// <summary>True when the user clicked the OK button.</summary>
        public static bool Ask(Window? owner, string header, string note, IReadOnlyList<string> lines, string okText)
        {
            var dlg = new ListPreviewDialog(header, note, lines, okText) { Owner = owner };
            return dlg.ShowDialog() == true;
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
