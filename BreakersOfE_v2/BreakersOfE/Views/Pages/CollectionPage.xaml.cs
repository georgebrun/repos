using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using BreakersOfE.Models;
using BreakersOfE.ViewModels;

namespace BreakersOfE.Views.Pages
{
    /// <summary>
    /// Collection page code-behind.
    /// 
    /// Minimal logic — only things that require direct control access:
    ///   - Search bar scroll-to-match (needs DataGrid.ScrollIntoView)
    ///   - Card image loading (needs Image.Source)
    ///   - Double-click to add (needs DataGrid event)
    /// 
    /// Everything else is in CollectionViewModel via data binding.
    /// </summary>
    public partial class CollectionPage : Page
    {
        public CollectionPage()
        {
            InitializeComponent();

            // Listen for card selection changes to update the image
            DataContextChanged += (s, e) =>
            {
                if (DataContext is CollectionViewModel vm)
                {
                    vm.PropertyChanged += ViewModel_PropertyChanged;
                }
            };
        }

        // ══════════════════════════════════════════════════════════════════
        // SEARCH — begins-with, scroll-to-match, NEVER filters rows
        // ══════════════════════════════════════════════════════════════════

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (DataContext is not CollectionViewModel vm) return;

            string search = vm.SearchText?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(search)) return;

            // Search the active grid (whichever was last interacted with)
            // Default to pool grid
            DataGrid targetGrid = PoolGrid;
            var items = vm.PoolCards;

            // Find first item whose name begins with the search text
            var match = items.FirstOrDefault(c =>
                c.Name.StartsWith(search, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                targetGrid.SelectedItem = match;
                targetGrid.ScrollIntoView(match);
            }

            // Also search collection grid
            var collMatch = vm.CollectionCards.FirstOrDefault(c =>
                c.Name.StartsWith(search, StringComparison.OrdinalIgnoreCase));

            if (collMatch != null)
            {
                CollectionGrid.SelectedItem = collMatch;
                CollectionGrid.ScrollIntoView(collMatch);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // CARD IMAGE LOADING
        // ══════════════════════════════════════════════════════════════════

        private void ViewModel_PropertyChanged(object? sender,
            System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CollectionViewModel.SelectedCardLocalImagePath) ||
                e.PropertyName == nameof(CollectionViewModel.SelectedCardImageUrl))
            {
                LoadCardImage();
            }
        }

        private void LoadCardImage()
        {
            if (DataContext is not CollectionViewModel vm) return;

            try
            {
                // Priority 1: Local cached image
                if (!string.IsNullOrEmpty(vm.SelectedCardLocalImagePath) &&
                    File.Exists(vm.SelectedCardLocalImagePath))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(vm.SelectedCardLocalImagePath);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    CardImage.Source = bitmap;
                    return;
                }

                // Priority 2: Scryfall URL
                if (!string.IsNullOrEmpty(vm.SelectedCardImageUrl))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(vm.SelectedCardImageUrl);
                    bitmap.EndInit();
                    CardImage.Source = bitmap;
                    return;
                }

                // Priority 3: No image
                CardImage.Source = null;
            }
            catch
            {
                CardImage.Source = null;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // DOUBLE-CLICK TO ADD
        // ══════════════════════════════════════════════════════════════════

        private void PoolGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (DataContext is CollectionViewModel vm)
            {
                vm.AddToCollectionCommand.Execute(null);
            }
        }
    }
}
