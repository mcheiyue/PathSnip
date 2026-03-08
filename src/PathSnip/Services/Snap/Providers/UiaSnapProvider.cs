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
        private const double MinElementSize = 6;
        private const double WindowBoundsTolerance = 2;
        private const double MaxBoundsAreaScale = 1.02;

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

            AutomationElement element;
            try
            {
                element = AutomationElement.FromPoint(screenPoint);
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

            if (element == null)
            {
                return SnapResult.None;
            }

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

            if (!bounds.Contains(screenPoint))
            {
                return SnapResult.None;
            }

            Rect windowBounds = currentSnap.Bounds.Value;
            if (!windowBounds.IntersectsWith(bounds))
            {
                return SnapResult.None;
            }

            if (!IsElementBoundsInsideWindow(bounds, windowBounds))
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

            IntPtr? windowHandle = currentSnap.WindowHandle;
            if (!windowHandle.HasValue || windowHandle.Value == IntPtr.Zero)
            {
                try
                {
                    int nativeHandle = element.Current.NativeWindowHandle;
                    if (nativeHandle != 0)
                    {
                        windowHandle = new IntPtr(nativeHandle);
                    }
                }
                catch (ElementNotAvailableException)
                {
                }
                catch (InvalidOperationException)
                {
                }
            }

            return new SnapResult(bounds, label, true, SnapKind.Element, SnapSource.UIA, 0.85, windowHandle);
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
    }
}
