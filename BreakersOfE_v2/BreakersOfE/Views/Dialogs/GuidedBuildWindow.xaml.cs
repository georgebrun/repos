using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BreakersOfE.DeckBuilder;
using BreakersOfE.Services;
using Wpf.Ui.Controls;

namespace BreakersOfE.Views.Dialogs
{
    /// <summary>
    /// Guided Build: the build one step at a time — each guide role (Ramp,
    /// Card draw …), then the deck's plan. Each step shows the best options
    /// (the engine's ranking, with what's chosen so far already in the deck)
    /// and ticks the top ones to start. <see cref="Chosen"/> is everything
    /// ticked, in step order; the Build page locks them and fills the rest.
    /// </summary>
    public partial class GuidedBuildWindow : FluentWindow
    {
        private sealed record Step(Role? Role, string Title, int Target);

        private readonly BuildContext _ctx;
        private readonly List<Step> _steps = new();
        /// <summary>What's ticked in each step (and whether the step was opened yet).</summary>
        private readonly Dictionary<int, List<string>> _picks = new();
        private readonly Dictionary<string, Eligible> _byName;
        private readonly int _spellSlots;
        private int _step;
        private List<OptionRow> _rows = new();
        private bool _loading;

        /// <summary>Every card ticked, in step order (null when cancelled).</summary>
        public List<string>? Chosen { get; private set; }
        /// <summary>60-card: the copies of each card ticked (as the step offered them).</summary>
        public Dictionary<string, int> ChosenCopies { get; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Copies of each card offered (60-card: 1–4; Commander: 1).</summary>
        private readonly Dictionary<string, int> _copies = new(StringComparer.OrdinalIgnoreCase);

        public GuidedBuildWindow(BuildContext ctx, Window? owner)
        {
            InitializeComponent();
            if (owner != null) Owner = owner;
            _ctx = ctx;
            _byName = new Dictionary<string, Eligible>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in ctx.Eligible) _byName.TryAdd(e.Card.Name, e);

            var c = ctx.Constraints;
            int landTarget = c.UseGuide && c.Guide.For(Role.Land) is { Enabled: true } lt ? lt.Count : 37;
            _spellSlots = Math.Max(0, c.DeckSize - (c.IsConstructed ? 0 : Math.Max(1, c.Commanders.Count)) - landTarget);
            // Key cards (60-card) are in already, with the copies they'll get.
            foreach (var n in c.Locked)
                if (_byName.TryGetValue(n, out var key))
                    _copies[n] = c.IsConstructed ? Math.Max(1, c.LockedCopies?.GetValueOrDefault(n) ?? (key.Card.Restricted ? 1 : c.CopyLimit)) : 1;
            if (c.UseGuide)
                foreach (var role in Builder.Order)
                    if (c.Guide.For(role) is { Enabled: true, Count: > 0 } t)
                        _steps.Add(new Step(role, RoleClassifier.Name(role), t.Count));
            _steps.Add(new Step(null, "The deck's plan", 0));

            WindowTitleBar.Title = c.IsConstructed ? "Guided Build — 60-card" : $"Guided Build — {string.Join(" + ", c.Commanders)}";
            KeyDown += (_, e) => { if (e.Key == Key.F1) HelpWindow.Open("build"); };
            Loaded += async (_, _) => await ShowStepAsync(0);
        }

        // ══ Steps ═════════════════════════════════════════════════════════

        /// <summary>Cards ticked in every step but this one.</summary>
        private List<string> Others(int step) =>
            _picks.Where(kv => kv.Key != step).OrderBy(kv => kv.Key).SelectMany(kv => kv.Value)
                  .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        private List<string> AllChosen() =>
            _picks.OrderBy(kv => kv.Key).SelectMany(kv => kv.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        private bool IsLand(string name) => _byName.TryGetValue(name, out var e) && e.Card.IsLand;
        /// <summary>Copies of a chosen card (always 1 in Commander).</summary>
        private int CopiesOf(string name) => _copies.TryGetValue(name, out int n) ? n : 1;
        private int Spells(IEnumerable<string> names) => names.Distinct(StringComparer.OrdinalIgnoreCase).Where(n => !IsLand(n)).Sum(CopiesOf);
        private int InRole(IEnumerable<string> names, Role r) => names.Distinct(StringComparer.OrdinalIgnoreCase).Where(n => HasRole(n, r)).Sum(CopiesOf);
        /// <summary>The key cards (60-card), counted as chosen in every step.</summary>
        private IEnumerable<string> Keys => _ctx.Constraints.Locked;
        private bool IsGameChanger(string name) => _byName.TryGetValue(name, out var e) && e.Card.IsGameChanger;
        private bool HasRole(string name, Role r) => _byName.TryGetValue(name, out var e) && e.Roles.Contains(r);

        private async Task ShowStepAsync(int index)
        {
            SaveStep();
            _step = Math.Clamp(index, 0, _steps.Count - 1);
            var step = _steps[_step];
            var others = Others(_step);
            int spellsElsewhere = Spells(others.Concat(Keys));

            StepTitle.Text = $"Step {_step + 1} of {_steps.Count} — {step.Title}" + (step.Role != null ? $" (guide: {step.Target})" : "");
            StepHint.Text = step.Role != null
                ? $"The best {step.Title.ToLowerInvariant()} cards for this deck, with what you've chosen so far already in it. Cards chosen in other steps that do this count toward it too. Tick what you want; untick what you don't."
                : "The best of the rest for the commander's plan. Tick as many as you like — or Finish and the builder fills the rest. Lands are added at the end either way.";
            ShowDots();
            BtnBack.IsEnabled = _step > 0;
            BtnNext.Content = _step == _steps.Count - 1 ? "Finish" : "Next";

            _loading = true;
            SetBusy(true, "Ranking the options…");
            try
            {
                int max = step.Role != null ? Math.Max(step.Target * 3, step.Target + 15) : Math.Max(40, _spellSlots - spellsElsewhere + 20);
                var withKeys = others.Concat(Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var options = await Task.Run(() => DeckBuildService.Options(_ctx, withKeys, step.Role, max));
                foreach (var o in options) _copies[o.Card.Name] = Math.Max(1, o.Copies);
                bool first = !_picks.ContainsKey(_step);
                HashSet<string> ticked;
                if (first)
                {
                    // To start: the top ones, as many as the guide still wants (cards from other steps count).
                    int need = step.Role is { } r ? Math.Max(0, step.Target - InRole(others.Concat(Keys), r))
                                                  : Math.Max(0, _spellSlots - spellsElsewhere);
                    int gc = GameChangersIn(others);
                    int got = 0, spells = spellsElsewhere;
                    ticked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var o in options)
                    {
                        if (got >= need) break;
                        if (o.Card.IsGameChanger && gc >= _ctx.Constraints.GameChangerLimit) continue;
                        if (!o.Card.IsLand && spells + CopiesOf(o.Card.Name) > _spellSlots) continue;
                        if (o.Card.IsGameChanger) gc++;
                        ticked.Add(o.Card.Name);
                        got += CopiesOf(o.Card.Name);
                        if (!o.Card.IsLand) spells += CopiesOf(o.Card.Name);
                    }
                }
                else ticked = new HashSet<string>(_picks[_step], StringComparer.OrdinalIgnoreCase);

                // Ticked cards from before that aren't among the options any more stay listed.
                _rows = options.Select(p => new OptionRow(p) { Use = ticked.Contains(p.Card.Name) }).ToList();
                foreach (var name in ticked.Where(n => !_rows.Any(r => r.Name.Equals(n, StringComparison.OrdinalIgnoreCase))))
                    if (_byName.TryGetValue(name, out var e))
                        _rows.Insert(0, new OptionRow(new Pick { Card = e.Card, Roles = e.Roles, Reason = "Chosen before", Copies = CopiesOf(name) }) { Use = true });
                OptionsGrid.ItemsSource = _rows;
                _picks[_step] = _rows.Where(r => r.Use).Select(r => r.Name).ToList();
                ShowCount();
                SetBusy(false, options.Count == 0 ? "No more cards for this step within your limits." : "");
            }
            catch (Exception ex)
            {
                SetBusy(false, $"Couldn't rank the options: {ex.Message}");
            }
            finally { _loading = false; }
        }

        /// <summary>Remember what's ticked in the step on screen.</summary>
        private void SaveStep()
        {
            if (_loading || !_picks.ContainsKey(_step)) return;
            _picks[_step] = _rows.Where(r => r.Use).Select(r => r.Name).ToList();
        }

        private int GameChangersIn(IEnumerable<string> names) => _ctx.Constraints.IsConstructed ? 0 :
            names.Distinct(StringComparer.OrdinalIgnoreCase).Count(IsGameChanger) + _ctx.Commanders.Count(c => c.IsGameChanger);

        private void ShowCount()
        {
            var step = _steps[_step];
            var all = Others(_step).Concat(_rows.Where(r => r.Use).Select(r => r.Name)).Concat(Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            int spells = Spells(all);
            string here = step.Role is { } r
                ? $"{step.Title}: {InRole(all, r)} of {step.Target} (ticked here: {_rows.Count(x => x.Use)})"
                : $"Ticked here: {_rows.Count(x => x.Use)}";
            int gc = GameChangersIn(all);
            string gcText = _ctx.Constraints.IsConstructed ? ""
                : _ctx.Constraints.GameChangerLimit == int.MaxValue ? $"   ·   {gc} Game Changers"
                : $"   ·   {gc} of {_ctx.Constraints.GameChangerLimit} Game Changers";
            CountText.Text = $"{here}   ·   Chosen so far: {spells} of {_spellSlots} spells" +
                             (_ctx.Constraints.IsConstructed ? " (copies counted)" : "") + gcText;
        }

        private void ShowDots()
        {
            StepDots.Children.Clear();
            for (int i = 0; i < _steps.Count; i++)
            {
                bool current = i == _step;
                int ticked = _picks.TryGetValue(i, out var l) ? l.Count : -1;
                StepDots.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 0, 6, 4),
                    Background = Models.LegalityInfo.BackgroundBrush(current ? "legal" : ""),
                    Child = new System.Windows.Controls.TextBlock
                    {
                        Text = _steps[i].Title + (ticked >= 0 ? $" ({ticked})" : ""), FontSize = 12,
                        FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal,
                        Foreground = Models.LegalityInfo.ForegroundBrush(current ? "legal" : ""),
                    },
                });
            }
        }

        private void SetBusy(bool busy, string text)
        {
            BusyBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            OptionsGrid.IsEnabled = BtnNext.IsEnabled = BtnFinish.IsEnabled = !busy;
            if (busy) BtnBack.IsEnabled = false;
            else BtnBack.IsEnabled = _step > 0;
            StatusText.Text = text;
            Mouse.OverrideCursor = busy ? Cursors.AppStarting : null;
        }

        // ══ Ticking ═══════════════════════════════════════════════════════

        /// <summary>A tick: refused when it would break the bracket's Game Changer limit or overfill the spell slots.</summary>
        private void Use_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not OptionRow row) return;
            if (row.Use)
            {
                var others = Others(_step);
                var here = _rows.Where(r => r.Use && r != row).Select(r => r.Name).ToList();
                int limit = _ctx.Constraints.GameChangerLimit;
                if (row.IsGameChanger && GameChangersIn(others.Concat(here)) >= limit)
                {
                    row.Use = false;
                    StatusText.Text = $"Bracket {_ctx.Constraints.Bracket} allows {(limit == 0 ? "no" : "up to " + limit)} Game Changer{(limit == 1 ? "" : "s")}. Untick one first.";
                    return;
                }
                if (!row.IsLand && Spells(others.Concat(here).Concat(Keys)) + CopiesOf(row.Name) > _spellSlots)
                {
                    row.Use = false;
                    StatusText.Text = $"That's all {_spellSlots} spell slots (the rest are lands). Untick something first.";
                    return;
                }
            }
            StatusText.Text = "";
            _picks[_step] = _rows.Where(r => r.Use).Select(r => r.Name).ToList();
            ShowCount();
            ShowDots();
        }

        private readonly CardPopup _popup = new();

        /// <summary>Double-click an option (not its tick box) or the card panel's picture: the larger card window.</summary>
        private void OptionsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject d && FindParent<System.Windows.Controls.CheckBox>(d) != null) return;
            if (System.Windows.Controls.ItemsControl.ContainerFromElement(OptionsGrid, e.OriginalSource as DependencyObject) is not System.Windows.Controls.DataGridRow row) return;
            if (row.Item is OptionRow r) OpenCardWindow(r);
        }

        private void Detail_ImageDoubleClicked(object? sender, EventArgs e)
        {
            if (OptionsGrid.SelectedItem is OptionRow r) OpenCardWindow(r);
        }

        private void OpenCardWindow(OptionRow r)
        {
            var names = _rows.Select(x => x.Name).ToList();
            _popup.Open(this, names, _rows.IndexOf(r), name =>
            {
                var next = _rows.FirstOrDefault(x => x.Name == name);
                if (next == null) return;
                OptionsGrid.SelectedItem = next;
                OptionsGrid.ScrollIntoView(next);
            });
        }

        private static T? FindParent<T>(DependencyObject? d) where T : DependencyObject
        {
            while (d != null && d is not T)
                d = d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                    ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            return d as T;
        }

        private void OptionsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (OptionsGrid.SelectedItem is OptionRow row)
                Detail.ShowCard(DeckBuildService.PoolCardByName(row.Name));
        }

        // ══ Buttons ═══════════════════════════════════════════════════════

        private async void Back_Click(object sender, RoutedEventArgs e)
        {
            if (_step > 0) await ShowStepAsync(_step - 1);
        }

        private async void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_step >= _steps.Count - 1) { Finish(); return; }
            await ShowStepAsync(_step + 1);
        }

        private void Finish_Click(object sender, RoutedEventArgs e) => Finish();

        private void Finish()
        {
            SaveStep();
            Chosen = AllChosen();
            foreach (var n in Chosen) ChosenCopies[n] = CopiesOf(n);
            DialogResult = true;
        }

        protected override void OnClosed(EventArgs e)
        {
            Mouse.OverrideCursor = null;
            base.OnClosed(e);
        }

        // ── Rows ────────────────────────────────────────────────────────
        /// <summary>One option in a step.</summary>
        public sealed class OptionRow : INotifyPropertyChanged
        {
            private bool _use;

            public OptionRow(Pick p)
            {
                Name = p.Card.Name;
                Copies = p.Copies;
                Reason = p.Reason;
                Type = p.Card.TypeLine;
                IsLand = p.Card.IsLand;
                IsGameChanger = p.Card.IsGameChanger;
                ManaValue = p.Card.IsLand ? "" : p.Card.ManaValue.ToString("0", CultureInfo.CurrentCulture);
                PriceText = p.Card.Price is { } pr ? $"${pr:0.00}" : "";
                SourceText = p.ToGet > 0 && p.ToGet < p.Copies ? $"{p.Copies - p.ToGet} yours + {p.ToGet} to get"
                    : p.ToGet > 0 || p.Card.FreeCopies <= 0 ? (p.Card.Price is { } c ? $"To get ${c:0.00}" : "To get")
                    : "Free (yours)";
            }

            public bool Use
            {
                get => _use;
                set { if (_use == value) return; _use = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Use))); }
            }
            public string Name { get; }
            public int Copies { get; }
            public string Reason { get; }
            public string Type { get; }
            public bool IsLand { get; }
            public bool IsGameChanger { get; }
            public string ManaValue { get; }
            public string PriceText { get; }
            public string SourceText { get; }

            public event PropertyChangedEventHandler? PropertyChanged;
        }
    }
}
