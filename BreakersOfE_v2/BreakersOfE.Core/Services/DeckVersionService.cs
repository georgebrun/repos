using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using BreakersOfE.Models;

namespace BreakersOfE.Services
{
    /// <summary>One saved version of a deck.</summary>
    public sealed class DeckVersion
    {
        /// <summary>The version's deck file (a copy of the deck as it was).</summary>
        public string Path { get; init; } = "";
        public DateTime Saved { get; init; }
        public string Note { get; init; } = "";
        /// <summary>Cards in the deck then (sideboard and tokens not counted).</summary>
        public int Cards { get; init; }

        public string SavedText => Saved.ToString("g");
        public string CardsText => Cards.ToString();
    }

    /// <summary>
    /// Deck versions: saved copies of a deck with a note, to compare with each
    /// other or with the deck now, and to restore. Kept in "Deck Versions\&lt;deck
    /// id&gt;" in the data folder (by the deck's id, so renaming the deck keeps
    /// them). Saved by hand (Versions… → Save Version), when the deck builder
    /// creates a deck, and before a restore (so a restore can be undone).
    /// </summary>
    public static class DeckVersionService
    {
        private const string IndexFile = "versions.json";

        private sealed class Entry
        {
            public string File { get; set; } = "";
            public DateTime Saved { get; set; }
            public string Note { get; set; } = "";
            public int Cards { get; set; }
        }

        private static string FolderOf(Deck deck) =>
            System.IO.Path.Combine(AppFolderService.RootFolder, "Deck Versions", deck.DeckId);

        private static List<Entry> ReadIndex(string folder)
        {
            string path = System.IO.Path.Combine(folder, IndexFile);
            if (!File.Exists(path)) return new();
            try { return JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(path)) ?? new(); }
            catch { return new(); }
        }

        private static void WriteIndex(string folder, List<Entry> entries) =>
            SafeFile.WriteAllText(System.IO.Path.Combine(folder, IndexFile),
                                  JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));

        private static int MainCount(Deck deck) =>
            deck.Cards.Where(c => !c.IsTokenLine && c.Category != DeckCardCategory.Sideboard).Sum(c => c.TotalQuantity);

        /// <summary>The deck's versions, newest first.</summary>
        public static List<DeckVersion> List(Deck deck)
        {
            if (string.IsNullOrWhiteSpace(deck.DeckId)) return new();
            string folder = FolderOf(deck);
            return ReadIndex(folder)
                .Where(e => File.Exists(System.IO.Path.Combine(folder, e.File)))
                .OrderByDescending(e => e.Saved)
                .Select(e => new DeckVersion { Path = System.IO.Path.Combine(folder, e.File), Saved = e.Saved, Note = e.Note, Cards = e.Cards })
                .ToList();
        }

        /// <summary>Save the deck as it is now, with a note. Never fails loudly: returns null when it couldn't.</summary>
        public static DeckVersion? SaveVersion(Deck deck, string note)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(deck.DeckId)) DeckService.Save(deck);   // gives it its id
                string folder = FolderOf(deck);
                Directory.CreateDirectory(folder);
                var now = DateTime.Now;
                string file = $"{now:yyyyMMdd-HHmmss-fff}.deck";
                SafeFile.WriteAllText(System.IO.Path.Combine(folder, file), DeckService.ToJson(deck));
                var entries = ReadIndex(folder);
                var entry = new Entry { File = file, Saved = now, Note = (note ?? "").Trim(), Cards = MainCount(deck) };
                entries.Add(entry);
                WriteIndex(folder, entries);
                return new DeckVersion { Path = System.IO.Path.Combine(folder, file), Saved = now, Note = entry.Note, Cards = entry.Cards };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Save version: {ex.Message}");
                return null;
            }
        }

        /// <summary>Delete one version.</summary>
        public static void Delete(Deck deck, DeckVersion v)
        {
            string folder = FolderOf(deck);
            var entries = ReadIndex(folder);
            entries.RemoveAll(e => string.Equals(System.IO.Path.Combine(folder, e.File), v.Path, StringComparison.OrdinalIgnoreCase));
            WriteIndex(folder, entries);
            try { File.Delete(v.Path); } catch { /* the index no longer lists it */ }
        }

        /// <summary>A version's deck (not for saving as is: its file is the version's).</summary>
        public static Deck? Open(DeckVersion v) => DeckService.Load(v.Path);

        /// <summary>
        /// What changed from <paramref name="from"/> to <paramref name="to"/>:
        /// cards added and removed, per part of the deck (main, commander,
        /// sideboard, tokens), any printing of a card counted together.
        /// </summary>
        public static string Compare(Deck from, string fromLabel, Deck to, string toLabel)
        {
            static string Part(DeckCard c) =>
                c.IsTokenLine ? "Tokens" : DeckEditService.SectionOf(c) switch
                {
                    DeckCardCategory.Commander => "Commander",
                    DeckCardCategory.Sideboard => "Sideboard",
                    _ => "Main",
                };
            static Dictionary<(string Part, string Name), int> Counts(Deck d) =>
                d.Cards.GroupBy(c => (Part(c), c.Name)).ToDictionary(g => g.Key, g => g.Sum(c => c.TotalQuantity));

            var a = Counts(from);
            var b = Counts(to);
            var sb = new StringBuilder();
            sb.AppendLine($"From: {fromLabel}  ({MainCount(from)} cards)");
            sb.AppendLine($"To:   {toLabel}  ({MainCount(to)} cards)");
            sb.AppendLine();
            int changes = 0;
            foreach (var part in new[] { "Commander", "Main", "Sideboard", "Tokens" })
            {
                var keys = a.Keys.Concat(b.Keys).Where(k => k.Part == part).Distinct()
                            .OrderBy(k => k.Name, StringComparer.OrdinalIgnoreCase).ToList();
                var added = new List<string>();
                var removed = new List<string>();
                foreach (var k in keys)
                {
                    int diff = b.GetValueOrDefault(k) - a.GetValueOrDefault(k);
                    if (diff > 0) added.Add($"  + {diff} {k.Name}");
                    else if (diff < 0) removed.Add($"  − {-diff} {k.Name}");
                }
                if (added.Count + removed.Count == 0) continue;
                changes += added.Count + removed.Count;
                sb.AppendLine($"{part}: {added.Count} added, {removed.Count} removed");
                foreach (var l in added) sb.AppendLine(l);
                foreach (var l in removed) sb.AppendLine(l);
                sb.AppendLine();
            }
            if (changes == 0) sb.AppendLine("No differences.");
            return sb.ToString();
        }

        /// <summary>
        /// Restore a version: the deck now is saved as a version first ("Before
        /// restoring …"), its claimed copies are freed, the version becomes the
        /// deck (same file, name and id), and your free copies are claimed again
        /// — what it still needs goes on the Want List for it.
        /// </summary>
        public static (Deck? Deck, string Message, bool Warning) Restore(Deck current, DeckVersion v)
        {
            var old = Open(v);
            if (old == null) return (null, "Couldn't read that version.", true);
            if (SaveVersion(current, $"Before restoring the version of {v.SavedText}") == null)
                return (null, "Couldn't save the deck as it is now, so nothing was restored.", true);
            int freed = CollectionEditService.ReleaseAll(current.DeckId);
            if (freed < 0) return (null, "Couldn't free the deck's claimed copies, so nothing was restored.", true);

            old.DeckId = current.DeckId;
            old.Name = current.Name;
            old.FilePath = current.FilePath;
            DeckService.Save(old);
            var (claimed, wanted, problems) = DeckBuildService.ClaimAndWant(old);
            string msg = $"Restored the version of {v.SavedText}: {MainCount(old)} cards, {claimed} claimed from your collection" +
                         (wanted > 0 ? $", {wanted} on the Want List for this deck" : "") +
                         ". The deck as it was is saved as a version (restore it to undo)." +
                         (problems.Count > 0 ? "  " + string.Join("; ", problems.Take(3)) + "." : "");
            return (old, msg, problems.Count > 0);
        }
    }
}
