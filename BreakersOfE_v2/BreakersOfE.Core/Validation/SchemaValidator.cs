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
    }
}
