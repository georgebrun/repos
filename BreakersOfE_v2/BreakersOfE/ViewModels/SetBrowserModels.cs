using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace BreakersOfE.ViewModels
{
    /// <summary>
    /// One set tile in the set browser. Built from the in-memory pool
    /// (no extra download): code, name, type, release date, card count and
    /// total value all come from the cards themselves.
    /// </summary>
    public class SetTile : INotifyPropertyChanged
    {
        public string Code { get; init; } = "";
        public string Name { get; init; } = "";
        public string SetType { get; init; } = "";
        public string SymbolPath { get; init; } = "";

        /// <summary>Earliest release date among the set's cards (ISO yyyy-mm-dd).</summary>
        public string ReleasedAt { get; set; } = "";

        public int CardCount { get; set; }
        public decimal TotalValue { get; set; }

        // ── Completion: printings of this set in your collection ──────────
        /// <summary>Printings (collector numbers) you own at least one copy of, any finish.</summary>
        public int OwnedCount { get; set; }
        /// <summary>Value of the printings you don't own (non-foil price, else foil).</summary>
        public decimal MissingValue { get; set; }

        public double CompletionPercent =>
            CardCount == 0 ? 0 : Math.Round(OwnedCount * 100.0 / CardCount, 1);

        public string CompletionText =>
            CompletionPercent >= 10 || CompletionPercent == 0
                ? $"{CompletionPercent:0}%" : $"{CompletionPercent:0.#}%";

        public string OwnedText => $"{OwnedCount:N0} / {CardCount:N0}";

        public string MissingValueText =>
            MissingValue > 0 ? MissingValue.ToString("C2", CultureInfo.GetCultureInfo("en-US")) : "—";

        public string TotalValueText =>
            TotalValue > 0 ? TotalValue.ToString("C2", CultureInfo.GetCultureInfo("en-US")) : "—";

        public string ToolTipText =>
            $"{Name}\nOwned {OwnedText} ({CompletionText})\nSet value {TotalValueText} · Missing {MissingValueText}";

        public string YearText => ReleasedAt.Length >= 4 ? ReleasedAt[..4] : "";

        /// <summary>"2024 · 123 / 281 · 44%" — year, owned/total printings, completion.</summary>
        public string Summary =>
            string.IsNullOrEmpty(YearText)
                ? $"{OwnedText} · {CompletionText}"
                : $"{YearText} · {OwnedText} · {CompletionText}";

        private bool _isHighlighted;
        public bool IsHighlighted
        {
            get => _isHighlighted;
            set { if (_isHighlighted != value) { _isHighlighted = value; OnPropertyChanged(); } }
        }

        // Set symbol: rendered on first read, so only visible tiles pay the
        // SVG conversion cost. Neutral gray tint (the "common" tint) keeps the
        // browser calm; rarity color belongs on individual cards.
        private ImageSource? _symbol;
        private bool _symbolLoaded;
        public ImageSource? Symbol
        {
            get
            {
                if (!_symbolLoaded)
                {
                    _symbolLoaded = true;
                    if (!string.IsNullOrEmpty(SymbolPath))
                    {
                        _symbol = new Services.ImageSourceConverter().Convert(
                            new object[] { SymbolPath, "common" },
                            typeof(ImageSource), null!,
                            CultureInfo.CurrentCulture) as ImageSource;
                    }
                }
                return _symbol;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>
    /// One virtualized row in the set browser: either a section header
    /// ("Main Sets  54") or a row of N tiles.
    /// </summary>
    public class SetBrowserRow
    {
        public bool IsHeader { get; init; }
        public bool IsTileRow => !IsHeader;
        public string HeaderText { get; init; } = "";
        public string HeaderCount { get; init; } = "";
        public List<SetTile> Tiles { get; init; } = new();
    }

    /// <summary>
    /// Maps Scryfall set_type values to browser sections, in display order.
    /// </summary>
    public static class SetGrouping
    {
        private static readonly (string section, string[] types)[] Sections =
        {
            ("Main Sets",            new[] { "expansion" }),
            ("Core Sets",            new[] { "core" }),
            ("Masters & Remasters",  new[] { "masters" }),
            ("Draft Innovation",     new[] { "draft_innovation" }),
            ("Commander",            new[] { "commander" }),
            ("Un-Sets & Playtest",   new[] { "funny" }),
            ("Starter & Intro",      new[] { "starter" }),
            ("Duel Decks",           new[] { "duel_deck" }),
            ("Premium & Collector",  new[] { "from_the_vault", "spellbook", "premium_deck", "arsenal" }),
            ("Box Sets",             new[] { "box" }),
            ("Planechase, Archenemy & Vanguard", new[] { "planechase", "archenemy", "vanguard" }),
            ("Promos",               new[] { "promo" }),
            ("Digital",              new[] { "alchemy", "treasure_chest" }),
            ("Tokens & Memorabilia", new[] { "token", "memorabilia", "minigame" }),
        };

        public const string Other = "Other";

        public static string SectionFor(string? setType)
        {
            string t = (setType ?? "").ToLowerInvariant();
            foreach (var (section, types) in Sections)
                if (Array.IndexOf(types, t) >= 0) return section;
            return Other;
        }

        public static int OrderOf(string section)
        {
            for (int i = 0; i < Sections.Length; i++)
                if (Sections[i].section == section) return i;
            return Sections.Length;   // "Other" last
        }
    }
}