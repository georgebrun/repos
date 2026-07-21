using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;

namespace BreakersOfE.Windows
{
    public partial class CardImageWindow : Window
    {
        private readonly ImageSource? _frontImage;
        private readonly ImageSource? _backImage;
        private bool _showingBack = false;

        public CardImageWindow(ImageSource? image, string cardName,
            ImageSource? backImage = null)
        {
            InitializeComponent();
            _frontImage = image;
            _backImage = backImage;

            CardImage.Source = image;
            CardNameText.Text = cardName;
            Title = cardName;

            // Show flip button only for DFCs
            if (backImage != null)
            {
                BtnFlip.Visibility = Visibility.Visible;
            }
        }

        private void BtnFlip_Click(object sender, RoutedEventArgs e)
        {
            _showingBack = !_showingBack;
            CardImage.Source = _showingBack ? _backImage : _frontImage;
            BtnFlip.Content = _showingBack ? "🔄 Front Face" : "🔄 Back Face";
        }

        private void CardImage_Click(object sender, MouseButtonEventArgs e)
            => Close();
    }
}