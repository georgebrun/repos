using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BreakersOfE.Models;
using BreakersOfE.Playtest;
using BreakersOfE.Services;

namespace BreakersOfE.Views.Playtest
{
    /// <summary>
    /// Sample Hands: practice opening hands from the deck, one after another,
    /// without touching the game on the table. New Hand (Space) deals 7 again;
    /// Draw (D) shows the next cards you'd draw. A running count shows how the
    /// hands have gone (lands per hand); keeping is always your call.
    /// </summary>
    internal sealed class SampleHandsWindow : Window
    {
        private readonly Deck _deck;
        private readonly IReadOnlyDictionary<string, string> _layouts;
        private readonly WrapPanel _cards = new() { Margin = new Thickness(0, 8, 0, 0) };
        private readonly TextBlock _summary = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _tally = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        private Game _game = new();
        private Seat Me => _game.Seats[0];
        private readonly List<int> _landsPerHand = new();
        private int _version;

        internal SampleHandsWindow(Window owner, Deck deck, IReadOnlyDictionary<string, string> layouts)
        {
            _deck = deck;
            _layouts = layouts;
            Owner = owner;
            Title = $"Sample Hands — {deck.Name}";
            Width = 1060;
            Height = 560;
            MinWidth = 640;
            MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
            SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
            _tally.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            buttons.Children.Add(Btn("New Hand (Space)", Deal));
            buttons.Children.Add(Btn("Draw a Card (D)", Draw));
            var close = Btn("Close", Close);
            close.IsCancel = true;
            buttons.Children.Add(close);

            var top = new StackPanel { Margin = new Thickness(16, 12, 16, 0) };
            top.Children.Add(_summary);
            top.Children.Add(_tally);
            top.Children.Add(buttons);
            var dock = new DockPanel();
            DockPanel.SetDock(top, Dock.Top);
            dock.Children.Add(top);
            dock.Children.Add(new ScrollViewer { Content = _cards, Margin = new Thickness(12, 4, 12, 12), VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            Content = dock;

            PreviewKeyDown += (_, e) =>
            {
                if (e.Key is Key.Space or Key.Enter or Key.N) { Deal(); e.Handled = true; }
                else if (e.Key == Key.D) { Draw(); e.Handled = true; }
                else if (e.Key == Key.Escape) Close();
            };
            Deal();
        }

        private static Button Btn(string text, Action act)
        {
            var b = new Button { Content = text, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0), Focusable = false };
            b.Click += (_, _) => act();
            return b;
        }

        /// <summary>A fresh shuffle and 7 cards.</summary>
        private void Deal()
        {
            _game = new Game();
            _game.AddSeat(_deck, "You", 20, _layouts);
            var sum = Game.Summarize(Me.Hand);
            _landsPerHand.Add(sum.Lands);
            ShowHand();
        }

        /// <summary>The next card off the top (what your first draws would be).</summary>
        private void Draw()
        {
            if (Me.Library.Count == 0) return;
            var c = Me.Library[0];
            Me.Library.RemoveAt(0);
            Me.Hand.Add(c);
            ShowHand();
        }

        private void ShowHand()
        {
            int v = ++_version;
            var hand = Me.Hand;
            var opening = hand.Take(Game.HandSize).ToList();
            var sum = Game.Summarize(opening);
            var parts = new List<string> { $"{sum.Lands} land{(sum.Lands == 1 ? "" : "s")}, {sum.Spells} spell{(sum.Spells == 1 ? "" : "s")}" };
            if (sum.SpellColors.Length > 0) parts.Add($"spells need {sum.SpellColors}");
            if (sum.MissingColors.Length > 0) parts.Add($"no land yet for {sum.MissingColors}");
            if (sum.ManaValues.Count > 0) parts.Add("mana values " + string.Join(", ", sum.ManaValues));
            int drawn = hand.Count - opening.Count;
            _summary.Text = $"Hand {_landsPerHand.Count}: " + string.Join(" · ", parts) + (drawn > 0 ? $"   ·   then drew {drawn}" : "");

            int n = _landsPerHand.Count;
            int fewLands = _landsPerHand.Count(l => l <= 1), lots = _landsPerHand.Count(l => l >= 5);
            _tally.Text = $"{n} hand{(n == 1 ? "" : "s")} so far: {_landsPerHand.Average():0.0} lands on average · " +
                          $"0–1 lands: {fewLands} · 2–4 lands: {n - fewLands - lots} · 5 or more: {lots}";

            _cards.Children.Clear();
            for (int i = 0; i < hand.Count; i++)
            {
                var c = hand[i];
                var img = new Image { Width = 136, Height = 136 * 680.0 / 488.0, Stretch = Stretch.Fill, Source = CardPictures.CardBack };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                var box = new StackPanel { Margin = new Thickness(5) };
                box.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(7), ClipToBounds = true, Child = img,
                    BorderThickness = new Thickness(i >= Game.HandSize ? 2 : 0), BorderBrush = Brushes.SteelBlue,
                    ToolTip = i >= Game.HandSize ? $"Draw {i - Game.HandSize + 1}" : c.Card.Name,
                });
                box.Children.Add(new TextBlock
                {
                    Text = i >= Game.HandSize ? $"Draw {i - Game.HandSize + 1}" : c.FaceName(0), FontSize = 11, Width = 136,
                    TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 3, 0, 0),
                });
                _cards.Children.Add(box);
                _ = LoadAsync(img, c.Card, v);
            }
        }

        private async System.Threading.Tasks.Task LoadAsync(Image img, DeckCard card, int version)
        {
            try
            {
                var pic = await ImageCacheService.GetPictureAsync(card.ScryfallId, card.ImageNormalUrl, 300);
                if (version == _version) img.Source = (ImageSource?)pic ?? CardPictures.NotFound;
            }
            catch
            {
                if (version == _version) img.Source = CardPictures.NotFound;
            }
        }
    }
}
