using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BreakersOfE.Models;
using BreakersOfE.Services;
using BreakersOfE.Views.Dialogs;
using static BreakersOfE.Views.Pages.EditPageKit;

namespace BreakersOfE.Views.Pages
{
    // Two-sided tokens on Edit → Pool → Collection (Tokens): a two-sided card
    // ("Officer // Treasure") is a row of its own. Adding a token asks what's on
    // its other side (the card is added, one row); the Two-Sided… button and
    // right-click → Other Side… make cards from single tokens you have, and
    // Unpair takes a card apart again. You decide what's paired: BoE never
    // guesses (pairings differ between boosters and decks).
    // Part of EditCollectionPage (same class).
    public partial class EditCollectionPage
    {
        private MenuItem? _otherSideItem, _unpairItem, _exportPairsItem, _importPairsItem;
        private bool IsTokens => CollTag == CollectionEditService.TokensTable && !_setsMode;

        /// <summary>Tokens only: the Two-Sided… button and the Other Side… menu item.</summary>
        private void ShowPairControls()
        {
            var v = IsTokens ? Visibility.Visible : Visibility.Collapsed;
            BtnOtherSide.Visibility = v;
            if (_otherSideItem != null) _otherSideItem.Visibility = v;
            if (_unpairItem != null) _unpairItem.Visibility = v;
            if (_exportPairsItem != null) _exportPairsItem.Visibility = v;
            if (_importPairsItem != null) _importPairsItem.Visibility = v;
        }

        /// <summary>
        /// Add to Collection from the Other Side window: Qty two-sided cards of
        /// the token and its other side (one row, in the token's language and
        /// condition; the finish both sides have). One step for Undo.
        /// </summary>
        private void AddTwoSided(RowKey key, object token, TokenCard other, int qty)
        {
            if (token is not TokenCard first)
            {
                ShowStatus("Two-sided cards are added from the Tokens pool.", true);
                return;
            }
            var (nf, f, e) = CollectionEditService.PairFinishes(first, other);
            // Both sides of a card share its finish: the one asked for when both have it, else one they both have.
            string fin = CollectionEditService.FinishExists(key.Finish, nf, f, e) ? key.Finish
                       : nf ? CardFinish.NonFoil : f ? CardFinish.Foil : e ? CardFinish.Etched : "";
            if (fin.Length == 0)
            {
                ShowStatus($"{first.Name} and {other.Name} share no finish (one is foil-only, the other non-foil-only): add them as single tokens.", true);
                return;
            }
            var card = TokenPairing.Compose(first, other);
            string finNote = fin != key.Finish ? $" (not in {CardFinish.Display(key.Finish)}: added as {CardFinish.Display(fin)})" : "";
            Run("Added two-sided", new[] { card.ScryfallId }, () =>
            {
                var r = CollectionEditService.AddPair(first, other, fin, qty, key.Language, key.Condition);
                return new List<EditResult>
                {
                    r.Changed > 0
                        ? new EditResult { Changed = r.Changed, Warning = finNote.Length > 0, Touched = r.Touched,
                                           Message = $"Added {qty} × {card.Name}: one two-sided card{(qty == 1 ? "" : "s")}.{finNote}" }
                        : r,
                };
            });
        }

        /// <summary>Right-click → Unpair: on for one selected two-sided card.</summary>
        private void UpdateUnpairItem()
        {
            if (_unpairItem == null) return;
            _unpairItem.Header = "Unpair";
            _unpairItem.IsEnabled = false;
            if (!IsTokens) return;
            var rows = Targets(_bottom).Where(IsCollectionRow).ToList();
            if (rows.Count != 1 || rows[0] is not TokenCollectionEntry row || !CollectionEditService.IsPairRow(row)) return;
            _unpairItem.Header = $"Unpair {row.Name}";
            _unpairItem.IsEnabled = CollectionEditService.FreeCopies(row) > 0;
        }

        /// <summary>
        /// Unpair the selected two-sided card (Qty copies, free ones only): each
        /// side goes back to a single row of its own. One step for Undo.
        /// </summary>
        private void DoUnpair()
        {
            if (Targets(_bottom).FirstOrDefault(IsCollectionRow) is not TokenCollectionEntry row || !CollectionEditService.IsPairRow(row))
            {
                ShowStatus("Select a two-sided card (\"Officer // Treasure\") in the collection table first.", true);
                return;
            }
            int free = CollectionEditService.FreeCopies(row);
            if (free <= 0) { ShowStatus($"Every copy of {row.Name} is in a deck: free one first, then unpair it.", true); return; }
            int n = Math.Min(Qty, free);
            var (a, b) = TokenPairing.SideNames(row.Name);
            if (!Confirm($"Unpair {n} × {row.Name}?\n\nEach side goes back to a row of its own: {n} × {a} and {n} × {b}." +
                         (row.UsedCount > 0 ? $"\n\nCopies in decks ({Math.Min(row.UsedCount, row.Quantity)}) stay two-sided." : ""), "Unpair"))
                return;
            Run($"Unpaired {row.Name}", CollectionEditService.UnpairPrintings(row.ScryfallId), () =>
                new List<EditResult> { CollectionEditService.Unpair(row, n) });
        }

        /// <summary>Right-click → Export Two-Sided Pairs…: your two-sided cards as a CSV file, to share with your group.</summary>
        private void DoExportPairs()
        {
            string text = CollectionEditService.ExportPairsCsv(out int count);
            if (count == 0) { ShowStatus("You have no two-sided cards to export yet.", false); return; }
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Two-Sided Pairs",
                FileName = "BoE two-sided tokens.csv",
                Filter = "CSV file (*.csv)|*.csv",
                DefaultExt = ".csv",
            };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            try
            {
                System.IO.File.WriteAllText(dlg.FileName, text, new System.Text.UTF8Encoding(true));
                ShowStatus($"Exported {count} two-sided card{(count == 1 ? "" : "s")} to {System.IO.Path.GetFileName(dlg.FileName)}.", false);
            }
            catch (Exception ex)
            {
                ShowStatus($"Could not save the file: {ex.Message}", true);
            }
        }

        /// <summary>
        /// Right-click → Import Two-Sided Pairs…: a pairs file (yours or your
        /// group's) matched to your single tokens by set and number; asks first,
        /// then makes two-sided cards of what you have both sides of. One step for Undo.
        /// </summary>
        private void DoImportPairs()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import Two-Sided Pairs",
                Filter = "CSV file (*.csv)|*.csv|All files (*.*)|*.*",
            };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            List<CollectionEditService.ImportedPair>? pairs;
            try { pairs = CollectionEditService.ReadPairsCsv(System.IO.File.ReadAllText(dlg.FileName)); }
            catch (Exception ex) { ShowStatus($"Could not read the file: {ex.Message}", true); return; }
            if (pairs == null) { ShowStatus("That isn't a two-sided pairs file (exported from BoE).", true); return; }

            var yes = pairs.Where(p => p.CanPair > 0).ToList();
            var no = pairs.Where(p => p.CanPair <= 0).ToList();
            if (yes.Count == 0)
            {
                ShowStatus($"Nothing to pair: of the {pairs.Count} pairs in the file, you don't have free single copies of both sides of any.", false);
                return;
            }
            string list = string.Join("\n", yes.Take(12).Select(p => $"  {p.Text}  ×{p.CanPair}")) +
                          (yes.Count > 12 ? $"\n  … and {yes.Count - 12} more" : "");
            if (!Confirm($"Make these {yes.Count} two-sided card{(yes.Count == 1 ? "" : "s")} from the file?\n\n{list}" +
                         (no.Count > 0 ? $"\n\n{no.Count} more in the file are skipped: you don't have free single copies of both sides." : "") +
                         "\n\nCheck them against your cards: pairings can differ between boosters and decks.",
                         "Import Two-Sided Pairs"))
                return;
            Run("Imported two-sided pairs", CollectionEditService.ImportPrintings(yes), () =>
                new List<EditResult> { CollectionEditService.PairImported(yes) });
        }

        /// <summary>The Two-Sided… button (Tokens bar).</summary>
        private void BtnOtherSide_Click(object sender, RoutedEventArgs e)
        {
            _active = _bottom;
            DoOtherSide();
        }

        /// <summary>Two-Sided… / right-click → Other Side…: make two-sided cards of the selected single token.</summary>
        private void DoOtherSide()
        {
            if (Targets(_bottom).FirstOrDefault(IsCollectionRow) is not TokenCollectionEntry row)
            {
                ShowStatus("Select one of your tokens in the collection table (below) first, then Two-Sided… to pair it with the token on its back.", true);
                return;
            }
            if (CollectionEditService.IsRealDoubleFaced(row))
            {
                ShowStatus($"{row.Name} is one double-faced card in Scryfall already: it needs no other side.", false);
                return;
            }
            if (CollectionEditService.IsPairRow(row))
            {
                ShowStatus($"{row.Name} is a two-sided card already. Right-click → Unpair takes it apart.", false);
                return;
            }
            var dlg = new OtherSideWindow(Window.GetWindow(this), row, PairInRun);
            dlg.ShowDialog();
        }

        /// <summary>The Two-Sided window pairs through here: one Undo step (the token, the other side, and the card).</summary>
        private EditResult PairInRun(TokenCollectionEntry row, TokenCard other, int count)
        {
            EditResult? result = null;
            Run("Paired", CollectionEditService.PairPrintings(row.ScryfallId, other.ScryfallId, row.Name, row.CollectorNumber, other.Name, other.CollectorNumber), () =>
            {
                result = CollectionEditService.PairWithPrinting(row, other, count);
                return new List<EditResult> { result };
            });
            return result ?? new EditResult { Message = "Nothing paired.", Warning = true };
        }
    }
}
