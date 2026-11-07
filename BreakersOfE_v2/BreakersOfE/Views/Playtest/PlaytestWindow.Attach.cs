using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BreakersOfE.Playtest;

namespace BreakersOfE.Views.Playtest
{
    // Attaching (Auras, Equipment, Fortifications, or anything you choose)
    // and mutate. "Attach To…" / "Mutate Onto…" (or playing an Aura or a
    // mutate card) asks you to click the card on the battlefield; dropping
    // the card onto it does the same. Attached cards tuck behind their card
    // and move with it; a mutated creature is drawn as its top card.
    // Part of PlaytestWindow (same class).
    public partial class PlaytestWindow
    {
        private enum PickKind { Attach, Mutate, Ability }

        /// <summary>Waiting for a click on the battlefield: the card to attach or mutate, and how. Null = not picking.</summary>
        private (int CardId, PickKind Kind, bool Back)? _pick;

        /// <summary>Ask for the card to attach to (or mutate onto); the next click on the battlefield answers. Esc cancels.</summary>
        private void StartPick(GameCard c, PickKind kind, bool back = false)
        {
            // An ability's target can be on either side of the table; attaching and mutating stay on yours.
            var field = kind == PickKind.Ability ? _game.Seats.SelectMany(s => s.Battlefield) : Me.Battlefield;
            if (!field.Any(o => (kind == PickKind.Ability || o.Id != c.Id) && o.MergedInto == 0 && (kind != PickKind.Mutate || o.IsCreatureNow)))
            {
                System.Windows.MessageBox.Show(this, kind == PickKind.Mutate ? "There's no creature on the battlefield to mutate onto."
                                                   : kind == PickKind.Ability ? "There's nothing on the battlefield to target."
                                                                             : "There's nothing on the battlefield to attach it to.",
                    "Playtest", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _pick = (c.Id, kind, back);
            _selectedId = 0;
            Render();
        }

        private void CancelPick()
        {
            if (_pick == null) return;
            _pick = null;
            _pendingAbility = null;
            _firstTarget = 0;
            _spread = null;
            Render();
        }

        /// <summary>The card clicked while picking: attach to it, or mutate onto it (on top or underneath).</summary>
        private void CompletePick(GameCard target)
        {
            if (_pick is not { } p) return;
            _pick = null;
            var c = Find(p.CardId);
            while (target.MergedInto != 0 && Find(target.MergedInto) is { } top) target = top;
            if (p.Kind == PickKind.Ability)
            {
                if (c == null || _pendingAbility is not { } pa) { _pendingAbility = null; _spread = null; _firstTarget = 0; Render(); return; }
                if (_spread != null) { AddSpreadTarget(c, target.Id); return; }                 // one of several targets
                if (pa.Ability.NeedsTwoTargets && _firstTarget == 0)
                {
                    _firstTarget = target.Id;                                                    // yours first, then the other one
                    StartPick(c, PickKind.Ability);
                    return;
                }
                _pendingAbility = null;
                int first = _firstTarget;
                _firstTarget = 0;
                if (pa.Ability.NeedsTwoTargets) RunAbility(c, pa.Ability, first, pa.X, pa.Paid, target2: target.Id);
                else RunAbility(c, pa.Ability, target.Id, pa.X, pa.Paid);
                return;
            }
            if (c == null || c.Id == target.Id) { Render(); return; }
            if (p.Kind == PickKind.Mutate) MutateOnto(c, target, p.Back);
            else AttachTo(c, target, p.Back);
        }

        /// <summary>Attach: from the hand, an Aura is cast with its target (onto the stack when that's on); otherwise straight on.</summary>
        private void AttachTo(GameCard c, GameCard target, bool back = false)
        {
            if (Me.ZoneOf(c) == Zone.Hand && c.IsAura && S.StackFirst) _game.CastWithTarget(c, target, mutate: false, onTop: false, backUp: back);
            else _game.Attach(c, target);
            _lastPlayedId = c.Id;
        }

        /// <summary>Mutate: on top or underneath (your choice), onto the stack from the hand when that's on.</summary>
        private void MutateOnto(GameCard c, GameCard target, bool back = false)
        {
            if (!target.IsCreatureNow)
            {
                System.Windows.MessageBox.Show(this, $"{target.ShownName} isn't a creature: mutate goes onto a creature.", "Playtest",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Render();
                return;
            }
            var r = System.Windows.MessageBox.Show(this,
                $"Mutate {c.FaceName(0)} onto {target.ShownName}:\n\nYes: on top ({c.FaceName(0)} becomes the creature you see).\n" +
                $"No: underneath ({target.ShownName} stays on top and gains its abilities).",
                "Mutate", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) { Render(); return; }
            bool onTop = r == MessageBoxResult.Yes;
            if (Me.ZoneOf(c) == Zone.Hand && S.StackFirst) _game.CastWithTarget(c, target, mutate: true, onTop: onTop, backUp: back);
            else _game.Merge(c, target, onTop);
            _lastPlayedId = c.Id;
        }

        /// <summary>Drawn with another card: attached to one that's on the battlefield, or merged under a mutated creature.</summary>
        private bool DrawnWithOther(GameCard c) =>
            c.MergedInto != 0 || (c.AttachedTo != 0 && Me.Battlefield.Any(o => o.Id == c.AttachedTo));

        /// <summary>What's attached to a card, in the order it was attached.</summary>
        private List<GameCard> AttachedTo(GameCard host) =>
            Me.Battlefield.Where(o => o.AttachedTo == host.Id && o.MergedInto == 0).ToList();

        /// <summary>The battlefield card (drawn on its own) under a point, other than the one being dragged.</summary>
        private GameCard? CardAt(Point p, int except)
        {
            GameCard? hit = null;
            foreach (var c in Me.Battlefield)
            {
                if (c.Id == except || DrawnWithOther(c)) continue;
                if (_visuals.TryGetValue(c.Id, out var v) && Over(v, p)) hit = c;       // the last one drawn is on top
            }
            return hit;
        }

        /// <summary>
        /// How far each attached card shows below the one above it, and how
        /// many fit: they cascade down from the card, but never past the top of
        /// the artifact and enchantment row (for a card in the top rows) or the
        /// bottom of the battlefield (for one in the lower rows). When there's
        /// no room for all, the last one shown says how many more are under.
        /// </summary>
        private (double Peek, int Shown) Cascade(GameCard host, int count, double h, double fh)
        {
            if (count == 0) return (0, 0);
            double want = h * 0.40, least = h * 0.13;
            double hostBottom = host.Y * fh + h;
            double limit = fh + h;                                          // the bottom of the battlefield
            if (host.Lane == Lane.Top)
            {
                double clear = (h + 28) / Math.Max(1, fh);
                double landTop = Me.Battlefield.Where(o => o.Lane == Lane.Land && !DrawnWithOther(o)).Select(o => o.Y).DefaultIfEmpty(1).Min();
                double rowTop = (Math.Min(1, landTop) - clear) * fh;      // where the artifact and enchantment row starts
                if (rowTop > hostBottom) limit = rowTop;
            }
            double room = Math.Max(0, limit - hostBottom);
            double peek = Math.Min(want, room / count);
            if (peek >= least) return (peek, count);
            int fits = Math.Max(1, (int)Math.Floor(room / least));
            return (Math.Max(least, Math.Min(want, room / fits)), Math.Min(count, fits));
        }

        // ── The fan: a card's attachments spread out while the mouse is over it ──
        /// <summary>The card whose attachments are fanned out (0 = none).</summary>
        private int _fanHostId;

        /// <summary>The fanned cards, top first (the card, then what's attached in order), and how far apart.</summary>
        private readonly List<(GameCard Card, FrameworkElement Visual)> _fan = new();
        private double _fanStep, _fanCardH;

        /// <summary>The card to fan for a card under the mouse: itself if things are attached to it, or the card it's attached to.</summary>
        private int? FanHostOf(int id)
        {
            var c = Find(id);
            if (c == null) return null;
            if (c.AttachedTo != 0 && Me.Battlefield.Any(o => o.Id == c.AttachedTo)) return c.AttachedTo;
            return AttachedTo(c).Count > 0 ? c.Id : null;
        }

        /// <summary>
        /// Draw the fan over everything: the card on top, then each attached
        /// card below it, far enough apart to read its text (its picture, name
        /// and cost under the card above). The card under the mouse comes to
        /// the front, chosen by where the mouse is (each card owns its strip),
        /// so moving up or down passes every card in turn, as in a big hand.
        /// </summary>
        private void RenderFan()
        {
            FanLayer.Children.Clear();
            _fan.Clear();
            var host = Find(_fanHostId);
            if (host == null || Me.ZoneOf(host) != Zone.Battlefield || ZoomLayer.Visibility == Visibility.Visible)
            {
                _fanHostId = 0;
                return;
            }
            var attached = AttachedTo(host);
            if (attached.Count == 0) { _fanHostId = 0; return; }

            double w = CardW, h = CardH, step = h * 0.48;
            double fw = Math.Max(1, Field.ActualWidth - w), fh = Math.Max(1, Field.ActualHeight - h);
            Point at;
            try { at = Field.TranslatePoint(new Point(host.X * fw, host.Y * fh), Root); }
            catch (InvalidOperationException) { return; }
            double height = h + attached.Count * step;
            double top = Math.Max(4, Math.Min(at.Y, Root.ActualHeight - height - 4));    // room for the whole fan

            var panel = new Canvas
            {
                Width = w + 8, Height = height, Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),   // catches the mouse
            };
            Canvas.SetLeft(panel, at.X);
            Canvas.SetTop(panel, top);
            panel.Children.Add(new Border
            {
                Width = w + 8, Height = height + 8, Margin = new Thickness(-4), CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), IsHitTestVisible = false,
            });
            var cards = new List<GameCard> { host };
            cards.AddRange(attached);
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                var v = CardVisual(c, w, h);
                Canvas.SetLeft(v, 0);
                Canvas.SetTop(v, i * step);
                Panel.SetZIndex(v, c.Id == _hoverId ? 1000 : cards.Count - i);     // the card on top over the rest, as on the table
                Hook(v, c, Zone.Battlefield);
                _visuals[c.Id] = v;
                panel.Children.Add(v);
                _fan.Add((c, v));
            }
            _fanStep = step;
            _fanCardH = h;
            panel.MouseMove += (_, e) => FanHover(e.GetPosition(panel).Y);
            panel.MouseLeave += (_, _) =>
            {
                if (_drag?.Started == true) return;
                // Redrawn under the mouse (a click, a picture arriving) isn't leaving: check once things settle.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (FanLayer.Children.Count > 0 && FanLayer.Children[0] is UIElement f && f.IsMouseOver) return;
                    if (_fanHostId == 0) return;
                    _fanHostId = 0;
                    Render();
                }), System.Windows.Threading.DispatcherPriority.Input);
            };
            FanLayer.Children.Add(panel);
        }

        /// <summary>The fanned card under the mouse, by its strip: the card on top owns everything down to its bottom edge, each attached card the strip of it that shows below.</summary>
        private void FanHover(double y)
        {
            if (_fan.Count == 0 || _drag?.Started == true) return;
            int i = y < _fanCardH ? 0 : 1 + (int)Math.Floor((y - _fanCardH) / Math.Max(1, _fanStep));
            i = Math.Clamp(i, 0, _fan.Count - 1);
            var c = _fan[i].Card;
            if (c.Id == _hoverId) return;
            _hoverId = c.Id;
            ShowInPanel(c);
            for (int k = 0; k < _fan.Count; k++)
                Panel.SetZIndex(_fan[k].Visual, _fan[k].Card.Id == _hoverId ? 1000 : _fan.Count - k);
        }

        /// <summary>The hint over the hand while picking.</summary>
        private bool RenderPickBar()
        {
            if (_pick is not { } p || Find(p.CardId) is not { } c) return false;
            MulliganBar.Visibility = Visibility.Visible;
            MulliganButtons.Visibility = Visibility.Visible;
            BtnKeep.Visibility = BtnMulligan.Visibility = Visibility.Collapsed;
            BtnSkipDiscard.Visibility = Visibility.Visible;
            BtnSkipDiscard.Content = "Cancel";
            var pa = _pendingAbility;
            bool two = p.Kind == PickKind.Ability && pa?.Ability.NeedsTwoTargets == true;
            bool fights = two && pa!.Value.Ability.Effects.Any(e => e.Kind == EffectKind.Fight && e.What == "fight");
            MulliganTitle.Text = p.Kind == PickKind.Mutate ? $"Click the creature to mutate {c.FaceName(0)} onto."
                : _spread != null ? $"{c.ShownName}: click target {_spread.Count + 1}" + (_spreadMax < 99 ? $" of up to {_spreadMax}" : "") +
                                    (_spreadDivided ? $" ({_spreadLeft} damage left to divide)" : "") + "."
                : two && _firstTarget == 0 ? $"{c.ShownName}: click your creature first."
                : two ? $"{c.ShownName}: now click the creature it {(fights ? "fights" : "deals damage to")} (either side of the table)."
                : p.Kind == PickKind.Ability ? $"Click the target for {c.ShownName}: {pa?.Ability.Label} (either side of the table)"
                : $"Click the card to attach {c.ShownName} to.";
            MulliganInfo.Text = _spread is { Count: > 0 } ? "Done when you have them all; Esc, or Cancel, to stop." : "Esc, or Cancel, to stop.";
            if (_spread is { Count: > 0 })
            {
                BtnKeep.Visibility = Visibility.Visible;
                BtnKeep.Content = "Done";
            }
            return true;
        }
    }
}
