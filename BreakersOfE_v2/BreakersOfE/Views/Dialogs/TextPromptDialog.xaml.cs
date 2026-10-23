using System.Windows;
using Wpf.Ui.Controls;

namespace BreakersOfE.Views.Dialogs
{
    /// <summary>Ask for one line of text (e.g. a new deck's name).</summary>
    public partial class TextPromptDialog : FluentWindow
    {
        public string Value => InputBox.Text;

        public TextPromptDialog(string title, string prompt, string initial)
        {
            InitializeComponent();
            Title = title;
            TitleText.Text = title;
            PromptText.Text = prompt;
            InputBox.Text = initial;
            Loaded += (_, _) => { InputBox.Focus(); InputBox.SelectAll(); };
        }

        /// <summary>The text, or null when cancelled or left empty.</summary>
        public static string? Ask(Window? owner, string title, string prompt, string initial = "")
        {
            var dlg = new TextPromptDialog(title, prompt, initial) { Owner = owner };
            return dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.Value) ? dlg.Value.Trim() : null;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(InputBox.Text)) { InputBox.Focus(); return; }
            DialogResult = true;
        }
    }
}
