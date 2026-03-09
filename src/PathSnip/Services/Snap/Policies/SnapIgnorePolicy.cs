using System;
using System.Windows;

namespace PathSnip.Services.Snap
{
    public sealed class SnapIgnorePolicy
    {
        private const double MinElementSize = 8;
        private const double MaxElementAreaRatio = 0.9;
        private const double EmptyLabelMinAreaRatio = 0.01;
        private const double EmptyLabelMaxAreaRatio = 0.75;
        private const double ExtremeAspectRatio = 4.5;

        public bool ShouldIgnore(SnapResult windowSnap, SnapResult candidate)
        {
            if (!candidate.IsValid || !candidate.Bounds.HasValue)
            {
                return true;
            }

            if (candidate.Kind != SnapKind.Element)
            {
                return false;
            }

            Rect elementBounds = candidate.Bounds.Value;
            if (elementBounds.Width < MinElementSize || elementBounds.Height < MinElementSize)
            {
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
            if (areaRatio > MaxElementAreaRatio)
            {
                return true;
            }

            bool hasLabel = !string.IsNullOrWhiteSpace(candidate.Label);
            if (!hasLabel)
            {
                double width = Math.Max(1, elementBounds.Width);
                double height = Math.Max(1, elementBounds.Height);
                double aspectRatio = width >= height ? width / height : height / width;

                if (areaRatio >= EmptyLabelMinAreaRatio && areaRatio <= EmptyLabelMaxAreaRatio)
                {
                    return true;
                }

                if (aspectRatio >= ExtremeAspectRatio)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
