using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BreakersOfE.Services;

namespace BreakersOfE.Views.Dialogs
{
    /// <summary>
    /// v2's first start on a v1 data folder: says what will happen, converts
    /// (V1Conversion: backed up, counted before and after, put back if
    /// anything differs) and shows the counts. Nothing starts until it's done.
    /// v2 is installed and v1 is gone, so there's no way out: no Quit, and the
    /// window can't be closed until it's done. A failed conversion (files put
    /// back) offers Try Again or Close; BoE asks again at the next start.
    /// </summary>
    public partial class V1ConversionWindow : Wpf.Ui.Controls.FluentWindow
    {
        private enum Stage { Intro, Working, Done, Failed }
        private Stage _stage = Stage.Intro;
        private V1Conversion.Result? _result;
        private bool _continue;
        private bool _closeAllowed;

        /// <summary>Convert and show the result. True: converted, start BoE (the card data update starts by itself).</summary>
        public static bool Run()
        {
            var w = new V1ConversionWindow();
            w.ShowDialog();
            return w._continue;
        }

        public V1ConversionWindow()
        {
            InitializeComponent();
            FolderText.Text = "Your data folder: " + AppFolderService.RootFolder;
            // No way out until it's done (the X is hidden; this also stops Alt+F4).
            // Done: closing is the same as Continue. Failed: Close (or the X) ends BoE.
            Closing += (_, e) =>
            {
                if (_stage == Stage.Done) { _continue = true; return; }
                if (!_closeAllowed) e.Cancel = true;
            };
        }

        private async void BtnGo_Click(object sender, RoutedEventArgs e)
        {
            switch (_stage)
            {
                case Stage.Intro:
                case Stage.Failed:           // Try Again (v1's files were put back)
                    await ConvertAsync();
                    break;
                case Stage.Done:
                    _continue = true;
                    Close();
                    break;
            }
        }

        /// <summary>Failed only: close BoE (it asks again at the next start).</summary>
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            _closeAllowed = true;
            Close();
        }

        private void BtnBackup_Click(object sender, RoutedEventArgs e)
        {
            if (_result?.BackupFolder is { Length: > 0 } folder && System.IO.Directory.Exists(folder))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }

        private async Task ConvertAsync()
        {
            _stage = Stage.Working;
            _closeAllowed = false;
            Bar.ShowClose = false;
            IntroPanel.Visibility = Visibility.Collapsed;
            ResultPanel.Visibility = Visibility.Collapsed;
            ProblemText.Visibility = Visibility.Collapsed;
            NextText.Visibility = Visibility.Collapsed;
            BtnClose.Visibility = Visibility.Collapsed;
            BtnBackup.Visibility = Visibility.Collapsed;
            WorkPanel.Visibility = Visibility.Visible;
            BtnGo.IsEnabled = false;

            var progress = new Progress<string>(s => StepText.Text = s);
            string root = AppFolderService.RootFolder;
            try
            {
                _result = await Task.Run(() => V1Conversion.Run(root, progress));
            }
            catch (Exception ex)
            {
                _result = new V1Conversion.Result { Success = false, Message = "The conversion stopped: " + ex.Message };
            }

            if (_result.Success)
            {
                // Converted: v2 from here on (the review of foil / etched rows waits for the card data).
                var s = AppSettingsService.Current;
                s.ConvertedFromV1 = DateTime.Now;
                s.FinishReviewPending = true;
                AppSettingsService.Save(s);
            }
            ShowResult(_result);
        }

        private void ShowResult(V1Conversion.Result r)
        {
            _stage = r.Success ? Stage.Done : Stage.Failed;
            WorkPanel.Visibility = Visibility.Collapsed;
            ResultPanel.Visibility = Visibility.Visible;

            ResultText.Text = r.Success ? "Done. " + r.Message : r.Message;
            ResultText.SetResourceReference(TextBlock.ForegroundProperty,
                r.Success ? "TextFillColorPrimaryBrush" : "BoeWarningBrush");   // amber: attention only (ISA-101)

            FillCounts(r);

            if (r.Problems.Count > 0)
            {
                ProblemText.Text = "What didn't match:\n" + string.Join("\n", r.Problems.Select(p => "• " + p));
                ProblemText.Visibility = Visibility.Visible;
            }

            // What happens next. Try Again only when v1's files are back in place.
            bool canRetry = !r.Success && V1Conversion.IsV1Folder(AppFolderService.RootFolder);
            NextText.Text = r.Success
                ? "Next, BoE downloads the new card data. It starts by itself when you click Continue (internet needed, a few minutes)."
                : canRetry
                    ? "Nothing is lost. Try again, or close BoE: it asks to convert again the next time it starts."
                    : "Your v1 files are all in the backup folder. Copy them back into your data folder, then start BoE again.";
            NextText.Visibility = Visibility.Visible;

            BtnBackup.Visibility = r.BackupFolder.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            BtnClose.Visibility = r.Success ? Visibility.Collapsed : Visibility.Visible;
            _closeAllowed = !r.Success;
            Bar.ShowClose = true;                     // done: the X is Continue; failed: the X is Close
            BtnGo.Content = r.Success ? "Continue" : "Try Again";
            BtnGo.Visibility = r.Success || canRetry ? Visibility.Visible : Visibility.Collapsed;
            BtnGo.IsEnabled = true;
            if (BtnGo.Visibility == Visibility.Visible) BtnGo.Focus(); else BtnClose.Focus();
        }

        /// <summary>
        /// The before / after table. Rows may grow (older v1 rows holding foil
        /// copies become two), so a different row count is no problem: no ✓, never ≠.
        /// </summary>
        private void FillCounts(V1Conversion.Result r)
        {
            var b = r.Before;
            var a = r.After;
            bool after = a.Rows > 0 || r.Success;
            var lines = new (string Label, int Before, int After, bool Check)[]
            {
                ("Collection rows", b.Rows, a.Rows, false),
                ("Card copies", b.Copies, a.Copies, true),
                ("  of them foil or etched", b.FoilCopies, a.FoilCopies, true),
                ("Tokens", b.Tokens, a.Tokens, true),
                ("Copies used by decks and the Trade Binder", b.Claimed, a.Claimed, true),
                ("Decks (and Trade Binder) using cards", b.Decks, a.Decks, true),
                ("Trade Binder copies", b.BinderCopies, a.BinderCopies, true),
                ("Want List copies", b.WantCopies, a.WantCopies, true),
            };

            CountGrid.Children.Clear();
            CountGrid.RowDefinitions.Clear();
            AddRow(0, "", "Before (v1)", after ? "After (v2)" : "", "", header: true);
            for (int i = 0; i < lines.Length; i++)
            {
                var l = lines[i];
                string mark = !after ? "" : l.Before == l.After ? "✓" : l.Check ? "≠" : "";
                AddRow(i + 1, l.Label, l.Before.ToString("N0"), after ? l.After.ToString("N0") : "", mark, header: false);
            }
        }

        private void AddRow(int row, string label, string before, string after, string mark, bool header)
        {
            CountGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            void Cell(int col, string text, bool right)
            {
                var tb = new TextBlock
                {
                    Text = text,
                    Margin = new Thickness(0, 2, 0, 2),
                    TextAlignment = right ? TextAlignment.Right : TextAlignment.Left,
                    FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal,
                };
                // Normal text; a count that differs is the one thing in amber.
                tb.SetResourceReference(TextBlock.ForegroundProperty,
                    mark == "≠" && col == 3 ? "BoeWarningBrush" : header ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush");
                Grid.SetRow(tb, row);
                Grid.SetColumn(tb, col);
                CountGrid.Children.Add(tb);
            }
            Cell(0, label, false);
            Cell(1, before, true);
            Cell(2, after, true);
            Cell(3, mark, true);
        }
    }
}
