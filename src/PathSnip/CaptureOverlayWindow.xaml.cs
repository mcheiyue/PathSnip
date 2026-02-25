using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using PathSnip.Services;

namespace PathSnip
{
    public enum AnnotationTool
    {
        None,
        Rectangle,
        Arrow,
        Text
    }

    public enum AnnotationColor
    {
        Blue,
        Red,
        Black
    }

    public enum AnnotationThickness
    {
        Thin,
        Medium,
        Thick
    }

    public partial class CaptureOverlayWindow : Window
    {
        private Point _startPoint;
        private bool _isSelecting;
        private bool _hasStartedSelection;  // 是否开始过选区（用于右键判断）
        private Rect _currentRect;
        private AnnotationTool _currentTool = AnnotationTool.None;
        
        // 每个工具独立的颜色和粗细设置
        private AnnotationColor _rectColor = AnnotationColor.Blue;
        private AnnotationThickness _rectThickness = AnnotationThickness.Medium;
        private AnnotationColor _arrowColor = AnnotationColor.Blue;
        private AnnotationThickness _arrowThickness = AnnotationThickness.Medium;
        
        private bool _isDrawing;
        private UIElement _currentDrawingElement;
        private readonly Stack<UIElement> _undoStack = new Stack<UIElement>();
        private bool _selectionCompleted;

        public event Action<Rect> CaptureCompleted;
        public event Action CaptureCancelled;

        public CaptureOverlayWindow(BitmapSource background)
        {
            InitializeComponent();

            // 设置背景图
            BackgroundImage.Source = background;

            // 全屏覆盖
            var bounds = ScreenCaptureService.GetVirtualScreenBounds();
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;

            // 初始化全屏遮罩矩形
            FullScreenGeometry.Rect = new Rect(0, 0, Width, Height);

            MouseLeftButtonDown += OnMouseLeftButtonDown;
            MouseMove += OnMouseMove;
            MouseLeftButtonUp += OnMouseLeftButtonUp;
            MouseRightButtonDown += OnMouseRightButtonDown;

            // 标注画布事件
            AnnotationCanvas.MouseLeftButtonDown += AnnotationCanvas_MouseLeftButtonDown;
            AnnotationCanvas.MouseMove += AnnotationCanvas_MouseMove;
            AnnotationCanvas.MouseLeftButtonUp += AnnotationCanvas_MouseLeftButtonUp;
        }

        private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;

            // 第零级：取消正在绘制的标注
            if (_isDrawing)
            {
                // 取消当前正在画的标注
                if (_currentDrawingElement != null)
                {
                    AnnotationCanvas.Children.Remove(_currentDrawingElement);
                    _currentDrawingElement = null;
                }
                _isDrawing = false;
                return;
            }

            // 第一级：取消当前工具选中
            if (_currentTool != AnnotationTool.None)
            {
                // 关闭气泡面板
                PropertyPopup.IsOpen = false;
                
                _currentTool = AnnotationTool.None;
                UpdateToolbarSelection();
                return;
            }

            // 第二级：选区已锁定时，取消选区
            if (_selectionCompleted)
            {
                ResetToSelectingState();
                return;
            }

            // 第三级：选区未锁定但已开始过时，重置到初始状态
            if (_hasStartedSelection)
            {
                ResetToInitialState();
                return;
            }

            // 第四级：从未开始选区，直接退出
            CaptureCancelled?.Invoke();
        }

        private void ResetToSelectingState()
        {
            _selectionCompleted = false;
            _hasStartedSelection = false;
            Toolbar.Visibility = Visibility.Collapsed;
            AnnotationCanvas.Visibility = Visibility.Collapsed;
            
            // 隐藏选区相关元素
            SelectionRect.Visibility = Visibility.Collapsed;
            OuterMask.Visibility = Visibility.Collapsed;
            SizeLabel.Visibility = Visibility.Collapsed;
            
            // 恢复初始状态
            SelectionRect.IsHitTestVisible = true;
            SizeLabel.IsHitTestVisible = true;
            HintText.Visibility = Visibility.Visible;
            
            // 清除当前选区
            _currentRect = Rect.Empty;
            
            // 清除标注画布和撤销栈（防止幽灵标注）
            AnnotationCanvas.Children.Clear();
            _undoStack.Clear();
        }

        private void ResetToInitialState()
        {
            _selectionCompleted = false;
            _isSelecting = false;
            _hasStartedSelection = false;
            _currentTool = AnnotationTool.None;
            
            // 隐藏所有UI元素
            Toolbar.Visibility = Visibility.Collapsed;
            AnnotationCanvas.Visibility = Visibility.Collapsed;
            SelectionRect.Visibility = Visibility.Collapsed;
            OuterMask.Visibility = Visibility.Collapsed;
            SizeLabel.Visibility = Visibility.Collapsed;
            
            // 显示提示文字
            HintText.Visibility = Visibility.Visible;
            
            // 恢复可交互性
            SelectionRect.IsHitTestVisible = true;
            SizeLabel.IsHitTestVisible = true;
            
            // 清除当前选区
            _currentRect = Rect.Empty;
            
            // 清除标注画布
            AnnotationCanvas.Children.Clear();
            _undoStack.Clear();
            
            // 更新工具栏状态
            UpdateToolbarSelection();
        }

        private void UpdateToolbarSelection()
        {
            // 更新工具栏按钮的选中状态
            RectToolBtn.IsChecked = _currentTool == AnnotationTool.Rectangle;
            ArrowToolBtn.IsChecked = _currentTool == AnnotationTool.Arrow;
            TextToolBtn.IsChecked = _currentTool == AnnotationTool.Text;
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 选区完成后点击应该触发标注或工具栏操作
            if (_selectionCompleted) return;

            _startPoint = e.GetPosition(SelectionCanvas);
            _isSelecting = true;
            _hasStartedSelection = true;

            SelectionRect.Visibility = Visibility.Visible;
            SizeLabel.Visibility = Visibility.Visible;
            HintText.Visibility = Visibility.Collapsed;
            OuterMask.Visibility = Visibility.Visible;

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
                _currentRect = rect;
                _selectionCompleted = true;

                // 转换屏幕坐标
                var screenRect = new Rect(
                    rect.Left + Left,
                    rect.Top + Top,
                    rect.Width,
                    rect.Height);

                // 显示工具栏
                ShowToolbar();
            }
            else
            {
                // 选区太小，取消
                CaptureCancelled?.Invoke();
            }
        }

        private void ShowToolbar()
        {
            // 先测量工具栏尺寸
            Toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var toolbarWidth = Toolbar.DesiredSize.Width;
            var toolbarHeight = Toolbar.DesiredSize.Height;

            // 工具栏定位在选区下方
            var toolbarLeft = _currentRect.Left + (_currentRect.Width - toolbarWidth) / 2;
            var toolbarTop = _currentRect.Bottom + 10;

            // 确保不超出屏幕
            if (toolbarLeft < 0) toolbarLeft = 0;
            if (toolbarLeft + toolbarWidth > Width) toolbarLeft = Width - toolbarWidth;
            if (toolbarTop + toolbarHeight > Height) toolbarTop = _currentRect.Top - toolbarHeight - 10;

            Canvas.SetLeft(Toolbar, toolbarLeft);
            Canvas.SetTop(Toolbar, toolbarTop);
            Toolbar.Visibility = Visibility.Visible;

            // 激活标注画布
            AnnotationCanvas.Visibility = Visibility.Visible;
            Canvas.SetLeft(AnnotationCanvas, _currentRect.Left);
            Canvas.SetTop(AnnotationCanvas, _currentRect.Top);
            AnnotationCanvas.Width = _currentRect.Width;
            AnnotationCanvas.Height = _currentRect.Height;

            // 锁定选区后不再响应主窗口的鼠标事件
            SelectionRect.IsHitTestVisible = false;
            SizeLabel.IsHitTestVisible = false;
        }

        private void UpdateSelection(Point start, Point end)
        {
            var rect = GetRect(start, end);

            // 更新选区边框
            Canvas.SetLeft(SelectionRect, rect.Left);
            Canvas.SetTop(SelectionRect, rect.Top);
            SelectionRect.Width = rect.Width;
            SelectionRect.Height = rect.Height;

            // 更新挖孔位置
            HoleGeometry.Rect = rect;

            // 更新尺寸标注
            SizeText.Text = $"{(int)rect.Width} × {(int)rect.Height}";
            Canvas.SetLeft(SizeLabel, rect.Left);
            Canvas.SetTop(SizeLabel, rect.Top - 25);
        }

        private void UpdateOuterMask(Rect selectionRect)
        {
            // 已经在 UpdateSelection 中更新 HoleGeometry，这里只需要确保可见
            OuterMask.Visibility = Visibility.Visible;
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

        #region 标注画布事件

        private void AnnotationCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_currentTool == AnnotationTool.None || _currentTool == AnnotationTool.Text) return;

            // 限制起始点在选区内
            _startPoint = e.GetPosition(AnnotationCanvas);
            _startPoint.X = Math.Max(0, Math.Min(_startPoint.X, AnnotationCanvas.Width));
            _startPoint.Y = Math.Max(0, Math.Min(_startPoint.Y, AnnotationCanvas.Height));

            _isDrawing = true;

            if (_currentTool == AnnotationTool.Rectangle)
            {
                var strokeColor = GetCurrentColor();
                var strokeThickness = GetCurrentThickness();
                
                var rect = new Rectangle
                {
                    Stroke = new SolidColorBrush(strokeColor),
                    StrokeThickness = strokeThickness,
                    Fill = Brushes.Transparent
                };
                Canvas.SetLeft(rect, _startPoint.X);
                Canvas.SetTop(rect, _startPoint.Y);
                AnnotationCanvas.Children.Add(rect);
                _currentDrawingElement = rect;
            }
            else if (_currentTool == AnnotationTool.Arrow)
            {
                var strokeColor = GetCurrentColor();
                var strokeThickness = GetCurrentThickness();
                
                // 创建箭头 - 使用Path绘制
                var arrowPath = new Path
                {
                    Stroke = new SolidColorBrush(strokeColor),
                    StrokeThickness = strokeThickness,
                    Fill = new SolidColorBrush(strokeColor),
                    Data = CreateArrowGeometry(_startPoint.X, _startPoint.Y, _startPoint.X, _startPoint.Y, strokeThickness)
                };
                AnnotationCanvas.Children.Add(arrowPath);
                _currentDrawingElement = arrowPath;
            }

            AnnotationCanvas.CaptureMouse();
        }

        private Geometry CreateArrowGeometry(double x1, double y1, double x2, double y2, double thickness)
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

        private void AnnotationCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDrawing) return;

            var currentPoint = e.GetPosition(AnnotationCanvas);

            // 限制坐标在选区内
            currentPoint.X = Math.Max(0, Math.Min(currentPoint.X, AnnotationCanvas.Width));
            currentPoint.Y = Math.Max(0, Math.Min(currentPoint.Y, AnnotationCanvas.Height));

            if (_currentTool == AnnotationTool.Rectangle && _currentDrawingElement is Rectangle rect)
            {
                var x = Math.Max(0, Math.Min(_startPoint.X, currentPoint.X));
                var y = Math.Max(0, Math.Min(_startPoint.Y, currentPoint.Y));
                var width = Math.Min(Math.Abs(currentPoint.X - _startPoint.X), AnnotationCanvas.Width - x);
                var height = Math.Min(Math.Abs(currentPoint.Y - _startPoint.Y), AnnotationCanvas.Height - y);

                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);
                rect.Width = width;
                rect.Height = height;
            }
            else if (_currentTool == AnnotationTool.Arrow && _currentDrawingElement is Path arrowPath)
            {
                // 限制终点在选区内
                var endX = Math.Max(0, Math.Min(currentPoint.X, AnnotationCanvas.Width));
                var endY = Math.Max(0, Math.Min(currentPoint.Y, AnnotationCanvas.Height));
                var thickness = GetCurrentThickness();
                arrowPath.Data = CreateArrowGeometry(_startPoint.X, _startPoint.Y, endX, endY, thickness);
            }
        }

        private void AnnotationCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDrawing) return;

            _isDrawing = false;
            AnnotationCanvas.ReleaseMouseCapture();

            if (_currentDrawingElement != null)
            {
                _undoStack.Push(_currentDrawingElement);
                _currentDrawingElement = null;
            }
        }

        #endregion

        #region 工具栏按钮事件

        private void Tool_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string toolName)
            {
                // 切换工具前先移除文字事件处理器
                AnnotationCanvas.MouseLeftButtonDown -= TextAnnotation_Click;

                switch (toolName)
                {
                    case "Rectangle":
                        _currentTool = AnnotationTool.Rectangle;
                        ShowPropertyPanel(); // 显示属性栏
                        break;
                    case "Arrow":
                        _currentTool = AnnotationTool.Arrow;
                        ShowPropertyPanel(); // 显示属性栏
                        break;
                    case "Text":
                        _currentTool = AnnotationTool.Text;
                        PropertyPanel.Visibility = Visibility.Collapsed; // 文字不需要颜色/粗细
                        AnnotationCanvas.MouseLeftButtonDown += TextAnnotation_Click;
                        break;
                }
            }
        }

        private void Tool_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb)
            {
                // 取消选中时清除工具
                _currentTool = AnnotationTool.None;
                PropertyPanel.Visibility = Visibility.Collapsed; // 隐藏属性栏
                AnnotationCanvas.MouseLeftButtonDown -= TextAnnotation_Click;
            }
        }

        private void TextAnnotation_Click(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(AnnotationCanvas);

            // 创建文字输入框
            var textBox = new TextBox
            {
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 120, 212)),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                MinWidth = 50
            };

            Canvas.SetLeft(textBox, pos.X);
            Canvas.SetTop(textBox, pos.Y);
            AnnotationCanvas.Children.Add(textBox);
            _undoStack.Push(textBox);

            textBox.Focus();
            textBox.SelectAll();

            // 失去焦点时完成输入
            textBox.LostFocus += (s, args) =>
            {
                if (string.IsNullOrWhiteSpace(textBox.Text))
                {
                    AnnotationCanvas.Children.Remove(textBox);
                }
            };

            // 回车确认
            textBox.KeyDown += (s, args) =>
            {
                if (args.Key == Key.Enter)
                {
                    Keyboard.ClearFocus();
                }
            };
        }

        private void UndoBtn_Click(object sender, RoutedEventArgs e)
        {
            // 检查元素是否还在 Canvas 中，如果之前因为文本为空被自动移除了，就跳过它
            while (_undoStack.Count > 0)
            {
                var element = _undoStack.Pop();
                if (AnnotationCanvas.Children.Contains(element))
                {
                    AnnotationCanvas.Children.Remove(element);
                    break; // 成功撤销一次可见操作，退出
                }
            }
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            // 强制当前焦点元素（如 TextBox）失去焦点，隐藏输入光标
            Keyboard.ClearFocus();

            // 隐藏UI元素，只保留背景图和标注
            SelectionRect.Visibility = Visibility.Collapsed;
            SizeLabel.Visibility = Visibility.Collapsed;
            OuterMask.Visibility = Visibility.Collapsed;
            Toolbar.Visibility = Visibility.Collapsed;
            
            // 强制渲染
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            
            // 使用背景图 + 标注合成后裁剪
            if (BackgroundImage.Source is BitmapSource background)
            {
                // 获取当前屏幕 DPI
                PresentationSource source = PresentationSource.FromVisual(this);
                double dpiX = 96.0, dpiY = 96.0;
                if (source?.CompositionTarget != null)
                {
                    dpiX = 96.0 * source.CompositionTarget.TransformToDevice.M11;
                    dpiY = 96.0 * source.CompositionTarget.TransformToDevice.M22;
                }

                // 计算物理像素尺寸
                var width = (int)(Width * dpiX / 96.0);
                var height = (int)(Height * dpiY / 96.0);
                
                // 创建渲染目标（使用实际 DPI）
                var renderBitmap = new RenderTargetBitmap(width, height, dpiX, dpiY, PixelFormats.Pbgra32);
                
                var drawingVisual = new DrawingVisual();
                using (var dc = drawingVisual.RenderOpen())
                {
                    // 绘制背景图（需要按 DPI 比例缩放）
                    dc.DrawImage(background, new Rect(0, 0, width, height));
                    
                    // 绘制标注画布内容（只绘制在选区内）
                    if (AnnotationCanvas.Visibility == Visibility.Visible)
                    {
                        // 创建裁剪区域（只绘制选区内，按 DPI 比例缩放）
                        var clipGeometry = new RectangleGeometry(new Rect(
                            _currentRect.Left * dpiX / 96.0,
                            _currentRect.Top * dpiY / 96.0,
                            _currentRect.Width * dpiX / 96.0,
                            _currentRect.Height * dpiY / 96.0));
                        dc.PushClip(clipGeometry);
                        
                        // 绘制标注画布（需要偏移到正确位置，按 DPI 比例缩放）
                        var annotationBrush = new VisualBrush(AnnotationCanvas)
                        {
                            Stretch = Stretch.None,
                            AlignmentX = AlignmentX.Left,
                            AlignmentY = AlignmentY.Top
                        };
                        // 标注画布的绘制位置
                        var annotationLeft = Canvas.GetLeft(AnnotationCanvas) * dpiX / 96.0;
                        var annotationTop = Canvas.GetTop(AnnotationCanvas) * dpiY / 96.0;
                        var annotationWidth = AnnotationCanvas.Width * dpiX / 96.0;
                        var annotationHeight = AnnotationCanvas.Height * dpiY / 96.0;
                        dc.DrawRectangle(annotationBrush, null, new Rect(annotationLeft, annotationTop, annotationWidth, annotationHeight));
                        
                        dc.Pop();
                    }
                }
                
                renderBitmap.Render(drawingVisual);
                
                // 裁剪选区部分（按 DPI 比例缩放）
                var cropRect = new Int32Rect(
                    (int)(_currentRect.Left * dpiX / 96.0),
                    (int)(_currentRect.Top * dpiY / 96.0),
                    (int)(_currentRect.Width * dpiX / 96.0),
                    (int)(_currentRect.Height * dpiY / 96.0));
                
                var croppedBitmap = new CroppedBitmap(renderBitmap, cropRect);
                
                // 触发保存
                CaptureCompletedWithImage?.Invoke(croppedBitmap);
                return;
            }
            
            // 如果没有背景图，回退到原来的方式
            var screenRect = new Rect(
                _currentRect.Left + Left,
                _currentRect.Top + Top,
                _currentRect.Width,
                _currentRect.Height);

            CaptureCompleted?.Invoke(screenRect);
        }

        public event Action<BitmapSource> CaptureCompletedWithImage;

        #region 颜色和粗细

        private Color GetCurrentColor()
        {
            // 获取当前工具对应的颜色
            var color = _currentTool == AnnotationTool.Rectangle ? _rectColor : _arrowColor;
            switch (color)
            {
                case AnnotationColor.Blue:
                    return Color.FromRgb(0, 120, 212);   // #0078D4
                case AnnotationColor.Red:
                    return Color.FromRgb(220, 53, 69);   // #DC3545
                case AnnotationColor.Black:
                    return Color.FromRgb(51, 51, 51);   // #333333
                default:
                    return Color.FromRgb(0, 120, 212);
            }
        }

        private double GetCurrentThickness()
        {
            // 获取当前工具对应的粗细
            var thickness = _currentTool == AnnotationTool.Rectangle ? _rectThickness : _arrowThickness;
            switch (thickness)
            {
                case AnnotationThickness.Thin:
                    return 1;
                case AnnotationThickness.Medium:
                    return 2;
                case AnnotationThickness.Thick:
                    return 4;
                default:
                    return 2;
            }
        }

        private void ColorBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string colorName)
            {
                var color = AnnotationColor.Blue;
                switch (colorName)
                {
                    case "#0078D4":
                        color = AnnotationColor.Blue;
                        break;
                    case "#DC3545":
                        color = AnnotationColor.Red;
                        break;
                    case "#333333":
                        color = AnnotationColor.Black;
                        break;
                }
                
                // 保存到当前工具的属性
                if (_currentTool == AnnotationTool.Rectangle)
                    _rectColor = color;
                else if (_currentTool == AnnotationTool.Arrow)
                    _arrowColor = color;

                // 立即更新UI
                UpdatePropertyPanelUI();
            }
        }

        private void ThicknessBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string thicknessName)
            {
                var thickness = AnnotationThickness.Medium;
                switch (thicknessName)
                {
                    case "Thin":
                        thickness = AnnotationThickness.Thin;
                        break;
                    case "Medium":
                        thickness = AnnotationThickness.Medium;
                        break;
                    case "Thick":
                        thickness = AnnotationThickness.Thick;
                        break;
                }
                
                // 保存到当前工具的属性
                if (_currentTool == AnnotationTool.Rectangle)
                    _rectThickness = thickness;
                else if (_currentTool == AnnotationTool.Arrow)
                    _arrowThickness = thickness;
            }
        }

        private void ShowPropertyPanel()
        {
            if (_currentTool == AnnotationTool.None || _currentTool == AnnotationTool.Text)
            {
                // 文字工具不需要属性栏
                PropertyPopup.IsOpen = false;
                return;
            }

            // 先关闭再打开，确保位置更新
            PropertyPopup.IsOpen = false;

            // 确保属性面板可见
            PropertyPanel.Visibility = Visibility.Visible;

            // 更新UI显示当前工具的设置
            UpdatePropertyPanelUI();

            // 找到当前工具按钮
            RadioButton currentToolBtn = null;
            if (_currentTool == AnnotationTool.Rectangle)
                currentToolBtn = RectToolBtn;
            else if (_currentTool == AnnotationTool.Arrow)
                currentToolBtn = ArrowToolBtn;

            if (currentToolBtn != null)
            {
                // 使用Popup定位到工具按钮
                PropertyPopup.PlacementTarget = currentToolBtn;
                PropertyPopup.Placement = PlacementMode.Top;
                
                // 居中偏移
                var offsetX = -30;
                
                // 边界检查 - 确保不超出左/右边界
                var btnPos = currentToolBtn.TranslatePoint(new Point(0, 0), this);
                if (btnPos.X + offsetX < 0)
                    offsetX = (int)(-btnPos.X);
                else if (btnPos.X + offsetX + 120 > ActualWidth)
                    offsetX = (int)(ActualWidth - btnPos.X - 120);
                
                PropertyPopup.HorizontalOffset = offsetX;
                PropertyPopup.VerticalOffset = -10;
                PropertyPopup.IsOpen = true;
            }
        }

        private void UpdatePropertyPanelUI()
        {
            // 根据当前工具的设置更新UI选中状态
            var color = _currentTool == AnnotationTool.Rectangle ? _rectColor : _arrowColor;
            var thickness = _currentTool == AnnotationTool.Rectangle ? _rectThickness : _arrowThickness;

            // 更新颜色选中状态
            ColorBlue.IsChecked = color == AnnotationColor.Blue;
            ColorRed.IsChecked = color == AnnotationColor.Red;
            ColorBlack.IsChecked = color == AnnotationColor.Black;

            // 更新粗细选中状态
            ThicknessThin.IsChecked = thickness == AnnotationThickness.Thin;
            ThicknessMedium.IsChecked = thickness == AnnotationThickness.Medium;
            ThicknessThick.IsChecked = thickness == AnnotationThickness.Thick;
        }

        #endregion

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            CaptureCancelled?.Invoke();
        }

        #endregion
    }
}
