using System;
using System.IO;
using System.Linq;
using System.Windows;
using Wpf.Ui.Controls;

namespace BreakersOfE.Views.Dialogs
{
    /// <summary>The Rule 0 sheet for a draft (DeckBuilder.Rule0): shown as text, to copy or save as a .txt file.</summary>
    public partial class Rule0Window : FluentWindow
    {
        private readonly string _deckName;

        public Rule0Window(string sheet, string deckName, Window? owner)
        {
            InitializeComponent();
            if (owner != null) Owner = owner;
            _deckName = deckName;
            SheetText.Text = sheet;
            KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.F1) HelpWindow.Open("build"); };
        }

        /// <summary>Close (the window isn't a dialog, so the Cancel button doesn't close it by itself).</summary>
        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(SheetText.Text);
                StatusText.Text = "Copied. Paste it into a chat or note for your playgroup.";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Couldn't copy: {ex.Message}";
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string safe = new string(_deckName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray()).Trim();
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save the Rule 0 Sheet",
                FileName = $"{(safe.Length > 0 ? safe : "Deck")} - Rule 0.txt",
                Filter = "Text file (*.txt)|*.txt",
                DefaultExt = ".txt",
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                File.WriteAllText(dlg.FileName, SheetText.Text);
                StatusText.Text = $"Saved {Path.GetFileName(dlg.FileName)}.";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Couldn't save: {ex.Message}";
            }
        }
    }
}
