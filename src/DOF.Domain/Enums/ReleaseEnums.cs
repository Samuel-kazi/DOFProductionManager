namespace DOF.Domain.Enums;

public enum ChannelType
{
    Video,
    Audio,
    Social,
    Broadcast,
    Web,
    Other,
}

/// <summary>
/// Stored release statuses. "Overdue" is part of the vocabulary but is never stored: a release is
/// overdue when it is still open and its scheduled time has passed without a publication confirmation,
/// so the flag clears itself when the release is rescheduled or published.
/// </summary>
public enum ReleaseStatus
{
    Draft,
    AwaitingApproval,
    Ready,
    Scheduled,
    Published,
    Postponed,
    Overdue,
    Cancelled,
    Failed,
}
