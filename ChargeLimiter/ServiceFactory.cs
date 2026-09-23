using ChargeLimiter.Services;

namespace ChargeLimiter;

public static class ServiceFactory
{
    public static bool DemoMode { get; private set; }

    public static void Configure(string[] args)
    {
        DemoMode =
            args.Any(a => string.Equals(a, "--demo", StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(Environment.GetEnvironmentVariable("CHARGE_LIMITER_DEMO"), "1", StringComparison.OrdinalIgnoreCase);
    }

    public static AppServices Create()
    {
        var store = new JsonSettingsStore();
        IBatteryStatusService battery = DemoMode || !PlatformInfo.IsWindows
            ? new DemoBatteryStatusService()
            : new WindowsBatteryStatusService();

        IStartupRegistration startup = DemoMode || !PlatformInfo.IsWindows
            ? new NullStartupRegistration()
            : new StartupRegistration();

        var softCap = new SoftCapMonitor(battery, new SoftCapActionRunner(), store, startup);
        IChargeLimitOrchestrator dell = DemoMode || !PlatformInfo.IsWindows
            ? new ChargeLimitOrchestrator(
                new IChargeLimitBackend[] { new DemoChargeLimitBackend() },
                battery)
            : new ChargeLimitOrchestrator(
                new IChargeLimitBackend[]
                {
                    new DellWmiChargeLimitBackend(),
                    new CctkChargeLimitBackend()
                },
                battery);

        IBiosAttributeEnumerator bios = DemoMode || !PlatformInfo.IsWindows
            ? new EmptyBiosAttributeEnumerator()
            : new DellBiosAttributeEnumerator();

        ISmartChargingProbe probe = new SmartChargingProbe();

        return new AppServices(dell, softCap, bios, probe, battery);
    }
}

public sealed class AppServices
{
    public AppServices(
        IChargeLimitOrchestrator dell,
        ISoftCapMonitor softCap,
        IBiosAttributeEnumerator bios,
        ISmartChargingProbe probe,
        IBatteryStatusService battery)
    {
        Dell = dell;
        SoftCap = softCap;
        Bios = bios;
        Probe = probe;
        Battery = battery;
    }

    public IChargeLimitOrchestrator Dell { get; }
    public ISoftCapMonitor SoftCap { get; }
    public IBiosAttributeEnumerator Bios { get; }
    public ISmartChargingProbe Probe { get; }
    public IBatteryStatusService Battery { get; }
}
