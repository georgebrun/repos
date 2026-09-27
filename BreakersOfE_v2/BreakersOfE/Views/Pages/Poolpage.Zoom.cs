using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using BreakersOfE.Filtering;
using BreakersOfE.ViewModels;


namespace BreakersOfE.Views.Pages
{
    // Grid zoom: Ctrl + mouse wheel, Ctrl + = / Ctrl + - / Ctrl + 0.
    // Part of PoolPage (same class).
    public partial class PoolPage
    {
        // ══════════════════════════════════════════════════════════════════
        // ZOOM — grid only (the gallery has its own Card size slider).
        // 70%–150% in 10% steps; text, rows, symbols and pills scale together.
        // Saved per table (and separately for Edit) in GridZoom.json.
        // ══════════════════════════════════════════════════════════════════
        private const double ZoomMin = 0.7, ZoomMax = 1.5, ZoomStep = 0.1;
        private double _zoom = 1.0;

        private void InitZoom()
        {
            PoolGrid.PreviewMouseWheel += (s, e) =>
            {
                if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == 0) return;
                ApplyZoom(_zoom + (e.Delta > 0 ? ZoomStep : -ZoomStep), save: true);
                e.Handled = true;
            };
            PoolGrid.PreviewKeyDown += (s, e) =>
            {
                if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == 0) return;
                switch (e.Key)
                {
                    case System.Windows.Input.Key.OemPlus:
                    case System.Windows.Input.Key.Add:
                        ApplyZoom(_zoom + ZoomStep, save: true); e.Handled = true; break;
                    case System.Windows.Input.Key.OemMinus:
                    case System.Windows.Input.Key.Subtract:
                        ApplyZoom(_zoom - ZoomStep, save: true); e.Handled = true; break;
                    case System.Windows.Input.Key.D0:
                    case System.Windows.Input.Key.NumPad0:
                        ApplyZoom(1.0, save: true); e.Handled = true; break;
                }
            };
        }

        /// <summary>Scale the grid (and its totals row, so columns stay aligned).</summary>
        private void ApplyZoom(double zoom, bool save)
        {
            zoom = Math.Round(Math.Clamp(zoom, ZoomMin, ZoomMax), 1);
            _zoom = zoom;
            var scale = Math.Abs(zoom - 1.0) < 0.001 ? null : new ScaleTransform(zoom, zoom);
            PoolGrid.LayoutTransform = scale ?? Transform.Identity;
            TotalsGrid.LayoutTransform = scale ?? Transform.Identity;
            if (save && !string.IsNullOrEmpty(_layoutTable))
                Services.GridLayoutService.SetZoom(LayoutKey(_layoutTable), zoom);
            if (save) _vm.StatusText = $"Zoom {zoom:P0}";
        }
    }
}
