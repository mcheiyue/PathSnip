using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace PathSnip.Services
{
    public static class ScreenCaptureService
    {
        public static BitmapSource Capture(Rect region)
        {
            int left = (int)region.Left;
            int top = (int)region.Top;
            int width = (int)region.Width;
            int height = (int)region.Height;

            if (width <= 0 || height <= 0)
            {
                throw new ArgumentException("选区宽度和高度必须大于 0");
            }

            using (var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(left, top, 0, 0, new System.Drawing.Size(width, height));
                }

                return ConvertToBitmapSource(bitmap);
            }
        }

        private static BitmapSource ConvertToBitmapSource(Bitmap bitmap)
        {
            var bitmapData = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly,
                bitmap.PixelFormat);

            var bitmapSource = BitmapSource.Create(
                bitmapData.Width, bitmapData.Height,
                96, 96,
                System.Windows.Media.PixelFormats.Bgra32,
                null,
                bitmapData.Scan0,
                bitmapData.Stride * bitmapData.Height,
                bitmapData.Stride);

            bitmap.UnlockBits(bitmapData);

            bitmapSource.Freeze();
            return bitmapSource;
        }

        public static Rect GetVirtualScreenBounds()
        {
            return new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);
        }
    }
}
