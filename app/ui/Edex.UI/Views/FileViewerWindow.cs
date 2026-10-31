// SPDX-License-Identifier: GPL-3.0-only
using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Docnet.Core;
using Docnet.Core.Models;
using Edex.Core.Files;

namespace Edex.UI.Views;

/// <summary>Built-in viewer for images, text and PDF files. Audio and video are handed to the system player.</summary>
public sealed class FileViewerWindow : Window
{
    public const long MaxTextBytes = 2 * 1024 * 1024;
    private static readonly object s_pdfLock = new();

    private readonly Palette _palette;
    private readonly string _path;
    private readonly ContentControl _body = new();
    private readonly TextBlock _status;
    private int _pdfPage;
    private int _pdfPages;

    /// <summary>Returns true when the built-in viewer can show this category.</summary>
    public static bool CanView(FileCategory category) =>
        category is FileCategory.Image or FileCategory.Pdf || FileTypes.IsTextLike(category);

    public FileViewerWindow(Palette palette, string path)
    {
        _palette = palette;
        _path = path;
        Title = $"{Path.GetFileName(path)} — eDEX-UI";
        Width = 960;
        Height = 720;
        Background = palette.BackgroundBrush;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var header = new DockPanel { Margin = new Thickness(12, 10) };
        var system = new Button { Content = "Open with system app", Foreground = palette.AccentBrush, Background = palette.GlowBrush };
        system.Click += (_, _) => OpenWithSystem(path);
        DockPanel.SetDock(system, Dock.Right);
        header.Children.Add(system);
        _status = Ui.Text("", palette.MutedBrush, 11);
        _status.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(_status, Dock.Right);
        _status.Margin = new Thickness(0, 0, 12, 0);
        header.Children.Add(_status);
        header.Children.Add(Ui.Text(path, palette.AccentBrush, 12, FontWeight.SemiBold).Also(t => t.TextTrimming = TextTrimming.PathSegmentEllipsis));

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(new Border
        {
            Margin = new Thickness(12, 0, 12, 12),
            BorderBrush = palette.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Child = _body,
        });
        Content = root;
        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape) Close();
            else if (_pdfPages > 0 && e.Key is Avalonia.Input.Key.Right or Avalonia.Input.Key.PageDown) ShowPdfPage(_pdfPage + 1);
            else if (_pdfPages > 0 && e.Key is Avalonia.Input.Key.Left or Avalonia.Input.Key.PageUp) ShowPdfPage(_pdfPage - 1);
        };
        Load();
    }

    /// <summary>What the viewer ended up showing ("image 800x600", "pdf 3 pages", "text 120 lines", or an error).</summary>
    public string Summary { get; private set; } = "";

    private void Load()
    {
        var category = FileTypes.Classify(Path.GetFileName(_path), FileEntryKind.File);
        try
        {
            if (category == FileCategory.Image) LoadImage();
            else if (category == FileCategory.Pdf) LoadPdf();
            else LoadText();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException or Docnet.Core.Exceptions.DocnetException or DllNotFoundException)
        {
            Summary = "error: " + e.Message;
            _body.Content = Ui.Text("Cannot open this file: " + e.Message, _palette.ForegroundBrush, 13).Also(t => t.Margin = new Thickness(16));
        }
    }

    private void LoadImage()
    {
        using var stream = File.OpenRead(_path);
        var bitmap = new Bitmap(stream);
        Summary = $"image {bitmap.PixelSize.Width}x{bitmap.PixelSize.Height}";
        _status.Text = Summary;
        _body.Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = new Image { Source = bitmap, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly },
        };
    }

    private void LoadText()
    {
        var info = new FileInfo(_path);
        var bytes = new byte[Math.Min(info.Length, MaxTextBytes)];
        using (var stream = info.OpenRead()) stream.ReadExactly(bytes);
        string text = TextPreview.Decode(bytes);
        int lines = text.Count(c => c == '\n') + 1;
        Summary = $"text {lines} lines";
        _status.Text = info.Length > MaxTextBytes ? $"{lines} lines (first {MaxTextBytes / 1024 / 1024} MB)" : $"{lines} lines";
        _body.Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = new SelectableTextBlock
            {
                Text = text,
                FontFamily = new FontFamily(_palette.Theme.FontFamily),
                FontSize = 13,
                Foreground = _palette.ForegroundBrush,
                Margin = new Thickness(12),
            },
        };
    }

    private void LoadPdf()
    {
        lock (s_pdfLock)
        {
            using var reader = DocLib.Instance.GetDocReader(_path, new PageDimensions(1.0));
            _pdfPages = reader.GetPageCount();
        }
        Summary = $"pdf {_pdfPages} pages";
        ShowPdfPage(0);
    }

    private void ShowPdfPage(int page)
    {
        if (_pdfPages == 0) return;
        _pdfPage = Math.Clamp(page, 0, _pdfPages - 1);
        var bitmap = RenderPdfPage(_path, _pdfPage, 2.0);
        _status.Text = $"page {_pdfPage + 1} / {_pdfPages}   ← →";
        _body.Content = new ScrollViewer
        {
            Background = Brushes.White,
            Content = new Image { Source = bitmap, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly },
        };
    }

    /// <summary>Renders one PDF page with PDFium at the given scale (1.0 = 72 dpi).</summary>
    public static WriteableBitmap RenderPdfPage(string path, int page, double scale)
    {
        byte[] bgra;
        int width, height;
        lock (s_pdfLock)
        {
            using var reader = DocLib.Instance.GetDocReader(path, new PageDimensions(scale));
            using var pageReader = reader.GetPageReader(page);
            width = pageReader.GetPageWidth();
            height = pageReader.GetPageHeight();
            bgra = pageReader.GetImage();
        }
        // PDFium leaves the page background transparent; composite it on white.
        for (int i = 0; i < bgra.Length; i += 4)
        {
            int a = bgra[i + 3];
            if (a == 255) continue;
            bgra[i] = (byte)(bgra[i] * a / 255 + 255 - a);
            bgra[i + 1] = (byte)(bgra[i + 1] * a / 255 + 255 - a);
            bgra[i + 2] = (byte)(bgra[i + 2] * a / 255 + 255 - a);
            bgra[i + 3] = 255;
        }
        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (var buffer = bitmap.Lock())
        {
            for (int y = 0; y < height; y++)
                System.Runtime.InteropServices.Marshal.Copy(bgra, y * width * 4, buffer.Address + y * buffer.RowBytes, width * 4);
        }
        return bitmap;
    }

    /// <summary>Opens a file with the default application of the OS (used for audio and video).</summary>
    public static bool OpenWithSystem(string path)
    {
        try
        {
            ProcessStartInfo psi;
            if (OperatingSystem.IsWindows()) psi = new ProcessStartInfo(path) { UseShellExecute = true };
            else
            {
                psi = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open") { UseShellExecute = false };
                psi.ArgumentList.Add(path);
            }
            using var process = Process.Start(psi);
            return process is not null || OperatingSystem.IsWindows();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
