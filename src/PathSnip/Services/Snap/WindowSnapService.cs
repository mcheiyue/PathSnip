using System.Windows;
using PathSnip.Services;

namespace PathSnip.Services.Snap
{
    public sealed class WindowSnapService : ISnapService
    {
        public SnapResult GetCurrentSnap(Point screenPoint, int currentProcessId)
        {
            WindowTarget? windowTarget = WindowDetectionService.GetWindowTargetUnderCursor(screenPoint, currentProcessId);
            if (!windowTarget.HasValue)
            {
                return SnapResult.None;
            }

            return SnapResult.FromWindow(windowTarget.Value.Bounds, windowHandle: windowTarget.Value.Hwnd);
        }
    }
}
