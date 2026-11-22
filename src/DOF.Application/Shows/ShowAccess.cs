using DOF.Application.Common.Abstractions;
using DOF.Domain.Rules;

namespace DOF.Application.Shows;

/// <summary>
/// Who may change shows, seasons and episodes. Checked on the server for every operation; the desktop
/// only uses the resulting <c>canEdit</c> flags to decide what to show.
/// </summary>
public sealed class ShowAccess(ICurrentUser currentUser)
{
    public Guid Me => currentUser.RequiredUserId;

    /// <summary>Create and archive shows, and edit any show or episode.</summary>
    public bool CanManage => currentUser.HasPermission(Permissions.EpisodesManage);

    /// <summary>Show details and seasons: managers and the show's producer.</summary>
    public bool CanEditShow(Guid? showProducerId) => CanManage || Is(showProducerId);

    /// <summary>Episode details, schedule and crew: whoever can edit the show, plus the episode's producer.</summary>
    public bool CanEditEpisode(Guid? showProducerId, Guid? episodeProducerId) =>
        CanEditShow(showProducerId) || Is(episodeProducerId);

    /// <summary>Production status: episode editors plus anyone assigned to the episode (an editor moving it to review, say).</summary>
    public bool CanChangeEpisodeStatus(Guid? showProducerId, Guid? episodeProducerId, IEnumerable<Guid> assigneeIds) =>
        CanEditEpisode(showProducerId, episodeProducerId) || (currentUser.UserId is { } me && assigneeIds.Contains(me));

    private bool Is(Guid? userId) => userId is { } id && id == currentUser.UserId;
}
