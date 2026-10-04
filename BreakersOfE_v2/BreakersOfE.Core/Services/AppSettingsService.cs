using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BreakersOfE.Services
{
    /// <summary>The user's settings (Settings.json). The Settings window edits these.</summary>
    public sealed class AppSettings
    {
        /// <summary>
        /// Trade %: the share of market price a card shop pays in trade
        /// credit. Trade Value = market × Trade %. Approved default: 70%.
        /// </summary>
        public int TradePercent { get; set; } = AppSettingsService.DefaultTradePercent;

        /// <summary>Language new cards go in as (Edit pages, imports that don't say).</summary>
        public string DefaultLanguage { get; set; } = Models.CardLanguage.Default;

        /// <summary>Condition new cards go in as.</summary>
        public string DefaultCondition { get; set; } = Models.CardCondition.Default;

        /// <summary>
        /// Keep pictures of YOUR cards (collection, Trade Binder, Want List,
        /// decks) on disk, so they show offline. Downloaded in the background
        /// as cards are added.
        /// </summary>
        public bool SaveMyPictures { get; set; } = true;

        /// <summary>
        /// Also keep every other picture you look at (the Pool's). Off: those
        /// are shown from the internet and not saved.
        /// </summary>
        public bool SaveViewedPictures { get; set; }

        /// <summary>The page the app opens on: pool, collection, decks, sets or keywords.</summary>
        public string StartPage { get; set; } = "pool";

        /// <summary>Remind to Update Database after this many days (0 = never).</summary>
        public int UpdateReminderDays { get; set; } = 14;
    }

    /// <summary>
    /// App-wide user settings, stored in Documents\BoE_V2\Settings.json.
    /// Read anywhere through <see cref="Current"/>; a missing or damaged file
    /// gives the defaults (it never stops the app). The Settings window
    /// (to be built) changes them through <see cref="Save"/>.
    /// </summary>
    public static class AppSettingsService
    {
        public const int DefaultTradePercent = 70;

        /// <summary>The start pages Settings offers (value, label).</summary>
        public static readonly (string Value, string Label)[] StartPages =
        {
            ("pool", "Card Pool — Cards"),
            ("collection", "Collection — Cards"),
            ("decks", "Decks"),
            ("sets", "Sets"),
            ("keywords", "Keyword Dictionary"),
        };

        private static string FilePath => Path.Combine(AppFolderService.RootFolder, "Settings.json");
        private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };
        private static AppSettings? _current;

        /// <summary>Raised after the settings are saved.</summary>
        public static event Action? Changed;

        public static AppSettings Current => _current ??= Load();

        /// <summary>Trade % as a fraction (0.70), kept within 1–100%.</summary>
        public static decimal TradeFraction => Math.Clamp(Current.TradePercent, 1, 100) / 100m;

        private static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath) &&
                    JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), _json) is { } s)
                {
                    if (s.TradePercent < 1 || s.TradePercent > 100) s.TradePercent = DefaultTradePercent;
                    // Anything unknown (hand-edited, older file) falls back to the default.
                    if (!Models.CardLanguage.All.Contains(s.DefaultLanguage)) s.DefaultLanguage = Models.CardLanguage.Default;
                    if (!Models.CardCondition.All.Contains(s.DefaultCondition)) s.DefaultCondition = Models.CardCondition.Default;
                    if (!StartPages.Any(p => p.Value == s.StartPage)) s.StartPage = "pool";
                    s.UpdateReminderDays = Math.Clamp(s.UpdateReminderDays, 0, 365);
                    return s;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Settings load failed: {ex.Message}");
            }
            return new AppSettings();
        }

        public static void Save(AppSettings settings)
        {
            _current = settings;
            try { File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, _json)); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Settings save failed: {ex.Message}");
            }
            Changed?.Invoke();
        }
    }
}
