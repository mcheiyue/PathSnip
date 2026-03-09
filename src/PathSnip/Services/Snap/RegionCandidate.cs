using System;
using System.Windows;

namespace PathSnip.Services.Snap
{
    public sealed class RegionCandidate
    {
        public RegionCandidate(Rect bounds, string label, RegionKind regionKind, SnapSource source, double confidence, IntPtr? windowHandle = null)
        {
            Bounds = bounds;
            Label = label ?? string.Empty;
            RegionKind = regionKind;
            Source = source;
            Confidence = confidence;
            WindowHandle = windowHandle;
        }

        public Rect Bounds { get; }

        public string Label { get; }

        public RegionKind RegionKind { get; }

        public SnapSource Source { get; }

        public double Confidence { get; }

        public IntPtr? WindowHandle { get; }
    }
}
