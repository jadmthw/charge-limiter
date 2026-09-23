using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using ChargeLimiter.ViewModels;
using ChargeLimiter.Views;

namespace ChargeLimiter;

public partial class App : Application
{
    private TrayIcon? _tray;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = ServiceFactory.Create();
            var vm = new MainViewModel(services);
            desktop.MainWindow = new MainWindow { DataContext = vm };
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) =>
            {
                services.SoftCap.Stop();
                _tray?.Dispose();
            };

            TryInstallTray(desktop);
            _ = vm.InitializeCommand.ExecuteAsync(null);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void TryInstallTray(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            WindowIcon? icon = null;
            try
            {
                using var stream = AssetLoader.Open(new Uri("avares://ChargeLimiter/Assets/avalonia-logo.ico"));
                icon = new WindowIcon(stream);
            }
            catch
            {
                // tray without custom icon still works on some hosts
            }

            var menu = new NativeMenu();
            var show = new NativeMenuItem("Show Charge Limiter");
            show.Click += (_, _) =>
            {
                if (desktop.MainWindow is { } w)
                {
                    w.Show();
                    w.Activate();
                    w.WindowState = WindowState.Normal;
                }
            };
            var exit = new NativeMenuItem("Exit");
            exit.Click += (_, _) => desktop.Shutdown();
            menu.Add(show);
            menu.Add(exit);

            _tray = new TrayIcon
            {
                Icon = icon,
                ToolTipText = "Charge Limiter — soft cap monitor",
                IsVisible = true,
                Menu = menu
            };
            _tray.Clicked += (_, _) =>
            {
                if (desktop.MainWindow is { } w)
                {
                    w.Show();
                    w.Activate();
                }
            };

            if (desktop.MainWindow is Window main)
            {
                main.Closing += (_, e) =>
                {
                    // Close to tray so the soft-cap monitor keeps running.
                    e.Cancel = true;
                    main.Hide();
                };
            }
        }
        catch
        {
            // Headless / non-desktop: skip tray.
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
    }
}
