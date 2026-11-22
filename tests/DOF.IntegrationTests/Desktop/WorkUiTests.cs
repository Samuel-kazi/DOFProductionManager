using System.Net.Http.Json;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Desktop.ViewModels;
using DOF.Desktop.ViewModels.Calendar;
using DOF.Desktop.ViewModels.Pages;
using DOF.Desktop.ViewModels.Work;
using DOF.Domain.Rules;
using static DOF.IntegrationTests.Desktop.Ui;
using static DOF.IntegrationTests.TestApi;
using CalendarMode = DOF.Desktop.ViewModels.Calendar.CalendarMode;

namespace DOF.IntegrationTests.Desktop;

/// <summary>Projects, tasks, and their dashboard and calendar entries in the desktop client, against the real API.</summary>
public class WorkUiTests(DofApiFactory factory, HeadlessSession headless)
{
    private static readonly TimeZoneInfo Nairobi = TimeZoneInfo.FindSystemTimeZoneById("Africa/Nairobi");
    private static DateOnly Day(int n) => new DateOnly(2034, 2, 6).AddDays(n);
    private static DateTimeOffset At(DateOnly day, int hour) =>
        new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(new TimeOnly(hour, 0)), Nairobi), TimeSpan.Zero);

    private static async Task<T> PageAsync<T>(ShellViewModel shell, string title) where T : PageViewModel
    {
        await shell.NavigateCommand.ExecuteAsync(shell.AllItems.Single(i => i.Title == title));
        var page = Assert.IsType<T>(shell.CurrentPage);
        await WaitForAsync(() => page.IsLoading ? null : page);
        Assert.False(page.HasError, page.ErrorMessage);
        return page;
    }

    [Fact]
    public Task A_project_can_be_created_and_managed_from_the_projects_page() => Ui.Run(headless, async () =>
    {
        var (member, _) = await factory.UserClientAsync(SystemRoles.TeamMember);
        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);
        var shell = await SignInAsync(main);
        var projects = await PageAsync<ProjectsViewModel>(shell, "Projects");
        Assert.True(projects.CanCreate);

        // Create
        await projects.NewProjectCommand.ExecuteAsync(null);
        var editor = Assert.IsType<ProjectEditorViewModel>(projects.SidePanel);
        var name = $"Easter documentary {Guid.NewGuid():N}"[..28];
        editor.Name = name;
        editor.ProjectType = "Documentary";
        editor.SelectedManager = editor.Managers.Single(m => m.Label == "System Administrator");
        editor.StartDate = Day(0).ToDateTime(TimeOnly.MinValue);
        editor.TargetEndDate = Day(60).ToDateTime(TimeOnly.MinValue);
        Snapshot(window, "20-project-editor");
        await projects.SaveProjectCommand.ExecuteAsync(editor);
        var detail = Assert.IsType<ProjectDetailViewModel>(projects.Detail);
        Assert.Equal(name, detail.Name);

        // Milestone
        await projects.NewMilestoneCommand.ExecuteAsync(null);
        var milestone = Assert.IsType<MilestoneEditorViewModel>(projects.SidePanel);
        milestone.Title = "Script locked";
        milestone.DueDate = Day(14).ToDateTime(TimeOnly.MinValue);
        milestone.SelectedPerson = milestone.People.Single(p => p.Id == member.Id);
        await projects.SaveMilestoneCommand.ExecuteAsync(milestone);
        detail = projects.Detail!;
        Assert.Equal("Script locked", Assert.Single(detail.Milestones).Title);

        // Member
        detail.NewMember = detail.NonMembers.Single(p => p.Id == member.Id);
        detail.NewMemberRole = "Researcher";
        await projects.AddMemberCommand.ExecuteAsync(null);
        Assert.Contains(projects.Detail!.Members, m => m.UserId == member.Id && m.ProjectRole == "Researcher");

        // Status (saved through the picker) and history
        projects.Detail!.SelectedStatus = "Active";
        await WaitForAsync(() => projects.Detail?.Project.Status == "Active" ? projects.Detail : null);
        var history = projects.Detail!.History.Select(h => h.Summary).ToList();
        Assert.Contains("Status changed from Proposed to Active", history);
        Assert.Contains("Milestone created", history);
        Assert.Contains(history, h => h.EndsWith("joined as Researcher"));
        Snapshot(window, "21-project-detail");

        await projects.BackToListCommand.ExecuteAsync(null);
        Assert.Contains(projects.Projects, p => p.Name == name && p.NextMilestoneText.StartsWith("Next: Script locked"));
        Snapshot(window, "22-projects-list");
        window.Close();
    });

    [Fact]
    public Task Tasks_are_created_listed_completed_and_shown_on_a_board() => Ui.Run(headless, async () =>
    {
        var (member, _) = await factory.UserClientAsync(SystemRoles.TeamMember);
        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);
        var shell = await SignInAsync(main);
        var tasks = await PageAsync<TasksViewModel>(shell, "Tasks");
        Assert.Equal(TaskView.MyTasks, tasks.SelectedView.View);

        // Create a task for someone else (the administrator may assign).
        await tasks.NewTaskCommand.ExecuteAsync(null);
        var editor = Assert.IsType<TaskEditorViewModel>(tasks.SidePanel);
        Assert.True(editor.CanAssignOthers);
        var title = $"Prepare autocue {Guid.NewGuid():N}"[..24];
        editor.Title = title;
        editor.HasDueDate = true;
        editor.DueDate = Day(3).ToDateTime(TimeOnly.MinValue);
        editor.DueTime = new TimeSpan(10, 0, 0);
        foreach (var person in editor.People)
        {
            person.IsSelected = person.Id == member.Id || person.FullName == "System Administrator";
        }
        editor.People.Single(p => p.Id == member.Id).IsPrimary = true;
        Snapshot(window, "23-task-editor");
        await tasks.SaveTaskCommand.ExecuteAsync(editor);

        var details = Assert.IsType<TaskDetailsViewModel>(tasks.SidePanel);
        Assert.Equal(At(Day(3), 10), details.Task.DueAt);
        Assert.Equal(member.Id, details.Task.Assignees.Single(a => a.IsPrimary).UserId);
        Assert.Contains(details.History, h => h.Summary == "Task created");
        Snapshot(window, "24-task-details");

        // It's in "My tasks" (the admin is an assignee) and "Assigned by me".
        var item = await WaitForAsync(() => tasks.Items.FirstOrDefault(i => i.Title == title));
        tasks.SelectedView = tasks.Views.Single(v => v.View == TaskView.AssignedByMe);
        item = await WaitForAsync(() => tasks.Items.FirstOrDefault(i => i.Title == title));

        // Complete straight from the list, then see it under Completed.
        await tasks.ToggleCompleteCommand.ExecuteAsync(item);
        tasks.SelectedView = tasks.Views.Single(v => v.View == TaskView.Completed);
        var done = await WaitForAsync(() => tasks.Items.FirstOrDefault(i => i.Title == title));
        Assert.True(done.IsCompleted);
        Assert.NotNull(done.Task.CompletedBy);

        // Board groups by status.
        tasks.ShowBoardCommand.Execute(null);
        Assert.Equal(["NotStarted", "InProgress", "AwaitingReview", "Blocked", "Completed"], tasks.Columns.Select(c => c.Status));
        Assert.Contains(tasks.Columns.Single(c => c.Status == "Completed").Tasks, t => t.Title == title);
        tasks.CloseSidePanelCommand.Execute(null);
        Snapshot(window, "25-tasks-board");
        window.Close();
    });

    [Fact]
    public Task Team_members_create_tasks_only_for_themselves() => Ui.Run(headless, async () =>
    {
        var (member, _) = await factory.UserClientAsync(SystemRoles.TeamMember);
        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        Show(main);
        var shell = await SignInAsync(main, member.Email, member.Password);
        Assert.False(shell.CanCreateProjects);
        Assert.False((await PageAsync<ProjectsViewModel>(shell, "Projects")).CanCreate);

        await shell.CreateTaskCommand.ExecuteAsync(null);
        var tasks = Assert.IsType<TasksViewModel>(shell.CurrentPage);
        var editor = Assert.IsType<TaskEditorViewModel>(tasks.SidePanel);
        Assert.False(editor.CanAssignOthers);
        Assert.Equal(member.Id, Assert.Single(editor.People).Id);
        editor.Title = "Charge camera batteries";
        await tasks.SaveTaskCommand.ExecuteAsync(editor);
        var details = Assert.IsType<TaskDetailsViewModel>(tasks.SidePanel);
        Assert.Equal(member.Id, Assert.Single(details.Task.Assignees).UserId);

        // Status changes from the details panel are saved and recorded.
        details.SelectedStatus = "InProgress";
        var updated = await WaitForAsync(() => tasks.SidePanel is TaskDetailsViewModel d && d.Task.Status == "InProgress" ? d : null);
        Assert.Contains(updated.History, h => h.Summary == "Status changed from Not started to In progress");
    });

    [Fact]
    public Task Milestones_and_tasks_on_the_calendar_open_their_records_and_can_be_dragged() => Ui.Run(headless, async () =>
    {
        var admin = await factory.AdminClientAsync();
        var project = await (await admin.PostAsJsonAsync("/api/v1/projects",
            new CreateProjectRequest("Calendar-linked project", null, "Campaign", "Normal", null, null, null), Ct)).DataAsync<ProjectResponse>();
        var milestone = await (await admin.PostAsJsonAsync($"/api/v1/projects/{project.Id}/milestones",
            new CreateMilestoneRequest("Launch day", null, Day(22), "High", null), Ct)).DataAsync<MilestoneResponse>();
        var task = await (await admin.PostAsJsonAsync("/api/v1/tasks",
            new CreateTaskRequest("Send press kit", null, project.Id, At(Day(23), 11), "Normal", null, null), Ct)).DataAsync<TaskResponse>();

        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);
        var shell = await SignInAsync(main);
        var calendar = await PageAsync<CalendarViewModel>(shell, "Calendar");
        calendar.AnchorDate = Day(22);
        await calendar.SetModeCommand.ExecuteAsync(CalendarMode.Week);

        var milestoneItem = calendar.DayColumns.Single(c => c.Date == Day(22)).AllDayItems.Single(i => i.Item.SourceId == milestone.Id);
        var taskItem = calendar.DayColumns.Single(c => c.Date == Day(23)).TimedItems.Single(t => t.Item.Item.SourceId == task.Id);
        Assert.Equal(11 * 60, taskItem.StartMinute);
        Assert.True(milestoneItem.CanReschedule);
        Snapshot(window, "26-calendar-with-work");

        // Dragging moves the owning records.
        await calendar.RescheduleAsync(milestoneItem, Day(24).ToDateTime(TimeOnly.MinValue));
        await calendar.RescheduleAsync(taskItem.Item, At(Day(25), 14).ToOffset(TimeSpan.FromHours(3)).DateTime);
        var moved = await (await admin.GetAsync($"/api/v1/milestones/{milestone.Id}", Ct)).DataAsync<MilestoneResponse>();
        var movedTask = await (await admin.GetAsync($"/api/v1/tasks/{task.Id}", Ct)).DataAsync<TaskResponse>();
        Assert.Equal(Day(24), moved.DueDate);
        Assert.Equal(At(Day(25), 14), movedTask.DueAt);

        // Opening items goes to their records.
        var milestoneNow = calendar.DayColumns.Single(c => c.Date == Day(24)).AllDayItems.Single(i => i.Item.SourceId == milestone.Id);
        await calendar.OpenItemCommand.ExecuteAsync(milestoneNow);
        var projects = Assert.IsType<ProjectsViewModel>(shell.CurrentPage);
        Assert.Equal(project.Id, projects.Detail!.Project.Id);
        Assert.Equal(milestone.Id, Assert.IsType<MilestoneEditorViewModel>(projects.SidePanel).Existing!.Id);

        await shell.OpenRecordAsync("Task", task.Id);
        var tasks = Assert.IsType<TasksViewModel>(shell.CurrentPage);
        Assert.Equal(task.Id, Assert.IsType<TaskDetailsViewModel>(tasks.SidePanel).Task.Id);
        window.Close();
    });

    [Fact]
    public Task Dashboards_show_my_tasks_overdue_work_and_workload() => Ui.Run(headless, async () =>
    {
        var admin = await factory.AdminClientAsync();
        var me = await (await admin.GetAsync("/api/v1/auth/me", Ct)).DataAsync<CurrentUserResponse>();
        var (member, _) = await factory.UserClientAsync(SystemRoles.TeamMember);
        var mine = await (await admin.PostAsJsonAsync("/api/v1/tasks", new CreateTaskRequest(
            $"Mine {Guid.NewGuid():N}"[..12], null, null, DateTimeOffset.UtcNow.AddHours(5), "High", [me.Id], me.Id), Ct)).DataAsync<TaskResponse>();
        var late = await (await admin.PostAsJsonAsync("/api/v1/tasks", new CreateTaskRequest(
            $"Late {Guid.NewGuid():N}"[..12], null, null, DateTimeOffset.UtcNow.AddDays(-1), "Normal", [member.Id], member.Id), Ct)).DataAsync<TaskResponse>();

        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);
        var shell = await SignInAsync(main);
        var dashboard = Assert.IsType<DashboardViewModel>(shell.CurrentPage);

        Assert.Contains(dashboard.MyTasks, t => t.Id == mine.Id);
        Snapshot(window, "27-dashboard-my-tasks");

        dashboard.ShowOrganisationCommand.Execute(null);
        Assert.Contains(dashboard.OverdueTasks, t => t.Id == late.Id);
        var row = dashboard.TeamWorkload.Single(w => w.Summary.UserId == member.Id);
        Assert.True(row.HasOverdue);
        Snapshot(window, "28-dashboard-organisation-work");

        // A workload row opens that person's tasks.
        await dashboard.OpenPersonCommand.ExecuteAsync(row);
        var tasks = Assert.IsType<TasksViewModel>(shell.CurrentPage);
        await WaitForAsync(() => tasks.Items.FirstOrDefault(t => t.Id == late.Id));
        Assert.True(tasks.HasPersonFilter);
        Assert.All(tasks.Items, t => Assert.Contains(t.Task.Assignees, a => a.UserId == member.Id));

        // Completing from the dashboard.
        await shell.NavigateCommand.ExecuteAsync(shell.PrimaryItems[0]);
        dashboard = Assert.IsType<DashboardViewModel>(shell.CurrentPage);
        await WaitForAsync(() => dashboard.IsLoading ? null : dashboard);
        await dashboard.CompleteTaskCommand.ExecuteAsync(dashboard.MyTasks.Single(t => t.Id == mine.Id));
        Assert.DoesNotContain(dashboard.MyTasks, t => t.Id == mine.Id);
        window.Close();
    });
}
