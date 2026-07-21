using BreakersOfE.Models;
using BreakersOfE.Services;
using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace BreakersOfE.Windows
{
    public partial class ImportExportWindow : Window
    {
        public enum StartMode
        {
            ImportCollection,
            ImportDeck,
            ExportCollection,
            ExportDeck
        }

        private readonly Deck? _activeDeck;

        public ImportExportWindow(StartMode mode = StartMode.ImportCollection,
            Deck? activeDeck = null)
        {
            InitializeComponent();
            _activeDeck = activeDeck;

            // Pre-fill export path with a default filename
            TxtExportPath.Text = Path.Combine(
                AppFolderService.ExportsFolder, "collection.csv");

            // Set starting state based on mode
            switch (mode)
            {
                case StartMode.ImportCollection:
                    ModeTab.SelectedIndex = 0;
                    RbImportCollection.IsChecked = true;
                    break;
                case StartMode.ImportDeck:
                    ModeTab.SelectedIndex = 0;
                    RbImportDeck.IsChecked = true;
                    break;
                case StartMode.ExportCollection:
                    ModeTab.SelectedIndex = 1;
                    RbExportCollection.IsChecked = true;
                    ExportTarget_Changed(this, new RoutedEventArgs());
                    break;
                case StartMode.ExportDeck:
                    ModeTab.SelectedIndex = 1;
                    RbExportDeck.IsChecked = true;
                    DeckFileRow.Visibility = Visibility.Visible;
                    ExportTarget_Changed(this, new RoutedEventArgs());
                    if (activeDeck != null && !string.IsNullOrEmpty(activeDeck.FilePath))
                        TxtDeckPath.Text = activeDeck.FilePath;
                    if (activeDeck != null)
                        TxtExportPath.Text = Path.Combine(
                            AppFolderService.ExportsFolder,
                            AppFolderService.SafeFileName(activeDeck.Name) + ".deck");
                    break;
            }
        }

        // ── Tab change ────────────────────────────────────────────────────────
        private void ModeTab_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (BtnAction == null) return;
            bool isImport = ModeTab.SelectedIndex == 0;
            BtnAction.Content = isImport ? "⬇  Import" : "⬆  Export";
        }

        // ── Export target changed ─────────────────────────────────────────────
        private void ExportTarget_Changed(object sender, RoutedEventArgs e)
        {
            if (TxtDeckPath == null) return;
            bool isDeck = RbExportDeck.IsChecked == true;
            DeckFileRow.Visibility = isDeck ? Visibility.Visible : Visibility.Collapsed;

            // Enable all formats for all targets
            RbExpMoxfield.IsEnabled = true;
            RbExpTcgPlayer.IsEnabled = true;
            RbExpDeckbox.IsEnabled = true;
            RbExpDragonShield.IsEnabled = true;
            RbExpNative.IsEnabled = true;
            // PlainText, ManaBox, Archidekt all support deck export too
            if (RbExpPlainText != null) RbExpPlainText.IsEnabled = true;
        }

        // ── Browse for deck file to export ───────────────────────────────────
        private void BtnBrowseDeckFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select deck to export",
                Filter = "Deck Files (*.deck)|*.deck|All files (*.*)|*.*",
                InitialDirectory = AppFolderService.DecksFolder
            };
            if (dlg.ShowDialog() == true)
                TxtDeckPath.Text = dlg.FileName;
        }

        // ── Import target changed ─────────────────────────────────────────────
        private void ImportTarget_Changed(object sender, RoutedEventArgs e)
        {
            // Auto-detect handles all formats (collection and deck) automatically.
            // Nothing format-specific to toggle here anymore.
        }

        // ── File browse — Import ──────────────────────────────────────────────
        private void BtnBrowseImport_Click(object sender, RoutedEventArgs e)
        {
            bool isDeck = RbImportDeck.IsChecked == true;
            var dlg = new OpenFileDialog
            {
                Title = "Select file to import",
                Filter = isDeck
                    ? "All Supported Deck Formats|*.csv;*.txt|CSV files (*.csv)|*.csv|Text decklist (*.txt)|*.txt|All files (*.*)|*.*"
                    : "All Supported Files|*.csv;*.json;*.txt|CSV files (*.csv)|*.csv|JSON backup (*.json)|*.json|Text decklist (*.txt)|*.txt|All files (*.*)|*.*",
                InitialDirectory = AppFolderService.ImportsFolder
            };
            if (dlg.ShowDialog() == true)
                TxtImportPath.Text = dlg.FileName;
        }

        // ── File browse — Export ──────────────────────────────────────────────
        private void BtnBrowseExport_Click(object sender, RoutedEventArgs e)
        {
            bool isDeck = RbExportDeck?.IsChecked == true;
            bool isJson = RbExpNative?.IsChecked == true;
            bool isPlainText = RbExpPlainText?.IsChecked == true;

            string filter = isJson
                ? "JSON files (*.json)|*.json|All files (*.*)|*.*"
                : isPlainText
                    ? "Text files (*.txt)|*.txt|All files (*.*)|*.*"
                    : "CSV files (*.csv)|*.csv|All files (*.*)|*.*";

            string baseName = isDeck
                ? (_activeDeck?.Name ?? "deck")
                : "collection";

            string ext = isJson ? ".json"
                : isPlainText ? ".txt" : ".csv";
            string defaultName = AppFolderService.SafeFileName(baseName) + ext;

            var dlg = new SaveFileDialog
            {
                Title = "Save export file",
                Filter = filter,
                InitialDirectory = AppFolderService.ExportsFolder,
                FileName = defaultName
            };
            if (dlg.ShowDialog() == true)
                TxtExportPath.Text = dlg.FileName;
        }

        // ── Main action button ────────────────────────────────────────────────
        private void BtnAction_Click(object sender, RoutedEventArgs e)
        {
            bool isImport = ModeTab.SelectedIndex == 0;
            if (isImport) DoImport();
            else DoExport();
        }

        private void DoImport()
        {
            string path = TxtImportPath.Text.Trim();
            if (!File.Exists(path))
            {
                Log("❌ No file selected or file not found.");
                return;
            }

            // Detect or select format
            ImportExportFormat fmt;
            if (RbFmtAutoDetect.IsChecked == true)
                fmt = CollectionImportExportService.DetectFormat(path);
            else if (RbFmtMoxfield.IsChecked == true) fmt = ImportExportFormat.Moxfield;
            else if (RbFmtTcgPlayer.IsChecked == true) fmt = ImportExportFormat.TcgPlayer;
            else if (RbFmtDeckbox.IsChecked == true) fmt = ImportExportFormat.Deckbox;
            else if (RbFmtDragonShield.IsChecked == true) fmt = ImportExportFormat.DragonShield;
            else fmt = CollectionImportExportService.DetectFormat(path);

            Log($"📂 Importing: {Path.GetFileName(path)}");
            Log($"   Format detected: {fmt}");

            BtnAction.IsEnabled = false;
            try
            {
                if (RbImportDeck.IsChecked == true)
                {
                    var (result, deck) = CollectionImportExportService
                        .ImportDeck(path, fmt);
                    LogResult(result);
                    if (deck != null)
                    {
                        // Build a clean file path from the deck name
                        string deckPath = Path.Combine(
                            AppFolderService.DecksFolder,
                            AppFolderService.SafeFileName(deck.Name) + ".deck");
                        DeckService.SaveAs(deck, deckPath);
                        Log($"✅ Deck saved as: {Path.GetFileName(deckPath)}");
                        Log($"   Location: {deckPath}");
                        Log("   Open it via File → Open Deck.");
                    }
                }
                else
                {
                    bool merge = RbMerge.IsChecked == true;
                    var result = CollectionImportExportService
                        .ImportCollection(path, fmt, merge);
                    LogResult(result);
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Error: {ex.Message}");
            }
            finally
            {
                BtnAction.IsEnabled = true;
            }
        }

        private void DoExport()
        {
            string path = TxtExportPath.Text.Trim();
            if (string.IsNullOrEmpty(path))
            {
                Log("❌ No output file selected.");
                return;
            }

            // If path is a directory, append a default filename
            if (Directory.Exists(path))
            {
                bool isDeck = RbExportDeck.IsChecked == true;
                bool isJson = RbExpNative.IsChecked == true;
                bool isPlainText = RbExpPlainText?.IsChecked == true;
                string ext = isJson ? ".json" : isPlainText ? ".txt" : ".csv";
                string name = isDeck
                    ? (_activeDeck?.Name ?? "deck")
                    : "collection";
                path = Path.Combine(path,
                    Services.AppFolderService.SafeFileName(name) + ext);
                TxtExportPath.Text = path;
            }

            // Determine format
            ImportExportFormat fmt;
            if (RbExpMoxfield.IsChecked == true) fmt = ImportExportFormat.Moxfield;
            else if (RbExpTcgPlayer.IsChecked == true) fmt = ImportExportFormat.TcgPlayer;
            else if (RbExpDeckbox.IsChecked == true) fmt = ImportExportFormat.Deckbox;
            else if (RbExpDragonShield.IsChecked == true) fmt = ImportExportFormat.DragonShield;
            else if (RbExpManaBox.IsChecked == true) fmt = ImportExportFormat.ManaBox;
            else if (RbExpArchidekt.IsChecked == true) fmt = ImportExportFormat.Archidekt;
            else if (RbExpPlainText?.IsChecked == true) fmt = ImportExportFormat.PlainText;
            else if (RbExpFullCsv.IsChecked == true) fmt = ImportExportFormat.FullCsv;
            else if (RbExpAvailableCsv.IsChecked == true) fmt = ImportExportFormat.AvailableCsv;
            else fmt = ImportExportFormat.BreakersOfE;

            Log($"💾 Exporting to: {Path.GetFileName(path)}");
            Log($"   Format: {fmt}");

            BtnAction.IsEnabled = false;
            try
            {
                if (RbExportDeck.IsChecked == true)
                {
                    // Use active deck or load from file path
                    Deck? deck = _activeDeck;
                    if (deck == null && !string.IsNullOrEmpty(TxtDeckPath?.Text))
                    {
                        string deckFile = TxtDeckPath.Text.Trim();
                        if (File.Exists(deckFile))
                            deck = DeckService.Load(deckFile);
                    }
                    if (deck == null)
                    {
                        Log("❌ No deck selected. Use Browse to pick a .deck file.");
                        return;
                    }
                    CollectionImportExportService.ExportDeck(path, fmt, deck);
                    Log($"✅ Deck exported: {deck.Cards.Count} cards.");
                }
                else if (RbExportWantList?.IsChecked == true)
                {
                    CollectionImportExportService.ExportWantList(path, fmt);
                    Log($"✅ Want List exported.");
                }
                else if (RbExportTradeBinder?.IsChecked == true)
                {
                    CollectionImportExportService.ExportTradeBinder(path, fmt);
                    Log($"✅ Trade Binder exported.");
                }
                else
                {
                    CollectionImportExportService.ExportCollection(path, fmt);
                    Log($"✅ Collection exported.");
                }

                StatusText.Text = $"Exported to {Path.GetFileName(path)}";

                // Open the folder
                if (MessageBox.Show("Export complete. Open folder?",
                    "Done", MessageBoxButton.YesNo, MessageBoxImage.Information)
                    == MessageBoxResult.Yes)
                    System.Diagnostics.Process.Start("explorer.exe",
                        Path.GetDirectoryName(path)!);
            }
            catch (Exception ex)
            {
                Log($"❌ Error: {ex.Message}");
            }
            finally
            {
                BtnAction.IsEnabled = true;
            }
        }

        private void LogResult(Services.CollectionImportResult result)
        {
            if (result.CardsImported > 0)
                Log($"✅ Imported:   {result.CardsImported} card(s)");
            if (result.CardsUpdated > 0)
                Log($"🔄 Updated:    {result.CardsUpdated} existing entry(s)");
            if (result.CardsNotFound > 0)
                Log($"⚠️  Not found:  {result.CardsNotFound} card(s) (not in your pool)");
            if (result.CardsSkipped > 0)
                Log($"⏭  Skipped:    {result.CardsSkipped} card(s)");
            foreach (var w in result.Warnings)
                Log($"   ⚠ {w}");
            foreach (var err in result.Errors)
                Log($"   ❌ {err}");

            StatusText.Text = result.Success
                ? $"Done — {result.CardsImported + result.CardsUpdated} card(s) processed"
                : "Completed with errors";
        }

        private void Log(string message)
        {
            LogBox.Text += (LogBox.Text.Length > 0 ? "\n" : "") + message;
            LogBox.ScrollToEnd();
        }

        private void BtnCopyLog_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(LogBox.Text)) return;
            Clipboard.SetText(LogBox.Text);
            StatusText.Text = "✅ Log copied to clipboard.";
        }

        private void BtnSaveLog_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(LogBox.Text)) return;
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save import/export log",
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = $"BreakersOfE_Log_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                InitialDirectory = AppFolderService.ExportsFolder
            };
            if (dlg.ShowDialog() == true)
            {
                System.IO.File.WriteAllText(dlg.FileName, LogBox.Text);
                StatusText.Text = $"✅ Log saved to {System.IO.Path.GetFileName(dlg.FileName)}";
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
            => Close();
    }
}