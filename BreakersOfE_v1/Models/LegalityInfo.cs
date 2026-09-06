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
    /// One source of truth so the collection grid columns, the column chooser,
    /// and the left info panel all stay in sync. Adding/removing a format here
    /// automatically flows to all three.
    /// </summary>
    public static class LegalityInfo
    {
        /// <summary>
        /// Every format Scryfall reports, in the order columns should appear.
        /// Key = Scryfall JSON key. Header = grid column header / chooser label.
        /// DefaultVisible = shown on first run (rest hidden but toggleable).
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

        // ── Status parsing ─────────────────────────────────────────────────
        public static string RawStatus(string legalitiesJson, string formatKey)
        {
            if (string.IsNullOrWhiteSpace(legalitiesJson)) return string.Empty;
            try
            {
                using var doc = JsonDocument.Parse(legalitiesJson);
                if (doc.RootElement.TryGetProperty(formatKey, out var v))
                    return v.GetString()?.ToLowerInvariant() ?? string.Empty;
            }
            catch { }
            return string.Empty;
        }

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
        /// Sort rank — problems first so "sort Commander" surfaces banned cards.
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

        // ── Colors (match the mockup) ──────────────────────────────────────
        public static Brush BackgroundBrush(string status) => status switch
        {
            "legal" => Frozen(0xC0, 0xDD, 0x97), // green
            "restricted" => Frozen(0xB5, 0xD4, 0xF4), // blue
            "banned" => Frozen(0xFA, 0xC7, 0x75), // amber
            "not_legal" => Frozen(0xF7, 0xC1, 0xC1), // red
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
    }

    public record FormatDef(string Key, string Header, bool DefaultVisible);
}