using BreakersOfE.Services;
using System.Windows;
using System.Windows.Controls;

namespace BreakersOfE.Windows
{
    public partial class KeywordDictionaryWindow : Window
    {
        public KeywordDictionaryWindow()
        {
            InitializeComponent();
            ShowAll();
        }

        private void ShowAll(string query = "")
        {
            var results = string.IsNullOrWhiteSpace(query)
                ? MtgKeywordService.All
                : MtgKeywordService.Search(query);
            var list = results.OrderBy(k => k.Name).ToList();
            DictGrid.ItemsSource = list;
            CountText.Text = $"{list.Count} keyword{(list.Count == 1 ? "" : "s")}";
        }

        private void TxtSearch_Changed(object sender, TextChangedEventArgs e)
            => ShowAll(TxtSearch.Text.Trim());

        private void DictGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DictGrid.SelectedItem is not MtgKeyword kw) return;
            SelectedKeywordName.Text = kw.Name;
            SelectedKeywordDef.Text = kw.Definition;
        }
    }
}