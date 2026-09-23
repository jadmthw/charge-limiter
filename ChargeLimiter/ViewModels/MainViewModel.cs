using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChargeLimiter.Services;

namespace ChargeLimiter.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IChargeLimitOrchestrator _orchestrator;

    public MainViewModel(IChargeLimitOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
        StatusHeadline = "Reading charge configuration…";
        StatusDetail = "Probing Dell WMI and Command | Configure backends.";
        IsLoading = true;
    }

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private bool _isSupported;
    [ObservableProperty] private string _statusHeadline = string.Empty;
    [ObservableProperty] private string _statusDetail = string.Empty;
    [ObservableProperty] private string _batteryText = "Battery: —";
    [ObservableProperty] private string _limitText = "Limit: —";
    [ObservableProperty] private string _backendText = "Backend: —";
    [ObservableProperty] private string _platformNote = string.Empty;
    [ObservableProperty] private string _nextSteps = string.Empty;
    [ObservableProperty] private string _actionMessage = string.Empty;
    [ObservableProperty] private bool _hasActionMessage;
    [ObservableProperty] private bool _actionSucceeded;

    public ObservableCollection<string> ProbeLines { get; } = new();

    public bool CanMutate => !IsLoading && !IsBusy && IsSupported;

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(CanMutate));
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanMutate));
    partial void OnIsSupportedChanged(bool value) => OnPropertyChanged(nameof(CanMutate));

    [RelayCommand]
    public async Task InitializeAsync()
    {
        await RefreshCoreAsync().ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        await RefreshCoreAsync().ConfigureAwait(true);
    }

    private bool CanRefresh() => !IsLoading && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanMutateGuard))]
    private async Task ApplyEightyAsync()
    {
        await RunMutationAsync(() => _orchestrator.ApplyEightyPercentAsync()).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanMutateGuard))]
    private async Task RestoreFullAsync()
    {
        await RunMutationAsync(() => _orchestrator.RestoreFullChargeAsync()).ConfigureAwait(true);
    }

    private bool CanMutateGuard() => CanMutate;

    private async Task RunMutationAsync(Func<Task<Models.OperationResult>> action)
    {
        IsBusy = true;
        HasActionMessage = false;
        ApplyEightyCommand.NotifyCanExecuteChanged();
        RestoreFullCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();

        try
        {
            var result = await action().ConfigureAwait(true);
            ActionSucceeded = result.Success;
            ActionMessage = result.Message;
            HasActionMessage = true;
            await RefreshCoreAsync(preserveActionMessage: true).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ActionSucceeded = false;
            ActionMessage = ex.Message;
            HasActionMessage = true;
        }
        finally
        {
            IsBusy = false;
            ApplyEightyCommand.NotifyCanExecuteChanged();
            RestoreFullCommand.NotifyCanExecuteChanged();
            RefreshCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task RefreshCoreAsync(bool preserveActionMessage = false)
    {
        IsLoading = true;
        HasError = false;
        if (!preserveActionMessage)
        {
            HasActionMessage = false;
        }

        StatusHeadline = "Reading charge configuration…";
        StatusDetail = "Probing Dell WMI and Command | Configure backends.";
        RefreshCommand.NotifyCanExecuteChanged();
        ApplyEightyCommand.NotifyCanExecuteChanged();
        RestoreFullCommand.NotifyCanExecuteChanged();

        try
        {
            var state = await _orchestrator.RefreshAsync().ConfigureAwait(true);
            BatteryText = $"Battery: {state.Battery.StatusText}";
            PlatformNote = state.PlatformNote;
            NextSteps = state.NextSteps;
            ProbeLines.Clear();
            foreach (var probe in state.Probes)
            {
                var mark = probe.Available ? "ready" : "missing";
                ProbeLines.Add($"{probe.Name}: {mark} — {probe.Detail}");
            }

            if (state.ActiveLimit is { IsSupported: true } limit)
            {
                IsSupported = true;
                HasError = false;
                BackendText = $"Backend: {limit.BackendName}";
                LimitText = $"Limit: {limit.Summary}";
                StatusHeadline = limit.Mode == Models.ChargeLimitMode.Custom && limit.StopPercent is <= 85
                    ? "Charge limit is active"
                    : "Charge configuration available";
                StatusDetail = limit.Warning is { Length: > 0 }
                    ? $"{limit.Detail}\n{limit.Warning}"
                    : limit.Detail ?? limit.Summary;
            }
            else
            {
                IsSupported = false;
                HasError = true;
                BackendText = "Backend: none usable";
                LimitText = "Limit: not available";
                StatusHeadline = "Cannot set a software 80% limit on this PC";
                StatusDetail = state.Error ??
                               "Dell charge-threshold interfaces were not found or rejected the query.";
            }
        }
        catch (Exception ex)
        {
            IsSupported = false;
            HasError = true;
            StatusHeadline = "Something went wrong while reading status";
            StatusDetail = ex.Message;
            BatteryText = "Battery: unavailable";
            LimitText = "Limit: unavailable";
            BackendText = "Backend: unavailable";
        }
        finally
        {
            IsLoading = false;
            RefreshCommand.NotifyCanExecuteChanged();
            ApplyEightyCommand.NotifyCanExecuteChanged();
            RestoreFullCommand.NotifyCanExecuteChanged();
        }
    }
}
