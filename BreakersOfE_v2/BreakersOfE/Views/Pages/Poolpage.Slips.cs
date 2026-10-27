using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BreakersOfE.Models;
using BreakersOfE.Services;

namespace BreakersOfE.Views.Pages
{
    /// <summary>
    /// Right-click → Print Slip, on the Edit pages only, and only where it
    /// makes sense:
    /// <list type="bullet">
    /// <item>the Want List (cards you're getting): each row's quantity;</item>
    /// <item>the deck table in Edit → Decks: each line's count, basic lands left out;</item>
    /// <item>the token tables (pool and collection): one per row.</item>
    /// </list>
    /// Cards print as PROXY slips; tokens print plain. See <see cref="SlipPrinter"/>.
    /// </summary>
    public partial class PoolPage
    {
        private const string SlipItemTag = "PrintSlip";

        /// <summary>The tables Print Slip shows on.</summary>
        private bool SlipsAllowedHere =>
            _layoutPrefix.Length > 0 &&                                    // Edit pages (several rows at once)
            _currentTag is "WantList" or DeckTableTag or "Tokens" or "CollTokens";

        /// <summary>Add "Print Slip…" at the end of a row menu (once); it shows only on the tables above.</summary>
        private void AddPrintSlipItem(ContextMenu menu)
        {
            if (menu.Items.OfType<MenuItem>().Any(m => SlipItemTag.Equals(m.Tag))) return;
            var separator = new Separator();
            if (menu.Items.Count > 0) menu.Items.Add(separator);
            var item = new MenuItem { Header = "Print Slip…", Tag = SlipItemTag };
            item.Click += (_, _) => PrintSlipsForSelection();
            menu.Items.Add(item);
            menu.Opened += (_, _) =>
            {
                bool show = SlipsAllowedHere;
                item.Visibility = separator.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (!show) return;
                int n = SlipsFor(SelectedUnordered).Count;
                item.Header = n > 1 ? $"Print Slips ({n})…" : "Print Slip…";
                item.IsEnabled = n > 0;
            };
        }

        /// <summary>How many of each row: Want List = its quantity, deck = its count (no basic lands), tokens = one.</summary>
        private static int CopiesOf(object row) => row switch
        {
            WantListEntry w => Math.Max(0, w.Quantity),
            DeckCard d when d.IsFooter || d.IsBasicLand => 0,
            DeckCard d => Math.Max(0, d.TotalQuantity),
            _ => 1,
        };

        private static List<Slip> SlipsFor(IEnumerable<object> rows)
        {
            var slips = new List<Slip>();
            // Two-sided tokens print both sides; when both rows of one card are
            // selected, the card prints once.
            var printedAsOther = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                int copies = CopiesOf(row);
                if (copies == 0) continue;
                string sid = row.GetType().GetProperty("ScryfallId")?.GetValue(row) as string ?? "";
                if (sid.Length > 0 && printedAsOther.Remove(sid)) continue;
                var sides = Slip.WithOtherSides(row);
                if (sides.Count > 1 && row is TokenCollectionEntry or TokenCard or DeckCard)
                    foreach (var o in SafeOtherSides(row)) printedAsOther.Add(o);
                for (int c = 0; c < copies; c++) slips.AddRange(sides);
            }
            return slips;
        }

        private static IEnumerable<string> SafeOtherSides(object row)
        {
            try { return CollectionEditService.OtherSideRefs(row).Select(o => o.ScryfallId).ToList(); }
            catch { return Enumerable.Empty<string>(); }
        }

        private void PrintSlipsForSelection()
        {
            if (!SlipsAllowedHere) return;
            try
            {
                var slips = SlipsFor(SelectedCards);
                if (slips.Count == 0) return;
                SlipPrinter.Print(Window.GetWindow(this), slips);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(this)!, $"Could not print: {ex.Message}", "Print Slip",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
