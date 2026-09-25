using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Windows.Media;

namespace BreakersOfE.Models
{
    /// <summary>
    /// Central definition of every Magic format legality Scryfall reports,
    /// plus helpers to read a card's status out of its stored LegalitiesJson,
    /// map that status to a display chip (text + colors), and to a sort rank.
    ///
    /// Ported from v1 unchanged. One source of truth: the grid's legality
    /// columns, the Legality button's checklist, filtering, and sorting all
    /// read from here, so adding/removing a format here flows everywhere.
    /// </summary>
    public static class LegalityInfo
    {
        /// <summary>
        /// Every format Scryfall reports, in the order columns should appear.
        /// Key = Scryfall JSON key. Header = grid column header / checklist label.
        /// DefaultVisible = shown by default (rest hidden but toggleable).
        /// </summary>
        public static readonly IReadOnlyList<FormatDef> Formats = new List<FormatDef>
        {
            new("standard",        "Standard",         true),
            new("pioneer",         "Pioneer",          true),
            new("modern",          "Modern",           true),
            new("legacy",          "Legacy",           true),
            new("vintage",         "Vintage",          true),
            new("commander",       "Commander",        true),
            new("pauper",          "Pauper",           true),
            new("oathbreaker",     "Oathbreaker",      false),
            new("standardbrawl",   "Standard Brawl",   false),
            new("brawl",           "Brawl",            false),
            new("alchemy",         "Alchemy",          false),
            new("explorer",        "Explorer",         false),
            new("historic",        "Historic",         false),
            new("timeless",        "Timeless",         false),
            new("gladiator",       "Gladiator",        false),
            new("penny",           "Penny",            false),
            new("paupercommander", "Pauper Commander", false),
            new("duel",            "Duel Commander",   false),
            new("oldschool",       "Old School",       false),
            new("premodern",       "Premodern",        false),
            new("predh",           "PreDH",            false),
            new("future",          "Future Standard",  false),
        };

        /// <summary>Short chip text for a status.</summary>
        public static string ChipText(string status) => status switch
        {
            "legal" => "Legal",
            "restricted" => "Res",
            "banned" => "Ban",
            "not_legal" => "No",
            _ => "—"
        };

        /// <summary>
        /// Sort rank — problems first so sorting a format surfaces banned cards.
        /// Lower = more restrictive/notable.
        /// </summary>
        public static int SortRank(string status) => status switch
        {
            "banned" => 0,
            "restricted" => 1,
            "not_legal" => 2,
            "legal" => 3,
            _ => 4   // unknown / not tracked
        };

        // ── Chip colors (same as v1) ───────────────────────────────────────
        public static Brush BackgroundBrush(string status) => status switch
        {
            "legal" => Frozen(0xC0, 0xDD, 0x97),      // green
            "restricted" => Frozen(0xB5, 0xD4, 0xF4), // blue
            "banned" => Frozen(0xFA, 0xC7, 0x75),     // amber
            "not_legal" => Frozen(0xF7, 0xC1, 0xC1),  // red
            _ => Brushes.Transparent
        };

        public static Brush ForegroundBrush(string status) => status switch
        {
            "legal" => Frozen(0x17, 0x34, 0x04),
            "restricted" => Frozen(0x04, 0x2C, 0x53),
            "banned" => Frozen(0x41, 0x24, 0x02),
            "not_legal" => Frozen(0x50, 0x13, 0x13),
            _ => Frozen(0x88, 0x87, 0x80)
        };

        private static SolidColorBrush Frozen(byte r, byte g, byte b)
        {
            var br = new SolidColorBrush(Color.FromRgb(r, g, b));
            br.Freeze();
            return br;
        }

        /// <summary>Every format's status from a card's LegalitiesJson (one parse).</summary>
        public static Dictionary<string, string> ParseAll(string legalitiesJson)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(legalitiesJson)) return map;
            try
            {
                using var doc = JsonDocument.Parse(legalitiesJson);
                foreach (var p in doc.RootElement.EnumerateObject())
                    map[p.Name] = p.Value.GetString()?.ToLowerInvariant() ?? string.Empty;
            }
            catch { }
            return map;
        }
    }

    public record FormatDef(string Key, string Header, bool DefaultVisible);

    /// <summary>
    /// A row that can show legality columns. The grid binds
    /// {Binding Legality[commander].Text}; filtering and sorting use the same.
    /// </summary>
    public interface ILegalityRow
    {
        LegalityAccessor Legality { get; }
    }

    /// <summary>
    /// Bindable indexer over a card's legalities: Legality["commander"].
    /// Parses the JSON once per row, on first use.
    /// </summary>
    public sealed class LegalityAccessor
    {
        /// <summary>
        /// Alias for "the row's own format": a deck card maps "deck" to its
        /// deck's format (commander / standard), so one "Legal" column works
        /// for every deck type.
        /// </summary>
        public const string DeckFormatKey = "deck";

        private readonly Func<string> _json;
        private readonly Func<string>? _deckFormat;
        private Dictionary<string, string>? _status;
        private readonly Dictionary<string, LegalityCell> _cells =
            new(StringComparer.OrdinalIgnoreCase);

        public LegalityAccessor(Func<string> legalitiesJson, Func<string>? deckFormat = null)
        {
            _json = legalitiesJson;
            _deckFormat = deckFormat;
        }

        public LegalityCell this[string formatKey]
        {
            get
            {
                if (_deckFormat != null &&
                    string.Equals(formatKey, DeckFormatKey, StringComparison.OrdinalIgnoreCase))
                    formatKey = _deckFormat();

                if (!_cells.TryGetValue(formatKey, out var cell))
                {
                    _status ??= LegalityInfo.ParseAll(_json());
                    _status.TryGetValue(formatKey, out var s);
                    cell = new LegalityCell(s ?? string.Empty);
                    _cells[formatKey] = cell;
                }
                return cell;
            }
        }
    }

    /// <summary>One format's status for one card, with its chip text/colors.</summary>
    public sealed class LegalityCell
    {
        public LegalityCell(string status) => Status = status;
        public string Status { get; }
        public string Text => LegalityInfo.ChipText(Status);
        public int SortRank => LegalityInfo.SortRank(Status);
        public Brush Background => LegalityInfo.BackgroundBrush(Status);
        public Brush Foreground => LegalityInfo.ForegroundBrush(Status);
    }
}