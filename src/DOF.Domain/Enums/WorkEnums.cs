namespace DOF.Domain.Enums;

public enum ProjectStatus
{
    Proposed,
    Approved,
    Planning,
    Active,
    OnHold,
    Completed,
    Archived,
}

public enum ProjectType
{
    Documentary,
    ShowProduction,
    Conference,
    Campaign,
    MusicProduction,
    Partnership,
    Other,
}

/// <summary>Shared by milestones and tasks.</summary>
public enum WorkStatus
{
    NotStarted,
    InProgress,
    AwaitingReview,
    Completed,
    Blocked,
    Cancelled,
}
