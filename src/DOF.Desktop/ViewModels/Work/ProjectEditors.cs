using CommunityToolkit.Mvvm.ComponentModel;
using DOF.Contracts.Common;
using DOF.Contracts.Responses;

namespace DOF.Desktop.ViewModels.Work;

public sealed partial class ProjectEditorViewModel : ObservableObject
{
    public ProjectEditorViewModel(IEnumerable<ChoiceOption> people, ProjectResponse? existing)
    {
        Existing = existing;
        Managers = [new ChoiceOption(null, "No manager yet"), .. people];
        Name = existing?.Name ?? string.Empty;
        Description = existing?.Description;
        ProjectType = existing?.ProjectType ?? "ShowProduction";
        Priority = existing?.Priority ?? "Normal";
        SelectedManager = Managers.FirstOrDefault(m => m.Id == existing?.Manager?.Id) ?? Managers[0];
        StartDate = existing?.StartDate?.ToDateTime(TimeOnly.MinValue);
        TargetEndDate = existing?.TargetEndDate?.ToDateTime(TimeOnly.MinValue);
    }

    public ProjectResponse? Existing { get; }
    public bool IsNew => Existing is null;
    public string Heading => IsNew ? "New project" : "Edit project";
    public IReadOnlyList<string> ProjectTypes => WorkValues.ProjectTypes;
    public IReadOnlyList<string> Priorities => CalendarValues.Priorities;
    public IReadOnlyList<ChoiceOption> Managers { get; }

    [ObservableProperty] public partial string Name { get; set; }
    [ObservableProperty] public partial string? Description { get; set; }
    [ObservableProperty] public partial string ProjectType { get; set; }
    [ObservableProperty] public partial string Priority { get; set; }
    [ObservableProperty] public partial ChoiceOption SelectedManager { get; set; }
    [ObservableProperty] public partial DateTime? StartDate { get; set; }
    [ObservableProperty] public partial DateTime? TargetEndDate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => ErrorMessage is not null;

    public static DateOnly? ToDate(DateTime? value) => value is { } v ? DateOnly.FromDateTime(v) : null;
}

public sealed partial class MilestoneEditorViewModel : ObservableObject
{
    public MilestoneEditorViewModel(Guid projectId, IEnumerable<ChoiceOption> people, MilestoneResponse? existing, DateOnly defaultDue)
    {
        ProjectId = projectId;
        Existing = existing;
        People = [new ChoiceOption(null, "No one yet"), .. people];
        Title = existing?.Title ?? string.Empty;
        Description = existing?.Description;
        Priority = existing?.Priority ?? "Normal";
        Status = existing?.Status ?? "NotStarted";
        DueDate = (existing?.DueDate ?? defaultDue).ToDateTime(TimeOnly.MinValue);
        SelectedPerson = People.FirstOrDefault(p => p.Id == existing?.AssignedTo?.Id) ?? People[0];
    }

    public Guid ProjectId { get; }
    public MilestoneResponse? Existing { get; }
    public bool IsNew => Existing is null;
    public bool IsReadOnly => Existing is { CanEdit: false };
    public bool CanSave => !IsReadOnly;
    public string Heading => IsNew ? "New milestone" : IsReadOnly ? "Milestone" : "Edit milestone";
    public IReadOnlyList<string> Priorities => CalendarValues.Priorities;
    public IReadOnlyList<string> Statuses => WorkValues.WorkStatuses;
    public IReadOnlyList<ChoiceOption> People { get; }

    [ObservableProperty] public partial string Title { get; set; }
    [ObservableProperty] public partial string? Description { get; set; }
    [ObservableProperty] public partial string Priority { get; set; }
    [ObservableProperty] public partial string Status { get; set; }
    [ObservableProperty] public partial DateTime? DueDate { get; set; }
    [ObservableProperty] public partial ChoiceOption SelectedPerson { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => ErrorMessage is not null;
}
