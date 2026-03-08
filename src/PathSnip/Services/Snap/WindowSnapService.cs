using System.Windows;

namespace PathSnip.Services.Snap
{
    public sealed class WindowSnapService : ISnapService
    {
        public SnapResult GetCurrentSnap(Point screenPoint, int currentProcessId)
        {
            Rect? windowRect = WindowDetectionService.GetWindowUnderCursor(screenPoint, currentProcessId);
            if (!windowRect.HasValue)
            {
                return SnapResult.None;
            }

            return SnapResult.FromWindow(windowRect.Value);
        }
    }
}
