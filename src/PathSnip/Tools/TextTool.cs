using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PathSnip.Tools
{
    /// <summary>
    /// 文字标注工具
    /// 点击创建 TextBox，失去焦点时转换为 TextBlock
    /// </summary>
    public class TextTool : IAnnotationTool
    {
        public AnnotationType Type => AnnotationType.Text;
        public bool IsStrokeBased => false;

        private AnnotationToolContext? _context;
        private TextBox? _currentTextBox;
        private TextBlock? _currentTextBlock;
        private Point? _startPoint;

        public void OnSelected(AnnotationToolContext context)
        {
            _context = context;
        }

        public void OnDeselected()
        {
            ConfirmInput();
            _context = null;
        }

        public void OnMouseDown(Point position)
        {
            if (_context == null) return;

            // 先确认之前的输入（如果有）
            ConfirmInput();

            // 限制文字框位置在选区内（预留50px宽度和20px高度）
            var pos = _context.ClampToBounds(position);
            pos.X = Math.Max(_context.SelectionBounds.Left, Math.Min(pos.X, _context.SelectionBounds.Right - 50));
            pos.Y = Math.Max(_context.SelectionBounds.Top, Math.Min(pos.Y, _context.SelectionBounds.Bottom - 20));

            _startPoint = pos;

            // 创建文字输入框
            _currentTextBox = new TextBox
            {
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
                Foreground = _context.CurrentColorBrush,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                MinWidth = 50
            };

            Canvas.SetLeft(_currentTextBox, pos.X);
            Canvas.SetTop(_currentTextBox, pos.Y);
            _context.AnnotationCanvas.Children.Add(_currentTextBox);

            _currentTextBox.Focus();
            _currentTextBox.SelectAll();

            // 失去焦点时完成输入
            _currentTextBox.LostFocus += TextBox_LostFocus;

            // 回车确认
            _currentTextBox.KeyDown += TextBox_KeyDown;
        }

        public void OnMouseMove(Point position)
        {
            // 文字工具点击即创建，不需要拖拽
        }

        public void OnMouseUp(Point position)
        {
            // 文字工具点击即创建，不需要拖拽释放
        }

        public void Cancel()
        {
            if (_currentTextBox != null && _context != null)
            {
                _context.AnnotationCanvas.Children.Remove(_currentTextBox);
                _currentTextBox = null;
            }
            if (_currentTextBlock != null && _context != null)
            {
                _context.AnnotationCanvas.Children.Remove(_currentTextBlock);
                _currentTextBlock = null;
            }
            _startPoint = null;
        }

        public void OnComplete(Action<UndoAction> pushToUndo)
        {
            if (_currentTextBlock == null || _context == null) return;

            var element = _currentTextBlock;
            pushToUndo(
                // Undo: 从画布移除
                () => _context.AnnotationCanvas.Children.Remove(element),
                // Redo: 重新添加到画布
                () => _context.AnnotationCanvas.Children.Add(element)
            );
        }

        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ConfirmInput();
        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Keyboard.ClearFocus();
            }
        }

        /// <summary>
        /// 确认文字输入，将 TextBox 转换为 TextBlock
        /// </summary>
        private void ConfirmInput()
        {
            if (_currentTextBox == null || _context == null) return;

            // 移除事件监听
            _currentTextBox.LostFocus -= TextBox_LostFocus;
            _currentTextBox.KeyDown -= TextBox_KeyDown;

            var text = _currentTextBox.Text;
            var position = new Point(Canvas.GetLeft(_currentTextBox), Canvas.GetTop(_currentTextBox));

            // 移除 TextBox
            _context.AnnotationCanvas.Children.Remove(_currentTextBox);

            // 如果没有文字，结束
            if (string.IsNullOrWhiteSpace(text))
            {
                _currentTextBox = null;
                return;
            }

            // 转换为 TextBlock
            _currentTextBlock = new TextBlock
            {
                Text = text,
                FontFamily = _currentTextBox.FontFamily,
                FontSize = _currentTextBox.FontSize,
                Foreground = _currentTextBox.Foreground
            };

            Canvas.SetLeft(_currentTextBlock, position.X);
            Canvas.SetTop(_currentTextBlock, position.Y);
            _context.AnnotationCanvas.Children.Add(_currentTextBlock);

            // 推入撤销栈
            var textBlock = _currentTextBlock;
            _context.PushToUndo(
                // Undo
                () => _context.AnnotationCanvas.Children.Remove(textBlock),
                // Redo
                () => _context.AnnotationCanvas.Children.Add(textBlock)
            );

            _currentTextBox = null;
            _currentTextBlock = null;
        }
    }
}
