using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BreakersOfE.Playtest;

namespace BreakersOfE.Views.Playtest
{
    // The mouse at the table: hover shows a card in the side panel, a click
    // selects it (PlaytestWindow.Select.cs), a double-click zooms it, and a
    // card dragged onto the battlefield, the hand or a pile goes there.
    // Part of PlaytestWindow (same class).
    public partial class PlaytestWindow
    {
        private sealed class DragState
        {
            public int Id;
            public Zone From;
            public Point Start;
            public Point Grab;
            public bool Started;
            public FrameworkElement Source = null!;
            public Image? Ghost;
        }

        private DragState? _drag;

        /// <summary>Give a card's picture its mouse: hover, press (drag or double-click), right-click.</summary>
        private void Hook(FrameworkElement v, GameCard c, Zone zone)
        {
            int id = c.Id;
            v.MouseEnter += (_, _) =>
            {
                // In the hand, the card under the mouse goes by position (HandArea_MouseMove), not by which picture is on top.
                if (zone == Zone.Hand || _drag?.Started == true) return;
                // A card with things attached (or one of them): fan them out; the fan then follows the mouse itself.
                if (zone == Zone.Battlefield && FanHostOf(id) is int host)
                {
                    if (_fanHostId == host) return;
                    _fanHostId = host;
                    _hoverId = 0;                                // so the card is taken up again below
                }
                if (_hoverId == id) { ShowInPanel(Find(id)); return; }      // back on the same card: grow it again
                _hoverId = id;
                ShowInPanel(Find(id));
                QueueRender();                                   // the gold edge moves to this card
            };
            _visuals[id] = v;
            v.MouseLeave += (_, _) => { if (zone != Zone.Hand) HoverLeft(id); };
            v.MouseLeftButtonDown += (_, e) => CardDown(id, zone, v, e);
            v.MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                ShowCardMenu(id, zone, v);
            };
        }

        private void CardDown(int id, Zone zone, FrameworkElement v, MouseButtonEventArgs e)
        {
            e.Handled = true;
            // Choosing what to attach to / mutate onto: a battlefield card answers; anything else stops.
            if (_pick != null)
            {
                if (zone == Zone.Battlefield && Find(id) is { } target) CompletePick(target);
                else CancelPick();
                return;
            }
            if (e.ClickCount == 2)
            {
                _drag = null;
                Zoom(Find(id));                                  // a double-click only ever shows the card
                return;
            }
            // End of turn with too many cards: a click discards.
            if (zone == Zone.Hand && _discardDown > 0)
            {
                if (Find(id) is { } extra) DiscardForHandSize(extra);
                return;
            }
            // After a mulligan: a click puts the card on the bottom.
            if (zone == Zone.Hand && Me.BottomPending > 0)
            {
                if (Find(id) is { } bottom) _game.PutOnBottom(Me, bottom);
                return;
            }
            _drag = new DragState
            {
                Id = id, From = zone, Start = e.GetPosition(Root), Grab = e.GetPosition(v), Source = v,
            };
        }

        private void Window_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_drag == null) return;
            if (e.LeftButton != MouseButtonState.Pressed) { CancelDrag(); return; }
            var p = e.GetPosition(Root);
            if (!_drag.Started)
            {
                if ((p - _drag.Start).Length < 5) return;           // not a drag yet (a click)
                var c = Find(_drag.Id);
                if (c == null) { _drag = null; return; }
                _drag.Started = true;
                // The floating card is table-size, held where it was grabbed.
                double sw = Math.Max(1, _drag.Source.ActualWidth), sh = Math.Max(1, _drag.Source.ActualHeight);
                _drag.Grab = new Point(_drag.Grab.X * CardW / sw, _drag.Grab.Y * CardH / sh);
                _drag.Ghost = new Image
                {
                    Source = Picture(c), Width = CardW, Height = CardH, Stretch = Stretch.Fill,
                    Opacity = 0.85, IsHitTestVisible = false,
                };
                DragLayer.Children.Add(_drag.Ghost);
                ActionLayer.Children.Clear();
                HoverLayer.Children.Clear();
                _drag.Source.Opacity = 0.3;
                Root.CaptureMouse();
            }
            Canvas.SetLeft(_drag.Ghost!, p.X - _drag.Grab.X);
            Canvas.SetTop(_drag.Ghost!, p.Y - _drag.Grab.Y);
        }

        private void Window_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_drag == null) return;
            var d = _drag;
            _drag = null;
            if (!d.Started)
            {
                Select(d.Id);                                        // a click selects (a pile's own click still opens it)
                return;
            }
            e.Handled = true;                                        // a drop isn't a click on what's under it
            DragLayer.Children.Clear();
            Root.ReleaseMouseCapture();
            var p = e.GetPosition(Root);
            try
            {
                DropCard(d, p, new Point(p.X - d.Grab.X, p.Y - d.Grab.Y), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            }
            finally
            {
                Render();
            }
        }

        private void CancelDrag()
        {
            bool started = _drag?.Started == true;
            _drag = null;
            DragLayer.Children.Clear();
            if (Mouse.Captured == Root) Root.ReleaseMouseCapture();
            if (started) Render();
        }

        /// <summary>Is the point (in Root's coordinates) over this element?</summary>
        private bool Over(FrameworkElement fe, Point p)
        {
            if (!fe.IsVisible) return false;
            var q = Root.TranslatePoint(p, fe);
            return q.X >= 0 && q.Y >= 0 && q.X <= fe.ActualWidth && q.Y <= fe.ActualHeight;
        }

        private void DropCard(DragState d, Point p, Point topLeft, bool shift)
        {
            var c = Find(d.Id);
            if (c == null) return;
            if (Over(LibraryPile, p)) _game.Move(c, Zone.Library, shift ? Place.Bottom : Place.Top);
            else if (Over(GraveyardPile, p)) _game.Move(c, Zone.Graveyard);
            else if (Over(ExilePile, p)) _game.Move(c, Zone.Exile);
            else if (Over(CommandPile, p)) { if (c.IsCommander) _game.Move(c, Zone.Command); }
            else if (Over(StackPanelBox, p)) _game.Move(c, Zone.Stack);
            else if (HandArea.Parent is FrameworkElement hand && Over(hand, p)) _game.Move(c, Zone.Hand);
            else if (Over(Field, p) && CardAt(p, c.Id) is { } onto && (c.IsAura || c.IsEquipment || (c.HasMutate && d.From == Zone.Hand)) &&
                     onto.AttachedTo != c.Id)
            {
                // Dropped onto a card: an Aura or Equipment attaches; a mutate card from the hand mutates.
                if (c.HasMutate && d.From == Zone.Hand && onto.IsCreatureNow) MutateOnto(c, onto);
                else AttachTo(c, onto);
            }
            else if (Over(Field, p))
            {
                var fp = Root.TranslatePoint(topLeft, Field);
                double fw = Math.Max(1, Field.ActualWidth - CardW), fh = Math.Max(1, Field.ActualHeight - CardH);
                double x = Math.Clamp(fp.X / fw, 0, 1), y = Math.Clamp(fp.Y / fh, 0, 1);
                if (d.From == Zone.Battlefield && PileOf(c) is { Count: > 1 } pile) _game.RepositionMany(Me, pile, x, y);
                else if (d.From == Zone.Battlefield && c.AttachedTo != 0) _game.Unattach(c, x, y);     // dragged off its card
                else if (d.From == Zone.Battlefield) _game.Reposition(c, x, y);
                else PlayCard(c, x, y);
            }
        }

        /// <summary>
        /// Onto the battlefield: a modal double-faced card asks which face; no
        /// place given = the next free spot (lands along the bottom, the rest along the top).
        /// </summary>
        private void PlayCard(GameCard c, double? x = null, double? y = null, bool faceDown = false, bool tapped = false)
        {
            bool back = false;
            if (c.IsModal && !faceDown)
            {
                var pick = ModalFaceDialog.Ask(this, c.Card, FacePicture(c.Card, false), FacePicture(c.Card, true));
                if (pick == null) return;
                back = pick == 1;
            }
            // From the hand: an Aura needs its target as it's cast; a mutate card may mutate (your choice).
            if (!faceDown && Me.ZoneOf(c) == Zone.Hand)
            {
                if (LaneOf(c, back) != Lane.Land && (back ? c.FaceType(1) : c.FaceType(0)).Contains("Aura", StringComparison.OrdinalIgnoreCase))
                {
                    StartPick(c, PickKind.Attach, back);
                    return;
                }
                if (c.HasMutate && Me.Battlefield.Any(o => o.IsCreatureNow && o.MergedInto == 0))
                {
                    var r = System.Windows.MessageBox.Show(this, $"Mutate {c.FaceName(0)} onto a creature?\n\nYes: pick the creature.\nNo: cast it on its own.",
                        "Mutate", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                    if (r == MessageBoxResult.Cancel) return;
                    if (r == MessageBoxResult.Yes) { StartPick(c, PickKind.Mutate, back); return; }
                }
            }
            if (x == null || y == null)
                (x, y) = SpotFor(c, back, faceDown);                      // its lane, or its pile (basic lands)
            // Every spell goes on the stack first (⚙ Table), to be resolved (R). A land isn't cast:
            // it goes straight onto the battlefield, and so does a card put down face down.
            string type = back ? c.FaceType(1) : c.FaceType(0);
            bool isLand = type.Contains("Land", StringComparison.OrdinalIgnoreCase);
            bool instant = type.Contains("Instant", StringComparison.OrdinalIgnoreCase) || type.Contains("Sorcery", StringComparison.OrdinalIgnoreCase);
            if (!isLand && !faceDown && S.StackFirst) _game.Move(c, Zone.Stack, backUp: back, tapped: tapped && !instant);
            else if (instant && !faceDown) _game.Move(c, Zone.Graveyard);
            else _game.Move(c, Zone.Battlefield, Place.Top, x.Value, y.Value, backUp: back, faceDown: faceDown, tapped: tapped);
            _lastPlayedId = c.Id;
        }

    }
}
