// -----------------------------------------------------------------------------
// File: LightingShowcase.Composer.Avalonia/App.cs
// Purpose: Avalonia application bootstrap for the Lighting Composer desktop UI.
//
// Selects the dark Fluent theme and opens the main ComposerWindow when the desktop lifetime starts. This is the UI entry point reached after Program.Main.
// -----------------------------------------------------------------------------

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace LightingShowcase.Composer;

internal sealed class App : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new ComposerWindow(Program.StartupArguments);

        base.OnFrameworkInitializationCompleted();
    }
}
