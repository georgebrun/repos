using System;
using System.Collections.Generic;
using System.Linq;
using BreakersOfE.Data;
using Microsoft.EntityFrameworkCore;

namespace BreakersOfE.Services
{
    /// <summary>
    /// Which cards have which keywords, from the card pool (Scryfall's own
    /// keyword list per card). Keywords belong to the card, not the printing,
    /// so everything is by card name — that way decks, collections and the
    /// pool all answer the same way. Built once (about a second), again after
    /// a database update.
    /// </summary>
    public static class KeywordIndex
    {
        private static readonly object _lock = new();
        private static Dictionary<string, HashSet<string>>? _byCard;      // card name → its keywords
        private static Dictionary<string, List<string>>? _byKeyword;      // keyword → card names (sorted)

        private static readonly HashSet<string> None = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Forget everything (after Update Database).</summary>
        public static void Reset()
        {
            lock (_lock) { _byCard = null; _byKeyword = null; Version++; }
        }

        /// <summary>Goes up with every Reset (lists built from the index know they're out of date).</summary>
        public static int Version { get; private set; }

        /// <summary>Build it now (call on a background thread to keep the screen free).</summary>
        public static void EnsureLoaded() => Snapshot();

        /// <summary>Both lookups, built if needed, taken together (a Reset can't pull them away mid-use).</summary>
        private static (Dictionary<string, HashSet<string>> ByCard, Dictionary<string, List<string>> ByKeyword) Snapshot()
        {
            lock (_lock)
            {
                if (_byCard != null && _byKeyword != null) return (_byCard, _byKeyword);
                var byCard = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                var byKeyword = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    using var db = new AppDbContext();
                    foreach (var c in db.PoolCards.AsNoTracking()
                                 .Where(c => c.Keywords != null && c.Keywords != "")
                                 .Select(c => new { c.Name, c.Keywords })
                                 .ToList())
                    {
                        if (string.IsNullOrEmpty(c.Name)) continue;
                        if (!byCard.TryGetValue(c.Name, out var set))
                            byCard[c.Name] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var kw in c.Keywords.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            if (!set.Add(kw)) continue;
                            if (!byKeyword.TryGetValue(kw, out var names))
                                byKeyword[kw] = names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                            names.Add(c.Name);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Keyword index: {ex.Message}");
                }
                _byCard = byCard;
                _byKeyword = byKeyword.ToDictionary(kv => kv.Key, kv => kv.Value.ToList(), StringComparer.OrdinalIgnoreCase);
                return (_byCard, _byKeyword);
            }
        }

        /// <summary>A card's keywords (by name; empty when it has none or isn't in the pool).</summary>
        public static IReadOnlySet<string> For(string cardName)
        {
            return Snapshot().ByCard.TryGetValue(cardName ?? "", out var set) ? set : None;
        }

        /// <summary>True when the pool knows this card name.</summary>
        public static bool Knows(string cardName)
        {
            return Snapshot().ByCard.ContainsKey(cardName ?? "");
        }

        /// <summary>The cards (names, A→Z) with a keyword.</summary>
        public static IReadOnlyList<string> CardsWith(string keyword)
        {
            return Snapshot().ByKeyword.TryGetValue(keyword ?? "", out var names) ? names : Array.Empty<string>();
        }

        /// <summary>Every keyword some card has, A→Z.</summary>
        public static IReadOnlyList<string> AllKeywords()
        {
            return Snapshot().ByKeyword.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
