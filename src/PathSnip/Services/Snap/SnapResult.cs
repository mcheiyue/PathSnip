using System.Windows;

namespace PathSnip.Services.Snap
{
    public enum SnapKind
    {
        None,
        Window,
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
        public static readonly SnapResult None = new SnapResult(null, string.Empty, false, SnapKind.None, SnapSource.WindowDetection, 0);

        public SnapResult(Rect? bounds, string label, bool isValid, SnapKind kind, SnapSource source, double confidence)
        {
            Bounds = bounds;
            Label = label ?? string.Empty;
            IsValid = isValid;
            Kind = kind;
            Source = source;
            Confidence = confidence;
        }

        public Rect? Bounds { get; }

        public string Label { get; }

        public bool IsValid { get; }

        public SnapKind Kind { get; }

        public SnapSource Source { get; }

        public double Confidence { get; }

        public static SnapResult FromWindow(Rect bounds, string label = "", double confidence = 1.0)
        {
            return new SnapResult(bounds, label, true, SnapKind.Window, SnapSource.WindowDetection, confidence);
        }
    }
}
