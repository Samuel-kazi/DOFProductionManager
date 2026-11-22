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

public enum TaskView
{
    MyTasks,
    AssignedByMe,
    All,
    Overdue,
    Completed,
}

public sealed class BoardColumn(string status, IReadOnlyList<TaskItemViewModel> tasks)
{
    public string Status { get; } = status;
    public string Header => $"{WorkLabels.Status(Status)} · {Tasks.Count}";
    public string StatusBrushKey => WorkLabels.StatusBrushKey(Status);
    public IReadOnlyList<TaskItemViewModel> Tasks { get; } = tasks;
}

public sealed record TaskViewOption(TaskView View, string Label);

/// <summary>Tasks in list or board form, with views for "mine", "assigned by me", all, overdue and completed.</summary>
public sealed partial class TasksViewModel(ApiClient api, AuthSession session, UserClock clock, Lookups lookups) : PageViewModel
{
    private static readonly string[] BoardStatuses = ["NotStarted", "InProgress", "AwaitingReview", "Blocked", "Completed"];

    public override string Title => "Tasks";

    public IReadOnlyList<TaskViewOption> Views { get; } =
    [
        new(TaskView.MyTasks, "My tasks"),
        new(TaskView.AssignedByMe, "Assigned by me"),
        new(TaskView.All, "All tasks"),
        new(TaskView.Overdue, "Overdue"),
        new(TaskView.Completed, "Completed"),
    ];

    [ObservableProperty]
    public partial TaskViewOption SelectedView { get; set; } = null!;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsList))]
    public partial bool IsBoard { get; set; }

    public bool IsList => !IsBoard;

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    /// <summary>Set when opened from a person's workload: show only their tasks.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPersonFilter), nameof(PersonFilterText))]
    public partial (Guid Id, string Name)? PersonFilter { get; set; }

    public bool HasPersonFilter => PersonFilter is not null;
    public string PersonFilterText => PersonFilter is { } p ? $"Showing tasks for {p.Name}" : string.Empty;

    public ObservableCollection<TaskItemViewModel> Items { get; } = [];
    public ObservableCollection<BoardColumn> Columns { get; } = [];

    [ObservableProperty]
    public partial string SummaryText { get; private set; } = string.Empty;

    public bool IsEmpty => !IsLoading && !HasError && Items.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSidePanel))]
    public partial object? SidePanel { get; private set; }

    public bool HasSidePanel => SidePanel is not null;

    partial void OnSelectedViewChanged(TaskViewOption value) => _ = LoadAsync();

    partial void OnIsBoardChanged(bool value) => BuildBoard();

    public override Task LoadAsync()
    {
        if (SelectedView is null)
        {
            // Setting the view triggers the load.
            SelectedView = Views[0];
            return Task.CompletedTask;
        }
        return LoadCoreAsync();
    }

    private async Task LoadCoreAsync()
    {
        var me = session.CurrentUser!.Id;
        var path = SelectedView.View switch
        {
            TaskView.MyTasks => $"api/v1/tasks?assigneeId={me}&open=true",
            TaskView.AssignedByMe => "api/v1/tasks?assignedByMe=true&open=true",
            TaskView.Overdue => "api/v1/tasks?overdue=true",
            TaskView.Completed => "api/v1/tasks?statuses=Completed",
            _ => "api/v1/tasks?open=true",
        };
        if (PersonFilter is { } person && SelectedView.View is TaskView.All or TaskView.Overdue or TaskView.Completed)
        {
            path += $"&assigneeId={person.Id}";
        }
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            path += $"&search={Uri.EscapeDataString(SearchText.Trim())}";
        }
        path += "&pageSize=100";

        await RunAsync(async () =>
        {
            var page = await api.GetAsync<PagedResponse<TaskResponse>>(path);
            Items.Clear();
            foreach (var task in page.Items)
            {
                Items.Add(new TaskItemViewModel(task, clock));
            }
            SummaryText = page.TotalCount > page.Items.Count
                ? $"Showing {page.Items.Count} of {page.TotalCount} — refine the search to see others"
                : page.TotalCount == 1 ? "1 task" : $"{page.TotalCount} tasks";
            BuildBoard();
        });
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void BuildBoard()
    {
        Columns.Clear();
        if (!IsBoard)
        {
            return;
        }
        foreach (var status in BoardStatuses)
        {
            Columns.Add(new BoardColumn(status, [.. Items.Where(i => i.Status == status)]));
        }
    }

    [RelayCommand]
    private Task SearchAsync() => LoadCoreAsync();

    [RelayCommand]
    private void ShowList() => IsBoard = false;

    [RelayCommand]
    private void ShowBoard() => IsBoard = true;

    [RelayCommand]
    private Task ClearPersonFilterAsync()
    {
        PersonFilter = null;
        return LoadCoreAsync();
    }

    /// <summary>Shows one person's tasks (from the workload list).</summary>
    public Task ShowPersonAsync(Guid userId, string fullName)
    {
        PersonFilter = (userId, fullName);
        if (SelectedView?.View != TaskView.All)
        {
            SelectedView = Views.Single(v => v.View == TaskView.All);
            return Task.CompletedTask;
        }
        return LoadCoreAsync();
    }

    /// <summary>Ticks a task done (or reopens it) straight from the list.</summary>
    [RelayCommand]
    private async Task ToggleCompleteAsync(TaskItemViewModel item)
    {
        if (!item.CanEdit)
        {
            return;
        }
        item.IsBusy = true;
        var target = item.IsCompleted ? "NotStarted" : "Completed";
        if (await RunAsync(() => api.PatchAsync<TaskResponse>($"api/v1/tasks/{item.Id}/status", new ChangeStatusRequest(target))))
        {
            AppLog.Info($"Task {item.Id} -> {target}");
            await LoadCoreAsync();
            if (SidePanel is TaskDetailsViewModel open && open.Task.Id == item.Id)
            {
                await OpenTaskAsync(item.Id);
            }
        }
        item.IsBusy = false;
    }

    [RelayCommand]
    private Task OpenItemAsync(TaskItemViewModel item) => OpenTaskAsync(item.Id);

    /// <summary>Shows a task's details and history (also used when opening a task from elsewhere).</summary>
    public async Task OpenTaskAsync(Guid id) => await RunAsync(async () =>
    {
        var task = await api.GetAsync<TaskResponse>($"api/v1/tasks/{id}");
        var history = await api.GetAsync<List<ActivityEntryResponse>>($"api/v1/tasks/{id}/history");
        var details = new TaskDetailsViewModel(task, history, clock);
        details.StatusChanged = status => ChangeStatusAsync(details, status);
        SidePanel = details;
    });

    private async Task ChangeStatusAsync(TaskDetailsViewModel details, string status)
    {
        if (await RunAsync(() => api.PatchAsync<TaskResponse>($"api/v1/tasks/{details.Task.Id}/status",
                new ChangeStatusRequest(status, details.Task.Version))))
        {
            await LoadCoreAsync();
        }
        await OpenTaskAsync(details.Task.Id);
    }

    [RelayCommand]
    private void CloseSidePanel() => SidePanel = null;

    [RelayCommand]
    public async Task NewTaskAsync(Guid? projectId) => await NewTaskAsync(projectId, null);

    public async Task NewTaskAsync(Guid? projectId, Guid? episodeId)
    {
        if (await EditorAsync(null, projectId, episodeId) is { } editor)
        {
            SidePanel = editor;
        }
    }

    [RelayCommand]
    private async Task EditTaskAsync(TaskDetailsViewModel details)
    {
        if (await EditorAsync(details.Task, null) is { } editor)
        {
            SidePanel = editor;
        }
    }

    private async Task<TaskEditorViewModel?> EditorAsync(TaskResponse? existing, Guid? projectId, Guid? episodeId = null)
    {
        TaskEditorViewModel? editor = null;
        await RunAsync(async () =>
        {
            var me = session.CurrentUser!.Id;
            var canAssign = session.CurrentUser.Permissions.Contains("tasks.assign");
            var people = canAssign
                ? (await lookups.PeopleAsync()).Select(u => new PersonOption(u.Id, u.FullName))
                : [new PersonOption(me, session.CurrentUser.FullName)];
            var projects = (await lookups.OpenProjectsAsync()).Select(p => new ChoiceOption(p.Id, p.Name));
            var episodes = (await lookups.OpenEpisodesAsync()).Select(e => new ChoiceOption(e.Id, Lookups.EpisodeLabel(e)));
            editor = new TaskEditorViewModel(clock, people, projects, episodes, canAssign, me, existing, projectId, episodeId);
        });
        return editor;
    }

    [RelayCommand]
    private async Task SaveTaskAsync(TaskEditorViewModel editor)
    {
        editor.ErrorMessage = null;
        editor.IsSaving = true;
        try
        {
            var (ids, primary) = editor.Assignees();
            var saved = editor.Existing is { } existing
                ? await api.PutAsync<TaskResponse>($"api/v1/tasks/{existing.Id}", new UpdateTaskRequest(
                    editor.Title, editor.Description, editor.ProjectId, editor.Due, editor.Priority, ids, primary,
                    existing.Version, editor.EpisodeId))
                : await api.PostAsync<TaskResponse>("api/v1/tasks", new CreateTaskRequest(
                    editor.Title, editor.Description, editor.ProjectId, editor.Due, editor.Priority, ids, primary, editor.EpisodeId));
            AppLog.Info($"Saved task {saved.Id}");
            await LoadCoreAsync();
            await OpenTaskAsync(saved.Id);
        }
        catch (ApiException ex)
        {
            editor.ErrorMessage = ex.Message;
        }
        catch (ApiUnavailableException ex)
        {
            editor.ErrorMessage = ex.Message;
        }
        finally
        {
            editor.IsSaving = false;
        }
    }
}
