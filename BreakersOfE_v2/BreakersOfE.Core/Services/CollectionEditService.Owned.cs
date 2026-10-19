using System;
using System.Collections.Generic;
using System.Linq;
using BreakersOfE.Data;
using Microsoft.EntityFrameworkCore;

namespace BreakersOfE.Services
{
    /// <summary>How many copies of a card (any printing) you own, and how many no deck or the Trade Binder has claimed.</summary>
    public readonly record struct OwnedCount(int Owned, int Free);

    public static partial class CollectionEditService
    {
        /// <summary>
        /// Copies owned and free (unclaimed) per card name, any printing, from
        /// Collection → Cards. Names match by front face, so "Delver of
        /// Secrets" finds "Delver of Secrets // Insectile Aberration". Names
        /// you don't own are left out. Never throws.
        /// </summary>
        public static Dictionary<string, OwnedCount> OwnedByName(IEnumerable<string> names)
        {
            var result = new Dictionary<string, OwnedCount>(StringComparer.OrdinalIgnoreCase);
            var wanted = new HashSet<string>(names.Where(n => !string.IsNullOrWhiteSpace(n))
                                                  .Select(DeckDataService.FrontName),
                                             StringComparer.OrdinalIgnoreCase);
            if (wanted.Count == 0) return result;
            try
            {
                using var db = new CollectionDbContext();
                var rows = db.CollectionEntries.AsNoTracking()
                    .Select(e => new { e.Name, e.Quantity, e.UsedCount })
                    .ToList();
                foreach (var e in rows)
                {
                    string front = DeckDataService.FrontName(e.Name ?? "");
                    if (!wanted.Contains(front)) continue;
                    var had = result.GetValueOrDefault(front);
                    int free = Math.Max(0, e.Quantity - Math.Max(0, e.UsedCount));
                    result[front] = new OwnedCount(had.Owned + Math.Max(0, e.Quantity), had.Free + free);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Owned by name: {ex.Message}");
            }
            return result;
        }
    }
}
