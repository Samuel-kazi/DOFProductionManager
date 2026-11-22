using DOF.Application.Activity;
using DOF.Application.Common;
using DOF.Application.Common.Abstractions;
using DOF.Application.Common.Exceptions;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Domain.Entities;
using DOF.Domain.Enums;
using DOF.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DOF.Application.Work;

/// <summary>Projects, their members and milestones, and the project timeline.</summary>
public sealed class ProjectService(
    IApplicationDbContext db,
    WorkAccess access,
    WorkReaders readers,
    ActivityRecorder activity,
    ActivityQueries history,
    ILogger<ProjectService> logger)
{
    public const string ManagerRole = "Project manager";

    // ---------- projects ----------

    public async Task<PagedResponse<ProjectSummaryResponse>> ListAsync(int? page, int? pageSize, string? search,
        IReadOnlyCollection<string>? statuses, string? projectType, Guid? managerId, Guid? memberId, bool includeArchived,
        CancellationToken ct)
    {
        var (p, size) = Paging.Normalize(page, pageSize);
        var query = db.Projects.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(x => x.Name.ToLower().Contains(term));
        }
        if (WorkParsing.ParseAll<ProjectStatus>(statuses) is { } wanted)
        {
            query = query.Where(x => wanted.Contains(x.Status));
        }
        else if (!includeArchived)
        {
            query = query.Where(x => x.Status != ProjectStatus.Archived);
        }
        if (projectType is not null)
        {
            var type = WorkParsing.Parse<ProjectType>(projectType, "project type");
            query = query.Where(x => x.ProjectType == type);
        }
        if (managerId is not null)
        {
            query = query.Where(x => x.ManagerId == managerId);
        }
        if (memberId is not null)
        {
            query = query.Where(x => x.ManagerId == memberId || x.Members.Any(m => m.UserId == memberId));
        }

        var total = await query.CountAsync(ct);
        var items = await Summaries(query.OrderBy(x => x.Status == ProjectStatus.Archived)
                .ThenBy(x => x.Status == ProjectStatus.Completed).ThenBy(x => x.Name))
            .Skip((p - 1) * size).Take(size).ToListAsync(ct);
        return new PagedResponse<ProjectSummaryResponse>(items, p, size, total);
    }

    /// <summary>Summary projection with counts that lead somewhere (open tasks, overdue work, next milestone).</summary>
    public IQueryable<ProjectSummaryResponse> Summaries(IQueryable<Project> query)
    {
        var now = readers.Now;
        var today = readers.Today;
        return query.Select(x => new ProjectSummaryResponse(
            x.Id, x.Name, x.ProjectType.ToString(), x.Status.ToString(), x.Priority.ToString(),
            db.Users.Where(u => u.Id == x.ManagerId).Select(u => new UserSummaryResponse(u.Id, u.FullName)).FirstOrDefault(),
            x.StartDate, x.TargetEndDate, x.CompletedAt, x.Members.Count,
            db.Tasks.Count(t => t.ProjectId == x.Id && t.Status != WorkStatus.Completed && t.Status != WorkStatus.Cancelled),
            db.Tasks.Count(t => t.ProjectId == x.Id && t.Status != WorkStatus.Completed && t.Status != WorkStatus.Cancelled
                                && t.DueAt != null && t.DueAt < now)
            + db.ProjectMilestones.Count(m => m.ProjectId == x.Id && m.Status != WorkStatus.Completed
                                              && m.Status != WorkStatus.Cancelled && m.DueDate < today),
            db.ProjectMilestones.Where(m => m.ProjectId == x.Id && m.Status != WorkStatus.Completed && m.Status != WorkStatus.Cancelled)
                .OrderBy(m => m.DueDate).Select(m => m.Title).FirstOrDefault(),
            db.ProjectMilestones.Where(m => m.ProjectId == x.Id && m.Status != WorkStatus.Completed && m.Status != WorkStatus.Cancelled)
                .OrderBy(m => m.DueDate).Select(m => (DateOnly?)m.DueDate).FirstOrDefault()));
    }

    public async Task<ProjectResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking()
            .Include(x => x.Members).ThenInclude(m => m.User)
            .SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Project", id);
        var names = await readers.NamesAsync([project.ManagerId, project.CreatedBy], ct);
        var milestones = await readers.MilestonesAsync(
            db.ProjectMilestones.Where(m => m.ProjectId == id).OrderBy(m => m.DueDate).ThenBy(m => m.Title), ct);

        return new ProjectResponse(project.Id, project.Name, project.Description, project.ProjectType.ToString(),
            project.Status.ToString(), project.Priority.ToString(), WorkReaders.Summary(project.ManagerId, names),
            project.StartDate, project.TargetEndDate, project.CompletedAt, project.ArchivedAt,
            [.. project.Members.OrderBy(m => m.User.FullName)
                .Select(m => new ProjectMemberResponse(m.UserId, m.User.FullName, m.ProjectRole, m.JoinedAt))],
            milestones, WorkReaders.Summary(project.CreatedBy, names), project.CreatedAt, project.UpdatedAt, project.Version,
            access.CanEditProject(project.ManagerId), access.CanArchiveProjects);
    }

    public async Task<ProjectResponse> CreateAsync(CreateProjectRequest request, CancellationToken ct)
    {
        if (!access.CanCreateProjects)
        {
            throw new ForbiddenException();
        }
        await EnsureActiveUsersAsync([request.ManagerId], ct);

        var project = Project.Create(request.Name, request.Description,
            WorkParsing.Parse<ProjectType>(request.ProjectType, "project type"),
            WorkParsing.Parse<Priority>(request.Priority, "priority"),
            request.ManagerId, request.StartDate, request.TargetEndDate);
        if (request.ManagerId is { } manager)
        {
            project.SetMember(manager, ManagerRole, readers.Now);
        }

        db.Projects.Add(project);
        activity.Record(ActivityEntities.Project, project.Id, ActivityActions.Created, null,
            new { project.Name, Type = project.ProjectType.ToString(), Status = project.Status.ToString() });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Project {ProjectId} created by {UserId}", project.Id, access.Me);
        return await GetAsync(project.Id, ct);
    }

    public async Task<ProjectResponse> UpdateAsync(Guid id, UpdateProjectRequest request, CancellationToken ct)
    {
        var project = await LoadProjectAsync(id, request.Version, ct);
        if (!access.CanEditProject(project.ManagerId))
        {
            throw new ForbiddenException();
        }
        await EnsureActiveUsersAsync([request.ManagerId], ct);

        var before = Snapshot(project);
        project.Update(request.Name, request.Description,
            WorkParsing.Parse<ProjectType>(request.ProjectType, "project type"),
            WorkParsing.Parse<Priority>(request.Priority, "priority"),
            request.ManagerId, request.StartDate, request.TargetEndDate);
        if (request.ManagerId is { } manager && project.Members.All(m => m.UserId != manager))
        {
            project.SetMember(manager, ManagerRole, readers.Now);
        }

        activity.RecordChanges(ActivityEntities.Project, id, ActivityActions.Updated, before, Snapshot(project));
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Archiving and restoring need <c>projects.archive</c>; other status changes need edit rights.</summary>
    public async Task<ProjectResponse> ChangeStatusAsync(Guid id, ChangeStatusRequest request, CancellationToken ct)
    {
        var project = await LoadProjectAsync(id, request.Version, ct);
        var status = WorkParsing.Parse<ProjectStatus>(request.Status, "project status");
        if (status == project.Status)
        {
            return await GetAsync(id, ct);
        }

        var archiving = status == ProjectStatus.Archived || project.Status == ProjectStatus.Archived;
        if (archiving ? !access.CanArchiveProjects : !access.CanEditProject(project.ManagerId))
        {
            throw new ForbiddenException(archiving
                ? "Archiving or restoring a project needs the projects.archive permission."
                : "You don't have permission to do that.");
        }

        var previous = project.Status;
        project.ChangeStatus(status, readers.Now);
        activity.Record(ActivityEntities.Project, id, ActivityActions.StatusChanged,
            new { Status = previous.ToString() }, new { Status = status.ToString() });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Project {ProjectId} status {From} -> {To} by {UserId}", id, previous, status, access.Me);
        return await GetAsync(id, ct);
    }

    // ---------- members ----------

    public async Task<ProjectResponse> SetMemberAsync(Guid projectId, AddProjectMemberRequest request, CancellationToken ct)
    {
        var project = await db.Projects.Include(x => x.Members).SingleOrDefaultAsync(x => x.Id == projectId, ct)
                      ?? throw new NotFoundException("Project", projectId);
        EnsureCanEdit(project);
        project.EnsureNotArchived();
        await EnsureActiveUsersAsync([request.UserId], ct);
        var name = await db.Users.Where(u => u.Id == request.UserId).Select(u => u.FullName).SingleAsync(ct);

        var role = string.IsNullOrWhiteSpace(request.ProjectRole) ? "Member" : request.ProjectRole.Trim();
        var previousRole = project.SetMember(request.UserId, role, readers.Now);
        if (previousRole is null)
        {
            activity.Record(ActivityEntities.Project, projectId, ActivityActions.MemberAdded, null, new { Member = name, Role = role });
        }
        else if (previousRole != role)
        {
            activity.Record(ActivityEntities.Project, projectId, ActivityActions.MemberRoleChanged,
                new { Member = name, Role = previousRole }, new { Member = name, Role = role });
        }

        await db.SaveChangesAsync(ct);
        return await GetAsync(projectId, ct);
    }

    public async Task<ProjectResponse> RemoveMemberAsync(Guid projectId, Guid userId, CancellationToken ct)
    {
        var project = await db.Projects.Include(x => x.Members).ThenInclude(m => m.User)
                          .SingleOrDefaultAsync(x => x.Id == projectId, ct) ?? throw new NotFoundException("Project", projectId);
        EnsureCanEdit(project);
        if (project.ManagerId == userId)
        {
            throw new DomainException("The project manager stays a member. Choose a different manager first.");
        }

        var member = project.Members.FirstOrDefault(m => m.UserId == userId);
        if (member is not null && project.RemoveMember(userId))
        {
            activity.Record(ActivityEntities.Project, projectId, ActivityActions.MemberRemoved, new { Member = member.User.FullName }, null);
            await db.SaveChangesAsync(ct);
        }
        return await GetAsync(projectId, ct);
    }

    // ---------- milestones ----------

    public async Task<IReadOnlyList<MilestoneResponse>> ListMilestonesAsync(Guid projectId, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(x => x.Id == projectId, ct))
        {
            throw new NotFoundException("Project", projectId);
        }
        return await readers.MilestonesAsync(
            db.ProjectMilestones.Where(m => m.ProjectId == projectId).OrderBy(m => m.DueDate).ThenBy(m => m.Title), ct);
    }

    public async Task<MilestoneResponse> GetMilestoneAsync(Guid id, CancellationToken ct) =>
        (await readers.MilestonesAsync(db.ProjectMilestones.Where(m => m.Id == id), ct)).SingleOrDefault()
        ?? throw new NotFoundException("Milestone", id);

    public async Task<MilestoneResponse> CreateMilestoneAsync(Guid projectId, CreateMilestoneRequest request, CancellationToken ct)
    {
        var project = await db.Projects.SingleOrDefaultAsync(x => x.Id == projectId, ct)
                      ?? throw new NotFoundException("Project", projectId);
        EnsureCanEdit(project);
        project.EnsureNotArchived();
        await EnsureActiveUsersAsync([request.AssignedTo], ct);

        var milestone = ProjectMilestone.Create(projectId, request.Title, request.Description, request.DueDate,
            WorkParsing.Parse<Priority>(request.Priority, "priority"), request.AssignedTo);
        db.ProjectMilestones.Add(milestone);
        activity.Record(ActivityEntities.Milestone, milestone.Id, ActivityActions.Created, null,
            new { milestone.Title, DueDate = milestone.DueDate, Project = project.Name });
        await db.SaveChangesAsync(ct);
        return await GetMilestoneAsync(milestone.Id, ct);
    }

    public async Task<MilestoneResponse> UpdateMilestoneAsync(Guid id, UpdateMilestoneRequest request, CancellationToken ct)
    {
        var milestone = await LoadMilestoneAsync(id, request.Version, ct);
        if (!access.CanEditMilestone(milestone.Project.ManagerId, milestone.AssignedTo))
        {
            throw new ForbiddenException();
        }
        milestone.Project.EnsureNotArchived();
        await EnsureActiveUsersAsync([request.AssignedTo], ct);

        var before = MilestoneSnapshot(milestone);
        var previousDue = milestone.DueDate;
        var previousStatus = milestone.Status;
        milestone.Update(request.Title, request.Description, request.DueDate,
            WorkParsing.Parse<Priority>(request.Priority, "priority"), request.AssignedTo);
        milestone.ChangeStatus(WorkParsing.Parse<WorkStatus>(request.Status, "status"), readers.Now);

        if (previousDue != milestone.DueDate)
        {
            activity.Record(ActivityEntities.Milestone, id, ActivityActions.Rescheduled,
                new { DueDate = previousDue }, new { DueDate = milestone.DueDate });
        }
        if (previousStatus != milestone.Status)
        {
            activity.Record(ActivityEntities.Milestone, id, ActivityActions.StatusChanged,
                new { Status = previousStatus.ToString() }, new { Status = milestone.Status.ToString() });
        }
        var after = MilestoneSnapshot(milestone);
        activity.RecordChanges(ActivityEntities.Milestone, id, ActivityActions.Updated, before, after);

        await db.SaveChangesAsync(ct);
        return await GetMilestoneAsync(id, ct);
    }

    public async Task<MilestoneResponse> CompleteMilestoneAsync(Guid id, CancellationToken ct)
    {
        var milestone = await db.ProjectMilestones.Include(m => m.Project).SingleOrDefaultAsync(m => m.Id == id, ct)
                        ?? throw new NotFoundException("Milestone", id);
        if (!access.CanEditMilestone(milestone.Project.ManagerId, milestone.AssignedTo))
        {
            throw new ForbiddenException();
        }

        if (milestone.Status != WorkStatus.Completed)
        {
            var previous = milestone.Status;
            milestone.ChangeStatus(WorkStatus.Completed, readers.Now);
            activity.Record(ActivityEntities.Milestone, id, ActivityActions.StatusChanged,
                new { Status = previous.ToString() }, new { Status = WorkValues.Completed });
            await db.SaveChangesAsync(ct);
        }
        return await GetMilestoneAsync(id, ct);
    }

    // ---------- timeline ----------

    public async Task<ProjectTimelineResponse> TimelineAsync(Guid id, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking().Where(x => x.Id == id)
                          .Select(x => new ProjectRefResponse(x.Id, x.Name)).SingleOrDefaultAsync(ct)
                      ?? throw new NotFoundException("Project", id);
        var milestones = await readers.MilestonesAsync(
            db.ProjectMilestones.Where(m => m.ProjectId == id).OrderBy(m => m.DueDate).ThenBy(m => m.Title), ct);
        var tasks = await readers.TasksAsync(WorkReaders.Ordered(db.Tasks.Where(t => t.ProjectId == id)), ct);

        var milestoneIds = milestones.Select(m => m.Id).ToList();
        var taskIds = tasks.Select(t => t.Id).ToList();
        var entries = await history.GetAsync(a =>
            (a.EntityType == ActivityEntities.Project && a.EntityId == id)
            || (a.EntityType == ActivityEntities.Milestone && milestoneIds.Contains(a.EntityId))
            || (a.EntityType == ActivityEntities.Task && taskIds.Contains(a.EntityId)), ct);

        return new ProjectTimelineResponse(project, milestones, tasks, entries);
    }

    // ---------- helpers ----------

    private async Task<Project> LoadProjectAsync(Guid id, int? version, CancellationToken ct)
    {
        var project = await db.Projects.Include(x => x.Members).SingleOrDefaultAsync(x => x.Id == id, ct)
                      ?? throw new NotFoundException("Project", id);
        if (version is { } expected)
        {
            if (project.Version != expected)
            {
                throw new ConflictException("This project was modified by someone else. Reload the latest version and try again.");
            }
            db.Entry(project).Property(x => x.Version).OriginalValue = expected;
        }
        return project;
    }

    private async Task<ProjectMilestone> LoadMilestoneAsync(Guid id, int version, CancellationToken ct)
    {
        var milestone = await db.ProjectMilestones.Include(m => m.Project).SingleOrDefaultAsync(m => m.Id == id, ct)
                        ?? throw new NotFoundException("Milestone", id);
        if (milestone.Version != version)
        {
            throw new ConflictException("This milestone was modified by someone else. Reload the latest version and try again.");
        }
        db.Entry(milestone).Property(m => m.Version).OriginalValue = version;
        return milestone;
    }

    private void EnsureCanEdit(Project project)
    {
        if (!access.CanEditProject(project.ManagerId))
        {
            throw new ForbiddenException();
        }
    }

    private async Task EnsureActiveUsersAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var wanted = ids.OfType<Guid>().Distinct().ToList();
        if (wanted.Count > 0 && await db.Users.CountAsync(u => wanted.Contains(u.Id) && u.IsActive, ct) != wanted.Count)
        {
            throw new DomainException("Work can only be given to existing, active users.");
        }
    }

    private static Dictionary<string, object?> Snapshot(Project p) => new()
    {
        ["Name"] = p.Name, ["Description"] = p.Description, ["ProjectType"] = p.ProjectType.ToString(),
        ["Priority"] = p.Priority.ToString(), ["Manager"] = p.ManagerId, ["StartDate"] = p.StartDate,
        ["TargetEndDate"] = p.TargetEndDate,
    };

    private static Dictionary<string, object?> MilestoneSnapshot(ProjectMilestone m) => new()
    {
        ["Title"] = m.Title, ["Description"] = m.Description, ["Priority"] = m.Priority.ToString(), ["AssignedTo"] = m.AssignedTo,
    };
}
