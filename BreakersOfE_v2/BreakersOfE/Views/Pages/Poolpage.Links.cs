using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BreakersOfE.Services;

namespace BreakersOfE.Views.Pages
{
    /// <summary>
    /// Right-click → Open on ▸ EDHREC / Scryfall / Commander Spellbook, on the
    /// Edit pages' tables: the clicked card's page in the browser. (Every page
    /// also has these links in the card panel.)
    /// </summary>
    public partial class PoolPage
    {
        private const string LinksItemTag = "OpenOn";

        private void AddLinksItem(ContextMenu menu)
        {
            if (menu.Items.OfType<MenuItem>().Any(m => LinksItemTag.Equals(m.Tag))) return;
            var separator = new Separator();
            if (menu.Items.Count > 0) menu.Items.Add(separator);
            var open = new MenuItem { Header = "Open on", Tag = LinksItemTag };
            foreach (var (header, site) in new[] { ("EDHREC", 0), ("Scryfall", 1), ("Commander Spellbook", 2) })
            {
                var item = new MenuItem { Header = header };
                item.Click += (_, _) => OpenCardOn(site);
                open.Items.Add(item);
            }
            menu.Items.Add(open);
            menu.Opened += (_, _) =>
            {
                bool show = CardDetailText.Get(SelectedCard, "Name").Length > 0;     // not a footer / empty row
                open.Visibility = separator.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            };
        }

        private void OpenCardOn(int site)
        {
            var card = SelectedCard;
            string name = CardDetailText.Get(card, "Name");
            if (name.Length == 0) return;
            CardLinks.Open(CardDetailText.LinkFor(card!, site));
        }
    }
}
