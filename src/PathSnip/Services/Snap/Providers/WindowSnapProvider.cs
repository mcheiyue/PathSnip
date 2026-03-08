using System.Windows;

namespace PathSnip.Services.Snap
{
    public sealed class WindowSnapProvider : ISnapProvider
    {
        private readonly ISnapService _snapService;

        public WindowSnapProvider()
            : this(new WindowSnapService())
        {
        }

        public WindowSnapProvider(ISnapService snapService)
        {
            _snapService = snapService;
        }

        public SnapResult GetCurrentSnap(Point screenPoint, int currentProcessId)
        {
            return _snapService.GetCurrentSnap(screenPoint, currentProcessId);
        }
    }
}
