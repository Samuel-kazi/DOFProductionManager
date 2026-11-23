using DOF.Application.Activity;
using DOF.Application.Common;
using DOF.Application.Common.Abstractions;
using DOF.Application.Common.Exceptions;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Domain.Entities;
using DOF.Domain.Enums;
using DOF.Domain.Exceptions;
using DOF.Domain.Rules;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DOF.Application.Calendar;

/// <summary>
/// Weekly session series such as a Monday-to-Friday devotion: one request plans the whole week, with a
/// theme per day and who is sharing. Each day is an ordinary calendar event (reminders, Google invites,
/// PDFs and the calendar treat it like any other); the week keeps them together so they can be edited at once.
/// </summary>
public sealed class SessionWeekService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IOptions<OrganisationOptions> organisation,
    ActivityRecorder activity,
    ILogger<SessionWeekService> logger)
{
    public const int ListLimit = 50;

    /// <summary>Earlier weeks, newest first: by name (any case) and/or only those starting before a date.</summary>
    public async Task<IReadOnlyList<SessionWeekSummaryResponse>> ListAsync(string? name, DateOnly? before, CancellationToken ct)
    {
        var weeks = db.SessionWeeks.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(name))
        {
            var wanted = name.Trim().ToLowerInvariant();
            weeks = weeks.Where(w => w.Name.ToLower() == wanted);
        }
        if (before is { } b)
        {
            weeks = weeks.Where(w => w.WeekStart < b);
        }
        return await weeks.OrderByDescending(w => w.WeekStart).ThenByDescending(w => w.CreatedAt).Take(ListLimit)
            .Select(w => new SessionWeekSummaryResponse(w.Id, w.Name, w.WeekStart, w.Speaker))
            .ToListAsync(ct);
    }

    public async Task<SessionWeekResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var week = await db.SessionWeeks.AsNoTracking().SingleOrDefaultAsync(w => w.Id == id, ct)
                   ?? throw new NotFoundException("Session week", id);
        var days = await db.CalendarEvents.AsNoTracking()
            .Where(e => e.SessionWeekId == id && e.Status != CalendarEventStatus.Cancelled)
            .OrderBy(e => e.StartAt).ToListAsync(ct);
        return new SessionWeekResponse(week.Id, week.Name, week.WeekStart, week.EventType.ToString(), week.Location, week.MeetingUrl,
            week.Speaker,
            [.. days.Select(e => new SessionDayResponse(e.Id, e.StartAt, e.EndAt, e.Theme,
                e.Speaker == week.Speaker ? null : e.Speaker, e.Title, e.Status.ToString()))],
            week.Version, currentUser.HasPermission(Permissions.CalendarManage));
    }

    public async Task<SessionWeekResponse> CreateAsync(SessionWeekRequest request, CancellationToken ct)
    {
        EnsureCanManage();
        var week = SessionWeek.Create(request.Name, request.WeekStart, Type(request), request.Location, request.MeetingUrl, request.Speaker);
        EnsureDaysInWeek(week, request.Days);
        db.SessionWeeks.Add(week);
        foreach (var day in request.Days)
        {
            AddDay(week, day);
        }
        activity.Record(ActivityEntities.SessionWeek, week.Id, ActivityActions.Created, null,
            new { week.Name, week.WeekStart, week.Speaker, Days = request.Days.Count });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Session week {WeekId} ({Name}, {WeekStart}) with {Days} days created by {UserId}",
            week.Id, week.Name, week.WeekStart, request.Days.Count, currentUser.UserId);
        return await GetAsync(week.Id, ct);
    }

    /// <summary>Applies the week's shared details to every day, updates and adds days, and cancels days left out.</summary>
    public async Task<SessionWeekResponse> UpdateAsync(Guid id, SessionWeekRequest request, CancellationToken ct)
    {
        EnsureCanManage();
        var week = await db.SessionWeeks.SingleOrDefaultAsync(w => w.Id == id, ct) ?? throw new NotFoundException("Session week", id);
        if (request.Version is { } version)
        {
            if (week.Version != version)
            {
                throw new ConflictException("This week was changed by someone else. Reload it and try again.");
            }
            db.Entry(week).Property(w => w.Version).OriginalValue = version;
        }
        var before = new { week.Name, week.WeekStart, week.Speaker, week.Location };
        week.Update(request.Name, request.WeekStart, Type(request), request.Location, request.MeetingUrl, request.Speaker);
        EnsureDaysInWeek(week, request.Days);

        var events = await db.CalendarEvents.Where(e => e.SessionWeekId == id && e.Status != CalendarEventStatus.Cancelled)
            .ToDictionaryAsync(e => e.Id, ct);
        foreach (var day in request.Days)
        {
            if (day.EventId is { } eventId)
            {
                var existing = events.GetValueOrDefault(eventId)
                               ?? throw new DomainException("One of the days no longer belongs to this week. Reload it and try again.");
                existing.Update(week.DayTitle(day.Theme), existing.Description, week.EventType, day.StartAt, day.EndAt, false,
                    week.Location, week.MeetingUrl, existing.Priority, CalendarEventStatus.Scheduled);
                existing.SetSessionDay(week, day.Theme, SpeakerFor(week, day));
            }
            else
            {
                AddDay(week, day);
            }
        }
        var kept = request.Days.Select(d => d.EventId).OfType<Guid>().ToHashSet();
        foreach (var dropped in events.Values.Where(e => !kept.Contains(e.Id)))
        {
            dropped.Cancel();
            activity.Record(ActivityEntities.CalendarEvent, dropped.Id, ActivityActions.Cancelled, null, new { Reason = "Removed from its session week" });
        }
        activity.RecordChanges(ActivityEntities.SessionWeek, id, ActivityActions.Updated,
            new Dictionary<string, object?> { ["Name"] = before.Name, ["WeekStart"] = before.WeekStart, ["Speaker"] = before.Speaker, ["Location"] = before.Location },
            new Dictionary<string, object?> { ["Name"] = week.Name, ["WeekStart"] = week.WeekStart, ["Speaker"] = week.Speaker, ["Location"] = week.Location });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Session week {WeekId} updated by {UserId}", id, currentUser.UserId);
        return await GetAsync(id, ct);
    }

    private void AddDay(SessionWeek week, SessionDayRequest day)
    {
        var calendarEvent = CalendarEvent.Create(week.DayTitle(day.Theme), null, week.EventType, day.StartAt, day.EndAt, false,
            week.Location, week.MeetingUrl, Priority.Normal);
        calendarEvent.SetSessionDay(week, day.Theme, SpeakerFor(week, day));
        db.CalendarEvents.Add(calendarEvent);
        activity.Record(ActivityEntities.CalendarEvent, calendarEvent.Id, ActivityActions.Created, null,
            new { calendarEvent.Title, calendarEvent.StartAt, calendarEvent.EndAt, calendarEvent.Speaker });
    }

    /// <summary>The day's own speaker, else the week's.</summary>
    private static string? SpeakerFor(SessionWeek week, SessionDayRequest day) =>
        string.IsNullOrWhiteSpace(day.Speaker) ? week.Speaker : day.Speaker.Trim();

    private void EnsureDaysInWeek(SessionWeek week, IReadOnlyList<SessionDayRequest> days)
    {
        var zone = ZonedTime.Find(organisation.Value.DefaultTimeZone);
        var dates = days.Select(d => ZonedTime.LocalDate(d.StartAt, zone)).ToList();
        if (dates.Any(d => d < week.WeekStart || d > week.WeekStart.AddDays(6)))
        {
            throw new DomainException($"Every day must fall in the week of {week.WeekStart:d MMM yyyy} (Monday to Sunday).");
        }
        if (dates.Distinct().Count() != dates.Count)
        {
            throw new DomainException("Each day can appear only once in a week.");
        }
    }

    private static CalendarEventType Type(SessionWeekRequest request) =>
        CalendarEventMapping.Parse<CalendarEventType>(request.EventType, "event type");

    private void EnsureCanManage()
    {
        if (!currentUser.HasPermission(Permissions.CalendarManage))
        {
            throw new ForbiddenException("Planning sessions needs the calendar.manage permission.");
        }
    }
}

public sealed class SessionWeekRequestValidator : AbstractValidator<SessionWeekRequest>
{
    public SessionWeekRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().WithMessage("Give the sessions a name, for example Morning Devotion.")
            .MaximumLength(SessionWeek.NameMaxLength);
        RuleFor(r => r.WeekStart).Must(d => d.DayOfWeek == DayOfWeek.Monday).WithMessage("A session week starts on a Monday.");
        RuleFor(r => r.EventType).Must(t => CalendarValues.EventTypes.Contains(t))
            .WithMessage($"Event type must be one of: {string.Join(", ", CalendarValues.EventTypes)}.");
        RuleFor(r => r.Location).MaximumLength(500);
        RuleFor(r => r.MeetingUrl)
            .Must(u => u is null || (Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http"))
            .WithMessage("Meeting link must be a full web address (https://…).");
        RuleFor(r => r.Speaker).MaximumLength(SessionWeek.SpeakerMaxLength);
        RuleFor(r => r.Days).NotEmpty().WithMessage("Choose at least one day.")
            .Must(d => d.Count <= 7).WithMessage("A week has at most seven days.");
        RuleForEach(r => r.Days).ChildRules(day =>
        {
            day.RuleFor(d => d.EndAt).Must((d, end) => end > d.StartAt).WithMessage("Each session must end after it starts.");
            day.RuleFor(d => d.EndAt).Must((d, end) => end - d.StartAt <= TimeSpan.FromHours(24))
                .WithMessage("A session can last at most a day.");
            day.RuleFor(d => d.Theme).MaximumLength(SessionWeek.ThemeMaxLength);
            day.RuleFor(d => d.Speaker).MaximumLength(SessionWeek.SpeakerMaxLength);
        });
    }
}
