using System.Globalization;
using DOF.Contracts.Responses;
using DOF.Desktop.Services;

namespace DOF.Desktop.ViewModels.Work;

public static class WorkLabels
{
    public static string Status(string value) => value switch
    {
        "NotStarted" => "Not started",
        "InProgress" => "In progress",
        "AwaitingReview" => "Awaiting review",
        "OnHold" => "On hold",
        _ => value,
    };

    public static string? EpisodeName(EpisodeRefResponse? e) =>
        e is null ? null : $"{e.ShowName} {e.Code}".TrimEnd() + $": {e.Title}";

    public static string ProjectType(string value) => value switch
    {
        "ShowProduction" => "Show production",
        "MusicProduction" => "Music production",
        _ => value,
    };

    /// <summary>Resource key for a status colour.</summary>
    public static string StatusBrushKey(string status) => status switch
    {
        "Completed" => "Brush.Online",
        "Blocked" or "Cancelled" => "Brush.Offline",
        "AwaitingReview" or "OnHold" => "Brush.Checking",
        "InProgress" or "Active" => "Brush.Cat.Meeting",
        _ => "Brush.TextMuted",
    };

    public static string Due(DateTimeOffset? due, UserClock clock)
    {
        if (due is null)
        {
            return "No due date";
        }

        var local = clock.ToLocal(due.Value);
        var today = clock.Today;
        var day = DateOnly.FromDateTime(local);
        var when = day == today ? "Today" : day == today.AddDays(1) ? "Tomorrow" : day == today.AddDays(-1) ? "Yesterday"
            : local.ToString(day.Year == today.Year ? "ddd d MMM" : "d MMM yyyy");
        return $"{when} {local:HH:mm}";
    }

    public static string DueDate(DateOnly due, DateOnly today) =>
        due == today ? "Today" : due == today.AddDays(1) ? "Tomorrow" : due == today.AddDays(-1) ? "Yesterday"
        : due.ToString(due.Year == today.Year ? "ddd d MMM" : "d MMM yyyy");

    /// <summary>History entry as one line, with ISO timestamps in the summary shown in the user's timezone.</summary>
    public static string History(ActivityEntryResponse entry, UserClock clock)
    {
        var summary = entry.Summary;
        foreach (var value in entry.OldValues.Values.Concat(entry.NewValues.Values).OfType<string>().Distinct())
        {
            if (value.Contains('T') && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant))
            {
                summary = summary.Replace(value, clock.ToLocal(instant).ToString("d MMM yyyy HH:mm"));
            }
            else if (DateOnly.TryParseExact(value, "yyyy-MM-dd", out var date))
            {
                summary = summary.Replace(value, date.ToString("d MMM yyyy"));
            }
        }
        foreach (var code in new[] { "NotStarted", "InProgress", "AwaitingReview", "OnHold" })
        {
            summary = summary.Replace(code, Status(code));
        }
        return summary;
    }
}

public sealed record HistoryLine(string Summary, string Meta)
{
    public static HistoryLine From(ActivityEntryResponse entry, UserClock clock) => new(
        WorkLabels.History(entry, clock),
        $"{entry.User?.FullName ?? "System"} · {clock.ToLocal(entry.CreatedAt):d MMM yyyy HH:mm}");
}
