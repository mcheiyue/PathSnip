using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows;

namespace PathSnip.Services.Snap
{
    public sealed class SnapEngine
    {
        private readonly IReadOnlyList<ISnapProvider> _providers;
        private readonly UiaSnapProvider _uiaSnapProvider;
        private readonly MsaaSnapProvider _msaaSnapProvider;
        private readonly SnapIgnorePolicy _ignorePolicy = new SnapIgnorePolicy();
        private readonly SnapRankingPolicy _rankingPolicy = new SnapRankingPolicy();
        private readonly SnapStabilizer _stabilizer = new SnapStabilizer();
        private readonly object _policySync = new object();
        private long _requestVersion;
        private Rect? _lastWindowBounds;
        private SnapResult _lastAcceptedElement = SnapResult.None;

        public SnapEngine(IEnumerable<ISnapProvider> providers, UiaSnapProvider uiaSnapProvider = null, MsaaSnapProvider msaaSnapProvider = null)
        {
            if (providers == null)
            {
                throw new ArgumentNullException(nameof(providers));
            }

            var providerList = new List<ISnapProvider>(providers);
            if (providerList.Count == 0)
            {
                throw new ArgumentException("providers cannot be empty", nameof(providers));
            }

            _providers = providerList;
            _uiaSnapProvider = uiaSnapProvider;
            _msaaSnapProvider = msaaSnapProvider;
        }

        public long NextRequestVersion()
        {
            return Interlocked.Increment(ref _requestVersion);
        }

        public bool IsCurrentRequest(long requestVersion)
        {
            return requestVersion == Volatile.Read(ref _requestVersion);
        }

        public SnapResult GetCurrentSnap(Point screenPoint, int currentProcessId)
        {
            for (int i = 0; i < _providers.Count; i++)
            {
                SnapResult snapResult = _providers[i].GetCurrentSnap(screenPoint, currentProcessId);
                if (snapResult.IsValid)
                {
                    return snapResult;
                }
            }

            return SnapResult.None;
        }

        public async Task<SnapResult> TryGetElementSnapAsync(
            Point screenPoint,
            int currentProcessId,
            SnapResult currentSnap,
            long requestVersion,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            if (!currentSnap.IsValid || !currentSnap.Bounds.HasValue)
            {
                return SnapResult.None;
            }

            if (_uiaSnapProvider == null && _msaaSnapProvider == null)
            {
                return SnapResult.None;
            }

            if (!IsCurrentRequest(requestVersion))
            {
                return SnapResult.None;
            }

            bool msaaFirst = IsMsaaPreferred(currentSnap.WindowHandle);
            var providers = new List<Func<CancellationToken, Task<SnapResult>>>(2);

            if (msaaFirst)
            {
                if (_msaaSnapProvider != null)
                {
                    providers.Add(ct => _msaaSnapProvider.GetCurrentSnapAsync(screenPoint, currentProcessId, currentSnap, ct));
                }

                if (_uiaSnapProvider != null)
                {
                    providers.Add(ct => _uiaSnapProvider.GetCurrentSnapAsync(screenPoint, currentProcessId, currentSnap, ct));
                }
            }
            else
            {
                if (_uiaSnapProvider != null)
                {
                    providers.Add(ct => _uiaSnapProvider.GetCurrentSnapAsync(screenPoint, currentProcessId, currentSnap, ct));
                }

                if (_msaaSnapProvider != null)
                {
                    providers.Add(ct => _msaaSnapProvider.GetCurrentSnapAsync(screenPoint, currentProcessId, currentSnap, ct));
                }
            }

            for (int i = 0; i < providers.Count; i++)
            {
                if (!IsCurrentRequest(requestVersion))
                {
                    return SnapResult.None;
                }

                SnapResult snapResult = await TryDetectWithTimeoutAsync(providers[i], timeoutMs, cancellationToken).ConfigureAwait(false);
                if (!snapResult.IsValid)
                {
                    continue;
                }

                if (!IsCurrentRequest(requestVersion))
                {
                    return SnapResult.None;
                }

                SnapResult acceptedResult = ApplyPolicies(currentSnap, snapResult, screenPoint);
                if (acceptedResult.IsValid)
                {
                    return acceptedResult;
                }
            }

            return SnapResult.None;
        }

        private SnapResult ApplyPolicies(SnapResult windowSnap, SnapResult candidate, Point cursorPoint)
        {
            lock (_policySync)
            {
                if (HasWindowContextChanged(windowSnap))
                {
                    _stabilizer.Reset();
                    _lastAcceptedElement = SnapResult.None;
                }

                _lastWindowBounds = windowSnap.Bounds;

                if (_ignorePolicy.ShouldIgnore(windowSnap, candidate))
                {
                    return SnapResult.None;
                }

                if (!_rankingPolicy.ShouldUseElement(windowSnap, candidate, cursorPoint, _lastAcceptedElement))
                {
                    return SnapResult.None;
                }

                SnapResult stabilized = _stabilizer.Evaluate(candidate, DateTime.UtcNow);
                if (!stabilized.IsValid)
                {
                    return SnapResult.None;
                }

                _lastAcceptedElement = stabilized;
                return stabilized;
            }
        }

        private static async Task<SnapResult> TryDetectWithTimeoutAsync(
            Func<CancellationToken, Task<SnapResult>> detect,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            Task<SnapResult> detectTask;
            try
            {
                detectTask = detect(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return SnapResult.None;
            }

            Task timeoutTask = Task.Delay(timeoutMs);
            Task completedTask = await Task.WhenAny(detectTask, timeoutTask).ConfigureAwait(false);
            if (completedTask != detectTask)
            {
                return SnapResult.None;
            }

            try
            {
                SnapResult snapResult = await detectTask.ConfigureAwait(false);
                return snapResult.IsValid ? snapResult : SnapResult.None;
            }
            catch (OperationCanceledException)
            {
                return SnapResult.None;
            }
        }

        private static bool IsMsaaPreferred(IntPtr? windowHandle)
        {
            if (!windowHandle.HasValue || windowHandle.Value == IntPtr.Zero)
            {
                return false;
            }

            uint processId;
            if (!TryGetWindowProcessId(windowHandle.Value, out processId))
            {
                return false;
            }

            string processName;
            try
            {
                Process process = Process.GetProcessById((int)processId);
                processName = process.ProcessName;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(processName))
            {
                return false;
            }

            string normalized = processName.ToLowerInvariant();
            return normalized.Contains("chrome")
                || normalized.Contains("msedge")
                || normalized.Contains("edge")
                || normalized.Contains("opera")
                || normalized.Contains("brave")
                || normalized.Contains("vivaldi")
                || normalized.Contains("electron")
                || normalized == "code"
                || normalized.Contains("code");
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

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private bool HasWindowContextChanged(SnapResult windowSnap)
        {
            if (!windowSnap.IsValid || !windowSnap.Bounds.HasValue)
            {
                return false;
            }

            if (!_lastWindowBounds.HasValue)
            {
                return false;
            }

            Rect previous = _lastWindowBounds.Value;
            Rect current = windowSnap.Bounds.Value;

            if (ComputeIou(previous, current) < 0.6)
            {
                return true;
            }

            return Math.Abs(previous.Left - current.Left) > 6
                || Math.Abs(previous.Top - current.Top) > 6
                || Math.Abs(previous.Width - current.Width) > 6
                || Math.Abs(previous.Height - current.Height) > 6;
        }

        private static double ComputeIou(Rect a, Rect b)
        {
            Rect intersection = Rect.Intersect(a, b);
            if (intersection.IsEmpty)
            {
                return 0;
            }

            double intersectionArea = Math.Max(0, intersection.Width * intersection.Height);
            double unionArea = Math.Max(1, a.Width * a.Height + b.Width * b.Height - intersectionArea);
            return intersectionArea / unionArea;
        }
    }
}
