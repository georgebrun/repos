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
    /// Tokens ▾ → Any Token…: every token in the card pool, found by name or
    /// type ("treasure", "1/1 elf", "zombie"), whether or not the deck lists
    /// it. One line per kind of token (its newest printing); pick it, say how
    /// many, Make.
    /// </summary>
    internal sealed class TokenSearchWindow : Window
    {
        private readonly TextBox _find = new() { Margin = new Thickness(0, 0, 0, 8) };
        private readonly ListBox _list = new() { Height = 380 };
        private readonly Image _preview = new() { Width = 220, Height = 220 * 680.0 / 488.0, Stretch = Stretch.Fill };
        private readonly TextBox _count = new() { Text = "1", Width = 50, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _found = new() { FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };
        private readonly List<TokenCard> _all;
        private int _picVersion;

        /// <summary>The token chosen, and how many.</summary>
        public TokenCard? Picked { get; private set; }
        public int Count { get; private set; } = 1;

        public static (TokenCard Token, int Count)? Ask(Window owner)
        {
            var w = new TokenSearchWindow(owner);
            return w.ShowDialog() == true && w.Picked != null ? (w.Picked, w.Count) : null;
        }

        private TokenSearchWindow(Window owner)
        {
            Owner = owner;
            Title = "Make a Token";
            Width = 760;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
            SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");

            _all = PlaytestDecks.AllTokens()
                .Where(t => !string.IsNullOrWhiteSpace(t.Name))
                .GroupBy(t => (t.Name, t.PowerToughness, t.ColorIdentity, t.TypeLine))
                .Select(g => g.OrderByDescending(t => t.ReleasedAt).First())   // one per kind: its newest picture
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.PowerToughness)
                .ToList();

            var left = new StackPanel { Width = 470, Margin = new Thickness(0, 0, 16, 0) };
            left.Children.Add(new TextBlock { Text = "Find a token (name, type, power/toughness, color)", FontSize = 12, Margin = new Thickness(0, 0, 0, 4) });
            left.Children.Add(_find);
            left.Children.Add(_list);
            _found.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            left.Children.Add(_found);

            var right = new StackPanel();
            right.Children.Add(new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = _preview, Background = new SolidColorBrush(Color.FromArgb(30, 128, 128, 128)) });
            var countRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            countRow.Children.Add(new TextBlock { Text = "How many", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            countRow.Children.Add(_count);
            right.Children.Add(countRow);

            var top = new StackPanel { Orientation = Orientation.Horizontal };
            top.Children.Add(left);
            top.Children.Add(right);

            var make = new Button { Content = "Make", Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            make.Click += (_, _) => Make();
            var cancel = new Button { Content = "Cancel", Padding = new Thickness(14, 4, 14, 4), IsCancel = true };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            buttons.Children.Add(make);
            buttons.Children.Add(cancel);

            var root = new StackPanel { Margin = new Thickness(16) };
            root.Children.Add(top);
            root.Children.Add(buttons);
            Content = root;

            _find.TextChanged += (_, _) => Fill();
            _list.SelectionChanged += (_, _) => ShowPicked();
            _list.MouseDoubleClick += (_, _) => Make();
            _find.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Down && _list.Items.Count > 0)
                {
                    _list.SelectedIndex = Math.Min(_list.Items.Count - 1, _list.SelectedIndex + 1);
                    _list.ScrollIntoView(_list.SelectedItem);
                    e.Handled = true;
                }
                if (e.Key == Key.Up && _list.SelectedIndex > 0)
                {
                    _list.SelectedIndex--;
                    _list.ScrollIntoView(_list.SelectedItem);
                    e.Handled = true;
                }
            };
            Loaded += (_, _) => _find.Focus();
            Fill();
        }

        private sealed class Row
        {
            public TokenCard Token { get; init; } = null!;
            public override string ToString()
            {
                var t = Token;
                string pt = string.IsNullOrEmpty(t.PowerToughness) ? "" : $" {t.PowerToughness}";
                string colors = string.IsNullOrEmpty(t.ColorIdentity) ? "colorless" : t.ColorIdentity;
                return $"{t.Name}{pt} · {colors} · {t.TypeLine}";
            }
        }

        private void Fill()
        {
            var words = _find.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            bool Match(TokenCard t)
            {
                string all = $"{t.Name} {t.PowerToughness} {t.TypeLine} {t.ColorIdentity} {ColorNames(t.ColorIdentity)} {t.OracleText}";
                return words.All(w => all.Contains(w, StringComparison.OrdinalIgnoreCase));
            }
            var hits = _all.Where(Match).ToList();
            _list.Items.Clear();
            foreach (var t in hits.Take(300)) _list.Items.Add(new Row { Token = t });
            _found.Text = _all.Count == 0
                ? "No tokens in the card pool yet: run Update Database once."
                : hits.Count > 300 ? $"{hits.Count} tokens: the first 300 shown. Type more to narrow it."
                : $"{hits.Count} token{(hits.Count == 1 ? "" : "s")}.";
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
            else ShowPicked();
        }

        private static string ColorNames(string? ci) => string.Join(" ", (ci ?? "").Select(c => c switch
        {
            'W' => "white", 'U' => "blue", 'B' => "black", 'R' => "red", 'G' => "green", _ => "",
        }));

        private async void ShowPicked()
        {
            int v = ++_picVersion;
            if (_list.SelectedItem is not Row r) { _preview.Source = null; return; }
            _preview.Source = CardPictures.CardBack;
            var pic = await ImageCacheService.GetPictureAsync(r.Token.ScryfallId, r.Token.ImageNormalUrl, 300);
            if (v == _picVersion) _preview.Source = (ImageSource?)pic ?? CardPictures.NotFound;
        }

        private void Make()
        {
            if (_list.SelectedItem is not Row r) return;
            Picked = r.Token;
            Count = int.TryParse(_count.Text.Trim(), out int n) ? Math.Clamp(n, 1, 100) : 1;
            DialogResult = true;
        }
    }
}
