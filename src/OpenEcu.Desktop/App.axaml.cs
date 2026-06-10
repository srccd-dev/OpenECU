using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using OpenEcu.App.Services;
using OpenEcu.App.ViewModels;
using OpenEcu.Desktop.Views;

namespace OpenEcu.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(new ConnectionFactory())
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
