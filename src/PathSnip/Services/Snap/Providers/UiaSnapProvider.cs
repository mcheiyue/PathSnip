using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;

namespace PathSnip.Services.Snap
{
    public sealed class UiaSnapProvider
    {
        private const int DwmaExtendedFrameBounds = 9;
        private const double MinElementSize = 6;
        private const double WindowBoundsTolerance = 2;
        private const double MaxBoundsAreaScale = 1.02;
        private const int MaxTraversalDepth = 8;

        public Task<SnapResult> GetCurrentSnapAsync(Point screenPoint, int currentProcessId, SnapResult currentSnap, CancellationToken cancellationToken)
        {
            if (!currentSnap.IsValid || !currentSnap.Bounds.HasValue)
            {
                return Task.FromResult(SnapResult.None);
            }

            Rect windowBounds = currentSnap.Bounds.Value;
            if (!windowBounds.Contains(screenPoint))
            {
                return Task.FromResult(SnapResult.None);
            }

            return Task.Run(() => ResolveElementSnap(screenPoint, currentProcessId, currentSnap, cancellationToken), cancellationToken);
        }

        private static SnapResult ResolveElementSnap(Point screenPoint, int currentProcessId, SnapResult currentSnap, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Rect logicalWindowBounds = currentSnap.Bounds.Value;
            if (!TryBuildCoordinateContext(currentSnap, screenPoint, out Point physicalPoint, out Rect physicalWindowBounds, out double scaleX, out double scaleY))
            {
                return SnapResult.None;
            }

            AutomationElement element = null;
            IntPtr? currentWindowHandle = currentSnap.WindowHandle;
            if (currentWindowHandle.HasValue && currentWindowHandle.Value != IntPtr.Zero)
            {
                try
                {
                    element = AutomationElement.FromHandle(currentWindowHandle.Value);
                }
                catch (ElementNotAvailableException)
                {
                }
                catch (InvalidOperationException)
                {
                }
                catch (COMException)
                {
                }
            }

            if (element == null)
            {
                try
                {
                    element = AutomationElement.FromPoint(physicalPoint);
                }
                catch (ElementNotAvailableException)
                {
                    return SnapResult.None;
                }
                catch (InvalidOperationException)
                {
                    return SnapResult.None;
                }
                catch (COMException)
                {
                    return SnapResult.None;
                }
            }

            if (element == null)
            {
                return SnapResult.None;
            }

            element = ResolveDeepestElementAtPoint(element, physicalPoint, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            int elementProcessId;
            try
            {
                elementProcessId = element.Current.ProcessId;
            }
            catch (ElementNotAvailableException)
            {
                return SnapResult.None;
            }
            catch (InvalidOperationException)
            {
                return SnapResult.None;
            }

            if (elementProcessId == currentProcessId)
            {
                return SnapResult.None;
            }

            Rect bounds;
            try
            {
                bounds = element.Current.BoundingRectangle;
            }
            catch (ElementNotAvailableException)
            {
                return SnapResult.None;
            }
            catch (InvalidOperationException)
            {
                return SnapResult.None;
            }

            if (bounds.IsEmpty || bounds.Width < MinElementSize || bounds.Height < MinElementSize)
            {
                return SnapResult.None;
            }

            if (!bounds.Contains(physicalPoint))
            {
                return SnapResult.None;
            }

            if (!physicalWindowBounds.IntersectsWith(bounds))
            {
                return SnapResult.None;
            }

            if (!IsElementBoundsInsideWindow(bounds, physicalWindowBounds))
            {
                return SnapResult.None;
            }

            string label = string.Empty;
            try
            {
                label = element.Current.Name ?? string.Empty;
            }
            catch (ElementNotAvailableException)
            {
            }
            catch (InvalidOperationException)
            {
            }

            IntPtr? resultWindowHandle = currentSnap.WindowHandle;
            if (!resultWindowHandle.HasValue || resultWindowHandle.Value == IntPtr.Zero)
            {
                try
                {
                    int nativeHandle = element.Current.NativeWindowHandle;
                    if (nativeHandle != 0)
                    {
                        resultWindowHandle = new IntPtr(nativeHandle);
                    }
                }
                catch (ElementNotAvailableException)
                {
                }
                catch (InvalidOperationException)
                {
                }
            }

            Rect logicalBounds = ConvertToLogicalBounds(bounds, physicalWindowBounds, logicalWindowBounds, scaleX, scaleY);
            return new SnapResult(logicalBounds, label, true, SnapKind.Element, SnapSource.UIA, 0.85, resultWindowHandle);
        }

        private static AutomationElement ResolveDeepestElementAtPoint(AutomationElement rootElement, Point physicalPoint, CancellationToken cancellationToken)
        {
            AutomationElement current = rootElement;
            for (int depth = 0; depth < MaxTraversalDepth; depth++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                AutomationElement next = FindBestChildContainingPoint(current, physicalPoint, cancellationToken);
                if (next == null)
                {
                    break;
                }

                current = next;
            }

            return current;
        }

        private static AutomationElement FindBestChildContainingPoint(AutomationElement parent, Point physicalPoint, CancellationToken cancellationToken)
        {
            AutomationElement child;
            try
            {
                child = TreeWalker.RawViewWalker.GetFirstChild(parent);
            }
            catch (ElementNotAvailableException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }

            AutomationElement bestChild = null;
            double bestArea = double.MaxValue;

            while (child != null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Rect childBounds;
                if (TryGetBounds(child, out childBounds) && !childBounds.IsEmpty && childBounds.Contains(physicalPoint))
                {
                    double area = Math.Max(1, childBounds.Width * childBounds.Height);
                    if (area < bestArea)
                    {
                        bestArea = area;
                        bestChild = child;
                    }
                }

                try
                {
                    child = TreeWalker.RawViewWalker.GetNextSibling(child);
                }
                catch (ElementNotAvailableException)
                {
                    break;
                }
                catch (InvalidOperationException)
                {
                    break;
                }
            }

            return bestChild;
        }

        private static bool TryGetBounds(AutomationElement element, out Rect bounds)
        {
            try
            {
                bounds = element.Current.BoundingRectangle;
                return true;
            }
            catch (ElementNotAvailableException)
            {
                bounds = Rect.Empty;
                return false;
            }
            catch (InvalidOperationException)
            {
                bounds = Rect.Empty;
                return false;
            }
        }

        private static bool IsElementBoundsInsideWindow(Rect elementBounds, Rect windowBounds)
        {
            Rect toleratedWindowBounds = windowBounds;
            toleratedWindowBounds.Inflate(WindowBoundsTolerance, WindowBoundsTolerance);
            if (elementBounds.Left < toleratedWindowBounds.Left ||
                elementBounds.Top < toleratedWindowBounds.Top ||
                elementBounds.Right > toleratedWindowBounds.Right ||
                elementBounds.Bottom > toleratedWindowBounds.Bottom)
            {
                return false;
            }

            double windowArea = Math.Max(1, windowBounds.Width * windowBounds.Height);
            double elementArea = Math.Max(1, elementBounds.Width * elementBounds.Height);
            if (elementArea > windowArea * MaxBoundsAreaScale)
            {
                return false;
            }

            return true;
        }

        private static bool TryBuildCoordinateContext(
            SnapResult currentSnap,
            Point logicalPoint,
            out Point physicalPoint,
            out Rect physicalWindowBounds,
            out double scaleX,
            out double scaleY)
        {
            Rect logicalWindowBounds = currentSnap.Bounds.GetValueOrDefault(Rect.Empty);
            scaleX = 1;
            scaleY = 1;

            IntPtr hwnd = currentSnap.WindowHandle.GetValueOrDefault(IntPtr.Zero);
            Rect physicalBounds;
            if (hwnd != IntPtr.Zero && TryGetPhysicalWindowBounds(hwnd, out physicalBounds))
            {
                physicalWindowBounds = physicalBounds;

                if (!logicalWindowBounds.IsEmpty && logicalWindowBounds.Width > 0 && logicalWindowBounds.Height > 0)
                {
                    scaleX = physicalWindowBounds.Width / logicalWindowBounds.Width;
                    scaleY = physicalWindowBounds.Height / logicalWindowBounds.Height;
                }

                if (scaleX <= 0 || scaleX > 4)
                {
                    scaleX = 1;
                }

                if (scaleY <= 0 || scaleY > 4)
                {
                    scaleY = 1;
                }

                if (logicalWindowBounds.IsEmpty)
                {
                    physicalPoint = logicalPoint;
                    return true;
                }

                physicalPoint = new Point(
                    physicalWindowBounds.Left + (logicalPoint.X - logicalWindowBounds.Left) * scaleX,
                    physicalWindowBounds.Top + (logicalPoint.Y - logicalWindowBounds.Top) * scaleY);
                return true;
            }

            physicalPoint = logicalPoint;
            physicalWindowBounds = logicalWindowBounds;
            return !physicalWindowBounds.IsEmpty;
        }

        private static Rect ConvertToLogicalBounds(Rect physicalBounds, Rect physicalWindowBounds, Rect logicalWindowBounds, double scaleX, double scaleY)
        {
            if (logicalWindowBounds.IsEmpty || physicalWindowBounds.IsEmpty || scaleX <= 0 || scaleY <= 0)
            {
                return physicalBounds;
            }

            double left = logicalWindowBounds.Left + (physicalBounds.Left - physicalWindowBounds.Left) / scaleX;
            double top = logicalWindowBounds.Top + (physicalBounds.Top - physicalWindowBounds.Top) / scaleY;
            double width = physicalBounds.Width / scaleX;
            double height = physicalBounds.Height / scaleY;
            return new Rect(left, top, width, height);
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
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

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
    }
}
