using System.Collections.Generic;
using System.Windows;
using Wpf.Ui.Controls;

namespace BreakersOfE.Views.Dialogs
{
    /// <summary>The user's answer to "Add anyway?".</summary>
    public enum AddWarningsChoice { Cancel, AddAll, AddOthers }

    /// <summary>
    /// Edit → Decks: adding breaks a deck rule (copies, legality, colors,
    /// size). Lists why; the user decides — a warning, never a block.
    /// "Add only the others" shows when some of the cards being added are fine.
    /// </summary>
    public partial class AddWarningsDialog : FluentWindow
    {
        private AddWarningsChoice _choice = AddWarningsChoice.Cancel;

        private AddWarningsDialog(string header, IReadOnlyList<string> warnings, int warnedCards, int fineCards)
        {
            InitializeComponent();
            HeaderText.Text = header;
            WarningList.ItemsSource = warnings;
            BtnSkip.Visibility = fineCards > 0 ? Visibility.Visible : Visibility.Collapsed;
            BtnSkip.Content = $"Add only the other {fineCards}";
            HintText.Text = fineCards > 0
                ? $"Add anyway adds all {warnedCards + fineCards}. Add only the other {fineCards} leaves out the {warnedCards} with a warning."
                : "Nothing is blocked — the deck check will keep showing the problem until it's fixed.";
        }

        public static AddWarningsChoice Ask(Window? owner, string header, IReadOnlyList<string> warnings,
                                            int warnedCards, int fineCards)
        {
            var dlg = new AddWarningsDialog(header, warnings, warnedCards, fineCards) { Owner = owner };
            dlg.ShowDialog();
            return dlg._choice;
        }

        private void AddAll_Click(object sender, RoutedEventArgs e)
        {
            _choice = AddWarningsChoice.AddAll;
            DialogResult = true;
        }

        private void Skip_Click(object sender, RoutedEventArgs e)
        {
            _choice = AddWarningsChoice.AddOthers;
            DialogResult = true;
        }
    }
}
