using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using BreakersOfE.Playtest;

namespace BreakersOfE.Views.Playtest
{
    // Combat, as an assist: in the combat step the player whose turn it is picks
    // attackers (Attack on a creature's bar, or Attack with All), then Declare
    // Attackers; the defending player picks blocks (Block… on their creature's
    // bar, after the table turns to them); then Combat Damage works it all out as
    // one move (first strike, trample, deathtouch, lifelink, infect; lethal damage
    // to the graveyard). Everything can be undone. Part of PlaytestWindow.
    public partial class PlaytestWindow
    {
        /// <summary>Creatures picked to attack, before Declare Attackers: id → what it attacks (0 = the player, else a planeswalker's id).</summary>
        private readonly Dictionary<int, int> _attackPick = new();

        /// <summary>The defending player said Done Blocking this combat (no blocks is their choice, so Combat Damage doesn't ask).</summary>
        private bool _blocksDone;

        /// <summary>The game-over bar's losers, put away with Keep Playing (it comes back when something new is lost).</summary>
        private string _gameOverSeen = "";

        private bool MyTurn => _game.Seats.Count < 2 || _game.ActiveSeat == Me.Index;

        /// <summary>The player across the table whose permanents can be attacked (none on your own).</summary>
        private Seat? Defender => _game.Seats.Count > 1 ? _game.Seats.FirstOrDefault(s => s.Index != _game.ActiveSeat) : null;

        /// <summary>The bar over the hand during the combat step: what's being chosen, and the buttons to move on.</summary>
        private void RenderCombat()
        {
            CombatBar.Visibility = Visibility.Collapsed;
            CombatButtons.Children.Clear();
            if (_game.Phase != Phase.Combat) { _attackPick.Clear(); _blocksDone = false; return; }
            if (_pick != null || !Me.Kept) return;
            var attackers = _game.Seats.SelectMany(s => s.Battlefield.Where(c => c.Attacking)).ToList();
            CombatBar.Visibility = Visibility.Visible;
            bool two = Defender != null;
            string Step(int n, string what) => two ? $"Step {n} of 3 · {what}" : what;
            if (!_game.InCombat)
            {
                _blocksDone = false;
                if (!MyTurn)
                {
                    CombatTitle.Text = $"Combat: {_game.Active.Name} chooses attackers.";
                    CombatInfo.Text = "Switch Sides to choose for them.";
                    return;
                }
                // Gone or tapped since they were picked: dropped.
                foreach (var id in _attackPick.Keys.ToList())
                    if (Me.Find(id) is not { } c || Me.ZoneOf(c) != Zone.Battlefield || c.Tapped) _attackPick.Remove(id);
                CombatTitle.Text = Step(1, _attackPick.Count == 0 ? $"{_game.Active.Name}: choose attackers." : $"{_game.Active.Name}: {_attackPick.Count} picked to attack.");
                CombatInfo.Text = "Click a creature, then Attack on its bar (again to take it back), or Attack with All. Declare when you're set.";
                CombatButtons.Children.Add(CombatButton("Attack with All", () =>
                {
                    foreach (var c in Me.Battlefield.Where(CanAttack)) _attackPick.TryAdd(c.Id, 0);
                    Render();
                }));
                if (_attackPick.Count > 0) CombatButtons.Children.Add(CombatButton("Clear", () => { _attackPick.Clear(); Render(); }));
                CombatButtons.Children.Add(CombatButton(_attackPick.Count > 0 ? $"Declare Attackers ({_attackPick.Count})" : "Declare Attackers", () =>
                {
                    var list = _attackPick.Select(kv => (kv.Key, kv.Value)).ToList();
                    _attackPick.Clear();
                    _game.DeclareAttackers(Me, list);
                }, enabled: _attackPick.Count > 0));
                CombatButtons.Children.Add(CombatButton("No Attack", NextStep, tip: "On to the second main phase"));
                return;
            }
            int total = attackers.Sum(a => Math.Max(0, a.PowerNow ?? 0));
            var blockers = _game.Seats.SelectMany(s => s.Battlefield.Where(c => c.Blocking != 0)).ToList();
            if (!MyTurn)
            {
                // The defending player: blocks.
                CombatTitle.Text = Step(2, $"{Me.Name} blocks: {attackers.Count} attacking ({total} power), {blockers.Count} blocking.");
                CombatInfo.Text = "Click one of your creatures, then Block… on its bar. Done Blocking hands it back for combat damage (no blocks is fine).";
                CombatButtons.Children.Add(CombatButton("Done Blocking", () =>
                {
                    _blocksDone = true;
                    _game.BlocksDone();
                    SwitchTo(_game.ActiveSeat, cover: true, $"{_game.Active.Name}: combat damage");
                }));
                return;
            }
            bool blocksDue = Defender is { } dd && !_blocksDone && blockers.Count == 0 &&
                             dd.Battlefield.Any(c => c.IsCreatureNow && !c.Tapped && c.MergedInto == 0);
            CombatTitle.Text = two
                ? Step(blocksDue ? 2 : 3, blocksDue ? $"{Defender!.Name} blocks next: {attackers.Count} attacking ({total} power)."
                                                     : $"Combat damage: {attackers.Count} attacking ({total} power)" + (blockers.Count > 0 ? $", {blockers.Count} blocking." : ", no blocks."))
                : $"Attacking with {attackers.Count} ({total} power)" + (blockers.Count > 0 ? $"; {blockers.Count} blocking." : ".");
            CombatInfo.Text = blocksDue ? $"Their Blocks turns the table to {Defender!.Name} to choose blocks; then Combat Damage."
                            : two ? "Combat Damage deals it all, then on to Main 2 (Undo takes it back)."
                            : "Combat Damage deals it (on your own, damage to a player is yours to count).";
            if (Defender is { } d)
                CombatButtons.Children.Add(CombatButton("Their Blocks", () => SwitchTo(d.Index, cover: true, $"{d.Name}: your blocks"), tip: "Turn the table to the defending player"));
            Button? dmg = null;
            dmg = CombatButton("Combat Damage", () =>
            {
                if (!blocksDue) { _game.CombatDamage(); return; }
                // No blocks yet, and they have creatures that could: ask first.
                var m = new ContextMenu { PlacementTarget = dmg, Placement = PlacementMode.Top };
                m.Items.Add(new MenuItem { Header = $"{Defender!.Name} hasn't blocked yet.", IsEnabled = false });
                m.Items.Add(Item("Their Blocks", () => SwitchTo(Defender!.Index, cover: true, $"{Defender!.Name}: your blocks")));
                m.Items.Add(Item("Deal Damage Anyway (no blocks)", () => _game.CombatDamage()));
                m.IsOpen = true;
            });
            CombatButtons.Children.Add(dmg);
        }

        private Button CombatButton(string text, Action act, bool enabled = true, string tip = "")
        {
            var b = new Button { Content = text, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0), IsEnabled = enabled };
            if (tip.Length > 0) b.ToolTip = tip;
            b.Click += (_, _) => act();
            return b;
        }

        private bool CanAttack(GameCard c) => c.IsCreatureNow && !c.Tapped && c.MergedInto == 0 && c.AttachedTo == 0;

        /// <summary>A creature's combat buttons on its action bar: Attack (picking attackers) or Block… (the defending player's).</summary>
        private void AddCombatButtons(StackPanel row, GameCard c)
        {
            if (_game.Phase != Phase.Combat || !c.IsCreatureNow || c.MergedInto != 0) return;
            if (MyTurn && !_game.InCombat && CanAttack(c))
            {
                bool picked = _attackPick.ContainsKey(c.Id);
                row.Children.Add(BarButton(picked ? "Don't Attack" : "Attack", () =>
                {
                    if (!_attackPick.Remove(c.Id)) _attackPick[c.Id] = 0;
                    Render();
                }, picked ? "Take it out of the attack" : "It attacks the defending player"));
                // Their planeswalkers (and battles) can be attacked instead.
                var walkers = Defender?.Battlefield.Where(o => o.Counters.ContainsKey("Loyalty") || o.Counters.ContainsKey("Defense")).ToList() ?? new List<GameCard>();
                if (walkers.Count > 0)
                {
                    var at = BarButton("Attack…", () => { }, "Attack one of their planeswalkers instead");
                    at.Click += (_, _) =>
                    {
                        var m = new ContextMenu { PlacementTarget = at, Placement = PlacementMode.Bottom };
                        m.Items.Add(Item(Defender!.Name, () => { _attackPick[c.Id] = 0; Render(); }));
                        foreach (var w in walkers) { var pw = w; m.Items.Add(Item(pw.ShownName, () => { _attackPick[c.Id] = pw.Id; Render(); })); }
                        m.IsOpen = true;
                    };
                    row.Children.Add(at);
                }
            }
            // An attacker blocked by two or more: which blocker takes its damage first.
            if (MyTurn && c.Attacking && _game.Seats.SelectMany(s => s.Battlefield).Count(b => b.Blocking == c.Id) > 1)
            {
                var order = BarButton("Damage Order…", () => { }, "Which blocker takes its damage first");
                order.Click += (_, _) =>
                {
                    var m = new ContextMenu { PlacementTarget = order, Placement = PlacementMode.Bottom };
                    m.Items.Add(new MenuItem { Header = $"{c.ShownName} deals damage in this order (click one to put it first):", IsEnabled = false });
                    foreach (var b in _game.Seats.SelectMany(s => s.Battlefield).Where(b => b.Blocking == c.Id).OrderBy(b => b.BlockOrder).ToList())
                    {
                        var blocker = b;
                        m.Items.Add(Item($"{blocker.ShownName}  {blocker.PowerNow}/{blocker.ToughnessNow}", () => _game.FirstInDamageOrder(blocker)));
                    }
                    m.IsOpen = true;
                };
                row.Children.Add(order);
            }
            if (!MyTurn && _game.InCombat && !_game.CombatDealt && !c.Tapped)
            {
                var block = BarButton(c.Blocking != 0 ? "Blocking…" : "Block…", () => { }, "Choose the attacker it blocks");
                block.Click += (_, _) =>
                {
                    var m = new ContextMenu { PlacementTarget = block, Placement = PlacementMode.Bottom };
                    foreach (var a in _game.Seats.SelectMany(s => s.Battlefield.Where(o => o.Attacking)))
                    {
                        var attacker = a;
                        var mi = Item($"{attacker.ShownName}  {attacker.PowerNow}/{attacker.ToughnessNow}", () => _game.Block(c, attacker));
                        mi.IsChecked = c.Blocking == attacker.Id;
                        m.Items.Add(mi);
                    }
                    if (c.Blocking != 0) m.Items.Add(Item("Don't Block", () => _game.Block(c, null)));
                    m.IsOpen = true;
                };
                row.Children.Add(block);
            }
        }

        /// <summary>
        /// Two players: someone has lost (0 life, 10 poison, 21 commander damage, an empty library). The bar says who and why,
        /// with Play Again, Undo (the last move) or Keep Playing (put away until something new is lost).
        /// </summary>
        private void RenderGameOver()
        {
            GameOverBar.Visibility = Visibility.Collapsed;
            GameOverButtons.Children.Clear();
            if (_game.Seats.Count < 2) return;
            var losers = _game.Losers();
            string key = string.Join("|", losers.Select(l => $"{l.Seat.Index}:{l.Why}"));
            if (losers.Count == 0) { _gameOverSeen = ""; return; }
            if (key == _gameOverSeen) return;
            var winners = _game.Seats.Where(s => losers.All(l => l.Seat.Index != s.Index)).ToList();
            GameOverTitle.Text = winners.Count == 1 ? $"{winners[0].Name} {(winners[0].IsYou ? "win" : "wins")}!" : "It's a draw: everyone lost.";
            GameOverInfo.Text = string.Join("  ·  ", losers.Select(l => $"{l.Seat.Name} lost: {l.Why}."));
            GameOverBar.Visibility = Visibility.Visible;
            GameOverButtons.Children.Add(CombatButton("Play Again", () => StartOver(_oppDeck), tip: "A new game against the same deck"));
            GameOverButtons.Children.Add(CombatButton("Undo", UndoMove, tip: _game.CanUndo ? $"Undo: {_game.UndoWhat}" : "Nothing to undo", enabled: _game.CanUndo));
            GameOverButtons.Children.Add(CombatButton("Keep Playing", () => { _gameOverSeen = key; RenderGameOver(); }, tip: "Put this away (it's your call)"));
        }

        /// <summary>The combat mark on a card: picked to attack, attacking (and what), blocking (what).</summary>
        private string CombatMark(GameCard c)
        {
            if (c.Attacking)
                return c.AttackTarget > 0 && _game.FindAny(c.AttackTarget) is { } t ? $"ATTACKING {t.ShownName.ToUpperInvariant()}" : "ATTACKING";
            if (c.Blocking != 0) return _game.FindAny(c.Blocking) is { } a ? $"BLOCKING {a.ShownName.ToUpperInvariant()}" : "BLOCKING";
            if (_attackPick.TryGetValue(c.Id, out int target))
                return target > 0 && _game.FindAny(target) is { } w ? $"WILL ATTACK {w.ShownName.ToUpperInvariant()}" : "WILL ATTACK";
            return "";
        }
    }
}
