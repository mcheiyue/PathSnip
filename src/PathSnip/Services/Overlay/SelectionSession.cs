using System;
using System.Windows;

namespace PathSnip.Services.Overlay
{
    public sealed class SelectionSession
    {
        public Point StartPoint { get; private set; }

        public bool IsSelecting { get; private set; }

        public bool IsDragging { get; private set; }

        public Rect? PotentialSnapRect { get; private set; }

        public bool HasPotentialSnapRect
        {
            get { return PotentialSnapRect.HasValue; }
        }

        public void Begin(Point startPoint, Rect? potentialSnapRect)
        {
            StartPoint = startPoint;
            PotentialSnapRect = potentialSnapRect;
            IsSelecting = true;
            IsDragging = false;
        }

        public void Update(Point currentPoint, double dragThreshold)
        {
            if (IsDragging)
            {
                return;
            }

            if (Math.Abs(currentPoint.X - StartPoint.X) <= dragThreshold &&
                Math.Abs(currentPoint.Y - StartPoint.Y) <= dragThreshold)
            {
                return;
            }

            IsDragging = true;
            PotentialSnapRect = null;
        }

        public void Complete()
        {
            IsSelecting = false;
        }

        public void Reset()
        {
            StartPoint = new Point();
            PotentialSnapRect = null;
            IsSelecting = false;
            IsDragging = false;
        }
    }
}
