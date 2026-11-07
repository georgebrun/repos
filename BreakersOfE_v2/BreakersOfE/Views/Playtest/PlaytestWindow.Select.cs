using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BreakersOfE.Playtest;
using BreakersOfE.Services;

namespace BreakersOfE.Views.Playtest
{
    // Selecting a card and what can be done with it (in the manner of Duels
    // of the Planeswalkers): a click selects and never acts; the action bar
    // beside the card offers what makes sense for it; Enter / Space does the
    // main one; Z zooms, L zooms the last card played. Also the big-hand
    // scrolling and the end-of-turn discard. Part of PlaytestWindow (same class).
    public partial class PlaytestWindow
    {
        /// <summary>The selected card (0 = none).</summary>
        private int _selectedId;

        /// <summary>The last card played (L zooms it).</summary>
        private int _lastPlayedId;

        /// <summary>Each card's picture on the table now, by id (for placing the action bar).</summary>
        private readonly Dictionary<int, FrameworkElement> _visuals = new();

        // ── Selecting ────────────────────────────────────────────────────
        private void Select(int id)
        {
            _selectedId = id;
            if (Find(id) is { } c) ShowInPanel(c);
            Render();
        }

        private void Deselect()
        {
            if (_selectedId == 0) return;
            _selectedId = 0;
            Render();
        }

        private void Field_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource == Field) Deselect();                // empty table: nothing selected
        }

        /// <summary>
        /// A click anywhere that isn't a card, a button or the action bar
        /// (blank table, the hand's empty space, the piles' frames, the side
        /// panel) lets go of the selected card.
        /// </summary>
        private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_selectedId == 0 || ZoomLayer.Visibility == Visibility.Visible) return;
            for (var d = e.OriginalSource as DependencyObject; d != null; d = ParentOf(d))
            {
                if (d is System.Windows.Controls.Primitives.ButtonBase || d is System.Windows.Controls.Primitives.ScrollBar ||
                    d is ComboBox || d is TextBox || d == ActionLayer) return;       // a control: it does its own thing
                if (d is FrameworkElement fe && _visuals.ContainsValue(fe)) return;  // a card: it selects itself
            }
            Deselect();
        }

        private static DependencyObject? ParentOf(DependencyObject d) =>
            d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);

        /// <summary>The hand's card spacing as last drawn (for finding the card under the mouse).</summary>
        private double _handStep;

        /// <summary>
        /// The hand card under the mouse goes by where the mouse is along the
        /// hand, each card owning the strip it shows: so moving left or right
        /// passes every card in turn, even with the one under the mouse brought to the front.
        /// </summary>
        private void HandArea_MouseMove(object sender, MouseEventArgs e)
        {
            if (_drag?.Started == true || Me.Hand.Count == 0) return;
            var p = e.GetPosition(HandArea);
            // Above the fan, on the lifted card (reading it, or reaching for its buttons): it stays up.
            // Along the fan itself, the strip under the mouse decides, so no card is skipped.
            if (p.Y < 0 && LiftedHandId() is int lifted && _visuals.TryGetValue(lifted, out var lv) && PointerOn(lv))
            {
                if (_hoverId != lifted) { _hoverId = lifted; QueueHandRender(); }
                return;
            }
            double x = p.X - _handStart;
            int i = _handStep <= 0 ? 0 : (int)Math.Floor(x / _handStep);
            i = Math.Clamp(i, 0, Me.Hand.Count - 1);
            var c = Me.Hand[i];
            if (c.Id == _hoverId) return;
            _hoverId = c.Id;
            ShowInPanel(c);
            QueueHandRender();
        }

        private bool _handRenderQueued;

        /// <summary>
        /// The card under the mouse changed in the hand: redraw just the hand (and the
        /// action bar beside it), not the whole table, so running along the hand stays smooth.
        /// </summary>
        private void QueueHandRender()
        {
            if (_handRenderQueued) return;
            _handRenderQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _handRenderQueued = false;
                if (_drag?.Started == true || _game == null || _game.Seats.Count == 0) return;
                RenderHand();
                Dispatcher.BeginInvoke(new Action(PlaceActionBar), System.Windows.Threading.DispatcherPriority.Loaded);
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        /// <summary>The card keys act on: the selected one, else the one under the mouse.</summary>
        private GameCard? Target() => Find(_selectedId) ?? Find(_hoverId);

        /// <summary>Enter / Space: the card's main action where it is.</summary>
        private void MainAction(GameCard c)
        {
            switch (Me.ZoneOf(c))
            {
                case Zone.Hand:
                case Zone.Command: PlayCard(c); break;
                case Zone.Battlefield:
                    if (PileOf(c) is { Count: > 1 } pile) TapOne(pile); else ToggleTap(c);
                    break;
                case Zone.Library: if (Me.TopRevealed && Me.Library.FirstOrDefault()?.Id == c.Id) PlayCard(c); break;
                case Zone.Graveyard: OpenBrowser(Zone.Graveyard); break;
                case Zone.Exile: OpenBrowser(Zone.Exile); break;
                case Zone.Stack:
                    ResolveCard(c);                                  // an instant or sorcery does what it says
                    break;
            }
        }

        // ── The action bar ───────────────────────────────────────────────
        private Button BarButton(string text, Action act, string tip)
        {
            var b = new Button
            {
                Content = text, Padding = new Thickness(9, 2, 9, 3), Margin = new Thickness(0, 0, 4, 0),
                FontSize = 12, ToolTip = tip, Focusable = false,
            };
            b.Click += (_, _) => act();
            return b;
        }

        /// <summary>After drawing: the selected card's actions, just above it (below when there's no room).</summary>
        private void PlaceActionBar()
        {
            ActionLayer.Children.Clear();
            var c = Find(_selectedId);
            if (c == null || _drag?.Started == true || ZoomLayer.Visibility == Visibility.Visible) return;
            if (!_visuals.TryGetValue(c.Id, out var v) || !v.IsVisible) return;
            var zone = Me.ZoneOf(c);
            if (zone == null) return;

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            switch (zone.Value)
            {
                case Zone.Hand:
                    row.Children.Add(BarButton(c.IsModal ? "Play…" : "Play", () => PlayCard(c), "Play it (Enter)"));
                    if (!c.IsInstantOrSorceryNow)
                        row.Children.Add(BarButton("Play Tapped", () => PlayCard(c, tapped: true), "It enters the battlefield tapped (as it resolves, when it goes on the stack)"));
                    row.Children.Add(BarButton("Face Down", () => PlayCard(c, faceDown: true), "Play it face down"));
                    row.Children.Add(BarButton("Discard", () => _game.Move(c, Zone.Graveyard), "Into the graveyard (G)"));
                    AddAbilityButtons(row, c);
                    break;
                case Zone.Battlefield when PileOf(c) is { Count: > 1 } pile:
                    row.Children.Add(BarButton("Tap 1", () => TapOne(pile), "Tap the next untapped one (Enter)"));
                    if (pile.Any(p => p.Tapped)) row.Children.Add(BarButton("Untap 1", () => UntapOne(pile), "Untap one"));
                    row.Children.Add(BarButton($"Tap All {pile.Count}", () => TapCards(pile), "Every land in the pile (for mana)"));
                    row.Children.Add(BarButton("Untap All", () => _game.SetTapped(Me, pile, false), "Every land in the pile"));
                    break;
                case Zone.Battlefield:
                    AddCombatButtons(row, c);
                    row.Children.Add(BarButton(c.Tapped ? "Untap" : "Tap", () => ToggleTap(c), "Enter or Space"));
                    if (!c.Tapped && !AsksOnTap(c) && Mana(c).Count > 0)
                        row.Children.Add(BarButton("Mana…", () => OfferMana(new List<GameCard> { c }, Mana(c), justTap: false), "Tap it for mana"));
                    if (c.AttachedTo != 0) row.Children.Add(BarButton("Unattach", () => _game.Unattach(c, c.X + 0.05, c.Y), "Take it off"));
                    else if (c.IsAura || c.IsEquipment) row.Children.Add(BarButton("Attach…", () => StartPick(c, PickKind.Attach), "Click the card to attach it to"));
                    if (c.HasBack) row.Children.Add(BarButton("Turn Over", () => _game.TurnOver(c), "T"));
                    if (c.CanFlip) row.Children.Add(BarButton("Flip", () => _game.Flip(c), "F"));
                    row.Children.Add(BarButton("+1/+1", () => _game.AddCounters(c, "+1/+1", 1), "A +1/+1 counter (+); − takes one off"));
                    AddAbilityButtons(row, c);
                    break;
                case Zone.Command:
                    row.Children.Add(BarButton($"Cast (+{Me.CommanderTax(c)})", () => PlayCard(c), "Cast it from the command zone (Enter)"));
                    break;
                case Zone.Library:
                    row.Children.Add(BarButton("Play", () => PlayCard(c), "Play the top card (Enter)"));
                    row.Children.Add(BarButton("To Hand", () => _game.Move(c, Zone.Hand), "H"));
                    break;
                case Zone.Stack:
                    row.Children.Add(BarButton("Resolve", () => MainAction(c), "Resolve it (Enter; R resolves the top)"));
                    row.Children.Add(BarButton("Counter", () => _game.Move(c, Zone.Graveyard), "Countered: to the graveyard"));
                    break;
                case Zone.Graveyard:
                case Zone.Exile:
                    row.Children.Add(BarButton("Look Through", () => OpenBrowser(zone.Value), $"The whole {Game.ZoneWord(zone.Value)} (Enter)"));
                    row.Children.Add(BarButton("To Hand", () => _game.Move(c, Zone.Hand), "H"));
                    break;
            }
            row.Children.Add(BarButton("Zoom", () => Zoom(c), "Z, or double-click"));
            var more = BarButton("⋯", () => { }, "Everything else (right-click)");
            more.Click += (_, _) => ShowCardMenu(c.Id, zone.Value, more);
            row.Children.Add(more);

            var bar = new Border
            {
                Child = row, Padding = new Thickness(5), CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(235, 28, 30, 34)),
                BorderBrush = Brushes.Gold, BorderThickness = new Thickness(1),
            };
            ActionLayer.Children.Add(bar);
            bar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Rect box;
            try
            {
                box = v.TransformToAncestor(Root).TransformBounds(new Rect(0, 0, v.ActualWidth, v.ActualHeight));
            }
            catch (InvalidOperationException)
            {
                ActionLayer.Children.Clear();                            // redrawn meanwhile: the next drawing places it
                return;
            }
            double bw = bar.DesiredSize.Width, bh = bar.DesiredSize.Height;
            double x = Math.Clamp(box.X + box.Width / 2 - bw / 2, 4, Math.Max(4, Root.ActualWidth - bw - 4));
            double y = box.Y - bh - 6;
            // Above the card, unless that's off the battlefield (over the toolbar): then below it.
            double fieldTop = 0;
            try { fieldTop = Field.TranslatePoint(new Point(0, 0), Root).Y; } catch (InvalidOperationException) { }
            if (y < Math.Max(40, fieldTop)) y = Math.Min(box.Bottom + 6, Math.Max(4, Root.ActualHeight - bh - 4));
            Canvas.SetLeft(bar, x);
            Canvas.SetTop(bar, y);
        }

        // ── Zoom ─────────────────────────────────────────────────────────
        private int _zoomVersion;

        /// <summary>The card big in the middle (both faces of a two-faced card). A click or any key closes it.</summary>
        private void Zoom(GameCard? c)
        {
            if (c == null) return;
            int v = ++_zoomVersion;
            ActionLayer.Children.Clear();
            HoverLayer.Children.Clear();
            ZoomFaces.Children.Clear();
            if (!ReferenceEquals(_zoomCard, c)) _zoomBack = c.BackUp && c.HasBack;
            _zoomCard = c;
            // Just the card, big ("reading the card explains the card"); a two-faced card turns over with the button.
            double h = Math.Max(300, Root.ActualHeight * 0.78), w = h * CardAspect;
            bool back = _zoomBack && c.HasBack;
            var img = new Image { Width = w, Height = h, Stretch = Stretch.Fill, Source = FacePicture(c.Card, back) };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            ZoomFaces.Children.Add(new Border { CornerRadius = new CornerRadius(w * 0.045), ClipToBounds = true, Child = img });
            _ = SharpenAsync(img, c.Card, back, v);
            BtnZoomTurn.Visibility = c.HasBack ? Visibility.Visible : Visibility.Collapsed;
            BtnZoomTurn.Content = back ? "Turn Over (front)" : "Turn Over (back)";
            ZoomLayer.Visibility = Visibility.Visible;
            Focus();
        }

        /// <summary>The full-size picture for the zoom (the table's are smaller).</summary>
        private async Task SharpenAsync(Image img, Models.DeckCard card, bool back, int version)
        {
            try
            {
                var pic = back
                    ? await ImageCacheService.GetPictureAsync(null, card.ImageBackUrl)
                    : await ImageCacheService.GetPictureAsync(card.ScryfallId, card.ImageNormalUrl);
                if (pic != null && version == _zoomVersion) img.Source = pic;
            }
            catch
            {
                // Keep the smaller picture.
            }
        }

        private void CloseZoom()
        {
            ZoomLayer.Visibility = Visibility.Collapsed;
            _zoomVersion++;
            _zoomCard = null;
            Render();
        }

        private void ZoomLayer_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            CloseZoom();
        }

        // ── A big hand ───────────────────────────────────────────────────
        /// <summary>How far the hand is scrolled sideways (when it's wider than the table).</summary>
        private double _handScroll;

        private void HandRow_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            _handScroll -= e.Delta * 0.6;
            e.Handled = true;
            Render();
        }

        private void HandText_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            OpenBrowser(Zone.Hand);
        }

        // ── End of turn: down to the maximum hand size ───────────────────
        /// <summary>Cards still to discard before the turn passes (0 = not asking).</summary>
        private int _discardDown;

        /// <summary>What to do once the hand is down to size (the turn or step move that asked).</summary>
        private Action? _afterDiscard;

        /// <summary>Why cards are being discarded, when it isn't the end of turn ("Merfolk Looter: click 1 card to discard.").</summary>
        private string _discardWhy = "";

        /// <summary>Cards discarded since an ability asked ("…then draw that many cards").</summary>
        private int _discardCount;

        /// <summary>
        /// Leaving the end step (cleanup): down to the maximum hand size first,
        /// unless there's none; then <paramref name="next"/>. Skip is always there.
        /// </summary>
        private void AfterCleanup(Action next)
        {
            if (_discardDown > 0) return;                                // still choosing what to discard
            if (S.MaxHandSize > 0 && Me.Hand.Count > S.MaxHandSize)
            {
                _discardDown = Me.Hand.Count - S.MaxHandSize;
                _afterDiscard = next;
                Render();
                return;
            }
            next();
        }

        private void FinishDiscard()
        {
            _discardDown = 0;
            _discardWhy = "";
            var next = _afterDiscard;
            _afterDiscard = null;
            if (next != null) next(); else Render();
        }

        private void BtnSkipDiscard_Click(object sender, RoutedEventArgs e)
        {
            if (_pick != null) CancelPick(); else FinishDiscard();
        }

        /// <summary>A hand card clicked while discarding down: it goes to the graveyard; the last one carries on.</summary>
        private void DiscardForHandSize(GameCard c)
        {
            _game.Move(c, Zone.Graveyard);
            _discardCount++;
            _discardDown--;
            if (_discardDown <= 0) FinishDiscard();
            else Render();
        }
    }
}
