using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DOF.Contracts.Common;
using DOF.Contracts.Responses;
using DOF.Desktop.Services;

namespace DOF.Desktop.ViewModels.Work;

/// <summary>One project: header, milestones, tasks, members and full history.</summary>
public sealed partial class ProjectDetailViewModel : ObservableObject
{
    public ProjectDetailViewModel(ProjectResponse project, ProjectTimelineResponse timeline, UserClock clock, DateOnly today,
        IEnumerable<ChoiceOption> people)
    {
        Project = project;
        Milestones = [.. timeline.Milestones.Select(m => new MilestoneItemViewModel(m, today))];
        Tasks = [.. timeline.Tasks.Select(t => new TaskItemViewModel(t, clock))];
        History = [.. timeline.History.Select(h => HistoryLine.From(h, clock))];
        Members = [.. project.Members];
        var memberIds = project.Members.Select(m => m.UserId).ToHashSet();
        NonMembers = [.. people.Where(p => p.Id is { } id && !memberIds.Contains(id))];
        StatusOptions = project.CanArchive ? WorkValues.ProjectStatuses : [.. WorkValues.ProjectStatuses.Where(s => s != "Archived")];
        SelectedStatus = project.Status;
        NewMemberRole = "Member";
    }

    public ProjectResponse Project { get; }
    public string Name => Project.Name;
    public string? Description => Project.Description;
    public string TypeText => WorkLabels.ProjectType(Project.ProjectType);
    public string PriorityText => $"{Project.Priority} priority";
    public string ManagerText => Project.Manager?.FullName ?? "No manager";
    public string DatesText => (Project.StartDate, Project.TargetEndDate) switch
    {
        ({ } s, { } e) => $"{s:d MMM yyyy} – {e:d MMM yyyy}",
        ({ } s, null) => $"From {s:d MMM yyyy}",
        (null, { } e) => $"Target {e:d MMM yyyy}",
        _ => "No dates set",
    };
    public bool CanEdit => Project.CanEdit;
    public bool IsArchived => Project.Status == WorkValues.Archived;
    public bool CanEditStatus => Project.CanEdit && (!IsArchived || Project.CanArchive);
    public bool CanAddWork => Project.CanEdit && !IsArchived;
    public IReadOnlyList<string> StatusOptions { get; }

    public ObservableCollection<MilestoneItemViewModel> Milestones { get; }
    public ObservableCollection<TaskItemViewModel> Tasks { get; }
    public ObservableCollection<ProjectMemberResponse> Members { get; }
    public IReadOnlyList<HistoryLine> History { get; }
    public IReadOnlyList<ChoiceOption> NonMembers { get; }

    public bool HasNoMilestones => Milestones.Count == 0;
    public bool HasNoTasks => Tasks.Count == 0;

    [ObservableProperty] public partial string SelectedStatus { get; set; }
    [ObservableProperty] public partial ChoiceOption? NewMember { get; set; }
    [ObservableProperty] public partial string NewMemberRole { get; set; }

    public Func<string, Task>? StatusChanged { get; set; }

    private bool _saving;

    /// <summary>Only real choices are saved (see TaskDetailsViewModel for why empty values are ignored).</summary>
    partial void OnSelectedStatusChanged(string value)
    {
        if (_saving || string.IsNullOrEmpty(value) || value == Project.Status || !StatusOptions.Contains(value)
            || StatusChanged is not { } save)
        {
            return;
        }

        _saving = true;
        _ = save(value);
    }
}
