using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BreakersOfE.Playtest;

namespace BreakersOfE.Views.Playtest
{
    // Keyword badges down the left of a creature (its own, from its Auras and
    // Equipment, and from the battlefield), and "Zz" when it's summoning sick.
    // Reminders only: nothing is stopped. Part of PlaytestWindow (same class).
    public partial class PlaytestWindow
    {
        private static string Short(string keyword) => keyword switch
        {
            "First strike" => "1st strike",
            "Double strike" => "2× strike",
            "Indestructible" => "Indestruct.",
            "Protection" => "Protection",
            _ => keyword,
        };

        private void AddKeywordBadges(Grid grid, GameCard c, double w, double h)
        {
            var keywords = Keywords.For(c, Me, _game.Seats);
            double size = Math.Max(8.5, w * 0.08);
            if (keywords.Count > 0)
            {
                var list = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(w * 0.03, h * 0.13, 0, 0),
                    ToolTip = string.Join(", ", keywords),
                };
                const int most = 5;
                foreach (var k in keywords.Take(keywords.Count > most ? most - 1 : most))
                    list.Children.Add(KeywordPill(Short(k), size, Color.FromArgb(225, 25, 25, 25)));
                if (keywords.Count > most)
                    list.Children.Add(KeywordPill($"+{keywords.Count - (most - 1)} more", size, Color.FromArgb(225, 25, 25, 25)));
                grid.Children.Add(list);
            }
            if (Keywords.IsSick(c, _game.Turn, keywords))
            {
                var zz = KeywordPill("Zz", size * 1.1, Color.FromArgb(230, 40, 70, 130));
                zz.HorizontalAlignment = HorizontalAlignment.Right;
                zz.VerticalAlignment = VerticalAlignment.Top;
                zz.Margin = new Thickness(0, h * 0.13, w * 0.03, 0);
                zz.ToolTip = "Summoning sick: it came in this turn, so it can't attack or use {T} abilities until your next turn (no haste).";
                grid.Children.Add(zz);
            }
        }

        private static Border KeywordPill(string text, double size, Color back) => new()
        {
            Background = new SolidColorBrush(back), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4, 0, 4, 1), Margin = new Thickness(0, 0, 0, 2), HorizontalAlignment = HorizontalAlignment.Left,
            BorderBrush = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)), BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = size, FontWeight = FontWeights.SemiBold },
        };
    }
}
