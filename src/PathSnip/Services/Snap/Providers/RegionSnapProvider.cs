using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace PathSnip.Services.Snap
{
    public sealed class RegionSnapProvider
    {
        private const int DwmaExtendedFrameBounds = 9;

        public SnapResult GetCurrentRegionSnap(Point screenPoint, int currentProcessId, SnapResult windowSnap)
        {
            if (!windowSnap.IsValid || !windowSnap.Bounds.HasValue)
            {
                return SnapResult.None;
            }

            IntPtr windowHandle = windowSnap.WindowHandle.GetValueOrDefault(IntPtr.Zero);
            if (windowHandle == IntPtr.Zero)
            {
                return SnapResult.None;
            }

            Rect logicalWindowBounds = windowSnap.Bounds.Value;
            Rect physicalWindowBounds;
            if (!TryGetPhysicalWindowBounds(windowHandle, out physicalWindowBounds))
            {
                return SnapResult.None;
            }

            if (logicalWindowBounds.IsEmpty || logicalWindowBounds.Width <= 0 || logicalWindowBounds.Height <= 0)
            {
                return SnapResult.None;
            }

            double scaleX = physicalWindowBounds.Width / logicalWindowBounds.Width;
            double scaleY = physicalWindowBounds.Height / logicalWindowBounds.Height;
            if (!IsValidScale(scaleX) || !IsValidScale(scaleY))
            {
                return SnapResult.None;
            }

            Point physicalPoint = new Point(
                physicalWindowBounds.Left + (screenPoint.X - logicalWindowBounds.Left) * scaleX,
                physicalWindowBounds.Top + (screenPoint.Y - logicalWindowBounds.Top) * scaleY);

            IntPtr regionHandle = ResolveRegionHwnd(windowHandle, physicalPoint);
            Rect physicalRegionBounds;

            if (regionHandle != IntPtr.Zero && regionHandle != windowHandle)
            {
                RECT rect;
                if (!GetWindowRect(regionHandle, out rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
                {
                    return SnapResult.None;
                }

                physicalRegionBounds = new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            }
            else
            {
                Rect clientBounds;
                if (!TryGetClientBounds(windowHandle, out clientBounds))
                {
                    return SnapResult.None;
                }

                physicalRegionBounds = clientBounds;
            }

            Rect logicalRegionBounds = new Rect(
                logicalWindowBounds.Left + (physicalRegionBounds.Left - physicalWindowBounds.Left) / scaleX,
                logicalWindowBounds.Top + (physicalRegionBounds.Top - physicalWindowBounds.Top) / scaleY,
                physicalRegionBounds.Width / scaleX,
                physicalRegionBounds.Height / scaleY);

            if (logicalRegionBounds.IsEmpty || logicalRegionBounds.Width <= 0 || logicalRegionBounds.Height <= 0)
            {
                return SnapResult.None;
            }

            RegionCandidate regionCandidate = new RegionCandidate(
                logicalRegionBounds,
                string.Empty,
                RegionKind.Unknown,
                SnapSource.WindowDetection,
                0.7,
                windowHandle);

            return SnapResult.FromRegion(regionCandidate);
        }

        private static IntPtr ResolveRegionHwnd(IntPtr topWindowHandle, Point physicalPoint)
        {
            var pt = new POINT
            {
                X = (int)Math.Round(physicalPoint.X),
                Y = (int)Math.Round(physicalPoint.Y)
            };

            IntPtr hit = WindowFromPoint(pt);
            if (hit == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            IntPtr root = GetAncestor(hit, GA_ROOT);
            if (root == IntPtr.Zero || root != topWindowHandle)
            {
                return IntPtr.Zero;
            }

            return hit;
        }

        private static bool TryGetPhysicalWindowBounds(IntPtr hwnd, out Rect bounds)
        {
            bounds = Rect.Empty;

            RECT dwmRect;
            int hr = DwmGetWindowAttribute(hwnd, DwmaExtendedFrameBounds, out dwmRect, Marshal.SizeOf(typeof(RECT)));
            if (hr == 0 && dwmRect.Right > dwmRect.Left && dwmRect.Bottom > dwmRect.Top)
            {
                bounds = new Rect(dwmRect.Left, dwmRect.Top, dwmRect.Right - dwmRect.Left, dwmRect.Bottom - dwmRect.Top);
                return true;
            }

            RECT rawRect;
            if (GetWindowRect(hwnd, out rawRect) && rawRect.Right > rawRect.Left && rawRect.Bottom > rawRect.Top)
            {
                bounds = new Rect(rawRect.Left, rawRect.Top, rawRect.Right - rawRect.Left, rawRect.Bottom - rawRect.Top);
                return true;
            }

            return false;
        }

        private static bool TryGetClientBounds(IntPtr hwnd, out Rect bounds)
        {
            bounds = Rect.Empty;

            RECT client;
            if (!GetClientRect(hwnd, out client))
            {
                return false;
            }

            var origin = new POINT { X = client.Left, Y = client.Top };
            if (!ClientToScreen(hwnd, ref origin))
            {
                return false;
            }

            int width = client.Right - client.Left;
            int height = client.Bottom - client.Top;
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            bounds = new Rect(origin.X, origin.Y, width, height);
            return true;
        }

        private static bool IsValidScale(double scale)
        {
            return scale > 0 && scale <= 4;
        }

        private const uint GA_ROOT = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);
    }
}
