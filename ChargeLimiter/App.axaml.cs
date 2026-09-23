using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ChargeLimiter.ViewModels;
using ChargeLimiter.Views;

namespace ChargeLimiter;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var orchestrator = ServiceFactory.CreateOrchestrator();
            var vm = new MainViewModel(orchestrator);
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm
            };
            _ = vm.InitializeCommand.ExecuteAsync(null);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
