using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using PathSnip.Helpers;
using PathSnip.Services.Overlay;
using PathSnip.Services.Snap;
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
        Highlighter,
        Step
    }

    public enum AnnotationColor
    {
        Yellow,
        Green,
        Cyan,
        Pink,
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
        private const byte HighlighterAlpha = 76;

        private Point _startPoint;
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
        private AnnotationColor _highlighterColor = AnnotationColor.Yellow;
        private AnnotationThickness _highlighterThickness = AnnotationThickness.Thick;
        private AnnotationThickness _mosaicThickness = AnnotationThickness.Medium;

        // 记录拖拽开始时的四个虚拟边界（用于反向拖拽翻转）
        private double _dragLeft, _dragTop, _dragRight, _dragBottom;

        // 记录鼠标相对于锚点的偏移量（用于绝对坐标拖拽）
        private double _mouseOffsetX;
        private double _mouseOffsetY;

        private bool _isDrawing;
        private bool _selectionCompleted;
        private readonly SelectionSession _selectionSession = new SelectionSession();
        private readonly SelectionBitmapComposer _selectionBitmapComposer = new SelectionBitmapComposer();
        private readonly OverlayShortcutHandler _shortcutHandler = new OverlayShortcutHandler();

        // 窗口吸附相关变量
        private DateTime _lastWindowDetectionTime = DateTime.MinValue;
        private readonly int _currentProcessId;
        private readonly SnapEngine _snapEngine = new SnapEngine(new ISnapProvider[] { new WindowSnapProvider() }, new UiaSnapProvider(), new MsaaSnapProvider());
        private SnapResult _currentSnapResult = SnapResult.None;
        private readonly DispatcherTimer _elementSnapHoverTimer;
        private CancellationTokenSource _elementSnapCts;
        private Point _lastHoverMousePosition;
        private const int ElementSnapHoverDelayMs = 40;
        private const int ElementSnapTimeoutMs = 100;
        private const int ElementLockGraceMs = 120;
        private const double ElementExitTolerancePx = 8;
        private const double FastPointerSpeedThreshold = 900;
        private readonly bool _enableSmartSnap;
        private readonly SmartSnapMode _smartSnapMode;
        private readonly bool _enableElementSnap;
        private readonly bool _holdAltToBypassSnap;
        private bool _isSnapBypassedByAlt;
        private SnapIntentMode _snapIntentMode = SnapIntentMode.WindowLock;
        private DateTime _elementLockUntil = DateTime.MinValue;
        private DateTime _lastPointerMoveAt = DateTime.MinValue;
        private Point _lastPointerScreenPoint;
        private double _pointerSpeedPxPerSecond;

        private enum SnapIntentMode
        {
            WindowLock,
            ElementProbe,
            ElementLock
        }

        // 放大镜相关变量
        private string _currentColorHex = "";
        private bool _isShowingCopyFeedback = false;
        private static readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private long _lastMagnifierUpdateTime;
        private Point _lastMagnifierPosition;
        private const int MagnifierThrottleMs = 16; // 约60fps
        private const double MagnifierMoveThreshold = 2; // 位移小于2像素不重算

        public event Action<Rect> CaptureCompleted;
        public event Action CaptureCancelled;

        public CaptureOverlayWindow(BitmapSource background)
        {
            InitializeComponent();

            Loaded += (s, e) =>
            {
                Focus();
                UpdateImeInterceptionState(Keyboard.FocusedElement);
            };

            AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnOverlayGotKeyboardFocus), true);

            AnnotationCanvas.Focusable = true;
            SelectionCanvas.Focusable = true;

            // 初始化当前进程ID（用于过滤自身窗口）
            _currentProcessId = WindowDetectionService.GetCurrentProcessId();

            var config = ConfigService.Instance;
            _enableSmartSnap = config.EnableSmartSnap;
            _smartSnapMode = config.SmartSnapMode;
            _enableElementSnap = config.EnableElementSnap;
            _holdAltToBypassSnap = config.HoldAltToBypassSnap;

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

            // 初始化放大镜
            // MagnifierUI.Visibility = Visibility.Visible; // 移至 Loaded 事件中

            MouseLeftButtonDown += OnMouseLeftButtonDown;
            MouseMove += OnMouseMove;
            MouseLeftButtonUp += OnMouseLeftButtonUp;
            MouseRightButtonDown += OnMouseRightButtonDown;

            _elementSnapHoverTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(ElementSnapHoverDelayMs)
            };
            _elementSnapHoverTimer.Tick += ElementSnapHoverTimer_Tick;

            Closed += (s, e) => StopElementSnapUpgrade();

            // 标注画布事件
            AnnotationCanvas.MouseLeftButtonDown += AnnotationCanvas_MouseLeftButtonDown;
            AnnotationCanvas.MouseMove += AnnotationCanvas_MouseMove;
            AnnotationCanvas.MouseLeftButtonUp += AnnotationCanvas_MouseLeftButtonUp;
        }

        private void OnOverlayGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            UpdateImeInterceptionState(e.NewFocus);
        }

        private void UpdateImeInterceptionState(IInputElement focusedElement)
        {
            var focusedObject = focusedElement as DependencyObject;
            bool shouldEnableIme = focusedObject is TextBoxBase;

            InputMethod.SetIsInputMethodEnabled(this, shouldEnableIme);

            if (shouldEnableIme && focusedObject != null)
            {
                InputMethod.SetIsInputMethodEnabled(focusedObject, true);
            }
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
            StopElementSnapUpgrade();
            _selectionSession.Reset();
            _currentSnapResult = SnapResult.None;
            _isSnapBypassedByAlt = false;
            _snapIntentMode = SnapIntentMode.WindowLock;
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
            UpdateIdleHintText();
            HintText.Visibility = Visibility.Visible;

            // 清除当前选区
            _currentRect = Rect.Empty;

            // 清除标注画布、马赛克画布和撤销栈（防止幽灵标注）
            AnnotationCanvas.Children.Clear();
            MosaicCanvas.Children.Clear();

            // 销毁上下文，下次框选重新生成干净的
            _toolContext = null;
            _mosaicBackground = null;
            _mosaicBackgroundSelection = Rect.Empty;
        }

        private void ResetToInitialState()
        {
            StopElementSnapUpgrade();
            _selectionSession.Reset();
            _currentSnapResult = SnapResult.None;
            _isSnapBypassedByAlt = false;
            _snapIntentMode = SnapIntentMode.WindowLock;
            _selectionCompleted = false;
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
            UpdateIdleHintText();
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
            _mosaicBackground = null;
            _mosaicBackgroundSelection = Rect.Empty;

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
            if (_selectionCompleted) return;

            StopElementSnapUpgrade();

            SelectionCanvas.Focus();
            _hasStartedSelection = true;

            Point startPoint = e.GetPosition(SelectionCanvas);
            Rect? potentialSnapRect = null;

            if (_currentSnapResult.IsValid && _currentSnapResult.Bounds.HasValue && WindowHighlightRect?.Visibility == Visibility.Visible)
            {
                potentialSnapRect = _currentSnapResult.Bounds;
                WindowHighlightRect.Visibility = Visibility.Collapsed;
            }

            _selectionSession.Begin(startPoint, potentialSnapRect);

            SelectionRect.Visibility = Visibility.Visible;
            SizeLabel.Visibility = Visibility.Visible;
            HintText.Visibility = Visibility.Collapsed;
            OuterMask.Visibility = Visibility.Visible;

            UpdateSelection(_selectionSession.StartPoint, _selectionSession.StartPoint);
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            var mousePos = e.GetPosition(this);
            var now = DateTime.Now;
            Point cursorScreenPoint = new Point(mousePos.X + Left, mousePos.Y + Top);
            UpdatePointerMotion(cursorScreenPoint, now);

            // 窗口吸附检测 + 放大镜更新（仅在未开始框选时）
            if (!_selectionSession.IsSelecting && !_selectionCompleted)
            {
                if (!_enableSmartSnap || _smartSnapMode == SmartSnapMode.ManualOnly)
                {
                    if (WindowHighlightRect != null)
                    {
                        WindowHighlightRect.Visibility = Visibility.Collapsed;
                    }

                    StopElementSnapUpgrade();
                    _currentSnapResult = SnapResult.None;
                    MagnifierUI.Visibility = Visibility.Visible;
                    return;
                }

                if (_isSnapBypassedByAlt)
                {
                    if (WindowHighlightRect != null)
                    {
                        WindowHighlightRect.Visibility = Visibility.Collapsed;
                    }

                    _currentSnapResult = SnapResult.None;
                    MagnifierUI.Visibility = Visibility.Visible;
                    return;
                }

                // 窗口吸附检测（50ms 节流）
                if ((now - _lastWindowDetectionTime).TotalMilliseconds >= 50)
                {
                    _lastWindowDetectionTime = now;
                    long requestVersion = _snapEngine.NextRequestVersion();
                    UpdateSnapTargetUnderCursor(e, requestVersion, now, cursorScreenPoint);
                }

                if (_smartSnapMode != SmartSnapMode.WindowOnly && _enableElementSnap)
                {
                    ScheduleElementSnapUpgrade(mousePos, cursorScreenPoint, now);
                }
                else
                {
                    StopElementSnapUpgrade();
                }

                // 显式恢复放大镜的可见性
                MagnifierUI.Visibility = Visibility.Visible;

                // 放大镜更新 - 节流优化
                var nowMs = _stopwatch.ElapsedMilliseconds;
                var timeSinceLastUpdate = nowMs - _lastMagnifierUpdateTime;
                var distance = Math.Sqrt(
                    Math.Pow(mousePos.X - _lastMagnifierPosition.X, 2) +
                    Math.Pow(mousePos.Y - _lastMagnifierPosition.Y, 2));

                if (timeSinceLastUpdate >= MagnifierThrottleMs || distance >= MagnifierMoveThreshold)
                {
                    _lastMagnifierUpdateTime = nowMs;
                    _lastMagnifierPosition = mousePos;
                    UpdateMagnifier(mousePos);
                }
                return;
            }

            // 隐藏窗口高亮框和放大镜
            if (WindowHighlightRect != null)
                WindowHighlightRect.Visibility = Visibility.Collapsed;

            MagnifierUI.Visibility = Visibility.Collapsed;
            StopElementSnapUpgrade();

            if (!_selectionSession.IsSelecting) return;

            var currentPoint = e.GetPosition(SelectionCanvas);

            _selectionSession.Update(currentPoint, 3);

            // 实时更新拉框
            if (_selectionSession.IsDragging || !_selectionSession.HasPotentialSnapRect)
            {
                UpdateSelection(_selectionSession.StartPoint, currentPoint);
            }
        }

        private void UpdateSnapTargetUnderCursor(MouseEventArgs e, long requestVersion, DateTime now, Point cursorScreenPoint)
        {
            if (!_snapEngine.IsCurrentRequest(requestVersion))
            {
                return;
            }

            if (ShouldKeepElementLock(cursorScreenPoint, now))
            {
                return;
            }

            SnapResult snapResult = _snapEngine.GetCurrentSnap(cursorScreenPoint, _currentProcessId);

            if (!_snapEngine.IsCurrentRequest(requestVersion))
            {
                return;
            }

            if (_currentSnapResult.IsValid && _currentSnapResult.Kind == SnapKind.Element &&
                snapResult.IsValid && snapResult.Kind == SnapKind.Window &&
                IsSameWindow(_currentSnapResult, snapResult) &&
                !IsFastPointerMotion())
            {
                _snapIntentMode = SnapIntentMode.ElementProbe;
                return;
            }

            _currentSnapResult = snapResult;
            _snapIntentMode = snapResult.Kind == SnapKind.Element ? SnapIntentMode.ElementLock : SnapIntentMode.WindowLock;
            ApplySnapHighlight(_currentSnapResult);
        }

        private void ScheduleElementSnapUpgrade(Point mousePos, Point cursorScreenPoint, DateTime now)
        {
            _lastHoverMousePosition = mousePos;

            if (_selectionSession.IsSelecting || _selectionCompleted)
            {
                return;
            }

            if (!_enableSmartSnap || !_enableElementSnap || _smartSnapMode == SmartSnapMode.WindowOnly || _smartSnapMode == SmartSnapMode.ManualOnly)
            {
                _elementSnapHoverTimer.Stop();
                CancelElementSnapRequest();
                return;
            }

            if (IsFastPointerMotion())
            {
                _snapIntentMode = SnapIntentMode.WindowLock;
                _elementSnapHoverTimer.Stop();
                CancelElementSnapRequest();
                return;
            }

            if (_currentSnapResult.IsValid && _currentSnapResult.Kind == SnapKind.Element)
            {
                Rect elementBounds = _currentSnapResult.Bounds.GetValueOrDefault(Rect.Empty);
                elementBounds.Inflate(ElementExitTolerancePx, ElementExitTolerancePx);
                if (elementBounds.Contains(cursorScreenPoint))
                {
                    _snapIntentMode = SnapIntentMode.ElementLock;
                    _elementLockUntil = now.AddMilliseconds(ElementLockGraceMs);
                }
                else
                {
                    _snapIntentMode = SnapIntentMode.ElementProbe;
                }
            }
            else
            {
                _snapIntentMode = SnapIntentMode.ElementProbe;
            }

            if (!_elementSnapHoverTimer.IsEnabled)
            {
                _elementSnapHoverTimer.Start();
            }
        }

        private async void ElementSnapHoverTimer_Tick(object sender, EventArgs e)
        {
            _elementSnapHoverTimer.Stop();

            if (_selectionSession.IsSelecting || _selectionCompleted)
            {
                return;
            }

            if (!_enableSmartSnap || !_enableElementSnap || _smartSnapMode == SmartSnapMode.WindowOnly || _smartSnapMode == SmartSnapMode.ManualOnly || _isSnapBypassedByAlt)
            {
                return;
            }

            if (IsFastPointerMotion())
            {
                return;
            }

            if (!_currentSnapResult.IsValid || !_currentSnapResult.Bounds.HasValue)
            {
                return;
            }

            CancelElementSnapRequest();
            var currentCts = new CancellationTokenSource();
            _elementSnapCts = currentCts;

            long requestVersion = _snapEngine.NextRequestVersion();
            Point screenPos = new Point(_lastHoverMousePosition.X + Left, _lastHoverMousePosition.Y + Top);

            try
            {
                SnapResult upgradedSnap = await _snapEngine.TryGetElementSnapAsync(
                    screenPos,
                    _currentProcessId,
                    _currentSnapResult,
                    requestVersion,
                    ElementSnapTimeoutMs,
                    currentCts.Token,
                    LogService.CreateOperationId("snap"));

                if (!_snapEngine.IsCurrentRequest(requestVersion))
                {
                    return;
                }

                if (_selectionSession.IsSelecting || _selectionCompleted)
                {
                    return;
                }

                if (!upgradedSnap.IsValid || !upgradedSnap.Bounds.HasValue)
                {
                    return;
                }

                _currentSnapResult = upgradedSnap;
                _snapIntentMode = SnapIntentMode.ElementLock;
                _elementLockUntil = DateTime.Now.AddMilliseconds(ElementLockGraceMs);
                ApplySnapHighlight(_currentSnapResult);
            }
            finally
            {
                if (ReferenceEquals(_elementSnapCts, currentCts))
                {
                    _elementSnapCts = null;
                }

                currentCts.Dispose();
            }
        }

        private void ApplySnapHighlight(SnapResult snapResult)
        {
            if (!snapResult.IsValid || !snapResult.Bounds.HasValue || WindowHighlightRect == null)
            {
                if (WindowHighlightRect != null)
                {
                    WindowHighlightRect.Visibility = Visibility.Collapsed;
                }

                return;
            }

            Rect rect = snapResult.Bounds.Value;
            var relativeRect = new Rect(
                rect.Left - Left,
                rect.Top - Top,
                rect.Width,
                rect.Height);

            Canvas.SetLeft(WindowHighlightRect, relativeRect.Left);
            Canvas.SetTop(WindowHighlightRect, relativeRect.Top);
            WindowHighlightRect.Width = relativeRect.Width;
            WindowHighlightRect.Height = relativeRect.Height;
            WindowHighlightRect.Visibility = Visibility.Visible;
        }

        private void StopElementSnapUpgrade()
        {
            _elementSnapHoverTimer.Stop();
            CancelElementSnapRequest();
            _snapIntentMode = SnapIntentMode.WindowLock;
            _elementLockUntil = DateTime.MinValue;
        }

        private void UpdateIdleHintText()
        {
            if (HintText == null)
            {
                return;
            }

            HintText.Text = "悬停吸附目标，拖拽可手动框选，右键或 ESC 取消";
        }

        private void CancelElementSnapRequest()
        {
            var cts = Interlocked.Exchange(ref _elementSnapCts, null);
            if (cts == null)
            {
                return;
            }

            cts.Cancel();
            cts.Dispose();
        }

        private void UpdateMagnifier(Point mousePos)
        {
            if (!(BackgroundImage.Source is BitmapSource bg)) return;

            // 获取当前像素颜色
            var color = MagnifierHelper.GetPixelColor(bg, mousePos.X, mousePos.Y, _dpiScaleX, _dpiScaleY);

            // 更新颜色显示
            ColorRGBText.Text = MagnifierHelper.ToRgbString(color);
            _currentColorHex = MagnifierHelper.ToHexString(color);
            
            // 如果正在显示复制成功的提示，则暂时不更新这行文字
            if (!_isShowingCopyFeedback)
            {
                ColorHexText.Text = "#" + _currentColorHex;
            }

            // 更新放大图像（25×25 像素区域，400% 放大后为 100×100）
            MagnifierImage.Source = MagnifierHelper.GetMagnifiedRegion(bg, mousePos.X, mousePos.Y, 40, _dpiScaleX, _dpiScaleY);

            // 更新放大镜位置（考虑屏幕边缘碰撞）
            UpdateMagnifierPosition(mousePos);
        }

        private void UpdateMagnifierPosition(Point mousePos)
        {
            double offsetX = 15;
            double offsetY = 15;
            double magnifierWidth = 160;
            double magnifierHeight = 245;

            double newLeft = mousePos.X + offsetX;
            double newTop = mousePos.Y + offsetY;

            // 检测右边缘碰撞
            if (newLeft + magnifierWidth > Width)
            {
                newLeft = mousePos.X - magnifierWidth - offsetX;
            }

            // 检测下边缘碰撞
            if (newTop + magnifierHeight > Height)
            {
                newTop = mousePos.Y - magnifierHeight - offsetY;
            }

            // 确保不超出左/上边界
            if (newLeft < 0) newLeft = 0;
            if (newTop < 0) newTop = 0;

            Canvas.SetLeft(MagnifierUI, newLeft);
            Canvas.SetTop(MagnifierUI, newTop);
        }

        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_selectionSession.IsSelecting) return;

            _selectionSession.Complete();

            // 情况A：用户单击了鼠标（没有触发拖拽），且刚才鼠标下有窗口
            if (_selectionSession.HasPotentialSnapRect && !_selectionSession.IsDragging)
            {
                Rect absoluteRect = _selectionSession.PotentialSnapRect.Value;
                _currentRect = new Rect(
                    absoluteRect.Left - Left,
                    absoluteRect.Top - Top,
                    absoluteRect.Width,
                    absoluteRect.Height);

                if (_currentRect.Width > 5 && _currentRect.Height > 5)
                {
                    _selectionCompleted = true;
                    UpdateSelection(_currentRect.TopLeft, _currentRect.BottomRight);
                    ShowToolbar();
                }
                else
                {
                    CaptureCancelled?.Invoke();
                }
                return;
            }
            
            // 情况B：用户手动拉框完毕
            var endPoint = e.GetPosition(SelectionCanvas);
            var rect = GetRect(_selectionSession.StartPoint, endPoint);

            if (rect.Width > 5 && rect.Height > 5)
            {
                _currentRect = rect;
                _selectionCompleted = true;
                UpdateSelection(_currentRect.TopLeft, _currentRect.BottomRight);
                ShowToolbar();
            }
            else
            {
                CaptureCancelled?.Invoke();
            }
        }

        private void ShowToolbar()
        {
            HintText.Visibility = Visibility.Collapsed;
            Toolbar.Visibility = Visibility.Visible;
            Toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Toolbar.Arrange(new Rect(Toolbar.DesiredSize));
            Toolbar.UpdateLayout();

            var toolbarWidth = Toolbar.DesiredSize.Width;
            var toolbarHeight = Toolbar.DesiredSize.Height;

            var toolbarLeft = _currentRect.Left + (_currentRect.Width - toolbarWidth) / 2;
            var toolbarTop = _currentRect.Bottom + 10;

            if (toolbarLeft < 10) toolbarLeft = 10;
            if (toolbarLeft + toolbarWidth + 10 > Width) toolbarLeft = Width - toolbarWidth - 10;

            if (toolbarTop + toolbarHeight + 10 > Height)
            {
                toolbarTop = _currentRect.Bottom - toolbarHeight - 10;
                if (toolbarTop < _currentRect.Top)
                {
                    toolbarTop = _currentRect.Top - toolbarHeight - 10;
                }
            }

            if (toolbarTop < 10) toolbarTop = 10;
            if (toolbarTop + toolbarHeight + 10 > Height) toolbarTop = Height - toolbarHeight - 10;

            Canvas.SetLeft(Toolbar, toolbarLeft);
            Canvas.SetTop(Toolbar, toolbarTop);

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

                if (_mosaicBackground != null && !AreRectsEquivalent(_mosaicBackgroundSelection, _currentRect))
                {
                    _mosaicBackground = null;
                    _mosaicBackgroundSelection = Rect.Empty;
                    _toolContext.MosaicBackground = null;
                }
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

        // 预渲染的马赛克背景（用于自由涂抹）
        private BitmapSource _mosaicBackground;
        private Rect _mosaicBackgroundSelection = Rect.Empty;
        private double _dpiScaleX = 1.0;
        private double _dpiScaleY = 1.0;

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

                // 【核心修改】不要在这里写 PropertyPopup.IsOpen = false; 或任何折叠代码！
            }
        }

        private void UndoBtn_Click(object sender, RoutedEventArgs e)
        {
            // 使用新架构的上下文进行撤销
            _toolContext?.Undo();
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            CancelCapture();
        }

        private void PinBtn_Click(object sender, RoutedEventArgs e)
        {
            var rect = _currentRect;
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return;
            }

            string operationId = LogService.CreateOperationId("pin");
            var pinWatch = Stopwatch.StartNew();
            LogService.LogInfo("capture.pin.start", $"rect=({rect.Left:F2},{rect.Top:F2},{rect.Width:F2},{rect.Height:F2})", operationId, "pin.start");

            try
            {
                var composedBitmap = ComposeCurrentSelectionBitmap();
                if (composedBitmap == null)
                {
                    LogService.LogWarn("capture.pin.background_missing", "背景图为空，贴图取消", operationId, "pin.fallback");
                    return;
                }

                double screenLeft = rect.Left + SystemParameters.VirtualScreenLeft;
                double screenTop = rect.Top + SystemParameters.VirtualScreenTop;

                var pinnedWindow = new PinnedImageWindow();
                pinnedWindow.SetImage(composedBitmap);
                pinnedWindow.SetBounds(screenLeft, screenTop, rect.Width, rect.Height);
                pinnedWindow.Show();

                pinWatch.Stop();
                LogService.LogInfo("capture.pin.completed", $"elapsedMs={pinWatch.ElapsedMilliseconds} size={composedBitmap.PixelWidth}x{composedBitmap.PixelHeight}", operationId, "pin.done");
                CancelCapture();
            }
            catch (Exception ex)
            {
                pinWatch.Stop();
                LogService.LogException("capture.pin.failed", ex, $"贴图失败 elapsedMs={pinWatch.ElapsedMilliseconds}", operationId, "pin.error");
            }
        }

        private async void Window_KeyDown(object sender, KeyEventArgs e)
        {
            OverlayShortcutAction action = _shortcutHandler.ResolveKeyDown(
                e.Key,
                e.ImeProcessedKey,
                CanHandlePinShortcut(),
                CanHandleColorCopyShortcut(),
                CanHandleCycleShortcut(),
                CanHandleBypassShortcut());

            if (await ExecuteShortcutActionAsync(action))
            {
                e.Handled = true;
            }
        }

        private async void Window_KeyUp(object sender, KeyEventArgs e)
        {
            OverlayShortcutAction action = _shortcutHandler.ResolveKeyUp(e.Key, CanHandleBypassShortcut());
            if (await ExecuteShortcutActionAsync(action))
            {
                e.Handled = true;
            }
        }

        private async void Window_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            OverlayShortcutAction action = _shortcutHandler.ResolveTextInput(
                e.Text,
                CanHandlePinShortcut(),
                CanHandleColorCopyShortcut());

            if (await ExecuteShortcutActionAsync(action))
            {
                e.Handled = true;
            }
        }

        private bool CanHandlePinShortcut()
        {
            if (Toolbar.Visibility != Visibility.Visible || _currentRect.Width <= 0 || _currentRect.Height <= 0)
            {
                return false;
            }

            return !(Keyboard.FocusedElement is TextBoxBase);
        }

        private bool CanHandleColorCopyShortcut()
        {
            if (MagnifierUI.Visibility != Visibility.Visible)
            {
                return false;
            }

            return !(Keyboard.FocusedElement is TextBoxBase);
        }

        private bool CanHandleCycleShortcut()
        {
            return !_selectionCompleted
                && !_selectionSession.IsSelecting
                && _enableSmartSnap
                && _smartSnapMode != SmartSnapMode.ManualOnly;
        }

        private bool CanHandleBypassShortcut()
        {
            return !_selectionCompleted && _holdAltToBypassSnap;
        }

        private async Task<bool> ExecuteShortcutActionAsync(OverlayShortcutAction action)
        {
            switch (action)
            {
                case OverlayShortcutAction.Cancel:
                    CancelCapture();
                    return true;

                case OverlayShortcutAction.Pin:
                    PinBtn_Click(this, new RoutedEventArgs());
                    return true;

                case OverlayShortcutAction.CopyColor:
                    await CopyCurrentColorAsync();
                    return true;

                case OverlayShortcutAction.CycleNext:
                    LogService.LogInfo("snap.cycle.tab", "cycle next requested", stage: "shortcut");
                    return await CycleSnapTargetAsync(true);

                case OverlayShortcutAction.CyclePrevious:
                    LogService.LogInfo("snap.cycle.shift_tab", "cycle previous requested", stage: "shortcut");
                    return await CycleSnapTargetAsync(false);

                case OverlayShortcutAction.BypassOn:
                    _isSnapBypassedByAlt = true;
                    StopElementSnapUpgrade();
                    if (WindowHighlightRect != null)
                    {
                        WindowHighlightRect.Visibility = Visibility.Collapsed;
                    }
                    LogService.LogInfo("snap.mode.bypass_alt", "alt bypass enabled", stage: "shortcut");
                    return true;

                case OverlayShortcutAction.BypassOff:
                    _isSnapBypassedByAlt = false;
                    LogService.LogInfo("snap.mode.bypass_alt", "alt bypass disabled", stage: "shortcut");
                    return true;

                default:
                    return false;
            }
        }

        private async Task<bool> CycleSnapTargetAsync(bool forward)
        {
            if (_selectionCompleted || _selectionSession.IsSelecting || _isSnapBypassedByAlt)
            {
                return false;
            }

            if (!_enableSmartSnap || _smartSnapMode == SmartSnapMode.ManualOnly)
            {
                return false;
            }

            if (_lastPointerMoveAt == DateTime.MinValue)
            {
                return false;
            }

            Point screenPoint = _lastPointerScreenPoint;

            if (_currentSnapResult.IsValid && _currentSnapResult.Kind == SnapKind.Element)
            {
                SnapResult windowSnap = _snapEngine.GetCurrentSnap(screenPoint, _currentProcessId);
                if (!windowSnap.IsValid)
                {
                    return false;
                }

                _currentSnapResult = windowSnap;
                ApplySnapHighlight(_currentSnapResult);
                return true;
            }

            if (!forward)
            {
                return false;
            }

            if (!_enableElementSnap || _smartSnapMode == SmartSnapMode.WindowOnly)
            {
                return false;
            }

            if (!_currentSnapResult.IsValid)
            {
                _currentSnapResult = _snapEngine.GetCurrentSnap(screenPoint, _currentProcessId);
                if (!_currentSnapResult.IsValid)
                {
                    return false;
                }
            }

            long requestVersion = _snapEngine.NextRequestVersion();
            SnapResult upgradedSnap = await _snapEngine.TryGetElementSnapAsync(
                screenPoint,
                _currentProcessId,
                _currentSnapResult,
                requestVersion,
                ElementSnapTimeoutMs,
                CancellationToken.None,
                LogService.CreateOperationId("snap"));

            if (!_snapEngine.IsCurrentRequest(requestVersion) || !upgradedSnap.IsValid || !upgradedSnap.Bounds.HasValue)
            {
                return false;
            }

            _currentSnapResult = upgradedSnap;
            ApplySnapHighlight(_currentSnapResult);
            return true;
        }

        private async Task CopyCurrentColorAsync()
        {
            if (string.IsNullOrEmpty(_currentColorHex))
            {
                return;
            }

            string hexWithPrefix = "#" + _currentColorHex;
            string operationId = LogService.CreateOperationId("clip");

            ShowColorCopyFeedback("复制中...");
            bool copied = await ClipboardService.TrySetTextAsync(hexWithPrefix, operationId);

            if (copied)
            {
                System.Media.SystemSounds.Asterisk.Play();
                ShowColorCopyFeedback("已复制!");
                return;
            }

            ShowColorCopyFeedback("复制失败");
            LogService.LogWarn("capture.color_copy.failed", "色值复制失败", operationId, "color.copy");
        }

        private void ShowColorCopyFeedback(string text)
        {
            _isShowingCopyFeedback = true;
            ColorHexText.Text = text;

            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(800)
            };

            timer.Tick += (s, args) =>
            {
                _isShowingCopyFeedback = false;
                ColorHexText.Text = "#" + _currentColorHex;
                timer.Stop();
            };

            timer.Start();
        }

        private void CancelCapture()
        {
            this.Focus();
            StopElementSnapUpgrade();
            _selectionSession.Reset();
            _currentSnapResult = SnapResult.None;
            _isSnapBypassedByAlt = false;
            _snapIntentMode = SnapIntentMode.WindowLock;
            SelectionRect.Visibility = Visibility.Collapsed;
            SizeLabel.Visibility = Visibility.Collapsed;
            OuterMask.Visibility = Visibility.Collapsed;
            Toolbar.Visibility = Visibility.Collapsed;
            PropertyPopup.IsOpen = false;
            AnnotationCanvas.Visibility = Visibility.Collapsed;
            MosaicCanvas.Visibility = Visibility.Collapsed;
            HideResizeAnchors();
            UpdateIdleHintText();
            HintText.Visibility = Visibility.Visible;

            CaptureCancelled?.Invoke();
        }

        private void UpdatePointerMotion(Point cursorScreenPoint, DateTime now)
        {
            if (_lastPointerMoveAt != DateTime.MinValue)
            {
                double elapsedMs = (now - _lastPointerMoveAt).TotalMilliseconds;
                if (elapsedMs > 0)
                {
                    double dx = cursorScreenPoint.X - _lastPointerScreenPoint.X;
                    double dy = cursorScreenPoint.Y - _lastPointerScreenPoint.Y;
                    double distance = Math.Sqrt(dx * dx + dy * dy);
                    _pointerSpeedPxPerSecond = distance * 1000 / elapsedMs;
                }
            }

            _lastPointerMoveAt = now;
            _lastPointerScreenPoint = cursorScreenPoint;
        }

        private bool IsFastPointerMotion()
        {
            return _pointerSpeedPxPerSecond >= FastPointerSpeedThreshold;
        }

        private bool ShouldKeepElementLock(Point cursorScreenPoint, DateTime now)
        {
            if (_currentSnapResult.Kind != SnapKind.Element || !_currentSnapResult.Bounds.HasValue)
            {
                return false;
            }

            Rect bounds = _currentSnapResult.Bounds.Value;
            Rect toleratedBounds = bounds;
            toleratedBounds.Inflate(ElementExitTolerancePx, ElementExitTolerancePx);

            if (toleratedBounds.Contains(cursorScreenPoint))
            {
                _snapIntentMode = SnapIntentMode.ElementLock;
                _elementLockUntil = now.AddMilliseconds(ElementLockGraceMs);
                return true;
            }

            if (_snapIntentMode == SnapIntentMode.ElementLock && now <= _elementLockUntil)
            {
                return true;
            }

            return false;
        }

        private static bool IsSameWindow(SnapResult left, SnapResult right)
        {
            if (!left.WindowHandle.HasValue || !right.WindowHandle.HasValue)
            {
                return false;
            }

            IntPtr leftHandle = left.WindowHandle.Value;
            IntPtr rightHandle = right.WindowHandle.Value;
            if (leftHandle == IntPtr.Zero || rightHandle == IntPtr.Zero)
            {
                return false;
            }

            return leftHandle == rightHandle;
        }

        private void HideFinalizeChrome()
        {
            this.Focus();
            SelectionRect.Visibility = Visibility.Collapsed;
            SizeLabel.Visibility = Visibility.Collapsed;
            OuterMask.Visibility = Visibility.Collapsed;
            Toolbar.Visibility = Visibility.Collapsed;
            PropertyPopup.IsOpen = false;
            HideResizeAnchors();
        }

        private BitmapSource ComposeCurrentSelectionBitmap()
        {
            if (!(BackgroundImage.Source is BitmapSource background))
            {
                return null;
            }

            PresentationSource source = PresentationSource.FromVisual(this);
            double dpiX = 96.0;
            double dpiY = 96.0;
            if (source?.CompositionTarget != null)
            {
                dpiX = 96.0 * source.CompositionTarget.TransformToDevice.M11;
                dpiY = 96.0 * source.CompositionTarget.TransformToDevice.M22;
            }

            var request = new SelectionBitmapComposeRequest
            {
                Background = background,
                MosaicCanvas = MosaicCanvas,
                AnnotationCanvas = AnnotationCanvas,
                SelectionRect = _currentRect,
                DpiX = dpiX,
                DpiY = dpiY,
                SurfaceWidth = ActualWidth,
                SurfaceHeight = ActualHeight
            };

            return _selectionBitmapComposer.Compose(request);
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            string operationId = LogService.CreateOperationId("save");
            var composeWatch = Stopwatch.StartNew();
            LogService.LogInfo("capture.save.start", $"rect=({_currentRect.Left:F2},{_currentRect.Top:F2},{_currentRect.Width:F2},{_currentRect.Height:F2})", operationId, "compose.start");

            this.Focus();

            try
            {
                HideFinalizeChrome();

                // 强制渲染，确保UI已经隐藏
                Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);

                var renderBitmap = ComposeCurrentSelectionBitmap();
                if (renderBitmap != null)
                {
                    composeWatch.Stop();
                    LogService.LogInfo("capture.save.compose_completed", $"elapsedMs={composeWatch.ElapsedMilliseconds} size={renderBitmap.PixelWidth}x{renderBitmap.PixelHeight}", operationId, "compose.done");

                    CaptureCompletedWithImage?.Invoke(renderBitmap, operationId);
                    return;
                }

                // 如果没有背景图的回退逻辑
                var screenRect = new Rect(
                    _currentRect.Left + Left,
                    _currentRect.Top + Top,
                    _currentRect.Width,
                    _currentRect.Height);

                composeWatch.Stop();
                LogService.LogWarn("capture.save.background_missing", "背景图为空，回退屏幕区域保存", operationId, "compose.fallback");
                CaptureCompleted?.Invoke(screenRect);
            }
            catch (Exception ex)
            {
                composeWatch.Stop();
                LogService.LogException("capture.save.failed", ex, $"保存失败 elapsedMs={composeWatch.ElapsedMilliseconds}", operationId, "compose.error");
                CaptureCancelled?.Invoke();
            }
        }

        public event Action<BitmapSource, string> CaptureCompletedWithImage;

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
            var border = PropertyPopup.Child as System.Windows.Controls.Border;
            if (border != null) border.Visibility = Visibility.Visible;

            var colorPanel = border?.Child as System.Windows.Controls.StackPanel;
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
            var border = PropertyPopup.Child as System.Windows.Controls.Border;
            if (border != null) border.Visibility = Visibility.Visible;

            var colorPanel = border?.Child as System.Windows.Controls.StackPanel;
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
            else if (_currentTool == AnnotationTool.Highlighter)
            {
                ShowPropertyPanel();
            }
            else if (_currentTool == AnnotationTool.Mosaic)
            {
                ShowPropertyPanelForMosaic();
            }
        }

        #endregion

        #endregion
    }
}
