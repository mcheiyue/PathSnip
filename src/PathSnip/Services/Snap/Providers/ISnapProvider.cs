using System.Windows;

namespace PathSnip.Services.Snap
{
    public interface ISnapProvider
    {
        SnapResult GetCurrentSnap(Point screenPoint, int currentProcessId);
    }
}
