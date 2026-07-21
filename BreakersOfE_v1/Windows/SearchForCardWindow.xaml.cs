using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace BreakersOfE.Windows
{
    public partial class SearchForCardWindow : Window
    {
        private static List<string> _history = new();
        private const int MaxHistory = 10;

        public string CardName { get; private set; } = string.Empty;
        public bool SearchInPool { get; private set; } = true;

        public SearchForCardWindow(List<string>? existingHistory = null)
        {
            InitializeComponent();

            if (existingHistory != null)
                _history = existingHistory;

            // Populate history
            foreach (var item in _history)
                CardNameCombo.Items.Add(item);

            CardNameCombo.Focus();
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            string name = CardNameCombo.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Please enter a card name.",
                    "Search", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            CardName = name;
            SearchInPool = RadioSearchPool.IsChecked == true;

            // Add to history
            if (!_history.Contains(name))
            {
                _history.Insert(0, name);
                if (_history.Count > MaxHistory)
                    _history.RemoveAt(_history.Count - 1);
            }

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void CardNameCombo_KeyDown(object sender,
            System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
                BtnOK_Click(sender, new RoutedEventArgs());
        }

        public static List<string> GetHistory() => _history;
    }
}