// SPDX-License-Identifier: GPL-3.0-only
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Edex.Core.Input;
using Edex.Core.Ui;

namespace Edex.Windows;

internal sealed class EdexWindow : Window, IDisposable
{
    public const double MinimumWidth = 640;
    public const double MinimumHeight = 400;

    private readonly AppHost _host;
    private readonly Surface _surface;

    public EdexWindow()
    {
        _host = new AppHost();
        _surface = new Surface(_host);
        Content = _surface;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        Background = Brushes.Black;
        Title = "eDEX-UI";
        MinWidth = MinimumWidth;
        MinHeight = MinimumHeight;
        Loaded += OnLoaded;
        KeyDown += OnKeyDown;
        MouseDown += OnMouseDown;
        SizeChanged += OnSizeChanged;
    }

    public AppHost Host => _host;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyFullscreen();
        _host.Start();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _host.Resize(e.NewSize.Width, e.NewSize.Height);
        _surface.InvalidateVisual();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && e.KeyboardDevice.Modifiers == ModifierKeys.None)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }

        var text = KeyTranslator.Translate(e.Key, e.KeyboardDevice.Modifiers, false);
        if (text is { Length: > 0 })
        {
            _host.Send(text);
            e.Handled = true;
        }
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(_surface);
        if (!_host.Dashboard.HitTest(point.X, point.Y, out var hit))
        {
            return;
        }

        if (hit.Value is KeyDefinition key)
        {
            _host.SendKey(key);
        }
    }

    private void ToggleFullscreen()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        ApplyFullscreen();
    }

    public void Dispose() => _host.Dispose();

    private void ApplyFullscreen()
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
        }
    }
}
