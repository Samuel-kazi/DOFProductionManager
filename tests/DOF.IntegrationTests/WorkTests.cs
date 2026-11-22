using System.Net;
using System.Net.Http.Json;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Domain.Rules;
using DOF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static DOF.IntegrationTests.TestApi;

namespace DOF.IntegrationTests;

public class WorkTests(DofApiFactory factory)
{
    private static readonly TimeZoneInfo Nairobi = TimeZoneInfo.FindSystemTimeZoneById("Africa/Nairobi");

    // Far-future dates per test keep calendar assertions independent of other tests.
    private static DateOnly Day(int n) => new DateOnly(2033, 1, 3).AddDays(n);

    private static DateTimeOffset At(DateOnly day, int hour) =>
        new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(new TimeOnly(hour, 0)), Nairobi), TimeSpan.Zero);

    private static async Task<ProjectResponse> CreateProjectAsync(HttpClient client, string name, Guid? managerId = null) =>
        await (await client.PostAsJsonAsync("/api/v1/projects",
            new CreateProjectRequest(name, "Purpose", "Documentary", "High", managerId, Day(0), Day(90)), Ct))
            .DataAsync<ProjectResponse>();

    private static async Task<MilestoneResponse> CreateMilestoneAsync(HttpClient client, Guid projectId, string title, DateOnly due, Guid? assignee = null) =>
        await (await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/milestones",
            new CreateMilestoneRequest(title, null, due, "Normal", assignee), Ct)).DataAsync<MilestoneResponse>();

    private static async Task<TaskResponse> CreateTaskAsync(HttpClient client, string title, DateTimeOffset? due = null,
        Guid? projectId = null, IReadOnlyList<Guid>? assignees = null, Guid? primary = null) =>
        await (await client.PostAsJsonAsync("/api/v1/tasks",
            new CreateTaskRequest(title, null, projectId, due, "Normal", assignees, primary), Ct)).DataAsync<TaskResponse>();

    private static async Task<List<CalendarItemResponse>> CalendarAsync(HttpClient client, DateOnly from, DateOnly to, string extra = "") =>
        await (await client.GetAsync(
                $"/api/v1/calendar?start={Uri.EscapeDataString(At(from, 0).ToString("O"))}&end={Uri.EscapeDataString(At(to, 0).ToString("O"))}{extra}", Ct))
            .DataAsync<List<CalendarItemResponse>>();

    // ---------- projects ----------

    [Fact]
    public async Task Projects_are_created_by_permitted_users_with_the_manager_as_a_member()
    {
        var admin = await factory.AdminClientAsync();
        var (manager, _) = await factory.UserClientAsync(SystemRoles.Producer);
        var (_, member) = await factory.UserClientAsync(SystemRoles.TeamMember);

        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/v1/projects",
            new CreateProjectRequest("Nope", null, "Other", "Normal", null, null, null), Ct)).StatusCode);

        var response = await admin.PostAsJsonAsync("/api/v1/projects",
            new CreateProjectRequest("Heritage documentary", "History of the ministry", "Documentary", "High", manager.Id, Day(0), Day(120)), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var project = await response.DataAsync<ProjectResponse>();

        Assert.Equal("Proposed", project.Status);
        Assert.Equal(manager.Id, project.Manager!.Id);
        Assert.Equal("Project manager", Assert.Single(project.Members).ProjectRole);
        Assert.True(project.CanEdit);

        var invalid = await admin.PostAsJsonAsync("/api/v1/projects",
            new CreateProjectRequest("", null, "Film", "Normal", null, Day(10), Day(1)), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task A_project_manager_can_edit_their_own_project_without_the_edit_permission()
    {
        var admin = await factory.AdminClientAsync();
        var (manager, managerClient) = await factory.UserClientAsync(SystemRoles.TeamMember);
        var (_, outsider) = await factory.UserClientAsync(SystemRoles.TeamMember);
        var project = await CreateProjectAsync(admin, "Youth conference", manager.Id);
        var update = new UpdateProjectRequest("Youth conference 2033", null, "Conference", "Normal", manager.Id, Day(0), Day(30), project.Version);

        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PutAsJsonAsync($"/api/v1/projects/{project.Id}", update, Ct)).StatusCode);
        var updated = await (await managerClient.PutAsJsonAsync($"/api/v1/projects/{project.Id}", update, Ct)).DataAsync<ProjectResponse>();
        Assert.Equal("Youth conference 2033", updated.Name);

        var stale = await managerClient.PutAsJsonAsync($"/api/v1/projects/{project.Id}", update, Ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task Status_changes_keep_completion_history_and_archiving_needs_its_own_permission()
    {
        var admin = await factory.AdminClientAsync();
        var (_, producer) = await factory.UserClientAsync(SystemRoles.Producer);
        var project = await CreateProjectAsync(admin, "Worship album");

        var completed = await (await producer.PatchAsJsonAsync($"/api/v1/projects/{project.Id}/status", new ChangeStatusRequest("Completed"), Ct))
            .DataAsync<ProjectResponse>();
        Assert.NotNull(completed.CompletedAt);
        var reopened = await (await producer.PatchAsJsonAsync($"/api/v1/projects/{project.Id}/status", new ChangeStatusRequest("Active"), Ct))
            .DataAsync<ProjectResponse>();
        Assert.Null(reopened.CompletedAt);

        // Producers can edit but not archive.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await producer.PatchAsJsonAsync($"/api/v1/projects/{project.Id}/status", new ChangeStatusRequest("Archived"), Ct)).StatusCode);
        var archived = await (await admin.PatchAsJsonAsync($"/api/v1/projects/{project.Id}/status", new ChangeStatusRequest("Archived"), Ct))
            .DataAsync<ProjectResponse>();
        Assert.NotNull(archived.ArchivedAt);

        // Archived projects take no new work.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync($"/api/v1/projects/{project.Id}/milestones",
            new CreateMilestoneRequest("Late", null, Day(5), "Normal", null), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync("/api/v1/tasks",
            new CreateTaskRequest("Late", null, project.Id, null, "Normal", null, null), Ct)).StatusCode);

        // The timeline keeps every step.
        var timeline = await (await admin.GetAsync($"/api/v1/projects/{project.Id}/timeline", Ct)).DataAsync<ProjectTimelineResponse>();
        Assert.Equal(["Status changed from Active to Archived", "Status changed from Completed to Active",
                "Status changed from Proposed to Completed", "Project created"],
            timeline.History.Select(h => h.Summary));
        Assert.All(timeline.History, h => Assert.NotNull(h.User));
    }

    [Fact]
    public async Task Members_can_be_added_renamed_and_removed_with_history()
    {
        var admin = await factory.AdminClientAsync();
        var (manager, _) = await factory.UserClientAsync(SystemRoles.Producer);
        var (editor, _) = await factory.UserClientAsync(SystemRoles.TeamMember);
        var project = await CreateProjectAsync(admin, "Campaign 2033", manager.Id);

        await (await admin.PostAsJsonAsync($"/api/v1/projects/{project.Id}/members", new AddProjectMemberRequest(editor.Id, "Editor"), Ct))
            .DataAsync<ProjectResponse>();
        var renamed = await (await admin.PostAsJsonAsync($"/api/v1/projects/{project.Id}/members",
            new AddProjectMemberRequest(editor.Id, "Lead editor"), Ct)).DataAsync<ProjectResponse>();
        Assert.Equal("Lead editor", renamed.Members.Single(m => m.UserId == editor.Id).ProjectRole);

        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await admin.DeleteAsync($"/api/v1/projects/{project.Id}/members/{manager.Id}", Ct)).StatusCode);
        var removed = await (await admin.DeleteAsync($"/api/v1/projects/{project.Id}/members/{editor.Id}", Ct)).DataAsync<ProjectResponse>();
        Assert.DoesNotContain(removed.Members, m => m.UserId == editor.Id);

        var history = (await (await admin.GetAsync($"/api/v1/projects/{project.Id}/timeline", Ct)).DataAsync<ProjectTimelineResponse>()).History;
        Assert.Contains(history, h => h.Action == "MemberAdded" && h.Summary.EndsWith("joined as Editor"));
        Assert.Contains(history, h => h.Action == "MemberRoleChanged" && h.Summary.Contains("from Editor to Lead editor"));
        Assert.Contains(history, h => h.Action == "MemberRemoved");
    }

    // ---------- milestones ----------

    [Fact]
    public async Task Milestones_appear_on_the_calendar_and_move_without_duplication()
    {
        var admin = await factory.AdminClientAsync();
        var project = await CreateProjectAsync(admin, "Christmas special");
        var milestone = await CreateMilestoneAsync(admin, project.Id, "Rough cut", Day(10));

        var items = await CalendarAsync(admin, Day(7), Day(14), "&sourceTypes=Milestone");
        var item = Assert.Single(items, i => i.SourceId == milestone.Id);
        Assert.Equal("Milestone", item.SourceType);
        Assert.Equal("Milestone", item.EventType);
        Assert.True(item.IsAllDay);
        Assert.Equal(At(Day(10), 0), item.StartAt);
        Assert.Equal(At(Day(11), 0), item.EndAt);
        Assert.Equal("Rough cut · Christmas special", item.Title);

        var moved = await (await admin.PutAsJsonAsync($"/api/v1/milestones/{milestone.Id}", new UpdateMilestoneRequest(
            milestone.Title, null, Day(12), "Normal", "InProgress", null, milestone.Version), Ct)).DataAsync<MilestoneResponse>();
        Assert.Equal(Day(12), moved.DueDate);

        var after = (await CalendarAsync(admin, Day(7), Day(14))).Where(i => i.SourceId == milestone.Id).ToList();
        Assert.Equal(At(Day(12), 0), Assert.Single(after).StartAt);
        Assert.Single(await CalendarAsync(admin, Day(7), Day(14), $"&projectId={project.Id}"));

        var history = (await (await admin.GetAsync($"/api/v1/projects/{project.Id}/timeline", Ct)).DataAsync<ProjectTimelineResponse>()).History;
        Assert.Contains(history, h => h.EntityType == "Milestone" && h.Summary == $"Due date changed from {Day(10):yyyy-MM-dd} to {Day(12):yyyy-MM-dd}");
        Assert.Contains(history, h => h.EntityType == "Milestone" && h.Summary == "Status changed from NotStarted to InProgress");
    }

    [Fact]
    public async Task The_assignee_can_complete_a_milestone_but_others_cannot()
    {
        var admin = await factory.AdminClientAsync();
        var (owner, ownerClient) = await factory.UserClientAsync(SystemRoles.TeamMember);
        var (_, outsider) = await factory.UserClientAsync(SystemRoles.TeamMember);
        var project = await CreateProjectAsync(admin, "Partnership launch");
        var milestone = await CreateMilestoneAsync(admin, project.Id, "Sign agreement", Day(20), owner.Id);

        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PatchAsync($"/api/v1/milestones/{milestone.Id}/complete", null, Ct)).StatusCode);
        var done = await (await ownerClient.PatchAsync($"/api/v1/milestones/{milestone.Id}/complete", null, Ct)).DataAsync<MilestoneResponse>();

        Assert.Equal("Completed", done.Status);
        Assert.NotNull(done.CompletedAt);
        Assert.Equal(owner.Id, done.AssignedTo!.Id);
    }

    // ---------- tasks ----------

    [Fact]
    public async Task Anyone_can_create_their_own_tasks_but_assigning_others_needs_permission()
    {
        var (member, memberClient) = await factory.UserClientAsync(SystemRoles.TeamMember);
        var (colleague, _) = await factory.UserClientAsync(SystemRoles.TeamMember);
        var (_, producer) = await factory.UserClientAsync(SystemRoles.Producer);

        var own = await CreateTaskAsync(memberClient, "Book the studio");
        var self = Assert.Single(own.Assignees);
        Assert.Equal((member.Id, true), (self.UserId, self.IsPrimary));
        Assert.True(own.CanEdit);

        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.PostAsJsonAsync("/api/v1/tasks",
            new CreateTaskRequest("Delegate", null, null, null, "Normal", [colleague.Id], null), Ct)).StatusCode);

        var delegated = await CreateTaskAsync(producer, "Prepare lighting", assignees: [member.Id, colleague.Id], primary: colleague.Id);
        Assert.Equal([colleague.Id, member.Id], delegated.Assignees.Select(a => a.UserId));
        Assert.True(delegated.Assignees[0].IsPrimary);

        // The member is an assignee, so they may update it, but may not add someone new.
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.PostAsJsonAsync($"/api/v1/tasks/{delegated.Id}/assignments",
            new AssignTaskRequest(Guid.NewGuid(), false), Ct)).StatusCode);
        var promoted = await (await producer.PostAsJsonAsync($"/api/v1/tasks/{delegated.Id}/assignments",
            new AssignTaskRequest(member.Id, true), Ct)).DataAsync<TaskResponse>();
        Assert.Equal(member.Id, promoted.Assignees.Single(a => a.IsPrimary).UserId);
    }

    [Fact]
    public async Task Assigned_tasks_appear_in_the_personal_dashboard_and_on_the_calendar()
    {
        var (member, memberClient) = await factory.UserClientAsync(SystemRoles.TeamMember);
        var (_, producer) = await factory.UserClientAsync(SystemRoles.Producer);
        var due = At(Day(30), 17);
        var task = await CreateTaskAsync(producer, "Deliver final mix", due, assignees: [member.Id]);

        var dashboard = await (await memberClient.GetAsync("/api/v1/dashboard/personal", Ct)).DataAsync<PersonalDashboardResponse>();
        Assert.Contains(dashboard.MyTasks, t => t.Id == task.Id);

        var item = Assert.Single(await CalendarAsync(memberClient, Day(28), Day(32), $"&assignedUserId={member.Id}"),
            i => i.SourceId == task.Id);
        Assert.Equal(("Task", "TaskDeadline", due), (item.SourceType, item.EventType, item.StartAt));
        Assert.Null(item.EndAt);
        Assert.Equal("Primary", item.AssignedUsers.Single().AssignmentType);
        Assert.True(item.CanEdit);
        Assert.Empty(await CalendarAsync(memberClient, Day(28), Day(32), "&eventTypes=Meeting&eventTypes=Milestone"));
    }

    [Fact]
    public async Task Completing_a_task_records_who_and_when_and_reopening_keeps_the_history()
    {
        var (member, memberClient) = await factory.UserClientAsync(SystemRoles.TeamMember);
        var task = await CreateTaskAsync(memberClient, "Upload rushes", At(Day(40), 12));

        var done = await (await memberClient.PatchAsJsonAsync($"/api/v1/tasks/{task.Id}/status", new ChangeStatusRequest("Completed"), Ct))
            .DataAsync<TaskResponse>();
        Assert.NotNull(done.CompletedAt);
        Assert.Equal(member.Id, done.CompletedBy!.Id);

        var reopened = await (await memberClient.PatchAsJsonAsync($"/api/v1/tasks/{task.Id}/status",
            new ChangeStatusRequest("InProgress", done.Version), Ct)).DataAsync<TaskResponse>();
        Assert.Null(reopened.CompletedAt);

        var stale = await memberClient.PatchAsJsonAsync($"/api/v1/tasks/{task.Id}/status", new ChangeStatusRequest("Blocked", done.Version), Ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var history = await (await memberClient.GetAsync($"/api/v1/tasks/{task.Id}/history", Ct)).DataAsync<List<ActivityEntryResponse>>();
        Assert.Equal(["Status changed from Completed to InProgress", "Status changed from NotStarted to Completed", "Task created"],
            history.Select(h => h.Summary));
        Assert.All(history, h => Assert.Equal(member.Id, h.User!.Id));
    }

    [Fact]
    public async Task Task_updates_record_rescheduling_and_reassignment()
    {
        var (member, _) = await factory.UserClientAsync(SystemRoles.TeamMember);
        var (_, producer) = await factory.UserClientAsync(SystemRoles.Producer);
        var task = await CreateTaskAsync(producer, "Colour grade", At(Day(50), 9), assignees: [member.Id]);
        var producerMe = await (await producer.GetAsync("/api/v1/auth/me", Ct)).DataAsync<CurrentUserResponse>();

        var updated = await (await producer.PutAsJsonAsync($"/api/v1/tasks/{task.Id}", new UpdateTaskRequest(
            "Colour grade v2", null, null, At(Day(52), 9), "High", [producerMe.Id, member.Id], producerMe.Id, task.Version), Ct))
            .DataAsync<TaskResponse>();
        Assert.Equal(producerMe.Id, updated.Assignees.Single(a => a.IsPrimary).UserId);

        var history = await (await producer.GetAsync($"/api/v1/tasks/{task.Id}/history", Ct)).DataAsync<List<ActivityEntryResponse>>();
        Assert.Contains(history, h => h.Action == "Rescheduled");
        Assert.Contains(history, h => h.Action == "AssigneesChanged" && h.Summary.Contains($"{producerMe.FullName} (primary)"));
        Assert.Contains(history, h => h.Action == "Updated" && h.Summary.Contains("title") && h.Summary.Contains("priority"));
    }

    [Fact]
    public async Task Task_lists_filter_by_view()
    {
        var (member, memberClient) = await factory.UserClientAsync(SystemRoles.TeamMember);
        var (_, producer) = await factory.UserClientAsync(SystemRoles.Producer);
        var marker = Guid.NewGuid().ToString("N")[..6];
        var mine = await CreateTaskAsync(producer, $"For member {marker}", At(Day(60), 9), assignees: [member.Id]);
        var overdue = await CreateTaskAsync(producer, $"Late {marker}", DateTimeOffset.UtcNow.AddDays(-2), assignees: [member.Id]);
        var own = await CreateTaskAsync(memberClient, $"Self {marker}");
        await memberClient.PatchAsJsonAsync($"/api/v1/tasks/{own.Id}/status", new ChangeStatusRequest("Completed"), Ct);

        async Task<Guid[]> Ids(HttpClient client, string path) =>
            [.. (await (await client.GetAsync(path, Ct)).DataAsync<PagedResponse<TaskResponse>>()).Items
                .Where(t => t.Title.EndsWith(marker)).Select(t => t.Id)];

        Assert.Equal([overdue.Id, mine.Id], await Ids(memberClient, "/api/v1/tasks/my-tasks"));
        Assert.Equal([overdue.Id], await Ids(memberClient, "/api/v1/tasks/overdue?mine=true"));
        Assert.Equal([overdue.Id, mine.Id], await Ids(producer, "/api/v1/tasks?assignedByMe=true"));
        Assert.Equal([own.Id], await Ids(memberClient, $"/api/v1/tasks?assigneeId={member.Id}&open=false"));
        Assert.Equal([mine.Id], await Ids(memberClient, $"/api/v1/tasks?search=for%20member%20{marker}"));
        Assert.True((await (await memberClient.GetAsync($"/api/v1/tasks/{overdue.Id}", Ct)).DataAsync<TaskResponse>()).IsOverdue);
    }

    // ---------- dashboards, overdue, workload ----------

    [Fact]
    public async Task Overdue_work_pending_reviews_and_workload_are_reported()
    {
        var admin = await factory.AdminClientAsync();
        var (member, memberClient) = await factory.UserClientAsync(SystemRoles.TeamMember);
        var (_, producer) = await factory.UserClientAsync(SystemRoles.Producer);
        var producerMe = await (await producer.GetAsync("/api/v1/auth/me", Ct)).DataAsync<CurrentUserResponse>();
        var project = await CreateProjectAsync(admin, $"Overdue project {Guid.NewGuid():N}"[..24]);
        await admin.PatchAsJsonAsync($"/api/v1/projects/{project.Id}/status", new ChangeStatusRequest("Active"), Ct);

        var lateTask = await CreateTaskAsync(producer, "Late script", DateTimeOffset.UtcNow.AddDays(-1), project.Id, [member.Id]);
        var soonTask = await CreateTaskAsync(producer, "Soon edit", DateTimeOffset.UtcNow.AddDays(3), project.Id, [member.Id]);
        var lateMilestone = await CreateMilestoneAsync(admin, project.Id, "Missed checkpoint",
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3), member.Id);
        var upcomingMilestone = await CreateMilestoneAsync(admin, project.Id, "Next checkpoint",
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5), member.Id);

        var overdue = await (await memberClient.GetAsync("/api/v1/dashboard/overdue?mine=true", Ct)).DataAsync<OverdueResponse>();
        Assert.Equal([lateTask.Id], overdue.Tasks.Select(t => t.Id));
        Assert.Equal([lateMilestone.Id], overdue.Milestones.Select(m => m.Id));
        Assert.True(overdue.Milestones[0].IsOverdue);

        // Submitting work for review puts it on the creator's dashboard.
        await memberClient.PatchAsJsonAsync($"/api/v1/tasks/{soonTask.Id}/status", new ChangeStatusRequest("AwaitingReview"), Ct);
        var producerDashboard = await (await producer.GetAsync("/api/v1/dashboard/personal", Ct)).DataAsync<PersonalDashboardResponse>();
        Assert.Contains(producerDashboard.PendingReviews, t => t.Id == soonTask.Id);

        var workload = await (await admin.GetAsync($"/api/v1/users/{member.Id}/workload", Ct)).DataAsync<WorkloadResponse>();
        Assert.Equal((2, 1, 1, 2), (workload.Summary.OpenTasks, workload.Summary.OverdueTasks, workload.Summary.DueThisWeek,
            workload.Summary.OpenMilestones));
        Assert.Equal([lateTask.Id, soonTask.Id], workload.OpenTasks.Select(t => t.Id));

        var management = await (await admin.GetAsync("/api/v1/dashboard/management", Ct)).DataAsync<ManagementDashboardResponse>();
        var summary = management.ActiveProjects.Single(p => p.Id == project.Id);
        Assert.Equal((2, 2, "Missed checkpoint"), (summary.OpenTasks, summary.OverdueItems, summary.NextMilestoneTitle));
        Assert.Contains(management.UpcomingMilestones, m => m.Id == upcomingMilestone.Id);
        Assert.Contains(management.Overdue.Tasks, t => t.Id == lateTask.Id);
        Assert.Contains(management.TeamWorkload, w => w.UserId == member.Id && w.OverdueTasks == 1);
        Assert.DoesNotContain(management.TeamWorkload, w => w.UserId == producerMe.Id && w.OpenTasks > 0);
    }

    // ---------- audit trail ----------

    [Fact]
    public async Task The_activity_log_records_security_and_calendar_changes_and_cannot_be_altered()
    {
        var admin = await factory.AdminClientAsync();
        var start = At(Day(70), 9);
        var calendarEvent = await (await admin.PostAsJsonAsync("/api/v1/calendar/events", new CreateCalendarEventRequest(
            "Audit me", null, "Meeting", start, start.AddHours(1), false, null, null, "Normal", []), Ct)).DataAsync<CalendarEventResponse>();
        await admin.PutAsJsonAsync($"/api/v1/calendar/events/{calendarEvent.Id}", new UpdateCalendarEventRequest(
            "Audit me", null, "Meeting", start.AddDays(1), start.AddDays(1).AddHours(1), false, null, null, "Normal", "Scheduled", [],
            calendarEvent.Version), Ct);
        var (member, _) = await factory.UserClientAsync(SystemRoles.TeamMember);
        await admin.PatchAsJsonAsync($"/api/v1/users/{member.Id}/status", new UpdateUserStatusRequest(false), Ct);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DofDbContext>();
        var eventActions = await db.ActivityLogs.Where(a => a.EntityId == calendarEvent.Id).Select(a => a.Action).ToListAsync(Ct);
        Assert.Equal(["Created", "Rescheduled"], eventActions.Order());
        Assert.Contains(await db.ActivityLogs.Where(a => a.EntityId == member.Id).Select(a => a.Action).ToListAsync(Ct), a => a == "StatusChanged");

        // The database itself rejects edits and deletions of history.
        var update = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE activity_logs SET action = 'Tampered'", Ct));
        Assert.Contains("append-only", update.MessageText);
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM activity_logs", Ct));
    }

    [Fact]
    public void Shared_work_values_match_the_domain()
    {
        Assert.Equal(Enum.GetNames<DOF.Domain.Enums.ProjectStatus>(), WorkValues.ProjectStatuses);
        Assert.Equal(Enum.GetNames<DOF.Domain.Enums.ProjectType>(), WorkValues.ProjectTypes);
        Assert.Equal(Enum.GetNames<DOF.Domain.Enums.WorkStatus>(), WorkValues.WorkStatuses);
    }
}
