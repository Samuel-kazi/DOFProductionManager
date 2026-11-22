namespace DOF.Domain.Enums;

/// <summary>Kinds of standalone calendar event (events with no other owning record).</summary>
public enum CalendarEventType
{
    Meeting,
    MinistryEvent,
    Training,
    ExternalAppointment,
    RecordingSession,
    Other,
}

public enum CalendarEventStatus
{
    Scheduled,
    Completed,
    Cancelled,
}

public enum Priority
{
    Low,
    Normal,
    High,
    Urgent,
}

public enum CalendarAssignmentType
{
    Organizer,
    Attendee,
}

/// <summary>The kind of record a calendar item comes from. The calendar never copies these records.</summary>
public enum CalendarSourceType
{
    CalendarEvent,
    Milestone,
    Task,
    Episode,
    Release,
}
