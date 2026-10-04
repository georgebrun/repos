using System;

namespace BreakersOfE.Services
{
    public enum AppTheme
    {
        Light,
        Dark,
        System,
        Custom
    }

    /// <summary>
    /// The app theme the user picked (Settings → Appearance: Light, Dark,
    /// Follow Windows or Custom) and the one in effect. Only remembers and resolves;
    /// the app (ThemeApplier) does the actual switching. Saved in
    /// Settings.json as "Light", "Dark", "System" or "Custom".
    /// </summary>
    public static class ThemeService
    {
        /// <summary>The user's choice (may be System = follow Windows).</summary>
        public static AppTheme SelectedMode { get; private set; } = AppTheme.Dark;

        /// <summary>Light or Dark: what the app shows now.</summary>
        public static AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;

        /// <summary>Custom: the theme its colour preset starts from (Light or Dark).</summary>
        public static AppTheme CustomBase { get; set; } = AppTheme.Dark;

        /// <summary>Raised after the theme in effect changes.</summary>
        public static event Action? Changed;

        /// <summary>A saved value ("Light", "Dark", "System", "Custom") → the mode; anything else → Dark.</summary>
        public static AppTheme Parse(string? value) =>
            Enum.TryParse<AppTheme>(value, ignoreCase: true, out var mode) && Enum.IsDefined(mode) ? mode : AppTheme.Dark;

        /// <summary>
        /// Windows' app theme (Settings → Personalization → Colors → "Choose
        /// your app mode"): AppsUseLightTheme 0 = dark, 1 or missing = light.
        /// </summary>
        public static AppTheme GetWindowsTheme()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key?.GetValue("AppsUseLightTheme") is int i)
                    return i == 0 ? AppTheme.Dark : AppTheme.Light;
            }
            catch { /* registry unavailable: light */ }
            return AppTheme.Light;
        }

        /// <summary>Light or Dark for a mode (System → what Windows uses now; Custom → its preset's base).</summary>
        public static AppTheme Resolve(AppTheme mode) => mode switch
        {
            AppTheme.System => GetWindowsTheme(),
            AppTheme.Custom => CustomBase == AppTheme.Light ? AppTheme.Light : AppTheme.Dark,
            _ => mode,
        };

        /// <summary>
        /// Use this mode. Returns true when the theme in effect changed (the
        /// app then switches its colours).
        /// </summary>
        public static bool SetMode(AppTheme mode)
        {
            SelectedMode = mode;
            var effective = Resolve(mode);
            if (effective == CurrentTheme) return false;
            CurrentTheme = effective;
            Changed?.Invoke();
            return true;
        }
    }
}
