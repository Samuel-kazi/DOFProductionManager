namespace DOF.Contracts.Requests;

/// <summary>
/// A personal reminder about a record: <see cref="OffsetMinutes"/> before its time (recalculated if the
/// record moves) or at a fixed time <see cref="At"/>. Exactly one of the two.
/// </summary>
public sealed record CreateReminderRequest(string SourceType, Guid SourceId, int? OffsetMinutes, DateTimeOffset? At, string Channel = "InApp");

public sealed record UpdateReminderRequest(int? OffsetMinutes, DateTimeOffset? At, string Channel, int Version);

public sealed record UpdateNotificationPreferencesRequest(
    bool EmailEnabled,
    bool DesktopEnabled,
    bool OverdueAlerts,
    IReadOnlyList<int> ReminderOffsets);
