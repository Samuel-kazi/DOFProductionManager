using DOF.Domain.Enums;
using DOF.Domain.Exceptions;

namespace DOF.Domain.Entities;

/// <summary>
/// A calendar entry with no other owning record: staff meetings, ministry events, training, external
/// appointments, or recording sessions not yet linked to an episode. Dates owned by other records
/// (episodes, milestones, tasks, releases) are never stored here.
/// </summary>
public sealed class CalendarEvent : AuditableEntity, IVersioned
{
    private readonly List<CalendarEventAssignment> _assignments = [];

    private CalendarEvent() { }

    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public CalendarEventType EventType { get; private set; }
    public DateTimeOffset StartAt { get; private set; }
    public DateTimeOffset EndAt { get; private set; }
    public bool IsAllDay { get; private set; }
    public string? Location { get; private set; }
    public string? MeetingUrl { get; private set; }
    public Priority Priority { get; private set; }
    public CalendarEventStatus Status { get; private set; }
    public int Version { get; private set; }

    public IReadOnlyCollection<CalendarEventAssignment> Assignments => _assignments;

    public static CalendarEvent Create(
        string title, string? description, CalendarEventType eventType, DateTimeOffset startAt, DateTimeOffset endAt,
        bool isAllDay, string? location, string? meetingUrl, Priority priority)
    {
        var calendarEvent = new CalendarEvent { Status = CalendarEventStatus.Scheduled };
        calendarEvent.Update(title, description, eventType, startAt, endAt, isAllDay, location, meetingUrl, priority,
            CalendarEventStatus.Scheduled);
        return calendarEvent;
    }

    public void Update(
        string title, string? description, CalendarEventType eventType, DateTimeOffset startAt, DateTimeOffset endAt,
        bool isAllDay, string? location, string? meetingUrl, Priority priority, CalendarEventStatus status)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("An event needs a title.");
        }

        EnsureValidTimes(startAt, endAt);
        Title = title.Trim();
        Description = Normalize(description);
        EventType = eventType;
        StartAt = startAt.ToUniversalTime();
        EndAt = endAt.ToUniversalTime();
        IsAllDay = isAllDay;
        Location = Normalize(location);
        MeetingUrl = Normalize(meetingUrl);
        Priority = priority;
        Status = status;
    }

    /// <summary>Moves the event, keeping everything else. Cancelled events must be reinstated first.</summary>
    public void Reschedule(DateTimeOffset startAt, DateTimeOffset endAt)
    {
        if (Status == CalendarEventStatus.Cancelled)
        {
            throw new DomainException("A cancelled event cannot be rescheduled. Reinstate it first.");
        }

        EnsureValidTimes(startAt, endAt);
        StartAt = startAt.ToUniversalTime();
        EndAt = endAt.ToUniversalTime();
    }

    /// <summary>Operational records are cancelled rather than deleted, so their history is kept.</summary>
    public void Cancel() => Status = CalendarEventStatus.Cancelled;

    public void SetAssignments(IEnumerable<(Guid UserId, CalendarAssignmentType Type)> assignments)
    {
        var wanted = assignments
            .GroupBy(a => a.UserId)
            .ToDictionary(g => g.Key, g => g.Min(a => a.Type)); // Organizer wins over Attendee.

        _assignments.RemoveAll(a => !wanted.ContainsKey(a.UserId));
        foreach (var existing in _assignments)
        {
            existing.ChangeType(wanted[existing.UserId]);
        }
        foreach (var (userId, type) in wanted.Where(w => _assignments.All(a => a.UserId != w.Key)))
        {
            _assignments.Add(new CalendarEventAssignment(Id, userId, type));
        }
    }

    private static void EnsureValidTimes(DateTimeOffset startAt, DateTimeOffset endAt)
    {
        if (endAt <= startAt)
        {
            throw new DomainException("An event must end after it starts.");
        }
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
