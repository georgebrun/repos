using System;
using System.Windows;
using BreakersOfE.Services;
using Wpf.Ui.Controls;

namespace BreakersOfE.Views.Dialogs
{
    /// <summary>
    /// First start after installing (no data folder yet): pick where BoE
    /// keeps its data. Closing it without choosing uses the default
    /// (Documents\BoE_V2). Shown once — afterwards Settings → Data folder.
    /// </summary>
    public partial class FirstRunFolderDialog : FluentWindow
    {
        private string _picked;
        private bool _chosen;

        private FirstRunFolderDialog()
        {
            InitializeComponent();
            _picked = AppFolderService.DefaultRootFolder;
            ShowPick();
            Closing += (_, _) =>
            {
                if (!_chosen) AppFolderService.ChooseFirstFolder(AppFolderService.DefaultRootFolder);
            };
        }

        /// <summary>Ask, and set the data folder (before anything is created in it).</summary>
        public static void Ask() => new FirstRunFolderDialog().ShowDialog();

        private string Target => AppFolderService.DataFolderFor(_picked);

        private void ShowPick()
        {
            string target = Target;
            FolderBox.Text = target;
            bool isDefault = string.Equals(target.TrimEnd('\\'), AppFolderService.DefaultRootFolder.TrimEnd('\\'),
                                           StringComparison.OrdinalIgnoreCase);
            NoteText.Text = isDefault ? "This is the default folder."
                : !string.Equals(target, System.IO.Path.GetFullPath(_picked), StringComparison.OrdinalIgnoreCase)
                    ? "That folder already has other files in it, so BoE makes its own \"BoE_V2\" folder inside it."
                    : System.IO.File.Exists(System.IO.Path.Combine(target, "breakersofe.db")) ||
                      System.IO.File.Exists(System.IO.Path.Combine(target, "Collection", "collection.db"))
                        ? "BoE data is already in that folder — BoE will use it."
                        : "";
            BtnDefault.Visibility = isDefault ? Visibility.Collapsed : Visibility.Visible;
            ErrorText.Visibility = Visibility.Collapsed;
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Where should BoE keep its data?" };
            if (dlg.ShowDialog(this) != true) return;
            _picked = dlg.FolderName;
            ShowPick();
        }

        private void Default_Click(object sender, RoutedEventArgs e)
        {
            _picked = AppFolderService.DefaultRootFolder;
            ShowPick();
        }

        private void Start_Click(object sender, RoutedEventArgs e)
        {
            string problem = AppFolderService.ChooseFirstFolder(Target);
            if (problem.Length > 0)
            {
                ErrorText.Text = problem;
                ErrorText.Visibility = Visibility.Visible;
                return;
            }
            _chosen = true;
            Close();
        }
    }
}
