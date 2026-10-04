using System;
using System.Collections.Generic;
using BreakersOfE.Models;

namespace BreakersOfE.Services
{
    /// <summary>The file layouts the importer reads.</summary>
    public enum ImportFormat
    {
        /// <summary>Work it out from the file itself.</summary>
        Auto,
        /// <summary>BoE's own full collection file (every field: finish, language, condition, notes, storage …).</summary>
        BoeFull,
        /// <summary>ManaBox CSV (also what MTG Deck Tools uploads).</summary>
        ManaBox,
        /// <summary>Any other app's CSV, read by its column names.</summary>
        OtherCsv,
        /// <summary>A text list: "4 Lightning Bolt (M10) 146 *F*", with optional Commander / Deck / Sideboard headers.</summary>
        TextList,
    }

    /// <summary>Where an import goes.</summary>
    public enum ImportTarget
    {
        /// <summary>The card collection (tokens go to the token collection).</summary>
        Collection,
        WantList,
        /// <summary>A new deck file.</summary>
        NewDeck,
    }

    /// <summary>Add to what's there, or make the target match the file.</summary>
    public enum ImportMode { Add, Replace }

    /// <summary>Which part of a deck a line belongs to (text lists with headers).</summary>
    public enum ImportSection { None, Commander, Main, Sideboard, Tokens, Maybe }

    /// <summary>One card line as read from a file — before matching to the pool.</summary>
    public sealed class ImportLine
    {
        /// <summary>Line number in the file (1-based).</summary>
        public int LineNumber { get; set; }
        /// <summary>The line as written, for the preview ("4 Lightning Bolt (M10) 146").</summary>
        public string Source { get; set; } = "";

        public int Quantity { get; set; } = 1;
        public string Name { get; set; } = "";
        public string SetCode { get; set; } = "";
        public string SetName { get; set; } = "";
        public string CollectorNumber { get; set; } = "";
        public string ScryfallId { get; set; } = "";

        /// <summary>Finish as stated (null = the file doesn't say).</summary>
        public string? Finish { get; set; }
        /// <summary>Language / condition as stated (null = the file doesn't say).</summary>
        public string? Language { get; set; }
        public string? Condition { get; set; }

        public decimal? Price { get; set; }
        public string Notes { get; set; } = "";
        public string Storage { get; set; } = "";
        public bool? Favorite { get; set; }
        public DateTime? DateAdded { get; set; }

        public ImportSection Section { get; set; }
        /// <summary>BoE full file: which collection table the row belongs to ("Collection", "CollTokens", …).</summary>
        public string? Table { get; set; }
    }

    /// <summary>What reading a file gave: its lines, the layout it was read as, and anything worth saying.</summary>
    public sealed class ImportParseResult
    {
        public ImportFormat Format { get; set; }
        public List<ImportLine> Lines { get; } = new();
        /// <summary>Lines that couldn't be read at all ("line 12: no card name").</summary>
        public List<string> Problems { get; } = new();
        /// <summary>A refusal for the whole file (empty = fine).</summary>
        public string Error { get; set; } = "";
    }

    /// <summary>One printing in the card pool (cards or tokens), as the matcher knows it.</summary>
    public sealed class PoolPrinting
    {
        /// <summary>"Collection" (cards) or "CollTokens" (tokens) — the collection table it goes to.</summary>
        public string Table { get; init; } = "";
        public string ScryfallId { get; init; } = "";
        public string Name { get; init; } = "";
        public string SetCode { get; init; } = "";
        public string SetName { get; init; } = "";
        public string CollectorNumber { get; init; } = "";
        public string ReleasedAt { get; init; } = "";
        public bool NonFoil { get; init; }
        public bool Foil { get; init; }
        public bool Etched { get; init; }
        public bool Digital { get; init; }
        public bool IsToken => Table == CollectionEditService.TokensTable;

        /// <summary>"M10 #146 — Magic 2010".</summary>
        public string Text => $"{SetCode.ToUpperInvariant()} #{CollectorNumber} — {SetName}" + (IsToken ? " (token)" : "");

        public bool Has(string finish) => CollectionEditService.FinishExists(finish, NonFoil, Foil, Etched);

        /// <summary>The finishes this printing comes in.</summary>
        public IEnumerable<string> Finishes()
        {
            if (NonFoil) yield return CardFinish.NonFoil;
            if (Foil) yield return CardFinish.Foil;
            if (Etched) yield return CardFinish.Etched;
        }

        public override string ToString() => Text;
    }

    /// <summary>How a line matched.</summary>
    public enum MatchStatus
    {
        /// <summary>One printing, and it comes in the finish asked for.</summary>
        Ready,
        /// <summary>Several printings fit; one was picked — check it.</summary>
        Check,
        /// <summary>The printing was found but the line can't go in as it is (finish it doesn't come in, 0 copies …).</summary>
        Problem,
        /// <summary>No printing in the card pool fits.</summary>
        NotFound,
        /// <summary>Left out on purpose (a Maybeboard line).</summary>
        Skipped,
    }
}
