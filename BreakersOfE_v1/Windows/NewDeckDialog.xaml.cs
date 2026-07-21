using BreakersOfE.Models;
using System.Windows;

namespace BreakersOfE.Windows
{
    public partial class NewDeckDialog : Window
    {
        public string DeckName { get; private set; } = "New Deck";
        public DeckType DeckType { get; private set; } = DeckType.Standard;

        public NewDeckDialog()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                TxtDeckName.SelectAll();
                TxtDeckName.Focus();
            };
        }

        private void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            string name = TxtDeckName.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Please enter a deck name.",
                    "New Deck", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                TxtDeckName.Focus();
                return;
            }

            DeckName = name;
            DeckType = RadioCommander.IsChecked == true
                ? DeckType.Commander
                : DeckType.Standard;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
