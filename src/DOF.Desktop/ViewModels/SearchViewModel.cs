using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DOF.Contracts.Responses;
using DOF.Desktop.Services;
using DOF.Desktop.Services.Api;

namespace DOF.Desktop.ViewModels;

/// <summary>The top-bar global search: results as you type (from two characters), each opening its record.</summary>
public sealed partial class SearchViewModel(ApiClient api) : ObservableObject
{
    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(250);

    private CancellationTokenSource? _pending;

    public ObservableCollection<SearchResultResponse> Results { get; } = [];

    public IRecordNavigator? Navigator { get; set; }

    [ObservableProperty] public partial string Text { get; set; } = string.Empty;

    [ObservableProperty] public partial bool IsOpen { get; set; }

    [ObservableProperty] public partial string? Message { get; private set; }

    partial void OnTextChanged(string value)
    {
        _pending?.Cancel();
        if (value.Trim().Length < 2)
        {
            Results.Clear();
            IsOpen = false;
            return;
        }
        var cts = _pending = new CancellationTokenSource();
        _ = DebouncedAsync(cts.Token);
    }

    private async Task DebouncedAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(Delay, ct);
            await SearchAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    [RelayCommand]
    public async Task SearchAsync(CancellationToken ct = default)
    {
        var text = Text.Trim();
        if (text.Length < 2)
        {
            return;
        }
        try
        {
            var results = await api.GetAsync<List<SearchResultResponse>>($"api/v1/search?q={Uri.EscapeDataString(text)}", ct);
            if (ct.IsCancellationRequested || text != Text.Trim())
            {
                return;
            }
            Results.Clear();
            foreach (var r in results)
            {
                Results.Add(r);
            }
            Message = results.Count == 0 ? "Nothing found." : null;
            IsOpen = true;
        }
        catch (Exception ex) when (ex is ApiException or ApiUnavailableException)
        {
            Message = ex.Message;
            IsOpen = true;
            AppLog.Warning($"Search failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task OpenAsync(SearchResultResponse result)
    {
        IsOpen = false;
        Text = string.Empty;
        if (Navigator is { } navigator)
        {
            await navigator.OpenRecordAsync(result.SourceType, result.Id);
        }
    }
}
