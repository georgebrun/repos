using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using BreakersOfE.Services;

namespace BreakersOfE.Views
{
    /// <summary>
    /// Help: the topic list (with search) on the left, the topic on the right.
    /// One Help window at a time; it stays open beside the app while you work
    /// (Help button in the side menu, F1 for the page you're on).
    /// The topics are the files in Resources\Help — see index.txt there.
    /// </summary>
    public partial class HelpWindow : Wpf.Ui.Controls.FluentWindow
    {
        private static HelpWindow? _open;

        /// <summary>The first topic shown.</summary>
        public const string StartTopic = "getting-started";

        private HelpTopic? _current;
        private readonly Stack<HelpTopic> _back = new();
        private bool _selecting;            // the tree is being set from code, not clicked

        /// <summary>Open Help (or bring it to the front) on a topic; null keeps the topic it shows.</summary>
        public static void Open(string? topicId = null)
        {
            if (_open == null)
            {
                _open = new HelpWindow();
                _open.Closed += (_, _) => _open = null;
                // Closing the app closes Help too (it has no owner, so it can sit behind the app).
                if (Application.Current.MainWindow is { } main && !ReferenceEquals(main, _open))
                    main.Closed += (_, _) => _open?.Close();
                _open.Show();
                _open.ShowTopic(HelpTopics.Find(topicId) ?? HelpTopics.Find(StartTopic) ?? HelpTopics.All().FirstOrDefault());
                return;
            }
            if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal;
            _open.Activate();
            if (HelpTopics.Find(topicId) is { } topic) _open.ShowTopic(topic);
        }

        public HelpWindow()
        {
            InitializeComponent();
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) Close();
                else if (e.Key == Key.Back && Keyboard.FocusedElement is not System.Windows.Controls.TextBox && _back.Count > 0) GoBack();
                else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { SearchBox.Focus(); SearchBox.SelectAll(); }
            };
            FillTree(null);
        }

        // ── Topic list and search ──────────────────────────────────────
        /// <summary>The topic list; with search words, only the topics that mention all of them.</summary>
        private void FillTree(string[]? words)
        {
            TopicTree.Items.Clear();
            int found = 0;
            foreach (var t in HelpTopics.Tree)
            {
                if (t.IsGroup)
                {
                    var kids = t.Children.Where(c => Matches(c, words)).ToList();
                    if (kids.Count == 0) continue;
                    var group = new TreeViewItem
                    {
                        Header = Wrapped(t.Title),
                        FontWeight = FontWeights.SemiBold,
                        IsExpanded = true,
                        Focusable = false,
                    };
                    foreach (var c in kids) group.Items.Add(Item(c));
                    TopicTree.Items.Add(group);
                    found += kids.Count;
                }
                else if (Matches(t, words))
                {
                    TopicTree.Items.Add(Item(t));
                    found++;
                }
            }

            if (words == null) SearchCount.Visibility = Visibility.Collapsed;
            else
            {
                SearchCount.Text = found == 0 ? "No topic mentions all of those words."
                                 : found == 1 ? "1 topic" : $"{found} topics";
                SearchCount.Visibility = Visibility.Visible;
            }
            if (_current != null) SelectInTree(_current);
        }

        private static TreeViewItem Item(HelpTopic t) =>
            new() { Header = Wrapped(t.Title), Tag = t, FontWeight = FontWeights.Normal };

        /// <summary>A list title that wraps when the list is narrow (it never scrolls sideways).</summary>
        private static TextBlock Wrapped(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };

        private static bool Matches(HelpTopic t, string[]? words) =>
            words == null || words.All(w =>
                t.Title.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                t.Subtitle.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                t.Body.Contains(w, StringComparison.OrdinalIgnoreCase));

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var words = SearchBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            FillTree(words.Length == 0 ? null : words);
        }

        private void TopicTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_selecting) return;
            if (e.NewValue is TreeViewItem { Tag: HelpTopic topic }) ShowTopic(topic);
        }

        private void SelectInTree(HelpTopic topic)
        {
            _selecting = true;
            try
            {
                foreach (var item in TopicTree.Items.OfType<TreeViewItem>())
                {
                    if (item.Tag == topic) { item.IsSelected = true; item.BringIntoView(); return; }
                    foreach (var child in item.Items.OfType<TreeViewItem>())
                        if (child.Tag == topic)
                        {
                            item.IsExpanded = true;
                            child.IsSelected = true;
                            child.BringIntoView();
                            return;
                        }
                }
            }
            finally { _selecting = false; }
        }

        // ── Showing a topic ────────────────────────────────────────────
        /// <summary>Show a topic; the one shown before goes on the Back list.</summary>
        public void ShowTopic(HelpTopic? topic, bool remember = true)
        {
            if (topic == null || topic.IsGroup || topic == _current) return;
            if (remember && _current != null) _back.Push(_current);
            _current = topic;
            BtnBack.IsEnabled = _back.Count > 0;

            TopicTitle.Text = topic.Title;
            TopicSubtitle.Text = topic.Subtitle;
            TopicSubtitle.Visibility = topic.Subtitle.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            HelpRenderer.Render(topic.Body, ContentPanel, id => ShowTopic(HelpTopics.Find(id)));
            ContentScroll.ScrollToTop();
            SelectInTree(topic);
        }

        private void GoBack()
        {
            if (_back.Count == 0) return;
            ShowTopic(_back.Pop(), remember: false);
            BtnBack.IsEnabled = _back.Count > 0;
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e) => GoBack();
    }

    /// <summary>
    /// Turns a Help topic's text into the page. The text is plain, like an
    /// e-mail (the rules are also at the top of Resources\Help\index.txt):
    ///   ## Heading           a section heading
    ///   - item               a bullet;  "1. step" a numbered step
    ///                        (a line indented two spaces continues the item above)
    ///   Tip: / Note: / Warning:   a box (Warning is amber: something to watch out for)
    ///   **bold**             bold
    ///   [Ctrl+Z]             a key (drawn as a key cap)
    ///   [[add-cards]]        a link to another topic (its title); [[add-cards|text]] with your own text
    ///   a blank line         starts a new paragraph
    /// </summary>
    internal static class HelpRenderer
    {
        private static readonly Regex InlineRx = new(@"\[\[([^\]|]+)(?:\|([^\]]+))?\]\]|\*\*(.+?)\*\*|\[([^\[\]]+)\]", RegexOptions.Compiled);
        private static readonly Regex NumberRx = new(@"^(\d+)\.\s+(.*)$", RegexOptions.Compiled);

        private enum Kind { Paragraph, Heading, Bullet, Number, Tip, Note, Warning }

        public static void Render(string body, Panel target, Action<string> openTopic)
        {
            target.Children.Clear();
            Kind kind = Kind.Paragraph;
            string number = "";
            var text = new List<string>();

            void Flush()
            {
                if (text.Count > 0) target.Children.Add(Block(kind, number, string.Join(" ", text), openTopic));
                text.Clear();
                kind = Kind.Paragraph;
            }

            foreach (string raw in body.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.TrimEnd();
                string t = line.Trim();
                if (t.Length == 0) { Flush(); continue; }

                // A line indented under a list item or box continues it.
                if (text.Count > 0 && kind != Kind.Paragraph && kind != Kind.Heading && line.StartsWith("  "))
                {
                    text.Add(t);
                    continue;
                }

                if (t.StartsWith("## ")) { Flush(); kind = Kind.Heading; text.Add(t[3..]); Flush(); continue; }
                if (t.StartsWith("- ")) { Flush(); kind = Kind.Bullet; text.Add(t[2..]); continue; }
                if (NumberRx.Match(t) is { Success: true } m) { Flush(); kind = Kind.Number; number = m.Groups[1].Value; text.Add(m.Groups[2].Value); continue; }
                if (BoxKind(t) is { } box)
                {
                    Flush();
                    kind = box.Kind;
                    text.Add(t[box.Word.Length..].Trim());
                    continue;
                }

                // Plain text: joins the paragraph (after a list item or box, a new paragraph).
                if (kind != Kind.Paragraph) Flush();
                text.Add(t);
            }
            Flush();
        }

        /// <summary>"Tip: …", "Note: …", "Warning: …": a box of that kind.</summary>
        private static (Kind Kind, string Word)? BoxKind(string t) =>
            t.StartsWith("Tip:", StringComparison.Ordinal) ? (Kind.Tip, "Tip:") :
            t.StartsWith("Note:", StringComparison.Ordinal) ? (Kind.Note, "Note:") :
            t.StartsWith("Warning:", StringComparison.Ordinal) ? (Kind.Warning, "Warning:") : null;

        private static UIElement Block(Kind kind, string number, string text, Action<string> openTopic)
        {
            switch (kind)
            {
                case Kind.Heading:
                {
                    var tb = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 6) };
                    tb.SetResourceReference(FrameworkElement.StyleProperty, "BoeSectionTitle");
                    return tb;
                }
                case Kind.Bullet:
                case Kind.Number:
                {
                    var grid = new Grid { Margin = new Thickness(6, 0, 0, 6) };
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    var mark = new TextBlock
                    {
                        Text = kind == Kind.Bullet ? "•" : number + ".",
                        FontSize = 14,
                        FontWeight = kind == Kind.Number ? FontWeights.SemiBold : FontWeights.Normal,
                    };
                    mark.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
                    var body = Text(text, openTopic);
                    Grid.SetColumn(body, 1);
                    grid.Children.Add(mark);
                    grid.Children.Add(body);
                    return grid;
                }
                case Kind.Tip:
                case Kind.Note:
                case Kind.Warning:
                {
                    var label = new Run(kind == Kind.Tip ? "Tip  " : kind == Kind.Note ? "Note  " : "Watch out  ") { FontWeight = FontWeights.SemiBold };
                    var body = Text(text, openTopic);
                    if (body.Inlines.FirstInline is { } first) body.Inlines.InsertBefore(first, label);
                    else body.Inlines.Add(label);
                    var box = new Border
                    {
                        BorderThickness = new Thickness(3, 0, 0, 0),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(12, 8, 12, 8),
                        Margin = new Thickness(0, 4, 0, 10),
                        Child = body,
                    };
                    box.SetResourceReference(Border.BackgroundProperty, "BoePanelBrush");
                    // Amber only where something needs attention (ISA-101); tips use the accent.
                    box.SetResourceReference(Border.BorderBrushProperty,
                        kind == Kind.Warning ? "BoeWarningBrush" : kind == Kind.Tip ? "AccentFillColorDefaultBrush" : "TextFillColorSecondaryBrush");
                    if (kind == Kind.Warning) label.SetResourceReference(TextElement.ForegroundProperty, "BoeWarningBrush");
                    return box;
                }
                default:
                {
                    var tb = Text(text, openTopic);
                    tb.Margin = new Thickness(0, 0, 0, 10);
                    return tb;
                }
            }
        }

        /// <summary>A wrapped paragraph with **bold**, [keys] and [[links]].</summary>
        private static TextBlock Text(string text, Action<string> openTopic)
        {
            var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 21 };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");

            int at = 0;
            foreach (Match m in InlineRx.Matches(text))
            {
                if (m.Index > at) tb.Inlines.Add(new Run(text[at..m.Index]));
                if (m.Groups[1].Success)
                {
                    string id = m.Groups[1].Value.Trim();
                    string label = m.Groups[2].Success ? m.Groups[2].Value : HelpTopics.Find(id)?.Title ?? id;
                    var link = new Hyperlink(new Run(label)) { ToolTip = "Open this Help topic" };
                    link.SetResourceReference(TextElement.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
                    link.Click += (_, _) => openTopic(id);
                    tb.Inlines.Add(link);
                }
                else if (m.Groups[3].Success)
                {
                    tb.Inlines.Add(new Run(m.Groups[3].Value) { FontWeight = FontWeights.SemiBold });
                }
                else
                {
                    tb.Inlines.Add(Key(m.Groups[4].Value));
                }
                at = m.Index + m.Length;
            }
            if (at < text.Length) tb.Inlines.Add(new Run(text[at..]));
            return tb;
        }

        /// <summary>A key cap: "Ctrl+Z" drawn as keys.</summary>
        private static Inline Key(string keys)
        {
            var cap = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 0, 5, 1),
                Margin = new Thickness(1, 0, 1, 0),
                Child = new TextBlock { Text = keys, FontSize = 12, FontWeight = FontWeights.SemiBold },
            };
            cap.SetResourceReference(Border.BorderBrushProperty, "TextFillColorSecondaryBrush");
            cap.SetResourceReference(Border.BackgroundProperty, "BoePanelBrush");
            ((TextBlock)cap.Child).SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            return new InlineUIContainer(cap) { BaselineAlignment = BaselineAlignment.Center };
        }
    }
}
