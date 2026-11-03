using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BreakersOfE.Models;

namespace BreakersOfE.Views.Playtest
{
    /// <summary>
    /// A modal double-faced card (spell // land and the like) going onto the
    /// battlefield: click the face to play it on. 0 = front, 1 = back, null = cancelled.
    /// </summary>
    internal static class ModalFaceDialog
    {
        public static int? Ask(Window owner, DeckCard card, ImageSource front, ImageSource back)
        {
            int? picked = null;
            var w = new Window
            {
                Owner = owner,
                Title = "Which face?",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };
            w.SetResourceReference(Window.BackgroundProperty, "ApplicationBackgroundBrush");
            w.SetResourceReference(Window.ForegroundProperty, "TextFillColorPrimaryBrush");

            var names = CardFaces.Sides(card.Name);
            var faces = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            for (int side = 0; side < 2; side++)
            {
                int s = side;
                var img = new Image { Source = side == 0 ? front : back, Width = 220, Height = 220 * 680.0 / 488.0, Stretch = Stretch.Fill };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                var panel = new StackPanel { Margin = new Thickness(10), Cursor = Cursors.Hand, Background = Brushes.Transparent };
                panel.Children.Add(new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = img });
                panel.Children.Add(new TextBlock
                {
                    Text = $"{(side == 0 ? "Front" : "Back")}: {(s < names.Length ? names[s] : card.Name)}",
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0), FontWeight = FontWeights.SemiBold,
                });
                panel.MouseLeftButtonUp += (_, _) => { picked = s; w.DialogResult = true; };
                faces.Children.Add(panel);
            }

            var cancel = new Button { Content = "Cancel", Padding = new Thickness(14, 4, 14, 4), IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
            var root = new StackPanel { Margin = new Thickness(14) };
            root.Children.Add(new TextBlock { Text = "Play it on which face?", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 4) });
            root.Children.Add(faces);
            root.Children.Add(cancel);
            w.Content = root;
            w.KeyDown += (_, e) =>
            {
                if (e.Key is Key.D1 or Key.NumPad1 or Key.Left) { picked = 0; w.DialogResult = true; }
                if (e.Key is Key.D2 or Key.NumPad2 or Key.Right) { picked = 1; w.DialogResult = true; }
            };
            return w.ShowDialog() == true ? picked : null;
        }
    }
}
