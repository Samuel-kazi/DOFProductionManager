using DOF.Application.Common.Abstractions;
using DOF.Domain.Rules;

namespace DOF.Application.Work;

/// <summary>
/// Who may change projects, milestones and tasks. Checked on the server for every operation; the
/// desktop only uses the resulting <c>canEdit</c> flags to decide what to show.
/// </summary>
public sealed class WorkAccess(ICurrentUser currentUser)
{
    public Guid Me => currentUser.RequiredUserId;

    public bool CanCreateProjects => currentUser.HasPermission(Permissions.ProjectsCreate);

    public bool CanArchiveProjects => currentUser.HasPermission(Permissions.ProjectsArchive);

    public bool CanAssignOthers => currentUser.HasPermission(Permissions.TasksAssign);

    /// <summary>Project editors and the project's own manager.</summary>
    public bool CanEditProject(Guid? managerId) =>
        currentUser.HasPermission(Permissions.ProjectsEdit) || (managerId is { } m && m == currentUser.UserId);

    /// <summary>Milestones: whoever can edit the project, plus the person the milestone is assigned to.</summary>
    public bool CanEditMilestone(Guid? projectManagerId, Guid? assignedTo) =>
        CanEditProject(projectManagerId) || (assignedTo is { } a && a == currentUser.UserId);

    /// <summary>Tasks: people who assign work, the task's creator and its assignees.</summary>
    public bool CanEditTask(Guid? createdBy, IEnumerable<Guid> assigneeIds) =>
        CanAssignOthers || createdBy == currentUser.UserId || (currentUser.UserId is { } me && assigneeIds.Contains(me));
}
