using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PathSnip.Tools
{
    /// <summary>
    /// 马赛克标注工具
    /// 轨迹型工具，使用 Polyline 记录绘制轨迹
    /// </summary>
    public class MosaicTool : IAnnotationTool
    {
        public AnnotationType Type => AnnotationType.Mosaic;
        public bool IsStrokeBased => true;

        private const double MinSampleDistanceDip = 2.5;
        private const double MinSampleIntervalMs = 16.0;
        private const double EndPointEpsilonDip = 0.5;

        private AnnotationToolContext _context;
        private Polyline _currentPolyline;
        private Point _lastSamplePoint;
        private DateTime _lastSampleTime;

        public void OnSelected(AnnotationToolContext context)
        {
            _context = context;
        }

        public void OnDeselected()
        {
            Cancel();
            // 不再设置 _context = null，避免闭包捕获导致撤销时崩溃
        }

        public void OnMouseDown(Point position)
        {
            if (_context == null) return;

            // 创建马赛克轨迹线
            _currentPolyline = new Polyline
            {
                Stroke = _context.CreateMosaicBrush(),
                StrokeThickness = _context.MosaicBlockSize,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            };

            _currentPolyline.Points.Add(position);
            _lastSamplePoint = position;
            _lastSampleTime = DateTime.UtcNow;

            _context.MosaicCanvas.Children.Add(_currentPolyline);
        }

        public void OnMouseMove(Point position)
        {
            if (_context == null || _currentPolyline == null) return;

            var now = DateTime.UtcNow;
            var distance = position - _lastSamplePoint;
            bool crossedDistanceThreshold = distance.LengthSquared >= MinSampleDistanceDip * MinSampleDistanceDip;
            bool crossedTimeThreshold = (now - _lastSampleTime).TotalMilliseconds >= MinSampleIntervalMs;

            if (!crossedDistanceThreshold && !crossedTimeThreshold)
            {
                return;
            }

            _currentPolyline.Points.Add(position);
            _lastSamplePoint = position;
            _lastSampleTime = now;
        }

        public void OnMouseUp(Point position)
        {
            if (_currentPolyline == null)
            {
                return;
            }

            if (_currentPolyline.Points.Count == 0)
            {
                _currentPolyline.Points.Add(position);
            }
            else
            {
                Point lastPoint = _currentPolyline.Points[_currentPolyline.Points.Count - 1];
                var distance = position - lastPoint;
                if (distance.LengthSquared >= EndPointEpsilonDip * EndPointEpsilonDip)
                {
                    _currentPolyline.Points.Add(position);
                }
            }

            OnComplete(action => _context.PushToUndo(action));

            _currentPolyline = null;
        }

        public void Cancel()
        {
            if (_currentPolyline != null && _context != null)
            {
                _context.MosaicCanvas.Children.Remove(_currentPolyline);
                _currentPolyline = null;
            }
        }

        public void OnComplete(Action<UndoAction> pushToUndo)
        {
            if (_currentPolyline == null || _context == null) return;

            var element = _currentPolyline;
            var undoAction = new UndoAction(
                // Undo: 从马赛克画布移除
                () => _context.MosaicCanvas.Children.Remove(element),
                // Redo: 重新添加到马赛克画布
                () => _context.MosaicCanvas.Children.Add(element)
            );
            pushToUndo(undoAction);
        }
    }
}
