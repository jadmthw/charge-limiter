using ChargeLimiter.Models;

namespace ChargeLimiter.Services;

/// <summary>
/// Pure hysteresis for the software soft-cap monitor.
/// Trigger when on AC and % >= target; rearm when unplugged or % <= rearm.
/// </summary>
public static class SoftCapLogic
{
    public enum Decision
    {
        Hold,
        Trigger,
        Rearm
    }

    public static Decision Decide(int? batteryPercent, bool? onAc, bool currentlyArmed, int target, int rearm)
    {
        if (batteryPercent is null)
        {
            return Decision.Hold;
        }

        // Off AC or below rearm → soft-cap is ready to fire again later.
        if (onAc != true || batteryPercent <= rearm)
        {
            return currentlyArmed ? Decision.Hold : Decision.Rearm;
        }

        if (currentlyArmed && batteryPercent >= target)
        {
            return Decision.Trigger;
        }

        return Decision.Hold;
    }
}

public interface ISoftCapActionRunner
{
    Task<string> RunAsync(SoftCapAction action, int batteryPercent, CancellationToken cancellationToken = default);
}

public interface ISoftCapMonitor
{
    SoftCapSettings Settings { get; }
    SoftCapStatus LastStatus { get; }
    bool Armed { get; }
    event EventHandler? StatusChanged;
    void UpdateSettings(SoftCapSettings settings);
    Task<SoftCapStatus> TickAsync(CancellationToken cancellationToken = default);
    void Start();
    void Stop();
}

public sealed class SoftCapMonitor : ISoftCapMonitor, IDisposable
{
    private readonly IBatteryStatusService _battery;
    private readonly ISoftCapActionRunner _actions;
    private readonly ISettingsStore _store;
    private readonly IStartupRegistration _startup;
    private readonly object _gate = new();
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    public SoftCapMonitor(
        IBatteryStatusService battery,
        ISoftCapActionRunner actions,
        ISettingsStore store,
        IStartupRegistration? startup = null)
    {
        _battery = battery;
        _actions = actions;
        _store = store;
        _startup = startup ?? new NullStartupRegistration();
        Settings = _store.Load();
        Armed = true;
        LastStatus = IdleStatus(Settings);
    }

    public SoftCapSettings Settings { get; private set; }
    public SoftCapStatus LastStatus { get; private set; }
    public bool Armed { get; private set; }
    public event EventHandler? StatusChanged;

    public void UpdateSettings(SoftCapSettings settings)
    {
        if (!settings.IsValid)
        {
            throw new ArgumentException("Target must be greater than rearm, and both must be sensible percents.");
        }

        Settings = settings;
        _store.Save(settings);
        try
        {
            _startup.SetEnabled(settings.RunAtLogin);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Soft-cap settings saved, but run-at-login failed: {ex.Message}", ex);
        }

        Armed = true;
        LastStatus = IdleStatus(settings);
        StatusChanged?.Invoke(this, EventArgs.Empty);

        if (settings.Enabled && settings.RunMonitorInBackground)
        {
            Start();
        }
        else
        {
            Stop();
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_loopTask is { IsCompleted: false })
            {
                return;
            }

            _loopCts = new CancellationTokenSource();
            _loopTask = Task.Run(() => LoopAsync(_loopCts.Token));
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _loopCts?.Cancel();
            _loopCts = null;
            _loopTask = null;
        }
    }

    public async Task<SoftCapStatus> TickAsync(CancellationToken cancellationToken = default)
    {
        var settings = Settings;
        var battery = await _battery.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);

        if (!settings.Enabled)
        {
            return Publish(new SoftCapStatus
            {
                Enabled = false,
                Headline = "Soft cap is off",
                Detail =
                    "Enable it to watch battery % on AC and fire a toast (or sleep/hibernate) at your target — " +
                    "so the pack does not sit at 100%. This is not a firmware charge-stop.",
                BatteryPercent = battery.Percent,
                OnAc = battery.PluggedIn,
                Armed = Armed,
                LastPollUtc = DateTimeOffset.UtcNow
            });
        }

        var decision = SoftCapLogic.Decide(
            battery.Percent,
            battery.PluggedIn,
            Armed,
            settings.TargetPercent,
            settings.RearmPercent);

        var lastAction = string.Empty;
        var triggered = false;

        switch (decision)
        {
            case SoftCapLogic.Decision.Rearm:
                Armed = true;
                lastAction = $"Rearmed (battery {battery.Percent}%, AC={battery.PluggedIn}).";
                break;
            case SoftCapLogic.Decision.Trigger:
                lastAction = await _actions.RunAsync(settings.Action, battery.Percent!.Value, cancellationToken)
                    .ConfigureAwait(false);
                Armed = false; // require rearm before firing again
                triggered = true;
                break;
            default:
                lastAction = Armed
                    ? $"Watching — will act at {settings.TargetPercent}% on AC ({settings.Action})."
                    : $"Waiting to rearm at ≤{settings.RearmPercent}% or unplug.";
                break;
        }

        var headline = triggered
            ? $"Soft cap fired at {battery.Percent}%"
            : Armed
                ? $"Soft cap armed ({settings.TargetPercent}% / rearm {settings.RearmPercent}%)"
                : "Soft cap waiting to rearm";

        return Publish(new SoftCapStatus
        {
            Enabled = true,
            Headline = headline,
            Detail =
                $"{battery.StatusText}. {lastAction} " +
                "Soft cap prevents idling at 100% by acting in software; charging hardware may still accept current until you unplug or sleep.",
            BatteryPercent = battery.Percent,
            OnAc = battery.PluggedIn,
            Armed = Armed,
            Triggered = triggered,
            LastAction = lastAction,
            LastPollUtc = DateTimeOffset.UtcNow
        });
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // keep looping
            }

            var delay = Math.Clamp(Settings.PollSeconds, 10, 600);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private SoftCapStatus Publish(SoftCapStatus status)
    {
        LastStatus = status;
        StatusChanged?.Invoke(this, EventArgs.Empty);
        return status;
    }

    private static SoftCapStatus IdleStatus(SoftCapSettings settings) =>
        new()
        {
            Enabled = settings.Enabled,
            Headline = settings.Enabled ? "Soft cap starting…" : "Soft cap is off",
            Detail = settings.Enabled
                ? "Waiting for first battery poll."
                : "Pure-software fallback for Latitude 7455 when firmware cannot expose a hard 80% stop.",
            Armed = true
        };

    public void Dispose() => Stop();
}
