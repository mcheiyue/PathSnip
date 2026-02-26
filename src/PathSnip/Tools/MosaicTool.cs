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

        private AnnotationToolContext _context;
        private Polyline _currentPolyline;

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

            // 添加到马赛克画布
            _context.MosaicCanvas.Children.Add(_currentPolyline);
        }

        public void OnMouseMove(Point position)
        {
            if (_context == null || _currentPolyline == null) return;

            // 自由涂抹：不断添加鼠标经过的轨迹点
            _currentPolyline.Points.Add(position);
        }

        public void OnMouseUp(Point position)
        {
            // 马赛克轨迹已经在 MouseMove 中完成
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
