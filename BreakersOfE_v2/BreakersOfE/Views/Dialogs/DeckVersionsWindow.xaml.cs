using System;
using System.Linq;
using System.Windows;
using BreakersOfE.Models;
using BreakersOfE.Services;
using Wpf.Ui.Controls;

namespace BreakersOfE.Views.Dialogs
{
    /// <summary>
    /// Deck versions (DeckVersionService): the list, Save Version (with a
    /// note), Compare (one with the deck now, or two with each other), Restore
    /// and Delete. <see cref="Restored"/> is set when a version was restored,
    /// so the Edit Decks page reopens the deck.
    /// </summary>
    public partial class DeckVersionsWindow : FluentWindow
    {
        private readonly Deck _deck;

        /// <summary>The restore's message, when a version was restored (null otherwise).</summary>
        public string? Restored { get; private set; }
        public bool RestoreWarning { get; private set; }

        public DeckVersionsWindow(Deck deck, Window? owner)
        {
            InitializeComponent();
            if (owner != null) Owner = owner;
            _deck = deck;
            WindowTitleBar.Title = $"Deck Versions — {deck.Name}";
            HeaderText.Text = deck.Name;
            KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.F1) HelpWindow.Open("decks"); };
            Reload();
        }

        private void Reload()
        {
            var list = DeckVersionService.List(_deck);
            VersionGrid.ItemsSource = list;
            if (list.Count == 0) CompareText.Text = "No versions yet. Save Version saves the deck as it is now.";
            UpdateButtons();
        }

        private DeckVersion[] Selected => VersionGrid.SelectedItems.OfType<DeckVersion>().ToArray();

        private void UpdateButtons()
        {
            int n = Selected.Length;
            BtnCompare.IsEnabled = n is 1 or 2;
            BtnRestore.IsEnabled = BtnDelete.IsEnabled = n == 1;
        }

        private void VersionGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateButtons();
            if (Selected.Length is 1 or 2) Compare();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var note = TextPromptDialog.Ask(this, "Save Version", "A short note for this version (what's different, or why you saved it).",
                                            $"Saved {DateTime.Now:g}");
            if (note == null) return;
            var v = DeckVersionService.SaveVersion(_deck, note);
            StatusText.Text = v == null ? "Couldn't save the version." : $"Saved: {v.Note}";
            Reload();
        }

        private void Compare_Click(object sender, RoutedEventArgs e) => Compare();

        /// <summary>One version: from it to the deck now. Two: from the older to the newer.</summary>
        private void Compare()
        {
            var sel = Selected.OrderBy(v => v.Saved).ToArray();
            try
            {
                if (sel.Length == 1)
                {
                    var old = DeckVersionService.Open(sel[0]);
                    if (old == null) { CompareText.Text = "Couldn't read that version."; return; }
                    CompareText.Text = DeckVersionService.Compare(old, Label(sel[0]), _deck, "the deck now");
                }
                else if (sel.Length == 2)
                {
                    var a = DeckVersionService.Open(sel[0]);
                    var b = DeckVersionService.Open(sel[1]);
                    if (a == null || b == null) { CompareText.Text = "Couldn't read one of the versions."; return; }
                    CompareText.Text = DeckVersionService.Compare(a, Label(sel[0]), b, Label(sel[1]));
                }
            }
            catch (Exception ex)
            {
                CompareText.Text = $"Couldn't compare: {ex.Message}";
            }
        }

        private static string Label(DeckVersion v) => v.Note.Length > 0 ? $"{v.SavedText} — {v.Note}" : v.SavedText;

        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            if (Selected is not [var v]) return;
            if (!Pages.EditPageKit.Confirm(this,
                $"Restore the version of {Label(v)}?\n\nThe deck as it is now is saved as a version first, so you can go back to it. " +
                "Its claimed copies are freed, the version's cards are claimed from your free copies, and what's still missing goes on the Want List for this deck.",
                "Restore Version")) return;
            var (deck, msg, warn) = DeckVersionService.Restore(_deck, v);
            if (deck == null) { StatusText.Text = msg; return; }
            // Wants for cards the restored deck doesn't have: keep (for the Collection) or remove.
            var stale = CollectionEditService.UnneededWants(deck);
            if (stale.Count > 0 &&
                DeckWantsDialog.Ask(this, $"{stale.Sum(w => w.Quantity)} card(s) on the Want List for this deck aren't in the restored version",
                    "Ticked cards stay on the Want List, for the Collection. Unticked cards are removed. " +
                    "Cancel leaves them on the Want List for this deck.", "Apply", stale))
            {
                var settled = CollectionEditService.SettleDeckWants(stale);
                if (settled.Message.Length > 0) msg += "  Want List: " + settled.Message;
                warn |= settled.Warning;
            }
            Restored = msg;
            RestoreWarning = warn;
            DialogResult = true;
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (Selected is not [var v]) return;
            if (!Pages.EditPageKit.Confirm(this, $"Delete the version of {Label(v)}?", "Delete Version")) return;
            DeckVersionService.Delete(_deck, v);
            StatusText.Text = "Deleted.";
            CompareText.Text = "";
            Reload();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
