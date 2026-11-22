using DOF.Contracts.Common;
using DOF.Contracts.Responses;
using DOF.Desktop.Services;
using DOF.Desktop.ViewModels.Calendar;
using DOF.Desktop.ViewModels.Work;

namespace DOF.Desktop.ViewModels.Shows;

public static class ShowLabels
{
    public static string Label(string code) => CalendarLabels.Humanize(code);

    /// <summary>Resource key for a show, season or episode status colour.</summary>
    public static string StatusBrushKey(string status) => status switch
    {
        "Published" or "Completed" or "Approved" or "ReadyForRelease" => "Brush.Online",
        "Cancelled" or "Archived" or "Ended" => "Brush.Offline",
        "InReview" or "AwaitingApproval" or "OnHiatus" or "PartiallyPublished" => "Brush.Checking",
        "Active" or "InProduction" or "Scheduled" or "Recorded" or "Editing" => "Brush.Cat.Meeting",
        _ => "Brush.TextMuted",
    };

    public static string Recording(DateTimeOffset? start, DateTimeOffset? end, UserClock clock)
    {
        if (start is not { } s)
        {
            return "Recording not scheduled";
        }
        var from = clock.ToLocal(s);
        var to = end is { } e ? clock.ToLocal(e) : from;
        return from.Date == to.Date ? $"{When(s, clock)}–{to:HH:mm}" : $"{When(s, clock)} – {to:ddd d MMM HH:mm}";
    }

    /// <summary>"Today 09:00", "Tomorrow 14:30", "Tue 7 Mar 09:00" (year added when not this year).</summary>
    public static string When(DateTimeOffset instant, UserClock clock)
    {
        var local = clock.ToLocal(instant);
        var today = clock.Today;
        var day = DateOnly.FromDateTime(local);
        var date = day == today ? "Today" : day == today.AddDays(1) ? "Tomorrow"
            : local.ToString(day.Year == today.Year ? "ddd d MMM" : "ddd d MMM yyyy");
        return $"{date} {local:HH:mm}";
    }

    public static string Episode(EpisodeSummaryResponse e) => e.Code.Length == 0 ? e.Title : $"{e.Code} · {e.Title}";
}

public sealed class ShowCardViewModel(ShowSummaryResponse show, UserClock clock)
{
    public ShowSummaryResponse Show { get; } = show;
    public string Name => Show.Name;
    public string TypeText => ShowLabels.Label(Show.ShowType);
    public string StatusText => ShowLabels.Label(Show.Status);
    public string StatusBrushKey => ShowLabels.StatusBrushKey(Show.Status);
    public string ProducerText => Show.Producer?.FullName ?? "No producer";
    public string CountsText =>
        $"{Plural(Show.SeasonCount, "season")} · {Plural(Show.EpisodeCount, "episode")} · {Show.EpisodesInProduction} in production";
    public string NextRecordingText => Show.NextRecordingAt is { } at
        ? $"Next recording: {Show.NextRecordingTitle} · {ShowLabels.When(at, clock)}"
        : "No recordings scheduled";

    private static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";
}

/// <summary>An episode row in a show's season list, on the dashboard, or in "my episodes".</summary>
public sealed class EpisodeRowViewModel(EpisodeSummaryResponse episode, UserClock clock)
{
    public EpisodeSummaryResponse Episode { get; } = episode;
    public Guid Id => Episode.Id;
    public string Title => ShowLabels.Episode(Episode);
    public string ShowName => Episode.ShowName;
    public string StatusText => ShowLabels.Label(Episode.Status);
    public string StatusBrushKey => ShowLabels.StatusBrushKey(Episode.Status);
    public string RecordingText => ShowLabels.Recording(Episode.RecordingStart, Episode.RecordingEnd, clock);
    public string ProducerText => Episode.Producer?.FullName ?? "No producer";
    public bool IsOverdue => Episode.IsOverdue;
    public bool IsCancelled => Episode.Status == ShowValues.Cancelled;
    public string? DeadlineText => Episode.NextDeadline is { } due
        ? $"{Episode.NextDeadlineLabel} due {WorkLabels.DueDate(due, clock.Today)}"
        : null;
    public bool HasDeadline => DeadlineText is not null;
}

/// <summary>A season heading with its episodes (or the show's episodes without a season).</summary>
public sealed class SeasonGroupViewModel(SeasonResponse? season, IReadOnlyList<EpisodeRowViewModel> episodes, bool canAddEpisodes)
{
    public SeasonResponse? Season { get; } = season;
    public string Heading => Season?.DisplayName ?? "Without a season";
    public string? DatesText => Season is null ? "One-off and special episodes" : (Season.StartDate, Season.EndDate) switch
    {
        ({ } s, { } e) => $"{s:d MMM yyyy} – {e:d MMM yyyy}",
        ({ } s, null) => $"From {s:d MMM yyyy}",
        (null, { } e) => $"Until {e:d MMM yyyy}",
        _ => null,
    };
    public string? StatusText => Season is null ? null : ShowLabels.Label(Season.Status);
    public bool HasStatus => Season is not null;
    public string StatusBrushKey => ShowLabels.StatusBrushKey(Season?.Status ?? string.Empty);
    public IReadOnlyList<EpisodeRowViewModel> Episodes { get; } = episodes;
    public bool HasNoEpisodes => Episodes.Count == 0;
    public bool CanAddEpisodes { get; } = canAddEpisodes && season?.Status != ShowValues.Cancelled;
    public bool CanEditSeason => Season?.CanEdit == true && canAddEpisodes;
    public string AddLabel => Season is null ? "Add one-off episode" : "Add episode";
}

/// <summary>A stage of the production timeline.</summary>
public sealed class StageViewModel(EpisodeStageResponse stage, DateOnly today)
{
    public string Name => stage.Stage;
    public string State => stage.State;
    public bool IsDone => stage.State == ShowValues.StageDone;
    public bool IsCurrent => stage.State == ShowValues.StageCurrent;
    public bool IsCancelled => stage.State == ShowValues.StageCancelled;
    public string Marker => IsDone ? "✓" : IsCancelled ? "✕" : IsCurrent ? "●" : "○";
    public string? DateText => stage.Date is { } d ? WorkLabels.DueDate(d, today) : null;
    public bool HasDate => DateText is not null;
}

public sealed record InfoLine(string Label, string Value);
