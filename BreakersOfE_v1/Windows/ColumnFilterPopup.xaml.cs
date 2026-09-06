using BreakersOfE.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BreakersOfE.Windows
{
    public class ValueItem : INotifyPropertyChanged
    {
        private bool _isChecked;
        public string DisplayValue { get; set; } = string.Empty;
        public string ActualValue { get; set; } = string.Empty;
        public bool IsAll { get; set; } = false;

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                _isChecked = value;
                PropertyChanged?.Invoke(this,
                    new PropertyChangedEventArgs(nameof(IsChecked)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public partial class ColumnFilterPopup : Window
    {
        private readonly ColumnFilterState _state;
        private readonly List<string> _allValues;
        private readonly List<ValueItem> _allItems = new();
        private bool _busy = false;

        // Snapshot on open for Cancel
        private readonly bool _origAllSelected;
        private readonly HashSet<string> _origSelectedValues;
        private readonly bool _origUseTextFilter;
        private readonly ColumnFilterOperator _origTextOperator;
        private readonly string _origTextValue;

        /// <summary>Fired when OK or Clear commits the filter.</summary>
        public event EventHandler? FilterChanged;
        /// <summary>Fired when Sort A-Z or Z-A is clicked. true = ascending.</summary>
        public event EventHandler<bool>? SortRequested;

        public ColumnFilterPopup(
            string columnName, string propertyName,
            List<string> allValues, ColumnFilterState existingState)
        {
            InitializeComponent();
            Title = $"Filter: {columnName}";
            _state = existingState;
            _allValues = allValues
                .OrderBy(v => v, Comparer<string>.Create(
                    ColumnFilterState.CompareNatural)).ToList();

            _origAllSelected = _state.AllSelected;
            _origSelectedValues = new HashSet<string>(_state.SelectedValues);
            _origUseTextFilter = _state.UseTextFilter;
            _origTextOperator = _state.TextOperator;
            _origTextValue = _state.TextValue;

            BuildItemList();
            PopulateOperatorCombo();
            RestoreTextState();
        }

        // ── Build checkbox list ─────────────────────────────────────────
        private void BuildItemList()
        {
            _allItems.Clear();
            foreach (var v in _allValues)
            {
                _allItems.Add(new ValueItem
                {
                    DisplayValue = string.IsNullOrEmpty(v) ? "(blank)" : v,
                    ActualValue = v,
                    IsChecked = _state.AllSelected ||
                                _state.SelectedValues.Contains(v)
                });
            }
            _state.TotalValueCount = _allValues.Count;
            ValuesListBox.ItemsSource = _allItems;

            _busy = true;
            SyncSelectAllCheckbox();
            _busy = false;
        }

        // ── Search box — FILTERS the visible checkboxes (Excel-style) ───
        private void ValueSearchBox_TextChanged(object sender,
            TextChangedEventArgs e)
        {
            string txt = ValueSearchBox.Text.Trim();
            if (string.IsNullOrEmpty(txt))
                ValuesListBox.ItemsSource = _allItems;
            else
                ValuesListBox.ItemsSource = _allItems
                    .Where(x => x.DisplayValue.Contains(txt,
                        StringComparison.OrdinalIgnoreCase)).ToList();

            _busy = true;
            SyncSelectAllCheckbox();
            _busy = false;
        }

        // ── Select All ────────────────────────────────────────────────
        // Uncheck: unchecks ALL items (visible + hidden) so you start fresh
        // Check: checks only the VISIBLE items (search results)
        private void SelectAll_Changed(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            _busy = true;
            bool check = ChkSelectAll.IsChecked == true;
            if (!check)
            {
                // Uncheck everything — visible and hidden
                foreach (var vi in _allItems) vi.IsChecked = false;
            }
            else
            {
                // Check only the visible items
                if (ValuesListBox.ItemsSource is IEnumerable<ValueItem> visible)
                    foreach (var vi in visible) vi.IsChecked = true;
            }
            _busy = false;
        }

        // ── Checkbox changed — sync Select All, no event fired ──────────
        private void ValueCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            _busy = true;
            SyncSelectAllCheckbox();
            _busy = false;
        }

        private void SyncSelectAllCheckbox()
        {
            // Reflect the same scope Select All acts on: the currently visible
            // (search-filtered) items. Previously this checked ALL items while
            // Select All only toggled visible ones, so the indicator could show
            // indeterminate right after a Select All on a filtered view.
            var scope = ValuesListBox.ItemsSource as IEnumerable<ValueItem>
                        ?? _allItems;
            var list = scope.ToList();
            if (list.Count == 0) { ChkSelectAll.IsChecked = false; return; }
            bool all = list.All(x => x.IsChecked);
            bool none = list.All(x => !x.IsChecked);
            ChkSelectAll.IsChecked = all ? true : none ? false : null;
        }

        // ── Sort buttons — fire immediately ─────────────────────────────
        private void BtnSortAsc_Click(object sender, RoutedEventArgs e)
            => SortRequested?.Invoke(this, true);
        private void BtnSortDesc_Click(object sender, RoutedEventArgs e)
            => SortRequested?.Invoke(this, false);

        // ── OK — commit checkbox state, fire event, close ───────────────
        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            CommitState();
            FilterChanged?.Invoke(this, EventArgs.Empty);
            Close();
        }

        private void CommitState()
        {
            // Commit based on stored intent, NOT which tab happens to be visible.
            // The previous version branched on MainTabControl.SelectedIndex, so
            // committing from the "wrong" tab could leave the other mode's state
            // stale (the clear-filter desync bug).

            // Always capture the current text-filter fields.
            _state.TextValue = TextFilterBox.Text;
            bool textActive =
                !string.IsNullOrEmpty(_state.TextValue) ||
                _state.TextOperator == ColumnFilterOperator.IsBlank ||
                _state.TextOperator == ColumnFilterOperator.IsNotBlank;

            // Always capture the current checkbox selection.
            bool allChecked = _allItems.All(x => x.IsChecked);
            _state.SelectedValues.Clear();
            if (allChecked)
            {
                _state.AllSelected = true;
            }
            else
            {
                _state.AllSelected = false;
                foreach (var item in _allItems.Where(x => x.IsChecked))
                    _state.SelectedValues.Add(item.ActualValue);
            }

            // A text filter takes precedence only when the user actually has one.
            // Otherwise the column filters by checked values (or nothing).
            _state.UseTextFilter = textActive;
        }

        // ── Cancel — revert to snapshot, close ──────────────────────────
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _state.AllSelected = _origAllSelected;
            _state.SelectedValues = new HashSet<string>(_origSelectedValues);
            _state.UseTextFilter = _origUseTextFilter;
            _state.TextOperator = _origTextOperator;
            _state.TextValue = _origTextValue;
            Close();
        }

        // ── Clear Filter — clear this column, commit, close ─────────────
        private void BtnClearFilter_Click(object sender, RoutedEventArgs e)
        {
            _state.Clear();
            FilterChanged?.Invoke(this, EventArgs.Empty);
            Close();
        }

        // ── Text filter tab ─────────────────────────────────────────────
        private void PopulateOperatorCombo()
        {
            OperatorCombo.Items.Clear();
            foreach (ColumnFilterOperator op in Enum.GetValues<ColumnFilterOperator>())
                OperatorCombo.Items.Add(ColumnFilterState.GetOperatorLabel(op));
            OperatorCombo.SelectedIndex = (int)_state.TextOperator;
        }

        private void RestoreTextState()
        {
            _busy = true;
            OperatorCombo.SelectedIndex = (int)_state.TextOperator;
            TextFilterBox.Text = _state.TextValue;
            UpdateTextBoxVisibility();
            if (_state.UseTextFilter) MainTabControl.SelectedIndex = 1;
            _busy = false;
        }

        private void UpdateTextBoxVisibility()
        {
            var op = (ColumnFilterOperator)OperatorCombo.SelectedIndex;
            TextFilterBox.Visibility =
                op == ColumnFilterOperator.IsBlank ||
                op == ColumnFilterOperator.IsNotBlank
                    ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OperatorCombo_SelectionChanged(object sender,
            SelectionChangedEventArgs e)
        {
            if (_busy) return;
            _state.TextOperator = (ColumnFilterOperator)OperatorCombo.SelectedIndex;
            UpdateTextBoxVisibility();
        }

        private void TextFilterBox_TextChanged(object sender,
            TextChangedEventArgs e)
        { }

        private void TextFilterBox_KeyDown(object sender,
            System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            { BtnOk_Click(sender, e); e.Handled = true; }
        }

        private void BtnClearTextFilter_Click(object sender, RoutedEventArgs e)
        {
            // Clear the text-filter portion of the stored state — not just the
            // textbox — and refresh so the grid updates immediately. Previously
            // this only blanked the textbox, leaving the real filter active and
            // giving false "cleared" feedback.
            _busy = true;
            TextFilterBox.Text = string.Empty;
            OperatorCombo.SelectedIndex = 0;
            _busy = false;

            _state.UseTextFilter = false;
            _state.TextValue = string.Empty;
            _state.TextOperator = ColumnFilterOperator.Contains;

            // If no value-checkbox filter is active either, the column is now
            // fully clear. Reflect that and refresh the grid live.
            FilterChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}