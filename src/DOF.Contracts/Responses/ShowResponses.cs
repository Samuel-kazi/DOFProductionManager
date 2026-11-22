namespace DOF.Contracts.Responses;

public sealed record ShowRefResponse(Guid Id, string Name);

public sealed record SeasonRefResponse(Guid Id, int SeasonNumber, string DisplayName);

/// <summary>A short reference to an episode; <see cref="Code"/> is like "S2 E5" (empty for unnumbered one-offs).</summary>
public sealed record EpisodeRefResponse(Guid Id, string Title, Guid ShowId, string ShowName, string Code);

public sealed record ShowSummaryResponse(
    Guid Id,
    string Name,
    string ShowType,
    string Status,
    UserSummaryResponse? Producer,
    int SeasonCount,
    int EpisodeCount,
    int EpisodesInProduction,
    DateTimeOffset? NextRecordingAt,
    string? NextRecordingTitle);

public sealed record SeasonResponse(
    Guid Id,
    Guid ShowId,
    int SeasonNumber,
    string? Name,
    string DisplayName,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string Status,
    int EpisodeCount,
    int Version,
    bool CanEdit);

public sealed record ShowResponse(
    Guid Id,
    string Name,
    string? Description,
    string ShowType,
    string Status,
    UserSummaryResponse? Producer,
    int? DefaultDurationMinutes,
    DateTimeOffset? ArchivedAt,
    IReadOnlyList<SeasonResponse> Seasons,
    UserSummaryResponse? CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Version,
    bool CanEdit,
    bool CanManage);

/// <summary>
/// An episode in a list. <see cref="NextDeadlineLabel"/>/<see cref="NextDeadline"/> is the next production
/// deadline still ahead of the episode's stage; <see cref="IsOverdue"/> means one has passed.
/// </summary>
public sealed record EpisodeSummaryResponse(
    Guid Id,
    Guid ShowId,
    string ShowName,
    SeasonRefResponse? Season,
    int? EpisodeNumber,
    string Code,
    string Title,
    string Status,
    UserSummaryResponse? Producer,
    DateTimeOffset? RecordingStart,
    DateTimeOffset? RecordingEnd,
    string? NextDeadlineLabel,
    DateOnly? NextDeadline,
    bool IsOverdue);

public sealed record EpisodeAssignmentResponse(Guid Id, Guid UserId, string FullName, string Responsibility, DateTimeOffset AssignedAt);

/// <summary>A stage of the production timeline (Planned, Recording, Editing, Review, Approval, Release preparation, Published).</summary>
public sealed record EpisodeStageResponse(string Stage, string State, DateOnly? Date);

public sealed record EpisodeResponse(
    Guid Id,
    ShowRefResponse Show,
    SeasonRefResponse? Season,
    int? EpisodeNumber,
    string Code,
    string Title,
    string? Description,
    UserSummaryResponse? Producer,
    DateTimeOffset? RecordingStart,
    DateTimeOffset? RecordingEnd,
    string? RecordingLocation,
    DateTimeOffset? RecordedAt,
    DateOnly? EditingDueDate,
    DateOnly? ReviewDueDate,
    DateOnly? ApprovalDueDate,
    string Status,
    DateTimeOffset? ArchivedAt,
    bool IsOverdue,
    IReadOnlyList<EpisodeAssignmentResponse> Assignments,
    IReadOnlyList<EpisodeStageResponse> Timeline,
    IReadOnlyList<TaskResponse> Tasks,
    IReadOnlyList<ReleaseResponse> Releases,
    UserSummaryResponse? CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Version,
    bool CanEdit,
    bool CanChangeStatus);
