namespace DOF.Domain.Enums;

public enum ShowType
{
    Recurring,
    LimitedSeries,
    OneOff,
    SpecialProduction,
}

public enum ShowStatus
{
    InDevelopment,
    Active,
    OnHiatus,
    Ended,
    Archived,
}

public enum SeasonStatus
{
    Planned,
    InProduction,
    Completed,
    Cancelled,
}

/// <summary>
/// Production stages of an episode. Episodes do not have to pass through every stage, and authorised
/// users may correct a status; the activity log keeps the full history.
/// </summary>
public enum EpisodeStatus
{
    Planned,
    Scheduled,
    Recorded,
    Editing,
    InReview,
    AwaitingApproval,
    Approved,
    ReadyForRelease,
    PartiallyPublished,
    Published,
    Cancelled,
}

public enum EpisodeResponsibility
{
    Producer,
    Director,
    Presenter,
    Editor,
    Reviewer,
    PublishingManager,
    Crew,
}
