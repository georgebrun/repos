using System;
using System.IO;
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
