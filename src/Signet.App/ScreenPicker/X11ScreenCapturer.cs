using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;

// DllImport rather than LibraryImport: the latter needs AllowUnsafeBlocks for the whole project, for a handful of calls.
#pragma warning disable SYSLIB1054

namespace Signet.App.ScreenPicker;

/// <summary>Captures the X11 root window (all monitors) with <c>XGetImage</c>, straight into memory.</summary>
[SupportedOSPlatform("linux")]
internal sealed class X11ScreenCapturer : IScreenCapturer
{
    private const int ZPixmap = 2;

    public Task<ScreenSnapshot?> CaptureAsync() => Task.Run(Capture);

    private static ScreenSnapshot? Capture()
    {
        nint display = XOpenDisplay(0);
        if (display == 0)
        {
            return null;
        }

        nint image = 0;
        try
        {
            int screen = XDefaultScreen(display);
            int width = XDisplayWidth(display, screen);
            int height = XDisplayHeight(display, screen);
            nint root = XRootWindow(display, screen);
            image = XGetImage(display, root, 0, 0, (uint)width, (uint)height, nuint.MaxValue, ZPixmap);
            if (image == 0)
            {
                return null;
            }

            XImage info = Marshal.PtrToStructure<XImage>(image);
            if (info.BitsPerPixel != 32 || info.Data == 0 || info.Width != width || info.Height != height)
            {
                return null;
            }

            return new ScreenSnapshot(Convert(info), width, height, 0, 0);
        }
        finally
        {
            if (image != 0)
            {
                _ = XDestroyImage(image);
            }

            _ = XCloseDisplay(display);
        }
    }

    // Reorders the 32-bit pixels, whose channels are described by the masks, into BGRA.
    private static byte[] Convert(XImage image)
    {
        int redShift = Shift(image.RedMask);
        int greenShift = Shift(image.GreenMask);
        int blueShift = Shift(image.BlueMask);
        bool bigEndian = image.ByteOrder == 1;
        byte[] result = new byte[checked(image.Width * image.Height * 4)];
        byte[] row = new byte[image.BytesPerLine];
        for (int y = 0; y < image.Height; y++)
        {
            Marshal.Copy(image.Data + (y * image.BytesPerLine), row, 0, row.Length);
            for (int x = 0; x < image.Width; x++)
            {
                int i = x * 4;
                uint value = bigEndian
                    ? (uint)((row[i] << 24) | (row[i + 1] << 16) | (row[i + 2] << 8) | row[i + 3])
                    : (uint)(row[i] | (row[i + 1] << 8) | (row[i + 2] << 16) | (row[i + 3] << 24));
                int o = ((y * image.Width) + x) * 4;
                result[o] = (byte)((value & image.BlueMask) >> blueShift);
                result[o + 1] = (byte)((value & image.GreenMask) >> greenShift);
                result[o + 2] = (byte)((value & image.RedMask) >> redShift);
                result[o + 3] = 0xFF;
            }
        }

        return result;
    }

    private static int Shift(nuint mask) => mask == 0 ? 0 : System.Numerics.BitOperations.TrailingZeroCount((ulong)mask);

    // Only the leading fields of Xlib's XImage that are needed.
    [StructLayout(LayoutKind.Sequential)]
    private struct XImage
    {
        public int Width;
        public int Height;
        public int XOffset;
        public int Format;
        public nint Data;
        public int ByteOrder;
        public int BitmapUnit;
        public int BitmapBitOrder;
        public int BitmapPad;
        public int Depth;
        public int BytesPerLine;
        public int BitsPerPixel;
        public nuint RedMask;
        public nuint GreenMask;
        public nuint BlueMask;
    }

    [DllImport("libX11.so.6")]
    private static extern nint XOpenDisplay(nint name);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(nint display);

    [DllImport("libX11.so.6")]
    private static extern int XDefaultScreen(nint display);

    [DllImport("libX11.so.6")]
    private static extern int XDisplayWidth(nint display, int screen);

    [DllImport("libX11.so.6")]
    private static extern int XDisplayHeight(nint display, int screen);

    [DllImport("libX11.so.6")]
    private static extern nint XRootWindow(nint display, int screen);

    [DllImport("libX11.so.6")]
    private static extern nint XGetImage(nint display, nint drawable, int x, int y, uint width, uint height, nuint planeMask, int format);

    [DllImport("libX11.so.6")]
    private static extern int XDestroyImage(nint image);
}
