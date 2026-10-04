using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using BreakersOfE.Services;
using Microsoft.EntityFrameworkCore;

namespace BreakersOfE.Views.Pages
{
    /// <summary>
    /// Keyword Dictionary (left menu, above Update Database): a reference for
    /// every keyword — keyword abilities, keyword actions and ability words.
    /// Each shows its definition, the official Comprehensive Rules text (once
    /// downloaded), an example card, the cards that have it, and buttons to
    /// see those cards in the Pool or in your collection.
    /// </summary>
    public partial class KeywordDictionaryPage : Page
    {
        /// <summary>One keyword in the list.</summary>
        public sealed class KeywordRow
        {
            public MtgKeyword Keyword { get; init; } = new();
            public string Name => Keyword.Name;
            public string Category => Keyword.CategoryName;
            public int Cards { get; init; }
            public int Owned { get; init; }
            public string CardsText => Cards > 0 ? Cards.ToString("N0") : "";
            /// <summary>Nothing to read about it yet: shown dimmer (still listed).</summary>
            public double TextOpacity => Keyword.HasAnyText || Keyword.Category == KeywordCategory.AbilityWord ? 1.0 : 0.6;
        }

        private List<KeywordRow> _rows = new();
        private ICollectionView? _view;
        private int _exampleVersion;
        private bool _loading, _reloadAfter;
        /// <summary>Card names in your collection, as of the last list build.</summary>
        private static HashSet<string> _owned = new(StringComparer.OrdinalIgnoreCase);

        public KeywordDictionaryPage()
        {
            InitializeComponent();
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            EditPageKit.DisableHostScroll(this);
            await LoadList();
            SearchBox.Focus();
        }

        // ══════════════════════════════════════════════════════════════════
        // LIST
        // ══════════════════════════════════════════════════════════════════
        /// <summary>Every keyword, with how many cards have it (pool and your collection).</summary>
        private async Task LoadList(string? select = null)
        {
            if (_loading) { _reloadAfter = true; return; }      // build again when this one is done
            _loading = true;
            ListCount.Text = "Loading keywords…";
            Detail.Visibility = Visibility.Hidden;
            try
            {
                string? keep = select ?? (KeywordList.SelectedItem as KeywordRow)?.Name;
                _rows = await Task.Run(BuildRows);
                _view = CollectionViewSource.GetDefaultView(_rows);
                _view.GroupDescriptions.Clear();
                _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(KeywordRow.Category)));
                KeywordList.ItemsSource = _view;

                var cats = new List<string> { "All categories" };
                cats.AddRange(_rows.Select(r => r.Category).Distinct().OrderBy(c => c));
                string? keepCat = CategoryBox.SelectedItem as string;
                CategoryBox.ItemsSource = cats;
                CategoryBox.SelectedItem = keepCat != null && cats.Contains(keepCat) ? keepCat : cats[0];

                ApplySearch();
                KeywordList.SelectedItem = _rows.FirstOrDefault(r => r.Name.Equals(keep, StringComparison.OrdinalIgnoreCase))
                                           ?? _view.Cast<KeywordRow>().FirstOrDefault();
                if (KeywordList.SelectedItem != null) KeywordList.ScrollIntoView(KeywordList.SelectedItem);
                ShowRulesStatus();
            }
            catch (Exception ex)
            {
                ListCount.Text = $"Could not load the keywords: {ex.Message}";
            }
            finally
            {
                _loading = false;
            }
            if (_reloadAfter)
            {
                _reloadAfter = false;
                await LoadList(select);
            }
        }

        private static List<KeywordRow> BuildRows()
        {
            KeywordIndex.EnsureLoaded();
            var keywords = MtgKeywordService.All.ToList();
            // Keywords on cards that the saved dictionary doesn't have yet (until the next Update Database).
            var known = new HashSet<string>(keywords.Select(k => k.Name), StringComparer.OrdinalIgnoreCase);
            foreach (string kw in KeywordIndex.AllKeywords())
                if (known.Add(kw))
                    keywords.Add(new MtgKeyword { Name = kw, Category = KeywordCategory.Discovered, Definition = MtgKeyword.NoDefinition });

            var owned = OwnedNames();
            _owned = owned;
            return keywords
                .GroupBy(k => k.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                .Select(k =>
                {
                    var cards = KeywordIndex.CardsWith(k.Name);
                    return new KeywordRow { Keyword = k, Cards = cards.Count, Owned = cards.Count(owned.Contains) };
                })
                .OrderBy(r => r.Category).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Card names in your collection (cards and tokens).</summary>
        private static HashSet<string> OwnedNames()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var db = new Data.CollectionDbContext();
                set.UnionWith(db.CollectionEntries.AsNoTracking().Where(e => e.Quantity > 0).Select(e => e.Name).Distinct().ToList());
                set.UnionWith(db.TokenCollectionEntries.AsNoTracking().Where(e => e.Quantity > 0).Select(e => e.Name).Distinct().ToList());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Keyword dictionary, collection: {ex.Message}");
            }
            return set;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplySearch();

        private void CategoryBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplySearch();

        private void ApplySearch()
        {
            if (_view == null) return;
            string q = SearchBox.Text.Trim();
            string? cat = CategoryBox.SelectedIndex > 0 ? CategoryBox.SelectedItem as string : null;
            _view.Filter = q.Length == 0 && cat == null ? null : o =>
            {
                var r = (KeywordRow)o;
                return (cat == null || r.Category == cat) &&
                       (q.Length == 0 || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
            };
            int shown = _view.Cast<object>().Count();
            ListCount.Text = shown == _rows.Count
                ? $"{_rows.Count:N0} keywords"
                : $"{shown:N0} of {_rows.Count:N0} keywords";
            // Typing: show the first match straight away.
            if (KeywordList.SelectedItem == null || !_view.Contains(KeywordList.SelectedItem))
                KeywordList.SelectedItem = _view.Cast<KeywordRow>().FirstOrDefault();
        }

        // ══════════════════════════════════════════════════════════════════
        // DETAIL
        // ══════════════════════════════════════════════════════════════════
        private void KeywordList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (KeywordList.SelectedItem is not KeywordRow row)
            {
                Detail.Visibility = Visibility.Hidden;
                return;
            }
            Detail.Visibility = Visibility.Visible;
            var k = row.Keyword;
            KwName.Text = k.Name;

            var meta = new List<string> { KindText(k) };
            if (k.RuleNumber.Length > 0) meta.Add($"Rule {k.RuleNumber}");
            meta.Add(row.Cards == 1 ? "1 card" : $"{row.Cards:N0} cards");
            meta.Add($"{row.Owned:N0} in your collection");
            KwMeta.Text = string.Join("  ·  ", meta);

            BtnShowPool.IsEnabled = row.Cards > 0;
            BtnShowCollection.IsEnabled = row.Owned > 0;

            // What it means: BoE's own short definition (or reminder text from a card).
            string definition = k.HasDefinition ? k.Definition : "";
            if (k.Category == KeywordCategory.AbilityWord)
                definition = "An ability word: it has no rules meaning of its own. It names abilities that work alike, " +
                             "so they're easy to find and talk about (Comprehensive Rules 207.2c)." +
                             (definition.Length > 0 ? "\n\n" + definition : "");
            else if (definition.Length == 0 && k.RulesText.Length == 0)
                definition = ComprehensiveRulesService.IsDownloaded
                    ? "No definition yet — the example card below shows how it reads."
                    : "No definition yet. Get the official rules (button above) — they may have it.";
            DefinitionPart.Visibility = definition.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            DefinitionText.Text = definition;

            RulesPart.Visibility = k.RulesText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            RulesHeader.Text = $"OFFICIAL RULES — COMPREHENSIVE RULES {k.RuleNumber}" +
                               (ComprehensiveRulesService.EffectiveDate.Length > 0 ? $" ({ComprehensiveRulesService.EffectiveDate})" : "");
            RulesText.Text = k.RulesText;

            var cards = KeywordIndex.CardsWith(k.Name);
            CardsPart.Visibility = cards.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            CardsHeader.Text = cards.Count == 1 ? "1 CARD HAS IT" : $"{cards.Count:N0} CARDS HAVE IT";
            const int shown = 60;
            CardsText.Text = string.Join("  ·  ", cards.Take(shown)) +
                             (cards.Count > shown ? $"  … and {cards.Count - shown:N0} more (Show in Pool lists them all)" : "");

            _ = ShowExample(k.Name, cards);
        }

        private static string KindText(MtgKeyword k) => k.Category switch
        {
            KeywordCategory.KeywordAction => "Keyword action",
            KeywordCategory.AbilityWord => "Ability word",
            KeywordCategory.Discovered => "Keyword ability",
            KeywordCategory.Other => "Keyword ability",
            _ => $"Keyword ability — {k.CategoryName}",
        };

        /// <summary>One card with the keyword (one you own when there is one), its rules text.</summary>
        private async Task ShowExample(string keyword, IReadOnlyList<string> cards)
        {
            int version = ++_exampleVersion;
            ExamplePart.Visibility = Visibility.Collapsed;
            if (cards.Count == 0) return;
            var owned = _rows.FirstOrDefault(r => r.Name == keyword)?.Owned > 0;
            var card = await Task.Run(() =>
            {
                try
                {
                    string pick = cards[0];
                    if (owned)
                    {
                        var mine = _owned;
                        pick = cards.FirstOrDefault(mine.Contains) ?? pick;
                    }
                    using var db = new Data.AppDbContext();
                    return db.PoolCards.AsNoTracking()
                        .Where(c => c.Name == pick)
                        .Select(c => new { c.Name, c.TypeLine, c.ManaCost, c.OracleText })
                        .FirstOrDefault();
                }
                catch { return null; }
            });
            if (version != _exampleVersion || card == null) return;
            ExamplePart.Visibility = Visibility.Visible;
            ExampleName.Text = card.Name + (string.IsNullOrEmpty(card.ManaCost) ? "" : "   " + card.ManaCost);
            ExampleType.Text = card.TypeLine;
            ExampleText.Text = card.OracleText;
        }

        // ══════════════════════════════════════════════════════════════════
        // SHOW IN POOL / COLLECTION
        // ══════════════════════════════════════════════════════════════════
        private void BtnShowPool_Click(object sender, RoutedEventArgs e) => Show(collection: false);

        private void BtnShowCollection_Click(object sender, RoutedEventArgs e) => Show(collection: true);

        private void Show(bool collection)
        {
            if (KeywordList.SelectedItem is not KeywordRow row) return;
            (Window.GetWindow(this) as MainWindow)?.ShowKeyword(row.Name, collection);
        }

        // ══════════════════════════════════════════════════════════════════
        // OFFICIAL RULES
        // ══════════════════════════════════════════════════════════════════
        private void ShowRulesStatus()
        {
            if (ComprehensiveRulesService.IsDownloaded)
            {
                string date = ComprehensiveRulesService.EffectiveDate;
                int n = ComprehensiveRulesService.Entries.Count;
                EditPageKit.ShowStatus(RulesStatus,
                    $"Official rules: Wizards' Comprehensive Rules{(date.Length > 0 ? $", effective {date}" : "")} " +
                    $"({n:N0} keyword rules). Update Database keeps them current.", false);
                BtnGetRules.Content = "Check for Newer Rules";
            }
            else
            {
                EditPageKit.ShowStatus(RulesStatus,
                    "The official rules text isn't downloaded yet — definitions here are BoE's own and reminder text from cards. " +
                    "Get the official rules for every keyword's Comprehensive Rules entry.", false);
                BtnGetRules.Content = "Get the Official Rules";
            }
        }

        private async void BtnGetRules_Click(object sender, RoutedEventArgs e)
        {
            BtnGetRules.IsEnabled = false;
            string was = ComprehensiveRulesService.EffectiveDate;
            EditPageKit.ShowStatus(RulesStatus, "Downloading the Comprehensive Rules from Wizards…", false);
            try
            {
                string date = await ComprehensiveRulesService.DownloadAsync();
                MtgKeywordService.RulesChanged();
                await LoadList();
                if (date.Length > 0 && date == was)
                    EditPageKit.ShowStatus(RulesStatus, $"You already have the newest rules (effective {date}).", false);
            }
            catch (Exception ex)
            {
                ShowRulesStatus();
                EditPageKit.ShowStatus(RulesStatus,
                    $"Could not get the rules: {ex.Message} Check your internet connection and try again; the keyword definitions still work.", true);
            }
            finally
            {
                BtnGetRules.IsEnabled = true;
            }
        }
    }
}
