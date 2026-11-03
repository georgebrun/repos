using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BreakersOfE.Models;
using BreakersOfE.Playtest;
using BreakersOfE.Services;

namespace BreakersOfE.Views.Playtest
{
    /// <summary>
    /// New Game ▾ → Against Another Deck…: pick the deck across the table from
    /// your decks (the Decks folder), by format then name; type to narrow the list.
    /// </summary>
    internal sealed class OpponentDeckWindow : Window
    {
        private sealed record Choice(string Path, string Name, string Format, int FormatOrder, string Commander, int Count);

        private readonly TextBox _find = new() { Margin = new Thickness(0, 8, 0, 8), Padding = new Thickness(6, 4, 6, 4) };
        private readonly ListBox _list = new();
        private readonly Button _play = new() { Content = "Play Against It", Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(0, 0, 8, 0), IsDefault = true, IsEnabled = false };
        private readonly List<Choice> _all = new();

        /// <summary>The chosen deck's file, or null.</summary>
        private string? _chosen;

        internal static string? Ask(Window owner)
        {
            var w = new OpponentDeckWindow(owner);
            return w.ShowDialog() == true ? w._chosen : null;
        }

        private OpponentDeckWindow(Window owner)
        {
            Owner = owner;
            Title = "Play Against";
            Width = 560;
            Height = 600;
            MinWidth = 420;
            MinHeight = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
            SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
            KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

            var root = new DockPanel { Margin = new Thickness(18, 14, 18, 14) };
            var top = new StackPanel();
            top.Children.Add(new TextBlock { Text = "The deck across the table", FontSize = 18, FontWeight = FontWeights.SemiBold });
            var hint = new TextBlock
            {
                Text = $"Any of your decks. It sits down as {PlaytestSettings.Current.OpponentNameOrDefault()} " +
                       "(their name and picture: ⚙ Table → Player; their sleeves and playmat: ⚙ Table → Look, This deck only).",
                FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0),
            };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            top.Children.Add(hint);
            var findRow = new DockPanel();
            var findLabel = new TextBlock { Text = "Find", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            DockPanel.SetDock(findLabel, Dock.Left);
            findRow.Children.Add(findLabel);
            findRow.Children.Add(_find);
            top.Children.Add(findRow);
            DockPanel.SetDock(top, Dock.Top);
            root.Children.Add(top);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            _play.Click += (_, _) => Choose();
            buttons.Children.Add(_play);
            var cancel = new Button { Content = "Cancel", Padding = new Thickness(16, 4, 16, 4), IsCancel = true };
            buttons.Children.Add(cancel);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
            _list.SelectionChanged += (_, _) => _play.IsEnabled = _list.SelectedItem is ListBoxItem { Tag: string };
            _list.MouseDoubleClick += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(_list, d) is ListBoxItem) Choose();
            };
            root.Children.Add(_list);
            Content = root;

            _find.TextChanged += (_, _) => Fill();
            Read();
            Fill();
            Loaded += (_, _) => _find.Focus();
        }

        /// <summary>Your decks: format, name, commander and card count (the cached read the deck browser uses).</summary>
        private void Read()
        {
            var formats = DeckFormats.All.ToList();
            foreach (var (path, deck) in DeckIndexService.AllDecks())
            {
                try
                {
                    var rule = DeckFormats.For(deck.DeckType);
                    var main = deck.PlayCards.Where(c => c.Category != DeckCardCategory.Sideboard).ToList();
                    if (main.Count == 0) continue;
                    string commander = string.Join(" & ", main.Where(c => c.IsCommander || c.Category == DeckCardCategory.Commander).Select(c => c.Name));
                    string name = string.IsNullOrWhiteSpace(deck.Name) ? System.IO.Path.GetFileNameWithoutExtension(path) : deck.Name;
                    _all.Add(new Choice(path, name, rule.Name, formats.IndexOf(rule), commander, main.Sum(c => c.TotalQuantity)));
                }
                catch
                {
                    // A deck that can't be read isn't offered.
                }
            }
            _all.Sort((a, b) => a.FormatOrder != b.FormatOrder ? a.FormatOrder.CompareTo(b.FormatOrder)
                                                               : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The list, narrowed to what's typed (name, commander or format), under a heading per format.</summary>
        private void Fill()
        {
            string q = _find.Text.Trim();
            _list.Items.Clear();
            string? format = null;
            foreach (var c in _all)
            {
                if (q.Length > 0 && !(c.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                      || c.Commander.Contains(q, StringComparison.OrdinalIgnoreCase)
                                      || c.Format.Contains(q, StringComparison.OrdinalIgnoreCase))) continue;
                if (c.Format != format)
                {
                    format = c.Format;
                    var head = new TextBlock { Text = format.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold, Opacity = 0.8, Margin = new Thickness(2, 10, 0, 2) };
                    _list.Items.Add(new ListBoxItem { Content = head, IsHitTestVisible = false, Focusable = false });
                }
                var body = new StackPanel { Margin = new Thickness(4, 2, 4, 2) };
                body.Children.Add(new TextBlock { Text = c.Name, FontWeight = FontWeights.SemiBold });
                var sub = new TextBlock
                {
                    Text = (c.Commander.Length > 0 ? c.Commander + " · " : "") + $"{c.Count} cards",
                    FontSize = 12,
                };
                sub.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
                body.Children.Add(sub);
                _list.Items.Add(new ListBoxItem { Content = body, Tag = c.Path });
            }
            if (_list.Items.Count == 0)
                _list.Items.Add(new ListBoxItem
                {
                    Content = _all.Count == 0 ? $"No decks yet: decks are kept in {AppFolderService.DecksFolder}" : "No deck matches that.",
                    IsHitTestVisible = false, Focusable = false,
                });
            _play.IsEnabled = false;
        }

        private void Choose()
        {
            if (_list.SelectedItem is not ListBoxItem { Tag: string path }) return;
            _chosen = path;
            DialogResult = true;
        }
    }
}
