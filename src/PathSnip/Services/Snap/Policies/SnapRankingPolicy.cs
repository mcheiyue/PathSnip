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
            SnapResult lastAcceptedElement,
            SnapAppProfile appProfile,
            out string reason)
        {
            reason = string.Empty;

            if (!windowSnap.IsValid || !windowSnap.Bounds.HasValue)
            {
                reason = "window_invalid";
                return elementSnap.IsValid;
            }

            if (!elementSnap.IsValid || !elementSnap.Bounds.HasValue)
            {
                reason = "element_invalid";
                return false;
            }

            Rect windowBounds = windowSnap.Bounds.Value;
            Rect elementBounds = elementSnap.Bounds.Value;
            double windowArea = Math.Max(1, windowBounds.Width * windowBounds.Height);
            double elementArea = Math.Max(1, elementBounds.Width * elementBounds.Height);
            double areaRatio = elementArea / windowArea;

            double quickPassMinAreaRatio = appProfile == SnapAppProfile.Explorer ? 0.002 :
                                           appProfile == SnapAppProfile.Ide ? 0.003 :
                                           appProfile == SnapAppProfile.Browser ? 0.004 : 0.005;

            if (elementBounds.Contains(cursorPoint) && areaRatio >= quickPassMinAreaRatio && areaRatio <= 0.92)
            {
                reason = "quick_pass";
                return true;
            }

            double elementScore = ScoreElement(windowSnap, elementSnap, cursorPoint, lastAcceptedElement, appProfile);
            double windowScore = ScoreWindow(windowSnap, cursorPoint, appProfile);
            bool accepted = elementScore >= windowScore + MinAcceptDelta;
            reason = accepted
                ? $"score_pass element={elementScore:F1} window={windowScore:F1}"
                : $"score_reject element={elementScore:F1} window={windowScore:F1}";
            return accepted;
        }

        private static double ScoreElement(SnapResult windowSnap, SnapResult elementSnap, Point cursorPoint, SnapResult lastAcceptedElement, SnapAppProfile appProfile)
        {
            Rect windowBounds = windowSnap.Bounds.Value;
            Rect elementBounds = elementSnap.Bounds.Value;

            double sourceWeight = elementSnap.Source == SnapSource.MSAA ? 38 : 36;
            if (appProfile == SnapAppProfile.Explorer)
            {
                sourceWeight += 3;
            }
            else if (appProfile == SnapAppProfile.Ide)
            {
                sourceWeight += 1;
            }

            double cursorFitWeight = elementBounds.Contains(cursorPoint) ? 15 : 0;

            double windowArea = Math.Max(1, windowBounds.Width * windowBounds.Height);
            double elementArea = Math.Max(1, elementBounds.Width * elementBounds.Height);
            double areaRatio = elementArea / windowArea;

            double sizeWeight;
            if (areaRatio <= 0.015)
            {
                sizeWeight = appProfile == SnapAppProfile.Explorer ? 10 : 6;
            }
            else if (areaRatio <= 0.55)
            {
                sizeWeight = 15;
            }
            else if (areaRatio <= 0.75)
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

        private static double ScoreWindow(SnapResult windowSnap, Point cursorPoint, SnapAppProfile appProfile)
        {
            Rect windowBounds = windowSnap.Bounds.Value;
            double baseScore = appProfile == SnapAppProfile.Explorer ? 24 :
                               appProfile == SnapAppProfile.Browser ? 26 :
                               appProfile == SnapAppProfile.Ide ? 27 : 28;
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
