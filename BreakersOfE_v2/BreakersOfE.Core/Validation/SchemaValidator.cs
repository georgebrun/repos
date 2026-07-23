using System.Text.Json;

namespace BreakersOfE.Validation
{
    /// <summary>
    /// Validates that Scryfall bulk data cards contain the fields
    /// our import pipeline expects. Run this against the first card
    /// in a bulk download BEFORE importing — if fields are missing,
    /// Scryfall changed their API and the import should be blocked
    /// to prevent database corruption.
    /// 
    /// Usage:
    ///   var (isValid, missing) = SchemaValidator.CheckCard(firstCardElement);
    ///   if (!isValid) → alert user, list missing fields, abort import
    /// </summary>
    public static class SchemaValidator
    {
        /// <summary>
        /// Fields that MUST be present on every card object for our
        /// import pipeline to work correctly. If any are missing,
        /// Scryfall has changed their data format.
        /// </summary>
        private static readonly HashSet<string> RequiredFields = new()
        {
            "id",                   // ScryfallId — universal anchor
            "name",                 // Card name
            "type_line",            // "Legendary Creature — Vampire"
            "mana_cost",            // "{2}{B}{R}"
            "colors",              // ["B", "R"]
            "color_identity",      // ["B", "R"]
            "set",                 // Set code: "c17"
            "set_name",            // "Commander 2017"
            "set_type",            // "commander"
            "collector_number",    // "36"
            "rarity",              // "mythic"
            "prices",              // { "usd": "45.00", ... }
            "games",               // ["paper", "mtgo"]
            "layout",              // "normal", "transform", etc.
            "lang",                // "en"
        };

        /// <summary>
        /// Fields we use but that aren't on every card (DFCs, tokens, etc.).
        /// Missing these is a warning, not a blocker.
        /// </summary>
        private static readonly HashSet<string> OptionalFields = new()
        {
            "oracle_text",         // Not on vanilla creatures or some tokens
            "image_uris",          // Not on DFCs (they use card_faces)
            "card_faces",          // Only on DFCs
            "power",               // Only on creatures
            "toughness",           // Only on creatures
            "loyalty",             // Only on planeswalkers
            "keywords",            // May be empty array
        };

        /// <summary>
        /// Validates a single card element from the bulk data.
        /// Returns whether it's valid and which required fields are missing.
        /// </summary>
        public static (bool IsValid, List<string> MissingFields) CheckCard(JsonElement card)
        {
            var missing = RequiredFields
                .Where(f => !card.TryGetProperty(f, out _))
                .ToList();

            return (missing.Count == 0, missing);
        }

        /// <summary>
        /// Checks optional fields and returns which ones are missing.
        /// These are warnings, not errors — the import can proceed.
        /// </summary>
        public static List<string> CheckOptionalFields(JsonElement card)
        {
            return OptionalFields
                .Where(f => !card.TryGetProperty(f, out _))
                .ToList();
        }

        /// <summary>
        /// Builds a human-readable report of schema validation results.
        /// </summary>
        public static string BuildReport(
            bool isValid,
            List<string> missingRequired,
            List<string> missingOptional)
        {
            if (isValid && missingOptional.Count == 0)
                return "Schema validation passed — all fields present.";

            var lines = new List<string>();

            if (!isValid)
            {
                lines.Add("⚠ SCHEMA VALIDATION FAILED — Import blocked.");
                lines.Add($"Missing required fields: {string.Join(", ", missingRequired)}");
                lines.Add("");
                lines.Add("Scryfall may have changed their data format.");
                lines.Add("Check scryfall.com/blog for announcements.");
            }
            else
            {
                lines.Add("Schema validation passed (with warnings).");
            }

            if (missingOptional.Count > 0)
            {
                lines.Add($"Missing optional fields: {string.Join(", ", missingOptional)}");
                lines.Add("These fields aren't on every card — this is usually fine.");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}
