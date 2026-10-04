using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BreakersOfE.Models;
using BreakersOfE.Services;

namespace BreakersOfE.Views.Pages
{
    /// <summary>
    /// Edit → Import / Export.
    ///
    /// Import: a file or pasted text is read (format detected, or picked),
    /// every line matched to the card pool, and shown in a PREVIEW — matched,
    /// "check" (several printings fit; one picked, others in the dropdown),
    /// problem (e.g. a finish the printing doesn't come in) or not found.
    /// Nothing changes until Import; one Undo takes the whole import back.
    ///
    /// Export: the collection, a deck, the Want List or the Trade Binder as a
    /// text list (MTG Deck Tools, Moxfield …), a ManaBox CSV (MTG Deck Tools'
    /// upload) or a spreadsheet CSV — previewed, then copied or saved. Plus
    /// BoE's own full collection file.
    /// </summary>
    public partial class ImportExportPage : Page
    {
        private readonly List<ImportPreviewRow> _rows = new();
        private ImportFormat _readAs = ImportFormat.Auto;
        private string _sourceName = "";
        private string _lastText = "";
        private ImportUndo? _undo;
        private static CardMatcher? _matcher;           // the card pool, loaded once …
        private static DateTime _matcherStamp;          // … and again after a database update
        private bool _busy;

        private sealed record TargetChoice(ImportTarget Target, string Text)
        {
            public override string ToString() => Text;
        }

        private sealed record FormatChoice(ImportFormat Format)
        {
            public override string ToString() => ImportParsers.Display(Format);
        }

        private sealed record ExportSourceChoice(ExportSource Source, string Text)
        {
            public override string ToString() => Text;
        }

        private sealed record ExportFormatChoice(ExportFormat Format, string Text);

        private sealed record DeckChoice(string Path, string Text);

        // Export state
        private bool _exportOn;                         // the Export tab has been shown (nothing is built before)
        private int _exportVersion;                     // newest build wins
        private ExportResult? _export;
        private bool _decksLoaded, _loadingDecks, _refreshPending;

        public ImportExportPage()
        {
            InitializeComponent();
            TargetBox.ItemsSource = new[]
            {
                new TargetChoice(ImportTarget.Collection, "Collection (cards and tokens)"),
                new TargetChoice(ImportTarget.WantList, "Want List"),
                new TargetChoice(ImportTarget.NewDeck, "A new deck"),
            };
            TargetBox.SelectedIndex = 0;
            FormatBox.ItemsSource = Enum.GetValues<ImportFormat>().Select(f => new FormatChoice(f)).ToList();
            FormatBox.SelectedIndex = 0;
            LanguageBox.ItemsSource = CardLanguage.All;
            LanguageBox.SelectedItem = AppSettingsService.Current.DefaultLanguage;      // Settings → Collection defaults
            ConditionBox.ItemsSource = CardCondition.All;
            ConditionBox.SelectedItem = AppSettingsService.Current.DefaultCondition;

            ExportSourceBox.ItemsSource = new[]
            {
                new ExportSourceChoice(ExportSource.Collection, "My collection"),
                new ExportSourceChoice(ExportSource.Deck, "A deck"),
                new ExportSourceChoice(ExportSource.WantList, "Want List"),
                new ExportSourceChoice(ExportSource.TradeBinder, "Trade Binder"),
            };
            ExportSourceBox.SelectedIndex = 0;
            ExportFormatBox.ItemsSource = FormatsFor(ExportSource.Collection);
            ExportFormatBox.SelectedIndex = 0;
            ExportOnlyFree.IsChecked = true;
        }

        private ImportTarget Target => (TargetBox.SelectedItem as TargetChoice)?.Target ?? ImportTarget.Collection;
        private ImportFormat Format => (FormatBox.SelectedItem as FormatChoice)?.Format ?? ImportFormat.Auto;
        private ImportMode Mode => ReplaceRadio.IsChecked == true ? ImportMode.Replace : ImportMode.Add;
        private string DefaultLanguage => LanguageBox.SelectedItem as string ?? CardLanguage.Default;
        private string DefaultCondition => ConditionBox.SelectedItem as string ?? CardCondition.Default;

        // ══════════════════════════════════════════════════════════════════
        // READ
        // ══════════════════════════════════════════════════════════════════
        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import cards from…",
                Filter = "Card lists (*.csv;*.txt)|*.csv;*.txt|All files (*.*)|*.*",
                InitialDirectory = Directory.Exists(AppFolderService.ImportsFolder) ? AppFolderService.ImportsFolder : "",
            };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            string text;
            try { text = File.ReadAllText(dlg.FileName); }
            catch (Exception ex) { ShowStatus($"Could not open the file: {ex.Message}", true); return; }
            PastePanel.Visibility = Visibility.Collapsed;
            if (string.IsNullOrWhiteSpace(DeckNameBox.Text) || Target != ImportTarget.NewDeck)
                DeckNameBox.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
            _ = Read(text, Path.GetFileName(dlg.FileName));
        }

        private void BtnPaste_Click(object sender, RoutedEventArgs e)
        {
            PastePanel.Visibility = PastePanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            if (PastePanel.Visibility == Visibility.Visible)
            {
                if (PasteBox.Text.Length == 0 && Clipboard.ContainsText()) PasteBox.Text = Clipboard.GetText();
                PasteBox.Focus();
            }
        }

        private void BtnReadPasted_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(PasteBox.Text)) { ShowStatus("Paste a card list into the box first.", true); return; }
            _ = Read(PasteBox.Text, "pasted text");
        }

        private void BtnClearPasted_Click(object sender, RoutedEventArgs e) => PasteBox.Clear();

        /// <summary>Read → match → preview (on a background thread; the card pool loads the first time).</summary>
        private async Task Read(string text, string sourceName)
        {
            if (_busy) return;
            _busy = true;
            SetBusy(true, _matcher == null ? "Loading the card pool (first time only)…" : "Reading…");
            try
            {
                var format = Format;
                var target = Target;
                var (parse, rows) = await Task.Run(() =>
                {
                    var p = ImportParsers.Parse(text, format);
                    var list = new List<ImportPreviewRow>();
                    if (p.Error.Length > 0) return (p, list);
                    var stamp = File.Exists(AppFolderService.DatabasePath) ? File.GetLastWriteTimeUtc(AppFolderService.DatabasePath) : DateTime.MinValue;
                    if (_matcher == null || stamp != _matcherStamp)
                    {
                        _matcher = CardMatcher.Load();
                        _matcherStamp = stamp;
                    }
                    foreach (var line in p.Lines)
                        list.Add(ImportPreviewRow.Make(line, _matcher!));
                    return (p, list);
                });

                _sourceName = sourceName;
                _lastText = text;
                _readAs = parse.Format;
                _rows.Clear();
                if (parse.Error.Length > 0)
                {
                    PreviewGrid.ItemsSource = null;
                    SummaryText.Text = parse.Error;
                    SourceText.Text = sourceName;
                    ShowStatus(parse.Error, true);
                    UpdateApply();
                    return;
                }
                _rows.AddRange(rows);
                foreach (var r in _rows)
                {
                    r.Recompute(target, DefaultLanguage, DefaultCondition);
                    r.Changed += Row_Changed;          // a dropdown changed it: keep the summary current
                }
                SourceText.Text = $"{sourceName} — read as {ImportParsers.Display(parse.Format)}";
                ShowGrid();
                ShowSummary(parse);
                string problems = parse.Problems.Count == 0 ? "" :
                    $"{parse.Problems.Count} line(s) couldn't be read: " + string.Join("  ", parse.Problems.Take(4)) +
                    (parse.Problems.Count > 4 ? " …" : "");
                ShowStatus(problems, problems.Length > 0);
            }
            catch (Exception ex)
            {
                ShowStatus($"Could not read it: {ex.Message}", true);
            }
            finally
            {
                _busy = false;
                SetBusy(false, null);
            }
        }

        private void ShowGrid()
        {
            PreviewGrid.ItemsSource = OnlyAttention.IsChecked == true
                ? _rows.Where(r => r.NeedsAttention).ToList()
                : _rows.ToList();
        }

        private void ShowSummary(ImportParseResult? parse = null)
        {
            if (_rows.Count == 0) { UpdateApply(); return; }
            int ready = _rows.Count(r => r.Status == MatchStatus.Ready);
            int check = _rows.Count(r => r.Status == MatchStatus.Check);
            int problem = _rows.Count(r => r.Status == MatchStatus.Problem);
            int notFound = _rows.Count(r => r.Status == MatchStatus.NotFound);
            int skipped = _rows.Count(r => r.Status == MatchStatus.Skipped);
            int copies = _rows.Where(r => r.WillImport).Sum(r => r.Quantity);
            var parts = new List<string> { $"{_rows.Count:N0} lines: {ready:N0} ready" };
            if (check > 0) parts.Add($"{check:N0} to check (several printings fit — one is picked, change it if needed)");
            if (problem > 0) parts.Add($"{problem:N0} with a problem (left out)");
            if (notFound > 0) parts.Add($"{notFound:N0} not found (left out)");
            if (skipped > 0) parts.Add($"{skipped:N0} skipped (Maybeboard)");
            SummaryText.Text = string.Join(" · ", parts) + $".  {copies:N0} copies will be imported.";
            UpdateApply();
        }

        // ══════════════════════════════════════════════════════════════════
        // IMPORT / UNDO
        // ══════════════════════════════════════════════════════════════════
        private async void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            var go = _rows.Where(r => r.WillImport).ToList();
            if (go.Count == 0 || _busy) return;
            var target = Target;
            var mode = target == ImportTarget.NewDeck ? ImportMode.Add : Mode;
            int copies = go.Sum(r => r.Quantity);
            int left = _rows.Count - go.Count;
            int unresolved = _rows.Count(r => r.Status is MatchStatus.NotFound or MatchStatus.Problem);
            if (mode == ImportMode.Replace && unresolved > 0)
            {
                // Left-out lines would count as "not in the file" — and those cards would be removed.
                ShowStatus($"Replace needs every line matched: {unresolved} line(s) are not found or have a problem. " +
                           "Fix them (pick a printing or finish), or use Add instead — otherwise those cards would be removed.", true);
                OnlyAttention.IsChecked = true;
                return;
            }
            string where = target switch
            {
                ImportTarget.WantList => "the Want List",
                ImportTarget.NewDeck => $"a new deck \"{(string.IsNullOrWhiteSpace(DeckNameBox.Text) ? "Imported Deck" : DeckNameBox.Text.Trim())}\"",
                _ => "your collection",
            };
            string question = mode == ImportMode.Replace
                ? $"REPLACE: make {where} match this file exactly?\n\n" +
                  (target == ImportTarget.WantList
                      ? "Everything on the Want List that isn't in the file is removed."
                      : "In each collection table the file has cards for, rows not in the file are removed — copies claimed by decks or the Trade Binder stay.") +
                  $"\n\nThe file has {copies:N0} copies in {go.Count:N0} lines." +
                  (left > 0 ? $"\n{left:N0} line(s) with problems or not found are left out." : "") +
                  "\n\nA backup is made first, and Undo Last Import takes it all back."
                : $"Import {copies:N0} copies ({go.Count:N0} lines) into {where}?" +
                  (left > 0 ? $"\n\n{left:N0} line(s) with problems or not found are left out." : "") +
                  (target == ImportTarget.NewDeck ? "" : "\n\nUndo Last Import takes it back.");
            if (MessageBox.Show(Window.GetWindow(this), question, mode == ImportMode.Replace ? "Replace" : "Import",
                    MessageBoxButton.OKCancel, mode == ImportMode.Replace ? MessageBoxImage.Warning : MessageBoxImage.Question) != MessageBoxResult.OK)
                return;

            var items = go.Select(r => r.ToItem()).ToList();
            string description = $"import of {_sourceName} ({copies:N0} copies into {where})";
            string deckName = DeckNameBox.Text;
            _busy = true;
            SetBusy(true, "Importing…");
            try
            {
                var (result, undo) = await Task.Run(() =>
                {
                    switch (target)
                    {
                        case ImportTarget.WantList:
                            return CollectionEditService.ImportIntoWantList(items, mode, description);
                        case ImportTarget.NewDeck:
                            var (r, u, _) = CollectionEditService.ImportAsDeck(items, deckName, description);
                            return (r, u);
                        default:
                            return CollectionEditService.ImportIntoCollection(items, mode, description);
                    }
                });
                if (undo != null) _undo = undo;
                ShowStatus(result.Message, result.Warning);
                if (undo != null && !result.Message.StartsWith("The import stopped"))
                {
                    // Done: clear the preview so the same list can't be imported twice by accident.
                    _rows.Clear();
                    _lastText = "";
                    PreviewGrid.ItemsSource = null;
                    SummaryText.Text = "Imported. Open another file or paste another list to import more.";
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"The import stopped: {ex.Message}", true);
            }
            finally
            {
                _busy = false;
                SetBusy(false, null);
            }
        }

        private async void BtnUndo_Click(object sender, RoutedEventArgs e)
        {
            if (_undo == null || _busy) return;
            if (MessageBox.Show(Window.GetWindow(this), $"Undo the {_undo.Text}?", "Undo Last Import",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                return;
            var undo = _undo;
            _busy = true;
            SetBusy(true, "Undoing…");
            try
            {
                var result = await Task.Run(() => CollectionEditService.UndoImport(undo));
                ShowStatus(result.Message, result.Warning);
                if (!result.Warning) _undo = null;
            }
            finally
            {
                _busy = false;
                SetBusy(false, null);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // EXPORT
        // ══════════════════════════════════════════════════════════════════
        private ExportSource ExportSourceNow => (ExportSourceBox.SelectedItem as ExportSourceChoice)?.Source ?? ExportSource.Collection;
        private ExportFormat ExportFormatNow => (ExportFormatBox.SelectedItem as ExportFormatChoice)?.Format ?? ExportFormat.TextList;

        private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Only the tabs themselves (combo boxes and the grid inside bubble up here too).
            if (!ReferenceEquals(e.OriginalSource, Tabs) || Tabs.SelectedIndex != 1) return;
            _exportOn = true;
            _decksLoaded = false;                         // a new deck (an import) shows up too
            _ = RefreshExport();                          // fresh every visit: an import may have changed things
        }

        private static readonly ExportFormatChoice[] AllFormats =
        {
            new(ExportFormat.TextList, "Text list — MTG Deck Tools, Moxfield, Archidekt (paste)"),
            new(ExportFormat.ManaBoxCsv, "ManaBox CSV — MTG Deck Tools upload, ManaBox"),
            new(ExportFormat.SpreadsheetCsv, "Spreadsheet CSV — every detail, for Excel"),
            new(ExportFormat.BoeFull, "BoE full backup — every table and detail (restore: Import → Replace)"),
        };

        /// <summary>The formats a list can be exported as (the BoE backup is the whole collection only).</summary>
        private static List<ExportFormatChoice> FormatsFor(ExportSource source) =>
            AllFormats.Where(f => f.Format != ExportFormat.BoeFull || source == ExportSource.Collection).ToList();

        private bool _settingFormats;

        private void ExportOption_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized || _settingFormats) return;
            if (ReferenceEquals(sender, ExportSourceBox))
            {
                // The format list follows the source; keep the chosen format when it's still offered.
                var keep = ExportFormatNow;
                var formats = FormatsFor(ExportSourceNow);
                _settingFormats = true;
                try
                {
                    ExportFormatBox.ItemsSource = formats;
                    ExportFormatBox.SelectedItem = formats.FirstOrDefault(f => f.Format == keep) ?? formats[0];
                }
                finally { _settingFormats = false; }
            }
            if (_exportOn) _ = RefreshExport();
        }

        /// <summary>The deck files, for the deck picker (read again on each visit to the tab).</summary>
        private async Task LoadDecks()
        {
            if (_loadingDecks) return;
            _loadingDecks = true;
            try
            {
                string? keep = (ExportDeckBox.SelectedItem as DeckChoice)?.Path;
                var decks = await Task.Run(() => DeckIndexService.AllDecks()
                    .Select(d => new DeckChoice(d.Path, !string.IsNullOrWhiteSpace(d.Deck.Name) ? d.Deck.Name : Path.GetFileNameWithoutExtension(d.Path)))
                    .OrderBy(d => d.Text, StringComparer.OrdinalIgnoreCase)
                    .ToList());
                ExportDeckBox.ItemsSource = decks;
                ExportDeckBox.SelectedItem = decks.FirstOrDefault(d => d.Path == keep) ?? decks.FirstOrDefault();
                _decksLoaded = true;
                _refreshPending = true;                   // build for the deck now picked (or say there are none)
            }
            catch (Exception ex)
            {
                ExportText.Text = $"Could not read the decks: {ex.Message}";
            }
            finally
            {
                _loadingDecks = false;
                if (_refreshPending)
                {
                    // Choices changed (or the deck got picked) while the list loaded: build for them now.
                    _refreshPending = false;
                    _ = RefreshExport();
                }
            }
        }

        /// <summary>Build the export for the current choices and show it in the preview.</summary>
        private async Task RefreshExport()
        {
            int version = ++_exportVersion;               // newest build wins; anything older is dropped
            _export = null;
            UpdateExportButtons();
            ExportText.Text = "";                         // an older "Saved …" / "Copied …" no longer applies
            EditPageKit.ShowStatus(ExportSummary, "Building…", false);
            if (_loadingDecks) { _refreshPending = true; return; }   // rebuilt when the deck list is in
            var source = ExportSourceNow;
            var format = ExportFormatNow;
            ExportDeckBox.Visibility = source == ExportSource.Deck ? Visibility.Visible : Visibility.Collapsed;
            bool full = format == ExportFormat.BoeFull;   // the backup is always everything
            ExportOnlyFree.IsEnabled = source == ExportSource.Collection && !full;
            ExportTokens.IsEnabled = (source is ExportSource.Collection or ExportSource.Deck) && !full;
            ExportMarkFinish.IsEnabled = format == ExportFormat.TextList;

            if (source == ExportSource.Deck && !_decksLoaded)
            {
                await LoadDecks();
                if (!_decksLoaded) EditPageKit.ShowStatus(ExportSummary, "Could not read the decks.", true);
                return;                                   // LoadDecks builds again with the deck picked
            }
            string deckPath = (ExportDeckBox.SelectedItem as DeckChoice)?.Path ?? "";
            if (source == ExportSource.Deck && deckPath.Length == 0)
            {
                ExportPreview.Text = "";
                EditPageKit.ShowStatus(ExportSummary, "No decks found.", true);
                return;
            }
            var options = new ExportOptions
            {
                Source = source,
                Format = format,
                DeckPath = deckPath,
                OnlyFree = source == ExportSource.Collection && !full && ExportOnlyFree.IsChecked == true,
                IncludeTokens = (source is ExportSource.Collection or ExportSource.Deck) && ExportTokens.IsChecked == true,
                MarkFinish = format == ExportFormat.TextList && ExportMarkFinish.IsChecked == true,
            };
            ExportResult result;
            try { result = await Task.Run(() => CollectionEditService.Export(options)); }
            catch (Exception ex) { result = new ExportResult { Error = $"Could not export: {ex.Message}" }; }
            if (version != _exportVersion) return;        // the choices changed meanwhile: a newer build is coming

            if (result.Error.Length > 0)
            {
                ExportPreview.Text = "";
                EditPageKit.ShowStatus(ExportSummary, result.Error, true);
                return;
            }
            _export = result;
            ExportPreview.Text = result.Text;
            ExportPreview.ScrollToHome();
            string summary = result.Lines == 0
                ? "Nothing to export" + (options.OnlyFree ? " (every copy is claimed by decks or the Trade Binder)." : ".")
                : $"{result.Lines:N0} lines, {result.Copies:N0} copies.";
            EditPageKit.ShowStatus(ExportSummary, result.Note.Length > 0 ? $"{summary}  {result.Note}" : summary,
                                   result.Note.Length > 0 || result.Lines == 0);
            UpdateExportButtons();
        }

        private void UpdateExportButtons()
        {
            bool ok = !_busy && _export != null && _export.Lines > 0;
            BtnExportCopy.IsEnabled = BtnExportSave.IsEnabled = ok;
        }

        private void BtnExportCopy_Click(object sender, RoutedEventArgs e)
        {
            if (_export == null || _export.Lines == 0) return;
            try
            {
                Clipboard.SetText(_export.Text);
                ExportText.Text = $"Copied {_export.Lines:N0} lines to the clipboard — paste them into the website.";
            }
            catch (Exception ex)
            {
                ExportText.Text = $"Could not copy (another program may be using the clipboard — try again): {ex.Message}";
            }
        }

        private async void BtnExportSave_Click(object sender, RoutedEventArgs e)
        {
            var export = _export;
            if (export == null || export.Lines == 0 || _busy) return;
            var format = ExportFormatNow;
            bool csv = format != ExportFormat.TextList;
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save the export",
                Filter = csv ? "CSV file (*.csv)|*.csv" : "Text file (*.txt)|*.txt",
                FileName = export.FileName,
                InitialDirectory = Directory.Exists(AppFolderService.ExportsFolder) ? AppFolderService.ExportsFolder : "",
            };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            string path = dlg.FileName;
            // Excel needs the UTF-8 mark to show accents; websites and other apps read plain UTF-8 best.
            var encoding = new System.Text.UTF8Encoding(format is ExportFormat.SpreadsheetCsv or ExportFormat.BoeFull);
            try
            {
                await Task.Run(() =>
                {
                    string tmp = path + ".saving";
                    File.WriteAllText(tmp, export.Text, encoding);
                    File.Move(tmp, path, overwrite: true);
                });
                EditPageKit.ShowStatus(ExportText, $"Saved {export.Lines:N0} lines to {path}", false);
            }
            catch (Exception ex)
            {
                ExportText.Text = $"Could not save: {ex.Message}";
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // OPTIONS
        // ══════════════════════════════════════════════════════════════════
        private void TargetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsInitialized) return;
            bool deck = Target == ImportTarget.NewDeck;
            DeckNamePanel.Visibility = deck ? Visibility.Visible : Visibility.Collapsed;
            AddRadio.IsEnabled = ReplaceRadio.IsEnabled = !deck;
            if (deck) AddRadio.IsChecked = true;
            bool wants = Target == ImportTarget.WantList;
            LanguageBox.IsEnabled = ConditionBox.IsEnabled = !wants && !deck;     // want rows and deck lines have none
            RecomputeAll();
        }

        private void FormatBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Read again the way the user picked (the same file or text).
            if (IsInitialized && _lastText.Length > 0 && !_busy) _ = Read(_lastText, _sourceName);
        }

        private void Defaults_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsInitialized) RecomputeAll();
        }

        private void Mode_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized) return;
            BtnApply.Content = ReplaceRadio.IsChecked == true ? "Replace…" : "Import";
        }

        private void OnlyAttention_Changed(object sender, RoutedEventArgs e)
        {
            if (_rows.Count > 0) ShowGrid();
        }

        private void RecomputeAll()
        {
            if (_rows.Count == 0) return;
            foreach (var r in _rows) r.Recompute(Target, DefaultLanguage, DefaultCondition);
            ShowGrid();
            ShowSummary();
        }

        private void UpdateApply()
        {
            BtnApply.IsEnabled = !_busy && _rows.Any(r => r.WillImport);
            BtnUndo.IsEnabled = !_busy && _undo != null;
            BtnUndo.ToolTip = _undo == null ? "Nothing to undo" : $"Undo the {_undo.Text}";
        }

        private void SetBusy(bool busy, string? text)
        {
            BtnOpen.IsEnabled = BtnPaste.IsEnabled = BtnReadPasted.IsEnabled = TargetBox.IsEnabled = FormatBox.IsEnabled =
                PreviewGrid.IsEnabled = !busy;
            UpdateExportButtons();
            AddRadio.IsEnabled = ReplaceRadio.IsEnabled = !busy && Target != ImportTarget.NewDeck;
            if (text != null) ShowStatus(text, false);
            UpdateApply();
            System.Windows.Input.Mouse.OverrideCursor = busy ? System.Windows.Input.Cursors.Wait : null;
        }

        private void ShowStatus(string text, bool warning) => EditPageKit.ShowStatus(ActionText, text, warning);

        private void Page_Loaded(object sender, RoutedEventArgs e) => EditPageKit.DisableHostScroll(this);

        private void Row_Changed() => ShowSummary();
    }

    /// <summary>
    /// One line of the import preview: the line as read, the printing it
    /// matched (changeable when several fit), its finish (only finishes the
    /// printing comes in), and whether it will be imported.
    /// </summary>
    public sealed class ImportPreviewRow : INotifyPropertyChanged
    {
        public ImportLine Line { get; private init; } = new();
        public int LineNumber => Line.LineNumber;
        public int Quantity => Line.Quantity;
        public string Source => Line.Source;
        public List<PoolPrinting> Candidates { get; private init; } = new();
        public bool CanChoose => Candidates.Count > 1;
        private string _how = "";
        private bool _userChose, _userFinish;
        private ImportTarget _target;
        private string _defLang = CardLanguage.Default, _defCond = CardCondition.Default;

        public event PropertyChangedEventHandler? PropertyChanged;
        /// <summary>The status changed after a dropdown edit (the page refreshes its summary).</summary>
        public event Action? Changed;

        public static ImportPreviewRow Make(ImportLine line, CardMatcher matcher)
        {
            List<PoolPrinting> found;
            string how;
            // BoE full file: rows of the special tables (planes, schemes …) by their own pool.
            if (line.Table is { Length: > 0 } && !CollectionEditService.PaperTables.Contains(line.Table))
                line.Table = null;                         // not a collection table (a typo, "WantList" …): match normally
            if (line.Table is { Length: > 0 } t && t != CollectionEditService.CardsTable && t != CollectionEditService.TokensTable &&
                line.ScryfallId.Length > 0 && CollectionEditService.PrintingFor(t, line.ScryfallId) is { } special)
            {
                found = new List<PoolPrinting> { special };
                how = "Scryfall ID";
            }
            else found = matcher.Match(line, out how);
            var row = new ImportPreviewRow { Line = line, Candidates = found, _how = how };
            row._chosen = found.FirstOrDefault();
            return row;
        }

        private PoolPrinting? _chosen;
        public PoolPrinting? Chosen
        {
            get => _chosen;
            set
            {
                if (value == null || ReferenceEquals(value, _chosen)) return;   // a recycled cell must not clear it
                _chosen = value;
                _userChose = true;
                _userFinish = false;
                Recompute(_target, _defLang, _defCond);
                Changed?.Invoke();
            }
        }

        public string CardName => _chosen?.Name ?? (Line.Name.Length > 0 ? Line.Name : "—");

        public List<string> FinishChoices { get; private set; } = new();
        private string? _finish;
        public string? FinishDisplay
        {
            get => _finish == null ? null : CardFinish.Display(_finish);
            set
            {
                if (value == null) return;
                string f = value switch { "Foil" => CardFinish.Foil, "Etched" => CardFinish.Etched, _ => CardFinish.NonFoil };
                if (f == _finish) return;
                _finish = f;
                _userFinish = true;
                Recompute(_target, _defLang, _defCond);
                Changed?.Invoke();
            }
        }

        public string LanguageText { get; private set; } = "";
        public string ConditionText { get; private set; } = "";
        public MatchStatus Status { get; private set; }
        public string StatusText { get; private set; } = "";
        public Brush StatusBrush { get; private set; } = Brushes.Gray;

        public bool WillImport => Status is MatchStatus.Ready or MatchStatus.Check;
        public bool NeedsAttention => Status is not MatchStatus.Ready;

        private static readonly Brush Amber = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0xA3, 0x17)));
        private static readonly Brush Red = Freeze(new SolidColorBrush(Color.FromRgb(0xD1, 0x34, 0x38)));
        private static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

        /// <summary>Work out finish, language, condition and status for the current choices.</summary>
        public void Recompute(ImportTarget target, string defaultLanguage, string defaultCondition)
        {
            _target = target;
            _defLang = defaultLanguage;
            _defCond = defaultCondition;
            var p = _chosen;
            bool collection = target == ImportTarget.Collection;
            LanguageText = collection ? Line.Language ?? defaultLanguage : "—";
            ConditionText = collection ? Line.Condition ?? defaultCondition : "—";

            if (Line.Section == ImportSection.Maybe)
            {
                Set(MatchStatus.Skipped, "Maybeboard — not imported", Brushes.Gray);
                return;
            }
            if (p == null)
            {
                FinishChoices = new List<string>();
                _finish = null;
                Set(MatchStatus.NotFound, Line.Name.Length > 0 ? "Not found in the card pool — left out" : "No card name — left out", Red);
                return;
            }
            FinishChoices = p.Finishes().Select(CardFinish.Display).ToList();
            string problem = "";
            if (!_userFinish)
                _finish = CollectionEditService.ResolveFinish(Line.Finish, p, out problem);
            if (_finish == null)
            {
                Set(MatchStatus.Problem, $"{CardFinish.Display(Line.Finish)} asked for, but this printing {problem} — pick a finish or leave it out", Amber);
                return;
            }
            if (!p.Has(_finish))
            {
                Set(MatchStatus.Problem, $"This printing doesn't come in {CardFinish.Display(_finish)} — pick a finish", Amber);
                return;
            }
            if (Line.Quantity <= 0) { Set(MatchStatus.Problem, "0 copies — left out", Amber); return; }
            if (target == ImportTarget.WantList && p.Table != CollectionEditService.CardsTable)
            {
                Set(MatchStatus.Problem, $"{(p.IsToken ? "A token" : "Not a regular card")} — the Want List holds cards; left out", Amber);
                return;
            }
            if (target == ImportTarget.NewDeck && p.Table != CollectionEditService.CardsTable && !p.IsToken)
            {
                Set(MatchStatus.Problem, "Not a card a deck can hold (plane, scheme …) — left out", Amber);
                return;
            }
            string finishNote = Line.Finish != null && CardFinish.Normalize(Line.Finish) != _finish && !_userFinish
                ? $" · {CardFinish.Display(Line.Finish)} read as {CardFinish.Display(_finish)} (this printing's only foil is etched)"
                : "";
            if (Candidates.Count > 1 && !_userChose)
                Set(MatchStatus.Check, $"Check: matched by {_how}; picked {p.SetCode.ToUpperInvariant()} #{p.CollectorNumber}{finishNote}", Amber);
            else
                Set(MatchStatus.Ready, (_userChose ? "Ready (your pick)" : $"Ready — {_how}") + finishNote,
                    (Brush)Application.Current.FindResource("TextFillColorSecondaryBrush"));
        }

        private void Set(MatchStatus s, string text, Brush brush)
        {
            Status = s;
            StatusText = text;
            StatusBrush = brush;
            foreach (var n in new[] { nameof(Status), nameof(StatusText), nameof(StatusBrush), nameof(FinishChoices),
                                      nameof(FinishDisplay), nameof(Chosen), nameof(CardName), nameof(LanguageText),
                                      nameof(ConditionText), nameof(WillImport), nameof(NeedsAttention) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }

        /// <summary>The line as the import service takes it.</summary>
        public ImportItem ToItem() => new()
        {
            Printing = _chosen!,
            Finish = _finish ?? CardFinish.NonFoil,
            Language = Line.Language ?? _defLang,
            Condition = Line.Condition ?? _defCond,
            Quantity = Line.Quantity,
            Notes = Line.Notes,
            Storage = Line.Storage,
            Favorite = Line.Favorite,
            DateAdded = Line.DateAdded,
            Section = Line.Section,
        };
    }
}
