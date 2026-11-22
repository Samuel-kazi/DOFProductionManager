using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Desktop.Services;
using DOF.Desktop.Services.Api;
using DOF.Desktop.ViewModels.Work;

namespace DOF.Desktop.ViewModels.Pages;

public sealed class ProjectCardViewModel(ProjectSummaryResponse project, DateOnly today)
{
    public ProjectSummaryResponse Project { get; } = project;
    public string Name => Project.Name;
    public string TypeText => WorkLabels.ProjectType(Project.ProjectType);
    public string StatusText => WorkLabels.Status(Project.Status);
    public string StatusBrushKey => WorkLabels.StatusBrushKey(Project.Status);
    public string ManagerText => Project.Manager?.FullName ?? "No manager";
    public string OpenTasksText => Project.OpenTasks == 1 ? "1 open task" : $"{Project.OpenTasks} open tasks";
    public bool HasOverdue => Project.OverdueItems > 0;
    public string OverdueText => $"{Project.OverdueItems} overdue";
    public string NextMilestoneText => Project.NextMilestoneTitle is { } title && Project.NextMilestoneDue is { } due
        ? $"Next: {title} · {WorkLabels.DueDate(due, today)}"
        : "No upcoming milestones";
}

/// <summary>Project list and project detail (milestones, tasks, members, history).</summary>
public sealed partial class ProjectsViewModel(ApiClient api, AuthSession session, UserClock clock, Lookups lookups) : PageViewModel
{
    public override string Title => "Projects";

    public bool CanCreate => session.CurrentUser?.Permissions.Contains("projects.create") == true;

    public ObservableCollection<ProjectCardViewModel> Projects { get; } = [];

    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IncludeArchived { get; set; }
    [ObservableProperty] public partial bool OnlyMine { get; set; }

    partial void OnIncludeArchivedChanged(bool value) => _ = LoadAsync();
    partial void OnOnlyMineChanged(bool value) => _ = LoadAsync();

    public bool IsEmpty => !IsLoading && !HasError && Projects.Count == 0 && Detail is null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowingList), nameof(ShowingDetail))]
    public partial ProjectDetailViewModel? Detail { get; private set; }

    public bool ShowingList => Detail is null;
    public bool ShowingDetail => Detail is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSidePanel))]
    public partial object? SidePanel { get; private set; }

    public bool HasSidePanel => SidePanel is not null;

    private DateOnly Today => clock.Today;

    public override async Task LoadAsync()
    {
        var path = "api/v1/projects?pageSize=100";
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            path += $"&search={Uri.EscapeDataString(SearchText.Trim())}";
        }
        if (IncludeArchived)
        {
            path += "&includeArchived=true";
        }
        if (OnlyMine && session.CurrentUser is { } me)
        {
            path += $"&memberId={me.Id}";
        }

        await RunAsync(async () =>
        {
            var page = await api.GetAsync<PagedResponse<ProjectSummaryResponse>>(path);
            Projects.Clear();
            foreach (var project in page.Items)
            {
                Projects.Add(new ProjectCardViewModel(project, Today));
            }
        });
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private Task SearchAsync() => LoadAsync();

    [RelayCommand]
    private Task OpenCardAsync(ProjectCardViewModel card) => OpenProjectAsync(card.Project.Id);

    /// <summary>Opens a project's detail (also used by the shell for Project and Milestone records).</summary>
    public async Task OpenProjectAsync(Guid id, Guid? milestoneId = null)
    {
        await RunAsync(async () =>
        {
            var project = await api.GetAsync<ProjectResponse>($"api/v1/projects/{id}");
            var timeline = await api.GetAsync<ProjectTimelineResponse>($"api/v1/projects/{id}/timeline");
            var people = project.CanEdit ? (await lookups.PeopleAsync()).Select(u => new ChoiceOption(u.Id, u.FullName)) : [];
            var detail = new ProjectDetailViewModel(project, timeline, clock, Today, people);
            detail.StatusChanged = status => ChangeStatusAsync(detail, status);
            Detail = detail;
            SidePanel = null;
            if (milestoneId is { } m && project.Milestones.FirstOrDefault(x => x.Id == m) is { } milestone)
            {
                SidePanel = new MilestoneEditorViewModel(id, people, milestone, Today);
            }
        });
    }

    /// <summary>Opens the project that owns a milestone, with that milestone shown.</summary>
    public async Task OpenMilestoneByIdAsync(Guid milestoneId)
    {
        MilestoneResponse? milestone = null;
        if (await RunAsync(async () => milestone = await api.GetAsync<MilestoneResponse>($"api/v1/milestones/{milestoneId}")))
        {
            await OpenProjectAsync(milestone!.Project.Id, milestoneId);
        }
    }

    private Task ReloadDetailAsync() => Detail is { } d ? OpenProjectAsync(d.Project.Id) : Task.CompletedTask;

    [RelayCommand]
    private async Task BackToListAsync()
    {
        Detail = null;
        SidePanel = null;
        await LoadAsync();
    }

    [RelayCommand]
    private void CloseSidePanel() => SidePanel = null;

    // ---- project editing ----

    [RelayCommand]
    public async Task NewProjectAsync()
    {
        await RunAsync(async () => SidePanel = new ProjectEditorViewModel(
            (await lookups.PeopleAsync()).Select(u => new ChoiceOption(u.Id, u.FullName)), null));
    }

    [RelayCommand]
    private async Task EditProjectAsync()
    {
        if (Detail is { } detail)
        {
            await RunAsync(async () => SidePanel = new ProjectEditorViewModel(
                (await lookups.PeopleAsync()).Select(u => new ChoiceOption(u.Id, u.FullName)), detail.Project));
        }
    }

    [RelayCommand]
    private async Task SaveProjectAsync(ProjectEditorViewModel editor) => await SaveAsync(editor, async () =>
    {
        var saved = editor.Existing is { } existing
            ? await api.PutAsync<ProjectResponse>($"api/v1/projects/{existing.Id}", new UpdateProjectRequest(
                editor.Name, editor.Description, editor.ProjectType, editor.Priority, editor.SelectedManager.Id,
                ProjectEditorViewModel.ToDate(editor.StartDate), ProjectEditorViewModel.ToDate(editor.TargetEndDate), existing.Version))
            : await api.PostAsync<ProjectResponse>("api/v1/projects", new CreateProjectRequest(
                editor.Name, editor.Description, editor.ProjectType, editor.Priority, editor.SelectedManager.Id,
                ProjectEditorViewModel.ToDate(editor.StartDate), ProjectEditorViewModel.ToDate(editor.TargetEndDate)));
        AppLog.Info($"Saved project {saved.Id}");
        await OpenProjectAsync(saved.Id);
    }, message => editor.ErrorMessage = message);

    private async Task ChangeStatusAsync(ProjectDetailViewModel detail, string status)
    {
        await RunAsync(() => api.PatchAsync<ProjectResponse>($"api/v1/projects/{detail.Project.Id}/status",
            new ChangeStatusRequest(status, detail.Project.Version)));
        await ReloadDetailAsync();
    }

    // ---- milestones ----

    [RelayCommand]
    private async Task NewMilestoneAsync()
    {
        if (Detail is { } detail)
        {
            await RunAsync(async () => SidePanel = new MilestoneEditorViewModel(detail.Project.Id,
                (await lookups.PeopleAsync()).Select(u => new ChoiceOption(u.Id, u.FullName)), null, Today.AddDays(14)));
        }
    }

    [RelayCommand]
    private async Task OpenMilestoneAsync(MilestoneItemViewModel item)
    {
        if (Detail is { } detail)
        {
            await RunAsync(async () =>
            {
                var people = item.CanEdit ? (await lookups.PeopleAsync()).Select(u => new ChoiceOption(u.Id, u.FullName)) : [];
                SidePanel = new MilestoneEditorViewModel(detail.Project.Id, people, item.Milestone, Today);
            });
        }
    }

    [RelayCommand]
    private async Task SaveMilestoneAsync(MilestoneEditorViewModel editor) => await SaveAsync(editor, async () =>
    {
        var due = DateOnly.FromDateTime(editor.DueDate ?? Today.ToDateTime(TimeOnly.MinValue));
        if (editor.Existing is { } existing)
        {
            await api.PutAsync<MilestoneResponse>($"api/v1/milestones/{existing.Id}", new UpdateMilestoneRequest(
                editor.Title, editor.Description, due, editor.Priority, editor.Status, editor.SelectedPerson.Id, existing.Version));
        }
        else
        {
            await api.PostAsync<MilestoneResponse>($"api/v1/projects/{editor.ProjectId}/milestones", new CreateMilestoneRequest(
                editor.Title, editor.Description, due, editor.Priority, editor.SelectedPerson.Id));
        }
        await ReloadDetailAsync();
    }, message => editor.ErrorMessage = message);

    [RelayCommand]
    private async Task CompleteMilestoneAsync(MilestoneItemViewModel item)
    {
        if (item.CanEdit && !item.IsCompleted
            && await RunAsync(() => api.PatchAsync<MilestoneResponse>($"api/v1/milestones/{item.Id}/complete", new { })))
        {
            await ReloadDetailAsync();
        }
    }

    // ---- members and tasks ----

    [RelayCommand]
    private async Task AddMemberAsync()
    {
        if (Detail is { NewMember.Id: { } userId } detail
            && await RunAsync(() => api.PostAsync<ProjectResponse>($"api/v1/projects/{detail.Project.Id}/members",
                new AddProjectMemberRequest(userId, detail.NewMemberRole))))
        {
            await ReloadDetailAsync();
        }
    }

    [RelayCommand]
    private async Task RemoveMemberAsync(ProjectMemberResponse member)
    {
        if (Detail is { } detail
            && await RunAsync(() => api.DeleteAsync($"api/v1/projects/{detail.Project.Id}/members/{member.UserId}")))
        {
            await ReloadDetailAsync();
        }
    }

    [RelayCommand]
    private Task OpenTaskAsync(TaskItemViewModel task) =>
        Navigator?.OpenRecordAsync("Task", task.Id) ?? Task.CompletedTask;

    [RelayCommand]
    private Task NewTaskAsync() =>
        Navigator?.CreateTaskAsync(Detail?.Project.Id) ?? Task.CompletedTask;

    private async Task SaveAsync(object editor, Func<Task> save, Action<string?> showError)
    {
        showError(null);
        try
        {
            await save();
        }
        catch (ApiException ex)
        {
            showError(ex.Message);
        }
        catch (ApiUnavailableException ex)
        {
            showError(ex.Message);
        }
        _ = editor;
    }
}
