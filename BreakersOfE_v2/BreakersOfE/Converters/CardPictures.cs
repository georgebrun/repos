using System;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BreakersOfE.Services
{
    /// <summary>
    /// The two stand-in pictures from v1: the card back (while a picture is
    /// on its way) and "Scryfall Fail" (no picture: offline, or none exists).
    /// Plus one way for the card detail views to show a card's picture.
    /// </summary>
    public static class CardPictures
    {
        public static ImageSource CardBack { get; } = Load("CardBack.jpg");
        public static ImageSource NotFound { get; } = Load("CardNotFound.png");

        private static ImageSource Load(string file)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri($"pack://application:,,,/Resources/Images/{file}", UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return new DrawingImage();            // never stop a page over a missing stand-in
            }
        }

        /// <summary>
        /// Show a card's picture in an Image: the card back at once, then the
        /// picture (kept copy, or from the internet — kept when it's one of
        /// your cards), or "Scryfall Fail" when there's none.
        /// <paramref name="isCurrent"/> says whether the view still wants
        /// this card when the picture arrives (another card may be selected by then).
        /// </summary>
        public static async Task ShowAsync(Image target, string? scryfallId, string? url, string? oldLocalPath, Func<bool> isCurrent)
        {
            target.Source = CardBack;
            ImageSource? pic = null;
            try
            {
                // v1-era rows carry their own saved picture path.
                if (!string.IsNullOrEmpty(oldLocalPath) && System.IO.File.Exists(oldLocalPath) &&
                    ImageCacheService.GetCachedPath(scryfallId) == null)
                    pic = await Task.Run(() => ImageCacheService.LoadBitmap(oldLocalPath));
                pic ??= await ImageCacheService.GetPictureAsync(scryfallId, url);
            }
            catch { pic = null; }
            if (isCurrent()) target.Source = pic ?? NotFound;
        }
    }
}
