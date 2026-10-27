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
    // Two-sided tokens on Edit → Pool → Collection (Tokens): adding a token asks
    // what's on its other side (both sides go in, paired), the Two-Sided…
    // button and right-click → Other Side… fix a pair later, and a question
    // comes before copies on a pair go. You decide what's paired: BoE never guesses
    // (pairings differ between boosters and decks).
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
        /// Add to Collection from the Other Side window: Qty copies of the token
        /// and of its other side (same finish, language and condition), paired.
        /// One step for Undo: both rows go back.
        /// </summary>
        private void AddBothSides(RowKey key, object token, TokenCard other, int qty)
        {
            var (nf, f, e) = CollectionEditService.FinishesOf(CollTag, other);
            // Both sides of a card share its finish; the other printing's own when it hasn't that one.
            string fin = CollectionEditService.FinishExists(key.Finish, nf, f, e) ? key.Finish
                       : nf ? CardFinish.NonFoil : f ? CardFinish.Foil : CardFinish.Etched;
            var otherKey = RowKey.Of(other.ScryfallId, fin, key.Language, key.Condition);
            string name = CardDetailText.Get(token, "Name");
            Run("Added both sides", new[] { key.ScryfallId, other.ScryfallId }, () =>
            {
                var r1 = CollectionEditService.Add(CollTag, token, key.Finish, qty, key.Language, key.Condition);
                if (r1.Changed <= 0) return new List<EditResult> { r1 };
                var r2 = CollectionEditService.Add(CollTag, other, fin, qty, key.Language, key.Condition);
                if (r2.Changed <= 0)
                    return new List<EditResult> { new() { Changed = r1.Changed, Warning = true, Touched = { key },
                        Message = r1.Message + "  " + r2.Message + "  Not paired." } };
                var row1 = CollectionEditService.TokenRow(key);
                var row2 = CollectionEditService.TokenRow(otherKey);
                var p = row1 != null && row2 != null
                    ? CollectionEditService.Pair(row1, row2, qty)
                    : new EditResult { Message = "The copies were added, but couldn't be paired.", Warning = true };
                string finNote = fin != key.Finish ? $" ({other.Name} has no {CardFinish.Display(key.Finish)}: added as {CardFinish.Display(fin)})" : "";
                return new List<EditResult>
                {
                    new()
                    {
                        Changed = r1.Changed + r2.Changed,
                        Warning = p.Changed <= 0 || finNote.Length > 0,
                        Touched = { key, otherKey },
                        Message = p.Changed > 0
                            ? $"Added {qty} × {name} // {other.Name}: two rows, one card{(qty == 1 ? "" : "s")}.{finNote}"
                            : $"Added {qty} × {name} and {qty} × {other.Name}, but not paired: {p.Message}",
                    },
                };
            });
            CollectionEditService.PairsChanged();
        }

        /// <summary>
        /// Right-click → Unpair: on for one selected row that's paired. One
        /// other side: "Unpair Treasure // Angel"; several: a list to pick from.
        /// </summary>
        private void UpdateUnpairItem()
        {
            if (_unpairItem == null) return;
            _unpairItem.Items.Clear();
            _unpairItem.Tag = null;
            _unpairItem.Header = "Unpair";
            _unpairItem.IsEnabled = false;
            if (!IsTokens) return;
            var rows = Targets(_bottom).Where(IsCollectionRow).ToList();
            if (rows.Count != 1 || rows[0] is not TokenCollectionEntry row) return;
            var sides = CollectionEditService.OtherSidesOf(CollectionEditService.TokenKey(row));
            if (sides.Count == 0) return;
            _unpairItem.IsEnabled = true;
            if (sides.Count == 1)
            {
                _unpairItem.Header = $"Unpair {row.Name} // {sides[0].Name}";
                _unpairItem.Tag = sides[0];
                return;
            }
            _unpairItem.Header = $"Unpair {row.Name} from";
            foreach (var s in sides)
            {
                var side = s;
                var mi = new MenuItem { Header = $"{side.Text} ×{side.Count}" };
                mi.Click += (_, e) => { e.Handled = true; DoUnpair(side); };
                _unpairItem.Items.Add(mi);
            }
        }

        /// <summary>Unpair the selected row from this other side (both rows stay, each its own card).</summary>
        private void DoUnpair(OtherSide side)
        {
            if (Targets(_bottom).FirstOrDefault(IsCollectionRow) is not TokenCollectionEntry row) return;
            if (!Confirm($"Unpair {row.Name} // {side.Name}?\n\nBoth rows stay in your collection, each its own card. " +
                         "Decks keep what they claimed.", "Unpair")) return;
            AfterPairing(CollectionEditService.Unpair(side.PairId));
        }

        /// <summary>Right-click → Export Two-Sided Pairs…: your pairs as a CSV file, to share with your group.</summary>
        private void DoExportPairs()
        {
            string text = CollectionEditService.ExportPairsCsv(out int count);
            if (count == 0) { ShowStatus("You have no two-sided pairs to export yet.", false); return; }
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
                ShowStatus($"Exported {count} two-sided pair{(count == 1 ? "" : "s")} to {System.IO.Path.GetFileName(dlg.FileName)}.", false);
            }
            catch (Exception ex)
            {
                ShowStatus($"Could not save the file: {ex.Message}", true);
            }
        }

        /// <summary>
        /// Right-click → Import Two-Sided Pairs…: a pairs file (yours or your
        /// group's) matched to your tokens by set and number; asks first, then
        /// pairs what you have both sides of. Your own pairs stay as they are.
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
                ShowStatus($"Nothing to pair: of the {pairs.Count} pairs in the file, you don't have both sides " +
                           "(not yet paired) of any.", false);
                return;
            }
            string list = string.Join("\n", yes.Take(12).Select(p => $"  {p.Text}  ×{p.CanPair}")) +
                          (yes.Count > 12 ? $"\n  … and {yes.Count - 12} more" : "");
            if (!Confirm($"Pair these {yes.Count} two-sided card{(yes.Count == 1 ? "" : "s")} from the file?\n\n{list}" +
                         (no.Count > 0 ? $"\n\n{no.Count} more in the file are skipped: you don't have both sides, or they're paired already." : "") +
                         "\n\nCheck them against your cards: pairings can differ between boosters and decks.",
                         "Import Two-Sided Pairs"))
                return;
            AfterPairing(CollectionEditService.PairImported(yes));
        }

        /// <summary>The Two-Sided… button (Tokens bar).</summary>
        private void BtnOtherSide_Click(object sender, RoutedEventArgs e)
        {
            _active = _bottom;
            DoOtherSide();
        }

        /// <summary>Two-Sided… / right-click → Other Side…: pair or unpair the selected token row.</summary>
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
            var dlg = new OtherSideWindow(Window.GetWindow(this), row, AddForPairing);
            dlg.ShowDialog();
            if (dlg.Changed)
            {
                // Copies may have come in from the pool: the pool table's Owned too.
                var poolRows = _top.FindAllLoaded(r => r is IOwnedCard).ToArray();
                if (poolRows.Length > 0)
                {
                    OwnedCountService.Fill(PoolTag, poolRows);
                    _top.RefreshGalleryCards(poolRows);
                }
                AfterPairing(new EditResult { Changed = 1, Message = "Two-sided tokens changed.", Touched = { CollectionEditService.TokenKey(row) } });
            }
        }

        /// <summary>
        /// The Two-Sided window adds the other side from the card pool through
        /// here, so Undo can take the copies back (the pair follows them).
        /// </summary>
        private EditResult AddForPairing(TokenCard card, string finish, int copies, string language, string condition)
        {
            var snap = CollectionEditService.Snapshot(CollTag, new[] { card.ScryfallId });
            var r = CollectionEditService.Add(CollTag, card, finish, copies, language, condition);
            if (r.Changed > 0 && snap != null)
            {
                CollectionEditService.MarkAfter(snap);
                PushUndo(snap, r.Message);
            }
            return r;
        }

        /// <summary>After pairs changed: status, the collection table and the Back side box.</summary>
        private void AfterPairing(EditResult r)
        {
            ShowStatus(r.Message, r.Warning);
            if (r.Changed <= 0) return;
            CollectionEditService.PairsChanged();
            var keys = new HashSet<RowKey>(r.Touched);
            _bottom.ReloadRows(x => keys.Contains(KeyOf(x)), x => false);
            RefreshCounts();
        }

        /// <summary>
        /// Before copies of token rows go (removed, moved, a lower quantity):
        /// if that takes copies that are on a pair, say which cards lose their
        /// other side, and ask. True to go ahead (always for other tables).
        /// </summary>
        private bool PairsOk(IEnumerable<(RowKey Key, int Removing)> changes, bool move = false)
        {
            if (!IsTokens) return true;
            var hit = new List<string>();
            var rows = CollectionEditService.TokenRows();
            foreach (var (key, removing) in changes)
            {
                var sides = CollectionEditService.OtherSidesOf(key);
                if (sides.Count == 0 || removing <= 0) continue;
                var row = rows.FirstOrDefault(r => CollectionEditService.TokenKey(r) == key);
                int off = row == null ? 0 : CollectionEditService.UnpairedCopies(row);
                if (removing <= off) continue;                       // single copies go first
                string name = row?.Name ?? "This token";
                hit.Add($"{name} // {string.Join(", ", sides.Select(s => s.Name))}");
            }
            if (hit.Count == 0) return true;
            return Confirm($"{string.Join("\n", hit.Distinct().Take(8))}{(hit.Count > 8 ? $"\n… and {hit.Count - 8} more" : "")}\n\n" +
                           (move
                               ? "Those copies are two-sided cards: the other side changes with them (one card), and they stay paired. "
                               : "Those copies are two-sided cards: the other side is removed with them (one card, both rows). ") +
                           "Copies in use by decks are kept. Undo brings both back, paired.",
                           "Two-Sided Tokens");
        }

        private static RowKey TokenKeyOf(object row) => CollectionEditService.TokenKey(row);

        /// <summary>A change of finish / language / condition (Change Finish …): the other side follows it.</summary>
        private (string? Finish, string? Language, string? Condition)? _moving;

        /// <summary>
        /// Run (Tokens): the edit took copies off a pair (removed, a lower Qty,
        /// moved). The other side is the same card, so the same number of its
        /// copies go too — or, for a move, move the same way and are paired
        /// again. Inside Run's step, so one Undo puts it all back.
        /// </summary>
        private List<EditResult> FollowPairs(List<CollectionEditService.PairLink> before, Dictionary<RowKey, int> qtyBefore)
        {
            var results = new List<EditResult>();
            CollectionEditService.PairsChanged();
            var after = CollectionEditService.PairLinks().ToDictionary(p => p.Id, p => p.Count);
            var qtyAfter = CollectionEditService.TokenQuantities();
            int Shrank(RowKey k) => qtyBefore.GetValueOrDefault(k) - qtyAfter.GetValueOrDefault(k);
            var rows = CollectionEditService.TokenRows();

            foreach (var p in before)
            {
                int lost = p.Count - after.GetValueOrDefault(p.Id);
                if (lost <= 0) continue;
                bool aWent = Shrank(p.A) > 0, bWent = Shrank(p.B) > 0;
                if (aWent == bWent) continue;                      // both sides were in the edit (or neither)
                var (gone, other, goneName, otherName) = aWent ? (p.A, p.B, p.AName, p.BName) : (p.B, p.A, p.BName, p.AName);
                var otherRow = rows.FirstOrDefault(r => CollectionEditService.TokenKey(r) == other);
                if (otherRow == null) continue;

                if (_moving is { } m)
                {
                    // Moved: the other side moves the same way, and the two are paired again.
                    var r = CollectionEditService.Move(CollTag, otherRow.TokenCollectionEntryId, lost, m.Finish, m.Language, m.Condition);
                    if (r.Changed <= 0) { results.Add(r); continue; }
                    var newGone = RowKey.Of(gone.ScryfallId, m.Finish ?? gone.Finish, m.Language ?? gone.Language, m.Condition ?? gone.Condition);
                    var newOther = RowKey.Of(other.ScryfallId, m.Finish ?? other.Finish, m.Language ?? other.Language, m.Condition ?? other.Condition);
                    var ra = CollectionEditService.TokenRow(newGone);
                    var rb = CollectionEditService.TokenRow(newOther);
                    var pr = ra != null && rb != null ? CollectionEditService.Pair(ra, rb, lost)
                                                      : new EditResult { Message = "couldn't pair them again", Warning = true };
                    results.Add(new EditResult
                    {
                        Changed = r.Changed,
                        Warning = pr.Changed <= 0,
                        Touched = { newOther },
                        Message = pr.Changed > 0
                            ? $"Other side moved too: {lost} × {otherName}, still paired with {goneName}."
                            : $"Other side moved too: {lost} × {otherName}, but {pr.Message}",
                    });
                }
                else
                {
                    var r = CollectionEditService.RemoveFromRow(CollTag, otherRow.TokenCollectionEntryId, lost, false);
                    results.Add(r.Changed > 0
                        ? new EditResult { Changed = r.Changed, Message = $"Other side removed too: {r.Changed} × {otherName} (same card as {goneName})." }
                        : r);
                }
            }
            return results;
        }
    }
}
