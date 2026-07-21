using BreakersOfE.Data;
using BreakersOfE.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace BreakersOfE.Windows
{
    public partial class ColumnChooserPopup : Window
    {
        private readonly DataGrid _grid;
        private readonly string _tableKey;
        private readonly List<CheckBox> _checkboxes = new();
        private CheckBox? _allCheckBox;
        private bool _suppressAllEvent = false;

        public ColumnChooserPopup(DataGrid grid, string tableKey)
        {
            InitializeComponent();
            _grid = grid;
            _tableKey = tableKey;
            System.Diagnostics.Debug.WriteLine(
                $"[ColChooserPopup] Constructed. Grid={grid?.Name} TableKey={tableKey} Columns={grid?.Columns.Count}");
            BuildColumnList();
        }

        private void BuildColumnList()
        {
            System.Diagnostics.Debug.WriteLine(
                $"[ColChooserPopup] BuildColumnList: {_grid.Columns.Count} columns");
            ColumnPanel.Children.Clear();
            _checkboxes.Clear();

            // (All) checkbox
            _allCheckBox = new CheckBox
            {
                Content = "(All)",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(4, 3, 0, 3),
                IsChecked = true
            };
            _allCheckBox.Checked += AllCheckBox_Checked;
            _allCheckBox.Unchecked += AllCheckBox_Unchecked;
            ColumnPanel.Children.Add(_allCheckBox);

            bool allVisible = true;

            foreach (var col in _grid.Columns)
            {
                string header = col.Header?.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(header)) continue;

                bool visible = col.Visibility == Visibility.Visible;
                if (!visible) allVisible = false;

                var cb = new CheckBox
                {
                    Content = header,
                    IsChecked = visible,
                    FontSize = 13,
                    Padding = new Thickness(4, 3, 0, 3),
                    Tag = col
                };
                cb.Checked += ColCheckBox_Changed;
                cb.Unchecked += ColCheckBox_Changed;
                _checkboxes.Add(cb);
                ColumnPanel.Children.Add(cb);
            }

            _suppressAllEvent = true;
            _allCheckBox.IsChecked = allVisible;
            _suppressAllEvent = false;
        }

        private void AllCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressAllEvent) return;
            foreach (var cb in _checkboxes)
                cb.IsChecked = true;
        }

        private void AllCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_suppressAllEvent) return;
            foreach (var cb in _checkboxes)
                cb.IsChecked = false;
        }

        private void ColCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb) return;
            if (cb.Tag is not DataGridColumn col) return;

            col.Visibility = cb.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;

            // Update (All) state
            bool allChecked = _checkboxes.TrueForAll(c => c.IsChecked == true);
            _suppressAllEvent = true;
            if (_allCheckBox != null)
                _allCheckBox.IsChecked = allChecked ? true :
                    _checkboxes.Exists(c => c.IsChecked == true) ? null : false;
            _suppressAllEvent = false;

            SaveLayout();
        }

        private void SaveLayout()
        {
            try
            {
                var vis = new Dictionary<string, bool>();
                foreach (var col in _grid.Columns)
                {
                    string hdr = col.Header?.ToString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(hdr))
                        vis[hdr] = col.Visibility == Visibility.Visible;
                }
                string json = JsonSerializer.Serialize(vis);
                using var db = new AppDbContext();
                var s = db.AppSettings.FirstOrDefault(
                    x => x.Key == "ColVis_" + _tableKey);
                if (s == null)
                    db.AppSettings.Add(new AppSetting
                    { Key = "ColVis_" + _tableKey, Value = json });
                else
                    s.Value = json;
                db.SaveChanges();
            }
            catch { }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}