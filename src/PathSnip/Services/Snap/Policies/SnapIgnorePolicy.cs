using System;
using System.Windows;

namespace PathSnip.Services.Snap
{
    public sealed class SnapIgnorePolicy
    {
        private const double DefaultMinElementSize = 10;
        private const double ExplorerMinElementSize = 12;
        private const double DefaultMinAreaRatio = 0.0025;
        private const double ExplorerMinAreaRatio = 0.0035;
        private const double BrowserMinAreaRatio = 0.0030;
        private const double IdeMinAreaRatio = 0.0028;
        private const double MaxElementAreaRatio = 0.9;
        private const double ExplorerMaxElementAreaRatio = 0.96;
        private const double EmptyLabelMinAreaRatio = 0.02;
        private const double EmptyLabelMaxAreaRatio = 0.75;
        private const double ExplorerEmptyLabelMinAreaRatio = 0.18;
        private const double ExplorerEmptyLabelMaxAreaRatio = 0.75;
        private const double ExtremeAspectRatio = 4.5;
        private const double ExplorerExtremeAspectRatio = 7.0;
        private const double TinyEmptyLabelAreaRatio = 0.004;

        public bool ShouldIgnore(SnapResult windowSnap, SnapResult candidate, SnapAppProfile appProfile, out string reason)
        {
            reason = string.Empty;

            if (!candidate.IsValid || !candidate.Bounds.HasValue)
            {
                reason = "invalid_candidate";
                return true;
            }

            if (candidate.Kind != SnapKind.Element)
            {
                return false;
            }

            Rect elementBounds = candidate.Bounds.Value;
            double minElementSize = appProfile == SnapAppProfile.Explorer ? ExplorerMinElementSize : DefaultMinElementSize;
            if (elementBounds.Width < minElementSize || elementBounds.Height < minElementSize)
            {
                reason = "too_small";
                return true;
            }

            if (!windowSnap.IsValid || !windowSnap.Bounds.HasValue)
            {
                return false;
            }

            Rect windowBounds = windowSnap.Bounds.Value;
            double windowArea = Math.Max(1, windowBounds.Width * windowBounds.Height);
            double elementArea = Math.Max(1, elementBounds.Width * elementBounds.Height);
            double areaRatio = elementArea / windowArea;

            double minAreaRatio = appProfile == SnapAppProfile.Explorer ? ExplorerMinAreaRatio :
                                  appProfile == SnapAppProfile.Browser ? BrowserMinAreaRatio :
                                  appProfile == SnapAppProfile.Ide ? IdeMinAreaRatio : DefaultMinAreaRatio;
            if (areaRatio < minAreaRatio)
            {
                reason = "too_small_area";
                return true;
            }

            double maxAreaRatio = appProfile == SnapAppProfile.Explorer ? ExplorerMaxElementAreaRatio : MaxElementAreaRatio;
            if (areaRatio > maxAreaRatio)
            {
                reason = "too_large";
                return true;
            }

            bool hasLabel = !string.IsNullOrWhiteSpace(candidate.Label);
            if (!hasLabel)
            {
                double width = Math.Max(1, elementBounds.Width);
                double height = Math.Max(1, elementBounds.Height);
                double aspectRatio = width >= height ? width / height : height / width;

                if (areaRatio <= TinyEmptyLabelAreaRatio)
                {
                    reason = "empty_tiny";
                    return true;
                }

                double emptyMinAreaRatio = appProfile == SnapAppProfile.Explorer ? ExplorerEmptyLabelMinAreaRatio : EmptyLabelMinAreaRatio;
                double emptyMaxAreaRatio = appProfile == SnapAppProfile.Explorer ? ExplorerEmptyLabelMaxAreaRatio : EmptyLabelMaxAreaRatio;
                double maxAspectRatio = appProfile == SnapAppProfile.Explorer ? ExplorerExtremeAspectRatio : ExtremeAspectRatio;

                if (areaRatio >= emptyMinAreaRatio && areaRatio <= emptyMaxAreaRatio)
                {
                    reason = "empty_container";
                    return true;
                }

                if (aspectRatio >= maxAspectRatio)
                {
                    reason = "empty_extreme_aspect";
                    return true;
                }
            }

            return false;
        }
    }
}
