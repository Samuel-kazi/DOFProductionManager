using DOF.Domain.Enums;

namespace DOF.Domain.Entities;

/// <summary>
/// A message for one person (reminder, overdue alert, escalation), linked to the record it is about.
/// <see cref="IdempotencyKey"/> is unique, so the same event can never notify twice.
/// </summary>
public sealed class Notification : Entity
{
    private Notification() { }

    public Notification(Guid userId, string title, string message, NotificationType type, ReminderSourceType? sourceType,
        Guid? sourceId, string idempotencyKey)
    {
        UserId = userId;
        Title = title.Length > 200 ? title[..200] : title;
        Message = message;
        NotificationType = type;
        SourceType = sourceType;
        SourceId = sourceId;
        IdempotencyKey = idempotencyKey;
    }

    public Guid UserId { get; private set; }
    public string Title { get; private set; } = null!;
    public string Message { get; private set; } = null!;
    public NotificationType NotificationType { get; private set; }
    public ReminderSourceType? SourceType { get; private set; }
    public Guid? SourceId { get; private set; }
    public bool IsRead { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;

    public void MarkRead(DateTimeOffset now)
    {
        if (!IsRead)
        {
            IsRead = true;
            ReadAt = now;
        }
    }
}
