using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Media;
using BreakersOfE.Services;

namespace BreakersOfE.ViewModels
{
    /// <summary>
    /// One tile in the gallery. Wraps any pool card type (PoolCard, TokenCard,
    /// PlanarCard, …) via cached reflection, so the gallery works for every
    /// Card Pool sub-page without per-type code.
    ///
    /// The image is LAZY: nothing downloads or decodes until the tile is
    /// actually shown (the binding reads Image). Virtualization means only
    /// visible rows ever ask.
    /// </summary>
    public class GalleryItem : INotifyPropertyChanged
    {
        // Decode width for thumbnails — sharp across the whole size-slider range
        // while keeping memory per tile small.
        private const int DecodeWidth = 340;

        public object Card { get; }
        public string ScryfallId { get; }
        public string ImageUrl { get; }
        public string Name { get; }
        public string PriceText { get; }
        public bool HasPrice => !string.IsNullOrEmpty(PriceText);

        /// <summary>Gold finish pill on the tile: "F" for a foil collection row
        /// or a foil-only pool printing, "E" for etched, blank otherwise.</summary>
        public string FinishPill { get; }
        public bool HasFinishPill => !string.IsNullOrEmpty(FinishPill);

        /// <summary>Owned marker (top-left): "×3" when you own copies of this printing.</summary>
        private string _ownedText = "";
        public string OwnedText
        {
            get => _ownedText;
            private set
            {
                if (_ownedText == value) return;
                _ownedText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasOwned));
            }
        }
        public bool HasOwned => !string.IsNullOrEmpty(OwnedText);
        private string _ownedTip = "";
        public string OwnedTip
        {
            get => _ownedTip;
            private set { if (_ownedTip != value) { _ownedTip = value; OnPropertyChanged(); } }
        }

        /// <summary>Re-read the count marker (after an edit on the Edit pages).</summary>
        public void RefreshCounts()
        {
            var (text, tip) = Counts(Card, _accessors.GetOrAdd(Card.GetType(), MakeAccessors));
            OwnedText = text;
            OwnedTip = tip;
        }

        /// <summary>Deck gallery: this card is the deck's commander.</summary>
        public bool IsCommander { get; private set; }

        /// <summary>Set checklist: a printing you don't own shows dimmed.</summary>
        private bool _isDimmed;
        public bool IsDimmed
        {
            get => _isDimmed;
            set { if (_isDimmed != value) { _isDimmed = value; OnPropertyChanged(); } }
        }

        // Set on every row rebuild (size slider / window width).
        public double TileWidth { get; set; }
        public double TileHeight { get; set; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
        }

        private ImageSource? _image;
        private bool _loading;

        /// <summary>
        /// Reading this is what triggers the load — so only tiles the user can
        /// see ever download/decode.
        /// </summary>
        public ImageSource? Image
        {
            get
            {
                // "Card not found" (offline?) tries again when the tile is shown a minute later.
                if (_isNotFound && !_loading && DateTime.UtcNow - _failedAt > TimeSpan.FromMinutes(1))
                {
                    _image = null;
                    IsNotFound = false;                      // card back (no name overlay) while it tries
                }
                if (_image == null && !_loading) BeginLoad();
                return _image ?? CardPictures.CardBack;      // face down until the picture arrives
            }
        }

        /// <summary>No picture (offline, or none exists): "Scryfall Fail" shows, with the card's name on it.</summary>
        private bool _isNotFound;
        private DateTime _failedAt;
        public bool IsNotFound
        {
            get => _isNotFound;
            private set { if (_isNotFound != value) { _isNotFound = value; OnPropertyChanged(); } }
        }

        private GalleryItem(object card, string sid, string url, string name,
                            string price, string finishPill)
        {
            Card = card;
            ScryfallId = sid;
            ImageUrl = url;
            Name = name;
            PriceText = price;
            FinishPill = finishPill;
        }

        private async void BeginLoad()
        {
            _loading = true;
            try
            {
                // Kept on disk when it's yours (or Settings keeps everything); else shown from the internet.
                var bmp = await ImageCacheService.GetPictureAsync(ScryfallId, ImageUrl, DecodeWidth);
                IsNotFound = bmp == null;
                if (bmp == null) _failedAt = DateTime.UtcNow;
                _image = bmp ?? CardPictures.NotFound;
                if (bmp != null) GalleryImageTracker.Track(this);
                OnPropertyChanged(nameof(Image));
            }
            finally
            {
                _loading = false;
            }
        }

        /// <summary>
        /// Drops the decoded bitmap to cap memory. Silent (no PropertyChanged):
        /// if the tile scrolls back into view, the binding re-reads Image and it
        /// reloads — from disk when kept, else from the internet again.
        /// </summary>
        internal void ReleaseImage() => _image = null;

        // ── Factory: works for any card type via cached reflection ──────────
        private static readonly ConcurrentDictionary<Type, Accessors> _accessors = new();

        private sealed class Accessors
        {
            public PropertyInfo? Sid, Url, SmallUrl, Name, Usd, UsdFoil, UsdEtched;
            public PropertyInfo? Finish, Price, FinishPill;   // collection rows / pill
            public PropertyInfo? OwnedTotal;                  // pool / deck cards
            public PropertyInfo? Quantity;                    // collection rows (this finish)
            public PropertyInfo? TotalQuantity;               // deck cards (non-foil + foil)
        }

        private static Accessors MakeAccessors(Type t) => new Accessors
            {
                Sid = t.GetProperty("ScryfallId"),
                Url = t.GetProperty("ImageNormalUrl"),
                SmallUrl = t.GetProperty("ImageSmallUrl"),
                Name = t.GetProperty("Name"),
                Usd = t.GetProperty("PriceUsd"),
                UsdFoil = t.GetProperty("PriceUsdFoil"),
                UsdEtched = t.GetProperty("PriceUsdEtched"),
                Finish = t.GetProperty("Finish"),
                Price = t.GetProperty("Price"),
                FinishPill = t.GetProperty("FinishPill"),
                OwnedTotal = t.GetProperty("OwnedTotal"),
                Quantity = t.GetProperty("Quantity"),
                TotalQuantity = t.GetProperty("TotalQuantity"),
            };

        public static GalleryItem FromCard(object card)
        {
            var a = _accessors.GetOrAdd(card.GetType(), MakeAccessors);

            string sid = a.Sid?.GetValue(card) as string ?? "";
            string url = a.Url?.GetValue(card) as string ?? "";
            if (string.IsNullOrEmpty(url))
                url = a.SmallUrl?.GetValue(card) as string ?? "";
            string name = a.Name?.GetValue(card) as string ?? "";

            // Price badge. Collection rows (they have Finish + Price): that
            // row's own finish price. Pool / deck cards: "non-foil / foil" when
            // both exist, else the one there is (foil-only, etched-only).
            string price;
            if (a.Finish != null && a.Price != null)
            {
                var p = a.Price.GetValue(card) as decimal?;
                price = p.HasValue ? $"${p.Value:F2}" : "";
            }
            else
            {
                decimal? usd = a.Usd?.GetValue(card) as decimal?;
                decimal? foil = a.UsdFoil?.GetValue(card) as decimal?;
                decimal? etched = a.UsdEtched?.GetValue(card) as decimal?;
                if (usd.HasValue && foil.HasValue) price = $"${usd.Value:F2} / ${foil.Value:F2}";
                else if ((usd ?? foil ?? etched) is decimal one) price = $"${one:F2}";
                else price = "";
            }

            string pill = a.FinishPill?.GetValue(card) as string ?? "";

            var (ownedText, ownedTip) = Counts(card, a);
            var item = new GalleryItem(card, sid, url, name, price, pill)
            {
                OwnedText = ownedText,
                OwnedTip = ownedTip,
                // Deck gallery: the commander gets a "Commander" badge, like its
                // row in the grid (flag or Commander category).
                IsCommander = card is Models.DeckCard dc &&
                              (dc.IsCommander || dc.Category == Models.DeckCardCategory.Commander),
            };

            return item;
        }

        /// <summary>
        /// Count marker, matching what the tile stands for:
        ///   deck cards  → copies in the deck (non-foil + foil);
        ///   collection, Trade Binder, Want List rows → that row's quantity
        ///     (one row per finish, so a foil row's ×N sits next to its F pill);
        ///   pool cards  → copies of this printing you own, "×3 (1F)".
        /// </summary>
        private static (string text, string tip) Counts(object card, Accessors a)
        {
            if (a.TotalQuantity?.GetValue(card) is int d)
            {
                int have = a.OwnedTotal?.GetValue(card) is int o ? o : 0;
                return (d > 0 ? $"×{d}" : "", $"Copies in this deck (you own {have})");
            }
            if (a.Quantity?.GetValue(card) is int q)
                return (q > 0 ? $"×{q}" : "", "Quantity");
            int owned = a.OwnedTotal?.GetValue(card) is int n ? n : 0;
            string shown = card.GetType().GetProperty("OwnedDisplay")?.GetValue(card) as string ?? "";
            return (owned > 0 ? "×" + (shown.Length > 0 ? shown : owned.ToString()) : "", "Copies you own");
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>One virtualized row of N tiles (N recalculated from width).</summary>
    public class GalleryRow
    {
        public List<GalleryItem> Items { get; }
        public GalleryRow(List<GalleryItem> items) => Items = items;
    }

    /// <summary>
    /// Caps how many decoded thumbnails stay in memory. Scrolling through
    /// thousands of cards would otherwise keep every bitmap alive. Oldest
    /// loaded images are released first; they reload from disk if seen again.
    /// </summary>
    internal static class GalleryImageTracker
    {
        private const int MaxLoaded = 600;
        private static readonly Queue<GalleryItem> _loaded = new();

        public static void Track(GalleryItem item)
        {
            _loaded.Enqueue(item);
            while (_loaded.Count > MaxLoaded)
            {
                var old = _loaded.Dequeue();
                if (!old.IsSelected) old.ReleaseImage();
            }
        }
    }
}