using DOF.Application.Common;
using DOF.Application.Common.Abstractions;
using DOF.Application.Work;
using DOF.Contracts.Common;
using DOF.Contracts.Responses;
using DOF.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DOF.Application.Calendar;

/// <summary>Project milestones, shown as all-day items on their due date (organisation timezone).</summary>
public sealed class MilestoneCalendarSource(
    IApplicationDbContext db, WorkAccess access, IOptions<OrganisationOptions> organisation) : ICalendarSource
{
    public string SourceType => nameof(CalendarSourceType.Milestone);

    public async Task<IReadOnlyList<CalendarItemResponse>> GetItemsAsync(CalendarQuery query, ICurrentUser user, CancellationToken ct)
    {
        if (query.ShowId is not null || !CalendarEventMapping.Includes(query.EventTypes, CalendarValues.MilestoneItem))
        {
            return [];
        }

        var zone = ZonedTime.Find(organisation.Value.DefaultTimeZone);
        var firstDay = ZonedTime.LocalDate(query.Start, zone);
        var lastDay = ZonedTime.LocalDate(query.End.AddTicks(-1), zone);
        var milestones = db.ProjectMilestones.AsNoTracking()
            .Where(m => m.DueDate >= firstDay && m.DueDate <= lastDay);

        if (WorkParsing.ParseAll<WorkStatus>(query.Statuses) is { } statuses)
        {
            milestones = milestones.Where(m => statuses.Contains(m.Status));
        }
        else if (!query.IncludeCancelled)
        {
            milestones = milestones.Where(m => m.Status != WorkStatus.Cancelled && m.Project.Status != ProjectStatus.Archived);
        }
        if (WorkParsing.ParseAll<Priority>(query.Priorities) is { } priorities)
        {
            milestones = milestones.Where(m => priorities.Contains(m.Priority));
        }
        if (query.AssignedUserId is { } assignee)
        {
            milestones = milestones.Where(m => m.AssignedTo == assignee);
        }
        if (query.ProjectId is { } project)
        {
            milestones = milestones.Where(m => m.ProjectId == project);
        }

        var rows = await milestones
            .Select(m => new
            {
                m.Id, m.Title, m.DueDate, m.Status, m.Priority, m.AssignedTo, ProjectName = m.Project.Name, m.Project.ManagerId,
                AssigneeName = db.Users.Where(u => u.Id == m.AssignedTo).Select(u => u.FullName).FirstOrDefault(),
            })
            .ToListAsync(ct);

        return rows.Select(m => new CalendarItemResponse(
            $"{SourceType}:{m.Id}", $"{m.Title} · {m.ProjectName}", SourceType, m.Id, CalendarValues.MilestoneItem,
            ZonedTime.StartOfDay(m.DueDate, zone), ZonedTime.StartOfDay(m.DueDate.AddDays(1), zone), true,
            m.Status.ToString(), m.Priority.ToString(), null,
            m.AssignedTo is { } id && m.AssigneeName is { } name ? [new AssignedUserResponse(id, name, "Responsible")] : [],
            access.CanEditMilestone(m.ManagerId, m.AssignedTo))).ToList();
    }
}

/// <summary>Task deadlines, shown as points in time. The location line names the task's project or episode.</summary>
public sealed class TaskDeadlineCalendarSource(IApplicationDbContext db, WorkAccess access) : ICalendarSource
{
    public string SourceType => nameof(CalendarSourceType.Task);

    public async Task<IReadOnlyList<CalendarItemResponse>> GetItemsAsync(CalendarQuery query, ICurrentUser user, CancellationToken ct)
    {
        if (!CalendarEventMapping.Includes(query.EventTypes, CalendarValues.TaskDeadlineItem))
        {
            return [];
        }

        var tasks = db.Tasks.AsNoTracking().Where(t => t.DueAt != null && t.DueAt >= query.Start && t.DueAt < query.End);
        if (WorkParsing.ParseAll<WorkStatus>(query.Statuses) is { } statuses)
        {
            tasks = tasks.Where(t => statuses.Contains(t.Status));
        }
        else if (!query.IncludeCancelled)
        {
            tasks = tasks.Where(t => t.Status != WorkStatus.Cancelled);
        }
        if (WorkParsing.ParseAll<Priority>(query.Priorities) is { } priorities)
        {
            tasks = tasks.Where(t => priorities.Contains(t.Priority));
        }
        if (query.AssignedUserId is { } assignee)
        {
            tasks = tasks.Where(t => t.Assignments.Any(a => a.UserId == assignee));
        }
        if (query.ProjectId is { } project)
        {
            tasks = tasks.Where(t => t.ProjectId == project);
        }
        if (query.ShowId is { } show)
        {
            tasks = tasks.Where(t => t.Episode != null && t.Episode.ShowId == show);
        }

        var rows = await tasks
            .Select(t => new
            {
                t.Id, t.Title, t.DueAt, t.Status, t.Priority, t.CreatedBy,
                ProjectName = t.Project != null ? t.Project.Name : t.Episode != null ? t.Episode.Show.Name + ": " + t.Episode.Title : null,
                Assignees = t.Assignments.Select(a => new { a.UserId, a.User.FullName, a.IsPrimary }).ToList(),
            })
            .ToListAsync(ct);

        return rows.Select(t => new CalendarItemResponse(
            $"{SourceType}:{t.Id}", t.Title, SourceType, t.Id, CalendarValues.TaskDeadlineItem,
            t.DueAt!.Value, null, false, t.Status.ToString(), t.Priority.ToString(), t.ProjectName,
            [.. t.Assignees.OrderByDescending(a => a.IsPrimary).ThenBy(a => a.FullName)
                .Select(a => new AssignedUserResponse(a.UserId, a.FullName, a.IsPrimary ? "Primary" : "Assignee"))],
            access.CanEditTask(t.CreatedBy, t.Assignees.Select(a => a.UserId)))).ToList();
    }
}
