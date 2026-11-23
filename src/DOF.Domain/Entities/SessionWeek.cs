using DOF.Domain.Enums;
using DOF.Domain.Exceptions;

namespace DOF.Domain.Entities;

/// <summary>
/// A week of a recurring session series (for example "Morning Devotion", Monday to Friday). It holds what
/// the days share (name, place, link, who is sharing that week); each day is an ordinary calendar event
/// with its own theme and, when someone else shares that day, its own speaker.
/// </summary>
public sealed class SessionWeek : AuditableEntity, IVersioned
{
    public const int NameMaxLength = 150;
    public const int SpeakerMaxLength = 200;
    public const int ThemeMaxLength = 200;

    private SessionWeek() { }

    public string Name { get; private set; } = null!;

    /// <summary>The Monday the week starts on.</summary>
    public DateOnly WeekStart { get; private set; }

    public CalendarEventType EventType { get; private set; }
    public string? Location { get; private set; }
    public string? MeetingUrl { get; private set; }

    /// <summary>Who shares on the days that do not name someone else.</summary>
    public string? Speaker { get; private set; }

    public int Version { get; private set; }

    public static SessionWeek Create(string name, DateOnly weekStart, CalendarEventType eventType, string? location,
        string? meetingUrl, string? speaker)
    {
        var week = new SessionWeek();
        week.Update(name, weekStart, eventType, location, meetingUrl, speaker);
        return week;
    }

    public void Update(string name, DateOnly weekStart, CalendarEventType eventType, string? location, string? meetingUrl, string? speaker)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Give the sessions a name, for example Morning Devotion.");
        }
        if (weekStart.DayOfWeek != DayOfWeek.Monday)
        {
            throw new DomainException("A session week starts on a Monday.");
        }
        Name = Limit(name.Trim(), NameMaxLength, "name");
        WeekStart = weekStart;
        EventType = eventType;
        Location = Clean(location);
        MeetingUrl = Clean(meetingUrl);
        Speaker = Clean(speaker) is { } s ? Limit(s, SpeakerMaxLength, "speaker") : null;
    }

    /// <summary>The calendar title of one day: "Morning Devotion: Walking in faith", or just the name without a theme.</summary>
    public string DayTitle(string? theme) => Clean(theme) is { } t ? $"{Name}: {t}" : Name;

    internal static string Limit(string value, int max, string what) =>
        value.Length <= max ? value : throw new DomainException($"The {what} can be at most {max} characters.");

    internal static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
