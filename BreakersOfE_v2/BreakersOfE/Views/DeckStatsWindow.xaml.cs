using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using BreakersOfE.Models;
using BreakersOfE.ViewModels;
using Wpf.Ui.Controls;

namespace BreakersOfE.Views
{
    /// <summary>
    /// Deck statistics: Overview, Mana (curve + color symbols vs. sources),
    /// and Format Check (the deck format's rules — DeckFormats / DeckRulesService). Read-only; always the
    /// whole deck (grid filters don't apply). Main deck = everything except
    /// the sideboard, commander included.
    /// </summary>
    public partial class DeckStatsWindow : FluentWindow
    {
        private const double MaxBar = 260;

        private readonly Deck _deck;
        /// <summary>Commander (the Bracket tab is Commander's).</summary>
        private readonly bool _isCommander;
        /// <summary>A command-zone format (Commander, Brawl, Oathbreaker, …): identity comes from the leader.</summary>
        private readonly bool _hasLeader;
        private readonly DeckFormatRule _rule;
        private readonly List<DeckCard> _main;
        private readonly List<DeckCard> _side;
        private readonly List<DeckCard> _commanders;

        /// <param name="constructedFormat">Constructed decks: the format to check against (null = the deck's own).</param>
        public DeckStatsWindow(Deck deck, Window? owner = null, string? constructedFormat = null)
        {
            InitializeComponent();
            if (owner != null) Owner = owner;
            KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape) Close();
                if (e.Key == System.Windows.Input.Key.F1) HelpWindow.Open("statistics");   // Help for this window
            };

            _deck = deck;
            _isCommander = deck.DeckType == DeckType.Commander;
            _rule = DeckFormats.For(deck, constructedFormat);
            _hasLeader = _rule.HasLeader;
            // Tokens (the deck's Tokens part) aren't part of the deck's statistics.
            _side = deck.PlayCards.Where(c => c.Category == DeckCardCategory.Sideboard).ToList();
            _main = deck.PlayCards.Where(c => c.Category != DeckCardCategory.Sideboard).ToList();
            // A commander is marked either way: the IsCommander flag or the Commander category.
            _commanders = deck.Cards.Where(IsCommanderCard).ToList();

            WindowTitleBar.Title = $"Deck Statistics — {deck.Name}";
            DeckNameText.Text = deck.Name;
            ScopeText.Text = $"{_rule.Name} deck" +
                (_commanders.Count > 0 ? $" · {string.Join(" & ", _commanders.Select(c => c.Name))}" : "") +
                " · whole deck (grid filters don't apply)";

            BuildOverview();
            BuildMana();
            BuildDrawOdds();
            BuildSampleHand();
            BuildCombos();
            BuildBracket();
            if (!_hasLeader || _commanders.Count == 0) EdhrecTab.Visibility = Visibility.Collapsed;
            BuildFormatCheck();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private static bool IsCommanderCard(DeckCard c) =>
            c.IsCommander || c.Category == DeckCardCategory.Commander;

        private static int Qty(IEnumerable<DeckCard> cards) => cards.Sum(c => c.TotalQuantity);

        // ══════════════════════════════════════════════════════════════════
        // OVERVIEW
        // ══════════════════════════════════════════════════════════════════
        private void BuildOverview()
        {
            // Color identity: the commander's for Commander decks, otherwise
            // every color used by the main deck. Shown as mana symbols.
            var identitySource = _hasLeader && _commanders.Count > 0 ? _commanders : _main;
            var identity = new HashSet<char>(identitySource.SelectMany(c => c.ColorIdentity));
            string symbols = string.Concat("WUBRG".Where(identity.Contains).Select(c => $"{{{c}}}"));
            if (symbols.Length == 0) symbols = "{C}";
            IdentityLabel.Text = _hasLeader && _commanders.Count > 0
                ? "Commander color identity" : "Color identity";
            IdentitySymbols.Content = new Services.ManaCostConverter().Convert(
                symbols, typeof(object), null!, System.Globalization.CultureInfo.CurrentCulture);

            int cards = Qty(_main);
            int lands = Qty(_main.Where(c => c.IsLand));
            int creatures = Qty(_main.Where(c => c.IsCreature && !c.IsLand));
            var nonLand = _main.Where(c => !c.IsLand).ToList();
            int nonLandQty = Qty(nonLand);
            double avg = nonLandQty > 0
                ? nonLand.Sum(c => c.ManaValue * c.TotalQuantity) / nonLandQty : 0;
            decimal value = _deck.Cards.Sum(c => c.RowValue);

            var summary = new List<SummaryItem>
            {
                new("Main deck", cards.ToString("N0")),
                new("Lands", cards > 0 ? $"{lands:N0} ({lands * 100.0 / cards:0}%)" : "0"),
                new("Creatures", creatures.ToString("N0")),
                new("Other spells", (nonLandQty - creatures).ToString("N0")),
                new("Avg mana value", nonLandQty > 0 ? avg.ToString("0.00") : "—"),
                new("Deck value", $"${value:N2}"),
            };
            if (!_hasLeader || _side.Count > 0)
                summary.Insert(1, new("Sideboard", Qty(_side).ToString("N0")));
            SummaryList.ItemsSource = summary;

            // Same groups as Collection Statistics (StatBuckets).
            ColorBars.ItemsSource = Bars(_main, c => StatBuckets.Color(c.TypeLine, c.ColorDisplay), StatBuckets.ColorOrder, withValue: true);
            TypeBars.ItemsSource = Bars(_main, c => StatBuckets.Type(c.TypeLine), StatBuckets.TypeOrder, withValue: true);
            RarityBars.ItemsSource = Bars(_main, c => StatBuckets.Rarity(c.Rarity), StatBuckets.RarityOrder, withValue: true);

            var editions = _main
                .GroupBy(c => string.IsNullOrWhiteSpace(c.SetName) ? c.SetCode : c.SetName)
                .Select(g => (label: g.Key, count: Qty(g), extra: $"${g.Sum(c => c.RowValue):N2}"))
                .OrderByDescending(x => x.count).ThenBy(x => x.label)
                .Take(10).ToList();
            EditionBars.ItemsSource = ToBars(editions);

            BuildSalt();
        }

        /// <summary>The main deck's saltiest cards (EDHREC's 0–4 vote), top 10.</summary>
        private void BuildSalt()
        {
            if (!Services.EdhrecService.HasSalt)
            {
                SaltList.ItemsSource = null;
                SaltNote.Text = "Salt scores aren't downloaded yet — run Update Database.";
                return;
            }
            var salty = _main.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(n => (name: n, salt: Services.EdhrecService.SaltOf(n)))
                .Where(x => x.salt != null)
                .OrderByDescending(x => x.salt!.Value.Score)
                .ToList();
            SaltList.ItemsSource = salty.Take(10).Select(x => new StatBar
            {
                Label = x.name,
                CountText = x.salt!.Value.Score.ToString("0.00"),
                BarWidth = Math.Max(2, MaxBar * x.salt.Value.Score / 4),
            }).ToList();
            double lowest = Services.EdhrecService.SaltLowest;
            SaltNote.Text = salty.Count == 0
                ? $"No card in the main deck scores {lowest:0.0} or more: a friendly deck to sit across from."
                : $"{salty.Count} card{(salty.Count == 1 ? "" : "s")} in the main deck score {lowest:0.0} or more (of 4) on EDHREC's salt vote" +
                  (salty.Count > 10 ? "; the 10 saltiest are shown." : ".") +
                  " Salt is how much Commander players dislike facing a card — worth a mention before the game.";
        }

        // ══════════════════════════════════════════════════════════════════
        // MANA
        // ══════════════════════════════════════════════════════════════════
        private static readonly (char code, string name)[] Colors =
        {
            ('W', "White"), ('U', "Blue"), ('B', "Black"), ('R', "Red"), ('G', "Green"), ('C', "Colorless"),
        };

        private void BuildMana()
        {
            // ── Curve: non-land main-deck cards by mana value (7+ grouped) ──
            var nonLand = _main.Where(c => !c.IsLand).ToList();
            var curve = new List<(string label, int count, string extra)>();
            for (int mv = 0; mv <= 7; mv++)
            {
                var at = nonLand.Where(c => mv < 7 ? (int)c.ManaValue == mv : c.ManaValue >= 7).ToList();
                int n = Qty(at);
                int creatures = Qty(at.Where(c => c.IsCreature));
                curve.Add((mv < 7 ? mv.ToString() : "7+", n, n > 0 ? $"{creatures} creature{(creatures == 1 ? "" : "s")}" : ""));
            }
            CurveBars.ItemsSource = ToBars(curve);
            int nonLandQty = Qty(nonLand);
            double avg = nonLandQty > 0 ? nonLand.Sum(c => c.ManaValue * c.TotalQuantity) / nonLandQty : 0;
            CurveNote.Text = nonLandQty > 0
                ? $"Average mana value {avg:0.00} across {nonLandQty} non-land cards. Right-hand number: creatures at that cost."
                : "No non-land cards.";

            // ── Lands summary ──
            var lands = _main.Where(c => c.IsLand).ToList();
            int landQty = Qty(lands);
            int basics = Qty(lands.Where(c => c.IsBasicLand));
            int cards = Qty(_main);
            LandSummary.ItemsSource = new List<SummaryItem>
            {
                new("Lands", landQty.ToString("N0")),
                new("% of deck", cards > 0 ? $"{landQty * 100.0 / cards:0}%" : "—"),
                new("Basic", basics.ToString("N0")),
                new("Non-basic", (landQty - basics).ToString("N0")),
                new("Other mana sources", Qty(_main.Where(c => !c.IsLand && ProducedColors(c).Count > 0)).ToString("N0")),
            };

            // ── Colors: symbols in costs vs. cards that make the color ──
            var pips = new Dictionary<char, int>();
            var landSources = new Dictionary<char, int>();
            var otherSources = new Dictionary<char, int>();
            foreach (var (code, _) in Colors) { pips[code] = 0; landSources[code] = 0; otherSources[code] = 0; }

            foreach (var card in _main)
            {
                foreach (var kv in CostPips(card.ManaCost))
                    pips[kv.Key] += kv.Value * card.TotalQuantity;

                var makes = ProducedColors(card);
                var target = card.IsLand ? landSources : otherSources;
                foreach (char c in makes) target[c] += card.TotalQuantity;
            }

            int totalPips = pips.Where(p => p.Key != 'C').Sum(p => p.Value);
            var rows = new List<ColorRow>();
            foreach (var (code, name) in Colors)
            {
                int p = pips[code], ls = landSources[code], os = otherSources[code];
                if (p == 0 && ls == 0 && os == 0) continue;

                double pipShare = code != 'C' && totalPips > 0 ? p * 100.0 / totalPips : 0;
                double landShare = landQty > 0 ? ls * 100.0 / landQty : 0;

                string check;
                if (p == 0) check = "Not needed";
                else if (ls + os == 0) check = "No sources";
                else if (code != 'C' && landShare + 10 < pipShare) check = "Short";
                else check = "OK";

                rows.Add(new ColorRow
                {
                    Color = name,
                    Pips = p.ToString("N0"),
                    PipShare = code == 'C' || totalPips == 0 ? "—" : $"{pipShare:0}%",
                    LandSources = ls.ToString("N0"),
                    LandShare = landQty > 0 ? $"{landShare:0}%" : "—",
                    OtherSources = os.ToString("N0"),
                    Check = check,
                    ChipBackground = check switch
                    {
                        "OK" => LegalityInfo.BackgroundBrush("legal"),
                        "Short" => LegalityInfo.BackgroundBrush("banned"),       // amber
                        "No sources" => LegalityInfo.BackgroundBrush("not_legal"), // red
                        _ => Brushes.Transparent,
                    },
                    ChipForeground = check switch
                    {
                        "OK" => LegalityInfo.ForegroundBrush("legal"),
                        "Short" => LegalityInfo.ForegroundBrush("banned"),
                        "No sources" => LegalityInfo.ForegroundBrush("not_legal"),
                        _ => LegalityInfo.ForegroundBrush(""),
                    },
                });
            }
            ColorGrid.ItemsSource = rows;
        }

        private static readonly Regex SymbolRx = new(@"\{([^}]+)\}", RegexOptions.Compiled);

        /// <summary>Colored symbols in a mana cost. Hybrid ({W/U}) counts for each color; {C} counts as colorless.</summary>
        private static Dictionary<char, int> CostPips(string manaCost)
        {
            var result = new Dictionary<char, int>();
            if (string.IsNullOrWhiteSpace(manaCost)) return result;
            // Two-sided cards ("{1}{R} // {5}{B}{R}"): the front side, like the mana value.
            foreach (Match m in SymbolRx.Matches(Models.CardFaces.Sides(manaCost)[0]))
            {
                string sym = m.Groups[1].Value.ToUpperInvariant();
                foreach (char c in "WUBRG")
                    if (sym.Contains(c)) result[c] = result.GetValueOrDefault(c) + 1;
                if (sym == "C") result['C'] = result.GetValueOrDefault('C') + 1;
            }
            return result;
        }

        private static readonly (string word, char code)[] BasicTypes =
        {
            ("Plains", 'W'), ("Island", 'U'), ("Swamp", 'B'), ("Mountain", 'R'), ("Forest", 'G'),
        };

        /// <summary>
        /// Colors a card can produce, from its rules text: "Add {G}", "any
        /// color", and (for lands) basic land types in its type line or rules
        /// text (fetch lands find those). Non-land cards count only if they add mana.
        /// </summary>
        private static HashSet<char> ProducedColors(DeckCard card)
        {
            var colors = new HashSet<char>();
            string text = card.OracleText ?? "";
            bool addsMana = text.Contains("Add ", StringComparison.OrdinalIgnoreCase) ||
                            text.Contains("add one mana", StringComparison.OrdinalIgnoreCase);

            if (addsMana)
            {
                if (text.Contains("any color", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("any one color", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("mana of any type", StringComparison.OrdinalIgnoreCase))
                    foreach (char c in "WUBRG") colors.Add(c);

                // Symbols in the sentences that add mana.
                foreach (var sentence in text.Split('\n', '.'))
                {
                    if (!sentence.Contains("Add ", StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (Match m in SymbolRx.Matches(sentence))
                    {
                        string sym = m.Groups[1].Value.ToUpperInvariant();
                        foreach (char c in "WUBRGC")
                            if (sym == c.ToString()) colors.Add(c);
                    }
                }
            }

            if (card.IsLand)
            {
                // Basic land types (typed duals, fetch lands that name them).
                string where = card.TypeLine + " " + text;
                foreach (var (word, code) in BasicTypes)
                    if (where.Contains(word, StringComparison.OrdinalIgnoreCase)) colors.Add(code);
            }
            return colors;
        }

        // ══════════════════════════════════════════════════════════════════
        // DRAW ODDS — hypergeometric chance of having at least N of the picked
        // cards by each turn. Library = main deck minus the commander(s).
        // ══════════════════════════════════════════════════════════════════
        private List<OddsCard> _oddsCards = new();
        private int _librarySize;

        private void BuildDrawOdds()
        {
            var library = _main.Where(c => !IsCommanderCard(c)).ToList();
            _librarySize = Qty(library);

            _oddsCards = library
                .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => new OddsCard
                {
                    Name = g.Key,
                    Count = Qty(g),
                    IsLand = g.First().IsLand,
                    IsCreature = g.First().IsCreature && !g.First().IsLand,
                })
                .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            OddsCardList.ItemsSource = _oddsCards;

            OddsAtLeast.ItemsSource = Enumerable.Range(1, 7).ToList();
            OddsAtLeast.SelectedIndex = 0;   // fires OddsOption_Changed → UpdateOdds
            UpdateOdds();
        }

        private void OddsCard_Click(object sender, RoutedEventArgs e) => UpdateOdds();
        private void OddsOption_Changed(object sender, RoutedEventArgs e)
        {
            if (IsInitialized && _oddsCards.Count > 0) UpdateOdds();
        }

        private void OddsPickLands_Click(object sender, RoutedEventArgs e) => PickOdds(o => o.IsLand);
        private void OddsPickCreatures_Click(object sender, RoutedEventArgs e) => PickOdds(o => o.IsCreature);
        private void OddsClear_Click(object sender, RoutedEventArgs e) => PickOdds(_ => false);

        private void PickOdds(Func<OddsCard, bool> pick)
        {
            foreach (var o in _oddsCards) o.IsSelected = pick(o);
            UpdateOdds();
        }

        private void UpdateOdds()
        {
            if (OddsBars == null) return;
            int copies = _oddsCards.Where(o => o.IsSelected).Sum(o => o.Count);
            int picked = _oddsCards.Count(o => o.IsSelected);
            int atLeast = OddsAtLeast.SelectedItem is int n ? n : 1;
            bool onDraw = OddsOnDraw.IsChecked == true;

            OddsSelectedText.Text = picked == 0
                ? "Tick one or more cards (they count as one group)."
                : $"{picked} card name(s) selected · {copies} copies in a {_librarySize}-card library";

            if (copies == 0 || _librarySize == 0)
            {
                OddsBars.ItemsSource = null;
                OddsNote.Text = "";
                return;
            }

            var rows = new List<(string label, double pct, int seen)>();
            rows.Add(("Opening hand", AtLeastChance(_librarySize, copies, Math.Min(7, _librarySize), atLeast), 7));
            for (int turn = 1; turn <= 10; turn++)
            {
                // Seen by your draw step on this turn: 7 + draws so far. On the
                // play you skip the turn-1 draw.
                int seen = 7 + (onDraw ? turn : turn - 1);
                seen = Math.Min(seen, _librarySize);
                rows.Add(($"Turn {turn}", AtLeastChance(_librarySize, copies, seen, atLeast), seen));
            }

            OddsBars.ItemsSource = rows.Select(r => new StatBar
            {
                Label = r.label,
                CountText = $"{r.pct:0.#}%",
                ExtraText = $"{r.seen} cards seen",
                BarWidth = r.pct <= 0 ? 0 : Math.Max(2, MaxBar * r.pct / 100),
            }).ToList();

            OddsNote.Text =
                $"Chance of at least {atLeast} of the selected cards among the cards seen by that turn's draw step " +
                $"({(onDraw ? "on the draw" : "on the play: no draw on turn 1")}). Mulligans, scry and card draw aren't counted.";
        }

        /// <summary>Hypergeometric: P(at least k successes) drawing n from a deck of N with K successes, in %.</summary>
        private static double AtLeastChance(int N, int K, int n, int k)
        {
            if (k <= 0) return 100;
            if (K < k || n < k) return 0;
            double below = 0;
            for (int i = 0; i < k; i++)
                below += Choose(K, i) * Choose(N - K, n - i) / Choose(N, n);
            return Math.Clamp((1 - below) * 100, 0, 100);
        }

        private static double Choose(int n, int k)
        {
            if (k < 0 || k > n) return 0;
            k = Math.Min(k, n - k);
            double r = 1;
            for (int i = 1; i <= k; i++) r = r * (n - k + i) / i;
            return r;
        }

        public sealed class OddsCard : System.ComponentModel.INotifyPropertyChanged
        {
            public string Name { get; init; } = "";
            public int Count { get; init; }
            public bool IsLand { get; init; }
            public bool IsCreature { get; init; }
            public string Label => $"{Name}  ×{Count}";

            private bool _isSelected;
            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected == value) return;
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }

            public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        }

        // ══════════════════════════════════════════════════════════════════
        // SAMPLE HAND — shuffle, draw 7, London mulligan, draw next.
        // ══════════════════════════════════════════════════════════════════
        private List<DeckCard> _libraryCards = new();
        private readonly List<DeckCard> _hand = new();
        private int _mulligans;
        private const double HandCardWidth = 150;
        private const double CardAspect = 680.0 / 488.0;

        private void BuildSampleHand()
        {
            // One entry per physical copy; the commander stays in the command zone.
            _libraryCards = _main.Where(c => !IsCommanderCard(c))
                .SelectMany(c => Enumerable.Repeat(c, c.TotalQuantity))
                .ToList();
            NewHand();
        }

        private void HandNew_Click(object sender, RoutedEventArgs e) { _mulligans = 0; NewHand(); }
        private void HandMulligan_Click(object sender, RoutedEventArgs e) { _mulligans++; NewHand(); }

        private void HandDraw_Click(object sender, RoutedEventArgs e)
        {
            if (_shuffled.Count == 0) return;
            _hand.Add(_shuffled[0]);
            _shuffled.RemoveAt(0);
            ShowHand();
        }

        private List<DeckCard> _shuffled = new();

        private void NewHand()
        {
            _shuffled = _libraryCards.OrderBy(_ => Random.Shared.Next()).ToList();
            _hand.Clear();
            int take = Math.Min(7, _shuffled.Count);
            _hand.AddRange(_shuffled.Take(take));
            _shuffled.RemoveRange(0, take);
            ShowHand();
        }

        private void ShowHand()
        {
            HandCards.ItemsSource = _hand.Select(c =>
            {
                var gi = GalleryItem.FromCard(c);
                gi.TileWidth = HandCardWidth;
                gi.TileHeight = Math.Round(HandCardWidth * CardAspect);
                return gi;
            }).ToList();

            int lands = _hand.Count(c => c.IsLand);
            string info = $"{_hand.Count} cards in hand · {lands} land{(lands == 1 ? "" : "s")} · {_shuffled.Count} left in library";
            if (_mulligans > 0)
                info += $" · Mulligan {_mulligans}: put {_mulligans} card{(_mulligans == 1 ? "" : "s")} on the bottom";
            HandInfo.Text = info;
            BtnDraw.IsEnabled = _shuffled.Count > 0;
            BtnMulligan.IsEnabled = _mulligans < 7;
        }

        // ══════════════════════════════════════════════════════════════════
        // COMBOS — Commander Spellbook's combos (combos.db, from Update
        // Database): the ones the main deck already has, and the ones it's a
        // single card short of, with how many free copies of that card you own.
        // ══════════════════════════════════════════════════════════════════
        private const int NearShown = 100;
        private List<Services.Combo> _completeCombos = new();
        private List<ComboRow> _nearRows = new();

        private void BuildCombos()
        {
            if (!Services.DeckDataService.HasCombos)
            {
                ComboHeadline.Text = "Combos not downloaded yet";
                ComboReason.Text = "Run Update Database: it downloads the combos from Commander Spellbook.";
                CompleteHeader.Visibility = NearHeader.Visibility = NearOptions.Visibility = Visibility.Collapsed;
                return;
            }

            // The main deck's names (commander included), and the colors a combo may use.
            var names = _main.Select(c => c.Name).Where(n => !string.IsNullOrWhiteSpace(n))
                             .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var have = new HashSet<string>(names.Select(Services.DeckDataService.FrontName), StringComparer.OrdinalIgnoreCase);
            var identitySource = _hasLeader && _commanders.Count > 0 ? _commanders : _main;
            var identity = new HashSet<char>(identitySource.SelectMany(c => c.ColorIdentity));
            bool Fits(Services.Combo c) =>
                c.Identity.Where(ch => "WUBRG".Contains(ch)).All(identity.Contains) &&
                (!_isCommander || c.CommanderLegal);

            var all = Services.DeckDataService.CombosNear(names, 1);
            _completeCombos = all.Where(c => c.Cards.All(n => have.Contains(Services.DeckDataService.FrontName(n)))).ToList();

            // One card away: exactly one card missing, in the deck's colors.
            var near = all.Where(c => !_completeCombos.Contains(c) && Fits(c))
                          .Select(c => (combo: c, missing: c.Cards.Where(n => !have.Contains(Services.DeckDataService.FrontName(n)))
                                                                  .Distinct(StringComparer.OrdinalIgnoreCase).ToList()))
                          .Where(x => x.missing.Count == 1)
                          .ToList();
            var owned = Services.CollectionEditService.OwnedByName(near.Select(x => x.missing[0]));
            _nearRows = near.Select(x =>
            {
                string miss = x.missing[0];
                var o = owned.GetValueOrDefault(Services.DeckDataService.FrontName(miss));
                var row = ComboRow.From(x.combo);
                row.MissingText = o.Free > 0 ? $"Missing: {miss} — {o.Free} free in your collection"
                                : o.Owned > 0 ? $"Missing: {miss} — you own {o.Owned}, all in use"
                                : $"Missing: {miss} — not in your collection";
                row.MissingLevel = o.Free > 0 ? "legal" : o.Owned > 0 ? "banned" : "not_legal";
                row.FreeCopies = o.Free;
                return row;
            })
            .OrderByDescending(r => r.FreeCopies > 0).ThenByDescending(r => r.Popularity)
            .ToList();

            CompleteCombos.ItemsSource = _completeCombos.Select(ComboRow.From).ToList();
            CompleteEmpty.Text = _completeCombos.Count == 0 ? "No complete combos in the main deck." : "";
            CompleteEmpty.Visibility = _completeCombos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            int free = _nearRows.Count(r => r.FreeCopies > 0);
            ComboHeadline.Text = $"{_completeCombos.Count:N0} combo{(_completeCombos.Count == 1 ? "" : "s")} in the deck";
            ComboReason.Text = $"{_nearRows.Count:N0} more are one card away" +
                (_nearRows.Count > 0 ? $"; {free:N0} of those you could finish from your collection's free copies." : ".");
            ShowNear();
        }

        private void NearFreeOnly_Click(object sender, RoutedEventArgs e) => ShowNear();

        private void ShowNear()
        {
            if (NearCombos == null) return;
            var rows = NearFreeOnly.IsChecked == true ? _nearRows.Where(r => r.FreeCopies > 0).ToList() : _nearRows;
            NearCombos.ItemsSource = rows.Take(NearShown).ToList();
            NearNote.Text = rows.Count == 0
                ? (NearFreeOnly.IsChecked == true ? "None you have free copies for." : "None in the deck's colors.")
                : rows.Count > NearShown ? $"Showing the {NearShown} most popular of {rows.Count:N0}. Free in your collection first." : "";
            NearNote.Visibility = NearNote.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ComboLink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
            catch { /* no browser */ }
            e.Handled = true;
        }

        // ══════════════════════════════════════════════════════════════════
        // EDHREC — the commander's recommended cards (EdhrecService: saved for
        // 7 days), marked in the deck / free / in use / not owned. Loaded the
        // first time the tab is opened, so the window opens without waiting.
        // ══════════════════════════════════════════════════════════════════
        private bool _edhrecLoaded;
        private bool _edhrecLoading;
        private List<EdhrecRow> _edhrecRows = new();

        private void StatsTabs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(e.OriginalSource, StatsTabs)) return;     // a combo box or grid inside a tab
            if (StatsTabs.SelectedItem == EdhrecTab && !_edhrecLoaded) _ = LoadEdhrecAsync(refresh: false);
        }

        private void EdhrecRefresh_Click(object sender, RoutedEventArgs e) => _ = LoadEdhrecAsync(refresh: true);

        private async System.Threading.Tasks.Task LoadEdhrecAsync(bool refresh)
        {
            if (_edhrecLoading) return;
            _edhrecLoading = true;
            BtnEdhrecRefresh.IsEnabled = false;
            var names = _commanders.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            EdhrecHeadline.Text = "EDHREC";
            EdhrecInfo.Text = refresh ? "Getting the latest from EDHREC…" : "Loading…";
            try
            {
                var page = await Services.EdhrecService.GetCommanderAsync(names, refresh, System.Threading.CancellationToken.None);

                // Mark each card: in the deck, else what you own (off the UI thread: the collection lookup).
                var have = new HashSet<string>(_deck.Cards.Select(c => Services.DeckDataService.FrontName(c.Name)), StringComparer.OrdinalIgnoreCase);
                var owned = await System.Threading.Tasks.Task.Run(() =>
                    Services.CollectionEditService.OwnedByName(page.Cards.Select(c => c.Name)));
                _edhrecRows = page.Cards.Select(c =>
                {
                    string front = Services.DeckDataService.FrontName(c.Name);
                    var o = owned.GetValueOrDefault(front);
                    return EdhrecRow.From(c, have.Contains(front), o.Owned, o.Free);
                }).ToList();

                EdhrecPageLink.NavigateUri = new Uri(page.Url);
                EdhrecHeadline.Text = $"{string.Join(" + ", names)} on EDHREC";
                int notIn = _edhrecRows.Count(r => !r.InDeck);
                int free = _edhrecRows.Count(r => !r.InDeck && r.Free > 0);
                EdhrecInfo.Text =
                    (page.Decks > 0 ? $"About {page.Decks:N0} decks · " : "") +
                    $"{_edhrecRows.Count:N0} cards recommended · {notIn:N0} not in your deck, {free:N0} of those free in your collection. " +
                    (page.Note.Length > 0 ? page.Note : $"Saved {page.Fetched:g}.");

                var sections = new List<string> { "All sections" };
                sections.AddRange(page.Sections);
                EdhrecSectionFilter.ItemsSource = sections;
                EdhrecSectionFilter.SelectedIndex = 0;
                _edhrecLoaded = true;
                ShowEdhrec();
            }
            catch (Exception ex)
            {
                EdhrecHeadline.Text = "EDHREC unavailable";
                EdhrecInfo.Text = ex.Message + " Try Refresh later.";
                EdhrecGrid.ItemsSource = null;
            }
            finally
            {
                _edhrecLoading = false;
                BtnEdhrecRefresh.IsEnabled = true;
            }
        }

        private void EdhrecFilter_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (IsInitialized) ShowEdhrec();
        }

        private void ShowEdhrec()
        {
            if (EdhrecGrid == null || EdhrecStatusFilter == null || EdhrecSectionFilter == null) return;
            IEnumerable<EdhrecRow> rows = _edhrecRows;
            rows = EdhrecStatusFilter.SelectedIndex switch
            {
                1 => rows.Where(r => !r.InDeck),
                2 => rows.Where(r => !r.InDeck && r.Free > 0),
                3 => rows.Where(r => r.InDeck),
                _ => rows,
            };
            if (EdhrecSectionFilter.SelectedIndex > 0 && EdhrecSectionFilter.SelectedItem is string section)
                rows = rows.Where(r => r.Section == section);
            EdhrecGrid.ItemsSource = rows.ToList();
        }

        private void EdhrecGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (EdhrecGrid.SelectedItem is EdhrecRow row) Services.CardLinks.Open(Services.CardLinks.Edhrec(row.Name));
        }

        // ══════════════════════════════════════════════════════════════════
        // BRACKET — Commander Brackets (beta) estimate from the cards:
        // Game Changers (Scryfall flag), extra turns and mass land denial
        // (rules text). Gives the MINIMUM bracket the cards allow; intent
        // (1 vs 2, 4 vs 5) and two-card combos aren't judged.
        // ══════════════════════════════════════════════════════════════════

        private static readonly Regex ExtraTurnRx =
            new(@"\bextra turns?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Best-guess mass land denial patterns (flagged "possible").
        private static readonly Regex[] LandDenialRx =
        {
            new(@"\b(destroy|exile) all\b[^.]*\blands\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"\beach (player|opponent) sacrifices\b[^.]*\blands?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"\breturn all lands\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"\blands don't untap\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"\bcan't untap more than\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"\blands (your opponents control )?(don't|do not) untap\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        };

        private void BuildBracket()
        {
            if (!_isCommander)
            {
                BracketTab.Visibility = Visibility.Collapsed;
                return;
            }

            static string Line(DeckCard c) => $"{c.Name} ({c.SetCode})";

            var gameChangers = _main.Where(c => c.IsGameChanger)
                .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                .OrderBy(c => c.Name).ToList();
            var extraTurns = _main.Where(c => ExtraTurnRx.IsMatch(c.OracleText ?? "") &&
                                              !(c.OracleText ?? "").Contains("can't take extra turns",
                                                    StringComparison.OrdinalIgnoreCase))
                .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                .OrderBy(c => c.Name).ToList();
            var landDenial = _main.Where(c => LandDenialRx.Any(rx => rx.IsMatch(c.OracleText ?? "")))
                .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                .OrderBy(c => c.Name).ToList();

            int gc = gameChangers.Count;
            var checks = new List<CheckResult>();

            // Game Changers
            checks.Add(gc == 0
                ? new CheckResult { Title = "Game Changers", IsOk = true, Status = "None",
                                    Summary = "No Game Changers — fits Brackets 1–2." }
                : new CheckResult { Title = "Game Changers", Status = gc <= 3 ? "Bracket 3+" : "Bracket 4+",
                                    Level = gc <= 3 ? "banned" : "not_legal",
                                    Summary = gc <= 3
                                        ? $"{gc} Game Changer(s) — Bracket 3 allows up to 3."
                                        : $"{gc} Game Changers — more than 3 means Bracket 4 or higher.",
                                    Details = gameChangers.Select(Line).ToList() });

            // Mass land denial (possible)
            checks.Add(landDenial.Count == 0
                ? new CheckResult { Title = "Mass land denial", IsOk = true, Status = "None",
                                    Summary = "No cards that look like mass land denial." }
                : new CheckResult { Title = "Mass land denial", Status = "Check", Level = "banned",
                                    Summary = $"{landDenial.Count} card(s) might be mass land denial (Bracket 4+ if they are). Check them:",
                                    Details = landDenial.Select(Line).ToList() });

            // Extra turns
            checks.Add(extraTurns.Count == 0
                ? new CheckResult { Title = "Extra turns", IsOk = true, Status = "None",
                                    Summary = "No extra-turn cards." }
                : new CheckResult { Title = "Extra turns", Status = extraTurns.Count <= 2 ? "OK" : "Bracket 3+",
                                    IsOk = extraTurns.Count <= 2, Level = extraTurns.Count <= 2 ? "legal" : "banned",
                                    Summary = extraTurns.Count <= 2
                                        ? $"{extraTurns.Count} extra-turn card(s) — a couple is fine in Bracket 2 as long as they don't chain."
                                        : $"{extraTurns.Count} extra-turn cards — enough to chain; Bracket 3 or higher.",
                                    Details = extraTurns.Select(Line).ToList() });

            // Two-card combos (Commander Spellbook): infinite or game-winning pairs.
            var twoCard = _completeCombos.Where(c => c.Cards.Count == 2 && Services.DeckDataService.IsInfiniteOrWin(c)).ToList();
            if (!Services.DeckDataService.HasCombos)
                checks.Add(new CheckResult { Title = "Two-card combos", Status = "Not checked", Level = "",
                                             Summary = "Combos aren't downloaded yet — run Update Database. Brackets 1–2 allow no two-card infinite combos; Bracket 3 only late in the game." });
            else if (twoCard.Count == 0)
                checks.Add(new CheckResult { Title = "Two-card combos", IsOk = true, Status = "None",
                                             Summary = "No two-card infinite or game-winning combos found." });
            else
                checks.Add(new CheckResult { Title = "Two-card combos", Status = "Bracket 3+", Level = "banned",
                                             Summary = $"{twoCard.Count} two-card combo(s). Brackets 1–2 don't allow them; Bracket 3 only if they can't come together early in the game — early ones mean Bracket 4.",
                                             Details = twoCard.Select(c => $"{string.Join(" + ", c.Cards)} — {string.Join("; ", c.Results)}").ToList() });

            BracketChecks.ItemsSource = checks;

            // Minimum bracket the cards allow.
            int min = 1;
            var reasons = new List<string>();
            if (gc > 3) { min = 4; reasons.Add($"{gc} Game Changers"); }
            else if (gc > 0) { min = 3; reasons.Add($"{gc} Game Changer{(gc == 1 ? "" : "s")}"); }
            if (extraTurns.Count > 2 && min < 3) { min = 3; reasons.Add($"{extraTurns.Count} extra-turn cards"); }
            if (twoCard.Count > 0 && min < 3) { min = 3; reasons.Add($"{twoCard.Count} two-card combo{(twoCard.Count == 1 ? "" : "s")}"); }

            BracketHeadline.Text = min switch
            {
                1 => "Bracket 1–2 (Exhibition / Core)",
                3 => "At least Bracket 3 (Upgraded)",
                _ => "At least Bracket 4 (Optimized / cEDH)",
            };
            string reasonText = reasons.Count == 0
                ? "Nothing in the cards pushes it higher. Whether it's 1 or 2 depends on how the deck is built to play."
                : $"Because of: {string.Join(", ", reasons)}.";
            if (landDenial.Count > 0)
                reasonText += $" If the {landDenial.Count} possible land-denial card(s) really are mass land denial, it's Bracket 4 or higher.";
            if (gameChangers.Count == 0 && !_main.Any(c => c.IsGameChanger) && GameChangerDataMissing())
                reasonText += " (No Game Changer data found — run Update Database so the pool has Scryfall's Game Changer flags.)";
            BracketReason.Text = reasonText;
        }

        /// <summary>True when the pool has no card flagged as a Game Changer at all (not updated yet).</summary>
        private static bool GameChangerDataMissing()
        {
            try
            {
                using var db = new Data.AppDbContext();
                return !db.PoolCards.Any(p => p.IsGameChanger);
            }
            catch { return false; }
        }

        // ══════════════════════════════════════════════════════════════════
        // FORMAT CHECK
        // ══════════════════════════════════════════════════════════════════
        private void BuildFormatCheck()
        {
            // One set of rules for every deck type (DeckFormats → DeckRulesService),
            // shared with the deck view's rules line.
            // Legality isn't listed here: "Legality by format" below covers it,
            // this deck's format included (marked "this deck").
            var byFormatShown = Services.DeckRulesService.LegalityByFormat(_deck, _rule).Count > 0;
            CheckList.ItemsSource = Services.DeckRulesService.Check(_deck, _rule)
                .Where(c => !(byFormatShown && c.Title == "Legality"))
                .Select(c => new CheckResult
                {
                    Title = c.Title, Summary = c.Summary, IsOk = c.IsOk, Details = c.Details,
                })
                .ToList();

            // Legality by format: every format this kind of deck can be played
            // in, with the cards each one doesn't allow.
            var byFormat = Services.DeckRulesService.LegalityByFormat(_deck, _rule);
            if (byFormat.Count == 0)
            {
                LegalityHeader.Visibility = Visibility.Collapsed;
                LegalityIntro.Visibility = Visibility.Collapsed;
                LegalityList.Visibility = Visibility.Collapsed;
                return;
            }
            LegalityIntro.Text = _rule.Type == DeckType.Standard
                ? "Which cards each Constructed format allows (size and copies are the same 60-card rules for all of them)."
                : $"Which cards each {_rule.Size}-card command-zone format allows. Only the card lists — size, commander and color identity for this deck's format are checked above.";
            LegalityList.ItemsSource = byFormat
                .Select(x => new CheckResult
                {
                    Title = x.IsCurrent ? $"{x.Check.Title}  (this deck)" : x.Check.Title,
                    Summary = x.Check.Summary,
                    IsOk = x.Check.IsOk,
                    Details = x.Check.Details,
                    Status = x.Check.IsOk ? "Legal" : $"{x.Check.Details.Count} illegal",
                })
                .ToList();
        }

        // ══════════════════════════════════════════════════════════════════
        // Bars (the groups themselves: StatBuckets, shared with the collection statistics)
        // ══════════════════════════════════════════════════════════════════
        private static List<StatBar> Bars(List<DeckCard> cards, Func<DeckCard, string> bucket,
                                          string[] order, bool withValue)
        {
            var groups = cards.GroupBy(bucket).ToDictionary(g => g.Key, g => g.ToList());
            var items = order.Where(groups.ContainsKey)
                .Select(k => (label: k, count: Qty(groups[k]),
                              extra: withValue ? $"${groups[k].Sum(c => c.RowValue):N2}" : ""))
                .ToList();
            return ToBars(items);
        }

        private static List<StatBar> ToBars(List<(string label, int count, string extra)> items)
        {
            int max = items.Count == 0 ? 0 : items.Max(i => i.count);
            return items.Select(i => new StatBar
            {
                Label = i.label,
                CountText = i.count.ToString("N0"),
                ExtraText = i.extra,
                BarWidth = max > 0 && i.count > 0 ? Math.Max(2, MaxBar * i.count / max) : 0,
            }).ToList();
        }

        // ── Row types ─────────────────────────────────────────────────────
        public sealed record SummaryItem(string Label, string Value);

        public sealed class StatBar
        {
            public string Label { get; init; } = "";
            public string CountText { get; init; } = "";
            public string ExtraText { get; init; } = "";
            public double BarWidth { get; init; }
        }

        public sealed class ColorRow
        {
            public string Color { get; init; } = "";
            public string Pips { get; init; } = "";
            public string PipShare { get; init; } = "";
            public string LandSources { get; init; } = "";
            public string LandShare { get; init; } = "";
            public string OtherSources { get; init; } = "";
            public string Check { get; init; } = "";
            public Brush ChipBackground { get; init; } = Brushes.Transparent;
            public Brush ChipForeground { get; init; } = Brushes.Gray;
        }

        /// <summary>One EDHREC recommendation on the EDHREC tab.</summary>
        public sealed class EdhrecRow
        {
            public string Name { get; init; } = "";
            public string Section { get; init; } = "";
            public double Inclusion { get; init; }
            public double Synergy { get; init; }
            public bool InDeck { get; init; }
            public int Owned { get; init; }
            public int Free { get; init; }
            public string DecksText => Inclusion < 0 ? "—" : $"{Inclusion:0}%";
            public string SynergyText => $"{Synergy * 100:+0;-0;0}%";
            public string StatusText => InDeck ? "In the deck"
                : Free > 0 ? $"{Free} free in your collection"
                : Owned > 0 ? $"Own {Owned}, all in use"
                : "Not owned";
            /// <summary>Sort order for the You column: free first, then in use, not owned, in the deck.</summary>
            public int StatusOrder => InDeck ? 3 : Free > 0 ? 0 : Owned > 0 ? 1 : 2;
            private string Level => InDeck ? "" : Free > 0 ? "legal" : Owned > 0 ? "banned" : "not_legal";
            public Brush StatusBackground => LegalityInfo.BackgroundBrush(Level);
            public Brush StatusForeground => LegalityInfo.ForegroundBrush(Level);

            public static EdhrecRow From(Services.EdhrecCard c, bool inDeck, int owned, int free) => new()
            {
                Name = c.Name, Section = c.Section, Inclusion = c.InclusionPercent, Synergy = c.Synergy,
                InDeck = inDeck, Owned = owned, Free = free,
            };
        }

        /// <summary>One combo on the Combos tab.</summary>
        public sealed class ComboRow
        {
            public string Results { get; init; } = "";
            public string CardsText { get; init; } = "";
            public string RequiresText { get; init; } = "";
            public string ManaText { get; init; } = "";
            public string Prerequisites { get; init; } = "";
            public string Steps { get; init; } = "";
            public string TagText { get; init; } = "";
            public string PopularityText { get; init; } = "";
            public int Popularity { get; init; }
            public Uri? Url { get; init; }
            public string MissingText { get; set; } = "";
            public string MissingLevel { get; set; } = "";
            public int FreeCopies { get; set; }

            public System.Windows.Visibility RequiresVisibility => RequiresText.Length > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            public System.Windows.Visibility MissingVisibility => MissingText.Length > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            public System.Windows.Visibility TagVisibility => TagText.Length > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            public Brush MissingBackground => LegalityInfo.BackgroundBrush(MissingLevel);
            public Brush MissingForeground => LegalityInfo.ForegroundBrush(MissingLevel);

            public static ComboRow From(Services.Combo c)
            {
                string tag = Services.DeckDataService.BracketTagName(c.BracketTag);
                return new ComboRow
                {
                    Results = c.Results.Count > 0 ? string.Join(" · ", c.Results) : "Combo",
                    CardsText = string.Join("  +  ", c.Cards),
                    RequiresText = c.Requires.Count > 0 ? "Also needs: " + string.Join("; ", c.Requires) : "",
                    ManaText = string.IsNullOrWhiteSpace(c.ManaNeeded) ? "" : $"Mana needed: {c.ManaNeeded}",
                    Prerequisites = c.Prerequisites,
                    Steps = c.Steps,
                    TagText = tag.Length > 0 ? $"Spellbook: {tag}" : "",
                    Popularity = c.Popularity,
                    PopularityText = c.Popularity > 0 ? $"In {c.Popularity:N0} decks on EDHREC" : "",
                    Url = Uri.TryCreate(c.Url, UriKind.Absolute, out var u) ? u : null,
                };
            }
        }

        public sealed class CheckResult
        {
            public string Title { get; init; } = "";
            public string Summary { get; init; } = "";
            public bool IsOk { get; init; }
            public List<string> Details { get; init; } = new();
            /// <summary>Optional chip text (default OK / Problem).</summary>
            public string? Status { get; init; }
            /// <summary>Optional chip color by legality status key: legal (green), banned (amber), not_legal (red).</summary>
            public string? Level { get; init; }
            public string StatusText => Status ?? (IsOk ? "OK" : "Problem");
            public Brush ChipBackground => LegalityInfo.BackgroundBrush(Level ?? (IsOk ? "legal" : "not_legal"));
            public Brush ChipForeground => LegalityInfo.ForegroundBrush(Level ?? (IsOk ? "legal" : "not_legal"));

            public static CheckResult Ok(string title, string summary) =>
                new() { Title = title, Summary = summary, IsOk = true };

            public static CheckResult Problem(string title, string summary, List<string>? details = null) =>
                new() { Title = title, Summary = summary, IsOk = false, Details = details ?? new() };
        }
    }
}
