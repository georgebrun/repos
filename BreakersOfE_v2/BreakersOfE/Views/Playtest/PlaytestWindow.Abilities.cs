using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using BreakersOfE.Models;
using BreakersOfE.Playtest;

namespace BreakersOfE.Views.Playtest
{
    // A card's activated abilities as buttons (the action bar) and a menu
    // (right-click): planeswalker loyalty, Equip, Cycling from the hand, and
    // "{cost}: effect" on anything. Each is a "you may": pressing it pays what
    // the table can pay and does what it can read (⚙ Table: or just reminds
    // you, or is off). Part of PlaytestWindow (same class).
    public partial class PlaytestWindow
    {
        /// <summary>An ability waiting for its target to be clicked, with its X.</summary>
        private (Ability Ability, int X, List<int> Paid)? _pendingAbility;

        /// <summary>Two targets (fights): the first one clicked (yours), while the second is picked.</summary>
        private int _firstTarget;

        /// <summary>Several targets being picked ("divided as you choose", "each of up to two targets", "Tap two target creatures"): the ones so far, with their share of the damage.</summary>
        private List<(int Target, int Amount)>? _spread;
        private int _spreadMax, _spreadLeft;
        private bool _spreadDivided, _spreadPlayers;

        /// <summary>The abilities shown for a card where it is: the battlefield's, the hand's (cycling, channel) or the graveyard's.</summary>
        private List<Ability> AbilitiesHere(GameCard c)
        {
            if (S.UseAbilities == AbilityUse.Off) return new List<Ability>();
            var zone = Me.ZoneOf(c);
            return zone is Zone.Battlefield or Zone.Hand or Zone.Graveyard
                ? Abilities.Of(c).Where(a => a.From == zone).ToList()
                : new List<Ability>();
        }

        private string AbilityTip(GameCard c, Ability a)
        {
            string text = a.Text.Replace("~", c.ShownName);
            string how = S.UseAbilities == AbilityUse.Remind ? "Logs it for you to do."
                : a.Understood ? "The table does all of it."
                : a.PartlyUnderstood ? "The table does what it can read; the log tells you the rest."
                : "The table pays what it can; the log tells you what to do.";
            string extra = a.Kind == AbilityKind.Loyalty && c.LoyaltyUsedTurn == _game.Turn ? "\nAlready used a loyalty ability this turn." : "";
            if (a.Tap && Keywords.IsSick(c, _game.Turn, Keywords.For(c, Me, _game.Seats))) extra += "\nSummoning sick: {T} abilities wait until your next turn.";
            if (a.Timing.Length > 0) extra += "\n" + a.Timing;
            return $"{text}\n\n{how}{extra}";
        }

        /// <summary>The ability buttons for the action bar: up to three, then "Abilities ▾" with the rest.</summary>
        private void AddAbilityButtons(StackPanel row, GameCard c)
        {
            var list = AbilitiesHere(c);
            if (list.Count == 0) return;
            int shown = list.Count <= 3 ? list.Count : 2;
            foreach (var a in list.Take(shown))
            {
                var ab = a;
                row.Children.Add(BarButton(ab.Label, () => UseAbility(c, ab), AbilityTip(c, ab)));
            }
            if (list.Count > shown)
            {
                Button? more = null;
                more = BarButton($"Abilities ({list.Count - shown}) ▾", () =>
                {
                    var m = new ContextMenu();
                    foreach (var a in list.Skip(shown))
                    {
                        var ab = a;
                        var it = new MenuItem { Header = ab.Label, ToolTip = AbilityTip(c, ab) };
                        it.Click += (_, _) => UseAbility(c, ab);
                        m.Items.Add(it);
                    }
                    m.PlacementTarget = more;
                    m.Placement = PlacementMode.Bottom;
                    m.IsOpen = true;
                }, "More of its abilities");
                row.Children.Add(more);
            }
        }

        /// <summary>"Abilities" in a card's right-click menu (null when it has none here).</summary>
        private MenuItem? AbilitiesMenu(GameCard c)
        {
            var list = AbilitiesHere(c);
            if (list.Count == 0) return null;
            var mi = new MenuItem { Header = S.UseAbilities == AbilityUse.Remind ? "Abilities (remind me)" : "Abilities" };
            foreach (var a in list)
            {
                var ab = a;
                var it = new MenuItem { Header = ab.Label, ToolTip = AbilityTip(c, ab) };
                it.Click += (_, _) => UseAbility(c, ab);
                mi.Items.Add(it);
            }
            return mi;
        }

        /// <summary>Press an ability: X first, then the cards it costs (what to sacrifice, tap, exile …), then its target, then it's done as one move.</summary>
        private void UseAbility(GameCard c, Ability a)
        {
            if (S.UseAbilities == AbilityUse.Remind) { _game.Remind(Me, c, a); return; }
            int x = 0;
            if (a.HasX)
            {
                string? s = BreakersOfE.Views.Dialogs.TextPromptDialog.Ask(this, c.ShownName, "What's X?", "1");
                if (s == null || !int.TryParse(s.Trim(), out x) || x < 0) return;
                x = Math.Min(x, 200);
            }
            ChooseCosts(c, a, 0, new List<int>(), paid => AfterCosts(c, a, x, paid));
        }

        private void AfterCosts(GameCard c, Ability a, int x, List<int> paid)
        {
            if (a.Spread is { } sp) { StartSpread(c, a, x, paid, sp); return; }
            if (a.NeedsTwoTargets) { _firstTarget = 0; PickAbilityTarget(c, a, x, paid); return; }
            if (a.NeedsPlayer && !a.NeedsTarget)
            {
                var pm = new ContextMenu { Placement = PlacementMode.MousePoint };
                pm.Items.Add(new MenuItem { Header = $"{c.ShownName}: which player?", IsEnabled = false });
                pm.Items.Add(Item("You", () => RunAbility(c, a, -1, x, paid)));
                pm.Items.Add(Item(Opp is { } other ? other.Name : "An opponent", () => RunAbility(c, a, -2, x, paid)));
                pm.IsOpen = true;
                return;
            }
            if (!a.NeedsTarget) { RunAbility(c, a, 0, x, paid); return; }
            if (a.Effects.Any(e => e.On == EffectOn.AnyTarget))
            {
                var m = new ContextMenu { Placement = PlacementMode.MousePoint };
                m.Items.Add(new MenuItem { Header = $"{c.ShownName}: the target", IsEnabled = false });
                m.Items.Add(Item("A creature or planeswalker (click it)…", () => PickAbilityTarget(c, a, x, paid)));
                m.Items.Add(Item("You", () => RunAbility(c, a, -1, x, paid)));
                m.Items.Add(Item("An opponent", () => RunAbility(c, a, -2, x, paid)));
                m.IsOpen = true;
                return;
            }
            PickAbilityTarget(c, a, x, paid);
        }

        /// <summary>
        /// Resolve a card on the stack: an instant or sorcery does what it says
        /// (its mode asked first, then its extra costs, X and target), as one
        /// move; anything else (and a spell the table can't read) resolves as before.
        /// </summary>
        private void ResolveCard(GameCard c)
        {
            var (x, y) = SpotFor(c);
            if (!c.IsInstantOrSorceryNow || S.UseAbilities == AbilityUse.Off || c.PendingTarget != 0) { _game.Resolve(c, x, y); return; }
            var spells = Abilities.ReadSpell(c.ShownText, c.ShownName);
            if (spells.Count == 0 || spells.All(s => !s.PartlyUnderstood))
            {
                _game.Resolve(c, x, y);
                if (spells.Count > 0) _game.Note($"For you to do: {c.ShownText.Replace("\n", " ")}");
                return;
            }
            if (S.UseAbilities == AbilityUse.Remind)
            {
                _game.Resolve(c, x, y);
                _game.Note($"{c.ShownName}: {c.ShownText.Replace("\n", " ")}");
                return;
            }
            if (spells.Count == 1) { UseAbility(c, spells[0]); return; }
            if (spells[0].ChooseCount > 1) { ChooseModes(c, spells, x, y); return; }
            var m = new ContextMenu { Placement = PlacementMode.MousePoint };
            m.Items.Add(new MenuItem { Header = $"{c.ShownName}: choose one", IsEnabled = false });
            foreach (var mode in spells)
            {
                var pick = mode;
                var mi = new MenuItem { Header = pick.Label, ToolTip = AbilityTip(c, pick) };
                mi.Click += (_, _) => UseAbility(c, pick);
                m.Items.Add(mi);
            }
            m.Items.Add(new Separator());
            m.Items.Add(Item("Just resolve it (I'll do the rest)", () => _game.Resolve(c, x, y)));
            m.IsOpen = true;
        }

        /// <summary>"Choose two" / "Choose three" / "one or more": tick the modes (the menu stays open), then resolve them together.</summary>
        private void ChooseModes(GameCard c, List<Ability> modes, double x, double y)
        {
            int most = modes[0].ChooseCount;
            var picked = new List<Ability>();
            var m = new ContextMenu { Placement = PlacementMode.MousePoint, StaysOpen = true };
            var go = new MenuItem { Header = "Resolve with the ticked modes", IsEnabled = false, FontWeight = FontWeights.SemiBold };
            m.Items.Add(new MenuItem
            {
                Header = most >= 99 ? $"{c.ShownName}: choose one or more" : $"{c.ShownName}: choose {most}", IsEnabled = false,
            });
            foreach (var mode in modes)
            {
                var pick = mode;
                var mi = new MenuItem { Header = pick.Label, ToolTip = AbilityTip(c, pick), IsCheckable = true, StaysOpenOnClick = true };
                mi.Click += (_, _) =>
                {
                    if (mi.IsChecked) picked.Add(pick); else picked.Remove(pick);
                    go.IsEnabled = picked.Count > 0 && picked.Count <= most;
                };
                m.Items.Add(mi);
            }
            m.Items.Add(new Separator());
            go.Click += (_, _) =>
            {
                m.IsOpen = false;
                var all = new Ability
                {
                    Kind = AbilityKind.Spell, From = Zone.Stack, Text = string.Join(" • ", picked.Select(p => p.Text)),
                    EffectText = string.Join(" ", picked.Select(p => p.EffectText)),
                };
                foreach (var p in picked) all.Effects.AddRange(p.Effects);
                all.Choices.AddRange(modes[0].Choices);
                all.OtherCosts.AddRange(modes[0].OtherCosts);
                UseAbility(c, all);
            };
            m.Items.Add(go);
            m.Items.Add(Item("Just resolve it (I'll do the rest)", () => { m.IsOpen = false; _game.Resolve(c, x, y); }));
            m.Items.Add(Item("Cancel", () => m.IsOpen = false));
            m.IsOpen = true;
        }

        /// <summary>A spell the table would ask about as it resolves (so Resolve All stops there).</summary>
        private bool AsksOnResolve(GameCard c) =>
            c.IsInstantOrSorceryNow && S.UseAbilities == AbilityUse.Do && c.PendingTarget == 0 &&
            Abilities.ReadSpell(c.ShownText, c.ShownName).Any(s => s.PartlyUnderstood);

        /// <summary>The cards that could pay a choice: what's on the battlefield to sacrifice, tap or return; the graveyard to exile; the hand to discard.</summary>
        private List<GameCard> Candidates(GameCard c, CostChoice choice, List<int> taken)
        {
            IEnumerable<GameCard> pool = choice.Kind switch
            {
                ChoiceKind.ExileFromGraveyard => Me.Graveyard,
                ChoiceKind.Discard => Me.Hand,
                ChoiceKind.Tap => Me.Battlefield.Where(o => !o.Tapped && o.MergedInto == 0),
                ChoiceKind.RemoveCounter => Me.Battlefield.Where(o => o.MergedInto == 0 &&
                                                                 (choice.Counter.Length > 0 ? o.Counters.GetValueOrDefault(choice.Counter) > 0 : o.Counters.Count > 0)),
                _ => Me.Battlefield.Where(o => o.MergedInto == 0),
            };
            return pool.Where(o => !taken.Contains(o.Id) && (!choice.Other || o.Id != c.Id) &&
                                   !(choice.Kind is ChoiceKind.Discard or ChoiceKind.ExileFromGraveyard && o.Id == c.Id) &&
                                   Abilities.Fits(o, choice.Filter)).ToList();
        }

        /// <summary>Each choice in turn, one card at a time from a menu at the mouse; random discards are the table's. Then <paramref name="done"/>.</summary>
        private void ChooseCosts(GameCard c, Ability a, int index, List<int> paid, Action<List<int>> done)
        {
            int at = 0, need = 0;
            CostChoice? choice = null;
            foreach (var ch in a.Choices)
            {
                if (ch.Random || ch.Count >= 999) continue;
                int had = paid.Count - at;
                if (had < ch.Count) { choice = ch; need = ch.Count - Math.Max(0, had); break; }
                at += ch.Count;
            }
            if (choice == null) { done(paid); return; }
            var options = Candidates(c, choice, paid);
            if (options.Count < need)
            {
                var r = System.Windows.MessageBox.Show(this,
                    $"{c.ShownName}: \"{choice.Text.Replace("~", c.ShownName)}\" needs {need} more, and there {(options.Count == 1 ? "is" : "are")} {options.Count}.\n\n" +
                    "OK: go ahead and pay the rest yourself.\nCancel: don't activate it.",
                    "Playtest", MessageBoxButton.OKCancel, MessageBoxImage.Information);
                if (r != MessageBoxResult.OK) return;
                paid.AddRange(options.Select(o => o.Id));
                paid.AddRange(Enumerable.Repeat(0, need - options.Count));        // the rest: yours
                ChooseCosts(c, a, index + 1, paid, done);
                return;
            }
            string verb = choice.Kind switch
            {
                ChoiceKind.Sacrifice => "Sacrifice", ChoiceKind.Tap => "Tap", ChoiceKind.ExileFromGraveyard => "Exile",
                ChoiceKind.Discard => "Discard", ChoiceKind.ExileFromBattlefield => "Exile",
                ChoiceKind.RemoveCounter => "Remove a counter from", ChoiceKind.PutCounter => "Put a counter on", _ => "Return",
            };
            var m = new ContextMenu { Placement = PlacementMode.MousePoint };
            m.Items.Add(new MenuItem { Header = $"{c.ShownName}: {verb} {(need == 1 ? "one" : $"{need} more")} ({choice.Text.Replace("~", c.ShownName)})", IsEnabled = false });
            foreach (var o in options.Take(40))
            {
                var pick = o;
                var mi = new MenuItem { Header = pick.ShownName + (pick.Tapped ? " (tapped)" : "") + (pick.IsToken ? " (token)" : "") };
                mi.MouseEnter += (_, _) => ShowInPanel(pick);
                mi.Click += (_, _) => { paid.Add(pick.Id); ChooseCosts(c, a, index + 1, paid, done); };
                m.Items.Add(mi);
            }
            m.Items.Add(new Separator());
            m.Items.Add(Item("Cancel", () => { }));
            m.IsOpen = true;
        }

        // ── Several targets ──────────────────────────────────────────────
        /// <summary>Start picking several targets: up to the most it allows, damage divided as you say.</summary>
        private void StartSpread(GameCard c, Ability a, int x, List<int> paid, Effect sp)
        {
            string[] parts = sp.What.Split(':');
            int most = parts.Length > 1 && int.TryParse(parts[1], out int k) ? k : 1;
            _spreadMax = most < 0 ? Math.Max(1, x) : most;                    // "Tap X target creatures"
            _spreadDivided = sp.What.StartsWith("divided:");
            _spreadLeft = _spreadDivided ? sp.Amount(x) : 0;
            _spreadPlayers = sp.On == EffectOn.AnyTarget;
            _spread = new List<(int Target, int Amount)>();
            _pendingAbility = (a, x, paid);
            if (_spreadDivided && _spreadLeft <= 0) { FinishSpread(c); return; }
            NextSpread(c);
        }

        /// <summary>The next target: a menu (a creature, you, an opponent, Done) when players count, else a click on the table.</summary>
        private void NextSpread(GameCard c)
        {
            if (!_spreadPlayers) { StartPick(c, PickKind.Ability); if (_pick == null) FinishSpread(c); return; }
            var m = new ContextMenu { Placement = PlacementMode.MousePoint };
            m.Items.Add(new MenuItem
            {
                Header = $"{c.ShownName}: target {_spread!.Count + 1}" + (_spreadMax < 99 ? $" of up to {_spreadMax}" : "") +
                         (_spreadDivided ? $" ({_spreadLeft} damage left)" : ""),
                IsEnabled = false,
            });
            m.Items.Add(Item("A creature or planeswalker (click it)…", () => StartPick(c, PickKind.Ability)));
            if (!_spread.Any(t => t.Target == -1)) m.Items.Add(Item("You", () => AddSpreadTarget(c, -1)));
            if (!_spread.Any(t => t.Target == -2)) m.Items.Add(Item(Opp is { } other ? other.Name : "An opponent", () => AddSpreadTarget(c, -2)));
            m.Items.Add(new Separator());
            m.Items.Add(Item("Done", () => FinishSpread(c), enabled: _spread.Count > 0));
            m.Items.Add(Item("Cancel", () => { _spread = null; _pendingAbility = null; Render(); }));
            m.IsOpen = true;
        }

        private string TargetName(int id) => id == -1 ? "you" : id == -2 ? (Opp?.Name ?? "an opponent") : _game.FindAny(id)?.ShownName ?? "that";

        /// <summary>One more target (with its share, when the damage is divided); the last one finishes.</summary>
        private void AddSpreadTarget(GameCard c, int id)
        {
            if (_spread == null) return;
            if (_spread.Any(t => t.Target == id)) { NextSpread(c); return; }             // each target once
            int amount = 0;
            if (_spreadDivided)
            {
                bool last = _spread.Count + 1 >= _spreadMax;
                string? s = BreakersOfE.Views.Dialogs.TextPromptDialog.Ask(this, c.ShownName,
                    $"How much of the {_spreadLeft} damage to {TargetName(id)}?", (last ? _spreadLeft : 1).ToString());
                if (s == null || !int.TryParse(s.Trim(), out amount) || amount <= 0) { NextSpread(c); return; }
                amount = Math.Min(amount, _spreadLeft);
                _spreadLeft -= amount;
            }
            _spread.Add((id, amount));
            if (_spread.Count >= _spreadMax || (_spreadDivided && _spreadLeft <= 0)) FinishSpread(c);
            else NextSpread(c);
        }

        /// <summary>Those are the targets: the damage left over (if any) goes to the last one, then it's done as one move.</summary>
        private void FinishSpread(GameCard c)
        {
            var list = _spread;
            var pa = _pendingAbility;
            _spread = null;
            _pendingAbility = null;
            _pick = null;
            if (list == null || pa is not { } p) { Render(); return; }
            if (_spreadDivided && _spreadLeft > 0 && list.Count > 0)
                list[^1] = (list[^1].Target, list[^1].Amount + _spreadLeft);
            int first = list.Select(t => t.Target).FirstOrDefault(t => t > 0);
            RunAbility(c, p.Ability, first, p.X, p.Paid, list);
        }

        private void PickAbilityTarget(GameCard c, Ability a, int x, List<int> paid)
        {
            _pendingAbility = (a, x, paid);
            StartPick(c, PickKind.Ability);
            if (_pick == null) _pendingAbility = null;                     // nothing to pick
        }

        /// <summary>Activate it, then do what's left: look at or search the library, discard, make a token by hand.</summary>
        private void RunAbility(GameCard c, Ability a, int target, int x, List<int>? paid = null,
                                List<(int Target, int Amount)>? spread = null, int target2 = 0)
        {
            var left = _game.Activate(Me, c, a, target, x, TokenFor, paid, card => FreeSpot(card.Lane), spread, target2);
            foreach (var f in left)
            {
                switch (f.Kind)
                {
                    case EffectKind.LookTop when f.Seat >= 0 && f.Seat < _game.Seats.Count:
                        // "the top N cards of target player's library": that player's
                        if (_game.Seats[f.Seat].Library.Count > 0) OpenBrowser(Zone.Library, Math.Max(1, f.N), _game.Seats[f.Seat]);
                        break;
                    case EffectKind.Scry:
                    case EffectKind.Surveil:
                    case EffectKind.LookTop:
                        if (Me.Library.Count > 0) OpenBrowser(Zone.Library, Math.Max(1, f.N));
                        break;
                    case EffectKind.Search:
                        if (Me.Library.Count > 0) OpenBrowser(Zone.Library);
                        break;
                    case EffectKind.FromGraveyard:
                        if (Me.Graveyard.Count > 0) OpenBrowser(Zone.Graveyard);
                        else _game.Note($"{c.ShownName}: your graveyard is empty ({f.Text}).");
                        break;
                    case EffectKind.Discard:
                        if (Me.Hand.Count > 0)
                        {
                            bool upTo = f.Text.Contains("up to", StringComparison.OrdinalIgnoreCase) || f.Text.Contains("any number", StringComparison.OrdinalIgnoreCase);
                            _discardDown = Math.Min(f.N, Me.Hand.Count);
                            _discardCount = 0;
                            _afterDiscard = null;
                            _discardWhy = upTo ? $"{c.ShownName}: click up to {_discardDown} card{(_discardDown == 1 ? "" : "s")} to discard, then Skip when you're done."
                                               : $"{c.ShownName}: click {_discardDown} card{(_discardDown == 1 ? "" : "s")} to discard.";
                            Render();
                        }
                        break;
                    case EffectKind.DrawThatMany when f.Text.StartsWith("If you do", StringComparison.OrdinalIgnoreCase):
                    {
                        // "You may discard a card. If you do, draw a card": the draw only once a card was discarded.
                        int draws = f.N;
                        if (_discardDown > 0) _afterDiscard = () => { if (_discardCount > 0) _game.Draw(Me, draws); else Render(); };
                        break;
                    }
                    case EffectKind.DrawThatMany:
                    {
                        int plus = f.N;
                        if (_discardDown > 0) _afterDiscard = () => { if (_discardCount + plus > 0) _game.Draw(Me, _discardCount + plus); else Render(); };
                        else if (plus > 0) _game.Draw(Me, plus);
                        break;
                    }
                    case EffectKind.PutBack:
                        if (Me.Hand.Count > 0)
                        {
                            _game.Note($"{c.ShownName}: put {f.N} card{(f.N == 1 ? "" : "s")} from your hand on top of your library (drag them onto the library, in the order you want).");
                            OpenBrowser(Zone.Hand);
                        }
                        break;
                    case EffectKind.Token:
                        _game.Note($"Make it yourself: {f.N} × {f.Token?.Describe} (Tokens ▾ → Any Token…): the card pool has no token that matches.");
                        break;
                    case EffectKind.Mana:
                        _game.Note($"{c.ShownName}: {f.Text}. Add it to the pool (click the colors beside your life).");
                        break;
                }
            }
            // Edicts with a choice: each player chooses what they sacrifice, one after the other.
            ChooseSacrifices(left.Where(f => f.Kind == EffectKind.OpponentSacrifices && f.Choices != null).ToList(), 0);
        }

        /// <summary>"Each opponent sacrifices a creature": the player picks which (a menu of the ones that qualify), then the next player.</summary>
        private void ChooseSacrifices(List<Game.AbilityFollowUp> asks, int index)
        {
            if (index >= asks.Count) return;
            var f = asks[index];
            var choices = f.Choices!.Select(id => _game.FindAny(id)).Where(o => o != null).Select(o => o!).ToList();
            if (choices.Count == 0 || f.Seat < 0 || f.Seat >= _game.Seats.Count) { ChooseSacrifices(asks, index + 1); return; }
            var who = _game.Seats[f.Seat];
            var m = new ContextMenu { Placement = PlacementMode.MousePoint };
            m.Items.Add(new MenuItem { Header = $"{(who.IsYou ? "You choose" : who.Name + " chooses")} what to sacrifice ({f.Text})", IsEnabled = false });
            foreach (var o in choices)
            {
                var pick = o;
                var mi = new MenuItem
                {
                    Header = pick.ShownName + (pick.PowerNow is int p && pick.ToughnessNow is int t ? $"  {p}/{t}" : "  (power varies: the table can't count it)") +
                             (pick.IsToken ? " (token)" : ""),
                };
                mi.Click += (_, _) =>
                {
                    _game.Move(pick, Zone.Graveyard);
                    if (f.N > 1)
                    {
                        var rest = f with { N = f.N - 1, Choices = f.Choices!.Where(id => id != pick.Id).ToList() };
                        asks[index] = rest;
                        ChooseSacrifices(asks, index);
                    }
                    else ChooseSacrifices(asks, index + 1);
                };
                m.Items.Add(mi);
            }
            m.Items.Add(new Separator());
            m.Items.Add(Item("They'll do it themselves", () => ChooseSacrifices(asks, index + 1)));
            m.IsOpen = true;
        }

        private static readonly (string Word, char Letter)[] ColorLetters =
            { ("white", 'W'), ("blue", 'U'), ("black", 'B'), ("red", 'R'), ("green", 'G') };

        /// <summary>
        /// The token an ability makes: the deck's Tokens part first, then any
        /// token in the card pool, matched by its types, power/toughness,
        /// colors and "with …". Null when nothing matches.
        /// </summary>
        private (DeckCard Card, string Layout, double X, double Y)? TokenFor(TokenSpec spec)
        {
            var types = spec.Types.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                            .Where(w => !w.Equals("creature", StringComparison.OrdinalIgnoreCase) && !w.Equals("artifact", StringComparison.OrdinalIgnoreCase) &&
                                        !w.Equals("enchantment", StringComparison.OrdinalIgnoreCase) && !w.Equals("legendary", StringComparison.OrdinalIgnoreCase))
                            .ToList();
            if (types.Count == 0) types = spec.Types.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            string colors = string.Concat(ColorLetters.Where(cl => spec.Colors.Contains(cl.Word)).Select(cl => cl.Letter));

            int Score(string name, string typeLine, string power, string toughness, string cardColors, string text)
            {
                string all = name + " " + typeLine;
                if (!types.All(t => all.Contains(t, StringComparison.OrdinalIgnoreCase))) return -1;
                if (spec.Power.Length > 0 && spec.Power != "X" && (power != spec.Power || toughness != spec.Toughness)) return -1;
                int score = 10;
                string have = string.Concat((cardColors ?? "").ToUpperInvariant().Where(ch => "WUBRG".Contains(ch)).Distinct().OrderBy(ch => ch));
                if (have == string.Concat(colors.OrderBy(ch => ch))) score += 5; else if (colors.Length > 0) return -1;
                if (spec.With.Length > 0) score += text.Contains(spec.With.Split(' ')[0], StringComparison.OrdinalIgnoreCase) ? 4 : -3;
                else if (text.Trim().Length == 0 || text.StartsWith("(")) score += 2;                 // a plain token for a plain description
                return score;
            }

            DeckCard? best = null;
            string layout = "";
            int bestScore = -1;
            foreach (var t in Me.Tokens)
            {
                int s = Score(t.Name, t.TypeLine, t.Power, t.Toughness, t.ColorIdentity, t.OracleText) + 3;   // the deck's own first
                if (s > bestScore) { bestScore = s; best = t; layout = MyLayouts.GetValueOrDefault(t.ScryfallId ?? "", ""); }
            }
            if (best == null || bestScore < 13)
                foreach (var t in PlaytestDecks.AllTokens())
                {
                    int s = Score(t.Name, t.TypeLine, t.Power, t.Toughness, t.Colors, t.OracleText);
                    if (s > bestScore) { bestScore = s; best = Services.DeckService.FromTokenCard(t); layout = t.Layout ?? ""; }
                }
            if (best == null) return null;
            var (px, py) = FreeSpot(LaneOf(best));
            return (best, layout, px, py);
        }
    }
}
