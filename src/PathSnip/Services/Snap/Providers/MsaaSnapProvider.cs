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
        private static int _inFlight;
        private static readonly Guid IAccessibleGuid = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");
        private const int DwmaExtendedFrameBounds = 9;
        private const uint ObjIdWindow = 0x00000000;
        private const uint ObjIdClient = 0xFFFFFFFC;
        private const int ChildIdSelf = 0;
        private const double MinElementSize = 6;
        private const double WindowBoundsTolerance = 2;
        private const double MaxBoundsAreaScale = 1.02;
        private const int MaxHitTestDepth = 8;

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

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromResult(SnapResult.None);
            }

            if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
            {
                return Task.FromResult(SnapResult.None);
            }

            return Task.Run(() =>
            {
                try
                {
                    return ResolveElementSnap(screenPoint, currentProcessId, currentSnap, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return SnapResult.None;
                }
                finally
                {
                    Interlocked.Exchange(ref _inFlight, 0);
                }
            });
        }

        private static SnapResult ResolveElementSnap(Point screenPoint, int currentProcessId, SnapResult currentSnap, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Rect logicalWindowBounds = currentSnap.Bounds.Value;
            if (!TryBuildCoordinateContext(currentSnap, screenPoint, out POINT point, out Rect physicalWindowBounds, out double scaleX, out double scaleY))
            {
                return SnapResult.None;
            }

            IntPtr hwnd = currentSnap.WindowHandle.GetValueOrDefault(IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
            {
                return SnapResult.None;
            }

            if (TryGetWindowProcessId(hwnd, out uint pid) && pid == (uint)currentProcessId)
            {
                return SnapResult.None;
            }

            IAccessible accessible;
            object child;
            if (!TryGetWindowAccessible(hwnd, out accessible, out child))
            {
                int hr = AccessibleObjectFromPoint(point, out accessible, out child);
                if (hr < 0 || accessible == null)
                {
                    return SnapResult.None;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            IAccessible target = accessible;
            object targetChild = NormalizeChildId(child);
            ResolveDeepestAccessibleAtPoint(accessible, point.X, point.Y, cancellationToken, out target, out targetChild);

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

            if (!bounds.Contains(new Point(point.X, point.Y)))
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

            string label = GetAccessibleName(target, targetChild);
            Rect logicalBounds = ConvertToLogicalBounds(bounds, physicalWindowBounds, logicalWindowBounds, scaleX, scaleY);
            return new SnapResult(logicalBounds, label, true, SnapKind.Element, SnapSource.MSAA, 0.8, hwnd);
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

        private static void ResolveDeepestAccessibleAtPoint(
            IAccessible root,
            int x,
            int y,
            CancellationToken cancellationToken,
            out IAccessible target,
            out object targetChild)
        {
            target = root;
            targetChild = ChildIdSelf;

            IAccessible current = root;
            for (int depth = 0; depth < MaxHitTestDepth; depth++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                object hit;
                try
                {
                    hit = current.accHitTest(x, y);
                }
                catch (COMException)
                {
                    break;
                }
                catch (InvalidCastException)
                {
                    break;
                }

                if (hit is IAccessible)
                {
                    current = (IAccessible)hit;
                    target = current;
                    targetChild = ChildIdSelf;
                    continue;
                }

                if (hit is int)
                {
                    int childId = (int)hit;
                    if (childId == ChildIdSelf)
                    {
                        target = current;
                        targetChild = ChildIdSelf;
                        break;
                    }

                    object childObj = null;
                    try
                    {
                        childObj = current.get_accChild(childId);
                    }
                    catch (COMException)
                    {
                    }
                    catch (InvalidCastException)
                    {
                    }

                    if (childObj is IAccessible)
                    {
                        current = (IAccessible)childObj;
                        target = current;
                        targetChild = ChildIdSelf;
                        continue;
                    }

                    target = current;
                    targetChild = childId;
                    break;
                }

                break;
            }
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

        [DllImport("oleacc.dll")]
        private static extern int AccessibleObjectFromWindow(IntPtr hWnd, uint dwId, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IAccessible ppvObject);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private static bool TryBuildCoordinateContext(
            SnapResult currentSnap,
            Point logicalPoint,
            out POINT physicalPoint,
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

                Point mappedPoint = logicalWindowBounds.IsEmpty
                    ? logicalPoint
                    : new Point(
                        physicalWindowBounds.Left + (logicalPoint.X - logicalWindowBounds.Left) * scaleX,
                        physicalWindowBounds.Top + (logicalPoint.Y - logicalWindowBounds.Top) * scaleY);

                physicalPoint = new POINT
                {
                    X = (int)Math.Round(mappedPoint.X),
                    Y = (int)Math.Round(mappedPoint.Y)
                };

                return true;
            }

            physicalPoint = new POINT
            {
                X = (int)Math.Round(logicalPoint.X),
                Y = (int)Math.Round(logicalPoint.Y)
            };
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

        private static bool TryGetWindowAccessible(IntPtr hWnd, out IAccessible accessible, out object child)
        {
            child = ChildIdSelf;
            Guid iid = IAccessibleGuid;

            int hr = AccessibleObjectFromWindow(hWnd, ObjIdClient, ref iid, out accessible);
            if (hr >= 0 && accessible != null)
            {
                return true;
            }

            hr = AccessibleObjectFromWindow(hWnd, ObjIdWindow, ref iid, out accessible);
            if (hr >= 0 && accessible != null)
            {
                return true;
            }

            accessible = null;
            return false;
        }
    }
}
