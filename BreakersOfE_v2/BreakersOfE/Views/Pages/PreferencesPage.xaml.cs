using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Appearance;

namespace BreakersOfE.Views.Pages
{
    /// <summary>
    /// Settings page — theme switching works right away in Phase 1.
    /// 
    /// Later phases will add MTG color themes, XP Classic,
    /// custom theme editor, agent config, cache management, etc.
    /// </summary>
    public partial class PreferencesPage : Page
    {
        public PreferencesPage()
        {
            InitializeComponent();
        }

        // ── Theme Switching ──────────────────────────────────────
        // These click handlers swap between Dark and Light themes.
        // Wpf.Ui handles all the control restyling automatically.

        private void DarkTheme_Click(object sender, RoutedEventArgs e)
        {
            ApplicationThemeManager.Apply(ApplicationTheme.Dark);
        }

        private void LightTheme_Click(object sender, RoutedEventArgs e)
        {
            ApplicationThemeManager.Apply(ApplicationTheme.Light);
        }
    }
}
