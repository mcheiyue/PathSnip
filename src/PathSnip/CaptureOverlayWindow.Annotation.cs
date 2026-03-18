using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PathSnip.Tools;

namespace PathSnip
{
    public partial class CaptureOverlayWindow
    {
        // 预渲染的马赛克背景（用于自由涂抹）
        private BitmapSource _mosaicBackground;
        private Rect _mosaicBackgroundSelection = Rect.Empty;
        private double _dpiScaleX = 1.0;
        private double _dpiScaleY = 1.0;

        #region 标注画布事件

        private void AnnotationCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            AnnotationCanvas.Focus();

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

        private static bool AreRectsEquivalent(Rect first, Rect second)
        {
            const double epsilon = 0.5;
            return Math.Abs(first.X - second.X) < epsilon &&
                   Math.Abs(first.Y - second.Y) < epsilon &&
                   Math.Abs(first.Width - second.Width) < epsilon &&
                   Math.Abs(first.Height - second.Height) < epsilon;
        }

        private void EnsureMosaicBackgroundForCurrentSelection()
        {
            if (_currentRect.Width <= 0 || _currentRect.Height <= 0)
            {
                return;
            }

            if (_mosaicBackground != null && AreRectsEquivalent(_mosaicBackgroundSelection, _currentRect))
            {
                return;
            }

            PrepareMosaicBackground(_currentRect);
        }

        private void PrepareMosaicBackground(Rect selectionBounds)
        {
            if (BackgroundImage.Source == null) return;
            if (selectionBounds.Width <= 0 || selectionBounds.Height <= 0) return;

            // 获取 DPI 缩放因子
            PresentationSource source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                _dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                _dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
            }

            var bg = BackgroundImage.Source as BitmapSource;
            if (bg == null) return;

            int cropX = Math.Max(0, (int)Math.Floor(selectionBounds.Left * _dpiScaleX));
            int cropY = Math.Max(0, (int)Math.Floor(selectionBounds.Top * _dpiScaleY));
            int cropWidth = (int)Math.Ceiling(selectionBounds.Width * _dpiScaleX);
            int cropHeight = (int)Math.Ceiling(selectionBounds.Height * _dpiScaleY);

            cropWidth = Math.Min(cropWidth, bg.PixelWidth - cropX);
            cropHeight = Math.Min(cropHeight, bg.PixelHeight - cropY);

            if (cropWidth <= 0 || cropHeight <= 0)
            {
                return;
            }

            var croppedBitmap = new CroppedBitmap(bg, new Int32Rect(cropX, cropY, cropWidth, cropHeight));

            // 缩放到较小尺寸
            int smallWidth = Math.Max(1, cropWidth / 32);
            int smallHeight = Math.Max(1, cropHeight / 32);

            var smallBitmap = new TransformedBitmap(croppedBitmap, new ScaleTransform(
                (double)smallWidth / cropWidth,
                (double)smallHeight / cropHeight));

            // 放大回来，使用邻近插值
            var renderBitmap = new TransformedBitmap(smallBitmap, new ScaleTransform(
                (double)cropWidth / smallWidth,
                (double)cropHeight / smallHeight));

            // 创建可用的位图
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawImage(renderBitmap, new Rect(0, 0, cropWidth, cropHeight));
            }

            var result = new RenderTargetBitmap(cropWidth, cropHeight, 96 * _dpiScaleX, 96 * _dpiScaleY, PixelFormats.Pbgra32);
            result.Render(visual);
            result.Freeze();

            _mosaicBackground = result;
            _mosaicBackgroundSelection = selectionBounds;

            if (_toolContext != null)
            {
                _toolContext.MosaicBackground = _mosaicBackground;
            }
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
                        _currentAnnotationTool = AnnotationToolFactory.Create(AnnotationType.Rectangle);
                        _currentAnnotationTool.OnSelected(_toolContext);
                        ShowPropertyPanel();
                        break;
                    case "Arrow":
                        _currentTool = AnnotationTool.Arrow;
                        UpdateToolContextStyle();
                        _currentAnnotationTool = AnnotationToolFactory.Create(AnnotationType.Arrow);
                        _currentAnnotationTool.OnSelected(_toolContext);
                        ShowPropertyPanel();
                        break;
                    case "Text":
                        _currentTool = AnnotationTool.Text;
                        UpdateToolContextStyle();
                        _currentAnnotationTool = AnnotationToolFactory.Create(AnnotationType.Text);
                        _currentAnnotationTool.OnSelected(_toolContext);
                        // 【核心修改】文字工具不需要属性栏，显式关闭
                        PropertyPopup.IsOpen = false;
                        break;
                    case "Highlighter":
                        _currentTool = AnnotationTool.Highlighter;
                        UpdateToolContextStyle();
                        _currentAnnotationTool = AnnotationToolFactory.Create(AnnotationType.Highlighter);
                        _currentAnnotationTool.OnSelected(_toolContext);
                        ShowPropertyPanel();
                        break;
                    case "Mosaic":
                        _currentTool = AnnotationTool.Mosaic;
                        UpdateToolContextStyle();
                        EnsureMosaicBackgroundForCurrentSelection();
                        if (_toolContext != null)
                        {
                            _toolContext.MosaicBackground = _mosaicBackground;
                        }

                        _currentAnnotationTool = AnnotationToolFactory.Create(AnnotationType.Mosaic);
                        _currentAnnotationTool.OnSelected(_toolContext);
                        ShowPropertyPanelForMosaic();
                        break;
                    case "Step":
                        _currentTool = AnnotationTool.Step;
                        UpdateToolContextStyle();
                        _currentAnnotationTool = AnnotationToolFactory.Create(AnnotationType.StepMarker);
                        _currentAnnotationTool.OnSelected(_toolContext);
                        // 【核心修改】步骤工具不需要属性栏，显式关闭
                        PropertyPopup.IsOpen = false;
                        break;
                }

                UpdateMoveSelectionThumbState();
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
                case AnnotationTool.Highlighter:
                    color = GetHighlighterColorFromEnum(_highlighterColor);
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
                case AnnotationTool.Highlighter:
                    thickness = GetHighlighterThicknessValue(_highlighterThickness);
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
                case AnnotationTool.Highlighter:
                    color = GetHighlighterColorFromEnum(_highlighterColor);
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
                case AnnotationTool.Highlighter:
                    thickness = GetHighlighterThicknessValue(_highlighterThickness);
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
                case AnnotationColor.Yellow:
                    return Color.FromRgb(255, 235, 59);
                case AnnotationColor.Green:
                    return Color.FromRgb(163, 230, 53);
                case AnnotationColor.Cyan:
                    return Color.FromRgb(34, 211, 238);
                case AnnotationColor.Pink:
                    return Color.FromRgb(244, 114, 182);
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

        private Color GetHighlighterColorFromEnum(AnnotationColor colorEnum)
        {
            var baseColor = GetColorFromEnum(colorEnum);
            return Color.FromArgb(HighlighterAlpha, baseColor.R, baseColor.G, baseColor.B);
        }

        private double GetHighlighterThicknessValue(AnnotationThickness thicknessEnum)
        {
            switch (thicknessEnum)
            {
                case AnnotationThickness.Thin:
                    return 8;
                case AnnotationThickness.Medium:
                    return 12;
                case AnnotationThickness.Thick:
                    return 16;
                default:
                    return 12;
            }
        }

        private static bool IsHighlighterPaletteColor(AnnotationColor color)
        {
            return color == AnnotationColor.Yellow
                || color == AnnotationColor.Green
                || color == AnnotationColor.Cyan
                || color == AnnotationColor.Pink;
        }

        private static bool IsAnnotationPaletteColor(AnnotationColor color)
        {
            return color == AnnotationColor.Blue
                || color == AnnotationColor.Red
                || color == AnnotationColor.Black;
        }

        private void UpdateColorPaletteVisibility()
        {
            if (_currentTool == AnnotationTool.Highlighter)
            {
                HighlighterColorGroup.Visibility = Visibility.Visible;
                AnnotationColorGroup.Visibility = Visibility.Collapsed;
                return;
            }

            HighlighterColorGroup.Visibility = Visibility.Collapsed;
            AnnotationColorGroup.Visibility = Visibility.Visible;
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
                _currentAnnotationTool?.OnDeselected();
                _currentAnnotationTool = null;

                UpdateMoveSelectionThumbState();

                // 【核心修改】不要在这里写 PropertyPopup.IsOpen = false; 或任何折叠代码！
            }
        }

        private void UndoBtn_Click(object sender, RoutedEventArgs e)
        {
            // 使用新架构的上下文进行撤销
            _toolContext?.Undo();
        }

        #endregion

        #region 颜色和粗细

        private Color GetCurrentColor()
        {
            // 获取当前工具对应的颜色
            AnnotationColor color;
            switch (_currentTool)
            {
                case AnnotationTool.Rectangle:
                    color = _rectColor;
                    break;
                case AnnotationTool.Arrow:
                    color = _arrowColor;
                    break;
                case AnnotationTool.Highlighter:
                    color = _highlighterColor;
                    break;
                default:
                    color = AnnotationColor.Blue;
                    break;
            }

            switch (color)
            {
                case AnnotationColor.Yellow:
                    return Color.FromRgb(255, 235, 59);
                case AnnotationColor.Green:
                    return Color.FromRgb(163, 230, 53);
                case AnnotationColor.Cyan:
                    return Color.FromRgb(34, 211, 238);
                case AnnotationColor.Pink:
                    return Color.FromRgb(244, 114, 182);
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
            var thickness = _currentTool == AnnotationTool.Rectangle
                ? _rectThickness
                : _currentTool == AnnotationTool.Arrow
                    ? _arrowThickness
                    : _highlighterThickness;

            switch (thickness)
            {
                case AnnotationThickness.Thin:
                    return _currentTool == AnnotationTool.Highlighter ? 8 : 1;
                case AnnotationThickness.Medium:
                    return _currentTool == AnnotationTool.Highlighter ? 12 : 2;
                case AnnotationThickness.Thick:
                    return _currentTool == AnnotationTool.Highlighter ? 16 : 4;
                default:
                    return _currentTool == AnnotationTool.Highlighter ? 12 : 2;
            }
        }

        private void ColorBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string colorName)
            {
                AnnotationColor color;
                switch (colorName)
                {
                    case "#FFEB3B":
                        color = AnnotationColor.Yellow;
                        break;
                    case "#A3E635":
                        color = AnnotationColor.Green;
                        break;
                    case "#22D3EE":
                        color = AnnotationColor.Cyan;
                        break;
                    case "#F472B6":
                        color = AnnotationColor.Pink;
                        break;
                    case "#0078D4":
                        color = AnnotationColor.Blue;
                        break;
                    case "#DC3545":
                        color = AnnotationColor.Red;
                        break;
                    case "#333333":
                        color = AnnotationColor.Black;
                        break;
                    default:
                        return;
                }

                // 保存到当前工具的属性
                if (_currentTool == AnnotationTool.Rectangle)
                {
                    if (!IsAnnotationPaletteColor(color)) return;
                    _rectColor = color;
                }
                else if (_currentTool == AnnotationTool.Arrow)
                {
                    if (!IsAnnotationPaletteColor(color)) return;
                    _arrowColor = color;
                }
                else if (_currentTool == AnnotationTool.Highlighter)
                {
                    if (!IsHighlighterPaletteColor(color)) return;
                    _highlighterColor = color;
                }
                else
                {
                    return;
                }

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
                else if (_currentTool == AnnotationTool.Highlighter)
                    _highlighterThickness = thickness;
                else if (_currentTool == AnnotationTool.Mosaic)
                    _mosaicThickness = thickness;

                // 实时同步给上下文
                UpdateToolContextStyle();
            }
        }

        private void ShowPropertyPanel()
        {
            // 1. 设置内容可见性
            var border = PropertyPopup.Child as Border;
            if (border != null) border.Visibility = Visibility.Visible;

            var colorPanel = border?.Child as StackPanel;
            if (colorPanel != null)
            {
                colorPanel.Children[0].Visibility = Visibility.Visible; // 颜色
                colorPanel.Children[1].Visibility = Visibility.Visible; // 粗细
            }

            UpdateColorPaletteVisibility();

            // 2. 更新UI
            UpdatePropertyPanelUI();

            // 3. 定位
            RadioButton currentToolBtn = null;
            if (_currentTool == AnnotationTool.Rectangle) currentToolBtn = RectToolBtn;
            else if (_currentTool == AnnotationTool.Arrow) currentToolBtn = ArrowToolBtn;
            else if (_currentTool == AnnotationTool.Highlighter) currentToolBtn = HighlighterToolBtn;

            if (currentToolBtn != null)
            {
                PropertyPopup.PlacementTarget = currentToolBtn;
                PropertyPopup.Placement = PlacementMode.Top;

                var offsetX = -30;
                var btnPos = currentToolBtn.TranslatePoint(new Point(0, 0), this);
                double popupWidth = Math.Max(120, PropertyPanel.ActualWidth);
                if (btnPos.X + offsetX < 0) offsetX = (int)(-btnPos.X);
                else if (btnPos.X + offsetX + popupWidth > ActualWidth) offsetX = (int)(ActualWidth - btnPos.X - popupWidth);

                PropertyPopup.HorizontalOffset = offsetX;
                PropertyPopup.VerticalOffset = -10;
            }

            // 4. 【核心修复】：如果是关闭的，打开它；如果本来就是打开的（从马赛克切过来），利用位移抖动强制 WPF 重新计算高度
            if (!PropertyPopup.IsOpen)
            {
                PropertyPopup.IsOpen = true;
            }

            // 轻微抖动 Offset 强制底层 HWND 根据现有的 2 行高度进行重绘计算
            PropertyPopup.HorizontalOffset += 1;
            PropertyPopup.HorizontalOffset -= 1;
        }

        private void ShowPropertyPanelForMosaic()
        {
            // 1. 设置内容可见性
            var border = PropertyPopup.Child as Border;
            if (border != null) border.Visibility = Visibility.Visible;

            var colorPanel = border?.Child as StackPanel;
            if (colorPanel != null)
            {
                colorPanel.Children[0].Visibility = Visibility.Collapsed; // 隐藏颜色
                colorPanel.Children[1].Visibility = Visibility.Visible; // 粗细
            }

            // 2. 更新UI
            ThicknessThin.IsChecked = _mosaicThickness == AnnotationThickness.Thin;
            ThicknessMedium.IsChecked = _mosaicThickness == AnnotationThickness.Medium;
            ThicknessThick.IsChecked = _mosaicThickness == AnnotationThickness.Thick;

            // 3. 定位
            PropertyPopup.PlacementTarget = MosaicToolBtn;
            PropertyPopup.Placement = PlacementMode.Top;

            var offsetX = -30;
            var btnPos = MosaicToolBtn.TranslatePoint(new Point(0, 0), this);
            double popupWidth = Math.Max(120, PropertyPanel.ActualWidth);
            if (btnPos.X + offsetX < 0) offsetX = (int)(-btnPos.X);
            else if (btnPos.X + offsetX + popupWidth > ActualWidth) offsetX = (int)(ActualWidth - btnPos.X - popupWidth);

            PropertyPopup.HorizontalOffset = offsetX;
            PropertyPopup.VerticalOffset = -10;

            // 4. 【核心修复】：保证打开，并抖动触发自适应缩放（变为 1 行高度）
            if (!PropertyPopup.IsOpen)
            {
                PropertyPopup.IsOpen = true;
            }

            // 轻微抖动
            PropertyPopup.HorizontalOffset += 1;
            PropertyPopup.HorizontalOffset -= 1;
        }

        private void UpdatePropertyPanelUI()
        {
            // 根据当前工具的设置更新UI选中状态
            AnnotationColor color;
            switch (_currentTool)
            {
                case AnnotationTool.Rectangle:
                    color = _rectColor;
                    break;
                case AnnotationTool.Arrow:
                    color = _arrowColor;
                    break;
                case AnnotationTool.Highlighter:
                    color = _highlighterColor;
                    break;
                default:
                    color = AnnotationColor.Blue;
                    break;
            }

            AnnotationThickness thickness;
            switch (_currentTool)
            {
                case AnnotationTool.Rectangle:
                    thickness = _rectThickness;
                    break;
                case AnnotationTool.Arrow:
                    thickness = _arrowThickness;
                    break;
                case AnnotationTool.Highlighter:
                    thickness = _highlighterThickness;
                    break;
                default:
                    thickness = AnnotationThickness.Medium;
                    break;
            }

            // 更新颜色选中状态
            ColorYellow.IsChecked = color == AnnotationColor.Yellow;
            ColorGreen.IsChecked = color == AnnotationColor.Green;
            ColorCyan.IsChecked = color == AnnotationColor.Cyan;
            ColorPink.IsChecked = color == AnnotationColor.Pink;
            ColorBlue.IsChecked = color == AnnotationColor.Blue;
            ColorRed.IsChecked = color == AnnotationColor.Red;
            ColorBlack.IsChecked = color == AnnotationColor.Black;

            UpdateColorPaletteVisibility();

            // 更新粗细选中状态
            ThicknessThin.IsChecked = thickness == AnnotationThickness.Thin;
            ThicknessMedium.IsChecked = thickness == AnnotationThickness.Medium;
            ThicknessThick.IsChecked = thickness == AnnotationThickness.Thick;
        }

        #endregion
    }
}
