using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows;
using PathSnip.Services;

namespace PathSnip.Services.Snap
{
    public sealed class SnapEngine
    {
        private readonly IReadOnlyList<ISnapProvider> _providers;
        private readonly UiaSnapProvider _uiaSnapProvider;
        private readonly MsaaSnapProvider _msaaSnapProvider;
        private readonly RegionSnapProvider _regionSnapProvider;
        private readonly SnapIgnorePolicy _ignorePolicy = new SnapIgnorePolicy();
        private readonly SnapRankingPolicy _rankingPolicy = new SnapRankingPolicy();
        private readonly SnapStabilizer _stabilizer = new SnapStabilizer();
        private readonly object _policySync = new object();
        private long _windowRequestVersion;
        private long _elementRequestVersion;
        private Rect? _lastWindowBounds;
        private SnapResult _lastAcceptedElement = SnapResult.None;

        public SnapEngine(
            IEnumerable<ISnapProvider> providers,
            UiaSnapProvider uiaSnapProvider = null,
            MsaaSnapProvider msaaSnapProvider = null,
            RegionSnapProvider regionSnapProvider = null)
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
            _regionSnapProvider = regionSnapProvider;
        }

        public long NextWindowRequestVersion()
        {
            return Interlocked.Increment(ref _windowRequestVersion);
        }

        public bool IsCurrentWindowRequest(long requestVersion)
        {
            return requestVersion == Volatile.Read(ref _windowRequestVersion);
        }

        public long NextElementRequestVersion()
        {
            return Interlocked.Increment(ref _elementRequestVersion);
        }

        public bool IsCurrentElementRequest(long requestVersion)
        {
            return requestVersion == Volatile.Read(ref _elementRequestVersion);
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

        public SnapResult TryGetRegionSnap(Point screenPoint, int currentProcessId, SnapResult windowSnap)
        {
            if (_regionSnapProvider == null)
            {
                return SnapResult.None;
            }

            return _regionSnapProvider.GetCurrentRegionSnap(screenPoint, currentProcessId, windowSnap);
        }

        public async Task<SnapResult> TryGetElementSnapAsync(
            Point screenPoint,
            int currentProcessId,
            SnapResult currentSnap,
            long requestVersion,
            int timeoutMs,
            CancellationToken cancellationToken,
            string operationId = null)
        {
            operationId = string.IsNullOrWhiteSpace(operationId) ? LogService.CreateOperationId("snap") : operationId;

            if (!currentSnap.IsValid || !currentSnap.Bounds.HasValue)
            {
                LogService.LogWarn("snap.element.skipped", "window snap is invalid", operationId, "element.guard");
                return SnapResult.None;
            }

            if (_uiaSnapProvider == null && _msaaSnapProvider == null)
            {
                LogService.LogWarn("snap.element.skipped", "no element providers available", operationId, "element.guard");
                return SnapResult.None;
            }

            if (!IsCurrentElementRequest(requestVersion))
            {
                LogService.LogWarn("snap.element.stale", $"requestVersion={requestVersion}", operationId, "element.guard");
                return SnapResult.None;
            }

            LogService.LogInfo(
                "snap.element.requested",
                $"point=({screenPoint.X:F1},{screenPoint.Y:F1}) timeoutMs={timeoutMs} requestVersion={requestVersion}",
                operationId,
                "element.request");

            bool msaaFirst = IsMsaaPreferred(currentSnap.WindowHandle);
            var providers = new List<KeyValuePair<string, Func<CancellationToken, Task<SnapResult>>>>(2);

            if (msaaFirst)
            {
                if (_msaaSnapProvider != null)
                {
                    providers.Add(new KeyValuePair<string, Func<CancellationToken, Task<SnapResult>>>(
                        "MSAA",
                        ct => _msaaSnapProvider.GetCurrentSnapAsync(screenPoint, currentProcessId, currentSnap, ct)));
                }

                if (_uiaSnapProvider != null)
                {
                    providers.Add(new KeyValuePair<string, Func<CancellationToken, Task<SnapResult>>>(
                        "UIA",
                        ct => _uiaSnapProvider.GetCurrentSnapAsync(screenPoint, currentProcessId, currentSnap, ct)));
                }
            }
            else
            {
                if (_uiaSnapProvider != null)
                {
                    providers.Add(new KeyValuePair<string, Func<CancellationToken, Task<SnapResult>>>(
                        "UIA",
                        ct => _uiaSnapProvider.GetCurrentSnapAsync(screenPoint, currentProcessId, currentSnap, ct)));
                }

                if (_msaaSnapProvider != null)
                {
                    providers.Add(new KeyValuePair<string, Func<CancellationToken, Task<SnapResult>>>(
                        "MSAA",
                        ct => _msaaSnapProvider.GetCurrentSnapAsync(screenPoint, currentProcessId, currentSnap, ct)));
                }
            }

            for (int i = 0; i < providers.Count; i++)
            {
                string providerName = providers[i].Key;

                if (!IsCurrentElementRequest(requestVersion))
                {
                    LogService.LogWarn("snap.element.stale", $"provider={providerName} requestVersion={requestVersion}", operationId, "element.guard");
                    return SnapResult.None;
                }

                LogService.LogInfo("snap.element.provider.try", $"provider={providerName}", operationId, "element.detect");

                SnapResult snapResult = await TryDetectWithTimeoutAsync(providers[i].Value, timeoutMs, cancellationToken).ConfigureAwait(false);
                if (!snapResult.IsValid)
                {
                    LogService.LogWarn("snap.element.provider.empty", $"provider={providerName}", operationId, "element.detect");
                    continue;
                }

                if (!IsCurrentElementRequest(requestVersion))
                {
                    LogService.LogWarn("snap.element.stale", $"provider={providerName} requestVersion={requestVersion}", operationId, "element.guard");
                    return SnapResult.None;
                }

                SnapResult acceptedResult = ApplyPolicies(currentSnap, snapResult, screenPoint, operationId, providerName);
                if (acceptedResult.IsValid)
                {
                    LogService.LogInfo(
                        "snap.element.completed",
                        $"provider={providerName} source={acceptedResult.Source} bounds=({acceptedResult.Bounds?.Left:F1},{acceptedResult.Bounds?.Top:F1},{acceptedResult.Bounds?.Width:F1},{acceptedResult.Bounds?.Height:F1})",
                        operationId,
                        "element.done");
                    return acceptedResult;
                }
            }

            LogService.LogWarn("snap.element.rejected", "all providers rejected by policy or no valid candidate", operationId, "element.done");
            return SnapResult.None;
        }

        private SnapResult ApplyPolicies(SnapResult windowSnap, SnapResult candidate, Point cursorPoint, string operationId, string providerName)
        {
            lock (_policySync)
            {
                SnapAppProfile appProfile = ResolveAppProfile(windowSnap.WindowHandle);

                if (HasWindowContextChanged(windowSnap))
                {
                    _stabilizer.Reset();
                    _lastAcceptedElement = SnapResult.None;
                }

                _lastWindowBounds = windowSnap.Bounds;

                string ignoreReason;
                if (_ignorePolicy.ShouldIgnore(windowSnap, candidate, appProfile, out ignoreReason))
                {
                    LogService.LogWarn("snap.candidates.ignored", $"provider={providerName} source={candidate.Source} profile={appProfile} reason={ignoreReason}", operationId, "policy.ignore");
                    return SnapResult.None;
                }

                string rankReason;
                if (!_rankingPolicy.ShouldUseElement(windowSnap, candidate, cursorPoint, _lastAcceptedElement, appProfile, out rankReason))
                {
                    LogService.LogWarn("snap.candidates.rejected", $"provider={providerName} source={candidate.Source} profile={appProfile} reason={rankReason}", operationId, "policy.rank");
                    return SnapResult.None;
                }

                SnapResult stabilized = _stabilizer.Evaluate(candidate, DateTime.UtcNow);
                if (!stabilized.IsValid)
                {
                    LogService.LogWarn("snap.stabilizer.keep", $"provider={providerName}", operationId, "policy.stabilizer");
                    return SnapResult.None;
                }

                _lastAcceptedElement = stabilized;
                LogService.LogInfo("snap.stabilizer.switch", $"provider={providerName} source={stabilized.Source}", operationId, "policy.stabilizer");
                return stabilized;
            }
        }

        private static SnapAppProfile ResolveAppProfile(IntPtr? windowHandle)
        {
            string processName = ResolveProcessName(windowHandle);
            if (string.IsNullOrWhiteSpace(processName))
            {
                return SnapAppProfile.Generic;
            }

            string normalized = processName.ToLowerInvariant();
            if (normalized == "explorer")
            {
                return SnapAppProfile.Explorer;
            }

            if (normalized.Contains("chrome")
                || normalized.Contains("msedge")
                || normalized.Contains("edge")
                || normalized.Contains("firefox")
                || normalized.Contains("opera")
                || normalized.Contains("brave")
                || normalized.Contains("vivaldi")
                || normalized.Contains("electron"))
            {
                return SnapAppProfile.Browser;
            }

            if (normalized == "code"
                || normalized.Contains("devenv")
                || normalized.Contains("rider")
                || normalized.Contains("idea")
                || normalized.Contains("pycharm"))
            {
                return SnapAppProfile.Ide;
            }

            return SnapAppProfile.Generic;
        }

        private static string ResolveProcessName(IntPtr? windowHandle)
        {
            if (!windowHandle.HasValue || windowHandle.Value == IntPtr.Zero)
            {
                return string.Empty;
            }

            uint processId;
            if (!TryGetWindowProcessId(windowHandle.Value, out processId))
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
            string processName = ResolveProcessName(windowHandle);

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
