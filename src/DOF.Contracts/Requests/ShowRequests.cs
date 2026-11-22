namespace DOF.Contracts.Requests;

public sealed record CreateShowRequest(
    string Name,
    string? Description,
    string ShowType,
    Guid? ProducerId,
    int? DefaultDurationMinutes);

public sealed record UpdateShowRequest(
    string Name,
    string? Description,
    string ShowType,
    Guid? ProducerId,
    int? DefaultDurationMinutes,
    int Version);

/// <summary>Without a number the season becomes the show's next season.</summary>
public sealed record CreateSeasonRequest(
    int? SeasonNumber,
    string? Name,
    DateOnly? StartDate,
    DateOnly? EndDate);

public sealed record UpdateSeasonRequest(
    int SeasonNumber,
    string? Name,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string Status,
    int Version);

/// <summary>
/// Created under a season (POST /seasons/{id}/episodes, numbered next in the season when no number is
/// given) or directly under a show for one-off productions (POST /shows/{id}/episodes). With a recording
/// start and no end, the show's default duration is used.
/// </summary>
public sealed record CreateEpisodeRequest(
    string Title,
    string? Description,
    int? EpisodeNumber,
    Guid? ProducerId,
    DateTimeOffset? RecordingStart,
    DateTimeOffset? RecordingEnd,
    string? RecordingLocation,
    DateOnly? EditingDueDate,
    DateOnly? ReviewDueDate,
    DateOnly? ApprovalDueDate);

/// <summary>Moving to another season of the same show, or to none, is allowed.</summary>
public sealed record UpdateEpisodeRequest(
    string Title,
    string? Description,
    Guid? SeasonId,
    int? EpisodeNumber,
    Guid? ProducerId,
    DateTimeOffset? RecordingStart,
    DateTimeOffset? RecordingEnd,
    string? RecordingLocation,
    DateOnly? EditingDueDate,
    DateOnly? ReviewDueDate,
    DateOnly? ApprovalDueDate,
    int Version);

/// <summary>
/// Any production status may be set, including corrections; the previous status stays in the history.
/// <see cref="RecordedAt"/> optionally says when a recording actually happened. <see cref="Note"/> is kept
/// with the history entry (for example, why a status was corrected).
/// </summary>
public sealed record ChangeEpisodeStatusRequest(string Status, int? Version = null, DateTimeOffset? RecordedAt = null, string? Note = null);

public sealed record AssignEpisodeRequest(Guid UserId, string Responsibility);
