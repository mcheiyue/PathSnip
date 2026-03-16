using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PathSnip
{
    public partial class PinnedImageWindow : Window
    {
        private Point _dragStartPoint;
        private bool _isDragging;
        private double _currentOpacity = 1.0;
        private double _currentScale = 1.0;
        private const double MinOpacity = 0.2;
        private const double MaxOpacity = 1.0;
        private const double MinScale = 0.2;
        private const double MaxScale = 5.0;
        private const double OpacityStep = 0.1;
        private const double ScaleStep = 0.1;

        private System.Windows.Threading.DispatcherTimer _opacityHintHideTimer;

        public PinnedImageWindow()
        {
            InitializeComponent();
        }

        public void SetImage(BitmapSource image)
        {
            if (image == null) return;

            PinnedImage.Source = image;
        }

        public void SetBounds(double screenLeft, double screenTop, double logicalWidth, double logicalHeight)
        {
            if (logicalWidth <= 0 || logicalHeight <= 0) return;

            double borderPadding = 8;
            Width = logicalWidth + borderPadding;
            Height = logicalHeight + borderPadding;
            Left = screenLeft - 4;
            Top = screenTop - 4;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                _isDragging = true;
                _dragStartPoint = e.GetPosition(this);
                CaptureMouse();
            }
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                var screenPos = PointToScreen(e.GetPosition(this));
                Left = screenPos.X - _dragStartPoint.X;
                Top = screenPos.Y - _dragStartPoint.Y;
            }
        }

        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                ReleaseMouseCapture();
            }
        }

        private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Delta > 0)
                {
                    _currentOpacity = Math.Min(MaxOpacity, _currentOpacity + OpacityStep);
                }
                else
                {
                    _currentOpacity = Math.Max(MinOpacity, _currentOpacity - OpacityStep);
                }

                Opacity = _currentOpacity;
                OpacityHint.Text = $"透明度: {(_currentOpacity * 100):F0}%";
                OpacityHint.Visibility = Visibility.Visible;

                ResetOpacityHintHideTimer();
            }
            else
            {
                if (e.Delta > 0)
                {
                    _currentScale = Math.Min(MaxScale, _currentScale + ScaleStep);
                }
                else
                {
                    _currentScale = Math.Max(MinScale, _currentScale - ScaleStep);
                }

                PinnedImage.RenderTransform = new ScaleTransform(_currentScale, _currentScale);
            }
        }

        private void Window_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 右键显示提示
            OpacityHint.Text = "右键/双击关闭贴图";
            OpacityHint.Visibility = Visibility.Visible;

            ResetOpacityHintHideTimer();
        }

        private void ResetOpacityHintHideTimer()
        {
            if (_opacityHintHideTimer == null)
            {
                _opacityHintHideTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                _opacityHintHideTimer.Tick += OpacityHintHideTimer_Tick;
            }

            _opacityHintHideTimer.Stop();
            _opacityHintHideTimer.Start();
        }

        private void OpacityHintHideTimer_Tick(object sender, EventArgs e)
        {
            _opacityHintHideTimer.Stop();
            OpacityHint.Visibility = Visibility.Collapsed;
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_opacityHintHideTimer != null)
            {
                _opacityHintHideTimer.Stop();
                _opacityHintHideTimer.Tick -= OpacityHintHideTimer_Tick;
                _opacityHintHideTimer = null;
            }

            base.OnClosed(e);
        }

        private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            Close();
        }
    }
}
