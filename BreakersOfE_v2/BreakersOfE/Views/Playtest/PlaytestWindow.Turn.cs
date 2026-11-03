using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BreakersOfE.Playtest;
using BreakersOfE.Services;

namespace BreakersOfE.Views.Playtest
{
    // The turn and its helpers: the steps of the turn (phase bar, Next Step),
    // "at the beginning of …" reminders, the stack, counters on you, damage
    // on creatures, combo alerts, and keeping a game to carry on later.
    // Part of PlaytestWindow (same class).
    public partial class PlaytestWindow
    {
        // ── The steps of the turn ────────────────────────────────────────
        private void RenderPhaseBar()
        {
            PhaseBar.Children.Clear();
            foreach (Phase p in Enum.GetValues(typeof(Phase)))
            {
                bool now = p == _game.Phase;
                var chip = new Border
                {
                    Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 4, 0), CornerRadius = new CornerRadius(6),
                    Cursor = Cursors.Hand,
                    Background = now ? new SolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0xDE)) : new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
                    ToolTip = now ? $"Now: {Game.PhaseName(p)}" : $"Go to {Game.PhaseName(p)}" + (p <= _game.Phase ? " (next turn)" : ""),
                    Child = new TextBlock
                    {
                        Text = Game.PhaseName(p), FontWeight = now ? FontWeights.SemiBold : FontWeights.Normal,
                        Foreground = now ? Brushes.White : (Brush)FindResource("TextFillColorSecondaryBrush"),
                    },
                };
                var target = p;
                chip.MouseLeftButtonUp += (_, _) => GoToPhase(target);
                PhaseBar.Children.Add(chip);
            }
        }

        private void BtnNextStep_Click(object sender, RoutedEventArgs e) => NextStep();

        /// <summary>P / Next Step: on to the next step (leaving the end step is the cleanup: hand size first).</summary>
        private void NextStep()
        {
            if (!Me.Kept) _game.Keep(Me);
            void Go()
            {
                _game.NextStep(Me, S.DrawOnNewTurn);
                ShowReminders();
            }
            if (_game.Phase == Phase.End) AfterCleanup(Go); else Go();
        }

        /// <summary>A step clicked on the phase bar: forward to it (through the next turn when it's behind).</summary>
        private void GoToPhase(Phase target)
        {
            if (target == _game.Phase) return;
            if (!Me.Kept) _game.Keep(Me);
            void Go()
            {
                _game.GoTo(Me, target, S.DrawOnNewTurn);
                ShowReminders();
            }
            if (target < _game.Phase) AfterCleanup(Go); else Go();       // behind: crosses the end of the turn
        }

        // ── Reminders ────────────────────────────────────────────────────
        private List<Reminder> _reminders = new();

        /// <summary>The "at the beginning of …" lines for the steps just entered (reminders only: your call).</summary>
        private void ShowReminders()
        {
            _reminders = S.ShowReminders ? Reminders.For(Me, _game.LastPassed) : new List<Reminder>();
            foreach (var r in _reminders)
                _game.Note($"Reminder ({Game.PhaseName(r.Phase)}): {r.Card.ShownName}: {r.Text}");
            RenderReminders();
        }

        private void RenderReminders()
        {
            ReminderList.Children.Clear();
            ReminderBar.Visibility = _reminders.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var r in _reminders.ToList())
            {
                var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                var x = new Button { Content = "✕", Padding = new Thickness(6, 0, 6, 1), FontSize = 11, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Put this one away", Focusable = false };
                var rem = r;
                x.Click += (_, _) => { _reminders.Remove(rem); RenderReminders(); };
                DockPanel.SetDock(x, Dock.Right);
                row.Children.Add(x);
                var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, Cursor = Cursors.Hand, FontSize = 12.5 };
                text.Inlines.Add(new System.Windows.Documents.Run($"{Game.PhaseName(r.Phase)} · {r.Card.ShownName}: ") { FontWeight = FontWeights.SemiBold });
                text.Inlines.Add(new System.Windows.Documents.Run(r.Text));
                text.ToolTip = "Click to select the card";
                text.MouseLeftButtonUp += (_, _) => Select(rem.Card.Id);
                row.Children.Add(text);
                ReminderList.Children.Add(row);
            }
        }

        private void RemindersDone_Click(object sender, RoutedEventArgs e)
        {
            _reminders.Clear();
            RenderReminders();
        }

        // ── The stack ────────────────────────────────────────────────────
        private void RenderStack()
        {
            StackCards.Children.Clear();
            var stack = _game.StackTopFirst();
            StackPanelBox.Visibility = stack.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            double w = PileW, h = PileH;
            for (int i = 0; i < stack.Count; i++)
            {
                var c = stack[i];
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
                var v = CardVisual(c, w, h);
                Hook(v, c, Zone.Stack);
                row.Children.Add(v);
                row.Children.Add(new TextBlock
                {
                    Text = (i == 0 ? "Resolves next:\n" : "") + c.ShownName, Foreground = Brushes.White, FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), MaxWidth = 170, TextWrapping = TextWrapping.Wrap,
                });
                StackCards.Children.Add(row);
            }
        }

        /// <summary>The top of the stack resolves (R): an instant or sorcery to the graveyard, anything else onto the battlefield.</summary>
        private void ResolveTop()
        {
            var top = _game.StackTopFirst().FirstOrDefault();
            if (top == null) return;
            ResolveCard(top);
        }

        private void ResolveTop_Click(object sender, RoutedEventArgs e) => ResolveTop();

        private void ResolveAll_Click(object sender, RoutedEventArgs e)
        {
            // One at a time: a spell that asks (its mode, its target) stops there, and you carry on after.
            for (int i = 0; i < 200 && _game.StackTopFirst().FirstOrDefault() is { } top; i++)
            {
                if (AsksOnResolve(top)) { ResolveCard(top); return; }
                ResolveCard(top);
            }
        }

        // ── Counters on you ──────────────────────────────────────────────
        private static readonly (string Kind, string Icon, string Tip)[] PlayerCounterKinds =
        {
            ("Poison", "☠", "Poison counters (ten loses the game)"),
            ("Energy", "⚡", "Energy counters"),
            ("Experience", "✦", "Experience counters"),
        };

        private void RenderPlayerCounters()
        {
            PlayerCounters.Children.Clear();
            foreach (var (kind, icon, tip) in PlayerCounterKinds)
            {
                int n = kind switch { "Poison" => Me.Poison, "Energy" => Me.Energy, _ => Me.Experience };
                var t = new TextBlock
                {
                    Text = $"{icon} {n}", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(5, 1, 5, 1),
                    Foreground = kind == "Poison" && n >= 10 ? Brushes.OrangeRed : n > 0 ? Brushes.White : new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
                    Cursor = Cursors.Hand, ToolTip = $"{tip}: click +1, right-click −1",
                };
                var k = kind;
                t.MouseLeftButtonUp += (_, e) => { e.Handled = true; _game.ChangePlayerCounter(Me, k, 1); };
                t.MouseRightButtonUp += (_, e) => { e.Handled = true; _game.ChangePlayerCounter(Me, k, -1); };
                PlayerCounters.Children.Add(t);
            }
        }

        // ── Until end of turn (Giant Growth and the like) ────────────────
        private MenuItem PumpMenu(GameCard c)
        {
            string now = c.PowerMod != 0 || c.ToughnessMod != 0
                ? $" ({(c.PowerMod >= 0 ? "+" : "")}{c.PowerMod}/{(c.ToughnessMod >= 0 ? "+" : "")}{c.ToughnessMod})" : "";
            var mi = new MenuItem { Header = "Until End of Turn" + now };
            foreach (var (dp, dt) in new[] { (1, 1), (2, 2), (3, 3), (2, 0), (-1, -1), (-2, -2), (-3, -3) })
            {
                int pp = dp, tt = dt;
                var it = new MenuItem { Header = $"{(pp >= 0 ? "+" : "")}{pp}/{(tt >= 0 ? "+" : "")}{tt}" };
                it.Click += (_, _) => _game.Pump(c, pp, tt);
                mi.Items.Add(it);
            }
            var other = new MenuItem { Header = "Other…" };
            other.Click += (_, _) =>
            {
                string? s = BreakersOfE.Views.Dialogs.TextPromptDialog.Ask(this, "Until end of turn", "Power/toughness change, like +4/+0 or -2/-2:", "+1/+1");
                var g = System.Text.RegularExpressions.Regex.Match(s ?? "", @"^\s*([+-]?\d{1,3})\s*/\s*([+-]?\d{1,3})\s*$");
                if (g.Success) _game.Pump(c, int.Parse(g.Groups[1].Value), int.Parse(g.Groups[2].Value));
            };
            mi.Items.Add(other);
            if (now.Length > 0)
            {
                mi.Items.Add(new Separator());
                var clear = new MenuItem { Header = "Clear Changes" };
                clear.Click += (_, _) => _game.ClearPump(c);
                mi.Items.Add(clear);
            }
            return mi;
        }

        // ── Damage on a creature ─────────────────────────────────────────
        private MenuItem DamageMenu(GameCard c)
        {
            var mi = new MenuItem { Header = c.Damage > 0 ? $"Damage ({c.Damage})" : "Damage" };
            foreach (int n in new[] { 1, 2, 3, 5 })
            {
                int k = n;
                var it = new MenuItem { Header = $"Mark {k}" };
                it.Click += (_, _) => _game.MarkDamage(c, k);
                mi.Items.Add(it);
            }
            var other = new MenuItem { Header = "Mark Other…" };
            other.Click += (_, _) => { if (AskNumber("Mark damage", "How much damage?") is int k) _game.MarkDamage(c, k); };
            mi.Items.Add(other);
            if (c.Damage > 0)
            {
                mi.Items.Add(new Separator());
                var less = new MenuItem { Header = "Take 1 Off" };
                less.Click += (_, _) => _game.MarkDamage(c, -1);
                mi.Items.Add(less);
                var clear = new MenuItem { Header = "Clear Damage" };
                clear.Click += (_, _) => _game.MarkDamage(c, -c.Damage);
                mi.Items.Add(clear);
            }
            return mi;
        }

        // ── Combos ───────────────────────────────────────────────────────
        private ComboWatch? _comboWatch;
        private Combo? _shownCombo;

        private void CheckCombos()
        {
            if (_comboWatch == null || !S.ComboAlerts) return;
            var fresh = _comboWatch.Check(Me);
            foreach (var c in fresh)
                _game.Note($"Combo ready: {string.Join(" + ", c.Cards)}" + (c.Results.Count > 0 ? $" ({string.Join("; ", c.Results)})" : ""));
            if (fresh.Count == 0) return;
            _shownCombo = fresh[0];
            ComboText.Text = $"Combo ready: {string.Join(" + ", _shownCombo.Cards)}" +
                (_shownCombo.Results.Count > 0 ? $"\n{string.Join(" · ", _shownCombo.Results.Take(3))}" : "") +
                (fresh.Count > 1 ? $"\n(and {fresh.Count - 1} more: see the game log)" : "");
            ComboBanner.Visibility = Visibility.Visible;
        }

        private void ComboClose_Click(object sender, RoutedEventArgs e) => ComboBanner.Visibility = Visibility.Collapsed;

        private void ComboSteps_Click(object sender, RoutedEventArgs e)
        {
            var c = _shownCombo;
            if (c == null) return;
            string text = $"{string.Join(" + ", c.Cards)}\n\n" +
                          (c.Requires.Count > 0 ? "Also needs: " + string.Join("; ", c.Requires) + "\n\n" : "") +
                          (c.Prerequisites.Length > 0 ? "Before you start: " + c.Prerequisites + "\n\n" : "") +
                          (c.ManaNeeded.Length > 0 ? "Mana: " + c.ManaNeeded + "\n\n" : "") +
                          (c.Steps.Length > 0 ? "How it goes:\n" + c.Steps + "\n\n" : "") +
                          (c.Results.Count > 0 ? "Result: " + string.Join("; ", c.Results) + "\n\n" : "") +
                          "From Commander Spellbook. Open its page?";
            if (System.Windows.MessageBox.Show(this, text, "Combo", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(c.Url) { UseShellExecute = true }); }
                catch { /* no browser */ }
            }
        }

        // ── Keeping a game for later ─────────────────────────────────────
        /// <summary>When the table closes: keep an unfinished game for next time (⚙ Table can switch this off).</summary>
        private void KeepGameOnClose()
        {
            if (!S.KeepGames) return;
            if (_game.HasMoves) SavedGames.Save(_deck, _game);
        }

        /// <summary>Opening the table: a game left last time? Carry on, or start fresh.</summary>
        private Game.State? OfferSavedGame()
        {
            if (!S.KeepGames) return null;
            var s = SavedGames.Load(_deck);
            if (s == null) return null;
            string when = s.Saved == default ? "" : $" ({s.Saved:g})";
            var r = System.Windows.MessageBox.Show(
                $"You left a game with {_deck.Name} on turn {s.Turn}, {Game.PhaseName(s.Phase)}{when}.\n\n" +
                "Yes: carry on where you left off.\nNo: start a new game (the old one is let go).",
                "Playtest", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r == MessageBoxResult.Yes) return s;
            SavedGames.Delete(_deck);
            return null;
        }
    }
}
