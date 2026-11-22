using System.Net.Http.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using DOF.Contracts.Common;
using DOF.Contracts.Responses;

namespace DOF.Desktop.Services.Api;

public enum ConnectionState
{
    Checking,
    Online,
    Offline,
}

/// <summary>
/// Tracks whether the API is reachable. Polls periodically and is also updated by every API call,
/// so the indicator reacts as soon as a request fails or succeeds.
/// </summary>
public sealed partial class ConnectionMonitor(HttpClient http, TimeProvider? clock = null) : ObservableObject
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsOnline), nameof(IsOffline))]
    public partial ConnectionState State { get; private set; } = ConnectionState.Checking;

    [ObservableProperty]
    public partial string? ServerVersion { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial DateTimeOffset? LastCheckedAt { get; private set; }

    public bool IsOnline => State == ConnectionState.Online;

    public bool IsOffline => State == ConnectionState.Offline;

    public string StatusText => State switch
    {
        ConnectionState.Online => "Connected",
        ConnectionState.Offline => "Offline",
        _ => "Connecting…",
    };

    public async Task<bool> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await http.GetAsync("api/v1/system/info", ct);
            if (!response.IsSuccessStatusCode)
            {
                ReportFailure();
                return false;
            }

            var info = await response.Content.ReadFromJsonAsync<ApiResponse<SystemInfoResponse>>(ct);
            ServerVersion = info?.Data?.Version;
            ReportSuccess();
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            ReportFailure();
            return false;
        }
    }

    /// <summary>Checks immediately, then every <paramref name="interval"/> until cancelled.</summary>
    public async Task RunAsync(TimeSpan interval, CancellationToken ct)
    {
        await CheckAsync(ct);
        using var timer = new PeriodicTimer(interval, _clock);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await CheckAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    public void ReportSuccess() => Set(ConnectionState.Online);

    public void ReportFailure() => Set(ConnectionState.Offline);

    private void Set(ConnectionState state)
    {
        if (State != state)
        {
            AppLog.Info($"Connection state: {State} -> {state}");
        }

        State = state;
        LastCheckedAt = _clock.GetUtcNow();
    }
}
