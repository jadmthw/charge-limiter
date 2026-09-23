using Avalonia;
using System;

namespace ChargeLimiter;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        ServiceFactory.Configure(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
