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

        public PinnedImageWindow()
        {
            InitializeComponent();
        }

        public void SetImage(BitmapSource image)
        {
            if (image == null) return;

            PinnedImage.Source = image;
            Width = image.PixelWidth;
            Height = image.PixelHeight;

            if (Width > SystemParameters.WorkArea.Width * 0.8)
            {
                double scale = (SystemParameters.WorkArea.Width * 0.8) / Width;
                Width = SystemParameters.WorkArea.Width * 0.8;
                Height = image.PixelHeight * scale;
            }

            if (Height > SystemParameters.WorkArea.Height * 0.8)
            {
                double scale = (SystemParameters.WorkArea.Height * 0.8) / Height;
                Height = SystemParameters.WorkArea.Height * 0.8;
                Width = image.PixelWidth * scale;
            }

            var screenCenter = new Point(
                SystemParameters.WorkArea.Width / 2 - Width / 2,
                SystemParameters.WorkArea.Height / 2 - Height / 2);
            Left = screenCenter.X;
            Top = screenCenter.Y;
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
                var currentPos = e.GetPosition(this);
                double offsetX = currentPos.X - _dragStartPoint.X;
                double offsetY = currentPos.Y - _dragStartPoint.Y;

                Left += offsetX;
                Top += offsetY;
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
                
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                timer.Tick += (s, args) =>
                {
                    OpacityHint.Visibility = Visibility.Collapsed;
                    timer.Stop();
                };
                timer.Start();
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
            Close();
        }
    }
}