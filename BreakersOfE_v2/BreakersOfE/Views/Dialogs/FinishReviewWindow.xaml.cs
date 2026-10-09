using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BreakersOfE.Services;

namespace BreakersOfE.Views.Dialogs
{
    /// <summary>
    /// After v1 → v2, once the card data is in: the printings that come in
    /// both foil and etched, whose copies v1 kept as foil. Foil to start (as
    /// v1 had them); Apply sets the ones picked as Etched to etched everywhere.
    /// Later: the list comes back at the next start.
    /// </summary>
    public partial class FinishReviewWindow : Wpf.Ui.Controls.FluentWindow
    {
        /// <summary>One printing in the list.</summary>
        public sealed class Item : INotifyPropertyChanged
        {
            public FinishQuestion Question { get; init; } = null!;
            public string Title => $"{Question.Name}  ({Question.SetCode.ToUpperInvariant()} #{Question.CollectorNumber})";
            public string Detail
            {
                get
                {
                    var parts = new List<string>();
                    if (Question.Copies > 0) parts.Add($"{Question.Copies} in your collection");
                    if (Question.InDecks > 0) parts.Add($"{Question.InDecks} in decks");
                    return parts.Count > 0 ? string.Join(" · ", parts) : "on the Trade Binder or Want List";
                }
            }
            public ImageSource? Picture { get; init; }

            private bool _etched;
            public bool IsEtched
            {
                get => _etched;
                set { _etched = value; Changed(nameof(IsEtched)); Changed(nameof(IsFoil)); }
            }
            public bool IsFoil
            {
                get => !_etched;
                set => IsEtched = !value;
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void Changed(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }

        private readonly List<Item> _items;
        private bool _applied;

        /// <summary>Ask about these printings. True when applied (the review is done).</summary>
        public static bool Ask(Window? owner, List<FinishQuestion> questions, int etchedOnlyFixed)
        {
            var w = new FinishReviewWindow(questions, etchedOnlyFixed);
            if (owner != null && owner.IsVisible) w.Owner = owner;
            else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            w.ShowDialog();
            return w._applied;
        }

        public FinishReviewWindow(List<FinishQuestion> questions, int etchedOnlyFixed)
        {
            InitializeComponent();
            _items = questions.Select(q => new Item { Question = q, Picture = Picture(q.ImageUrl) }).ToList();
            ItemsList.ItemsSource = _items;
            CountText.Text = _items.Count == 1 ? "1 printing" : $"{_items.Count} printings";
            if (etchedOnlyFixed > 0)
            {
                AutoText.Text = etchedOnlyFixed == 1
                    ? "1 printing that only comes in etched was set to etched already."
                    : $"{etchedOnlyFixed} printings that only come in etched were set to etched already.";
                AutoText.Visibility = Visibility.Visible;
            }
            KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        }

        private static ImageSource? Picture(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(url);
                bmp.DecodePixelWidth = 120;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        private void BtnAllFoil_Click(object sender, RoutedEventArgs e) { foreach (var i in _items) i.IsEtched = false; }
        private void BtnAllEtched_Click(object sender, RoutedEventArgs e) { foreach (var i in _items) i.IsEtched = true; }

        private void BtnLater_Click(object sender, RoutedEventArgs e) => Close();

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            var etched = _items.Where(i => i.IsEtched).Select(i => i.Question.ScryfallId).ToList();
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                if (etched.Count > 0) CollectionEditService.FoilToEtched(etched);
                _applied = true;
                Close();
            }
            catch (Exception ex)
            {
                StatusText.Text = "Couldn't apply: " + ex.Message;
                StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "BoeWarningBrush");
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }
    }
}
