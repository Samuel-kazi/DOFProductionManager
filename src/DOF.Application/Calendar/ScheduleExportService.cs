using System.Globalization;
using DOF.Application.Common;
using DOF.Application.Common.Abstractions;
using DOF.Contracts.Common;
using DOF.Contracts.Responses;
using DOF.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DOF.Application.Calendar;

/// <summary>
/// One row of a schedule: a calendar item on one local day. An item spanning several days appears on
/// each of them, with <see cref="Start"/> only on its first day and <see cref="End"/> only on its last.
/// <see cref="Category"/> (Event, Recording, Deadline, Milestone, Release) picks the colour.
/// </summary>
public sealed record ScheduleEntry(
    DateOnly Day,
    TimeOnly? Start,
    TimeOnly? End,
    bool IsAllDay,
    bool ContinuesFromPreviousDay,
    bool ContinuesToNextDay,
    string Title,
    string Kind,
    string Category,
    string Status,
    string Priority,
    string? Location,
    string People);

/// <summary>Everything a schedule PDF shows. Days are inclusive and in <see cref="TimeZone"/>.</summary>
public sealed record ScheduleDocument(
    string Period,
    string Title,
    string RangeLabel,
    DateOnly FirstDay,
    DateOnly LastDay,
    DateOnly Today,
    string TimeZone,
    DateTime GeneratedAt,
    string GeneratedBy,
    string? FilterSummary,
    IReadOnlyList<ScheduleEntry> Entries);

/// <summary>Builds the day, week, month or year schedule from the aggregated calendar and renders it as a PDF.</summary>
public sealed class ScheduleExportService(
    CalendarService calendar,
    ISchedulePdfRenderer renderer,
    IApplicationDbContext db,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILogger<ScheduleExportService> logger)
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-GB");

    public async Task<(byte[] Pdf, string FileName)> ExportAsync(
        string? period, DateOnly? date, string? timeZone, CalendarQuery filters, CancellationToken ct)
    {
        var document = await BuildAsync(period, date, timeZone, filters, ct);
        var pdf = renderer.Render(document);
        logger.LogInformation("Schedule PDF ({Period}, {First}) exported by {UserId}: {Count} entries",
            document.Period, document.FirstDay, currentUser.UserId, document.Entries.Count);
        return (pdf, $"dof-schedule-{document.Period.ToLowerInvariant()}-{document.FirstDay:yyyy-MM-dd}.pdf");
    }

    public async Task<ScheduleDocument> BuildAsync(
        string? period, DateOnly? date, string? timeZone, CalendarQuery filters, CancellationToken ct)
    {
        var kind = ScheduleValues.Periods.FirstOrDefault(p => string.Equals(p, period ?? ScheduleValues.Week, StringComparison.OrdinalIgnoreCase))
                   ?? throw new DomainException($"Period must be one of: {string.Join(", ", ScheduleValues.Periods)}.");
        var zone = await calendar.ResolveZoneAsync(timeZone, ct);
        var today = calendar.Today(zone);
        var anchor = date ?? today;

        var (first, endExclusive, title, label) = kind switch
        {
            ScheduleValues.Day => (anchor, anchor.AddDays(1), "Daily schedule", anchor.ToString("dddd d MMMM yyyy", Culture)),
            ScheduleValues.Week => WeekRange(anchor),
            ScheduleValues.Month => MonthRange(anchor),
            _ => (new DateOnly(anchor.Year, 1, 1), new DateOnly(anchor.Year + 1, 1, 1), "Yearly schedule",
                anchor.Year.ToString(Culture)),
        };
        var last = endExclusive.AddDays(-1);

        var items = await calendar.GetItemsAsync(filters with
        {
            Start = ZonedTime.StartOfDay(first, zone),
            End = ZonedTime.StartOfDay(endExclusive, zone),
        }, CalendarService.MaxExportRangeDays, ct);

        var entries = items.SelectMany(i => Split(i, zone, first, last))
            .OrderBy(e => e.Day).ThenByDescending(e => e.IsAllDay).ThenBy(e => e.Start).ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var me = currentUser.UserId;
        var generatedBy = await db.Users.AsNoTracking().Where(u => u.Id == me).Select(u => u.FullName).SingleOrDefaultAsync(ct) ?? "DOF";
        return new ScheduleDocument(kind, title, label, first, last, today, zone.Id,
            TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime, generatedBy, await FilterSummaryAsync(filters, ct), entries);
    }

    private static (DateOnly, DateOnly, string, string) WeekRange(DateOnly anchor)
    {
        var monday = ZonedTime.StartOfWeek(anchor);
        var sunday = monday.AddDays(6);
        var label = monday.Month == sunday.Month
            ? $"{monday.Day}–{sunday.ToString("d MMMM yyyy", Culture)}"
            : monday.Year == sunday.Year
                ? $"{monday.ToString("d MMMM", Culture)} – {sunday.ToString("d MMMM yyyy", Culture)}"
                : $"{monday.ToString("d MMMM yyyy", Culture)} – {sunday.ToString("d MMMM yyyy", Culture)}";
        return (monday, monday.AddDays(7), "Weekly schedule", label);
    }

    private static (DateOnly, DateOnly, string, string) MonthRange(DateOnly anchor)
    {
        var first = new DateOnly(anchor.Year, anchor.Month, 1);
        return (first, first.AddMonths(1), "Monthly schedule", first.ToString("MMMM yyyy", Culture));
    }

    private static IEnumerable<ScheduleEntry> Split(CalendarItemResponse item, TimeZoneInfo zone, DateOnly first, DateOnly last)
    {
        var start = TimeZoneInfo.ConvertTime(item.StartAt, zone);
        var end = TimeZoneInfo.ConvertTime(item.EndAt ?? item.StartAt, zone);
        var startDay = DateOnly.FromDateTime(start.DateTime);
        var endDay = end > start ? DateOnly.FromDateTime(end.AddTicks(-1).DateTime) : startDay;
        var people = string.Join(", ", item.AssignedUsers.Select(a => a.FullName).Distinct());
        var (kind, category) = Describe(item.EventType);

        for (var day = startDay < first ? first : startDay; day <= endDay && day <= last; day = day.AddDays(1))
        {
            yield return new ScheduleEntry(day,
                item.IsAllDay || day != startDay ? null : TimeOnly.FromDateTime(start.DateTime),
                item.IsAllDay || day != endDay || item.EndAt is null ? null : TimeOnly.FromDateTime(end.DateTime),
                item.IsAllDay, day > startDay, day < endDay, item.Title, kind, category, Spaced(item.Status), item.Priority,
                item.Location, people);
        }
    }

    /// <summary>A readable label and colour category for each calendar item type.</summary>
    public static (string Kind, string Category) Describe(string eventType) => eventType switch
    {
        "Meeting" => ("Meeting", "Event"),
        "MinistryEvent" => ("Ministry event", "Event"),
        "Training" => ("Training", "Event"),
        "ExternalAppointment" => ("Appointment", "Event"),
        "RecordingSession" => ("Recording session", "Recording"),
        CalendarValues.RecordingItem => ("Recording", "Recording"),
        CalendarValues.TaskDeadlineItem => ("Task due", "Deadline"),
        CalendarValues.EditingDeadlineItem => ("Editing due", "Deadline"),
        CalendarValues.ReviewDeadlineItem => ("Review due", "Deadline"),
        CalendarValues.ApprovalDeadlineItem => ("Approval due", "Deadline"),
        CalendarValues.MilestoneItem => ("Milestone", "Milestone"),
        CalendarValues.ReleaseItem => ("Release", "Release"),
        _ => ("Event", "Event"),
    };

    private static string Spaced(string value) =>
        string.Concat(value.Select((c, i) => i > 0 && char.IsUpper(c) ? $" {char.ToLowerInvariant(c)}" : c.ToString()));

    private async Task<string?> FilterSummaryAsync(CalendarQuery q, CancellationToken ct)
    {
        var parts = new List<string>();
        if (q.AssignedUserId is { } who)
        {
            var name = await db.Users.AsNoTracking().Where(u => u.Id == who).Select(u => u.FullName).SingleOrDefaultAsync(ct);
            parts.Add($"assigned to {name ?? "a team member"}");
        }
        if (q.EventTypes is { Count: > 0 } types)
        {
            parts.Add(string.Join(", ", types.Select(t => Describe(t).Kind)));
        }
        if (q.Priorities is { Count: > 0 } priorities)
        {
            parts.Add($"priority {string.Join(", ", priorities)}");
        }
        if (q.Statuses is { Count: > 0 } statuses)
        {
            parts.Add($"status {string.Join(", ", statuses.Select(Spaced))}");
        }
        if (q.ProjectId is { } project)
        {
            parts.Add($"project {await db.Projects.AsNoTracking().Where(p => p.Id == project).Select(p => p.Name).SingleOrDefaultAsync(ct)}");
        }
        if (q.ShowId is { } show)
        {
            parts.Add($"show {await db.Shows.AsNoTracking().Where(s => s.Id == show).Select(s => s.Name).SingleOrDefaultAsync(ct)}");
        }
        if (q.IncludeCancelled)
        {
            parts.Add("including cancelled");
        }
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }
}
