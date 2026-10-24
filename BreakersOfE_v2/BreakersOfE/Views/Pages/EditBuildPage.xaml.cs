using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BreakersOfE.DeckBuilder;
using BreakersOfE.Services;
using static BreakersOfE.Views.Pages.EditPageKit;

namespace BreakersOfE.Views.Pages
{
    /// <summary>
    /// Edit → Decks → Build. What can I build?, the constraints (commander and
    /// an optional partner / Background, bracket, sources, spend limit, deck
    /// value cap, guide), Build Draft (DeckBuildService → the engine, with
    /// Cancel) or Guided Build (step by step), the draft with a reason per
    /// card, Lock / Exclude and rebuild, the Rule 0 sheet, Statistics &amp; Test
    /// Hands on the draft, and Accept (a new deck, free copies claimed, the
    /// rest on the Want List for it).
    /// </summary>
    public partial class EditBuildPage : Page
    {
        /// <summary>Every commander choice (Backgrounds too: they're only second commanders).</summary>
        private List<DeckBuildService.CommanderChoice> _all = new();
        /// <summary>The first commander's list (no Backgrounds).</summary>
        private List<DeckBuildService.CommanderChoice> _commanders = new();
        private readonly List<GuideRow> _guideCommander = new();
        private readonly List<GuideRow> _guideConstructed = new();
        /// <summary>The guide shown: Commander's or the 60-card one (each keeps its own numbers).</summary>
        private List<GuideRow> _guide => IsConstructed ? _guideConstructed : _guideCommander;
        /// <summary>60-card: copies of a locked card (from the draft or Guided Build).</summary>
        private readonly Dictionary<string, int> _lockedCopies = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>60-card: the key cards to build around.</summary>
        private readonly System.Collections.ObjectModel.ObservableCollection<string> _keyCards = new();
        /// <summary>The draft's 60-card format key (null: a Commander draft).</summary>
        private string? _draftFormat;
        private readonly HashSet<string> _locked = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _excluded = new(StringComparer.OrdinalIgnoreCase);
        private DraftDeck? _draft;
        private BuildConstraints? _draftConstraints;
        private List<string> _draftNames = new();
        private string _draftCommander = "";
        private bool _busy;
        private CancellationTokenSource? _cts;

        private bool IsConstructed => ShapeBox?.SelectedIndex == 1;
        private Models.ConstructedFormat Format => FormatBox.SelectedItem as Models.ConstructedFormat ?? Models.DeckFormats.ConstructedFormats[0];

        public EditBuildPage()
        {
            InitializeComponent();
            RememberSizes(this, new RowDefinition(), new RowDefinition(), DetailColumn);   // just the card panel width (no table split here)
            foreach (var t in RoleGuide.CommanderDefault().Targets)
                _guideCommander.Add(new GuideRow { Role = t.Role, Count = t.Count, Enabled = t.Enabled });
            foreach (var t in RoleGuide.ConstructedDefault().Targets)
                _guideConstructed.Add(new GuideRow { Role = t.Role, Count = t.Count, Enabled = t.Enabled });
            GuideList.ItemsSource = _guideCommander;
            FormatBox.ItemsSource = Models.DeckFormats.ConstructedFormats;
            FormatBox.SelectedIndex = 0;
            KeyCardList.ItemsSource = _keyCards;
            CommanderBox.SelectionChanged += (_, _) => ShowCommanderInfo();
            PartnerBox.SelectionChanged += (_, _) => ShowPartnerInfo();
            // Esc stops a build.
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape && _busy && _cts != null) { _cts.Cancel(); e.Handled = true; }
            };
            ShowLocks();
        }

        /// <summary>Open the page: fill the commander list (once).</summary>
        public async void Start()
        {
            ShowStatus("", false);
            if (_all.Count > 0) return;
            CommanderInfo.Text = "Loading commanders…";
            try
            {
                _all = await Task.Run(DeckBuildService.CommanderChoices);
                _commanders = _all.Where(c => !c.IsBackground).ToList();
                CommanderBox.ItemsSource = _commanders;
                CommanderBox.DisplayMemberPath = nameof(DeckBuildService.CommanderChoice.Display);
                CommanderBox.SetValue(TextSearch.TextPathProperty, nameof(DeckBuildService.CommanderChoice.Name));
                PartnerBox.DisplayMemberPath = nameof(DeckBuildService.CommanderChoice.Display);
                PartnerBox.SetValue(TextSearch.TextPathProperty, nameof(DeckBuildService.CommanderChoice.Name));
                CommanderInfo.Text = $"{_commanders.Count(c => c.IsFree):N0} with a free copy, {_commanders.Count(c => c.Owned > 0 && !c.IsFree):N0} yours but in use, {_commanders.Count:N0} in all.";
            }
            catch (Exception ex)
            {
                CommanderInfo.Text = $"Couldn't read the card pool: {ex.Message}";
            }
        }

        private string CommanderName =>
            (CommanderBox.SelectedItem as DeckBuildService.CommanderChoice)?.Name ?? (CommanderBox.Text ?? "").Trim();

        private DeckBuildService.CommanderChoice? Partner =>
            PartnerPanel.Visibility == Visibility.Visible ? PartnerBox.SelectedItem as DeckBuildService.CommanderChoice : null;

        private void ShowCommanderInfo()
        {
            if (CommanderBox.SelectedItem is not DeckBuildService.CommanderChoice c) return;
            CommanderInfo.Text = CommanderText(c);
            Detail.ShowCard(DeckBuildService.PoolCardByName(c.Name));

            // A second commander: only the cards that can pair with this one.
            if (!c.CanHaveSecond)
            {
                PartnerPanel.Visibility = Visibility.Collapsed;
                PartnerBox.SelectedItem = null;
                return;
            }
            var keep = PartnerBox.SelectedItem as DeckBuildService.CommanderChoice;
            var list = _all.Where(x => c.CanPairWith(x)).ToList();
            PartnerBox.ItemsSource = list;
            PartnerBox.SelectedItem = keep != null ? list.FirstOrDefault(x => x.Name == keep.Name) : null;
            PartnerLabel.Text = $"Second commander (optional) — {Partners.Describe(c.TypeLine, c.OracleText)}";
            PartnerPanel.Visibility = Visibility.Visible;
            if (PartnerBox.SelectedItem == null)
                PartnerInfo.Text = list.Count == 0 ? "No card in the pool pairs with it."
                    : $"{list.Count:N0} card{(list.Count == 1 ? "" : "s")} can pair with it ({list.Count(x => x.IsFree):N0} free in your collection).";
        }

        private void ShowPartnerInfo()
        {
            if (PartnerBox.SelectedItem is not DeckBuildService.CommanderChoice p) return;
            PartnerInfo.Text = CommanderText(p);
            Detail.ShowCard(DeckBuildService.PoolCardByName(p.Name));
        }

        private void BtnNoPartner_Click(object sender, RoutedEventArgs e)
        {
            PartnerBox.SelectedItem = null;
            PartnerBox.Text = "";
            ShowCommanderInfo();
        }

        /// <summary>Is the commander yours to use: a free copy, or in use (which deck), or not owned.</summary>
        private static string CommanderText(DeckBuildService.CommanderChoice c)
        {
            if (c.IsFree) return c.Free == 1 ? "You have a free copy." : $"You have {c.Free} free copies.";
            if (c.Owned > 0)
            {
                var decks = DeckIndexService.DecksUsing(c.Name).Select(d => d.DeckName).Take(3).ToList();
                return $"Your {(c.Owned == 1 ? "copy is" : c.Owned + " copies are")} in use" +
                       (decks.Count > 0 ? $" ({string.Join(", ", decks)})" : "") +
                       ". Free one there, or choose \"My collection + cards to get\" to build it anyway (it goes on the Want List).";
            }
            return "You don't own it. Choose \"My collection + cards to get\" to build it (it goes on the Want List).";
        }

        /// <summary>
        /// What can I build?: Commander — your commanders and partner pairs, ranked;
        /// 60-card — color combinations for the format. Build This One picks it here.
        /// </summary>
        private void BtnIdeas_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            var w = IsConstructed
                ? new Dialogs.WhatCanIBuildWindow(Window.GetWindow(this), (Format.Key, Format.Name))
                : new Dialogs.WhatCanIBuildWindow(Window.GetWindow(this));
            if (w.ShowDialog() != true || w.Chosen is not { } idea) return;
            if (idea.IsConstructed)
            {
                foreach (char ch in "WUBRG")
                    if (ColorCheck(ch) is { } box) box.IsChecked = idea.ColorLetters.Contains(ch);
                if (idea.ColorLetters.Length == 0) ColorN.IsChecked = true;
                ShowStatus($"{idea.Name} ticked ({idea.EdhrecText}). Add key cards or plan words, then Build Draft.", false);
                return;
            }
            var choice = _commanders.FirstOrDefault(c => c.Name.Equals(idea.Commander.Name, StringComparison.OrdinalIgnoreCase));
            if (choice == null) { ShowStatus($"{idea.Commander.Name} isn't in the commander list yet. Try again in a moment.", true); return; }
            CommanderBox.SelectedItem = choice;                           // fills the partner list
            if (idea.Partner is { } partner && PartnerBox.ItemsSource is IEnumerable<DeckBuildService.CommanderChoice> list)
                PartnerBox.SelectedItem = list.FirstOrDefault(c => c.Name.Equals(partner.Name, StringComparison.OrdinalIgnoreCase));
            ShowStatus($"{idea.Name} picked. Set the bracket and sources, then Build Draft or Guided Build.", false);
        }

        private void Sources_Changed(object sender, RoutedEventArgs e)
        {
            if (SpendPanel != null) SpendPanel.IsEnabled = SourcePool.IsChecked == true;
        }

        // ══════════════════════════════════════════════════════════════════
        // BUILD
        // ══════════════════════════════════════════════════════════════════
        private static decimal Money(string text, decimal fallback) =>
            decimal.TryParse((text ?? "").Trim().TrimStart('$'), NumberStyles.Number, CultureInfo.CurrentCulture, out var v) && v >= 0 ? v : fallback;

        private BuildConstraints Constraints(IReadOnlyList<string> names)
        {
            var guide = new RoleGuide { Targets = _guide.Select(g => new RoleTarget { Role = g.Role, Count = Math.Max(0, g.Count), Enabled = g.Enabled }).ToList() };
            var sources = SourcePool.IsChecked == true ? SourceMode.CollectionAndPool : SourceMode.CollectionOnly;
            decimal spend = SourcePool.IsChecked == true ? Money(SpendBox.Text, 0) : 0;
            decimal? cap = ValueCapCheck.IsChecked == true ? Money(ValueCapBox.Text, 100) : null;
            bool useGuide = UseGuideCheck.IsChecked == true;
            int swaps = int.TryParse((SwapBox.Text ?? "").Trim(), out int sw) ? Math.Clamp(sw, 0, 30) : 12;
            SwapBox.Text = swaps.ToString();
            if (!IsConstructed)
                return new BuildConstraints
                {
                    Commanders = names, Bracket = BracketBox.SelectedIndex + 1, Sources = sources, SpendLimit = spend,
                    DeckValueCap = cap, Locked = _locked.ToList(), Excluded = _excluded.ToList(), UseGuide = useGuide, Guide = guide,
                    SwapOptions = swaps,
                };
            // 60-card: the colors ticked, the key cards locked (as many copies as allowed), the curve for the spell slots.
            int lands = useGuide && guide.For(Role.Land) is { Enabled: true } lt ? lt.Count : 24;
            string curve = (CurveBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Medium";
            return new BuildConstraints
            {
                Shape = DeckShape.Constructed, DeckSize = 60, ColorIdentity = Colors(),
                Bracket = PowerBox.SelectedIndex == 1 ? 4 : 3, Sources = sources, SpendLimit = spend, DeckValueCap = cap,
                Locked = _locked.Concat(_keyCards).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                LockedCopies = new Dictionary<string, int>(_lockedCopies, StringComparer.OrdinalIgnoreCase),
                Excluded = _excluded.ToList(), UseGuide = useGuide, Guide = guide,
                PlanWords = (PlanWordsBox.Text ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                Curve = curve.Length == 0 ? null : BuildConstraints.CurvePreset(curve, Math.Max(0, 60 - lands)),
                SwapOptions = swaps,
                AllowMulticolor = ColorM.IsChecked == true,
                AllowColorless = ColorN.IsChecked == true,
            };
        }

        // ══════════════════════════════════════════════════════════════════
        // 60-CARD: deck type, colors, key cards
        // ══════════════════════════════════════════════════════════════════
        private void ShapeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ConstructedSection == null || CommanderSection == null || GuideList == null) return;   // still loading
            bool k = IsConstructed;
            CommanderSection.Visibility = k ? Visibility.Collapsed : Visibility.Visible;
            ConstructedSection.Visibility = k ? Visibility.Visible : Visibility.Collapsed;
            BtnRule0.Visibility = k ? Visibility.Collapsed : Visibility.Visible;
            GuideList.ItemsSource = _guide;
            if (_locked.Count > 0 || _excluded.Count > 0)
            {
                _locked.Clear(); _excluded.Clear(); _lockedCopies.Clear();
                ShowLocks();
                ShowStatus("Locks and excludes cleared: they were for the other deck type.", false);
            }
        }

        private CheckBox? ColorCheck(char ch) => ch switch
        {
            'W' => ColorW, 'U' => ColorU, 'B' => ColorB, 'R' => ColorR, 'G' => ColorG, _ => null,
        };

        /// <summary>The colors ticked ("WUBRG" order; "" = colorless).</summary>
        private string Colors() => new string("WUBRG".Where(ch => ColorCheck(ch)?.IsChecked == true).ToArray());

        private void BtnAddKey_Click(object sender, RoutedEventArgs e) => AddKeyCard();

        private void KeyCardBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            _ = AddKeyCard();
            e.Handled = true;
        }

        /// <summary>A key card: found by name in the pool, listed, its colors ticked. False (and said why) when there's no such card.</summary>
        private bool AddKeyCard()
        {
            string typed = (KeyCardBox.Text ?? "").Trim();
            if (typed.Length == 0) return true;
            var card = DeckBuildService.FindCardByName(typed);
            if (card == null) { ShowStatus($"No card named \"{typed}\" in the card pool. Check the name in the Key cards box.", true); return false; }
            if (!_keyCards.Contains(card.Name, StringComparer.OrdinalIgnoreCase)) _keyCards.Add(card.Name);
            foreach (char ch in (card.ColorIdentity ?? "").ToUpperInvariant())
                if (ColorCheck(ch) is { } box) box.IsChecked = true;
            _excluded.Remove(card.Name);
            ShowLocks();
            Detail.ShowCard(card);
            KeyCardBox.Text = "";
            ShowStatus($"{card.Name} is a key card: as many copies as {Format.Name} allows, and its text sets the plan.", false);
            return true;
        }

        private void RemoveKey_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is string name) _keyCards.Remove(name);
        }

        /// <summary>The commander(s) to build for, checked: in the list, and free when only my collection is allowed. Null (and said why) when not.</summary>
        private List<string>? CheckedCommanders()
        {
            if (IsConstructed)
            {
                // A name still in the Key cards box counts (as if Add was clicked).
                if (!AddKeyCard()) return null;
                if (Colors().Length == 0 && _keyCards.Count == 0 && ColorN.IsChecked != true)
                {
                    ShowStatus("Tick the deck's colors (or N for a colorless deck, or add a key card) first.", true);
                    return null;
                }
                return new List<string>();                           // no commander
            }
            string name = CommanderName;
            if (name.Length == 0) { ShowStatus("Pick a commander first.", true); return null; }
            if (_commanders.Count > 0 && !_commanders.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                ShowStatus($"\"{name}\" isn't a legal commander in the card pool. Pick one from the list.", true);
                return null;
            }
            var names = new List<string> { name };
            var picks = new List<DeckBuildService.CommanderChoice?> { _commanders.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) };
            string typedPartner = (PartnerBox.Text ?? "").Trim();
            if (PartnerPanel.Visibility == Visibility.Visible && Partner == null && typedPartner.Length > 0)
            {
                ShowStatus($"\"{typedPartner}\" can't be a second commander with {name}. Pick one from its list, or ✕ for none.", true);
                return null;
            }
            if (Partner is { } p) { names.Add(p.Name); picks.Add(p); }
            // Only my collection: the commanders have to be free copies too.
            if (SourcePool.IsChecked != true)
                foreach (var choice in picks)
                    if (choice != null && !choice.IsFree)
                    {
                        ShowStatus($"{choice.Name}: " + CommanderText(choice), true);
                        return null;
                    }
            return names;
        }

        private async void BtnBuild_Click(object sender, RoutedEventArgs e) => await BuildAsync();

        private async Task BuildAsync()
        {
            if (_busy) return;
            var names = CheckedCommanders();
            if (names == null) return;
            var constraints = Constraints(names);
            string? format = IsConstructed ? Format.Key : null;
            ShowStatus("Building…", false);
            var result = await RunAsync(async (ct, progress) =>
            {
                var ctx = await DeckBuildService.PrepareAsync(names, constraints, ct, progress, format ?? "commander");
                progress.Report("Picking the cards…");
                return new BuildResult(ctx, DeckBuildService.Build(ctx, ctx.Constraints, ct));
            });
            if (result != null) ShowDraft(result.Run, result.Ctx.Constraints, names, format);
        }

        /// <summary>Guided Build: gather the cards (cancelable), step through the roles, then build around what was chosen.</summary>
        private async void BtnGuided_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            var names = CheckedCommanders();
            if (names == null) return;
            var constraints = Constraints(names);
            string? format = IsConstructed ? Format.Key : null;
            ShowStatus("Getting the cards ready for Guided Build…", false);
            var ctx = await RunAsync((ct, progress) => DeckBuildService.PrepareAsync(names, constraints, ct, progress, format ?? "commander"));
            if (ctx == null) return;

            var w = new Dialogs.GuidedBuildWindow(ctx, Window.GetWindow(this));
            if (w.ShowDialog() != true || w.Chosen == null)
            {
                ShowStatus("Guided Build closed. Nothing changed.", false);
                return;
            }
            foreach (var n in w.Chosen) { _locked.Add(n); _excluded.Remove(n); }
            if (IsConstructed)
                foreach (var kv in w.ChosenCopies) _lockedCopies[kv.Key] = kv.Value;
            ShowLocks();
            var locked = _locked.Concat(_keyCards).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var copies = new Dictionary<string, int>(_lockedCopies, StringComparer.OrdinalIgnoreCase);
            var run = await RunAsync((ct, _) => Task.FromResult(DeckBuildService.Build(ctx, ctx.Constraints.WithLocked(locked, copies), ct)));
            if (run != null)
            {
                ShowDraft(run, ctx.Constraints, names, format);
                ShowStatus($"Built around your {w.Chosen.Count} choices (locked — Clear locks to start over). " + ActionText.Text, false);
            }
        }

        private sealed record BuildResult(BuildContext Ctx, BuildRun Run);

        /// <summary>
        /// Run work off the screen's thread with the busy panel, progress and
        /// Cancel (or Esc). Cancel returns at once (the work's result is
        /// dropped when it finishes); the last draft stays. Null when canceled
        /// or failed (said so).
        /// </summary>
        private async Task<T?> RunAsync<T>(Func<CancellationToken, IProgress<string>, Task<T>> work) where T : class
        {
            var cts = _cts = new CancellationTokenSource();
            _busy = true;
            SetBusy(true);
            var progress = new Progress<string>(step => { if (!cts.IsCancellationRequested) BusyText.Text = step; });
            try
            {
                var task = Task.Run(() => work(cts.Token, progress));
                var canceled = new TaskCompletionSource<bool>();
                using (cts.Token.Register(() => canceled.TrySetResult(true)))
                {
                    if (await Task.WhenAny(task, canceled.Task) != task)
                    {
                        _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                        throw new OperationCanceledException(cts.Token);
                    }
                }
                return await task;
            }
            catch (OperationCanceledException)
            {
                ShowStatus(_draft != null ? "Canceled. The last draft is still shown." : "Canceled.", false);
                return null;
            }
            catch (Exception ex)
            {
                ShowStatus($"Couldn't build: {ex.Message}", true);
                return null;
            }
            finally
            {
                if (_cts == cts) _cts = null;
                cts.Dispose();
                _busy = false;
                SetBusy(false);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (_cts == null) return;
            BusyText.Text = "Canceling…";
            _cts.Cancel();
        }

        /// <summary>Working: a moving bar with what it's doing and Cancel, the choices and buttons locked, the busy cursor.</summary>
        private void SetBusy(bool busy)
        {
            BusyPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            ChoicesPanel.IsEnabled = !busy;
            DraftGrid.IsEnabled = !busy;
            BtnAccept.IsEnabled = BtnStats.IsEnabled = BtnRule0.IsEnabled = BtnTokens.IsEnabled = !busy && _draft != null && _draft.Picks.Count > 0;
            Mouse.OverrideCursor = busy ? Cursors.AppStarting : null;
            if (busy) BusyText.Text = "Starting…";
        }

        private void ShowDraft(BuildRun run, BuildConstraints constraints, List<string> names, string? format)
        {
            var d = run.Draft;
            _draft = d;
            _draftConstraints = constraints;
            _draftNames = names;
            _draftFormat = format;
            // A 60-card draft is named by its format and colors ("Modern RG").
            _draftCommander = format != null
                ? $"{Models.DeckFormats.Constructed(format).Name} {(constraints.ColorIdentity.Length > 0 ? constraints.ColorIdentity : "Colorless")}"
                : string.Join(" & ", names);
            DraftHeadline.Text = $"Draft: {d.CardCount} cards · worth ${d.Value:N2} · to spend ${d.ToSpend:N2}" +
                                 (format == null && d.GameChangers > 0 ? $" · {d.GameChangers} Game Changer{(d.GameChangers == 1 ? "" : "s")}" : "");
            RoleChips.Children.Clear();
            foreach (var r in d.Roles)
            {
                bool shortOf = r.Enabled && r.Got < r.Target;
                var chip = new Border
                {
                    CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 0, 6, 4),
                    Background = Models.LegalityInfo.BackgroundBrush(shortOf ? "banned" : r.Enabled ? "legal" : ""),
                    Child = new TextBlock
                    {
                        Text = r.Text, FontSize = 12,
                        Foreground = Models.LegalityInfo.ForegroundBrush(shortOf ? "banned" : r.Enabled ? "legal" : ""),
                    },
                };
                RoleChips.Children.Add(chip);
            }
            var notes = new List<string>(d.Notes);
            if (d.Combos.Count > 0)
                notes.Add($"Combos in the draft: {string.Join("; ", d.Combos.Take(4).Select(k => string.Join(" + ", k.Cards) + (k.Results.Length > 0 ? $" ({k.Results})" : "")))}" +
                          (d.Combos.Count > 4 ? $" and {d.Combos.Count - 4} more" : "") + ".");
            notes.Add($"Used: {string.Join(" · ", run.Sources)}. {run.EligibleCount:N0} cards to choose from.");
            DraftNotes.Text = string.Join("\n", notes);

            // The deck, then the swap options (marked "Swap" in Source).
            DraftGrid.ItemsSource = d.Picks.Select(p => new DraftRow(p)).Concat(d.SwapOptions.Select(p => new DraftRow(p, swap: true))).ToList();
            BtnAccept.IsEnabled = BtnStats.IsEnabled = BtnRule0.IsEnabled = BtnTokens.IsEnabled = d.Picks.Count > 0;
            int toGet = d.CardsToGet;
            ShowStatus(toGet > 0 ? $"{toGet} card(s) to get. Accept puts them on the Want List for the new deck." : "Every card comes from your collection.", false);
        }

        // ══════════════════════════════════════════════════════════════════
        // LOCK / EXCLUDE
        // ══════════════════════════════════════════════════════════════════
        private List<DraftRow> Selected => DraftGrid.SelectedItems.OfType<DraftRow>().Where(r => !r.IsCommander && !r.IsBasic).ToList();

        private void Lock_Click(object sender, RoutedEventArgs e)
        {
            var rows = Selected;
            if (rows.Count == 0) { ShowStatus("Select cards in the draft (not the commander or basic lands).", true); return; }
            foreach (var r in rows)
            {
                _locked.Add(r.Name); _excluded.Remove(r.Name);
                if (IsConstructed) _lockedCopies[r.Name] = r.Copies;      // 60-card: with its copies
            }
            ShowLocks();
            ShowStatus($"Locked {rows.Count}: they stay in every rebuild{(IsConstructed ? " (with their copies)" : "")}.", false);
        }

        private async void Exclude_Click(object sender, RoutedEventArgs e)
        {
            var rows = Selected;
            if (rows.Count == 0) { ShowStatus("Select cards in the draft (not the commander or basic lands).", true); return; }
            foreach (var r in rows) { _excluded.Add(r.Name); _locked.Remove(r.Name); _lockedCopies.Remove(r.Name); _keyCards.Remove(r.Name); }
            ShowLocks();
            await BuildAsync();
        }

        private void BtnClearLocks_Click(object sender, RoutedEventArgs e)
        {
            _locked.Clear();
            _excluded.Clear();
            _lockedCopies.Clear();
            ShowLocks();
        }

        private void ShowLocks()
        {
            var parts = new List<string>();
            if (_locked.Count > 0)
                parts.Add($"Locked ({_locked.Count}): {string.Join(", ", _locked.Take(6).Select(n => IsConstructed && _lockedCopies.TryGetValue(n, out int k) ? $"{k}× {n}" : n))}{(_locked.Count > 6 ? "…" : "")}");
            if (_excluded.Count > 0) parts.Add($"Excluded ({_excluded.Count}): {string.Join(", ", _excluded.Take(6))}{(_excluded.Count > 6 ? "…" : "")}");
            LocksText.Text = parts.Count == 0 ? "Right-click cards in the draft to lock or exclude them." : string.Join("\n", parts);
            BtnClearLocks.Visibility = parts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ══════════════════════════════════════════════════════════════════
        // CARD PANEL, STATISTICS, ACCEPT
        // ══════════════════════════════════════════════════════════════════
        private readonly CardPopup _popup = new();

        /// <summary>Double-click a draft card (or the card panel's picture): the larger card window, Prev / Next through the draft.</summary>
        private void DraftGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ItemsControl.ContainerFromElement(DraftGrid, e.OriginalSource as DependencyObject) is not DataGridRow row) return;
            if (row.Item is DraftRow r) OpenCardWindow(r);
        }

        private void Detail_ImageDoubleClicked(object? sender, EventArgs e)
        {
            if (DraftGrid.SelectedItem is DraftRow r) OpenCardWindow(r);
        }

        private void OpenCardWindow(DraftRow r)
        {
            if (DraftGrid.ItemsSource is not List<DraftRow> rows) return;
            var names = rows.Select(x => x.Name).ToList();
            _popup.Open(Window.GetWindow(this), names, rows.IndexOf(r), name =>
            {
                var next = rows.FirstOrDefault(x => x.Name == name);
                if (next == null) return;
                DraftGrid.SelectedItem = next;
                DraftGrid.ScrollIntoView(next);
            });
        }

        private void DraftGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DraftGrid.SelectedItem is DraftRow row)
                Detail.ShowCard(DeckBuildService.PoolCardByName(row.Name));
        }

        private void BtnStats_Click(object sender, RoutedEventArgs e)
        {
            if (_draft == null) return;
            try
            {
                var deck = DeckBuildService.ToDeck(_draft, $"Draft — {_draftCommander}", _draftFormat);
                new DeckStatsWindow(deck, Window.GetWindow(this), _draftFormat).Show();
            }
            catch (Exception ex)
            {
                ShowStatus($"Couldn't open Statistics: {ex.Message}", true);
            }
        }

        private void BtnRule0_Click(object sender, RoutedEventArgs e)
        {
            if (_draft == null || _draftConstraints == null || _draftFormat != null) return;
            string name = $"{string.Join(" & ", _draftNames.Select(n => n.Split(',')[0]))} (draft)";
            new Dialogs.Rule0Window(Rule0.Sheet(_draft, _draftConstraints, name), name, Window.GetWindow(this)).Show();
        }

        private void BtnAccept_Click(object sender, RoutedEventArgs e)
        {
            if (_draft == null) return;
            string suggested = _draftFormat != null ? $"{_draftCommander} — Built"
                : $"{string.Join(" & ", _draftNames.Select(n => n.Split(',')[0]))} — Built";
            var name = Dialogs.TextPromptDialog.Ask(Window.GetWindow(this), "Accept the Draft",
                "Name for the new deck. Your free copies are claimed for it; cards you don't have go on the Want List for it.",
                suggested);
            if (string.IsNullOrWhiteSpace(name)) return;
            try
            {
                var (deck, msg, warn) = DeckBuildService.Accept(_draft, name.Trim(), _draftFormat);
                ShowStatus(msg + "  Find it in Edit → Decks.", warn);
                // The tokens its cards make: the same Suggested Tokens window as Edit → Decks (print, or add them).
                string tokens = AddTokensAfterAccept(deck);
                if (tokens.Length > 0) ShowStatus(msg + "  " + tokens + "  Find it in Edit → Decks.", warn);
            }
            catch (Exception ex)
            {
                ShowStatus($"Couldn't create the deck: {ex.Message}", true);
            }
        }

        /// <summary>The deck's cards that make tokens (main deck and commander; not the swap options).</summary>
        private static List<Models.DeckCard> TokenMakers(Models.Deck deck) =>
            deck.Cards.Where(c => !c.IsTokenLine && c.Category != Models.DeckCardCategory.Sideboard).ToList();

        /// <summary>After Accept: Suggested Tokens for the new deck; the ticked ones are added (owned copies claimed). "" when none.</summary>
        private string AddTokensAfterAccept(Models.Deck deck)
        {
            try
            {
                var list = TokenSuggestionService.Suggest(deck, TokenMakers(deck), out _);
                if (list.Count == 0) return "";
                var dlg = Dialogs.TokenSuggestionsDialog.Ask(Window.GetWindow(this), $"Suggested tokens — {deck.Name}", list, "");
                if (dlg == null) return "";
                var results = DeckTokenService.AddTokens(deck, dlg.Picked, dlg.Copies);
                DeckService.Save(deck);
                int added = results.Sum(r => Math.Max(0, r.Changed));
                return added > 0 ? $"Added {dlg.Picked.Count} token{(dlg.Picked.Count == 1 ? "" : "s")} ({dlg.Copies} of each) to its Tokens part." : "";
            }
            catch (Exception ex)
            {
                return $"Couldn't add the tokens: {ex.Message}";
            }
        }

        /// <summary>Suggested Tokens for the draft: look and print (adding waits for Accept).</summary>
        private void BtnTokens_Click(object sender, RoutedEventArgs e)
        {
            if (_draft == null) return;
            try
            {
                var deck = DeckBuildService.ToDeck(_draft, $"Draft — {_draftCommander}", _draftFormat);
                var list = TokenSuggestionService.Suggest(deck, TokenMakers(deck), out bool noData);
                string empty = noData ? "The card pool doesn't have token links yet. Run Update Database once, then try again."
                                      : "None of the draft's cards make tokens (as far as Scryfall links them).";
                Dialogs.TokenSuggestionsDialog.Ask(Window.GetWindow(this), $"Suggested tokens — {_draftCommander} (draft)", list, empty, allowAdd: false);
            }
            catch (Exception ex)
            {
                ShowStatus($"Couldn't read the token links: {ex.Message}", true);
            }
        }

        private void ShowStatus(string text, bool warning) => EditPageKit.ShowStatus(ActionText, text, warning);

        // ── Rows ────────────────────────────────────────────────────────
        /// <summary>One guide target in the panel.</summary>
        public sealed class GuideRow
        {
            public Role Role { get; init; }
            public int Count { get; set; }
            public bool Enabled { get; set; } = true;
            public string Label => RoleClassifier.Name(Role);
        }

        /// <summary>One card of the draft in the grid.</summary>
        public sealed class DraftRow
        {
            public DraftRow(Pick p, bool swap = false)
            {
                IsSwap = swap;
                Name = p.Card.Name;
                Copies = p.Copies;
                Reason = p.Reason;
                IsCommander = p.IsCommander;
                IsBasic = p.Card.IsBasicLand || p.Card.TypeLine.StartsWith("Basic", StringComparison.Ordinal);
                Type = p.Card.TypeLine;
                ManaValue = p.Card.IsLand ? "" : p.Card.ManaValue.ToString("0", CultureInfo.CurrentCulture);
                PriceText = p.Card.Price is { } pr ? $"${pr:0.00}" : "";
                SourceText = IsBasic ? "Basic"
                    : p.ToGet > 0 && p.ToGet < p.Copies ? $"{p.Copies - p.ToGet} yours + {p.ToGet} to get"
                    : p.Source == CardSource.Pool ? (p.Cost > 0 ? $"To get ${p.Cost:0.00}" : "To get")
                    : p.Card.FreeCopies > 0 ? "Free (yours)" : "Yours, in use";
                if (swap) SourceText = "Swap · " + SourceText;
            }

            public string Name { get; }
            /// <summary>A swap option (goes to the sideboard on Accept), not part of the deck.</summary>
            public bool IsSwap { get; }
            public int Copies { get; }
            public string Reason { get; }
            public bool IsCommander { get; }
            public bool IsBasic { get; }
            public string Type { get; }
            public string ManaValue { get; }
            public string PriceText { get; }
            public string SourceText { get; }
        }
    }
}
