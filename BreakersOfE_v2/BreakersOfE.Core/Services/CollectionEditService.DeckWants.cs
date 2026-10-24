using System;
using System.Collections.Generic;
using System.Linq;
using BreakersOfE.Data;
using BreakersOfE.Models;
using Microsoft.EntityFrameworkCore;

namespace BreakersOfE.Services
{
    /// <summary>One Want List row that's for a deck (Tear Down asks what to do with it).</summary>
    public sealed class DeckWant
    {
        public int EntryId { get; init; }
        public string Name { get; init; } = "";
        public string SetCode { get; init; } = "";
        public string CollectorNumber { get; init; } = "";
        public string Finish { get; init; } = "";
        public int Quantity { get; init; }
        public string ForDeckName { get; init; } = "";
        /// <summary>Ticked: kept on the Want List, for the Collection. Unticked: removed.</summary>
        public bool Keep { get; set; } = true;

        public string Printing => $"{SetCode.ToUpperInvariant()} #{CollectorNumber}";
        public string FinishText => CardFinish.Display(Finish);
    }

    public static partial class CollectionEditService
    {
        /// <summary>The Want List rows that are for this deck.</summary>
        public static List<DeckWant> WantsForDeck(string deckId)
        {
            if (string.IsNullOrEmpty(deckId)) return new();
            using var db = new CollectionDbContext();
            return ToDeckWants(db.WantListEntries.AsNoTracking().Where(e => e.ForDeckId == deckId).ToList());
        }

        /// <summary>
        /// The deck's wants for cards it no longer has (in its main deck or
        /// command zone) — after a version was restored. By card name, so a
        /// want for another printing of a card the deck still plays is kept.
        /// </summary>
        public static List<DeckWant> UnneededWants(Deck deck)
        {
            var names = new HashSet<string>(deck.Cards.Where(c => !c.IsTokenLine && c.Category != DeckCardCategory.Sideboard)
                                                      .Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
            return WantsForDeck(deck.DeckId).Where(w => !names.Contains(w.Name)).ToList();
        }

        /// <summary>Want List rows for decks that no longer exist (torn down before Tear Down asked about them).</summary>
        public static List<DeckWant> OrphanWants()
        {
            try
            {
                var decks = DecksById();
                using var db = new CollectionDbContext();
                return ToDeckWants(db.WantListEntries.AsNoTracking().Where(e => e.ForDeckId != "").ToList()
                                     .Where(e => !decks.ContainsKey(e.ForDeckId)).ToList());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Orphan wants: {ex.Message}");
                return new();
            }
        }

        private static List<DeckWant> ToDeckWants(List<WantListEntry> rows) =>
            rows.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.SetCode)
                .Select(e => new DeckWant
                {
                    EntryId = e.WantListEntryId, Name = e.Name, SetCode = e.SetCode, CollectorNumber = e.CollectorNumber,
                    Finish = e.Finish, Quantity = Math.Max(0, e.Quantity), ForDeckName = e.ForDeckName ?? "",
                }).ToList();

        /// <summary>
        /// A deck's wants, settled: the ticked ones stay on the Want List for the
        /// Collection (joining a Collection want for the same printing and finish),
        /// the rest are removed.
        /// </summary>
        public static EditResult SettleDeckWants(IReadOnlyList<DeckWant> wants)
        {
            var keep = wants.Where(w => w.Keep).Select(w => w.EntryId).ToList();
            var remove = wants.Where(w => !w.Keep).Select(w => w.EntryId).ToList();
            int moved = 0, removed = 0;
            var problems = new List<string>();
            if (keep.Count > 0)
            {
                var r = SetWantFor(keep, "", "");
                if (r.Changed > 0) moved = r.Changed; else if (r.Warning) problems.Add(r.Message);
            }
            if (remove.Count > 0)
            {
                try
                {
                    using var db = new CollectionDbContext();
                    var rows = db.WantListEntries.Where(e => remove.Contains(e.WantListEntryId)).ToList();
                    if (rows.Count > 0)
                    {
                        EnsureBackup();
                        db.WantListEntries.RemoveRange(rows);
                        db.SaveChanges();
                        removed = rows.Count;
                    }
                }
                catch (Exception ex)
                {
                    problems.Add($"Could not remove the wants: {ex.Message}");
                }
            }
            var parts = new List<string>();
            if (moved > 0) parts.Add($"{moved} want{(moved == 1 ? "" : "s")} kept for the Collection");
            if (removed > 0) parts.Add($"{removed} removed from the Want List");
            return new EditResult
            {
                Changed = moved + removed,
                Warning = problems.Count > 0,
                Message = string.Join(", ", parts) + (parts.Count > 0 ? "." : "") +
                          (problems.Count > 0 ? " " + string.Join("; ", problems) : ""),
            };
        }
    }
}
