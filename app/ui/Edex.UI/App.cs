// SPDX-License-Identifier: GPL-3.0-only
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Edex.UI.Views;

namespace Edex.UI;

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
        Name = "eDEX-UI";
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow(EdexHost.Profile, EdexHost.SmokeTestDirectory);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
