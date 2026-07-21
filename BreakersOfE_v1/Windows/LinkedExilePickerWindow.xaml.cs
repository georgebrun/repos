using BreakersOfE.Models;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BreakersOfE.Windows
{
    public class LinkedExilePickerWindow : Window
    {
        public DeckCard? SelectedCard { get; private set; }

        private const double CardW = 90;
        private const double CardH = 126;
        private Window? _enlargedWindow;

        public LinkedExilePickerWindow(List<DeckCard> targets, string hostName)
        {
            Title = $"Exile Target Under {hostName}";
            Width = 860; Height = 420;
            WindowStyle = WindowStyle.ToolWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E));

            var grid = new Grid { Margin = new Thickness(12) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header
            var header = new TextBlock
            {
                Text = "Left-click to select a card to exile. Right-click to enlarge.",
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            // Card scroll area
            var scroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };

            foreach (var card in targets)
            {
                var border = MakeCardBorder(card);
                panel.Children.Add(border);
            }

            scroll.Content = panel;
            Grid.SetRow(scroll, 1);
            grid.Children.Add(scroll);

            // Close button
            var closeBtn = new Button
            {
                Content = "Cancel",
                Width = 80,
                Height = 28,
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66))
            };
            closeBtn.Click += (s, e) => { DialogResult = false; };
            Grid.SetRow(closeBtn, 2);
            grid.Children.Add(closeBtn);

            Content = grid;
        }

        private Border MakeCardBorder(DeckCard card)
        {
            var border = new Border
            {
                Width = CardW,
                Height = CardH,
                CornerRadius = new CornerRadius(4),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                BorderThickness = new Thickness(1.5),
                Cursor = Cursors.Hand,
                Margin = new Thickness(4),
                ToolTip = card.Name
            };

            if (!string.IsNullOrEmpty(card.LocalImagePath)
                && System.IO.File.Exists(card.LocalImagePath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(card.LocalImagePath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = (int)CardW;
                    bmp.EndInit();
                    border.Child = new System.Windows.Controls.Image
                    { Source = bmp, Stretch = Stretch.Fill };
                }
                catch { border.Child = MakeBack(card.Name); }
            }
            else
                border.Child = MakeBack(card.Name);

            // Left click = select
            border.MouseLeftButtonDown += (s, e) =>
            {
                SelectedCard = card;
                _enlargedWindow?.Close();
                DialogResult = true;
            };

            // Right click = enlarge
            border.MouseRightButtonDown += (s, e) =>
            {
                ShowEnlarged(card);
                e.Handled = true;
            };

            return border;
        }

        private static Grid MakeBack(string name)
        {
            var g = new Grid
            { Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x2A, 0x5E)) };
            g.Children.Add(new TextBlock
            {
                Text = name,
                Foreground = Brushes.White,
                FontSize = 8,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4),
                TextAlignment = TextAlignment.Center
            });
            return g;
        }

        private void ShowEnlarged(DeckCard card)
        {
            _enlargedWindow?.Close();
            var win = new Window
            {
                Title = card.Name,
                Width = 340,
                Height = 480,
                WindowStyle = WindowStyle.ToolWindow,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = Brushes.Black
            };
            if (!string.IsNullOrEmpty(card.LocalImagePath)
                && System.IO.File.Exists(card.LocalImagePath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(card.LocalImagePath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    win.Content = new System.Windows.Controls.Image
                    { Source = bmp, Stretch = Stretch.Uniform };
                }
                catch { }
            }
            win.MouseLeftButtonDown += (s, e) => win.Close();
            win.Closed += (s, e) => _enlargedWindow = null;
            _enlargedWindow = win;
            win.Show();
        }

        protected override void OnClosed(EventArgs e)
        {
            _enlargedWindow?.Close();
            base.OnClosed(e);
        }
    }
}