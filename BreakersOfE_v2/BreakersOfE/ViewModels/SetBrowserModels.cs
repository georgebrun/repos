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

        // ── Releases (Scryfall's set families) ────────────────────────────
        /// <summary>The table this set's cards are in: "Cards", "Tokens", "ArtSeries", "Oversized", "FrontCards".</summary>
        public string Table { get; init; } = "Cards";
        /// <summary>What this set is within its release: "Set", "Commander", "Promos", "Tokens" …</summary>
        public string Label { get; set; } = "";
        /// <summary>A release tile: its parts (main set first). Empty for a single set.</summary>
        public List<SetTile> Parts { get; set; } = new();
        public bool IsRelease => Parts.Count > 1;
        /// <summary>Third line: a release's parts ("Set · Commander · Promos"), or a part's label.</summary>
        public string PartsText { get; set; } = "";
        public bool HasPartsText => PartsText.Length > 0;

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
            $"{Name}" + (HasPartsText ? $" ({PartsText})" : "") +
            $"\nOwned {OwnedText} ({CompletionText})\n{(IsRelease ? "Release" : "Set")} value {TotalValueText} · Missing {MissingValueText}";

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
                            new object[] { SymbolPath, "ondark" },   // grey that reads on the window (Light or Dark)
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

        /// <summary>
        /// What a set is within its release, for its tile: the table decides
        /// for tokens, art series, oversized and front cards; otherwise the
        /// set type ("Commander", "Promos" …); the release's own set is "Set".
        /// </summary>
        public static string LabelFor(string table, string? setType, bool isMain) => table switch
        {
            "Tokens" => "Tokens",
            "ArtSeries" => "Art Series",
            "Oversized" => "Oversized",
            "FrontCards" => "Front Cards",
            _ when isMain => "Set",
            _ => (setType ?? "").ToLowerInvariant() switch
            {
                "commander" => "Commander",
                "promo" => "Promos",
                "masterpiece" => "Masterpieces",
                "box" => "Box Set",
                "funny" => "Extras",
                "alchemy" => "Alchemy",
                "duel_deck" => "Duel Decks",
                "starter" => "Starter",
                "memorabilia" => "Memorabilia",
                "minigame" => "Minigames",
                "token" => "Tokens",
                "draft_innovation" => "Draft",
                "expansion" or "core" or "masters" => "Set",
                "" => "Other",
                var t => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(t.Replace('_', ' ')),
            },
        };

        /// <summary>Order of parts on a release page: the set first, then these, then the rest.</summary>
        public static int LabelOrder(string label) => label switch
        {
            "Set" => 0, "Commander" => 1, "Promos" => 2, "Tokens" => 3, "Art Series" => 4,
            "Oversized" => 5, "Front Cards" => 6, _ => 7,
        };

        /// <summary>Which part of a release is its face: Cards-table sets of these kinds first.</summary>
        public static int MainRank(SetTile t)
        {
            if (t.Table != "Cards") return 50;
            return (t.SetType ?? "").ToLowerInvariant() switch
            {
                "expansion" => 0, "core" => 1, "masters" => 2, "draft_innovation" => 3, "commander" => 4,
                "funny" => 5, "starter" => 6, "duel_deck" => 7, "box" => 8, "masterpiece" => 9,
                "promo" => 20, "memorabilia" => 30, _ => 10,
            };
        }
    }
}