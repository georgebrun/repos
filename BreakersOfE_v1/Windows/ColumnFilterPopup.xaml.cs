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
            if (_allItems.Count == 0) { ChkSelectAll.IsChecked = false; return; }
            // State reflects ALL items, not just visible search results
            bool all = _allItems.All(x => x.IsChecked);
            bool none = _allItems.All(x => !x.IsChecked);
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
            if (MainTabControl.SelectedIndex == 1)
            {
                _state.TextValue = TextFilterBox.Text;
                _state.UseTextFilter = !string.IsNullOrEmpty(_state.TextValue) ||
                    _state.TextOperator == ColumnFilterOperator.IsBlank ||
                    _state.TextOperator == ColumnFilterOperator.IsNotBlank;
                return;
            }

            _state.UseTextFilter = false;
            _state.SelectedValues.Clear();
            bool allChecked = _allItems.All(x => x.IsChecked);
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
            _busy = true;
            TextFilterBox.Text = string.Empty;
            OperatorCombo.SelectedIndex = 0;
            _busy = false;
        }
    }
}