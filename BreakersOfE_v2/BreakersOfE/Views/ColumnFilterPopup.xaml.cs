using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BreakersOfE.Filtering;
using Wpf.Ui.Controls;

namespace BreakersOfE.Views
{
    /// <summary>One row in the value checklist.</summary>
    public class ValueItem : INotifyPropertyChanged
    {
        private bool _isChecked;
        public string DisplayValue { get; set; } = string.Empty;
        public string ActualValue { get; set; } = string.Empty;

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                _isChecked = value; PropertyChanged?.Invoke(this,
                    new PropertyChangedEventArgs(nameof(IsChecked)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>
    /// Excel-style column filter popup — UNIFIED model.
    ///
    /// The single source of truth is the set of checked values. The Text
    /// Filters tab doesn't create a parallel mode: its Apply button computes
    /// which values match the rule and checks exactly those boxes. Commit
    /// (OK) reads only the checkboxes. Clear Filter is the one and only clear
    /// path — it resets the state and refreshes the grid. There is no separate
    /// text-mode flag to fall out of sync, so "clear didn't clear" can't happen.
    /// </summary>
    public partial class ColumnFilterPopup : FluentWindow
    {
        private readonly ColumnFilterState _state;
        private readonly List<string> _allValues;
        private readonly List<ValueItem> _allItems = new();
        private bool _busy;

        // Snapshot for Cancel
        private readonly bool _origAllSelected;
        private readonly HashSet<string> _origSelected;

        /// <summary>Fired when OK or Clear commits — grid should re-apply filters.</summary>
        public event EventHandler? FilterChanged;

        /// <summary>Fired when a Sort button is clicked. true = ascending.</summary>
        public event EventHandler<bool>? SortRequested;

        public ColumnFilterPopup(
            string columnName, string propertyName,
            List<string> allValues, ColumnFilterState existingState)
        {
            InitializeComponent();
            Title = $"Filter: {columnName}";
            _state = existingState;

            _allValues = allValues
                .OrderBy(v => v, Comparer<string>.Create(ColumnFilterState.CompareNatural))
                .ToList();

            _origAllSelected = _state.AllSelected;
            _origSelected = new HashSet<string>(_state.SelectedValues, StringComparer.Ordinal);

            BuildItemList();
            PopulateOperatorCombo();
        }

        // ── Build the checklist from current state ──────────────────────
        private void BuildItemList()
        {
            _allItems.Clear();
            foreach (var v in _allValues)
            {
                _allItems.Add(new ValueItem
                {
                    DisplayValue = string.IsNullOrEmpty(v) ? "(blank)" : v,
                    ActualValue = v,
                    // AllSelected → everything checked; otherwise only stored values
                    IsChecked = _state.AllSelected || _state.SelectedValues.Contains(v)
                });
            }
            ValuesListBox.ItemsSource = _allItems;

            _busy = true;
            SyncSelectAll();
            _busy = false;
        }

        // ── Search narrows the visible checkboxes ───────────────────────
        private void ValueSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string txt = ValueSearchBox.Text.Trim();
            ValuesListBox.ItemsSource = string.IsNullOrEmpty(txt)
                ? _allItems
                : _allItems.Where(x => x.DisplayValue
                    .Contains(txt, StringComparison.OrdinalIgnoreCase)).ToList();

            _busy = true;
            SyncSelectAll();
            _busy = false;
        }

        // ── (Select All) toggles the VISIBLE items (Excel behavior) ─────
        private void SelectAll_Changed(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            _busy = true;

            bool check = ChkSelectAll.IsChecked == true;
            var visible = (ValuesListBox.ItemsSource as IEnumerable<ValueItem>)
                          ?? _allItems;
            foreach (var vi in visible) vi.IsChecked = check;

            _busy = false;
        }

        private void ValueCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            _busy = true;
            SyncSelectAll();
            _busy = false;
        }

        // (Select All) reflects the state of the VISIBLE items
        private void SyncSelectAll()
        {
            var visible = (ValuesListBox.ItemsSource as IEnumerable<ValueItem>)?.ToList()
                          ?? _allItems;
            if (visible.Count == 0) { ChkSelectAll.IsChecked = false; return; }

            bool all = visible.All(x => x.IsChecked);
            bool none = visible.All(x => !x.IsChecked);
            ChkSelectAll.IsChecked = all ? true : none ? (bool?)false : null;
        }

        // ── Sort ────────────────────────────────────────────────────────
        private void BtnSortAsc_Click(object sender, RoutedEventArgs e)
            => SortRequested?.Invoke(this, true);
        private void BtnSortDesc_Click(object sender, RoutedEventArgs e)
            => SortRequested?.Invoke(this, false);

        // ── Text Filters tab ────────────────────────────────────────────
        private void PopulateOperatorCombo()
        {
            OperatorCombo.Items.Clear();
            foreach (TextFilterOperator op in Enum.GetValues<TextFilterOperator>())
                OperatorCombo.Items.Add(ColumnFilterState.OperatorLabel(op));
            OperatorCombo.SelectedIndex = 0; // Contains
            UpdateTextBoxState();
        }

        private void OperatorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => UpdateTextBoxState();

        private void UpdateTextBoxState()
        {
            var op = (TextFilterOperator)OperatorCombo.SelectedIndex;
            bool noArg = op is TextFilterOperator.IsBlank or TextFilterOperator.IsNotBlank;
            TextFilterBox.IsEnabled = !noArg;
            if (noArg) TextFilterBox.Text = string.Empty;
        }

        private void TextFilterBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            { BtnApplyText_Click(sender, e); e.Handled = true; }
        }

        /// <summary>
        /// Apply the text rule: compute matching values, check exactly those,
        /// then jump to the Values tab so the user sees the result. This is the
        /// bridge that keeps a single source of truth.
        /// </summary>
        private void BtnApplyText_Click(object sender, RoutedEventArgs e)
        {
            var op = (TextFilterOperator)OperatorCombo.SelectedIndex;
            string text = TextFilterBox.Text ?? string.Empty;

            // Update the checkboxes to match the rule
            var matched = _allItems.Where(vi => TextRuleMatches(vi.ActualValue, op, text))
                                   .Select(vi => vi.ActualValue)
                                   .ToHashSet(StringComparer.Ordinal);

            _busy = true;
            foreach (var vi in _allItems)
                vi.IsChecked = matched.Contains(vi.ActualValue);
            _busy = false;

            // Show the result on the Values tab
            ValueSearchBox.Text = string.Empty;
            ValuesListBox.ItemsSource = _allItems;
            MainTabControl.SelectedIndex = 0;

            _busy = true;
            SyncSelectAll();
            _busy = false;
        }

        // Local mirror of the state's text-matching (kept private to the popup)
        private static bool TextRuleMatches(string? value, TextFilterOperator op, string text)
        {
            value ??= string.Empty;
            string v = value.ToLowerInvariant();
            string t = (text ?? string.Empty).ToLowerInvariant();
            return op switch
            {
                TextFilterOperator.Contains => v.Contains(t),
                TextFilterOperator.DoesNotContain => !v.Contains(t),
                TextFilterOperator.Equals => v == t,
                TextFilterOperator.DoesNotEqual => v != t,
                TextFilterOperator.BeginsWith => v.StartsWith(t),
                TextFilterOperator.EndsWith => v.EndsWith(t),
                TextFilterOperator.IsBlank => string.IsNullOrWhiteSpace(value),
                TextFilterOperator.IsNotBlank => !string.IsNullOrWhiteSpace(value),
                TextFilterOperator.GreaterThan => Num(value, text) > 0,
                TextFilterOperator.GreaterThanOrEqual => Num(value, text) >= 0,
                TextFilterOperator.LessThan => Num(value, text) < 0,
                TextFilterOperator.LessThanOrEqual => Num(value, text) <= 0,
                _ => true
            };
        }

        private static int Num(string a, string b)
        {
            if (decimal.TryParse(a.TrimStart('$', ' '),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var da) &&
                decimal.TryParse(b.TrimStart('$', ' '),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var db))
                return da.CompareTo(db);
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        // ── OK — commit checkbox state (the ONLY thing that defines the filter) ──
        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            bool allChecked = _allItems.All(x => x.IsChecked);
            _state.SelectedValues.Clear();

            if (allChecked)
            {
                _state.AllSelected = true;   // no narrowing
            }
            else
            {
                _state.AllSelected = false;
                foreach (var item in _allItems.Where(x => x.IsChecked))
                    _state.SelectedValues.Add(item.ActualValue);
            }

            FilterChanged?.Invoke(this, EventArgs.Empty);
            Close();
        }

        // ── Cancel — restore snapshot ───────────────────────────────────
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _state.AllSelected = _origAllSelected;
            _state.SelectedValues = new HashSet<string>(_origSelected, StringComparer.Ordinal);
            Close();
        }

        // ── Clear Filter — the single, always-works clear path ──────────
        private void BtnClearFilter_Click(object sender, RoutedEventArgs e)
        {
            _state.Clear();                       // AllSelected = true, values emptied
            FilterChanged?.Invoke(this, EventArgs.Empty);
            Close();
        }
    }
}