using BreakersOfE.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace BreakersOfE.Services
{
    public static class RulingsService
    {
        private static readonly HttpClient _http = new();

        /// <summary>
        /// Get rulings for a card. Checks local DB first, falls back to API.
        /// Returns a list of (date, text) tuples.
        /// </summary>
        public static async Task<List<(string date, string text)>> GetRulingsAsync(
            string scryfallId, string oracleId = "")
        {
            if (string.IsNullOrWhiteSpace(scryfallId) && string.IsNullOrWhiteSpace(oracleId))
                return new List<(string, string)>();

            // Try local DB first (stored by oracle_id from bulk download)
            try
            {
                using var db = new RulingsDbContext();
                db.EnsureCreated();

                List<CardRuling>? local = null;
                if (!string.IsNullOrEmpty(oracleId))
                    local = db.CardRulings
                        .Where(r => r.ScryfallId == oracleId)
                        .OrderBy(r => r.PublishedAt)
                        .ToList();

                if ((local == null || local.Count == 0) && !string.IsNullOrEmpty(scryfallId))
                    local = db.CardRulings
                        .Where(r => r.ScryfallId == scryfallId)
                        .OrderBy(r => r.PublishedAt)
                        .ToList();

                if (local != null && local.Count > 0)
                    return local.Select(r => (r.PublishedAt, r.Comment)).ToList();
            }
            catch { }

            // Fall back to Scryfall API
            try
            {
                var url = $"https://api.scryfall.com/cards/{scryfallId}/rulings";
                _http.DefaultRequestHeaders.Clear();
                _http.DefaultRequestHeaders.Add("User-Agent", "BreakersOfE/2.0");
                _http.DefaultRequestHeaders.Add("Accept", "application/json");

                var response = await _http.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                    return new List<(string, string)>();

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                var results = new List<(string date, string text)>();
                if (doc.RootElement.TryGetProperty("data", out var data))
                {
                    foreach (var ruling in data.EnumerateArray())
                    {
                        string date = ruling.TryGetProperty("published_at", out var d)
                            ? d.GetString() ?? "" : "";
                        string text = ruling.TryGetProperty("comment", out var c)
                            ? c.GetString() ?? "" : "";
                        if (!string.IsNullOrEmpty(text))
                            results.Add((date, text));
                    }
                }

                // Cache in local DB for next time
                if (results.Count > 0)
                {
                    try
                    {
                        using var db = new RulingsDbContext();
                        db.EnsureCreated();
                        foreach (var (date, text) in results)
                        {
                            db.CardRulings.Add(new CardRuling
                            {
                                ScryfallId = scryfallId,
                                PublishedAt = date,
                                Comment = text
                            });
                        }
                        db.SaveChanges();
                    }
                    catch { }
                }

                return results;
            }
            catch
            {
                return new List<(string, string)>();
            }
        }
    }
}