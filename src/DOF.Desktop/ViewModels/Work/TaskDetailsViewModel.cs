using CommunityToolkit.Mvvm.ComponentModel;
using DOF.Contracts.Common;
using DOF.Contracts.Responses;
using DOF.Desktop.Services;

namespace DOF.Desktop.ViewModels.Work;

/// <summary>The task record with its history.</summary>
public sealed partial class TaskDetailsViewModel(TaskResponse task, IReadOnlyList<ActivityEntryResponse> history, UserClock clock)
    : ObservableObject
{
    public TaskResponse Task { get; } = task;
    public string Title => Task.Title;
    public string? Description => Task.Description;
    public string? ProjectName => Task.Project?.Name;
    public bool HasProject => Task.Project is not null;
    public string? EpisodeName => WorkLabels.EpisodeName(Task.Episode);
    public bool HasEpisode => Task.Episode is not null;
    public Reminders.RemindMeViewModel RemindMe => _remindMe ??= new("Task", Task.Id, Task.DueAt is not null && Task.Status is not ("Completed" or "Cancelled"));
    private Reminders.RemindMeViewModel? _remindMe;
    public string DueText => WorkLabels.Due(Task.DueAt, clock);
    public bool IsOverdue => Task.IsOverdue;
    public string PriorityText => $"{Task.Priority} priority";
    public bool CanEdit => Task.CanEdit;
    public IReadOnlyList<string> StatusOptions => WorkValues.WorkStatuses;

    public IReadOnlyList<string> People =>
        [.. Task.Assignees.Select(a => a.IsPrimary ? $"{a.FullName} (responsible)" : a.FullName)];

    public string AuditText
    {
        get
        {
            var created = $"Created by {Task.CreatedBy?.FullName ?? "unknown"} · {clock.ToLocal(Task.CreatedAt):d MMM yyyy HH:mm}";
            return Task.CompletedAt is { } done
                ? $"{created}\nCompleted by {Task.CompletedBy?.FullName ?? "unknown"} · {clock.ToLocal(done):d MMM yyyy HH:mm}"
                : created;
        }
    }

    public IReadOnlyList<HistoryLine> History { get; } = [.. history.Select(h => HistoryLine.From(h, clock))];

    /// <summary>Bound to the status picker; changing it asks the page to save.</summary>
    [ObservableProperty]
    public partial string SelectedStatus { get; set; } = task.Status;

    public Func<string, Task>? StatusChanged { get; set; }

    private bool _saving;

    /// <summary>
    /// Only real choices are saved. A picker that is being torn down can write an empty value back; ignoring
    /// it (and repeat changes while one is in flight) prevents a reload loop.
    /// </summary>
    partial void OnSelectedStatusChanged(string value)
    {
        if (_saving || string.IsNullOrEmpty(value) || value == Task.Status || !StatusOptions.Contains(value)
            || StatusChanged is not { } save)
        {
            return;
        }

        _saving = true;
        _ = save(value);
    }
}
