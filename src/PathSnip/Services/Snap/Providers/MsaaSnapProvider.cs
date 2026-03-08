using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Accessibility;

namespace PathSnip.Services.Snap
{
    public sealed class MsaaSnapProvider
    {
        private const int ChildIdSelf = 0;
        private const double MinElementSize = 6;
        private const double WindowBoundsTolerance = 2;
        private const double MaxBoundsAreaScale = 1.02;

        public Task<SnapResult> GetCurrentSnapAsync(Point screenPoint, int currentProcessId, SnapResult currentSnap, CancellationToken cancellationToken)
        {
            if (!currentSnap.IsValid || !currentSnap.Bounds.HasValue)
            {
                return Task.FromResult(SnapResult.None);
            }

            if (!currentSnap.Bounds.Value.Contains(screenPoint))
            {
                return Task.FromResult(SnapResult.None);
            }

            return Task.Run(() => ResolveElementSnap(screenPoint, currentProcessId, currentSnap, cancellationToken), cancellationToken);
        }

        private static SnapResult ResolveElementSnap(Point screenPoint, int currentProcessId, SnapResult currentSnap, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IntPtr hwnd = currentSnap.WindowHandle.GetValueOrDefault(IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
            {
                return SnapResult.None;
            }

            if (TryGetWindowProcessId(hwnd, out uint pid) && pid == (uint)currentProcessId)
            {
                return SnapResult.None;
            }

            var point = new POINT
            {
                X = (int)screenPoint.X,
                Y = (int)screenPoint.Y
            };

            IAccessible accessible;
            object child;
            int hr = AccessibleObjectFromPoint(point, out accessible, out child);
            if (hr < 0 || accessible == null)
            {
                return SnapResult.None;
            }

            cancellationToken.ThrowIfCancellationRequested();

            IAccessible target = accessible;
            object targetChild = NormalizeChildId(child);

            try
            {
                object hit = accessible.accHitTest(point.X, point.Y);
                if (hit is IAccessible)
                {
                    target = (IAccessible)hit;
                    targetChild = ChildIdSelf;
                }
                else if (hit is int)
                {
                    targetChild = (int)hit;
                }
            }
            catch (COMException)
            {
            }
            catch (InvalidCastException)
            {
            }

            Rect bounds;
            if (!TryGetAccessibleBounds(target, targetChild, out bounds) &&
                !TryGetAccessibleBounds(target, ChildIdSelf, out bounds))
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

            string label = GetAccessibleName(target, targetChild);
            return new SnapResult(bounds, label, true, SnapKind.Element, SnapSource.MSAA, 0.8, hwnd);
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

        private static bool TryGetAccessibleBounds(IAccessible accessible, object childId, out Rect bounds)
        {
            bounds = Rect.Empty;

            try
            {
                int left;
                int top;
                int width;
                int height;
                accessible.accLocation(out left, out top, out width, out height, childId);

                if (width <= 0 || height <= 0)
                {
                    return false;
                }

                bounds = new Rect(left, top, width, height);
                return true;
            }
            catch (COMException)
            {
                return false;
            }
            catch (InvalidCastException)
            {
                return false;
            }
        }

        private static string GetAccessibleName(IAccessible accessible, object childId)
        {
            try
            {
                return accessible.get_accName(childId) ?? string.Empty;
            }
            catch (COMException)
            {
                return string.Empty;
            }
            catch (InvalidCastException)
            {
                return string.Empty;
            }
        }

        private static object NormalizeChildId(object childId)
        {
            if (childId is int)
            {
                return childId;
            }

            return ChildIdSelf;
        }

        private static bool TryGetWindowProcessId(IntPtr hWnd, out uint processId)
        {
            processId = 0;
            uint value = 0;
            GetWindowThreadProcessId(hWnd, out value);
            if (value == 0)
            {
                return false;
            }

            processId = value;
            return true;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("oleacc.dll")]
        private static extern int AccessibleObjectFromPoint(POINT point, [MarshalAs(UnmanagedType.Interface)] out IAccessible accessible, [MarshalAs(UnmanagedType.Struct)] out object child);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }
}
