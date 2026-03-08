using System;
using System.Windows;

namespace PathSnip.Services.Snap
{
    public sealed class SnapRankingPolicy
    {
        private const double MinAcceptDelta = 0;

        public bool ShouldUseElement(
            SnapResult windowSnap,
            SnapResult elementSnap,
            Point cursorPoint,
            SnapResult lastAcceptedElement)
        {
            if (!windowSnap.IsValid || !windowSnap.Bounds.HasValue)
            {
                return elementSnap.IsValid;
            }

            if (!elementSnap.IsValid || !elementSnap.Bounds.HasValue)
            {
                return false;
            }

            Rect windowBounds = windowSnap.Bounds.Value;
            Rect elementBounds = elementSnap.Bounds.Value;
            double windowArea = Math.Max(1, windowBounds.Width * windowBounds.Height);
            double elementArea = Math.Max(1, elementBounds.Width * elementBounds.Height);
            double areaRatio = elementArea / windowArea;
            if (elementBounds.Contains(cursorPoint) && areaRatio >= 0.005 && areaRatio <= 0.9)
            {
                return true;
            }

            double elementScore = ScoreElement(windowSnap, elementSnap, cursorPoint, lastAcceptedElement);
            double windowScore = ScoreWindow(windowSnap, cursorPoint);
            return elementScore >= windowScore + MinAcceptDelta;
        }

        private static double ScoreElement(SnapResult windowSnap, SnapResult elementSnap, Point cursorPoint, SnapResult lastAcceptedElement)
        {
            Rect windowBounds = windowSnap.Bounds.Value;
            Rect elementBounds = elementSnap.Bounds.Value;

            double sourceWeight = elementSnap.Source == SnapSource.MSAA ? 38 : 36;
            double cursorFitWeight = elementBounds.Contains(cursorPoint) ? 15 : 0;

            double windowArea = Math.Max(1, windowBounds.Width * windowBounds.Height);
            double elementArea = Math.Max(1, elementBounds.Width * elementBounds.Height);
            double areaRatio = elementArea / windowArea;

            double sizeWeight;
            if (areaRatio <= 0.02)
            {
                sizeWeight = 6;
            }
            else if (areaRatio <= 0.35)
            {
                sizeWeight = 15;
            }
            else if (areaRatio <= 0.65)
            {
                sizeWeight = 10;
            }
            else
            {
                sizeWeight = 2;
            }

            double interactiveWeight = string.IsNullOrWhiteSpace(elementSnap.Label) ? 2 : 10;

            double stabilityWeight = 0;
            if (lastAcceptedElement.IsValid && lastAcceptedElement.Bounds.HasValue &&
                ComputeIou(lastAcceptedElement.Bounds.Value, elementBounds) >= 0.85)
            {
                stabilityWeight = 20;
            }

            return sourceWeight + cursorFitWeight + sizeWeight + interactiveWeight + stabilityWeight;
        }

        private static double ScoreWindow(SnapResult windowSnap, Point cursorPoint)
        {
            Rect windowBounds = windowSnap.Bounds.Value;
            double baseScore = 28;
            double cursorFitWeight = windowBounds.Contains(cursorPoint) ? 6 : 0;
            return baseScore + cursorFitWeight;
        }

        private static double ComputeIou(Rect a, Rect b)
        {
            Rect intersection = Rect.Intersect(a, b);
            if (intersection.IsEmpty)
            {
                return 0;
            }

            double intersectionArea = Math.Max(0, intersection.Width * intersection.Height);
            double unionArea = Math.Max(1, a.Width * a.Height + b.Width * b.Height - intersectionArea);
            return intersectionArea / unionArea;
        }
    }
}
