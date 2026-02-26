using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PathSnip.Tools
{
    /// <summary>
    /// 步骤序号标注工具
    /// 点击创建带数字的圆形标签，序号自动递增
    /// </summary>
    public class StepMarkerTool : IAnnotationTool
    {
        public AnnotationType Type => AnnotationType.StepMarker;
        public bool IsStrokeBased => false;

        private AnnotationToolContext _context;
        private Grid _currentStepGrid;

        public void OnSelected(AnnotationToolContext context)
        {
            _context = context;
            // 不再每次选中都重置计数器，保持连续递增
        }

        public void OnDeselected()
        {
            // 不再设置 _context = null，避免闭包捕获导致撤销时崩溃
            _currentStepGrid = null;
        }

        public void OnMouseDown(Point position)
        {
            if (_context == null) return;

            // 限制序号位置在选区内（预留圆形尺寸）
            var pos = _context.ClampToBounds(position);
            pos.X = Math.Max(_context.SelectionBounds.Left, Math.Min(pos.X, _context.SelectionBounds.Right - 24));
            pos.Y = Math.Max(_context.SelectionBounds.Top, Math.Min(pos.Y, _context.SelectionBounds.Bottom - 24));

            // 创建步骤序号圆形标签
            _currentStepGrid = new Grid
            {
                Width = 24,
                Height = 24,
                Tag = "StepMarker" // 标记为步骤序号
            };

            // 圆形背景
            var ellipse = new Ellipse
            {
                Fill = new SolidColorBrush(Color.FromRgb(0, 120, 212)), // 蓝色背景
                Width = 24,
                Height = 24
            };

            // 序号文字
            var numberText = new TextBlock
            {
                Text = _context.StepCounter.ToString(),
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            _currentStepGrid.Children.Add(ellipse);
            _currentStepGrid.Children.Add(numberText);

            Canvas.SetLeft(_currentStepGrid, pos.X);
            Canvas.SetTop(_currentStepGrid, pos.Y);
            _context.AnnotationCanvas.Children.Add(_currentStepGrid);

            // 递增计数器
            _context.StepCounter++;
        }

        public void OnMouseMove(Point position)
        {
            // 步骤序号点击即创建，不需要拖拽
        }

        public void OnMouseUp(Point position)
        {
            // 步骤序号点击即创建
            OnComplete(action => _context.PushToUndo(action));

            _currentStepGrid = null;
        }

        public void Cancel()
        {
            // 步骤序号点击即创建，不需要取消
        }

        public void OnComplete(Action<UndoAction> pushToUndo)
        {
            if (_currentStepGrid == null || _context == null) return;

            var element = _currentStepGrid;

            var undoAction = new UndoAction(
                // Undo: 从画布移除，计数器递减
                () =>
                {
                    _context.AnnotationCanvas.Children.Remove(element);
                    _context.StepCounter = Math.Max(1, _context.StepCounter - 1);
                },
                // Redo: 重新添加到画布，计数器递增
                () =>
                {
                    _context.AnnotationCanvas.Children.Add(element);
                    _context.StepCounter++;
                }
            );
            pushToUndo(undoAction);
        }
    }
}
