using System.Windows;

namespace PathSnip.Services.Snap
{
    public interface ISnapService
    {
        SnapResult GetCurrentSnap(Point screenPoint, int currentProcessId);
    }
}
