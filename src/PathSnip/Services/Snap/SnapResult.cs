using System;
using System.Windows;

namespace PathSnip.Services.Snap
{
    public enum SnapKind
    {
        None,
        Window,
        Region,
        Element
    }

    public enum SnapSource
    {
        WindowDetection,
        UIA,
        MSAA,
        Image
    }

    public sealed class SnapResult
    {
        public static readonly SnapResult None = new SnapResult(null, string.Empty, false, SnapKind.None, SnapSource.WindowDetection, 0, null, RegionKind.Unknown, null);

        public SnapResult(
            Rect? bounds,
            string label,
            bool isValid,
            SnapKind kind,
            SnapSource source,
            double confidence,
            IntPtr? windowHandle = null,
            RegionKind regionKind = RegionKind.Unknown,
            RegionCandidate regionCandidate = null)
        {
            Bounds = bounds;
            Label = label ?? string.Empty;
            IsValid = isValid;
            Kind = kind;
            Source = source;
            Confidence = confidence;
            WindowHandle = windowHandle;
            RegionKind = regionKind;
            RegionCandidate = regionCandidate;
        }

        public Rect? Bounds { get; }

        public string Label { get; }

        public bool IsValid { get; }

        public SnapKind Kind { get; }

        public SnapSource Source { get; }

        public double Confidence { get; }

        public IntPtr? WindowHandle { get; }

        public RegionKind RegionKind { get; }

        public RegionCandidate RegionCandidate { get; }

        public static SnapResult FromWindow(Rect bounds, string label = "", double confidence = 1.0, IntPtr? windowHandle = null)
        {
            return new SnapResult(bounds, label, true, SnapKind.Window, SnapSource.WindowDetection, confidence, windowHandle);
        }

        public static SnapResult FromRegion(RegionCandidate regionCandidate)
        {
            if (regionCandidate == null)
            {
                return None;
            }

            return new SnapResult(
                regionCandidate.Bounds,
                regionCandidate.Label,
                true,
                SnapKind.Region,
                regionCandidate.Source,
                regionCandidate.Confidence,
                regionCandidate.WindowHandle,
                regionCandidate.RegionKind,
                regionCandidate);
        }
    }
}
