using System;
using System.Collections.Generic;
using System.Linq;

namespace BreakersOfE.Models
{
    /// <summary>
    /// Card condition, in full words. A collection row is one printing +
    /// finish + language + condition (the way Deckbox, ManaBox, Moxfield and
    /// TCGplayer keep collections), so 2 Near Mint and 1 Lightly Played copies
    /// of the same card are two rows.
    /// </summary>
    public static class CardCondition
    {
        public const string NearMint = "Near Mint";
        public const string Unknown = "Unknown";

        /// <summary>New cards go in as Near Mint (a settings choice later).</summary>
        public const string Default = NearMint;

        /// <summary>The choices, best to worst; Unknown for rows never graded (v1 data).</summary>
        public static readonly IReadOnlyList<string> All = new[]
        {
            NearMint, "Lightly Played", "Moderately Played", "Heavily Played", "Damaged", Unknown,
        };

        /// <summary>Any stored or imported value → one of <see cref="All"/>.</summary>
        public static string Normalize(string? value)
        {
            string v = (value ?? "").Trim();
            if (v.Length == 0) return Unknown;
            var exact = All.FirstOrDefault(c => c.Equals(v, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
            return v.ToUpperInvariant() switch
            {
                "M" or "MINT" or "NM" or "NM/M" or "NEAR_MINT" => NearMint,
                "LP" or "SP" or "EX" or "EXCELLENT" or "SLIGHTLY PLAYED" or "LIGHT_PLAYED" or "LIGHTLY_PLAYED" or "GOOD (LIGHTLY PLAYED)" => "Lightly Played",
                "MP" or "PL" or "PLAYED" or "GOOD" or "MODERATELY_PLAYED" => "Moderately Played",
                "HP" or "HEAVILY_PLAYED" or "POOR" => "Heavily Played",
                "D" or "DMG" or "DAMAGED" => "Damaged",
                _ => Unknown,
            };
        }
    }

    /// <summary>Card language, in full words (Scryfall's printed languages).</summary>
    public static class CardLanguage
    {
        public const string English = "English";

        /// <summary>New cards go in as English (a settings choice later).</summary>
        public const string Default = English;

        /// <summary>The common languages first, then the rare ones (old promos).</summary>
        public static readonly IReadOnlyList<string> All = new[]
        {
            English, "Spanish", "French", "German", "Italian", "Portuguese",
            "Japanese", "Korean", "Russian", "Chinese Simplified", "Chinese Traditional",
            "Phyrexian", "Hebrew", "Latin", "Ancient Greek", "Arabic", "Sanskrit", "Quenya",
        };

        /// <summary>Any stored value or Scryfall code → one of <see cref="All"/> (blank = English).</summary>
        public static string Normalize(string? value)
        {
            string v = (value ?? "").Trim();
            if (v.Length == 0) return English;
            var exact = All.FirstOrDefault(l => l.Equals(v, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
            return v.ToLowerInvariant() switch
            {
                "en" => English,
                "es" or "sp" => "Spanish",
                "fr" => "French",
                "de" => "German",
                "it" => "Italian",
                "pt" => "Portuguese",
                "ja" or "jp" => "Japanese",
                "ko" or "kr" => "Korean",
                "ru" => "Russian",
                "zhs" or "zh-hans" or "cs" or "chinese (simplified)" or "simplified chinese" => "Chinese Simplified",
                "zht" or "zh-hant" or "ct" or "chinese (traditional)" or "traditional chinese" => "Chinese Traditional",
                "ph" => "Phyrexian",
                "he" => "Hebrew",
                "la" => "Latin",
                "grc" => "Ancient Greek",
                "ar" => "Arabic",
                "sa" => "Sanskrit",
                "qya" => "Quenya",
                _ => v,              // keep anything else as it was typed
            };
        }
    }
}
