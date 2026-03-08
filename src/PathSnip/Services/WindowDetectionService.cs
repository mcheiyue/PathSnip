using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PathSnip.Services
{
    /// <summary>
    /// 窗口检测服务 - 提供窗口枚举、边界获取、DPI转换等功能
    /// 用于智能窗口吸附功能
    /// </summary>
    public static class WindowDetectionService
    {
        #region Win32 API

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        /// <summary>
        /// 获取窗口扩展样式
        /// </summary>
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        /// <summary>
        /// DWM 获取窗口属性
        /// </summary>
        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        /// <summary>
        /// 窗口矩形结构
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        // 窗口样式常量
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        // DWM 属性常量
        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        private const int DWMWA_CLOAKED = 14;

        // GetAncestor 命令
        private const uint GA_ROOT = 2;

        #endregion

        #region 公共方法

        public static WindowTarget? GetWindowTargetUnderCursor(Point mousePosition, int excludeProcessId)
        {
            var dpiInfo = GetDpiScale();
            var point = new POINT
            {
                X = (int)(mousePosition.X * dpiInfo.ScaleX),
                Y = (int)(mousePosition.Y * dpiInfo.ScaleY)
            };

            IntPtr hWnd;
            RECT bounds;
            if (!TryGetWindowByPoint(point, excludeProcessId, out hWnd, out bounds) &&
                !TryGetWindowByEnum(point, excludeProcessId, out hWnd, out bounds))
            {
                return null;
            }

            var logicalBounds = new Rect(
                bounds.Left / dpiInfo.ScaleX,
                bounds.Top / dpiInfo.ScaleY,
                (bounds.Right - bounds.Left) / dpiInfo.ScaleX,
                (bounds.Bottom - bounds.Top) / dpiInfo.ScaleY);

            return new WindowTarget(hWnd, logicalBounds);
        }

        /// <summary>
        /// 获取当前鼠标位置的窗口边界（逻辑像素坐标）
        /// </summary>
        /// <param name="mousePosition">鼠标在虚拟屏幕上的逻辑坐标</param>
        /// <param name="excludeProcessId">要排除的进程ID（通常为当前程序）</param>
        /// <returns>窗口边界矩形，如未检测到则返回 null</returns>
        public static Rect? GetWindowUnderCursor(Point mousePosition, int excludeProcessId)
        {
            WindowTarget? windowTarget = GetWindowTargetUnderCursor(mousePosition, excludeProcessId);
            if (!windowTarget.HasValue)
            {
                return null;
            }

            return windowTarget.Value.Bounds;
        }

        private static bool TryGetWindowByPoint(POINT point, int excludeProcessId, out IntPtr hWnd, out RECT bounds)
        {
            hWnd = IntPtr.Zero;
            bounds = default(RECT);

            IntPtr targetWindow = WindowFromPoint(point);
            if (targetWindow == IntPtr.Zero)
                return false;

            targetWindow = GetAncestor(targetWindow, GA_ROOT);
            if (targetWindow == IntPtr.Zero)
                return false;

            if (!IsValidWindowCandidate(targetWindow, excludeProcessId))
                return false;

            if (!TryGetWindowBounds(targetWindow, out bounds))
                return false;

            if (!IsPointInRect(point, bounds))
                return false;

            hWnd = targetWindow;
            return true;
        }

        private static bool TryGetWindowByEnum(POINT point, int excludeProcessId, out IntPtr hWnd, out RECT bounds)
        {
            hWnd = IntPtr.Zero;
            bounds = default(RECT);
            IntPtr targetWindow = IntPtr.Zero;
            RECT targetBounds = default(RECT);

            EnumWindows((enumHwnd, lParam) =>
            {
                if (!IsValidWindowCandidate(enumHwnd, excludeProcessId))
                    return true;

                RECT candidateBounds;
                if (!TryGetWindowBounds(enumHwnd, out candidateBounds))
                    return true;

                if (!IsPointInRect(point, candidateBounds))
                    return true;

                targetWindow = enumHwnd;
                targetBounds = candidateBounds;
                return false;
            }, IntPtr.Zero);

            if (targetWindow == IntPtr.Zero)
                return false;

            hWnd = targetWindow;
            bounds = targetBounds;
            return true;
        }

        private static bool IsValidWindowCandidate(IntPtr hWnd, int excludeProcessId)
        {
            if (!IsWindowVisible(hWnd) || IsIconic(hWnd))
                return false;

            int cloaked;
            if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out cloaked, Marshal.SizeOf<int>()) == 0 && cloaked != 0)
                return false;

            GetWindowThreadProcessId(hWnd, out uint processId);
            if (processId == excludeProcessId)
                return false;

            int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
            if ((exStyle & WS_EX_TRANSPARENT) != 0)
                return false;

            return true;
        }

        private static bool TryGetWindowBounds(IntPtr hWnd, out RECT bounds)
        {
            if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out bounds, Marshal.SizeOf<RECT>()) == 0)
                return true;

            return GetWindowRect(hWnd, out bounds);
        }

        private static bool IsPointInRect(POINT point, RECT rect)
        {
            return point.X >= rect.Left &&
                   point.X <= rect.Right &&
                   point.Y >= rect.Top &&
                   point.Y <= rect.Bottom;
        }

        /// <summary>
        /// 获取当前进程的 ID
        /// </summary>
        public static int GetCurrentProcessId()
        {
            return System.Diagnostics.Process.GetCurrentProcess().Id;
        }

        /// <summary>
        /// 获取 DPI 缩放信息
        /// </summary>
        public static DpiScale GetDpiScale()
        {
            double scaleX = 1.0;
            double scaleY = 1.0;

            var mainWindow = Application.Current?.MainWindow;
            if (mainWindow != null)
            {
                var presentationSource = PresentationSource.FromVisual(mainWindow);
                if (presentationSource?.CompositionTarget != null)
                {
                    var transform = presentationSource.CompositionTarget.TransformToDevice;
                    scaleX = transform.M11;
                    scaleY = transform.M22;
                }
            }

            return new DpiScale(scaleX, scaleY);
        }

        #endregion
    }

    /// <summary>
    /// DPI 缩放信息
    /// </summary>
    public struct DpiScale
    {
        public double ScaleX { get; }
        public double ScaleY { get; }

        public DpiScale(double scaleX, double scaleY)
        {
            ScaleX = scaleX;
            ScaleY = scaleY;
        }
    }

    public struct WindowTarget
    {
        public IntPtr Hwnd { get; }

        public Rect Bounds { get; }

        public WindowTarget(IntPtr hwnd, Rect bounds)
        {
            Hwnd = hwnd;
            Bounds = bounds;
        }
    }
}
