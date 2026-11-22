using DOF.Domain.Entities;
using DOF.Domain.Enums;
using DOF.Domain.Exceptions;

namespace DOF.UnitTests;

public class WorkDomainTests
{
    private static readonly DateTimeOffset Now = new(2033, 1, 3, 9, 0, 0, TimeSpan.Zero);

    private static WorkTask NewTask() => WorkTask.Create("Edit", null, null, Now.AddDays(1), Priority.Normal);

    [Fact]
    public void Tasks_always_have_exactly_one_primary_assignee()
    {
        var task = NewTask();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();

        task.SetAssignees([a, b], null, Now);
        Assert.Equal(a, task.PrimaryAssigneeId);

        task.Assign(b, isPrimary: true, Now);
        Assert.Equal(b, task.PrimaryAssigneeId);
        Assert.Single(task.Assignments, x => x.IsPrimary);

        task.Unassign(b, Now);
        Assert.Equal(a, task.PrimaryAssigneeId);
        Assert.Throws<DomainException>(() => task.Unassign(a, Now));
        Assert.Throws<DomainException>(() => task.SetAssignees([], null, Now));
    }

    [Fact]
    public void A_named_primary_is_added_even_if_missing_from_the_list()
    {
        var task = NewTask();
        Guid a = Guid.NewGuid(), primary = Guid.NewGuid();

        task.SetAssignees([a], primary, Now);

        Assert.Equal(2, task.Assignments.Count);
        Assert.Equal(primary, task.PrimaryAssigneeId);
    }

    [Fact]
    public void Completion_is_stamped_once_and_cleared_on_reopen()
    {
        var task = NewTask();
        var who = Guid.NewGuid();

        task.ChangeStatus(WorkStatus.Completed, who, Now);
        task.ChangeStatus(WorkStatus.Completed, Guid.NewGuid(), Now.AddHours(1));
        Assert.Equal((Now, who), (task.CompletedAt!.Value, task.CompletedBy!.Value));
        Assert.False(task.IsOverdue(Now.AddDays(5)));

        task.ChangeStatus(WorkStatus.InProgress, who, Now.AddHours(2));
        Assert.Null(task.CompletedAt);
        Assert.Null(task.CompletedBy);
        Assert.True(task.IsOverdue(Now.AddDays(5)));
    }

    [Fact]
    public void Milestones_are_overdue_only_while_open()
    {
        var milestone = ProjectMilestone.Create(Guid.NewGuid(), "Rough cut", null, new DateOnly(2033, 1, 10), Priority.Normal, null);

        Assert.False(milestone.IsOverdue(new DateOnly(2033, 1, 10)));
        Assert.True(milestone.IsOverdue(new DateOnly(2033, 1, 11)));
        milestone.ChangeStatus(WorkStatus.Completed, Now);
        Assert.False(milestone.IsOverdue(new DateOnly(2033, 1, 11)));
        Assert.NotNull(milestone.CompletedAt);
    }

    [Fact]
    public void Projects_record_completion_and_archiving_and_refuse_work_when_archived()
    {
        var project = Project.Create("Album", null, ProjectType.MusicProduction, Priority.High, null, null, null);
        Assert.Throws<DomainException>(() => Project.Create("Bad", null, ProjectType.Other, Priority.Low, null,
            new DateOnly(2033, 2, 1), new DateOnly(2033, 1, 1)));

        project.ChangeStatus(ProjectStatus.Completed, Now);
        project.ChangeStatus(ProjectStatus.Archived, Now.AddDays(1));
        Assert.Equal(Now, project.CompletedAt);
        Assert.Equal(Now.AddDays(1), project.ArchivedAt);
        Assert.Throws<DomainException>(project.EnsureNotArchived);

        project.ChangeStatus(ProjectStatus.Active, Now.AddDays(2));
        Assert.Null(project.ArchivedAt);
        Assert.Null(project.CompletedAt);
    }

    [Fact]
    public void Members_are_unique_and_role_changes_report_the_previous_role()
    {
        var project = Project.Create("Album", null, ProjectType.MusicProduction, Priority.High, null, null, null);
        var user = Guid.NewGuid();

        Assert.Null(project.SetMember(user, "Editor", Now));
        Assert.Equal("Editor", project.SetMember(user, "Lead", Now));
        Assert.Equal("Lead", Assert.Single(project.Members).ProjectRole);
        Assert.True(project.RemoveMember(user));
        Assert.False(project.RemoveMember(user));
    }
}
