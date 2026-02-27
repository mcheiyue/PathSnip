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
using PathSnip.Tools;

namespace PathSnip
{
    public enum AnnotationTool
    {
        None,
        Rectangle,
        Arrow,
        Text,
        Mosaic,
        Step
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
        private IAnnotationTool _currentAnnotationTool;  // 新架构：当前标注工具
        private AnnotationToolContext _toolContext;       // 新架构：工具上下文
        
        // 每个工具独立的颜色和粗细设置
        private AnnotationColor _rectColor = AnnotationColor.Blue;
        private AnnotationThickness _rectThickness = AnnotationThickness.Medium;
        private AnnotationColor _arrowColor = AnnotationColor.Blue;
        private AnnotationThickness _arrowThickness = AnnotationThickness.Medium;
        private AnnotationThickness _mosaicThickness = AnnotationThickness.Medium;
        
        // 记录拖拽开始时的四个虚拟边界（用于反向拖拽翻转）
        private double _dragLeft, _dragTop, _dragRight, _dragBottom;
        
        // 记录鼠标相对于锚点的偏移量（用于绝对坐标拖拽）
        private double _mouseOffsetX;
        private double _mouseOffsetY;
        
        private bool _isDrawing;
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

            // 预渲染马赛克背景（性能优化）
            PrepareMosaicBackground();

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
                // 调用工具的 Cancel
                _currentAnnotationTool?.Cancel();
                _isDrawing = false;
                
                // 解除 WPF 鼠标锁定状态
                AnnotationCanvas.ReleaseMouseCapture();
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
            
            // 彻底重置工具状态，防止幽灵上下文
            _currentTool = AnnotationTool.None;
            _currentAnnotationTool?.OnDeselected();
            _currentAnnotationTool = null;
            UpdateToolbarSelection();
            
            AnnotationCanvas.Visibility = Visibility.Collapsed;
            MosaicCanvas.Visibility = Visibility.Collapsed;
            
            // 隐藏选区相关元素
            SelectionRect.Visibility = Visibility.Collapsed;
            OuterMask.Visibility = Visibility.Collapsed;
            SizeLabel.Visibility = Visibility.Collapsed;
            
            // 隐藏调整锚点
            HideResizeAnchors();
            
            // 恢复初始状态
            SelectionRect.IsHitTestVisible = true;
            SizeLabel.IsHitTestVisible = true;
            HintText.Visibility = Visibility.Visible;
            
            // 清除当前选区
            _currentRect = Rect.Empty;
            
            // 清除标注画布、马赛克画布和撤销栈（防止幽灵标注）
            AnnotationCanvas.Children.Clear();
            MosaicCanvas.Children.Clear();
            
            // 销毁上下文，下次框选重新生成干净的
            _toolContext = null;
        }

        private void ResetToInitialState()
        {
            _selectionCompleted = false;
            _isSelecting = false;
            _hasStartedSelection = false;
            _currentTool = AnnotationTool.None;
            
            // 通知当前工具卸载
            _currentAnnotationTool?.OnDeselected();
            _currentAnnotationTool = null;
            
            // 隐藏所有UI元素
            Toolbar.Visibility = Visibility.Collapsed;
            AnnotationCanvas.Visibility = Visibility.Collapsed;
            MosaicCanvas.Visibility = Visibility.Collapsed;
            SelectionRect.Visibility = Visibility.Collapsed;
            OuterMask.Visibility = Visibility.Collapsed;
            SizeLabel.Visibility = Visibility.Collapsed;
            HideResizeAnchors();
            
            // 显示提示文字
            HintText.Visibility = Visibility.Visible;
            
            // 恢复可交互性
            SelectionRect.IsHitTestVisible = true;
            SizeLabel.IsHitTestVisible = true;
            
            // 清除当前选区
            _currentRect = Rect.Empty;
            
            // 清除标注画布和马赛克画布
            AnnotationCanvas.Children.Clear();
            MosaicCanvas.Children.Clear();
            
            // 销毁上下文
            _toolContext = null;
            
            // 更新工具栏状态
            UpdateToolbarSelection();
        }

        private void UpdateToolbarSelection()
        {
            // 更新工具栏按钮的选中状态
            RectToolBtn.IsChecked = _currentTool == AnnotationTool.Rectangle;
            ArrowToolBtn.IsChecked = _currentTool == AnnotationTool.Arrow;
            TextToolBtn.IsChecked = _currentTool == AnnotationTool.Text;
            MosaicToolBtn.IsChecked = _currentTool == AnnotationTool.Mosaic;
            StepToolBtn.IsChecked = _currentTool == AnnotationTool.Step;
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

            // 激活马赛克画布 - 保持全屏不动
            MosaicCanvas.Visibility = Visibility.Visible;
            Canvas.SetLeft(MosaicCanvas, 0);
            Canvas.SetTop(MosaicCanvas, 0);
            MosaicCanvas.Width = Width;
            MosaicCanvas.Height = Height;
            MosaicCanvas.Clip = new RectangleGeometry(_currentRect);

            // 激活标注画布 - 保持全屏不动，避免标注位移
            AnnotationCanvas.Visibility = Visibility.Visible;
            Canvas.SetLeft(AnnotationCanvas, 0);
            Canvas.SetTop(AnnotationCanvas, 0);
            AnnotationCanvas.Width = Width;
            AnnotationCanvas.Height = Height;
            
            // 设置裁剪区域，使标注只显示在选区内
            AnnotationCanvas.Clip = new RectangleGeometry(_currentRect);

            // 锁定选区后不再响应主窗口的鼠标事件
            SelectionRect.IsHitTestVisible = false;
            SizeLabel.IsHitTestVisible = false;

            // 显示选区调整锚点
            ShowResizeAnchors();

            // 初始化全局工具上下文（只初始化一次，后续只更新样式）
            if (_toolContext == null)
            {
                _toolContext = new AnnotationToolContext
                {
                    AnnotationCanvas = AnnotationCanvas,
                    MosaicCanvas = MosaicCanvas,
                    SelectionBounds = _currentRect,
                    MosaicBackground = _mosaicBackground,
                    StepCounter = 1
                };
            }
            else
            {
                // 更新选区边界
                _toolContext.SelectionBounds = _currentRect;
            }
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
            // 如果当前有焦点（比如在 TextBox 里），点击画布空白处时强制清除焦点，触发 LostFocus
            Keyboard.ClearFocus();

            if (_currentTool == AnnotationTool.None) return;

            // 限制起始点在选区内
            _startPoint = e.GetPosition(AnnotationCanvas);
            _startPoint.X = Math.Max(_currentRect.Left, Math.Min(_startPoint.X, _currentRect.Right));
            _startPoint.Y = Math.Max(_currentRect.Top, Math.Min(_startPoint.Y, _currentRect.Bottom));

            _isDrawing = true;

            // 委托给当前标注工具处理
            _currentAnnotationTool?.OnMouseDown(_startPoint);
            AnnotationCanvas.CaptureMouse();
        }

        private int GetMosaicBlockSize()
        {
            // Thin=8, Medium=16, Thick=32
            switch (_mosaicThickness)
            {
                case AnnotationThickness.Thin:
                    return 8;
                case AnnotationThickness.Thick:
                    return 32;
                default:
                    return 16;
            }
        }

        // 预渲染的马赛克背景（用于自由涂抹）
        private BitmapSource _mosaicBackground;
        private double _dpiScaleX = 1.0;
        private double _dpiScaleY = 1.0;

        private void PrepareMosaicBackground()
        {
            if (BackgroundImage.Source == null) return;

            // 获取 DPI 缩放因子
            PresentationSource source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                _dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                _dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
            }

            var bg = BackgroundImage.Source as BitmapSource;
            if (bg == null) return;

            // 创建全屏马赛克背景（预渲染一次，性能优化）
            int width = (int)(Width * _dpiScaleX);
            int height = (int)(Height * _dpiScaleY);

            // 缩放到较小尺寸
            int smallWidth = Math.Max(1, width / 32);
            int smallHeight = Math.Max(1, height / 32);

            var smallBitmap = new TransformedBitmap(bg, new ScaleTransform(
                (double)smallWidth / width,
                (double)smallHeight / height));

            // 放大回来，使用邻近插值
            var renderBitmap = new TransformedBitmap(smallBitmap, new ScaleTransform(
                (double)width / smallWidth,
                (double)height / smallHeight));

            // 创建可用的位图
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawImage(renderBitmap, new Rect(0, 0, width, height));
            }

            var result = new RenderTargetBitmap(width, height, 96 * _dpiScaleX, 96 * _dpiScaleY, PixelFormats.Pbgra32);
            result.Render(visual);

            _mosaicBackground = result;
        }

        private void AnnotationCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDrawing) return;

            var currentPoint = e.GetPosition(AnnotationCanvas);

            // 限制坐标在选区内
            currentPoint.X = Math.Max(_currentRect.Left, Math.Min(currentPoint.X, _currentRect.Right));
            currentPoint.Y = Math.Max(_currentRect.Top, Math.Min(currentPoint.Y, _currentRect.Bottom));

            // 委托给当前标注工具处理
            _currentAnnotationTool?.OnMouseMove(currentPoint);
        }

        private void AnnotationCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDrawing) return;

            _isDrawing = false;
            AnnotationCanvas.ReleaseMouseCapture();

            var currentPoint = e.GetPosition(AnnotationCanvas);

            // 委托给当前标注工具处理
            // 工具类内部已经在 OnMouseUp 中调用 PushToUndo，不需要重复调用
            _currentAnnotationTool?.OnMouseUp(currentPoint);
        }

        #endregion

        #region 工具栏按钮事件

        private void Tool_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string toolName)
            {
                switch (toolName)
                {
                    case "Rectangle":
                        _currentTool = AnnotationTool.Rectangle;
                        UpdateToolContextStyle();
                        // 新架构：创建 RectangleTool
                        _currentAnnotationTool = AnnotationToolFactory.Create(AnnotationType.Rectangle);
                        _currentAnnotationTool.OnSelected(_toolContext);
                        ShowPropertyPanel(); // 显示属性栏
                        break;
                    case "Arrow":
                        _currentTool = AnnotationTool.Arrow;
                        UpdateToolContextStyle();
                        // 新架构：创建 ArrowTool
                        _currentAnnotationTool = AnnotationToolFactory.Create(AnnotationType.Arrow);
                        _currentAnnotationTool.OnSelected(_toolContext);
                        ShowPropertyPanel(); // 显示属性栏
                        break;
                    case "Text":
                        _currentTool = AnnotationTool.Text;
                        UpdateToolContextStyle();
                        // 新架构：创建 TextTool
                        _currentAnnotationTool = AnnotationToolFactory.Create(AnnotationType.Text);
                        _currentAnnotationTool.OnSelected(_toolContext);
                        // TextTool 自己处理点击事件
                        break;
                    case "Mosaic":
                        _currentTool = AnnotationTool.Mosaic;
                        UpdateToolContextStyle();
                        // 新架构：创建 MosaicTool
                        _currentAnnotationTool = AnnotationToolFactory.Create(AnnotationType.Mosaic);
                        _currentAnnotationTool.OnSelected(_toolContext);
                        ShowPropertyPanelForMosaic(); // 马赛克只需要粗细
                        break;
                    case "Step":
                        _currentTool = AnnotationTool.Step;
                        UpdateToolContextStyle();
                        // 新架构：创建 StepMarkerTool
                        _currentAnnotationTool = AnnotationToolFactory.Create(AnnotationType.StepMarker);
                        _currentAnnotationTool.OnSelected(_toolContext);
                        // StepMarkerTool 自己处理点击事件
                        break;
                }
            }
        }

        /// <summary>
        /// 更新工具上下文的样式（颜色、粗细等）
        /// </summary>
        private void UpdateToolContextStyle()
        {
            if (_toolContext == null) return;

            Color color;
            switch (_currentTool)
            {
                case AnnotationTool.Rectangle:
                    color = GetColorFromEnum(_rectColor);
                    break;
                case AnnotationTool.Arrow:
                    color = GetColorFromEnum(_arrowColor);
                    break;
                default:
                    color = Colors.Blue;
                    break;
            }

            double thickness;
            switch (_currentTool)
            {
                case AnnotationTool.Rectangle:
                    thickness = GetThicknessValue(_rectThickness);
                    break;
                case AnnotationTool.Arrow:
                    thickness = GetThicknessValue(_arrowThickness);
                    break;
                case AnnotationTool.Mosaic:
                    thickness = GetMosaicBlockSize();
                    break;
                default:
                    thickness = 2.0;
                    break;
            }

            _toolContext.UpdateStyle(color, thickness, (int)thickness);
        }

        /// <summary>
        /// 更新工具上下文的样式（颜色、粗细等）
        /// </summary>
        private AnnotationToolContext CreateToolContext()
        {
            Color color;
            switch (_currentTool)
            {
                case AnnotationTool.Rectangle:
                    color = GetColorFromEnum(_rectColor);
                    break;
                case AnnotationTool.Arrow:
                    color = GetColorFromEnum(_arrowColor);
                    break;
                default:
                    color = Colors.Blue;
                    break;
            }

            double thickness;
            switch (_currentTool)
            {
                case AnnotationTool.Rectangle:
                    thickness = GetThicknessValue(_rectThickness);
                    break;
                case AnnotationTool.Arrow:
                    thickness = GetThicknessValue(_arrowThickness);
                    break;
                case AnnotationTool.Mosaic:
                    thickness = GetMosaicBlockSize();
                    break;
                default:
                    thickness = 2.0;
                    break;
            }

            return new AnnotationToolContext
            {
                AnnotationCanvas = AnnotationCanvas,
                MosaicCanvas = MosaicCanvas,
                CurrentColor = color,
                CurrentColorBrush = new SolidColorBrush(color),
                CurrentThickness = thickness,
                SelectionBounds = _currentRect,
                MosaicBlockSize = (int)thickness,
                MosaicBackground = _mosaicBackground
            };
        }

        /// <summary>
        /// 从枚举获取颜色值
        /// </summary>
        private Color GetColorFromEnum(AnnotationColor colorEnum)
        {
            switch (colorEnum)
            {
                case AnnotationColor.Blue:
                    return Color.FromRgb(0, 120, 212);
                case AnnotationColor.Red:
                    return Color.FromRgb(232, 17, 35);
                case AnnotationColor.Black:
                    return Colors.Black;
                default:
                    return Colors.Blue;
            }
        }

        /// <summary>
        /// 从枚举获取粗细值
        /// </summary>
        private double GetThicknessValue(AnnotationThickness thicknessEnum)
        {
            switch (thicknessEnum)
            {
                case AnnotationThickness.Thin:
                    return 2;
                case AnnotationThickness.Medium:
                    return 4;
                case AnnotationThickness.Thick:
                    return 6;
                default:
                    return 4;
            }
        }

        private void Tool_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb)
            {
                // 取消选中时清除工具
                _currentTool = AnnotationTool.None;
                // 清除新架构工具
                _currentAnnotationTool?.OnDeselected();
                _currentAnnotationTool = null;
                PropertyPanel.Visibility = Visibility.Collapsed; // 隐藏属性栏
            }
        }

        private void UndoBtn_Click(object sender, RoutedEventArgs e)
        {
            // 使用新架构的上下文进行撤销
            _toolContext?.Undo();
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            // 强制当前焦点元素（如 TextBox）失去焦点，隐藏输入光标
            Keyboard.ClearFocus();

            // 隐藏所有不需要被截入的 UI 元素
            SelectionRect.Visibility = Visibility.Collapsed;
            SizeLabel.Visibility = Visibility.Collapsed;
            OuterMask.Visibility = Visibility.Collapsed;
            Toolbar.Visibility = Visibility.Collapsed;
            HideResizeAnchors(); // 隐藏锚点

            // 强制渲染，确保UI已经隐藏
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);

            if (BackgroundImage.Source is BitmapSource background)
            {
                // 1. 获取当前屏幕 DPI
                PresentationSource source = PresentationSource.FromVisual(this);
                double dpiX = 96.0, dpiY = 96.0;
                if (source?.CompositionTarget != null)
                {
                    dpiX = 96.0 * source.CompositionTarget.TransformToDevice.M11;
                    dpiY = 96.0 * source.CompositionTarget.TransformToDevice.M22;
                }

                // 2. 关键修复：直接计算【选区】的物理像素尺寸，而不是全屏！
                var physicalWidth = (int)(_currentRect.Width * dpiX / 96.0);
                var physicalHeight = (int)(_currentRect.Height * dpiY / 96.0);

                // 3. 创建正好等于【选区尺寸】的画布
                var renderBitmap = new RenderTargetBitmap(physicalWidth, physicalHeight, dpiX, dpiY, PixelFormats.Pbgra32);

                var drawingVisual = new DrawingVisual();
                using (var dc = drawingVisual.RenderOpen())
                {
                    // 4. 先用 CroppedBitmap 从原全屏背景图中把选区抠出来
                    // 使用 Math.Min 防止超出原背景图的物理边界
                    int cropX = (int)(_currentRect.Left * dpiX / 96.0);
                    int cropY = (int)(_currentRect.Top * dpiY / 96.0);
                    int cropW = Math.Min(physicalWidth, background.PixelWidth - cropX);
                    int cropH = Math.Min(physicalHeight, background.PixelHeight - cropY);

                    var bgCropRect = new Int32Rect(cropX, cropY, cropW, cropH);
                    var croppedBackground = new CroppedBitmap(background, bgCropRect);

                    // 把抠出来的背景图画在 (0,0)，使用物理像素尺寸
                    dc.DrawImage(croppedBackground, new Rect(0, 0, physicalWidth, physicalHeight));

                    // 5. 绘制马赛克层（底层）
                    if (MosaicCanvas.Visibility == Visibility.Visible && MosaicCanvas.Children.Count > 0)
                    {
                        dc.PushTransform(new TranslateTransform(-_currentRect.Left, -_currentRect.Top));

                        var mosaicBrush = new VisualBrush(MosaicCanvas)
                        {
                            Stretch = Stretch.None,
                            AlignmentX = AlignmentX.Left,
                            AlignmentY = AlignmentY.Top,
                            ViewboxUnits = BrushMappingMode.Absolute,
                            Viewbox = new Rect(0, 0, ActualWidth, ActualHeight),
                            ViewportUnits = BrushMappingMode.Absolute,
                            Viewport = new Rect(0, 0, ActualWidth, ActualHeight)
                        };

                        dc.DrawRectangle(mosaicBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));

                        dc.Pop();
                    }

                    // 6. 绘制标注层（顶层）
                    if (AnnotationCanvas.Visibility == Visibility.Visible)
                    {
                        // 将全屏的 AnnotationCanvas 向左上方平移，使其选区部分恰好落入 (0,0)
                        dc.PushTransform(new TranslateTransform(-_currentRect.Left, -_currentRect.Top));

                        var annotationBrush = new VisualBrush(AnnotationCanvas)
                        {
                            Stretch = Stretch.None, // 绝对不拉伸
                            AlignmentX = AlignmentX.Left,
                            AlignmentY = AlignmentY.Top,
                            
                            // ================= 【核心修复在这里】 =================
                            // 必须强制指定绝对视图区！
                            // 拒绝 VisualBrush 自动裁剪内容边界，强行捕获全屏坐标系，防止产生双倍偏移！
                            ViewboxUnits = BrushMappingMode.Absolute,
                            Viewbox = new Rect(0, 0, ActualWidth, ActualHeight),
                            ViewportUnits = BrushMappingMode.Absolute,
                            Viewport = new Rect(0, 0, ActualWidth, ActualHeight)
                            // ======================================================
                        };

                        // 画笔是全屏大小的，通过上面的平移，它会自动把对应的部分严丝合缝地填入背景之上
                        dc.DrawRectangle(annotationBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));

                        dc.Pop();
                    }
                }

                // 6. 渲染合成
                renderBitmap.Render(drawingVisual);

                // 7. 直接输出！不再需要用 CroppedBitmap 二次裁剪了
                CaptureCompletedWithImage?.Invoke(renderBitmap);
                return;
            }

            // 如果没有背景图的回退逻辑
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

                // 实时同步给上下文
                UpdateToolContextStyle();
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
                else if (_currentTool == AnnotationTool.Mosaic)
                    _mosaicThickness = thickness;

                // 实时同步给上下文
                UpdateToolContextStyle();
            }
        }

        private void ShowPropertyPanel()
        {
            if (_currentTool == AnnotationTool.None || _currentTool == AnnotationTool.Text || _currentTool == AnnotationTool.Mosaic)
            {
                PropertyPopup.IsOpen = false;
                return;
            }

            // 1. 先关闭
            PropertyPopup.IsOpen = false;

            // 2. 动态获取面板并确保外层容器和子元素都是可见的
            var border = PropertyPopup.Child as System.Windows.Controls.Border;
            if (border != null) border.Visibility = Visibility.Visible;

            var colorPanel = border?.Child as System.Windows.Controls.StackPanel;
            if (colorPanel != null)
            {
                colorPanel.Visibility = Visibility.Visible;
                colorPanel.Children[0].Visibility = Visibility.Visible; // 显示颜色选择
                colorPanel.Children[1].Visibility = Visibility.Visible; // 显示粗细选择
            }

            // 更新UI显示当前工具的设置
            UpdatePropertyPanelUI();

            // 3. 定位逻辑
            RadioButton currentToolBtn = null;
            if (_currentTool == AnnotationTool.Rectangle)
                currentToolBtn = RectToolBtn;
            else if (_currentTool == AnnotationTool.Arrow)
                currentToolBtn = ArrowToolBtn;

            if (currentToolBtn != null)
            {
                PropertyPopup.PlacementTarget = currentToolBtn;
                PropertyPopup.Placement = PlacementMode.Top;
                
                var offsetX = -30;
                var btnPos = currentToolBtn.TranslatePoint(new Point(0, 0), this);
                if (btnPos.X + offsetX < 0) offsetX = (int)(-btnPos.X);
                else if (btnPos.X + offsetX + 120 > ActualWidth) offsetX = (int)(ActualWidth - btnPos.X - 120);
                
                PropertyPopup.HorizontalOffset = offsetX;
                PropertyPopup.VerticalOffset = -10;
            }

            // 强制测量内部元素的最新尺寸
            PropertyPanel.UpdateLayout();

            // 4. 先打开 Popup（此时会生成底层的 HWND 窗口）
            PropertyPopup.IsOpen = true;

            // 5. 【核心修复】在打开状态下重置 Child
            // 这会触发底层 DestroyWindow 和 CreateWindow，彻底根据最新尺寸重绘，杜绝任何尺寸残留！
            var child = PropertyPopup.Child;
            PropertyPopup.Child = null;
            PropertyPopup.Child = child;
        }

        private void ShowPropertyPanelForMosaic()
        {
            if (_currentTool != AnnotationTool.Mosaic)
            {
                PropertyPopup.IsOpen = false;
                return;
            }

            // 1. 先关闭
            PropertyPopup.IsOpen = false;

            // 2. 动态获取面板
            var border = PropertyPopup.Child as System.Windows.Controls.Border;
            if (border != null) border.Visibility = Visibility.Visible;

            var colorPanel = border?.Child as System.Windows.Controls.StackPanel;
            if (colorPanel != null)
            {
                colorPanel.Visibility = Visibility.Visible;
                colorPanel.Children[0].Visibility = Visibility.Collapsed; // 隐藏颜色选择
                colorPanel.Children[1].Visibility = Visibility.Visible;   // 显示粗细选择
            }

            // 更新粗细选中状态
            ThicknessThin.IsChecked = _mosaicThickness == AnnotationThickness.Thin;
            ThicknessMedium.IsChecked = _mosaicThickness == AnnotationThickness.Medium;
            ThicknessThick.IsChecked = _mosaicThickness == AnnotationThickness.Thick;

            // 3. 定位逻辑
            PropertyPopup.PlacementTarget = MosaicToolBtn;
            PropertyPopup.Placement = PlacementMode.Top;

            var offsetX = -30;
            var btnPos = MosaicToolBtn.TranslatePoint(new Point(0, 0), this);
            if (btnPos.X + offsetX < 0) offsetX = (int)(-btnPos.X);
            else if (btnPos.X + offsetX + 120 > ActualWidth) offsetX = (int)(ActualWidth - btnPos.X - 120);

            PropertyPopup.HorizontalOffset = offsetX;
            PropertyPopup.VerticalOffset = -10;

            // 强制测量内部元素的最新尺寸
            PropertyPanel.UpdateLayout();

            // 4. 先打开 Popup
            PropertyPopup.IsOpen = true;

            // 5. 【核心修复】在打开状态下重置 Child，消除高度黑边/裁剪问题
            var child = PropertyPopup.Child;
            PropertyPopup.Child = null;
            PropertyPopup.Child = child;
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

        #region 选区调整锚点

        private void ShowResizeAnchors()
        {
            // 显示8个调整锚点
            ResizeTopLeft.Visibility = Visibility.Visible;
            ResizeTopRight.Visibility = Visibility.Visible;
            ResizeBottomLeft.Visibility = Visibility.Visible;
            ResizeBottomRight.Visibility = Visibility.Visible;
            ResizeTop.Visibility = Visibility.Visible;
            ResizeBottom.Visibility = Visibility.Visible;
            ResizeLeft.Visibility = Visibility.Visible;
            ResizeRight.Visibility = Visibility.Visible;

            // 更新锚点位置
            UpdateResizeAnchors();
        }

        private void HideResizeAnchors()
        {
            // 隐藏所有调整锚点
            ResizeTopLeft.Visibility = Visibility.Collapsed;
            ResizeTopRight.Visibility = Visibility.Collapsed;
            ResizeBottomLeft.Visibility = Visibility.Collapsed;
            ResizeBottomRight.Visibility = Visibility.Collapsed;
            ResizeTop.Visibility = Visibility.Collapsed;
            ResizeBottom.Visibility = Visibility.Collapsed;
            ResizeLeft.Visibility = Visibility.Collapsed;
            ResizeRight.Visibility = Visibility.Collapsed;
        }

        private void UpdateResizeAnchors()
        {
            var halfAnchor = 5; // 锚点宽度的一半

            // 四角锚点
            Canvas.SetLeft(ResizeTopLeft, _currentRect.Left - halfAnchor);
            Canvas.SetTop(ResizeTopLeft, _currentRect.Top - halfAnchor);

            Canvas.SetLeft(ResizeTopRight, _currentRect.Right - halfAnchor);
            Canvas.SetTop(ResizeTopRight, _currentRect.Top - halfAnchor);

            Canvas.SetLeft(ResizeBottomLeft, _currentRect.Left - halfAnchor);
            Canvas.SetTop(ResizeBottomLeft, _currentRect.Bottom - halfAnchor);

            Canvas.SetLeft(ResizeBottomRight, _currentRect.Right - halfAnchor);
            Canvas.SetTop(ResizeBottomRight, _currentRect.Bottom - halfAnchor);

            // 四边中点锚点
            Canvas.SetLeft(ResizeTop, _currentRect.Left + _currentRect.Width / 2 - halfAnchor);
            Canvas.SetTop(ResizeTop, _currentRect.Top - halfAnchor);

            Canvas.SetLeft(ResizeBottom, _currentRect.Left + _currentRect.Width / 2 - halfAnchor);
            Canvas.SetTop(ResizeBottom, _currentRect.Bottom - halfAnchor);

            Canvas.SetLeft(ResizeLeft, _currentRect.Left - halfAnchor);
            Canvas.SetTop(ResizeLeft, _currentRect.Top + _currentRect.Height / 2 - halfAnchor);

            Canvas.SetLeft(ResizeRight, _currentRect.Right - halfAnchor);
            Canvas.SetTop(ResizeRight, _currentRect.Top + _currentRect.Height / 2 - halfAnchor);
        }

        private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            var thumb = sender as Thumb;
            if (thumb == null) return;

            // 拖拽时隐藏工具栏和属性栏
            Toolbar.Visibility = Visibility.Collapsed;
            PropertyPopup.IsOpen = false;

            var pos = Mouse.GetPosition(SelectionCanvas);
            double mouseX = pos.X;
            double mouseY = pos.Y;

            // 使用绝对坐标彻底取代 dx/dy
            if (thumb == ResizeTopLeft) { _dragLeft = mouseX + _mouseOffsetX; _dragTop = mouseY + _mouseOffsetY; }
            else if (thumb == ResizeTopRight) { _dragRight = mouseX + _mouseOffsetX; _dragTop = mouseY + _mouseOffsetY; }
            else if (thumb == ResizeBottomLeft) { _dragLeft = mouseX + _mouseOffsetX; _dragBottom = mouseY + _mouseOffsetY; }
            else if (thumb == ResizeBottomRight) { _dragRight = mouseX + _mouseOffsetX; _dragBottom = mouseY + _mouseOffsetY; }
            else if (thumb == ResizeTop) { _dragTop = mouseY + _mouseOffsetY; }
            else if (thumb == ResizeBottom) { _dragBottom = mouseY + _mouseOffsetY; }
            else if (thumb == ResizeLeft) { _dragLeft = mouseX + _mouseOffsetX; }
            else if (thumb == ResizeRight) { _dragRight = mouseX + _mouseOffsetX; }

            double newLeft = Math.Min(_dragLeft, _dragRight);
            double newRight = Math.Max(_dragLeft, _dragRight);
            double newTop = Math.Min(_dragTop, _dragBottom);
            double newBottom = Math.Max(_dragTop, _dragBottom);

            // 改为 1 像素限制，解决固定边发抖
            if (newRight - newLeft < 1) newRight = newLeft + 1;
            if (newBottom - newTop < 1) newBottom = newTop + 1;

            double newWidth = newRight - newLeft;
            double newHeight = newBottom - newTop;

            // 屏幕边界约束
            if (newLeft < 0) { newWidth += newLeft; newLeft = 0; }
            if (newTop < 0) { newHeight += newTop; newTop = 0; }
            if (newLeft + newWidth > Width) newWidth = Width - newLeft;
            if (newTop + newHeight > Height) newHeight = Height - newTop;

            _currentRect = new Rect(newLeft, newTop, newWidth, newHeight);

            // 更新UI
            UpdateSelection(_currentRect.TopLeft, _currentRect.BottomRight);
            UpdateResizeAnchors();
            AnnotationCanvas.Clip = new RectangleGeometry(_currentRect);

            // 黑科技光标翻转
            bool isFlippedX = _dragLeft > _dragRight;
            bool isFlippedY = _dragTop > _dragBottom;
            if (thumb == ResizeTopLeft || thumb == ResizeBottomRight)
                thumb.Cursor = (isFlippedX ^ isFlippedY) ? Cursors.SizeNESW : Cursors.SizeNWSE;
            else if (thumb == ResizeTopRight || thumb == ResizeBottomLeft)
                thumb.Cursor = (isFlippedX ^ isFlippedY) ? Cursors.SizeNWSE : Cursors.SizeNESW;
        }

        private void ResizeThumb_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 锚点上的右键点击触发取消选区（Level 2）
            e.Handled = true;
            ResetToSelectingState();
        }

        private void ResizeThumb_DragStarted(object sender, DragStartedEventArgs e)
        {
            var thumb = sender as Thumb;
            if (thumb == null) return;

            var pos = Mouse.GetPosition(SelectionCanvas);
            _dragLeft = _currentRect.Left;
            _dragTop = _currentRect.Top;
            _dragRight = _currentRect.Right;
            _dragBottom = _currentRect.Bottom;

            // 记录鼠标相对于锚点的偏移量（用于绝对坐标拖拽）
            if (thumb == ResizeTopLeft) { _mouseOffsetX = _dragLeft - pos.X; _mouseOffsetY = _dragTop - pos.Y; }
            else if (thumb == ResizeTopRight) { _mouseOffsetX = _dragRight - pos.X; _mouseOffsetY = _dragTop - pos.Y; }
            else if (thumb == ResizeBottomLeft) { _mouseOffsetX = _dragLeft - pos.X; _mouseOffsetY = _dragBottom - pos.Y; }
            else if (thumb == ResizeBottomRight) { _mouseOffsetX = _dragRight - pos.X; _mouseOffsetY = _dragBottom - pos.Y; }
            else if (thumb == ResizeTop) { _mouseOffsetY = _dragTop - pos.Y; }
            else if (thumb == ResizeBottom) { _mouseOffsetY = _dragBottom - pos.Y; }
            else if (thumb == ResizeLeft) { _mouseOffsetX = _dragLeft - pos.X; }
            else if (thumb == ResizeRight) { _mouseOffsetX = _dragRight - pos.X; }

            Toolbar.Visibility = Visibility.Collapsed;
            PropertyPopup.IsOpen = false;
        }

        private void ResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            // 恢复正常光标
            ResizeTopLeft.Cursor = Cursors.SizeNWSE;
            ResizeBottomRight.Cursor = Cursors.SizeNWSE;
            ResizeTopRight.Cursor = Cursors.SizeNESW;
            ResizeBottomLeft.Cursor = Cursors.SizeNESW;

            // 拖拽结束后，ShowToolbar 会根据新的 _currentRect 重新定位工具栏
            ShowToolbar();

            // 如果之前处于某种绘图工具状态，应该恢复显示属性栏
            if (_currentTool == AnnotationTool.Rectangle || _currentTool == AnnotationTool.Arrow)
            {
                ShowPropertyPanel();
            }
            else if (_currentTool == AnnotationTool.Mosaic)
            {
                ShowPropertyPanelForMosaic();
            }
        }

        #endregion

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            CaptureCancelled?.Invoke();
        }

        #endregion
    }
}
