using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Filee.App.Services;

/// <summary>Reads only the Windows shell's icon association for an extension; it never opens the source file.</summary>
internal static class WindowsFileTypeIconProvider
{
    private const uint FileAttributeNormal = 0x80;
    private const uint ShgfiIcon = 0x100;
    private const uint ShgfiSmallIcon = 0x1;
    private const uint ShgfiUseFileAttributes = 0x10;
    private const uint BiRgb = 0;
    private const uint DiNormal = 0x3;

    public static Bitmap? TryGet(string pathOrExtension)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var extension = Path.GetExtension(pathOrExtension);
        if (string.IsNullOrWhiteSpace(extension) && pathOrExtension.StartsWith(".", StringComparison.Ordinal))
            extension = pathOrExtension;
        if (string.IsNullOrWhiteSpace(extension)) return null;

        var info = new ShellFileInfo();
        var shellInfo = SHGetFileInfo("filee-icon" + extension, FileAttributeNormal, ref info,
            (uint)Marshal.SizeOf<ShellFileInfo>(), ShgfiIcon | ShgfiSmallIcon | ShgfiUseFileAttributes);
        if (shellInfo == IntPtr.Zero || info.Icon == IntPtr.Zero) return null;

        IntPtr dc = IntPtr.Zero;
        IntPtr bitmap = IntPtr.Zero;
        IntPtr previous = IntPtr.Zero;
        try
        {
            const int size = 32;
            dc = CreateCompatibleDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return null;
            var header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = size,
                Height = -size,
                Planes = 1,
                BitCount = 32,
                Compression = BiRgb,
                SizeImage = size * size * 4,
            };
            bitmap = CreateDIBSection(dc, ref header, 0, out var pixels, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || pixels == IntPtr.Zero) return null;
            previous = SelectObject(dc, bitmap);
            Marshal.Copy(new byte[size * size * 4], 0, pixels, size * size * 4);
            if (!DrawIconEx(dc, 0, 0, info.Icon, size, size, 0, IntPtr.Zero, DiNormal)) return null;

            var bytes = new byte[size * size * 4];
            Marshal.Copy(pixels, bytes, 0, bytes.Length);
            // GDI returns premultiplied channels. Avalonia's pointer-backed Bitmap constructor expects straight alpha.
            for (var i = 0; i < bytes.Length; i += 4)
            {
                var alpha = bytes[i + 3];
                if (alpha is 0 or 255) continue;
                for (var channel = 0; channel < 3; channel++)
                    bytes[i + channel] = (byte)Math.Min(255, (bytes[i + channel] * 255 + alpha / 2) / alpha);
            }
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Unpremul, handle.AddrOfPinnedObject(),
                    new PixelSize(size, size), new Vector(96, 96), size * 4);
            }
            finally { handle.Free(); }
        }
        finally
        {
            if (previous != IntPtr.Zero && dc != IntPtr.Zero) SelectObject(dc, previous);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (dc != IntPtr.Zero) DeleteDC(dc);
            DestroyIcon(info.Icon);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
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
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHGetFileInfoW")]
    private static extern IntPtr SHGetFileInfo(string path, uint fileAttributes, ref ShellFileInfo info,
        uint infoSize, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int width, int height,
        uint step, IntPtr flickerFreeDraw, uint flags);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader info, uint usage,
        out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr dc);
}
