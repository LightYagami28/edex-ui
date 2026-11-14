// SPDX-License-Identifier: GPL-3.0-only
using System.Windows;
using System.Windows.Media;

namespace Edex.Windows;

internal sealed class Surface(AppHost host) : FrameworkElement
{
    public AppHost Host { get; } = host;

    protected override void OnRender(DrawingContext drawingContext)
    {
        var scene = Host.Dashboard.BuildScene(DateTimeOffset.Now);
        Host.Renderer.Draw(drawingContext, scene, ActualWidth, ActualHeight);
    }
}
