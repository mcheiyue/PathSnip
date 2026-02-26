using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PathSnip.Tools
{
    /// <summary>
    /// 箭头标注工具
    /// </summary>
    public class ArrowTool : IAnnotationTool
    {
        public AnnotationType Type => AnnotationType.Arrow;
        public bool IsStrokeBased => false;

        private AnnotationToolContext _context;
        private Point? _startPoint;
        private Path _currentArrow;

        public void OnSelected(AnnotationToolContext context)
        {
            _context = context;
        }

        public void OnDeselected()
        {
            Cancel();
            _context = null;
        }

        public void OnMouseDown(Point position)
        {
            if (_context == null) return;

            // 限制起点在选区内
            _startPoint = _context.ClampToBounds(position);

            // 创建箭头 Path
            _currentArrow = new Path
            {
                Stroke = _context.CurrentColorBrush,
                StrokeThickness = _context.CurrentThickness,
                Fill = _context.CurrentColorBrush,
                Data = CreateArrowGeometry(
                    _startPoint.Value.X, _startPoint.Value.Y,
                    _startPoint.Value.X, _startPoint.Value.Y,
                    _context.CurrentThickness)
            };

            _context.AnnotationCanvas.Children.Add(_currentArrow);
        }

        public void OnMouseMove(Point position)
        {
            if (_context == null || _currentArrow == null || _startPoint == null) return;

            // 限制终点在选区内
            var currentPoint = _context.ClampToBounds(position);

            // 更新箭头几何
            _currentArrow.Data = CreateArrowGeometry(
                _startPoint.Value.X, _startPoint.Value.Y,
                currentPoint.X, currentPoint.Y,
                _context.CurrentThickness);
        }

        public void OnMouseUp(Point position)
        {
            // 箭头已经在 MouseMove 中完成绘制，这里只需要处理撤销栈
            OnComplete(action => _context.PushToUndo(action));

            _currentArrow = null;
            _startPoint = null;
        }

        public void Cancel()
        {
            if (_currentArrow != null && _context != null)
            {
                _context.AnnotationCanvas.Children.Remove(_currentArrow);
                _currentArrow = null;
            }
            _startPoint = null;
        }

        public void OnComplete(Action<UndoAction> pushToUndo)
        {
            if (_currentArrow == null || _context == null) return;

            var element = _currentArrow;
            var undoAction = new UndoAction(
                // Undo: 从画布移除
                () => _context.AnnotationCanvas.Children.Remove(element),
                // Redo: 重新添加到画布
                () => _context.AnnotationCanvas.Children.Add(element)
            );
            pushToUndo(undoAction);
        }

        /// <summary>
        /// 创建箭头几何图形
        /// </summary>
        private static Geometry CreateArrowGeometry(double x1, double y1, double x2, double y2, double thickness)
        {
            var group = new GeometryGroup();

            // 线条
            group.Children.Add(new LineGeometry(new Point(x1, y1), new Point(x2, y2)));

            // 箭头头部大小根据粗细调整
            var arrowLength = 8 + thickness * 2;
            var angle = Math.Atan2(y2 - y1, x2 - x1);
            var arrowAngle = Math.PI / 6; // 30度

            var arrowPoint1 = new Point(
                x2 - arrowLength * Math.Cos(angle - arrowAngle),
                y2 - arrowLength * Math.Sin(angle - arrowAngle));
            var arrowPoint2 = new Point(
                x2 - arrowLength * Math.Cos(angle + arrowAngle),
                y2 - arrowLength * Math.Sin(angle + arrowAngle));

            var arrowHead = new PathGeometry();
            var arrowFigure = new PathFigure { StartPoint = arrowPoint1, IsClosed = true };
            arrowFigure.Segments.Add(new LineSegment(new Point(x2, y2), true));
            arrowFigure.Segments.Add(new LineSegment(arrowPoint2, true));
            arrowHead.Figures.Add(arrowFigure);

            group.Children.Add(arrowHead);

            return group;
        }
    }
}
