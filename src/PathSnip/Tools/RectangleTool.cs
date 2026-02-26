using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PathSnip.Tools
{
    /// <summary>
    /// 矩形标注工具
    /// </summary>
    public class RectangleTool : IAnnotationTool
    {
        public AnnotationType Type => AnnotationType.Rectangle;
        public bool IsStrokeBased => false;

        private AnnotationToolContext? _context;
        private Point? _startPoint;
        private Rectangle? _currentRectangle;

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

            // 创建矩形
            _currentRectangle = new Rectangle
            {
                Stroke = _context.CurrentColorBrush,
                StrokeThickness = _context.CurrentThickness,
                Fill = Brushes.Transparent
            };

            Canvas.SetLeft(_currentRectangle, _startPoint.Value.X);
            Canvas.SetTop(_currentRectangle, _startPoint.Value.Y);
            _currentRectangle.Width = 0;
            _currentRectangle.Height = 0;

            _context.AnnotationCanvas.Children.Add(_currentRectangle);
        }

        public void OnMouseMove(Point position)
        {
            if (_context == null || _currentRectangle == null || _startPoint == null) return;

            // 限制终点在选区内
            var currentPoint = _context.ClampToBounds(position);

            // 计算矩形位置和尺寸（支持反向拖拽）
            var x = Math.Min(_startPoint.Value.X, currentPoint.X);
            var y = Math.Min(_startPoint.Value.Y, currentPoint.Y);
            var width = Math.Abs(currentPoint.X - _startPoint.Value.X);
            var height = Math.Abs(currentPoint.Y - _startPoint.Value.Y);

            // 限制在画布边界内
            width = Math.Min(width, _context.AnnotationCanvas.Width - x);
            height = Math.Min(height, _context.AnnotationCanvas.Height - y);

            Canvas.SetLeft(_currentRectangle, x);
            Canvas.SetTop(_currentRectangle, y);
            _currentRectangle.Width = width;
            _currentRectangle.Height = height;
        }

        public void OnMouseUp(Point position)
        {
            // 矩形已经在 MouseMove 中完成绘制，这里只需要处理撤销栈
            OnComplete(action => _context?.PushToUndo(action));

            _currentRectangle = null;
            _startPoint = null;
        }

        public void Cancel()
        {
            if (_currentRectangle != null && _context != null)
            {
                _context.AnnotationCanvas.Children.Remove(_currentRectangle);
                _currentRectangle = null;
            }
            _startPoint = null;
        }

        public void OnComplete(Action<UndoAction> pushToUndo)
        {
            if (_currentRectangle == null || _context == null) return;

            var element = _currentRectangle;
            pushToUndo(
                // Undo: 从画布移除
                () => _context.AnnotationCanvas.Children.Remove(element),
                // Redo: 重新添加到画布
                () => _context.AnnotationCanvas.Children.Add(element)
            );
        }
    }
}
