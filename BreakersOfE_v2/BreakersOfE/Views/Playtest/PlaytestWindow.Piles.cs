using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BreakersOfE.Models;
using BreakersOfE.Playtest;

namespace BreakersOfE.Views.Playtest
{
    // Where cards sit, and piles of basic lands. Played cards go to their
    // lane: creatures (and planeswalkers, battles) along the top, non-creature
    // artifacts and enchantments in the row above the lands, lands along the
    // bottom. Basic lands of one name share a pile ("Forest ×6 · 2 tapped");
    // nonbasic lands stay single. Part of PlaytestWindow (same class).
    public partial class PlaytestWindow
    {
        /// <summary>Piled: a basic land with nothing on it of its own (face up, no counters, no damage).</summary>
        private bool Pileable(GameCard c) => c.IsBasicLand && !c.FaceDown && c.Counters.Count == 0 && c.Damage == 0 &&
                                             c.AttachedTo == 0 && c.MergedInto == 0 && !Me.Battlefield.Any(o => o.AttachedTo == c.Id);

        /// <summary>The pile a battlefield card belongs to (just itself when it isn't piled), in battlefield order.</summary>
        private List<GameCard> PileOf(GameCard c)
        {
            if (!Pileable(c)) return new List<GameCard> { c };
            string name = c.FaceName(0);
            var pile = Me.Battlefield.Where(o => Pileable(o) && o.FaceName(0) == name).ToList();
            return pile.Count > 0 ? pile : new List<GameCard> { c };
        }

        /// <summary>The card a pile shows (and acts as): its first untapped one, else its first.</summary>
        private static GameCard Face(List<GameCard> pile) => pile.FirstOrDefault(c => !c.Tapped) ?? pile[0];

        /// <summary>The battlefield in groups: piles of basic lands, everything else on its own. In drawing order.</summary>
        private List<List<GameCard>> FieldGroups()
        {
            var groups = new List<List<GameCard>>();
            var piles = new Dictionary<string, List<GameCard>>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in Me.Battlefield)
            {
                if (DrawnWithOther(c)) continue;                         // drawn with its card (attached) or under its top (mutate)
                if (!Pileable(c)) { groups.Add(new List<GameCard> { c }); continue; }
                string name = c.FaceName(0);
                if (!piles.TryGetValue(name, out var pile))
                {
                    piles[name] = pile = new List<GameCard>();
                    groups.Add(pile);
                }
                pile.Add(c);
            }
            return groups;
        }

        /// <summary>Draw a pile: its face card, rotated when every land in it is tapped, a count, and a card or two peeking out behind.</summary>
        private FrameworkElement PileVisual(List<GameCard> pile, double w, double h)
        {
            var face = Face(pile);
            var front = CardVisual(face, w, h);
            if (pile.Count == 1) return front;
            int tapped = pile.Count(c => c.Tapped);
            var root = new Grid { Width = w + 8, Height = h + 8 };
            for (int i = Math.Min(2, pile.Count - 1); i >= 1; i--)               // the cards behind
                root.Children.Add(new Border
                {
                    Width = w, Height = h, CornerRadius = new CornerRadius(w * 0.045),
                    Margin = new Thickness(i * 4, i * 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                    Background = new SolidColorBrush(Color.FromArgb(200, 40, 40, 40)), BorderBrush = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                });
            front.HorizontalAlignment = HorizontalAlignment.Left;
            front.VerticalAlignment = VerticalAlignment.Top;
            root.Children.Add(front);
            root.Children.Add(new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 8, h * 0.06), Padding = new Thickness(6, 1, 6, 2), CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(235, 20, 20, 20)),
                Child = new TextBlock
                {
                    Text = $"×{pile.Count}" + (tapped > 0 && tapped < pile.Count ? $" · {tapped} tapped" : tapped == pile.Count ? " · all tapped" : ""),
                    Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = Math.Max(11, w * 0.12),
                },
            });
            return root;
        }

        /// <summary>Tap the next untapped land of a pile (or, when all are tapped, untap one).</summary>
        private void TapOne(List<GameCard> pile)
        {
            var c = pile.FirstOrDefault(x => !x.Tapped);
            if (c != null) TapCards(new List<GameCard> { c });
            else _game.SetTapped(Me, new[] { pile[0] }, false);
        }

        private void UntapOne(List<GameCard> pile)
        {
            var c = pile.FirstOrDefault(x => x.Tapped);
            if (c != null) _game.SetTapped(Me, new[] { c }, false);
        }

        // ── Where a card goes when played ────────────────────────────────
        /// <summary>The lane for a card played on a face (0 = front, 1 = back).</summary>
        private static Lane LaneOf(GameCard c, bool back)
        {
            if (back == c.BackUp) return c.Lane;
            string t = c.FaceType(back ? 1 : 0);
            bool Has(string w) => t.Contains(w, StringComparison.OrdinalIgnoreCase);
            if (Has("Land") && !Has("Creature")) return Lane.Land;
            if ((Has("Artifact") || Has("Enchantment")) && !Has("Creature")) return Lane.Support;
            return Lane.Top;
        }

        /// <summary>The lane for a token or any card by its type line (front face).</summary>
        private static Lane LaneOf(DeckCard card)
        {
            string t = CardFaces.Sides(card.TypeLine)[0];
            bool Has(string w) => t.Contains(w, StringComparison.OrdinalIgnoreCase);
            if (Has("Land") && !Has("Creature")) return Lane.Land;
            if ((Has("Artifact") || Has("Enchantment")) && !Has("Creature")) return Lane.Support;
            return Lane.Top;
        }

        /// <summary>Where a card lands when played: on its pile (a basic land with others of its name out), else the next free spot in its lane.</summary>
        private (double X, double Y) SpotFor(GameCard c, bool back = false, bool faceDown = false)
        {
            if (!faceDown && c.IsBasicLand)
            {
                var pile = Me.Battlefield.FirstOrDefault(o => o.Id != c.Id && Pileable(o) && o.FaceName(0) == c.FaceName(0));
                if (pile != null) return (pile.X, pile.Y);
            }
            return FreeSpot(faceDown ? Lane.Top : LaneOf(c, back));
        }

        /// <summary>
        /// The next free spot in a lane. Rows: lands from the bottom up; the
        /// artifact and enchantment row starts a full card above the highest land row;
        /// the top lane from the top down. Piles count as one spot.
        /// </summary>
        private (double X, double Y) FreeSpot(Lane lane)
        {
            double w = CardW, h = CardH;
            double fw = Math.Max(1, Field.ActualWidth - w), fh = Math.Max(1, Field.ActualHeight - h);
            double stepX = (w + 12) / fw, stepY = (h * 0.62 + 10) / fh;
            // The artifact and enchantment row sits a whole card (and a gap) above the highest land row, clear of the lands.
            double clear = (h + 28) / fh;
            double start = lane switch
            {
                Lane.Top => 0,
                Lane.Land => 1,
                _ => Math.Min(1, Me.Battlefield.Where(o => o.Lane == Lane.Land).Select(o => o.Y).DefaultIfEmpty(1).Min()) - clear,
            };
            int dir = lane == Lane.Top ? 1 : -1;
            for (int row = 0; row < 8; row++)
            {
                double y = Math.Clamp(start + dir * row * stepY, 0, 1);
                for (double x = 0; x <= 1.0001; x += stepX)
                {
                    // Taken: near a card, or near the cards cascading under it (what's attached to it).
                    bool taken = Me.Battlefield.Any(o =>
                    {
                        if (DrawnWithOther(o) || Math.Abs(o.X - x) * fw >= w * 0.85) return false;
                        double dy = (y - o.Y) * fh;
                        double below = h * 0.5 + AttachedTo(o).Count * h * 0.40;
                        return dy > -h * 0.5 && dy < below;
                    });
                    if (!taken) return (Math.Min(x, 1), y);
                }
            }
            return (0.5, 0.5);
        }
    }
}
