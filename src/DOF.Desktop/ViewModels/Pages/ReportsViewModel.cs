using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DOF.Contracts.Common;
using DOF.Contracts.Responses;
using DOF.Desktop.Services;
using DOF.Desktop.Services.Api;

namespace DOF.Desktop.ViewModels.Pages;

public sealed record ReportOption(string Key, string Title);

/// <summary>Reports (spec 5.10) with their parameters, a table view that opens records, and CSV export.</summary>
public sealed partial class ReportsViewModel(ApiClient api, AuthSession session, UserClock clock) : PageViewModel
{
    public override string Title => "Reports";

    public bool CanView => session.CurrentUser?.Permissions.Contains("reports.view") == true;

    public IReadOnlyList<ReportOption> Reports { get; } = [.. ReportValues.Reports.Select(r => new ReportOption(r.Key, r.Title))];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesWeek), nameof(UsesMonth), nameof(UsesDays), nameof(UsesRange))]
    public partial ReportOption SelectedReport { get; set; } = null!;

    partial void OnSelectedReportChanged(ReportOption value) => _ = RunAsync();

    public bool UsesWeek => SelectedReport?.Key == "weekly";
    public bool UsesMonth => SelectedReport?.Key == "monthly";
    public bool UsesDays => SelectedReport?.Key == "upcoming-releases";
    public bool UsesRange => SelectedReport?.Key == "publication-history";

    [ObservableProperty] public partial DateTime? WeekOf { get; set; }
    [ObservableProperty] public partial DateTime? MonthOf { get; set; }
    [ObservableProperty] public partial decimal? Days { get; set; } = 30;
    [ObservableProperty] public partial DateTime? From { get; set; }
    [ObservableProperty] public partial DateTime? To { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReport), nameof(IsEmptyReport))]
    public partial ReportResponse? Report { get; private set; }

    public bool HasReport => Report is not null;
    public bool IsEmptyReport => Report is { Rows.Count: 0 };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExportMessage))]
    public partial string? ExportMessage { get; private set; }

    public bool HasExportMessage => ExportMessage is not null;

    private int _generation;

    public override Task LoadAsync()
    {
        if (!CanView)
        {
            ErrorMessage = "Reports need the reports.view permission. Ask an administrator if you need them.";
            return Task.CompletedTask;
        }
        WeekOf ??= clock.Today.ToDateTime(TimeOnly.MinValue);
        MonthOf ??= clock.Today.ToDateTime(TimeOnly.MinValue);
        From ??= clock.Today.AddDays(-90).ToDateTime(TimeOnly.MinValue);
        To ??= clock.Today.ToDateTime(TimeOnly.MinValue);
        if (SelectedReport is null)
        {
            SelectedReport = Reports[0]; // triggers the first run
            return Task.CompletedTask;
        }
        return RunAsync();
    }

    /// <summary>The report's URL with its parameters (without format).</summary>
    public string Path => SelectedReport.Key switch
    {
        "weekly" => $"api/v1/reports/weekly?date={DateOnly.FromDateTime(WeekOf ?? clock.Now):yyyy-MM-dd}",
        "monthly" => $"api/v1/reports/monthly?year={(MonthOf ?? clock.Now).Year}&month={(MonthOf ?? clock.Now).Month}",
        "upcoming-releases" => $"api/v1/reports/upcoming-releases?days={(int)(Days ?? 30)}",
        "publication-history" =>
            $"api/v1/reports/publication-history?from={DateOnly.FromDateTime(From ?? clock.Now):yyyy-MM-dd}&to={DateOnly.FromDateTime(To ?? clock.Now):yyyy-MM-dd}",
        var key => $"api/v1/reports/{key}",
    };

    [RelayCommand]
    public async Task RunAsync()
    {
        if (!CanView || SelectedReport is null)
        {
            return;
        }
        var generation = ++_generation;
        ExportMessage = null;
        await RunAsync(async () =>
        {
            var report = await api.GetAsync<ReportResponse>(Path);
            if (generation == _generation)
            {
                Report = report;
            }
        });
    }

    /// <summary>Downloads the CSV of the current report into <paramref name="destination"/>.</summary>
    public async Task ExportCsvAsync(Stream destination)
    {
        var separator = Path.Contains('?') ? '&' : '?';
        if (await RunAsync(async () =>
            {
                var bytes = await api.GetBytesAsync($"{Path}{separator}format=csv");
                await destination.WriteAsync(bytes);
            }))
        {
            ExportMessage = "Exported.";
            AppLog.Info($"Exported report {SelectedReport.Key} as CSV");
        }
    }

    public void ReportExportFailure(string message)
    {
        AppLog.Warning($"Report export failed: {message}");
        ErrorMessage = $"The file could not be saved: {message}";
    }

    public string SuggestedFileName => $"dof-{SelectedReport?.Key}-{clock.Today:yyyyMMdd}.csv";

    public Task OpenRowAsync(int index) =>
        Report?.Links.ElementAtOrDefault(index) is { } link && Navigator is { } navigator
            ? navigator.OpenRecordAsync(link.SourceType, link.SourceId)
            : Task.CompletedTask;
}
