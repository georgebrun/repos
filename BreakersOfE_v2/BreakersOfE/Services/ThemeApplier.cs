using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;
using WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType;

namespace BreakersOfE.Services
{
    /// <summary>
    /// Switches the app's colours (Settings → Appearance), live: Light, Dark,
    /// Follow Windows (switches again when Windows does) or Custom (a colour
    /// preset on top of Light or Dark). Sets Wpf.Ui's theme, BoE's own theme
    /// colours (BoeGoldBrush, BoePanelBrush, BoeWarningBrush …) and the
    /// window backdrops. The card tables keep their light rows in every theme.
    /// </summary>
    public static class ThemeApplier
    {
        private static bool _watching;

        /// <summary>Custom has its own window colour: windows show it instead of Mica.</summary>
        private static bool _solidWindows;

        /// <summary>Wpf.Ui resources a Custom preset overrides (removed again when it doesn't).</summary>
        private static readonly string[] OverrideKeys =
        {
            "ApplicationBackgroundColor", "ApplicationBackgroundBrush",
            "SolidBackgroundFillColorBase", "SolidBackgroundFillColorBaseBrush",
            "CardBackgroundFillColorDefault", "CardBackgroundFillColorDefaultBrush",
            "TextFillColorPrimary", "TextFillColorPrimaryBrush",
            "TextFillColorSecondary", "TextFillColorSecondaryBrush",
        };

        /// <summary>At startup: the theme saved in Settings.</summary>
        public static void ApplySaved()
        {
            if (!_watching)
            {
                _watching = true;
                SystemEvents.UserPreferenceChanged += OnWindowsSettingChanged;
                // Windows opened later get the right backdrop too.
                EventManager.RegisterClassHandler(typeof(FluentWindow), FrameworkElement.LoadedEvent,
                    new RoutedEventHandler((s, _) => { if (s is FluentWindow w) SetBackdrop(w); }));
            }
            Apply(ThemeService.Parse(AppSettingsService.Current.Theme));
        }

        /// <summary>The user picked a theme: save it and switch now.</summary>
        public static void Use(AppTheme mode)
        {
            var s = AppSettingsService.Current;
            s.Theme = mode.ToString();
            AppSettingsService.Save(s);
            Apply(mode);
        }

        /// <summary>A Custom colour (or the preset) changed: show it now.</summary>
        public static void Refresh() => Apply(ThemeService.SelectedMode);

        /// <summary>On exit: stop listening to Windows.</summary>
        public static void Stop()
        {
            if (!_watching) return;
            _watching = false;
            SystemEvents.UserPreferenceChanged -= OnWindowsSettingChanged;
        }

        private static void OnWindowsSettingChanged(object? sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General || ThemeService.SelectedMode != AppTheme.System) return;
            Application.Current?.Dispatcher.BeginInvoke(new System.Action(() =>
            {
                if (ThemeService.SelectedMode == AppTheme.System && ThemeService.Resolve(AppTheme.System) != ThemeService.CurrentTheme)
                    Apply(AppTheme.System);
            }));
        }

        private static void Apply(AppTheme mode)
        {
            var app = Application.Current;
            if (app == null) return;

            ColorPreset? preset = mode == AppTheme.Custom ? AppSettingsService.Current.ActivePreset() : null;
            ThemeService.CustomBase = preset?.IsLight == true ? AppTheme.Light : AppTheme.Dark;
            ThemeService.SetMode(mode);
            bool dark = ThemeService.CurrentTheme == AppTheme.Dark;
            _solidWindows = preset != null && preset.Window.Length > 0;

            // Back to the plain theme first (and Windows' accent colour) …
            foreach (string key in OverrideKeys) app.Resources.Remove(key);
            ApplicationThemeManager.Apply(dark ? ApplicationTheme.Dark : ApplicationTheme.Light,
                                          _solidWindows ? WindowBackdropType.None : WindowBackdropType.Mica);

            // … BoE's own colours for it …
            var res = app.Resources;
            res["BoeGoldBrush"] = Frozen(dark ? Color.FromRgb(0xFF, 0xC0, 0x00) : Color.FromRgb(0x9A, 0x70, 0x00));
            res["BoePanelBrush"] = Frozen(Color.FromArgb(0x15, 0x80, 0x80, 0x80));
            res["BoeWarningBrush"] = Frozen(dark ? Color.FromRgb(0xE8, 0xA3, 0x17) : Color.FromRgb(0xB0, 0x78, 0x00));   // amber; deeper on Light so it reads
            // Scroll bar thumb: lighter under the mouse on Dark, darker on Light.
            res["BoeScrollThumbBrush"] = Frozen(dark ? Color.FromArgb(0x90, 0x9A, 0x9A, 0x9A) : Color.FromArgb(0xA0, 0x8A, 0x8A, 0x8A));
            res["BoeScrollThumbHoverBrush"] = Frozen(dark ? Color.FromArgb(0xD0, 0xC0, 0xC0, 0xC0) : Color.FromArgb(0xD0, 0x6A, 0x6A, 0x6A));
            res["BoeScrollThumbDragBrush"] = Frozen(dark ? Color.FromArgb(0xF0, 0xE0, 0xE0, 0xE0) : Color.FromArgb(0xF0, 0x50, 0x50, 0x50));

            // … then the Custom preset's colours on top.
            if (preset != null)
            {
                Override(res, preset.Window, "ApplicationBackgroundColor", "ApplicationBackgroundBrush");
                Override(res, preset.Window, "SolidBackgroundFillColorBase", "SolidBackgroundFillColorBaseBrush");
                Override(res, preset.Panel, "CardBackgroundFillColorDefault", "CardBackgroundFillColorDefaultBrush");
                if (preset.Panel.Length > 0) res["BoePanelBrush"] = Frozen(ToColor(preset.Panel));
                Override(res, preset.Text, "TextFillColorPrimary", "TextFillColorPrimaryBrush");
                Override(res, preset.SecondaryText, "TextFillColorSecondary", "TextFillColorSecondaryBrush");
                if (preset.Warning.Length > 0) res["BoeWarningBrush"] = Frozen(ToColor(preset.Warning));
                if (preset.Accent.Length > 0)
                    ApplicationAccentColorManager.Apply(ToColor(preset.Accent), dark ? ApplicationTheme.Dark : ApplicationTheme.Light);
            }

            foreach (Window w in app.Windows)
                if (w is FluentWindow fw) SetBackdrop(fw, redraw: true);
        }

        /// <summary>Mica, or the Custom window colour (solid).</summary>
        private static void SetBackdrop(FluentWindow w, bool redraw = false)
        {
            if (_solidWindows)
            {
                if (w.WindowBackdropType != WindowBackdropType.None) w.WindowBackdropType = WindowBackdropType.None;
                w.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "ApplicationBackgroundBrush");
            }
            else if (w.WindowBackdropType == WindowBackdropType.None)
            {
                w.WindowBackdropType = WindowBackdropType.Mica;
            }
            else if (redraw)
            {
                // Redraw Mica in the new theme.
                w.WindowBackdropType = WindowBackdropType.None;
                w.WindowBackdropType = WindowBackdropType.Mica;
            }
        }

        private static void Override(ResourceDictionary res, string hex, string colorKey, string brushKey)
        {
            if (hex.Length == 0) return;
            var c = ToColor(hex);
            res[colorKey] = c;
            res[brushKey] = Frozen(c);
        }

        public static Color ToColor(string hex)
        {
            var (r, g, b) = ColorPreset.Rgb(hex);
            return Color.FromRgb(r, g, b);
        }

        private static SolidColorBrush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }
}
