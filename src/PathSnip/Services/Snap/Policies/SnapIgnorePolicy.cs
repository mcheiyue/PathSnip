using System;
using System.Windows;

namespace PathSnip.Services.Snap
{
    public sealed class SnapIgnorePolicy
    {
        private const double MinElementSize = 8;
        private const double MaxElementAreaRatio = 0.9;

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
            if (elementArea / windowArea > MaxElementAreaRatio)
            {
                return true;
            }

            return false;
        }
    }
}
