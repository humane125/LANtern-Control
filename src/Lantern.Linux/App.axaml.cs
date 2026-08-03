using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Lantern.Linux;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var demoMode = desktop.Args?.Any(argument =>
                argument.Equals("--demo", StringComparison.OrdinalIgnoreCase)) == true;
            desktop.MainWindow = new MainWindow(demoMode);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
