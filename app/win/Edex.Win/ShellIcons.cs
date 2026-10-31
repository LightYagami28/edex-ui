// SPDX-License-Identifier: GPL-3.0-only
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Edex.Core.Files;

namespace Edex.Win;

/// <summary>Reads the icons Explorer shows for files and folders (SHGetFileInfo) and converts them to Avalonia bitmaps.</summary>
internal static class ShellIcons
{
    private static readonly Dictionary<string, IImage?> Cache = new(StringComparer.OrdinalIgnoreCase);

    // Files whose icon is stored in the file itself; everything else shares the icon of its extension.
    private static readonly HashSet<string> PerFile = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".lnk", ".ico", ".url", ".appref-ms", ".msc", ".cpl", ".scr" };

    public static IImage? Get(string path, FileEntryKind kind)
    {
        string ext = Path.GetExtension(path);
        bool perFile = kind == FileEntryKind.File && PerFile.Contains(ext);
        string key = kind == FileEntryKind.Directory ? "<dir>" : perFile ? path : "*" + ext.ToLowerInvariant();
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
            if (Cache.Count > 2000) Cache.Clear();
            var image = Load(perFile ? path : kind == FileEntryKind.Directory ? "folder" : "file" + ext, kind == FileEntryKind.Directory, !perFile);
            Cache[key] = image;
            return image;
        }
    }

    private static Bitmap? Load(string path, bool directory, bool byAttributes)
    {
        var info = new SHFILEINFOW();
        uint flags = SHGFI_ICON | SHGFI_LARGEICON | (byAttributes ? SHGFI_USEFILEATTRIBUTES : 0);
        uint attributes = directory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
        if (SHGetFileInfoW(path, attributes, ref info, (uint)Marshal.SizeOf<SHFILEINFOW>(), flags) == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            return null;
        try { return IconToBitmap(info.hIcon); }
        finally { DestroyIcon(info.hIcon); }
    }

    internal static Bitmap? IconToBitmap(IntPtr icon)
    {
        if (!GetIconInfo(icon, out var iconInfo)) return null;
        try
        {
            if (iconInfo.hbmColor == IntPtr.Zero) return null;
            var bm = new BITMAP();
            if (GetObjectW(iconInfo.hbmColor, Marshal.SizeOf<BITMAP>(), ref bm) == 0) return null;
            int width = bm.bmWidth, height = bm.bmHeight;
            var color = ReadBits(iconInfo.hbmColor, width, height);
            if (color is null) return null;

            bool hasAlpha = false;
            for (int i = 3; i < color.Length; i += 4) if (color[i] != 0) { hasAlpha = true; break; }
            if (!hasAlpha && iconInfo.hbmMask != IntPtr.Zero)
            {
                // Old-style icon: transparency comes from the AND mask.
                var mask = ReadBits(iconInfo.hbmMask, width, height);
                for (int i = 0; i < color.Length; i += 4)
                    color[i + 3] = mask is not null && mask[i] != 0 ? (byte)0 : (byte)255;
            }

            var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            using (var buffer = bitmap.Lock())
            {
                for (int row = 0; row < height; row++)
                    Marshal.Copy(color, row * width * 4, buffer.Address + row * buffer.RowBytes, width * 4);
            }
            return bitmap;
        }
        finally
        {
            if (iconInfo.hbmColor != IntPtr.Zero) DeleteObject(iconInfo.hbmColor);
            if (iconInfo.hbmMask != IntPtr.Zero) DeleteObject(iconInfo.hbmMask);
        }
    }

    private static byte[]? ReadBits(IntPtr hbitmap, int width, int height)
    {
        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height, // top-down
            biPlanes = 1,
            biBitCount = 32,
        };
        var pixels = new byte[width * height * 4];
        IntPtr dc = GetDC(IntPtr.Zero);
        try
        {
            return GetDIBits(dc, hbitmap, 0, (uint)height, pixels, ref header, 0) == 0 ? null : pixels;
        }
        finally { ReleaseDC(IntPtr.Zero, dc); }
    }

    private const uint SHGFI_ICON = 0x100, SHGFI_LARGEICON = 0x0, SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10, FILE_ATTRIBUTE_NORMAL = 0x80;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFOW
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot, yHotspot;
        public IntPtr hbmMask, hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfoW(string pszPath, uint dwFileAttributes, ref SHFILEINFOW psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
    [DllImport("gdi32.dll")] private static extern int GetObjectW(IntPtr h, int c, ref BITMAP pv);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint cLines, [Out] byte[] lpvBits, ref BITMAPINFOHEADER lpbmi, uint usage);
}
