using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BreakersOfE.Models;

namespace BreakersOfE.Services
{
    /// <summary>One deck that uses a card, and how many copies.</summary>
    public sealed class DeckUse
    {
        public string DeckName { get; init; } = "";
        public string FilePath { get; init; } = "";
        public int Copies { get; init; }
        public string Text => $"{DeckName}  ×{Copies}";
    }

    /// <summary>
    /// Cards shared between decks (read-only): which of your decks use a card.
    /// Reads every .deck file under Documents\BoE_V2\Decks (subfolders too).
    /// A card counts by NAME — any printing — and a double-faced card by its
    /// front face. Files are cached and re-read only when they change.
    /// </summary>
    public static class DeckIndexService
    {
        private sealed class Entry
        {
            public DateTime Stamp;
            public string Name = "";
            /// <summary>The deck as read from its file (shared with the deck browser).</summary>
            public Deck Deck = new();
            /// <summary>Card key → copies in this deck (all categories).</summary>
            public Dictionary<string, int> Copies = new(StringComparer.OrdinalIgnoreCase);
        }

        private static readonly Dictionary<string, Entry> _cache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new();

        /// <summary>
        /// Every readable deck file under the Decks folder, as (file path, deck).
        /// One cached read shared by the deck browser and Other Decks.
        /// </summary>
        public static List<(string Path, Deck Deck)> AllDecks()
        {
            lock (_lock)
                return Refresh().Select(kv => (kv.Key, kv.Value.Deck)).ToList();
        }

        /// <summary>
        /// Card keys (names, front face) used by one deck file, or by every deck
        /// when <paramref name="deckPath"/> is null. For "in a deck" filters.
        /// </summary>
        public static HashSet<string> CardKeys(string? deckPath = null)
        {
            lock (_lock)
            {
                var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (path, entry) in Refresh())
                {
                    if (deckPath != null && !string.Equals(path, deckPath, StringComparison.OrdinalIgnoreCase))
                        continue;
                    keys.UnionWith(entry.Copies.Keys);
                }
                return keys;
            }
        }

        /// <summary>How many deck files were read (after a refresh).</summary>
        public static int DeckCount
        {
            get { lock (_lock) return Refresh().Count; }
        }

        /// <summary>"Delver of Secrets // Insectile Aberration" → "Delver of Secrets".</summary>
        public static string CardKey(string? name)
        {
            string n = (name ?? "").Trim();
            int i = n.IndexOf(" // ", StringComparison.Ordinal);
            return i > 0 ? n[..i].Trim() : n;
        }

        /// <summary>
        /// Every deck that uses this card (by name), most copies first, then A→Z.
        /// <paramref name="excludePath"/> leaves one deck out (the deck on screen).
        /// </summary>
        public static List<DeckUse> DecksUsing(string cardName, string? excludePath = null)
        {
            string key = CardKey(cardName);
            if (key.Length == 0) return new List<DeckUse>();

            return Refresh()
                .Where(kv => excludePath == null ||
                             !string.Equals(kv.Key, excludePath, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Value.Copies.TryGetValue(key, out int n) && n > 0
                    ? new DeckUse { DeckName = kv.Value.Name, FilePath = kv.Key, Copies = n }
                    : null)
                .Where(u => u != null)
                .Select(u => u!)
                .OrderByDescending(u => u.Copies)
                .ThenBy(u => u.DeckName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Fill "Other Decks" on every card of an open deck: how many of your
        /// OTHER decks use each card, and a tooltip listing them.
        /// </summary>
        public static void Annotate(Deck deck, string deckPath)
        {
            var decks = Refresh()
                .Where(kv => !string.Equals(kv.Key, deckPath, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Value)
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var card in deck.Cards)
            {
                string key = CardKey(card.Name);
                var uses = decks
                    .Where(e => e.Copies.TryGetValue(key, out int n) && n > 0)
                    .Select(e => $"{e.Name}  ×{e.Copies[key]}")
                    .ToList();
                card.OtherDecksCount = uses.Count;
                card.OtherDecksTip = uses.Count == 0
                    ? "Not in any of your other decks"
                    : "Also in:\n" + string.Join("\n", uses);
            }
        }

        /// <summary>Scan the deck folder; re-read only new or changed files.</summary>
        private static Dictionary<string, Entry> Refresh()
        {
            lock (_lock)
            {
                string root = AppFolderService.DecksFolder;
                List<string> files;
                try { files = Directory.EnumerateFiles(root, "*.deck", SearchOption.AllDirectories).ToList(); }
                catch { files = new List<string>(); }

                var present = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
                foreach (var gone in _cache.Keys.Where(k => !present.Contains(k)).ToList())
                    _cache.Remove(gone);

                foreach (var path in files)
                {
                    DateTime stamp;
                    try { stamp = File.GetLastWriteTimeUtc(path); }
                    catch { continue; }
                    if (_cache.TryGetValue(path, out var cached) && cached.Stamp == stamp) continue;

                    try
                    {
                        var deck = JsonSerializer.Deserialize<Deck>(File.ReadAllText(path));
                        if (deck == null) { _cache.Remove(path); continue; }

                        var entry = new Entry
                        {
                            Stamp = stamp,
                            Deck = deck,
                            Name = string.IsNullOrWhiteSpace(deck.Name)
                                ? Path.GetFileNameWithoutExtension(path) : deck.Name,
                        };
                        foreach (var c in deck.Cards)
                        {
                            if (c.IsTokenLine) continue;          // tokens aren't part of the deck
                            string key = CardKey(c.Name);
                            if (key.Length == 0) continue;
                            entry.Copies[key] = entry.Copies.GetValueOrDefault(key) + c.TotalQuantity;
                        }
                        _cache[path] = entry;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Deck index ({path}): {ex.Message}");
                        _cache.Remove(path);
                    }
                }

                return new Dictionary<string, Entry>(_cache, StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
