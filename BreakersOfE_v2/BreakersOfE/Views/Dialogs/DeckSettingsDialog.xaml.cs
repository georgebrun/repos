using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BreakersOfE.Models;
using Wpf.Ui.Controls;

namespace BreakersOfE.Views.Dialogs
{
    /// <summary>
    /// Edit → Decks: name, deck type (DeckFormats) and, for Constructed, the
    /// format. "New Deck…" and "Deck Settings…" both use it.
    /// </summary>
    public partial class DeckSettingsDialog : FluentWindow
    {
        public string DeckName { get; private set; } = "";
        public DeckType DeckType { get; private set; } = DeckType.Standard;
        public string ConstructedFormat { get; private set; } = "standard";
        public string Description { get; private set; } = "";

        private DeckSettingsDialog(Deck? deck)
        {
            InitializeComponent();
            TypeBox.ItemsSource = DeckFormats.All;
            TypeBox.DisplayMemberPath = nameof(DeckFormatRule.Name);
            FormatBox.ItemsSource = DeckFormats.ConstructedFormats;
            FormatBox.DisplayMemberPath = nameof(Models.ConstructedFormat.Name);

            if (deck == null)
            {
                HeaderText.Text = "New Deck";
                BtnOk.Content = "Create";
                NameBox.Text = "New Deck";
                TypeBox.SelectedItem = DeckFormats.For(DeckType.Commander);
                FormatBox.SelectedItem = DeckFormats.Constructed("standard");
            }
            else
            {
                HeaderText.Text = "Deck Settings";
                BtnOk.Content = "Save";
                NameBox.Text = deck.Name;
                DescriptionBox.Text = deck.Description;
                TypeBox.SelectedItem = DeckFormats.For(deck.DeckType);
                FormatBox.SelectedItem = DeckFormats.Constructed(deck.ConstructedFormat);
            }
            ShowRules();
            Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
        }

        /// <summary>New deck: returns the dialog (read its values), or null when cancelled.</summary>
        public static DeckSettingsDialog? AskNew(Window? owner)
        {
            var dlg = new DeckSettingsDialog(null) { Owner = owner };
            return dlg.ShowDialog() == true ? dlg : null;
        }

        /// <summary>An open deck's settings: returns the dialog, or null when cancelled.</summary>
        public static DeckSettingsDialog? AskEdit(Window? owner, Deck deck)
        {
            var dlg = new DeckSettingsDialog(deck) { Owner = owner };
            return dlg.ShowDialog() == true ? dlg : null;
        }

        private void TypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowRules();

        private void ShowRules()
        {
            var rule = TypeBox.SelectedItem as DeckFormatRule ?? DeckFormats.For(DeckType.Standard);
            bool constructed = rule.Type == DeckType.Standard;
            FormatLabel.Visibility = FormatBox.Visibility = constructed ? Visibility.Visible : Visibility.Collapsed;
            RulesText.Text = rule.Summary;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            string name = NameBox.Text.Trim();
            if (name.Length == 0)
            {
                ErrorText.Text = "Give the deck a name.";
                ErrorText.Visibility = Visibility.Visible;
                NameBox.Focus();
                return;
            }
            var rule = TypeBox.SelectedItem as DeckFormatRule ?? DeckFormats.For(DeckType.Standard);
            DeckName = name;
            DeckType = rule.Type;
            ConstructedFormat = (FormatBox.SelectedItem as Models.ConstructedFormat)?.Key ?? "standard";
            Description = DescriptionBox.Text.Trim();
            DialogResult = true;
        }
    }
}
