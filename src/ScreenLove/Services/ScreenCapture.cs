using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenLove.Native;
using D = System.Drawing;
using DI = System.Drawing.Imaging;

namespace ScreenLove.Services
{
    /// <summary>Istantanea dell'intero desktop virtuale (tutti i monitor) in pixel fisici.</summary>
    public sealed class ScreenShot
    {
        public ScreenShot(BitmapSource image, Int32Rect bounds, byte[] pixels, int stride)
        {
            Image = image;
            Bounds = bounds;
            Pixels = pixels;
            Stride = stride;
        }

        public BitmapSource Image { get; }

        /// <summary>Posizione del desktop virtuale in coordinate schermo (può avere origine negativa).</summary>
        public Int32Rect Bounds { get; }

        public byte[] Pixels { get; }
        public int Stride { get; }
        public int Width => Image.PixelWidth;
        public int Height => Image.PixelHeight;

        public Color GetPixel(int x, int y)
        {
            x = Math.Clamp(x, 0, Width - 1);
            y = Math.Clamp(y, 0, Height - 1);
            int i = y * Stride + x * 4;
            return Color.FromRgb(Pixels[i + 2], Pixels[i + 1], Pixels[i]);
        }

        public BitmapSource Crop(Int32Rect r)
        {
            var cropped = new CroppedBitmap(Image, r);
            cropped.Freeze();
            return cropped;
        }
    }

    public static class ScreenCapture
    {
        public static ScreenShot CaptureVirtualScreen(bool includeCursor)
        {
            int x = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            int y = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
            int w = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
            int h = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

            using var bmp = new D.Bitmap(w, h, DI.PixelFormat.Format32bppRgb);
            using (var g = D.Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(x, y, 0, 0, new D.Size(w, h), D.CopyPixelOperation.SourceCopy);
                if (includeCursor)
                    DrawCursor(g, x, y);
            }

            int stride = w * 4;
            var pixels = new byte[stride * h];
            var data = bmp.LockBits(new D.Rectangle(0, 0, w, h), DI.ImageLockMode.ReadOnly, DI.PixelFormat.Format32bppRgb);
            try
            {
                for (int row = 0; row < h; row++)
                    Marshal.Copy(data.Scan0 + row * data.Stride, pixels, row * stride, stride);
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            // Format32bppRgb lascia l'alfa indefinito: lo forziamo opaco.
            for (int i = 3; i < pixels.Length; i += 4)
                pixels[i] = 255;

            var source = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            source.Freeze();
            return new ScreenShot(source, new Int32Rect(x, y, w, h), pixels, stride);
        }

        /// <summary>Cattura un rettangolo dello schermo (coordinate schermo fisiche) come pixel BGRA opachi.</summary>
        public static byte[] CaptureRectPixels(Int32Rect r)
        {
            using var bmp = new D.Bitmap(r.Width, r.Height, DI.PixelFormat.Format32bppRgb);
            using (var g = D.Graphics.FromImage(bmp))
                g.CopyFromScreen(r.X, r.Y, 0, 0, new D.Size(r.Width, r.Height), D.CopyPixelOperation.SourceCopy);

            int stride = r.Width * 4;
            var pixels = new byte[stride * r.Height];
            var data = bmp.LockBits(new D.Rectangle(0, 0, r.Width, r.Height), DI.ImageLockMode.ReadOnly, DI.PixelFormat.Format32bppRgb);
            try
            {
                for (int row = 0; row < r.Height; row++)
                    Marshal.Copy(data.Scan0 + row * data.Stride, pixels, row * stride, stride);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
            for (int i = 3; i < pixels.Length; i += 4)
                pixels[i] = 255;
            return pixels;
        }

        /// <summary>Disegna il cursore del mouse (se visibile) su una cattura con origine in originX/originY.</summary>
        public static void DrawCursor(D.Graphics g, int originX, int originY)
        {
            var info = new NativeMethods.CURSORINFO { cbSize = Marshal.SizeOf<NativeMethods.CURSORINFO>() };
            if (!NativeMethods.GetCursorInfo(ref info) || info.flags != 1 /* CURSOR_SHOWING */)
                return;
            if (!NativeMethods.GetIconInfo(info.hCursor, out var iconInfo))
                return;

            IntPtr hdc = g.GetHdc();
            try
            {
                NativeMethods.DrawIconEx(hdc,
                    info.ptScreenPos.X - iconInfo.xHotspot - originX,
                    info.ptScreenPos.Y - iconInfo.yHotspot - originY,
                    info.hCursor, 0, 0, 0, IntPtr.Zero, 3 /* DI_NORMAL */);
            }
            finally
            {
                g.ReleaseHdc(hdc);
                if (iconInfo.hbmMask != IntPtr.Zero) NativeMethods.DeleteObject(iconInfo.hbmMask);
                if (iconInfo.hbmColor != IntPtr.Zero) NativeMethods.DeleteObject(iconInfo.hbmColor);
            }
        }
    }
}
