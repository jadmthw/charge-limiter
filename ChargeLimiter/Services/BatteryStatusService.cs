using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ChargeLimiter.Models;

namespace ChargeLimiter.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsBatteryStatusService : IBatteryStatusService
{
    public Task<BatterySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT EstimatedChargeRemaining, BatteryStatus FROM Win32_Battery");
                using var results = searcher.Get();
                var battery = results.Cast<ManagementObject>().FirstOrDefault();
                if (battery is null)
                {
                    return new BatterySnapshot
                    {
                        Available = false,
                        StatusText = "No Win32_Battery instance found."
                    };
                }

                int? percent = battery["EstimatedChargeRemaining"] is null
                    ? null
                    : Convert.ToInt32(battery["EstimatedChargeRemaining"]);
                var statusCode = battery["BatteryStatus"] is null
                    ? 0
                    : Convert.ToInt32(battery["BatteryStatus"]);

                var charging = statusCode is 6 or 7 or 8 or 9;
                var pluggedIn = statusCode is 2 or 6 or 7 or 8 or 9;

                var text = statusCode switch
                {
                    1 => "On battery power",
                    2 => "On AC power",
                    3 => "Fully charged",
                    4 => "Low",
                    5 => "Critical",
                    6 => "Charging",
                    7 => "Charging (high)",
                    8 => "Charging (low)",
                    9 => "Charging (critical)",
                    11 => "Partially charged",
                    _ => $"Battery status code {statusCode}"
                };

                if (percent is int p)
                {
                    text = $"{text} · {p}%";
                }

                return new BatterySnapshot
                {
                    Available = true,
                    Percent = percent,
                    Charging = charging,
                    PluggedIn = pluggedIn,
                    StatusText = text
                };
            }
            catch (Exception ex)
            {
                return new BatterySnapshot
                {
                    Available = false,
                    StatusText = $"Could not read battery status: {ex.Message}"
                };
            }
        }, cancellationToken);
}

public sealed class PlatformInfo
{
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public static bool IsArm64 =>
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ||
        RuntimeInformation.OSArchitecture == Architecture.Arm64;

    public static string ArchitectureLabel =>
        $"{RuntimeInformation.OSDescription} / process={RuntimeInformation.ProcessArchitecture} / os={RuntimeInformation.OSArchitecture}";

    public static string Latitude7455Note =>
        "Latitude 7455 (Snapdragon / Windows on ARM): OEM Smart Charging may show a heart near 100% (voltage care). " +
        "Microsoft documents Smart charging as OEM firmware — no universal Windows 80% hard-limit API. " +
        "Dell PrimaryBattChargeCfg custom % is incompatible with ARM64. Use this app’s soft cap for controllable ~80% care.";

    public static string BuildPlatformNote()
    {
        if (!IsWindows)
        {
            return "Running off Windows — use the win-arm64 build on the Latitude 7455. " + Latitude7455Note;
        }

        return IsArm64
            ? Latitude7455Note
            : "Non-ARM process architecture detected. Prefer the win-arm64 build on Latitude 7455.";
    }

    public static string BuildNextSteps(bool writableBackend)
    {
        if (writableBackend)
        {
            return "A writable Dell charge-mode attribute was found (unusual on ARM64). Soft cap remains available.";
        }

        return
            "Pure-software path on Latitude 7455:\n" +
            "1. Enable Soft cap → target 80% / rearm 75% → action Notify (or Sleep/Hibernate) → Save & start.\n" +
            "2. Leave the app running (or set it to start with Windows) so the monitor can act on AC.\n" +
            "3. OEM Smart Charging (taskbar heart) is firmware-driven — not a user 80% slider you can force via powercfg.\n" +
            "4. BIOS Power → Battery Configuration may list ExpressCharge/etc.; Custom 75–80% remains unsupported on ARM64.";
    }
}
