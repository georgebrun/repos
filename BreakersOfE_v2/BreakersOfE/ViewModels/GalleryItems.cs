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
                if (_image == null && !_loading) BeginLoad();
                return _image;
            }
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
                string? path = await ImageCacheService.EnsureCachedAsync(ScryfallId, ImageUrl);
                if (path == null) return;   // offline / no image → name placeholder stays

                var bmp = await Task.Run(() => ImageCacheService.LoadBitmap(path, DecodeWidth));
                if (bmp == null) return;

                _image = bmp;
                GalleryImageTracker.Track(this);
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
        /// reloads from the disk cache (fast, no download).
        /// </summary>
        internal void ReleaseImage() => _image = null;

        // ── Factory: works for any card type via cached reflection ──────────
        private static readonly ConcurrentDictionary<Type, Accessors> _accessors = new();

        private sealed class Accessors
        {
            public PropertyInfo? Sid, Url, SmallUrl, Name, Usd, UsdFoil;
            public PropertyInfo? Finish, Price, FinishPill;   // collection rows / pill
        }

        public static GalleryItem FromCard(object card)
        {
            var a = _accessors.GetOrAdd(card.GetType(), t => new Accessors
            {
                Sid = t.GetProperty("ScryfallId"),
                Url = t.GetProperty("ImageNormalUrl"),
                SmallUrl = t.GetProperty("ImageSmallUrl"),
                Name = t.GetProperty("Name"),
                Usd = t.GetProperty("PriceUsd"),
                UsdFoil = t.GetProperty("PriceUsdFoil"),
                Finish = t.GetProperty("Finish"),
                Price = t.GetProperty("Price"),
                FinishPill = t.GetProperty("FinishPill"),
            });

            string sid = a.Sid?.GetValue(card) as string ?? "";
            string url = a.Url?.GetValue(card) as string ?? "";
            if (string.IsNullOrEmpty(url))
                url = a.SmallUrl?.GetValue(card) as string ?? "";
            string name = a.Name?.GetValue(card) as string ?? "";

            // Price badge. Collection rows (they have Finish + Price): that
            // row's own finish price. Pool cards: non-foil price, or the foil
            // price for foil-only printings.
            decimal? p;
            if (a.Finish != null && a.Price != null)
            {
                p = a.Price.GetValue(card) as decimal?;
            }
            else
            {
                decimal? usd = a.Usd?.GetValue(card) as decimal?;
                decimal? foil = a.UsdFoil?.GetValue(card) as decimal?;
                p = usd ?? foil;
            }
            string price = p.HasValue ? $"${p.Value:F2}" : "";

            string pill = a.FinishPill?.GetValue(card) as string ?? "";

            return new GalleryItem(card, sid, url, name, price, pill);
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