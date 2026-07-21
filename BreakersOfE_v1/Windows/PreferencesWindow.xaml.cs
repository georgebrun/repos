using BreakersOfE.Services;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BreakersOfE.Windows
{
    public partial class PreferencesWindow : Window
    {
        // ── Setting keys ──────────────────────────────────────────────────────
        public const string KeyAuthorName = "Pref_AuthorName";
        public const string KeyStartupView = "Pref_StartupView";
        public const string KeyTheme = "Theme"; // shared with ThemeService
        public const string KeyDefaultCondition = "Pref_DefaultCondition";
        public const string KeyDefaultLanguage = "Pref_DefaultLanguage";
        public const string KeyCurrency = "Pref_Currency";
        public const string KeyDefaultLife = "Tabletop_DefaultLife"; // shared with TabletopSettings
        public const string KeyImagesFolder = "Pref_ImagesFolder";
        public const string KeyDecksFolder = "Pref_DecksFolder";

        public PreferencesWindow()
        {
            InitializeComponent();
            LoadCurrentValues();
        }

        private void LoadCurrentValues()
        {
            TxtAuthorName.Text = Get(KeyAuthorName, "");
            TxtImagesFolder.Text = Get(KeyImagesFolder, AppFolderService.CardImagesFolder);
            TxtDecksFolder.Text = Get(KeyDecksFolder, AppFolderService.DecksFolder);

            SelectComboByTag(CmbStartupView, Get(KeyStartupView, "PoolToCollection"));
            SelectComboByContent(CmbDefaultCondition, Get(KeyDefaultCondition, "Near Mint"));
            SelectComboByContent(CmbDefaultLanguage, Get(KeyDefaultLanguage, "English"));
            SelectComboByTag(CmbCurrency, Get(KeyCurrency, "USD"));
            SelectComboByTag(CmbTheme, Get(KeyTheme, "System"));

            // Life total
            string lifeStr = Get(KeyDefaultLife, "20");
            if (lifeStr == "20") Life20.IsChecked = true;
            else if (lifeStr == "30") Life30.IsChecked = true;
            else if (lifeStr == "40") Life40.IsChecked = true;
            else { LifeCustom.IsChecked = true; LifeCustomBox.Text = lifeStr; }
        }

        private static string Get(string key, string def)
        {
            using var db = new Data.AppDbContext();
            return db.AppSettings.FirstOrDefault(s => s.Key == key)?.Value ?? def;
        }

        private static void Save(string key, string value)
        {
            using var db = new Data.AppDbContext();
            var s = db.AppSettings.FirstOrDefault(x => x.Key == key);
            if (s == null) db.AppSettings.Add(new Models.AppSetting { Key = key, Value = value });
            else s.Value = value;
            db.SaveChanges();
        }

        private static void SelectComboByTag(ComboBox cb, string tag)
        {
            foreach (ComboBoxItem item in cb.Items)
                if (item.Tag?.ToString() == tag) { cb.SelectedItem = item; return; }
            if (cb.Items.Count > 0) cb.SelectedIndex = 0;
        }

        private static void SelectComboByContent(ComboBox cb, string content)
        {
            foreach (ComboBoxItem item in cb.Items)
                if (item.Content?.ToString() == content) { cb.SelectedItem = item; return; }
            if (cb.Items.Count > 0) cb.SelectedIndex = 0;
        }

        private void BtnBrowseImages_Click(object sender, RoutedEventArgs e)
        {
            var path = BrowseFolder("Select card images folder", TxtImagesFolder.Text);
            if (path != null) TxtImagesFolder.Text = path;
        }

        private void BtnBrowseDecks_Click(object sender, RoutedEventArgs e)
        {
            var path = BrowseFolder("Select decks folder", TxtDecksFolder.Text);
            if (path != null) TxtDecksFolder.Text = path;
        }

        // WPF folder browser using a SaveFileDialog trick (no Windows.Forms needed)
        private static string? BrowseFolder(string description, string initialPath)
        {
            // Use OpenFileDialog with a filter trick to pick a folder
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = description,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Select folder",
                Filter = "Folder|*.",
                InitialDirectory = System.IO.Directory.Exists(initialPath)
                    ? initialPath : string.Empty
            };
            if (dlg.ShowDialog() == true)
                return System.IO.Path.GetDirectoryName(dlg.FileName);
            return null;
        }

        private void BtnOpenAppFolder_Click(object sender, RoutedEventArgs e)
            => System.Diagnostics.Process.Start("explorer.exe",
                AppFolderService.RootFolder);

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            Save(KeyAuthorName, TxtAuthorName.Text.Trim());
            Save(KeyImagesFolder, TxtImagesFolder.Text);
            Save(KeyDecksFolder, TxtDecksFolder.Text);

            if (CmbStartupView.SelectedItem is ComboBoxItem sv)
                Save(KeyStartupView, sv.Tag?.ToString() ?? "PoolToCollection");
            if (CmbDefaultCondition.SelectedItem is ComboBoxItem cond)
                Save(KeyDefaultCondition, cond.Content?.ToString() ?? "Near Mint");
            if (CmbDefaultLanguage.SelectedItem is ComboBoxItem lang)
                Save(KeyDefaultLanguage, lang.Content?.ToString() ?? "English");
            if (CmbCurrency.SelectedItem is ComboBoxItem curr)
                Save(KeyCurrency, curr.Tag?.ToString() ?? "USD");

            // Theme
            if (CmbTheme.SelectedItem is ComboBoxItem themeItem)
            {
                string theme = themeItem.Tag?.ToString() ?? "System";
                Save(KeyTheme, theme);
                AppTheme mode = theme switch
                {
                    "Dark" => AppTheme.Dark,
                    "Light" => AppTheme.Light,
                    _ => AppTheme.System
                };
                ThemeService.Apply(mode);
            }

            // Life total
            string life = Life20.IsChecked == true ? "20"
                        : Life30.IsChecked == true ? "30"
                        : Life40.IsChecked == true ? "40"
                        : (int.TryParse(LifeCustomBox.Text, out int v) && v > 0
                            ? v.ToString() : "20");
            Save(KeyDefaultLife, life);

            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;
    }
}