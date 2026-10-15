using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;

// DllImport rather than LibraryImport: the latter needs AllowUnsafeBlocks for the whole project, for a handful of calls.
#pragma warning disable SYSLIB1054

namespace Signet.App.ScreenPicker;

/// <summary>Captures the virtual screen (all monitors) with GDI <c>BitBlt</c>, straight into memory.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsScreenCapturer : IScreenCapturer
{
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private const uint SrcCopy = 0x00CC0020;
    private const uint CaptureBlt = 0x40000000;

    public Task<ScreenSnapshot?> CaptureAsync() => Task.Run(Capture);

    private static ScreenSnapshot? Capture()
    {
        int x = GetSystemMetrics(SmXVirtualScreen);
        int y = GetSystemMetrics(SmYVirtualScreen);
        int width = GetSystemMetrics(SmCxVirtualScreen);
        int height = GetSystemMetrics(SmCyVirtualScreen);
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        nint screenDc = GetDC(0);
        if (screenDc == 0)
        {
            return null;
        }

        nint memoryDc = 0;
        nint bitmap = 0;
        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            bitmap = CreateCompatibleBitmap(screenDc, width, height);
            if (memoryDc == 0 || bitmap == 0)
            {
                return null;
            }

            nint previous = SelectObject(memoryDc, bitmap);
            bool copied = BitBlt(memoryDc, 0, 0, width, height, screenDc, x, y, SrcCopy | CaptureBlt) != 0;
            SelectObject(memoryDc, previous);
            if (!copied)
            {
                return null;
            }

            BitmapInfoHeader header = new()
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = width,
                Height = -height, // top-down
                Planes = 1,
                BitCount = 32,
            };
            byte[] pixels = new byte[checked(width * height * 4)];
            return GetDIBits(memoryDc, bitmap, 0, (uint)height, ref pixels[0], ref header, 0) == height
                ? new ScreenSnapshot(pixels, width, height, x, y)
                : null;
        }
        finally
        {
            if (bitmap != 0)
            {
                _ = DeleteObject(bitmap);
            }

            if (memoryDc != 0)
            {
                _ = DeleteDC(memoryDc);
            }

            _ = ReleaseDC(0, screenDc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColorUsed;
        public uint ColorImportant;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint dc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint dc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleBitmap(nint dc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint dc, nint obj);

    [DllImport("gdi32.dll")]
    private static extern int BitBlt(nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(nint dc, nint bitmap, uint startLine, uint lines, ref byte bits, ref BitmapInfoHeader info, uint usage);

    [DllImport("gdi32.dll")]
    private static extern int DeleteObject(nint obj);

    [DllImport("gdi32.dll")]
    private static extern int DeleteDC(nint dc);
}
