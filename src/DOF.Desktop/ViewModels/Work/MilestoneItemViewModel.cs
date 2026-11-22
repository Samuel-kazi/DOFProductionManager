using DOF.Contracts.Responses;

namespace DOF.Desktop.ViewModels.Work;

public sealed class MilestoneItemViewModel(MilestoneResponse milestone, DateOnly today)
{
    public MilestoneResponse Milestone { get; } = milestone;
    public Guid Id => Milestone.Id;
    public string Title => Milestone.Title;
    public string ProjectName => Milestone.Project.Name;
    public string DueText => WorkLabels.DueDate(Milestone.DueDate, today);
    public bool IsOverdue => Milestone.IsOverdue;
    public bool IsCompleted => Milestone.Status == "Completed";
    public string StatusText => WorkLabels.Status(Milestone.Status);
    public string StatusBrushKey => WorkLabels.StatusBrushKey(Milestone.Status);
    public string AssigneeText => Milestone.AssignedTo?.FullName ?? "No one assigned";
    public bool CanEdit => Milestone.CanEdit;
}
