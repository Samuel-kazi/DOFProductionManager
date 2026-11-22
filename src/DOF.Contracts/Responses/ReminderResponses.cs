namespace DOF.Contracts.Responses;

/// <summary>A reminder. <see cref="SourceTitle"/> and <see cref="SourceAt"/> describe the record it is about (null when gone).</summary>
public sealed record ReminderResponse(
    Guid Id,
    string SourceType,
    Guid SourceId,
    string? SourceTitle,
    DateTimeOffset? SourceAt,
    int? OffsetMinutes,
    bool IsAutomatic,
    DateTimeOffset ScheduledAt,
    string Channel,
    string Status,
    DateTimeOffset? DeliveredAt,
    int RetryCount,
    string? LastError,
    string? CancellationReason,
    int Version);

public sealed record NotificationResponse(
    Guid Id,
    string Title,
    string Message,
    string NotificationType,
    string? SourceType,
    Guid? SourceId,
    bool IsRead,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

public sealed record UnreadCountResponse(int Count);

/// <summary><see cref="EmailAvailable"/> is false when the server has no e-mail configured; e-mail cannot then be enabled.</summary>
public sealed record NotificationPreferencesResponse(
    bool EmailEnabled,
    bool EmailAvailable,
    bool DesktopEnabled,
    bool OverdueAlerts,
    IReadOnlyList<int> ReminderOffsets);
