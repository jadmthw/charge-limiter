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

    public static IChargeLimitOrchestrator CreateOrchestrator()
    {
        if (DemoMode || !PlatformInfo.IsWindows)
        {
            return new ChargeLimitOrchestrator(
                new IChargeLimitBackend[] { new DemoChargeLimitBackend() },
                new DemoBatteryStatusService());
        }

        return new ChargeLimitOrchestrator(
            new IChargeLimitBackend[]
            {
                new DellWmiChargeLimitBackend(),
                new CctkChargeLimitBackend()
            },
            new WindowsBatteryStatusService());
    }
}
