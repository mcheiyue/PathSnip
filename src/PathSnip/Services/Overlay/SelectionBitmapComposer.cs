using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PathSnip.Services.Overlay
{
    public sealed class SelectionBitmapComposeRequest
    {
        public BitmapSource Background { get; set; }

        public Canvas MosaicCanvas { get; set; }

        public Canvas AnnotationCanvas { get; set; }

        public Rect SelectionRect { get; set; }

        public double DpiX { get; set; }

        public double DpiY { get; set; }

        public double SurfaceWidth { get; set; }

        public double SurfaceHeight { get; set; }
    }

    public sealed class SelectionBitmapComposer
    {
        public BitmapSource Compose(SelectionBitmapComposeRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (request.Background == null)
            {
                return null;
            }

            if (request.SelectionRect.Width <= 0 || request.SelectionRect.Height <= 0)
            {
                throw new InvalidOperationException("保存失败：选区尺寸无效。");
            }

            int physicalWidth = Math.Max(1, (int)Math.Round(request.SelectionRect.Width * request.DpiX / 96.0));
            int physicalHeight = Math.Max(1, (int)Math.Round(request.SelectionRect.Height * request.DpiY / 96.0));

            var renderBitmap = new RenderTargetBitmap(physicalWidth, physicalHeight, request.DpiX, request.DpiY, PixelFormats.Pbgra32);
            var drawingVisual = new DrawingVisual();
            using (DrawingContext dc = drawingVisual.RenderOpen())
            {
                int cropX = (int)Math.Floor(request.SelectionRect.Left * request.DpiX / 96.0);
                int cropY = (int)Math.Floor(request.SelectionRect.Top * request.DpiY / 96.0);

                cropX = Math.Max(0, Math.Min(cropX, Math.Max(0, request.Background.PixelWidth - 1)));
                cropY = Math.Max(0, Math.Min(cropY, Math.Max(0, request.Background.PixelHeight - 1)));

                int cropW = Math.Min(physicalWidth, request.Background.PixelWidth - cropX);
                int cropH = Math.Min(physicalHeight, request.Background.PixelHeight - cropY);

                if (cropW <= 0 || cropH <= 0)
                {
                    throw new InvalidOperationException(
                        string.Format(
                            "保存失败：裁剪区域无效 ({0},{1},{2},{3})。background={4}x{5}",
                            cropX,
                            cropY,
                            cropW,
                            cropH,
                            request.Background.PixelWidth,
                            request.Background.PixelHeight));
                }

                var backgroundRect = new Int32Rect(cropX, cropY, cropW, cropH);
                var croppedBackground = new CroppedBitmap(request.Background, backgroundRect);
                dc.DrawImage(croppedBackground, new Rect(0, 0, physicalWidth, physicalHeight));

                if (request.MosaicCanvas != null && request.MosaicCanvas.Children.Count > 0)
                {
                    DrawOverlayCanvas(dc, request.MosaicCanvas, request.SelectionRect, request.SurfaceWidth, request.SurfaceHeight);
                }

                if (request.AnnotationCanvas != null && request.AnnotationCanvas.Children.Count > 0)
                {
                    DrawOverlayCanvas(dc, request.AnnotationCanvas, request.SelectionRect, request.SurfaceWidth, request.SurfaceHeight);
                }
            }

            renderBitmap.Render(drawingVisual);
            renderBitmap.Freeze();
            return renderBitmap;
        }

        private static void DrawOverlayCanvas(DrawingContext dc, Canvas canvas, Rect selectionRect, double surfaceWidth, double surfaceHeight)
        {
            dc.PushTransform(new TranslateTransform(-selectionRect.Left, -selectionRect.Top));

            var overlayBrush = new VisualBrush(canvas)
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, surfaceWidth, surfaceHeight),
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, surfaceWidth, surfaceHeight)
            };

            dc.DrawRectangle(overlayBrush, null, new Rect(0, 0, surfaceWidth, surfaceHeight));
            dc.Pop();
        }
    }
}
