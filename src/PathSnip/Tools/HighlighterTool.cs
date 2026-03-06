using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PathSnip.Tools
{
    public class HighlighterTool : IAnnotationTool
    {
        public AnnotationType Type => AnnotationType.Highlighter;
        public bool IsStrokeBased => true;

        private const double MinPointDistance = 1.8;
        private const uint MinSampleIntervalMs = 12;

        private AnnotationToolContext _context;
        private Polyline _currentPolyline;
        private bool _isDrawing;
        private int _lastSampleTick;

        public void OnSelected(AnnotationToolContext context)
        {
            _context = context;
        }

        public void OnDeselected()
        {
            Cancel();
        }

        public void OnMouseDown(Point position)
        {
            if (_context == null)
            {
                return;
            }

            _isDrawing = true;
            _lastSampleTick = Environment.TickCount;

            _currentPolyline = new Polyline
            {
                Stroke = _context.CurrentColorBrush,
                StrokeThickness = Math.Max(1, _context.CurrentThickness),
                StrokeLineJoin = PenLineJoin.Bevel,
                StrokeStartLineCap = PenLineCap.Square,
                StrokeEndLineCap = PenLineCap.Square,
                IsHitTestVisible = false
            };

            _currentPolyline.Points.Add(position);
            _context.AnnotationCanvas.Children.Add(_currentPolyline);
        }

        public void OnMouseMove(Point position)
        {
            if (!_isDrawing || _currentPolyline == null)
            {
                return;
            }

            var points = _currentPolyline.Points;
            Point lastPoint = points.Count > 0 ? points[points.Count - 1] : position;
            double dx = position.X - lastPoint.X;
            double dy = position.Y - lastPoint.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);

            uint elapsedMs = unchecked((uint)(Environment.TickCount - _lastSampleTick));
            if (distance < MinPointDistance && elapsedMs < MinSampleIntervalMs)
            {
                return;
            }

            points.Add(position);
            _lastSampleTick = Environment.TickCount;
        }

        public void OnMouseUp(Point position)
        {
            if (!_isDrawing)
            {
                return;
            }

            _isDrawing = false;

            if (_currentPolyline != null)
            {
                _currentPolyline.Points.Add(position);
                OnComplete(action => _context.PushToUndo(action));
            }
        }

        public void Cancel()
        {
            if (_currentPolyline != null)
            {
                _context?.AnnotationCanvas.Children.Remove(_currentPolyline);
                _currentPolyline = null;
            }

            _isDrawing = false;
        }

        public void OnComplete(Action<UndoAction> pushToUndo)
        {
            if (_currentPolyline == null || _context == null)
            {
                return;
            }

            var completedPolyline = _currentPolyline;
            var undoAction = new UndoAction(
                () => _context.AnnotationCanvas.Children.Remove(completedPolyline),
                () => _context.AnnotationCanvas.Children.Add(completedPolyline)
            );
            pushToUndo(undoAction);

            _currentPolyline = null;
        }
    }
}
