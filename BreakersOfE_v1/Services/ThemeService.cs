using BreakersOfE.Data;
using System;
using System.Linq;
using System.Windows;

namespace BreakersOfE.Services
{
    public enum AppTheme
    {
        Light,
        Dark,
        System
    }

    public static class ThemeService
    {
        private const string SettingKey = "Theme";
        private const string LightThemePath =
            "pack://application:,,,/BreakersOfE;component/Themes/LightTheme.xaml";
        private const string DarkThemePath =
            "pack://application:,,,/BreakersOfE;component/Themes/DarkTheme.xaml";

        // The user's chosen mode (may be System). CurrentTheme is the
        // effective Light/Dark actually applied.
        public static AppTheme SelectedMode { get; private set; } = AppTheme.System;
        public static AppTheme CurrentTheme { get; private set; } = AppTheme.Light;

        /// <summary>
        /// Reads the active Windows app theme from the registry.
        /// AppsUseLightTheme = 0 means dark, 1 (or missing) means light.
        /// </summary>
        public static AppTheme GetWindowsTheme()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var val = key?.GetValue("AppsUseLightTheme");
                if (val is int i)
                    return i == 0 ? AppTheme.Dark : AppTheme.Light;
            }
            catch { /* registry unavailable — fall back to light */ }
            return AppTheme.Light;
        }

        /// <summary>Resolves a mode to the concrete Light/Dark to apply.</summary>
        private static AppTheme Resolve(AppTheme mode) =>
            mode == AppTheme.System ? GetWindowsTheme() : mode;

        // ── Apply theme at startup ────────────────────────────────────────
        public static void ApplySavedTheme()
        {
            // v1: dark mode is not yet polished, so the app ships light-only.
            // The theme switcher is hidden in the UI and we force Light here.
            // (Dark/System support remains below for a future v2 release.)
            Apply(AppTheme.Light);

            // Listener is harmless in light-only mode (it only acts when
            // SelectedMode == System, which v1 never sets). Wiring it keeps the
            // v2 code path live and avoids an unused-method warning.
            HookSystemThemeChanges();
        }

        // ── Toggle between light and dark (manual override) ───────────────
        public static void Toggle()
        {
            // Toggling always lands on an explicit Light/Dark, leaving System.
            Apply(CurrentTheme == AppTheme.Light
                ? AppTheme.Dark
                : AppTheme.Light);
        }

        // ── Apply a specific mode (Light, Dark, or System) ────────────────
        public static void Apply(AppTheme mode)
        {
            SelectedMode = mode;
            AppTheme effective = Resolve(mode);
            CurrentTheme = effective;

            string path = effective == AppTheme.Dark
                ? DarkThemePath
                : LightThemePath;

            var dict = new ResourceDictionary
            {
                Source = new Uri(path, UriKind.Absolute)
            };

            // Replace the theme dictionary in App resources
            var existing = Application.Current.Resources.MergedDictionaries
                .FirstOrDefault(d =>
                    d.Source != null &&
                    (d.Source.OriginalString.Contains("LightTheme") ||
                     d.Source.OriginalString.Contains("DarkTheme")));

            if (existing != null)
                Application.Current.Resources.MergedDictionaries
                    .Remove(existing);

            Application.Current.Resources.MergedDictionaries.Add(dict);

            SaveTheme(mode);
        }

        // ── Re-resolve when Windows theme changes (only matters in System) ─
        private static bool _hooked;
        private static void HookSystemThemeChanges()
        {
            if (_hooked) return;
            _hooked = true;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (e.Category == Microsoft.Win32.UserPreferenceCategory.General
                    && SelectedMode == AppTheme.System)
                {
                    // Marshal back to UI thread to swap resource dictionaries.
                    Application.Current.Dispatcher.Invoke(() =>
                        Apply(AppTheme.System));
                }
            };
        }

        // ── Persist to DB ─────────────────────────────────────────────────
        private static void SaveTheme(AppTheme theme)
        {
            try
            {
                using var db = new AppDbContext();
                var setting = db.AppSettings
                    .FirstOrDefault(s => s.Key == SettingKey);

                if (setting == null)
                {
                    db.AppSettings.Add(new Models.AppSetting
                    {
                        Key = SettingKey,
                        Value = theme.ToString()
                    });
                }
                else
                {
                    setting.Value = theme.ToString();
                }

                db.SaveChanges();
            }
            catch { /* non-fatal */ }
        }

        // ── Helper for toolbar icon ───────────────────────────────────────
        public static string ThemeToggleIcon =>
            CurrentTheme == AppTheme.Light ? "\uE708" : "\uE706";
        // E708 = moon (switch to dark), E706 = sun (switch to light)

        public static string ThemeToggleTooltip =>
            CurrentTheme == AppTheme.Light
                ? "Switch to Dark Mode"
                : "Switch to Light Mode";
    }
}