using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using BreakersOfE.Playtest;

namespace BreakersOfE.Views.Playtest
{
    // Mana: tapping a land or a mana stone adds its mana to your pool (a
    // card with a choice asks which); creatures and other cards tap for mana
    // from their menu (every ability is a "you may"). The pool sits beside
    // your life. Paying for spells stays yours to do.
    // Part of PlaytestWindow (same class).
    public partial class PlaytestWindow
    {
        /// <summary>The ways this card taps for mana now (after Blood Moon, Urborg … on the battlefield).</summary>
        private List<ManaOption> Mana(GameCard c) => ManaSources.Of(c, Me, _game.Seats);

        /// <summary>Tapping it means mana: a land, or a mana stone (a non-creature artifact that makes mana).</summary>
        private bool AsksOnTap(GameCard c) =>
            S.TapForMana && !c.FaceDown && (c.IsLandNow ||
            (!c.IsCreatureNow && c.FaceType(c.BackUp ? 1 : 0).Contains("Artifact", StringComparison.OrdinalIgnoreCase)));

        /// <summary>Tap or untap (Enter, the Tap button): tapping a land or mana stone makes its mana.</summary>
        private void ToggleTap(GameCard c)
        {
            if (c.Tapped) _game.Tap(c);
            else TapCards(new List<GameCard> { c });
        }

        /// <summary>Tap these (one card, or a pile's lands): for mana when they make it, else just tapped.</summary>
        private void TapCards(List<GameCard> cards)
        {
            var untapped = cards.Where(x => !x.Tapped).ToList();
            if (untapped.Count == 0) return;
            var first = untapped[0];
            var options = AsksOnTap(first) ? Mana(first) : new List<ManaOption>();
            if (options.Count == 0) { _game.SetTapped(Me, untapped, true); return; }
            if (options.Count == 1 && !options[0].Sacrifice) { FinishMana(untapped, options[0]); return; }
            OfferMana(untapped, options, justTap: true);
        }

        /// <summary>A menu at the mouse: each way to tap for mana (and, from Tap, "Just Tap").</summary>
        private void OfferMana(List<GameCard> cards, List<ManaOption> options, bool justTap)
        {
            var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
            string who = cards.Count == 1 ? cards[0].ShownName : $"{cards.Count} × {cards[0].ShownName}";
            menu.Items.Add(new MenuItem { Header = $"Tap {who} for:", IsEnabled = false });
            foreach (var o in options)
            {
                var pick = o;
                var mi = new MenuItem { Header = pick.Label, Icon = Pips(pick.Mana, 14) };
                mi.Click += (_, _) => FinishMana(cards, pick);
                menu.Items.Add(mi);
            }
            if (justTap)
            {
                menu.Items.Add(new Separator());
                var plain = new MenuItem { Header = "Just Tap (no mana)" };
                plain.Click += (_, _) => _game.SetTapped(Me, cards, true);
                menu.Items.Add(plain);
            }
            menu.IsOpen = true;
        }

        /// <summary>Tap for this choice: Utopia Sprawl and the like ask their color first.</summary>
        private void FinishMana(List<GameCard> cards, ManaOption option)
        {
            var extra = ManaSources.ExtraFor(cards[0], Me, _game.Seats);
            if (extra.AnyColor == 0) { _game.TapForMana(Me, cards, option); return; }
            var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
            menu.Items.Add(new MenuItem { Header = $"{string.Join(", ", extra.Because)}: the extra mana's color", IsEnabled = false });
            foreach (var m in ManaSources.Colors.Take(5))
            {
                string color = m;
                var mi = new MenuItem { Header = $"{ManaSources.Symbols(color)} {ManaSources.ColorName(color)}", Icon = Pips(color, 14) };
                mi.Click += (_, _) => _game.TapForMana(Me, cards, option, new string(color[0], extra.AnyColor));
                menu.Items.Add(mi);
            }
            menu.IsOpen = true;
        }

        /// <summary>"Tap for Mana" in a battlefield card's menu (creatures like Llanowar Elves too): null when it makes none.</summary>
        private MenuItem? ManaMenu(GameCard c)
        {
            if (c.Tapped) return null;
            var options = Mana(c);
            if (options.Count == 0) return null;
            bool sick = Keywords.IsSick(c, _game.Turn, Keywords.For(c, Me, _game.Seats));
            var mi = new MenuItem { Header = sick ? "Tap for Mana (summoning sick: not until your next turn)" : "Tap for Mana" };
            foreach (var o in options)
            {
                var pick = o;
                var it = new MenuItem { Header = pick.Label, Icon = Pips(pick.Mana, 14) };
                it.Click += (_, _) => FinishMana(new List<GameCard> { c }, pick);
                mi.Items.Add(it);
            }
            return mi;
        }

        // ── The pool ─────────────────────────────────────────────────────
        private static (Color Fill, Color Ink) ManaColor(string m) => m switch
        {
            "W" => (Color.FromRgb(0xF3, 0xEB, 0xC8), Color.FromRgb(0x30, 0x2A, 0x20)),
            "U" => (Color.FromRgb(0x2E, 0x7F, 0xD0), Colors.White),
            "B" => (Color.FromRgb(0x2B, 0x25, 0x23), Colors.White),
            "R" => (Color.FromRgb(0xD6, 0x40, 0x2F), Colors.White),
            "G" => (Color.FromRgb(0x2F, 0x8F, 0x47), Colors.White),
            _ => (Color.FromRgb(0x9C, 0x9C, 0x9C), Color.FromRgb(0x20, 0x20, 0x20)),
        };

        /// <summary>One mana symbol: a colored circle with its letter (or a count).</summary>
        private static FrameworkElement Pip(string m, double size, string? text = null)
        {
            var (fill, ink) = ManaColor(m);
            return new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Margin = new Thickness(1),
                Background = new SolidColorBrush(fill), BorderBrush = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = text ?? m, Foreground = new SolidColorBrush(ink), FontWeight = FontWeights.Bold,
                    FontSize = size * 0.55, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
        }

        /// <summary>Symbols in a row (for a menu's icon): "GG" → two green circles.</summary>
        private static FrameworkElement Pips(string mana, double size)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (char ch in mana.Take(4)) row.Children.Add(Pip(ch.ToString(), size));
            return row;
        }

        private void RenderMana()
        {
            ManaPool.Children.Clear();
            foreach (var m in ManaSources.Colors)
            {
                int n = Me.Mana.GetValueOrDefault(m);
                var p = Pip(m, 26, n.ToString());
                p.Opacity = n > 0 ? 1 : 0.35;
                p.Cursor = Cursors.Hand;
                p.ToolTip = $"{ManaSources.Symbols(m)} {ManaSources.ColorName(m)}: {n}. Click to add one, right-click to spend one.";
                string kind = m;
                p.MouseLeftButtonUp += (_, e) => { e.Handled = true; _game.ChangeMana(Me, kind, 1); };
                p.MouseRightButtonUp += (_, e) => { e.Handled = true; _game.ChangeMana(Me, kind, -1); };
                ManaPool.Children.Add(p);
            }
            var keeping = ManaSources.KeepingFor(Me, _game.Seats);
            ManaTitle.Text = Me.KeepManaThisTurn ? "MANA · KEPT" : keeping.Because.Count > 0 ? "MANA · KEEPS" : "MANA";
            ManaTitle.ToolTip = "Your mana pool: it empties as each step ends." +
                (keeping.Because.Count > 0 ? $"\n{string.Join(", ", keeping.Because)}: " +
                    (keeping.ToColorless ? "unspent mana becomes colorless instead." :
                     keeping.Keep.Count >= 6 ? "unspent mana doesn't empty." :
                     $"{string.Join(", ", keeping.Keep.Select(ManaSources.ColorName))} mana doesn't empty.") : "") +
                (Me.KeepManaThisTurn ? "\nKept this turn (until the cleanup)." : "") +
                "\nClick for Empty Pool and Keep This Turn.";
        }

        private void ManaTitle_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
            menu.Items.Add(new MenuItem { Header = $"Pool: {Me.ManaText}", IsEnabled = false });
            menu.Items.Add(Item("Empty Pool", () => _game.EmptyMana(Me), enabled: Me.ManaTotal > 0));
            var keep = new MenuItem
            {
                Header = "Keep This Turn (mana that lasts until end of turn)", IsCheckable = true, IsChecked = Me.KeepManaThisTurn,
            };
            keep.Click += (_, _) => _game.SetKeepMana(Me, !Me.KeepManaThisTurn);
            menu.Items.Add(keep);
            menu.IsOpen = true;
        }
    }
}
