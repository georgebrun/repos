using System;
using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace BreakersOfE.Views.Dialogs
{
    /// <summary>
    /// "How many copies?" for changing a row's finish, language or condition
    /// when the row holds more than one copy. Starts at every free copy.
    /// </summary>
    public partial class MoveCopiesDialog : FluentWindow
    {
        private readonly int _free;

        /// <summary>The number chosen (1…free), or null when cancelled.</summary>
        public int? Count { get; private set; }

        public MoveCopiesDialog(string action, string card, int owned, int used)
        {
            InitializeComponent();
            _free = Math.Max(0, owned - used);
            ActionText.Text = action;
            CardText.Text = card;
            CountBox.Text = _free.ToString();
            OfText.Text = $"of {_free}";
            UsedText.Text = used > 0
                ? $"{owned} in this row; {used} in use by decks stay as they are."
                : $"{owned} in this row.";
            Loaded += (_, _) => { CountBox.Focus(); CountBox.SelectAll(); };
        }

        /// <summary>Ask; returns the count, or null if cancelled.</summary>
        public static int? Ask(Window? owner, string action, string card, int owned, int used)
        {
            var dlg = new MoveCopiesDialog(action, card, owned, used) { Owner = owner };
            return dlg.ShowDialog() == true ? dlg.Count : null;
        }

        private void CountBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            foreach (char c in e.Text)
                if (!char.IsDigit(c)) { e.Handled = true; return; }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(CountBox.Text, out int n) || n < 1)
            {
                CountBox.Focus();
                CountBox.SelectAll();
                return;
            }
            Count = Math.Min(n, _free);
            DialogResult = true;
        }
    }
}
