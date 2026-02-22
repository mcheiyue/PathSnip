using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PathSnip.Services;

namespace PathSnip
{
    public partial class CaptureOverlayWindow : Window
    {
        private Point _startPoint;
        private bool _isSelecting;

        public event Action<Rect> CaptureCompleted;
        public event Action CaptureCancelled;

        public CaptureOverlayWindow()
        {
            InitializeComponent();

            // 全屏覆盖
            var bounds = ScreenCaptureService.GetVirtualScreenBounds();
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;

            MouseLeftButtonDown += OnMouseLeftButtonDown;
            MouseMove += OnMouseMove;
            MouseLeftButtonUp += OnMouseLeftButtonUp;
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _startPoint = e.GetPosition(SelectionCanvas);
            _isSelecting = true;

            SelectionRect.Visibility = Visibility.Visible;
            SizeLabel.Visibility = Visibility.Visible;
            HintText.Visibility = Visibility.Collapsed;

            UpdateSelection(_startPoint, _startPoint);
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isSelecting) return;

            var currentPoint = e.GetPosition(SelectionCanvas);
            UpdateSelection(_startPoint, currentPoint);
        }

        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isSelecting) return;

            _isSelecting = false;

            var endPoint = e.GetPosition(SelectionCanvas);
            var rect = GetRect(_startPoint, endPoint);

            if (rect.Width > 5 && rect.Height > 5)
            {
                // 转换屏幕坐标
                var screenRect = new Rect(
                    rect.Left + Left,
                    rect.Top + Top,
                    rect.Width,
                    rect.Height);

                CaptureCompleted?.Invoke(screenRect);
            }
            else
            {
                // 选区太小，取消
                CaptureCancelled?.Invoke();
            }
        }

        private void UpdateSelection(Point start, Point end)
        {
            var rect = GetRect(start, end);

            Canvas.SetLeft(SelectionRect, rect.Left);
            Canvas.SetTop(SelectionRect, rect.Top);
            SelectionRect.Width = rect.Width;
            SelectionRect.Height = rect.Height;

            // 更新尺寸标注
            SizeText.Text = $"{(int)rect.Width} × {(int)rect.Height}";
            Canvas.SetLeft(SizeLabel, rect.Left);
            Canvas.SetTop(SizeLabel, rect.Top - 25);
        }

        private Rect GetRect(Point start, Point end)
        {
            double x = Math.Min(start.X, end.X);
            double y = Math.Min(start.Y, end.Y);
            double width = Math.Abs(end.X - start.X);
            double height = Math.Abs(end.Y - start.Y);

            return new Rect(x, y, width, height);
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CaptureCancelled?.Invoke();
            }
        }
    }
}
