using ChargeLimiter.Services;
using Xunit;

namespace ChargeLimiter.Tests;

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
        Assert.Equal(ChargeLimiter.Models.ChargeLimitMode.Custom, state.ActiveLimit!.Mode);
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
        Assert.Equal(ChargeLimiter.Models.ChargeLimitMode.Standard, state.ActiveLimit!.Mode);
    }
}
