using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;

namespace PathSnip.Services.Snap
{
    public sealed class SnapEngine
    {
        private readonly IReadOnlyList<ISnapProvider> _providers;
        private readonly UiaSnapProvider _uiaSnapProvider;
        private long _requestVersion;

        public SnapEngine(IEnumerable<ISnapProvider> providers, UiaSnapProvider uiaSnapProvider = null)
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
            if (_uiaSnapProvider == null || !currentSnap.IsValid || !currentSnap.Bounds.HasValue)
            {
                return SnapResult.None;
            }

            if (!IsCurrentRequest(requestVersion))
            {
                return SnapResult.None;
            }

            Task<SnapResult> detectTask = _uiaSnapProvider.GetCurrentSnapAsync(screenPoint, currentProcessId, currentSnap, cancellationToken);
            Task timeoutTask = Task.Delay(timeoutMs);
            Task completedTask = await Task.WhenAny(detectTask, timeoutTask).ConfigureAwait(false);

            if (completedTask != detectTask)
            {
                return SnapResult.None;
            }

            SnapResult snapResult;
            try
            {
                snapResult = await detectTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return SnapResult.None;
            }

            if (!IsCurrentRequest(requestVersion))
            {
                return SnapResult.None;
            }

            return snapResult.IsValid ? snapResult : SnapResult.None;
        }
    }
}
