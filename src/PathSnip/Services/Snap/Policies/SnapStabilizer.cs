using System;
using System.Windows;

namespace PathSnip.Services.Snap
{
    public sealed class SnapStabilizer
    {
        private const int RequiredConsecutiveHits = 1;
        private const int MinSwitchDelayMs = 20;
        private const double StableIou = 0.85;

        private SnapResult _stableResult = SnapResult.None;
        private SnapResult _pendingResult = SnapResult.None;
        private int _pendingHits;
        private DateTime _lastSwitchAt = DateTime.MinValue;

        public SnapResult Evaluate(SnapResult candidate, DateTime now)
        {
            if (!candidate.IsValid || !candidate.Bounds.HasValue)
            {
                _pendingResult = SnapResult.None;
                _pendingHits = 0;
                return SnapResult.None;
            }

            if (!_stableResult.IsValid || !_stableResult.Bounds.HasValue)
            {
                _stableResult = candidate;
                _pendingResult = SnapResult.None;
                _pendingHits = 0;
                _lastSwitchAt = now;
                return _stableResult;
            }

            if (_stableResult.IsValid && _stableResult.Bounds.HasValue && IsSimilar(_stableResult, candidate))
            {
                _stableResult = candidate;
                _pendingResult = SnapResult.None;
                _pendingHits = 0;
                return _stableResult;
            }

            if ((now - _lastSwitchAt).TotalMilliseconds < MinSwitchDelayMs)
            {
                return _stableResult.IsValid ? _stableResult : SnapResult.None;
            }

            if (_pendingResult.IsValid && _pendingResult.Bounds.HasValue && IsSimilar(_pendingResult, candidate))
            {
                _pendingHits++;
            }
            else
            {
                _pendingResult = candidate;
                _pendingHits = 1;
            }

            if (_pendingHits < RequiredConsecutiveHits)
            {
                return _stableResult.IsValid ? _stableResult : SnapResult.None;
            }

            _stableResult = candidate;
            _pendingResult = SnapResult.None;
            _pendingHits = 0;
            _lastSwitchAt = now;
            return _stableResult;
        }

        public void Reset()
        {
            _stableResult = SnapResult.None;
            _pendingResult = SnapResult.None;
            _pendingHits = 0;
            _lastSwitchAt = DateTime.MinValue;
        }

        private static bool IsSimilar(SnapResult a, SnapResult b)
        {
            if (!a.Bounds.HasValue || !b.Bounds.HasValue)
            {
                return false;
            }

            if (a.Source != b.Source)
            {
                return false;
            }

            return ComputeIou(a.Bounds.Value, b.Bounds.Value) >= StableIou;
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
