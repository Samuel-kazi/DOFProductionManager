using DOF.Domain.Enums;

namespace DOF.Domain.Entities;

public sealed class CalendarEventAssignment : Entity
{
    private CalendarEventAssignment() { }

    public CalendarEventAssignment(Guid calendarEventId, Guid userId, CalendarAssignmentType assignmentType)
    {
        CalendarEventId = calendarEventId;
        UserId = userId;
        AssignmentType = assignmentType;
    }

    public Guid CalendarEventId { get; private set; }
    public Guid UserId { get; private set; }
    public CalendarAssignmentType AssignmentType { get; private set; }
    public User User { get; private set; } = null!;

    internal void ChangeType(CalendarAssignmentType type) => AssignmentType = type;
}
