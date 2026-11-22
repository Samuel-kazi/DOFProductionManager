using CommunityToolkit.Mvvm.ComponentModel;
using DOF.Contracts.Responses;
using DOF.Desktop.Services;

namespace DOF.Desktop.ViewModels.Work;

/// <summary>A task as shown in lists, boards and dashboards.</summary>
public sealed partial class TaskItemViewModel(TaskResponse task, UserClock clock) : ObservableObject
{
    public TaskResponse Task { get; } = task;
    public Guid Id => Task.Id;
    public string Title => Task.Title;
    public string? ProjectName => Task.Project?.Name;
    public string DueText => WorkLabels.Due(Task.DueAt, clock);
    public bool IsOverdue => Task.IsOverdue;
    public bool IsCompleted => Task.Status == "Completed";
    public bool IsCancelled => Task.Status == "Cancelled";
    public string Status => Task.Status;
    public string StatusText => WorkLabels.Status(Task.Status);
    public string StatusBrushKey => WorkLabels.StatusBrushKey(Task.Status);
    public string Priority => Task.Priority;
    public bool IsHighPriority => Task.Priority is "High" or "Urgent";
    public bool CanEdit => Task.CanEdit;
    public string PrimaryName => Task.Assignees.FirstOrDefault(a => a.IsPrimary)?.FullName ?? "Unassigned";
    public string AssigneesText => string.Join(", ", Task.Assignees.Select(a => a.FullName));
    public IReadOnlyList<string> Initials { get; } = [.. task.Assignees.Take(3).Select(a => Pages.TeamViewModel.Initials(a.FullName))];

    /// <summary>Bound to the completion checkbox; the page applies the change through the API.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }
}
