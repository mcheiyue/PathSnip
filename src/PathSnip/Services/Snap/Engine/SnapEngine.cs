using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;

namespace PathSnip.Services.Snap
{
    public sealed class SnapEngine
    {
        private readonly IReadOnlyList<ISnapProvider> _providers;
        private long _requestVersion;

        public SnapEngine(IEnumerable<ISnapProvider> providers)
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
    }
}
