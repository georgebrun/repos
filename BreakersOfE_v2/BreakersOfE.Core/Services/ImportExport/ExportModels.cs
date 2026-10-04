using System;

namespace BreakersOfE.Services
{
    /// <summary>What an export lists.</summary>
    public enum ExportSource
    {
        Collection,
        Deck,
        WantList,
        TradeBinder,
    }

    /// <summary>The file an export writes.</summary>
    public enum ExportFormat
    {
        /// <summary>"4 Lightning Bolt (M10) 146" lines — MTG Deck Tools, Moxfield, Archidekt, BoE's own import.</summary>
        TextList,
        /// <summary>ManaBox's CSV layout — the file MTG Deck Tools (and ManaBox) upload.</summary>
        ManaBoxCsv,
        /// <summary>Every detail of the list, for Excel and the like.</summary>
        SpreadsheetCsv,
        /// <summary>BoE's own backup: every paper collection table, every field (Import → Replace restores it).</summary>
        BoeFull,
    }

    /// <summary>What to export and how.</summary>
    public sealed class ExportOptions
    {
        public ExportSource Source { get; init; }
        public ExportFormat Format { get; init; }
        /// <summary>The deck file (Source = Deck).</summary>
        public string DeckPath { get; init; } = "";
        /// <summary>Collection: only copies not claimed by decks or the Trade Binder.</summary>
        public bool OnlyFree { get; init; }
        /// <summary>Collection and decks: tokens too.</summary>
        public bool IncludeTokens { get; init; }
        /// <summary>Text list: " *F*" / " *E*" after foil and etched lines.</summary>
        public bool MarkFinish { get; init; }
    }

    /// <summary>One line of an export: one printing in one finish (and language / condition where the list has them).</summary>
    public sealed class ExportRow
    {
        /// <summary>Decks: "Commander", "Deck", "Sideboard", "Tokens". Blank for other lists.</summary>
        public string Section { get; init; } = "";
        public string ScryfallId { get; init; } = "";
        public string Name { get; init; } = "";
        public string SetCode { get; init; } = "";
        public string SetName { get; init; } = "";
        public string CollectorNumber { get; init; } = "";
        public string Rarity { get; init; } = "";
        public string Finish { get; init; } = "nonfoil";
        public string Language { get; init; } = "";
        public string Condition { get; init; } = "";
        public int Quantity { get; set; }
        /// <summary>Collection: copies claimed by decks or the Trade Binder.</summary>
        public int InUse { get; init; }
        /// <summary>Market price of one copy in this finish.</summary>
        public decimal? Price { get; init; }
        /// <summary>Want List: my offer. Trade Binder: asking price.</summary>
        public decimal? MyPrice { get; init; }
        public string Notes { get; init; } = "";
        public string Storage { get; init; } = "";
        public bool IsToken { get; init; }
    }

    /// <summary>The finished export.</summary>
    public sealed class ExportResult
    {
        public string Text { get; init; } = "";
        public int Lines { get; init; }
        public int Copies { get; init; }
        /// <summary>A suggested file name (without folder).</summary>
        public string FileName { get; init; } = "";
        /// <summary>Something the user should know (e.g. ManaBox CSV has no deck sections).</summary>
        public string Note { get; init; } = "";
        public string Error { get; init; } = "";
    }
}
