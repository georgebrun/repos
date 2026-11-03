using System;

namespace BreakersOfE.Playtest
{
    /// <summary>
    /// How a picture sits in a frame that never changes (the battlefield, or
    /// a card for sleeves): the picture is moved and made bigger or smaller
    /// underneath it. Zoom 1 = just fills the frame (no gaps); below 1 the
    /// whole picture shows smaller, with the table around it; above 1 it's
    /// closer. Never stretched.
    /// </summary>
    public sealed class Framing
    {
        public const double MinZoom = 0.1, MaxZoom = 6;

        public double Zoom { get; set; } = 1;

        /// <summary>The picture's point at the frame's center, 0–1 across and down (may go past the edges).</summary>
        public double CenterX { get; set; } = 0.5;
        public double CenterY { get; set; } = 0.5;

        public bool IsDefault => Math.Abs(Zoom - 1) < 0.001 && Math.Abs(CenterX - 0.5) < 0.001 && Math.Abs(CenterY - 0.5) < 0.001;

        /// <summary>
        /// Where the <paramref name="pictureW"/>×<paramref name="pictureH"/>
        /// picture goes in a <paramref name="frameW"/>×<paramref name="frameH"/>
        /// frame: its left and top (may be outside the frame) and its size.
        /// </summary>
        public (double Left, double Top, double Width, double Height) Place(double pictureW, double pictureH, double frameW, double frameH)
        {
            if (pictureW <= 0 || pictureH <= 0 || frameW <= 0 || frameH <= 0) return (0, 0, frameW, frameH);
            double fill = Math.Max(frameW / pictureW, frameH / pictureH);
            double s = fill * Math.Clamp(Zoom, MinZoom, MaxZoom);
            double w = pictureW * s, h = pictureH * s;
            return (frameW / 2 - CenterX * w, frameH / 2 - CenterY * h, w, h);
        }

        /// <summary>Move the picture by a distance in the frame (a drag).</summary>
        public void Move(double dx, double dy, double pictureW, double pictureH, double frameW, double frameH)
        {
            var (_, _, w, h) = Place(pictureW, pictureH, frameW, frameH);
            if (w <= 0 || h <= 0) return;
            // Keep at least a sliver of the picture in the frame.
            CenterX = Math.Clamp(CenterX - dx / w, -frameW / (2 * w) + 0.02, 1 + frameW / (2 * w) - 0.02);
            CenterY = Math.Clamp(CenterY - dy / h, -frameH / (2 * h) + 0.02, 1 + frameH / (2 * h) - 0.02);
        }

        public Framing Clone() => new() { Zoom = Zoom, CenterX = CenterX, CenterY = CenterY };
    }
}
