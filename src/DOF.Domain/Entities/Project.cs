using DOF.Domain.Enums;
using DOF.Domain.Exceptions;

namespace DOF.Domain.Entities;

public sealed class Project : AuditableEntity, IVersioned, IArchivable
{
    private readonly List<ProjectMember> _members = [];

    private Project() { }

    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public ProjectType ProjectType { get; private set; }
    public ProjectStatus Status { get; private set; }
    public Priority Priority { get; private set; }
    public Guid? ManagerId { get; private set; }
    public DateOnly? StartDate { get; private set; }
    public DateOnly? TargetEndDate { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }
    public int Version { get; private set; }

    public IReadOnlyCollection<ProjectMember> Members => _members;

    public bool IsArchived => Status == ProjectStatus.Archived;

    public static Project Create(string name, string? description, ProjectType type, Priority priority,
        Guid? managerId, DateOnly? startDate, DateOnly? targetEndDate)
    {
        var project = new Project { Status = ProjectStatus.Proposed };
        project.Update(name, description, type, priority, managerId, startDate, targetEndDate);
        return project;
    }

    public void Update(string name, string? description, ProjectType type, Priority priority,
        Guid? managerId, DateOnly? startDate, DateOnly? targetEndDate)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("A project needs a name.");
        }
        if (startDate is not null && targetEndDate is not null && targetEndDate < startDate)
        {
            throw new DomainException("The target end date cannot be before the start date.");
        }

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        ProjectType = type;
        Priority = priority;
        ManagerId = managerId;
        StartDate = startDate;
        TargetEndDate = targetEndDate;
    }

    /// <summary>
    /// Any status can be set by an authorised user (corrections included). Completion and archive
    /// times are recorded on entry and cleared when the project leaves that status; history keeps the rest.
    /// </summary>
    public void ChangeStatus(ProjectStatus status, DateTimeOffset now)
    {
        CompletedAt = status == ProjectStatus.Completed ? CompletedAt ?? now
            : status == ProjectStatus.Archived ? CompletedAt
            : null;
        ArchivedAt = status == ProjectStatus.Archived ? ArchivedAt ?? now : null;
        Status = status;
    }

    public void EnsureNotArchived()
    {
        if (IsArchived)
        {
            throw new DomainException("This project is archived. Restore it before adding or changing its work.");
        }
    }

    /// <summary>Adds a member, or changes the role of an existing one. Returns the previous role, if any.</summary>
    public string? SetMember(Guid userId, string role, DateTimeOffset now)
    {
        var trimmed = string.IsNullOrWhiteSpace(role) ? "Member" : role.Trim();
        if (_members.FirstOrDefault(m => m.UserId == userId) is { } existing)
        {
            var previous = existing.ProjectRole;
            existing.ChangeRole(trimmed);
            return previous;
        }

        _members.Add(new ProjectMember(Id, userId, trimmed, now));
        return null;
    }

    public bool RemoveMember(Guid userId) => _members.RemoveAll(m => m.UserId == userId) > 0;
}
