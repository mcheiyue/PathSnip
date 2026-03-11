using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using PathSnip.Services;

namespace PathSnip.Services.Snap
{
    public sealed class RegionSnapProvider
    {
        private const int DwmaExtendedFrameBounds = 9;
        private const uint MonitorDefaultToNearest = 2;
        private const double MinRegionAreaRatio = 0.02;
        private const double MinChildEdgeLength = 40;

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

            Rect physicalClientBounds;
            if (!TryGetClientBounds(windowHandle, out physicalClientBounds))
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

            string processName = ResolveProcessName(windowHandle);
            RegionProfile profile = ResolveRegionProfile(processName);

            IntPtr regionHandle = ResolveRegionHwnd(windowHandle, physicalPoint);
            bool usedSynthetic = false;
            string regionSource = "child-hit";
            RegionSelection regionSelection;
            if (regionHandle != IntPtr.Zero && regionHandle != windowHandle)
            {
                RECT rect;
                if (!GetWindowRect(regionHandle, out rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
                {
                    return SnapResult.None;
                }

                var hitBounds = new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
                if (!IsMeaningfulRegion(hitBounds, physicalClientBounds))
                {
                    if (ShouldAttemptChildEnum(profile, physicalClientBounds, physicalPoint)
                        && TryBuildChildWindowRegionSelection(windowHandle, profile, physicalClientBounds, physicalPoint, out RegionSelection childSelection))
                    {
                        regionSelection = childSelection;
                        regionSource = "child-enum";
                    }
                    else if (ShouldUseSyntheticRegion(profile)
                        && TryBuildSyntheticRegionSelection(profile, physicalClientBounds, physicalPoint, out RegionSelection syntheticSelection))
                    {
                        regionSelection = syntheticSelection;
                        usedSynthetic = true;
                        regionSource = "fallback";
                    }
                    else
                    {
                        regionSelection = SelectRegionByProfile(profile, physicalClientBounds, physicalClientBounds);
                        regionSource = "client";
                    }
                }
                else
                {
                    regionSelection = SelectRegionByProfile(profile, hitBounds, physicalClientBounds);
                    regionSource = "child-hit";
                }
            }
            else
            {
                if (ShouldAttemptChildEnum(profile, physicalClientBounds, physicalPoint)
                    && TryBuildChildWindowRegionSelection(windowHandle, profile, physicalClientBounds, physicalPoint, out RegionSelection childSelection))
                {
                    regionSelection = childSelection;
                    regionSource = "child-enum";
                }
                else if (ShouldUseSyntheticRegion(profile)
                    && TryBuildSyntheticRegionSelection(profile, physicalClientBounds, physicalPoint, out RegionSelection syntheticSelection))
                {
                    regionSelection = syntheticSelection;
                    usedSynthetic = true;
                    regionSource = "fallback";
                }
                else
                {
                    regionSelection = SelectRegionByProfile(profile, physicalClientBounds, physicalClientBounds);
                    regionSource = "client";
                }
            }

            Rect physicalRegionBounds = regionSelection.Bounds;
            RegionKind regionKind = regionSelection.Kind;

            if (usedSynthetic && TryClampToWorkArea(physicalRegionBounds, out Rect clampedBounds))
            {
                physicalRegionBounds = clampedBounds;
            }

            if (physicalRegionBounds.IsEmpty || physicalRegionBounds.Width <= 0 || physicalRegionBounds.Height <= 0)
            {
                return SnapResult.None;
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
                regionSource,
                regionKind,
                SnapSource.WindowDetection,
                0.7,
                windowHandle);

            return SnapResult.FromRegion(regionCandidate);
        }

        private static bool IsMeaningfulRegion(Rect physicalRegionBounds, Rect physicalClientBounds)
        {
            if (physicalClientBounds.IsEmpty || physicalClientBounds.Width <= 0 || physicalClientBounds.Height <= 0)
            {
                return false;
            }

            if (physicalRegionBounds.IsEmpty || physicalRegionBounds.Width <= 0 || physicalRegionBounds.Height <= 0)
            {
                return false;
            }

            double clientArea = Math.Max(1, physicalClientBounds.Width * physicalClientBounds.Height);
            double regionArea = Math.Max(1, physicalRegionBounds.Width * physicalRegionBounds.Height);
            double ratio = regionArea / clientArea;
            return ratio >= MinRegionAreaRatio;
        }

        private static RegionSelection SelectRegionByProfile(RegionProfile profile, Rect physicalRegionBounds, Rect physicalClientBounds)
        {
            RegionKind kind = ClassifyRegionKind(profile, physicalRegionBounds, physicalClientBounds);

            if (profile == RegionProfile.Browser && kind == RegionKind.PathBar && IsTinyTopStrip(physicalRegionBounds, physicalClientBounds))
            {
                Rect promotedBounds = BuildPrimaryContentBounds(physicalClientBounds, physicalRegionBounds.Bottom, 0.12);
                return new RegionSelection(promotedBounds, RegionKind.MainContent);
            }

            if (profile == RegionProfile.Ide && kind == RegionKind.Toolbar && IsTinyTopStrip(physicalRegionBounds, physicalClientBounds))
            {
                Rect promotedBounds = BuildPrimaryContentBounds(physicalClientBounds, physicalRegionBounds.Bottom, 0.14);
                return new RegionSelection(promotedBounds, RegionKind.Editor);
            }

            return new RegionSelection(physicalRegionBounds, kind);
        }

        private static bool ShouldUseSyntheticRegion(RegionProfile profile)
        {
            return profile == RegionProfile.Browser || profile == RegionProfile.Ide;
        }

        private static bool ShouldAttemptChildEnum(RegionProfile profile, Rect physicalClientBounds, Point physicalPoint)
        {
            if (profile != RegionProfile.Browser && profile != RegionProfile.Ide)
            {
                return false;
            }

            if (physicalClientBounds.IsEmpty || physicalClientBounds.Width <= 0 || physicalClientBounds.Height <= 0)
            {
                return false;
            }

            double yRatio = (physicalPoint.Y - physicalClientBounds.Top) / Math.Max(1, physicalClientBounds.Height);
            yRatio = Clamp(yRatio, 0, 1);

            if (profile == RegionProfile.Browser)
            {
                return yRatio <= 0.18;
            }

            return yRatio <= 0.16 || yRatio >= 0.68;
        }

        private static bool TryBuildSyntheticRegionSelection(RegionProfile profile, Rect physicalClientBounds, Point physicalPoint, out RegionSelection selection)
        {
            selection = default;
            if (physicalClientBounds.IsEmpty || physicalClientBounds.Width <= 0 || physicalClientBounds.Height <= 0)
            {
                return false;
            }

            if (profile == RegionProfile.Browser)
            {
                selection = BuildBrowserSyntheticRegion(physicalClientBounds, physicalPoint);
                return true;
            }

            if (profile == RegionProfile.Ide)
            {
                selection = BuildIdeSyntheticRegion(physicalClientBounds, physicalPoint);
                return true;
            }

            return false;
        }

        private static bool TryBuildChildWindowRegionSelection(
            IntPtr windowHandle,
            RegionProfile profile,
            Rect physicalClientBounds,
            Point physicalPoint,
            out RegionSelection selection)
        {
            selection = default;
            if (profile != RegionProfile.Browser && profile != RegionProfile.Ide)
            {
                return false;
            }

            if (physicalClientBounds.IsEmpty || physicalClientBounds.Width <= 0 || physicalClientBounds.Height <= 0)
            {
                return false;
            }

            var candidates = new System.Collections.Generic.List<Rect>();
            var stopwatch = Stopwatch.StartNew();
            EnumChildWindows(windowHandle, (child, lParam) =>
            {
                if (!IsWindowVisible(child))
                {
                    return true;
                }

                RECT rect;
                if (!GetWindowRect(child, out rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
                {
                    return true;
                }

                Rect bounds = new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
                Rect intersected = Rect.Intersect(bounds, physicalClientBounds);
                if (intersected.IsEmpty || intersected.Width <= 0 || intersected.Height <= 0)
                {
                    return true;
                }

                if (intersected.Width < MinChildEdgeLength || intersected.Height < MinChildEdgeLength)
                {
                    return true;
                }

                if (!IsMeaningfulRegion(intersected, physicalClientBounds))
                {
                    return true;
                }

                candidates.Add(intersected);
                return true;
            }, IntPtr.Zero);

            stopwatch.Stop();
            LogService.LogInfo(
                "snap.region.child_enum",
                $"elapsedMs={stopwatch.ElapsedMilliseconds} candidates={candidates.Count} profile={profile} source=child-enum",
                stage: "region.child-enum");

            if (candidates.Count == 0)
            {
                return false;
            }

            Rect? best = null;
            double bestArea = 0;
            foreach (Rect candidate in candidates)
            {
                if (!candidate.Contains(physicalPoint))
                {
                    continue;
                }

                double area = candidate.Width * candidate.Height;
                if (best == null || area < bestArea)
                {
                    best = candidate;
                    bestArea = area;
                }
            }

            if (best == null)
            {
                return false;
            }

            RegionKind kind = ClassifyRegionKind(profile, best.Value, physicalClientBounds);
            if (kind == RegionKind.Unknown)
            {
                kind = profile == RegionProfile.Browser ? RegionKind.MainContent : RegionKind.Editor;
            }

            selection = new RegionSelection(best.Value, kind);
            return true;
        }

        private static RegionSelection BuildBrowserSyntheticRegion(Rect physicalClientBounds, Point physicalPoint)
        {
            return new RegionSelection(physicalClientBounds, RegionKind.MainContent);
        }

        private static RegionSelection BuildIdeSyntheticRegion(Rect physicalClientBounds, Point physicalPoint)
        {
            return new RegionSelection(physicalClientBounds, RegionKind.Editor);
        }

        private static double Clamp(double value, double min, double max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private static RegionKind ClassifyRegionKind(RegionProfile profile, Rect physicalRegionBounds, Rect physicalClientBounds)
        {
            if (physicalClientBounds.IsEmpty)
            {
                return RegionKind.Unknown;
            }

            double cx = physicalClientBounds.Left;
            double cy = physicalClientBounds.Top;
            double cw = Math.Max(1, physicalClientBounds.Width);
            double ch = Math.Max(1, physicalClientBounds.Height);

            double rx = physicalRegionBounds.Left;
            double ry = physicalRegionBounds.Top;
            double rw = Math.Max(1, physicalRegionBounds.Width);
            double rh = Math.Max(1, physicalRegionBounds.Height);

            double wRatio = rw / cw;
            double hRatio = rh / ch;
            double leftRatio = (rx - cx) / cw;
            double topRatio = (ry - cy) / ch;

            if (profile == RegionProfile.Explorer)
            {
                if (leftRatio <= 0.02 && wRatio <= 0.38 && hRatio >= 0.5)
                {
                    return RegionKind.NavigationPane;
                }

                if (leftRatio >= 0.62 && wRatio <= 0.38 && hRatio >= 0.5)
                {
                    return RegionKind.PreviewPane;
                }

                if (topRatio <= 0.2 && hRatio <= 0.28 && wRatio >= 0.6)
                {
                    return RegionKind.PathBar;
                }

                return RegionKind.ContentPane;
            }

            if (profile == RegionProfile.Browser)
            {
                if (topRatio <= 0.18 && hRatio <= 0.16 && wRatio >= 0.52)
                {
                    return RegionKind.PathBar;
                }

                if ((leftRatio <= 0.03 || leftRatio >= 0.67) && wRatio <= 0.33 && hRatio >= 0.45)
                {
                    return RegionKind.Sidebar;
                }

                return RegionKind.MainContent;
            }

            if (profile == RegionProfile.Ide)
            {
                if (topRatio <= 0.16 && hRatio <= 0.14 && wRatio >= 0.5)
                {
                    return RegionKind.Toolbar;
                }

                if (topRatio >= 0.68 && hRatio >= 0.2 && wRatio >= 0.45)
                {
                    return RegionKind.Panel;
                }

                if ((leftRatio <= 0.03 || leftRatio >= 0.67) && wRatio <= 0.33 && hRatio >= 0.5)
                {
                    return RegionKind.Sidebar;
                }

                return RegionKind.Editor;
            }

            return RegionKind.Unknown;
        }

        private static RegionProfile ResolveRegionProfile(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
            {
                return RegionProfile.Unknown;
            }

            if (string.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase))
            {
                return RegionProfile.Explorer;
            }

            if (IsBrowserProcess(processName))
            {
                return RegionProfile.Browser;
            }

            if (IsIdeProcess(processName))
            {
                return RegionProfile.Ide;
            }

            return RegionProfile.Unknown;
        }

        public static string ResolveProfileLabel(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
            {
                return "Unknown";
            }

            string processName = ResolveProcessName(windowHandle);
            RegionProfile profile = ResolveRegionProfile(processName);
            switch (profile)
            {
                case RegionProfile.Explorer:
                    return "Explorer";
                case RegionProfile.Browser:
                    return "Browser";
                case RegionProfile.Ide:
                    return "Ide";
                default:
                    return "Unknown";
            }
        }

        private static bool IsBrowserProcess(string processName)
        {
            return string.Equals(processName, "chrome", StringComparison.OrdinalIgnoreCase)
                || string.Equals(processName, "msedge", StringComparison.OrdinalIgnoreCase)
                || string.Equals(processName, "firefox", StringComparison.OrdinalIgnoreCase)
                || string.Equals(processName, "brave", StringComparison.OrdinalIgnoreCase)
                || string.Equals(processName, "opera", StringComparison.OrdinalIgnoreCase)
                || string.Equals(processName, "vivaldi", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsIdeProcess(string processName)
        {
            return string.Equals(processName, "devenv", StringComparison.OrdinalIgnoreCase)
                || string.Equals(processName, "code", StringComparison.OrdinalIgnoreCase)
                || string.Equals(processName, "code-insiders", StringComparison.OrdinalIgnoreCase)
                || string.Equals(processName, "rider64", StringComparison.OrdinalIgnoreCase)
                || string.Equals(processName, "idea64", StringComparison.OrdinalIgnoreCase)
                || string.Equals(processName, "pycharm64", StringComparison.OrdinalIgnoreCase)
                || string.Equals(processName, "webstorm64", StringComparison.OrdinalIgnoreCase)
                || string.Equals(processName, "goland64", StringComparison.OrdinalIgnoreCase)
                || string.Equals(processName, "clion64", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTinyTopStrip(Rect physicalRegionBounds, Rect physicalClientBounds)
        {
            if (physicalClientBounds.IsEmpty || physicalClientBounds.Width <= 0 || physicalClientBounds.Height <= 0)
            {
                return false;
            }

            double clientHeight = Math.Max(1, physicalClientBounds.Height);
            double clientWidth = Math.Max(1, physicalClientBounds.Width);
            double hRatio = physicalRegionBounds.Height / clientHeight;
            double wRatio = physicalRegionBounds.Width / clientWidth;
            double topRatio = (physicalRegionBounds.Top - physicalClientBounds.Top) / clientHeight;
            return topRatio <= 0.24 && hRatio <= 0.08 && wRatio >= 0.45;
        }

        private static Rect BuildPrimaryContentBounds(Rect physicalClientBounds, double hitRegionBottom, double minTopRatio)
        {
            if (physicalClientBounds.IsEmpty || physicalClientBounds.Width <= 0 || physicalClientBounds.Height <= 0)
            {
                return physicalClientBounds;
            }

            double minTop = physicalClientBounds.Top + physicalClientBounds.Height * minTopRatio;
            double targetTop = Math.Max(minTop, hitRegionBottom);
            targetTop = Math.Min(targetTop, physicalClientBounds.Bottom - 40);

            double height = physicalClientBounds.Bottom - targetTop;
            if (height <= 40)
            {
                return physicalClientBounds;
            }

            return new Rect(physicalClientBounds.Left, targetTop, physicalClientBounds.Width, height);
        }

        private static string ResolveProcessName(IntPtr windowHandle)
        {
            uint processId;
            if (!TryGetWindowProcessId(windowHandle, out processId))
            {
                return string.Empty;
            }

            try
            {
                return Process.GetProcessById((int)processId).ProcessName ?? string.Empty;
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
            catch (InvalidOperationException)
            {
                return string.Empty;
            }
        }

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

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

        private static IntPtr ResolveRegionHwnd(IntPtr topWindowHandle, Point physicalPoint)
        {
            var ptScreen = new POINT
            {
                X = (int)Math.Round(physicalPoint.X),
                Y = (int)Math.Round(physicalPoint.Y)
            };

            return ResolveDeepestChildWindowFromPoint(topWindowHandle, ptScreen);
        }

        private static IntPtr ResolveDeepestChildWindowFromPoint(IntPtr topWindowHandle, POINT ptScreen)
        {
            const uint CWP_SKIPINVISIBLE = 0x0001;
            const uint CWP_SKIPDISABLED = 0x0002;
            const uint CWP_SKIPTRANSPARENT = 0x0004;
            const uint flags = CWP_SKIPINVISIBLE | CWP_SKIPDISABLED | CWP_SKIPTRANSPARENT;

            IntPtr current = topWindowHandle;
            for (int depth = 0; depth < 10; depth++)
            {
                POINT ptClient = ptScreen;
                if (!ScreenToClient(current, ref ptClient))
                {
                    break;
                }

                RECT client;
                if (!GetClientRect(current, out client))
                {
                    break;
                }

                if (ptClient.X < client.Left || ptClient.Y < client.Top || ptClient.X >= client.Right || ptClient.Y >= client.Bottom)
                {
                    break;
                }

                IntPtr next = ChildWindowFromPointEx(current, ptClient, flags);
                if (next == IntPtr.Zero || next == current)
                {
                    break;
                }

                current = next;
            }

            return current == topWindowHandle ? IntPtr.Zero : current;
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

        private static bool TryClampToWorkArea(Rect bounds, out Rect clampedBounds)
        {
            clampedBounds = bounds;
            if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
            {
                return false;
            }

            RECT rect = new RECT
            {
                Left = (int)Math.Round(bounds.Left),
                Top = (int)Math.Round(bounds.Top),
                Right = (int)Math.Round(bounds.Right),
                Bottom = (int)Math.Round(bounds.Bottom)
            };

            IntPtr monitor = MonitorFromRect(ref rect, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
            {
                return false;
            }

            var info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            if (!GetMonitorInfo(monitor, ref info))
            {
                return false;
            }

            Rect workArea = new Rect(
                info.rcWork.Left,
                info.rcWork.Top,
                info.rcWork.Right - info.rcWork.Left,
                info.rcWork.Bottom - info.rcWork.Top);

            if (workArea.IsEmpty || workArea.Width <= 0 || workArea.Height <= 0)
            {
                return false;
            }

            Rect intersected = Rect.Intersect(bounds, workArea);
            if (intersected.IsEmpty || intersected.Width <= 0 || intersected.Height <= 0)
            {
                return false;
            }

            clampedBounds = intersected;
            return true;
        }

        private enum RegionProfile
        {
            Unknown,
            Explorer,
            Browser,
            Ide
        }

        private readonly struct RegionSelection
        {
            public RegionSelection(Rect bounds, RegionKind kind)
            {
                Bounds = bounds;
                Kind = kind;
            }

            public Rect Bounds { get; }

            public RegionKind Kind { get; }
        }

        private delegate bool EnumWindowProc(IntPtr hWnd, IntPtr lParam);

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

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr ChildWindowFromPointEx(IntPtr hWndParent, POINT pt, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);
    }
}
