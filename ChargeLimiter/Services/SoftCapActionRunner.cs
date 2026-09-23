using System.Diagnostics;
using System.Runtime.InteropServices;
using ChargeLimiter.Models;

namespace ChargeLimiter.Services;

public sealed class SoftCapActionRunner : ISoftCapActionRunner
{
    private DateTimeOffset _lastNotifyUtc = DateTimeOffset.MinValue;

    public Task<string> RunAsync(SoftCapAction action, int batteryPercent, CancellationToken cancellationToken = default) =>
        action switch
        {
            SoftCapAction.Sleep => SuspendAsync(hibernate: false),
            SoftCapAction.Hibernate => SuspendAsync(hibernate: true),
            _ => NotifyAsync(batteryPercent)
        };

    private Task<string> NotifyAsync(int batteryPercent)
    {
        if (DateTimeOffset.UtcNow - _lastNotifyUtc < TimeSpan.FromMinutes(10))
        {
            return Task.FromResult("Toast suppressed (rate-limited). Soft cap still considered fired.");
        }

        _lastNotifyUtc = DateTimeOffset.UtcNow;

        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult($"Notify: battery {batteryPercent}% on AC (demo/non-Windows).");
        }

        try
        {
            // msg.exe is present on Windows client and needs no WinRT packaging.
            Process.Start(new ProcessStartInfo
            {
                FileName = "msg.exe",
                Arguments =
                    $"* /TIME:30 \"Charge Limiter soft cap: battery at {batteryPercent}% on AC. " +
                    "Unplug or sleep to avoid sitting near 100%.\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
            return Task.FromResult($"Notified user (battery {batteryPercent}% on AC).");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Notify attempted (battery {batteryPercent}%): {ex.Message}");
        }
    }

    private static Task<string> SuspendAsync(bool hibernate)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(hibernate
                ? "Hibernate requested (demo/non-Windows — no-op)."
                : "Sleep requested (demo/non-Windows — no-op).");
        }

        try
        {
            var ok = SetSuspendState(hibernate, false, false);
            return Task.FromResult(ok
                ? (hibernate ? "Hibernate requested." : "Sleep requested.")
                : "Suspend request returned false.");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Suspend failed: {ex.Message}");
        }
    }

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);
}
