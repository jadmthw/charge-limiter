using ChargeLimiter.Models;
using ChargeLimiter.Services;
using Xunit;

namespace ChargeLimiter.Tests;

public class SoftCapLogicTests
{
    [Fact]
    public void Triggers_WhenArmed_OnAc_AtOrAboveTarget()
    {
        var d = SoftCapLogic.Decide(80, onAc: true, currentlyArmed: true, target: 80, rearm: 75);
        Assert.Equal(SoftCapLogic.Decision.Trigger, d);
    }

    [Fact]
    public void DoesNotRetrigger_UntilRearmed()
    {
        var d = SoftCapLogic.Decide(90, onAc: true, currentlyArmed: false, target: 80, rearm: 75);
        Assert.Equal(SoftCapLogic.Decision.Hold, d);
    }

    [Fact]
    public void Rearms_WhenBelowRearm()
    {
        var d = SoftCapLogic.Decide(70, onAc: true, currentlyArmed: false, target: 80, rearm: 75);
        Assert.Equal(SoftCapLogic.Decision.Rearm, d);
    }

    [Fact]
    public void Rearms_WhenUnplugged_IfNotArmed()
    {
        var d = SoftCapLogic.Decide(95, onAc: false, currentlyArmed: false, target: 80, rearm: 75);
        Assert.Equal(SoftCapLogic.Decision.Rearm, d);
    }

    [Fact]
    public void Holds_WhenBatteryPercentUnknown()
    {
        var d = SoftCapLogic.Decide(null, onAc: true, currentlyArmed: true, target: 80, rearm: 75);
        Assert.Equal(SoftCapLogic.Decision.Hold, d);
    }

    [Fact]
    public void Holds_InHysteresisBand_WhileArmed()
    {
        var d = SoftCapLogic.Decide(77, onAc: true, currentlyArmed: true, target: 80, rearm: 75);
        Assert.Equal(SoftCapLogic.Decision.Hold, d);
    }
}

public class SoftCapMonitorTests
{
    private sealed class FakeBattery : IBatteryStatusService
    {
        public BatterySnapshot Snapshot { get; set; } = new()
        {
            Available = true,
            Percent = 50,
            PluggedIn = true,
            StatusText = "fake"
        };

        public Task<BatterySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Snapshot);
    }

    private sealed class FakeActions : ISoftCapActionRunner
    {
        public int Calls { get; private set; }
        public SoftCapAction LastAction { get; private set; }

        public Task<string> RunAsync(SoftCapAction action, int batteryPercent, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastAction = action;
            return Task.FromResult($"fake:{action}:{batteryPercent}");
        }
    }

    private sealed class MemStore : ISettingsStore
    {
        public SoftCapSettings Settings { get; set; } = new();
        public SoftCapSettings Load() => Settings;
        public void Save(SoftCapSettings settings) => Settings = settings;
    }

    [Fact]
    public async Task Monitor_Triggers_Then_NeedsRearm()
    {
        var battery = new FakeBattery
        {
            Snapshot = new BatterySnapshot
            {
                Available = true,
                Percent = 82,
                PluggedIn = true,
                Charging = true,
                StatusText = "82% AC"
            }
        };
        var actions = new FakeActions();
        var store = new MemStore
        {
            Settings = new SoftCapSettings
            {
                Enabled = true,
                TargetPercent = 80,
                RearmPercent = 75,
                Action = SoftCapAction.Notify,
                RunMonitorInBackground = false
            }
        };

        using var monitor = new SoftCapMonitor(battery, actions, store);
        monitor.UpdateSettings(store.Settings);

        var first = await monitor.TickAsync();
        Assert.True(first.Triggered);
        Assert.Equal(1, actions.Calls);
        Assert.False(monitor.Armed);

        var second = await monitor.TickAsync();
        Assert.False(second.Triggered);
        Assert.Equal(1, actions.Calls);

        battery.Snapshot = new BatterySnapshot
        {
            Available = true,
            Percent = 70,
            PluggedIn = true,
            StatusText = "70%"
        };
        var third = await monitor.TickAsync();
        Assert.True(monitor.Armed);
        Assert.Contains("Rearm", third.LastAction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateSettings_Persists_And_TogglesStartup()
    {
        var battery = new FakeBattery();
        var actions = new FakeActions();
        var store = new MemStore();
        var startup = new NullStartupRegistration();
        using var monitor = new SoftCapMonitor(battery, actions, store, startup);

        monitor.UpdateSettings(new SoftCapSettings
        {
            Enabled = true,
            TargetPercent = 80,
            RearmPercent = 70,
            Action = SoftCapAction.Sleep,
            RunAtLogin = true,
            RunMonitorInBackground = false
        });

        Assert.True(store.Settings.RunAtLogin);
        Assert.True(startup.IsRegistered);
        Assert.Equal(SoftCapAction.Sleep, store.Settings.Action);

        monitor.UpdateSettings(new SoftCapSettings
        {
            Enabled = false,
            TargetPercent = 80,
            RearmPercent = 70,
            RunAtLogin = false,
            RunMonitorInBackground = false
        });
        Assert.False(startup.IsRegistered);
    }
}

public class OrchestratorTests
{
    [Fact]
    public async Task Demo_ApplyEighty_SetsCustomWindow()
    {
        var orchestrator = new ChargeLimitOrchestrator(
            new IChargeLimitBackend[] { new DemoChargeLimitBackend() },
            new DemoBatteryStatusService());

        var apply = await orchestrator.ApplyEightyPercentAsync();
        Assert.True(apply.Success, apply.Message);

        var state = await orchestrator.RefreshAsync();
        Assert.NotNull(state.ActiveLimit);
        Assert.Equal(ChargeLimitMode.Custom, state.ActiveLimit!.Mode);
        Assert.Equal(75, state.ActiveLimit.StartPercent);
        Assert.Equal(80, state.ActiveLimit.StopPercent);
    }

    [Fact]
    public async Task Demo_Restore_SetsStandard()
    {
        var orchestrator = new ChargeLimitOrchestrator(
            new IChargeLimitBackend[] { new DemoChargeLimitBackend() },
            new DemoBatteryStatusService());

        _ = await orchestrator.ApplyEightyPercentAsync();
        var restore = await orchestrator.RestoreFullChargeAsync();
        Assert.True(restore.Success, restore.Message);

        var state = await orchestrator.RefreshAsync();
        Assert.Equal(ChargeLimitMode.Standard, state.ActiveLimit!.Mode);
    }
}
