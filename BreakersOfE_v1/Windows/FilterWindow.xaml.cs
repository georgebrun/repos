using BreakersOfE.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BreakersOfE.Windows
{
    public partial class FilterWindow : Window
    {
        // ── State ─────────────────────────────────────────────────────────────
        private FilterContext _context;
        private FilterNode _rootNode;
        private List<FilterField> _fields;

        // ── Filter folder ─────────────────────────────────────────────────────
        private static string FilterFolder =>
            Services.AppFolderService.FiltersFolder;

        // ── Result ────────────────────────────────────────────────────────────
        public FilterNode? ResultNode { get; private set; }
        public bool CaseInsensitive => ChkCaseInsensitive.IsChecked == true;
        public bool IncludeUnpriced => ChkIncludeUnpriced.IsChecked == true;
        public bool UseNonFoilPrice => RadioPriceNonFoil.IsChecked == true;

        // ── Color quick-filter results ────────────────────────────────────────
        public bool QuickColorActive =>
            FwBtnW.IsChecked == true || FwBtnU.IsChecked == true ||
            FwBtnB.IsChecked == true || FwBtnR.IsChecked == true ||
            FwBtnG.IsChecked == true || FwBtnC.IsChecked == true;

        public ColorMatchMode QuickColorMode =>
            FwRbExact.IsChecked == true ? ColorMatchMode.ExactlyTheSelected :
            FwRbIncludes.IsChecked == true ? ColorMatchMode.AllSelected :
            FwRbAny.IsChecked == true ? ColorMatchMode.AnyOfSelected :
            ColorMatchMode.AtMost;  // default = At Most (Commander)

        public bool QuickFilterW => FwBtnW.IsChecked == true;
        public bool QuickFilterU => FwBtnU.IsChecked == true;
        public bool QuickFilterB => FwBtnB.IsChecked == true;
        public bool QuickFilterR => FwBtnR.IsChecked == true;
        public bool QuickFilterG => FwBtnG.IsChecked == true;
        public bool QuickFilterC => FwBtnC.IsChecked == true;

        private void ColorQuick_Changed(object sender, RoutedEventArgs e)
        {
            // No live preview needed — applied on OK
        }

        // ── Selected sets/editions from Tab 1 ────────────────────────────────
        public List<string> SelectedSetCodes { get; private set; } = new();

        // ════════════════════════════════════════════════════════════════════
        // CONSTRUCTOR
        // ════════════════════════════════════════════════════════════════════
        public FilterWindow(FilterContext context,
            FilterNode? existingFilter = null)
        {
            InitializeComponent();
            _context = context;
            _fields = FilterField.GetFields(context);
            _rootNode = existingFilter?.Clone()
                ?? FilterNode.NewGroup(FilterGroupOp.And);

            // Set title
            Title = context switch
            {
                FilterContext.Pool => "Filter Card Pool",
                FilterContext.Planechase => "Filter Planechase",
                FilterContext.Archenemy => "Filter Archenemy",
                FilterContext.Vanguard => "Filter Vanguard",
                FilterContext.Tokens => "Filter Tokens",
                FilterContext.ArtSeries => "Filter Art Series",
                FilterContext.Collection => "Filter Collection",
                FilterContext.Deck => "Filter Deck",
                _ => "Filter"
            };

            Loaded += FilterWindow_Loaded;
        }

        private void FilterWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSetTree();
            RebuildConditionPanel();

            // Hide Blocks/Formats for non-pool contexts
            if (_context != FilterContext.Pool &&
                _context != FilterContext.Collection)
            {
                RadioBlocks.IsEnabled = false;
                RadioFormats.IsEnabled = false;
                RadioEditions.IsChecked = true;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // TAB 1 — SET TREE
        // ════════════════════════════════════════════════════════════════════
        private void LoadSetTree()
        {
            SetTreeView.Items.Clear();
            SetDetailPanel.Children.Clear();

            if (RadioBlocks.IsChecked == true)
                LoadBlocksTree();
            else if (RadioFormats.IsChecked == true)
                LoadFormatsTree();
            else
                LoadEditionsTree();
        }

        private void LoadBlocksTree()
        {
            using var db = new Data.AppDbContext();

            // Group sets by SetType
            var groups = db.PoolCards.AsNoTracking()
                .Where(c => c.SetType != null && c.SetType != "")
                .Select(c => new { c.SetType, c.SetCode, c.SetName })
                .Distinct()
                .ToList()
                .GroupBy(x => x.SetType)
                .OrderBy(g => g.Key);

            foreach (var group in groups)
            {
                string displayName = SetTypeToDisplay(group.Key);
                var groupItem = new TreeViewItem
                {
                    Header = BuildGroupHeader(displayName, group.Key),
                    Tag = "group:" + group.Key
                };

                foreach (var set in group
                    .Select(x => new { x.SetCode, x.SetName })
                    .Distinct()
                    .OrderBy(x => x.SetName))
                {
                    var setItem = new TreeViewItem
                    {
                        Header = BuildSetHeader(
                            set.SetCode, set.SetName,
                            SelectedSetCodes.Contains(set.SetCode)),
                        Tag = "set:" + set.SetCode
                    };
                    groupItem.Items.Add(setItem);
                }

                SetTreeView.Items.Add(groupItem);
            }
        }

        private void LoadFormatsTree()
        {
            var formats = new[]
            {
                "Standard", "Pioneer", "Modern", "Legacy",
                "Vintage", "Commander", "Pauper", "Historic",
                "Explorer", "Alchemy", "Brawl", "Old School"
            };

            foreach (var fmt in formats)
            {
                var item = new TreeViewItem
                {
                    Header = BuildCheckHeader(fmt, false),
                    Tag = "format:" + fmt
                };
                SetTreeView.Items.Add(item);
            }
        }

        private void LoadEditionsTree()
        {
            using var db = new Data.AppDbContext();

            var sets = db.PoolCards.AsNoTracking()
                .Select(c => new { c.SetCode, c.SetName, c.ReleasedAt })
                .Distinct()
                .ToList()
                .OrderByDescending(x => x.ReleasedAt)
                .ThenBy(x => x.SetName);

            foreach (var set in sets)
            {
                var item = new TreeViewItem
                {
                    Header = BuildSetHeader(
                        set.SetCode, set.SetName,
                        SelectedSetCodes.Contains(set.SetCode)),
                    Tag = "set:" + set.SetCode
                };
                SetTreeView.Items.Add(item);
            }
        }

        private UIElement BuildGroupHeader(string name, string setType)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };

            var expand = new Button
            {
                Content = "⊞",
                Style = FindResource("ConditionButtonStyle") as Style,
                Width = 16,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            expand.Click += (s, e) =>
            {
                if (s is Button btn &&
                    btn.Parent is StackPanel p &&
                    p.Parent is TreeViewItem tvi)
                    tvi.IsExpanded = !tvi.IsExpanded;
                e.Handled = true;
            };
            sp.Children.Add(expand);

            var cb = new CheckBox
            {
                Margin = new Thickness(4, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            cb.Checked += GroupCheckBox_Changed;
            cb.Unchecked += GroupCheckBox_Changed;
            sp.Children.Add(cb);

            sp.Children.Add(new TextBlock
            {
                Text = name,
                FontWeight = FontWeights.SemiBold,
                Foreground = GetSetTypeBrush(setType),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            });

            return sp;
        }

        private UIElement BuildSetHeader(
            string code, string name, bool isChecked)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };

            sp.Children.Add(new TextBlock
            {
                Text = "    ",
                Width = 20
            }); // indent

            var cb = new CheckBox
            {
                IsChecked = isChecked,
                Margin = new Thickness(4, 0, 6, 0),
                Tag = code,
                VerticalAlignment = VerticalAlignment.Center
            };
            cb.Checked += SetCheckBox_Changed;
            cb.Unchecked += SetCheckBox_Changed;
            sp.Children.Add(cb);

            sp.Children.Add(new TextBlock
            {
                Text = $"{code}  {name}",
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            });

            return sp;
        }

        private UIElement BuildCheckHeader(string name, bool isChecked)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };

            var cb = new CheckBox
            {
                IsChecked = isChecked,
                Margin = new Thickness(4, 0, 6, 0),
                Tag = name,
                VerticalAlignment = VerticalAlignment.Center
            };
            cb.Checked += SetCheckBox_Changed;
            cb.Unchecked += SetCheckBox_Changed;
            sp.Children.Add(cb);

            sp.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            });

            return sp;
        }

        private void GroupCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb) return;
            if (cb.Parent is not StackPanel sp) return;
            if (sp.Parent is not TreeViewItem tvi) return;

            bool check = cb.IsChecked == true;

            // Check/uncheck all children
            foreach (TreeViewItem child in tvi.Items)
            {
                if (child.Header is StackPanel csp)
                {
                    foreach (var el in csp.Children)
                    {
                        if (el is CheckBox childCb)
                        {
                            childCb.IsChecked = check;
                            if (childCb.Tag is string code)
                            {
                                if (check && !SelectedSetCodes.Contains(code))
                                    SelectedSetCodes.Add(code);
                                else if (!check)
                                    SelectedSetCodes.Remove(code);
                            }
                        }
                    }
                }
            }
        }

        private void SetCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb) return;
            if (cb.Tag is not string code) return;

            if (cb.IsChecked == true)
            {
                if (!SelectedSetCodes.Contains(code))
                    SelectedSetCodes.Add(code);
            }
            else
            {
                SelectedSetCodes.Remove(code);
            }
        }

        private void RadioView_Checked(object sender, RoutedEventArgs e)
        {
            if (SetTreeView != null) LoadSetTree();
        }

        private void SetTreeView_SelectedItemChanged(object sender,
            RoutedPropertyChangedEventArgs<object> e)
        {
            // Show set details in right panel
            if (e.NewValue is TreeViewItem tvi &&
                tvi.Tag is string tag &&
                tag.StartsWith("set:"))
            {
                string code = tag[4..];
                ShowSetDetail(code);
            }
            else
            {
                SetDetailPanel.Children.Clear();
            }
        }

        private void ShowSetDetail(string setCode)
        {
            SetDetailPanel.Children.Clear();

            using var db = new Data.AppDbContext();

            var card = db.PoolCards
                .FirstOrDefault(c => c.SetCode == setCode);
            if (card == null) return;

            void AddDetail(string label, string value)
            {
                SetDetailPanel.Children.Add(new TextBlock
                {
                    Text = label,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 11,
                    Foreground = Brushes.Gray,
                    Margin = new Thickness(0, 8, 0, 1)
                });
                SetDetailPanel.Children.Add(new TextBlock
                {
                    Text = value,
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            AddDetail("EDITION CODE", card.SetCode);
            AddDetail("EDITION NAME", card.SetName);
            AddDetail("SET TYPE", SetTypeToDisplay(card.SetType));
            AddDetail("RELEASED", card.ReleasedAt);

            int count = db.PoolCards.Count(c => c.SetCode == setCode);
            AddDetail("TOTAL CARDS", count.ToString("N0"));
        }

        // ── All / None / Invert ───────────────────────────────────────────────
        private void BtnAll_Click(object sender, RoutedEventArgs e)
        {
            SetAllCheckBoxes(SetTreeView.Items, true);
        }

        private void BtnNone_Click(object sender, RoutedEventArgs e)
        {
            SetAllCheckBoxes(SetTreeView.Items, false);
            SelectedSetCodes.Clear();
        }

        private void BtnInvert_Click(object sender, RoutedEventArgs e)
        {
            InvertCheckBoxes(SetTreeView.Items);
        }

        private void SetAllCheckBoxes(ItemCollection items, bool check)
        {
            foreach (TreeViewItem item in items)
            {
                if (item.Header is StackPanel sp)
                {
                    foreach (var el in sp.Children)
                    {
                        if (el is CheckBox cb)
                        {
                            cb.IsChecked = check;
                            if (cb.Tag is string code)
                            {
                                if (check && !SelectedSetCodes.Contains(code))
                                    SelectedSetCodes.Add(code);
                                else if (!check)
                                    SelectedSetCodes.Remove(code);
                            }
                        }
                    }
                }
                SetAllCheckBoxes(item.Items, check);
            }
        }

        private void InvertCheckBoxes(ItemCollection items)
        {
            foreach (TreeViewItem item in items)
            {
                if (item.Header is StackPanel sp)
                {
                    foreach (var el in sp.Children)
                    {
                        if (el is CheckBox cb)
                        {
                            bool newVal = cb.IsChecked != true;
                            cb.IsChecked = newVal;
                            if (cb.Tag is string code)
                            {
                                if (newVal && !SelectedSetCodes.Contains(code))
                                    SelectedSetCodes.Add(code);
                                else if (!newVal)
                                    SelectedSetCodes.Remove(code);
                            }
                        }
                    }
                }
                InvertCheckBoxes(item.Items);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // TAB 2 — CONDITION BUILDER
        // ════════════════════════════════════════════════════════════════════
        private void RebuildConditionPanel()
        {
            ConditionPanel.Children.Clear();
            RenderNode(_rootNode, ConditionPanel, 0, null);
        }

        private void RenderNode(FilterNode node, Panel parent,
            int depth, FilterNode? parentNode)
        {
            if (node.IsGroup)
            {
                RenderGroup(node, parent, depth, parentNode);
            }
            else
            {
                RenderCondition(node, parent, depth, parentNode);
            }
        }

        private void RenderGroup(FilterNode group, Panel parent,
            int depth, FilterNode? parentNode)
        {
            var groupPanel = new StackPanel
            {
                Margin = new Thickness(depth * 20, 2, 0, 2)
            };

            // Group header row
            var headerRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 2, 0, 2)
            };

            // [...] button
            var menuBtn = new Button
            {
                Content = "...",
                Style = FindResource("ConditionButtonStyle") as Style,
                Width = 28,
                Height = 22,
                Margin = new Thickness(0, 0, 4, 0)
            };
            menuBtn.Click += (s, e) =>
                ShowGroupMenu(group, parentNode, menuBtn);
            headerRow.Children.Add(menuBtn);

            // Group operator button
            var opBtn = new Button
            {
                Content = group.GroupOpDisplay,
                Style = FindResource("GroupOpButtonStyle") as Style,
                Height = 22,
                Margin = new Thickness(0, 0, 4, 0)
            };
            opBtn.Click += (s, e) => ShowGroupOpMenu(group, opBtn);
            headerRow.Children.Add(opBtn);

            // "<root>" or "applies to the following conditions"
            headerRow.Children.Add(new TextBlock
            {
                Text = parentNode == null
                    ? "<root>"
                    : "applies to the following conditions",
                FontSize = 13,
                Foreground = Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0)
            });

            groupPanel.Children.Add(headerRow);

            // Children
            var childPanel = new StackPanel
            {
                Margin = new Thickness(16, 0, 0, 0)
            };

            foreach (var child in group.Children)
                RenderNode(child, childPanel, 0, group);

            groupPanel.Children.Add(childPanel);
            parent.Children.Add(groupPanel);
        }

        private void RenderCondition(FilterNode cond, Panel parent,
            int depth, FilterNode? parentNode)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(depth * 20, 2, 0, 2)
            };

            // [...] button
            var menuBtn = new Button
            {
                Content = "...",
                Style = FindResource("ConditionButtonStyle") as Style,
                Width = 28,
                Height = 22,
                Margin = new Thickness(0, 0, 4, 0)
            };
            menuBtn.Click += (s, e) =>
                ShowConditionMenu(cond, parentNode, menuBtn);
            row.Children.Add(menuBtn);

            // Field button
            var fieldBtn = new Button
            {
                Content = cond.FieldDisplay,
                Style = FindResource("FieldButtonStyle") as Style,
                Height = 22,
                Margin = new Thickness(0, 0, 4, 0)
            };
            fieldBtn.Click += (s, e) => ShowFieldMenu(cond, fieldBtn);
            row.Children.Add(fieldBtn);

            // Operator button
            var opBtn = new Button
            {
                Content = cond.OperatorDisplay,
                Style = FindResource("OperatorButtonStyle") as Style,
                Height = 22,
                Margin = new Thickness(0, 0, 4, 0)
            };
            opBtn.Click += (s, e) => ShowOperatorMenu(cond, opBtn);
            row.Children.Add(opBtn);

            // Value — show inline textbox or button
            if (cond.Operator != FilterOperator.IsBlank &&
                cond.Operator != FilterOperator.IsNotBlank)
            {
                var valBox = new TextBox
                {
                    Text = cond.IsBetweenOp()
                        ? $"{cond.Value} and {cond.Value2}"
                        : cond.Value,
                    MinWidth = 80,
                    Height = 22,
                    FontSize = 13,
                    BorderThickness = new Thickness(1),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(3, 0, 3, 0)
                };
                valBox.LostFocus += (s, e) =>
                {
                    if (cond.IsBetweenOp())
                    {
                        var parts = valBox.Text.Split("and",
                            StringSplitOptions.TrimEntries);
                        cond.Value = parts.Length > 0 ? parts[0] : string.Empty;
                        cond.Value2 = parts.Length > 1 ? parts[1] : string.Empty;
                    }
                    else
                    {
                        cond.Value = valBox.Text;
                    }
                };
                row.Children.Add(valBox);

                if (cond.IsBetweenOp())
                {
                    valBox.ToolTip = "Enter: value1 and value2";
                }

                if (cond.IsAnyOfOp())
                {
                    valBox.ToolTip = "Enter comma-separated values";
                }
            }

            parent.Children.Add(row);
        }

        // ── Context menus ─────────────────────────────────────────────────────
        private void ShowGroupMenu(FilterNode group,
            FilterNode? parent, Button anchor)
        {
            var menu = new ContextMenu();

            var addCond = new MenuItem { Header = "Add Condition" };
            addCond.Click += (s, e) =>
            {
                group.Children.Add(FilterNode.NewCondition(
                    _fields[0].Name, _fields[0].DisplayName));
                RebuildConditionPanel();
            };
            menu.Items.Add(addCond);

            var addGroup = new MenuItem { Header = "Add Group" };
            addGroup.Click += (s, e) =>
            {
                var newGroup = FilterNode.NewGroup();
                newGroup.Children.Add(FilterNode.NewCondition(
                    _fields[0].Name, _fields[0].DisplayName));
                group.Children.Add(newGroup);
                RebuildConditionPanel();
            };
            menu.Items.Add(addGroup);

            if (parent != null)
            {
                menu.Items.Add(new Separator());
                var remove = new MenuItem
                {
                    Header = "Remove Row",
                    Foreground = Brushes.Red
                };
                remove.Click += (s, e) =>
                {
                    parent.Children.Remove(group);
                    RebuildConditionPanel();
                };
                menu.Items.Add(remove);
            }

            menu.IsOpen = true;
        }

        private void ShowConditionMenu(FilterNode cond,
            FilterNode? parent, Button anchor)
        {
            var menu = new ContextMenu();

            if (parent != null)
            {
                var addCond = new MenuItem { Header = "Add Condition" };
                addCond.Click += (s, e) =>
                {
                    int idx = parent.Children.IndexOf(cond);
                    parent.Children.Insert(idx + 1,
                        FilterNode.NewCondition(
                            _fields[0].Name, _fields[0].DisplayName));
                    RebuildConditionPanel();
                };
                menu.Items.Add(addCond);

                var addGroup = new MenuItem { Header = "Add Group" };
                addGroup.Click += (s, e) =>
                {
                    int idx = parent.Children.IndexOf(cond);
                    var newGroup = FilterNode.NewGroup();
                    newGroup.Children.Add(FilterNode.NewCondition(
                        _fields[0].Name, _fields[0].DisplayName));
                    parent.Children.Insert(idx + 1, newGroup);
                    RebuildConditionPanel();
                };
                menu.Items.Add(addGroup);

                menu.Items.Add(new Separator());

                var remove = new MenuItem
                {
                    Header = "Remove Row",
                    Foreground = Brushes.Red
                };
                remove.Click += (s, e) =>
                {
                    parent.Children.Remove(cond);
                    RebuildConditionPanel();
                };
                menu.Items.Add(remove);
            }

            menu.IsOpen = true;
        }

        private void ShowGroupOpMenu(FilterNode group, Button anchor)
        {
            var menu = new ContextMenu();

            foreach (FilterGroupOp op in Enum.GetValues<FilterGroupOp>())
            {
                var item = new MenuItem
                {
                    Header = op switch
                    {
                        FilterGroupOp.And => "AND",
                        FilterGroupOp.Or => "OR",
                        FilterGroupOp.NotAnd => "NOT AND",
                        FilterGroupOp.NotOr => "NOT OR",
                        _ => "AND"
                    },
                    IsChecked = group.GroupOp == op
                };
                var captured = op;
                item.Click += (s, e) =>
                {
                    group.GroupOp = captured;
                    RebuildConditionPanel();
                };
                menu.Items.Add(item);
            }

            anchor.ContextMenu = menu;
            menu.IsOpen = true;
        }

        private void ShowFieldMenu(FilterNode cond, Button anchor)
        {
            var menu = new ContextMenu();

            foreach (var field in _fields)
            {
                var item = new MenuItem
                {
                    Header = field.DisplayName,
                    IsChecked = cond.Field == field.Name
                };
                var captured = field;
                item.Click += (s, e) =>
                {
                    cond.Field = captured.Name;
                    cond.FieldDisplay = captured.DisplayName;
                    // Reset operator to Contains when field changes
                    cond.Operator = FilterOperator.Contains;
                    cond.Value = string.Empty;
                    cond.Value2 = string.Empty;
                    RebuildConditionPanel();
                };
                menu.Items.Add(item);
            }

            anchor.ContextMenu = menu;
            menu.IsOpen = true;
        }

        private void ShowOperatorMenu(FilterNode cond, Button anchor)
        {
            var menu = new ContextMenu();

            // Find field data type
            string dataType = _fields
                .FirstOrDefault(f => f.Name == cond.Field)?.DataType
                ?? "string";

            foreach (var (op, label) in FilterField.GetOperators(dataType))
            {
                var item = new MenuItem
                {
                    Header = label,
                    IsChecked = cond.Operator == op
                };
                var captured = op;
                item.Click += (s, e) =>
                {
                    cond.Operator = captured;
                    cond.Value = string.Empty;
                    cond.Value2 = string.Empty;
                    RebuildConditionPanel();
                };
                menu.Items.Add(item);
            }

            anchor.ContextMenu = menu;
            menu.IsOpen = true;
        }

        // ── Add condition button ──────────────────────────────────────────────
        private void BtnAddCondition_Click(object sender, RoutedEventArgs e)
        {
            _rootNode.Children.Add(FilterNode.NewCondition(
                _fields[0].Name, _fields[0].DisplayName));
            RebuildConditionPanel();
        }

        // ════════════════════════════════════════════════════════════════════
        // OPEN / SAVE
        // ════════════════════════════════════════════════════════════════════
        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Open Filter",
                Filter = "Filter Files (*.filter)|*.filter|All Files (*.*)|*.*",
                InitialDirectory = FilterFolder
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                string json = File.ReadAllText(dlg.FileName);
                var saved = JsonSerializer.Deserialize<SavedFilter>(json);
                if (saved?.RootNode == null)
                    throw new Exception("Invalid filter file.");

                _rootNode = saved.RootNode;
                RebuildConditionPanel();
                SetStatus($"Loaded: {Path.GetFileName(dlg.FileName)}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open filter:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSaveAs_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "Save Filter As",
                Filter = "Filter Files (*.filter)|*.filter|All Files (*.*)|*.*",
                InitialDirectory = FilterFolder,
                FileName = "MyFilter.filter"
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                var saved = new SavedFilter
                {
                    Name = Path.GetFileNameWithoutExtension(dlg.FileName),
                    Context = _context,
                    RootNode = _rootNode.Clone(),
                    SavedAt = DateTime.Now
                };

                string json = JsonSerializer.Serialize(saved,
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(dlg.FileName, json);
                SetStatus($"Saved: {Path.GetFileName(dlg.FileName)}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not save filter:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // BOTTOM BUTTONS
        // ════════════════════════════════════════════════════════════════════
        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            _rootNode = FilterNode.NewGroup(FilterGroupOp.And);
            SelectedSetCodes.Clear();
            RebuildConditionPanel();
            LoadSetTree();
        }

        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "Filter Help\n\n" +
                "Tab 1 — Select specific Blocks, Formats, or Editions to filter by.\n\n" +
                "Tab 2 — Build conditions using AND/OR/NAND/NOR logic.\n" +
                "  Click [...] to add/remove conditions or groups.\n" +
                "  Click the red field name to change what to filter on.\n" +
                "  Click the green operator to change how to compare.\n" +
                "  Type in the value box to set the filter value.\n\n" +
                "Tab 3 — Set case sensitivity and price filter options.\n\n" +
                "Use Open/Save As to save and reload your filters.",
                "Filter Help",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            ResultNode = _rootNode.Clone();
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            ResultNode = _rootNode.Clone();
            // Signal parent to apply without closing
            ApplyRequested?.Invoke(this, EventArgs.Empty);
        }

        // ── Event for Apply (parent can subscribe) ────────────────────────────
        public event EventHandler? ApplyRequested;

        // ════════════════════════════════════════════════════════════════════
        // HELPERS
        // ════════════════════════════════════════════════════════════════════
        private void SetStatus(string msg)
        {
            Title = $"Filter — {msg}";
        }

        private static string SetTypeToDisplay(string setType) =>
            setType switch
            {
                "expansion" => "Expansions",
                "core" => "Core Sets",
                "masters" => "Masters",
                "draft_innovation" => "Draft Innovation",
                "commander" => "Commander",
                "duel_deck" => "Duel Decks",
                "planechase" => "Planechase",
                "archenemy" => "Archenemy",
                "vanguard" => "Vanguard",
                "funny" => "Funny",
                "promo" => "Promo",
                "token" => "Token",
                "memorabilia" => "Memorabilia",
                "box" => "Box Sets",
                "alchemy" => "Alchemy",
                "arsenal" => "Arsenal",
                "from_the_vault" => "From the Vault",
                "masterpiece" => "Masterpiece",
                "premium_deck" => "Premium Deck",
                "spellbook" => "Spellbook",
                "starter" => "Starter",
                "treasure_chest" => "Treasure Chest",
                _ => setType
            };

        private static Brush GetSetTypeBrush(string setType) =>
            setType switch
            {
                "expansion" => new SolidColorBrush(Color.FromRgb(0xCC, 0x66, 0x00)),
                "core" => new SolidColorBrush(Color.FromRgb(0x00, 0x66, 0xCC)),
                "masters" => new SolidColorBrush(Color.FromRgb(0x99, 0x00, 0x99)),
                "commander" => new SolidColorBrush(Color.FromRgb(0x00, 0x88, 0x44)),
                "promo" => new SolidColorBrush(Color.FromRgb(0xCC, 0x44, 0x00)),
                "funny" => new SolidColorBrush(Color.FromRgb(0xCC, 0xAA, 0x00)),
                _ => new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33))
            };
    }

    // ── Extension helpers on FilterNode ──────────────────────────────────────
    public static class FilterNodeExtensions
    {
        public static bool IsBetweenOp(this FilterNode node) =>
            node.Operator == FilterOperator.IsBetween ||
            node.Operator == FilterOperator.IsNotBetween;

        public static bool IsAnyOfOp(this FilterNode node) =>
            node.Operator == FilterOperator.IsAnyOf ||
            node.Operator == FilterOperator.IsNoneOf;
    }
}