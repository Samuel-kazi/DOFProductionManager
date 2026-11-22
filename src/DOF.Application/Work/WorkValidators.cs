using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using FluentValidation;

namespace DOF.Application.Work;

internal static class WorkRules
{
    public static IRuleBuilderOptions<T, string> OneOf<T>(this IRuleBuilder<T, string> rule, IReadOnlyList<string> allowed, string what) =>
        rule.Must(allowed.Contains).WithMessage($"{what} must be one of: {string.Join(", ", allowed)}.");
}

public sealed class CreateProjectRequestValidator : AbstractValidator<CreateProjectRequest>
{
    public CreateProjectRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Description).MaximumLength(4000);
        RuleFor(r => r.ProjectType).OneOf(WorkValues.ProjectTypes, "Project type");
        RuleFor(r => r.Priority).OneOf(CalendarValues.Priorities, "Priority");
        RuleFor(r => r.TargetEndDate).GreaterThanOrEqualTo(r => r.StartDate)
            .When(r => r.StartDate is not null && r.TargetEndDate is not null)
            .WithMessage("The target end date cannot be before the start date.");
    }
}

public sealed class UpdateProjectRequestValidator : AbstractValidator<UpdateProjectRequest>
{
    public UpdateProjectRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Description).MaximumLength(4000);
        RuleFor(r => r.ProjectType).OneOf(WorkValues.ProjectTypes, "Project type");
        RuleFor(r => r.Priority).OneOf(CalendarValues.Priorities, "Priority");
        RuleFor(r => r.TargetEndDate).GreaterThanOrEqualTo(r => r.StartDate)
            .When(r => r.StartDate is not null && r.TargetEndDate is not null)
            .WithMessage("The target end date cannot be before the start date.");
        RuleFor(r => r.Version).GreaterThanOrEqualTo(0);
    }
}

public sealed class ChangeStatusRequestValidator : AbstractValidator<ChangeStatusRequest>
{
    public ChangeStatusRequestValidator()
    {
        RuleFor(r => r.Status).NotEmpty().MaximumLength(40);
        RuleFor(r => r.Version).GreaterThanOrEqualTo(0).When(r => r.Version is not null);
    }
}

public sealed class AddProjectMemberRequestValidator : AbstractValidator<AddProjectMemberRequest>
{
    public AddProjectMemberRequestValidator()
    {
        RuleFor(r => r.UserId).NotEmpty();
        RuleFor(r => r.ProjectRole).MaximumLength(60);
    }
}

public sealed class CreateMilestoneRequestValidator : AbstractValidator<CreateMilestoneRequest>
{
    public CreateMilestoneRequestValidator()
    {
        RuleFor(r => r.Title).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Description).MaximumLength(4000);
        RuleFor(r => r.Priority).OneOf(CalendarValues.Priorities, "Priority");
        RuleFor(r => r.DueDate).NotEmpty();
    }
}

public sealed class UpdateMilestoneRequestValidator : AbstractValidator<UpdateMilestoneRequest>
{
    public UpdateMilestoneRequestValidator()
    {
        RuleFor(r => r.Title).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Description).MaximumLength(4000);
        RuleFor(r => r.Priority).OneOf(CalendarValues.Priorities, "Priority");
        RuleFor(r => r.Status).OneOf(WorkValues.WorkStatuses, "Status");
        RuleFor(r => r.DueDate).NotEmpty();
        RuleFor(r => r.Version).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateTaskRequestValidator : AbstractValidator<CreateTaskRequest>
{
    public CreateTaskRequestValidator()
    {
        RuleFor(r => r.Title).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Description).MaximumLength(4000);
        RuleFor(r => r.Priority).OneOf(CalendarValues.Priorities, "Priority");
        RuleFor(r => r.AssigneeIds).Must(ids => ids is null || ids.Count <= 20).WithMessage("A task can have at most 20 assignees.");
    }
}

public sealed class UpdateTaskRequestValidator : AbstractValidator<UpdateTaskRequest>
{
    public UpdateTaskRequestValidator()
    {
        RuleFor(r => r.Title).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Description).MaximumLength(4000);
        RuleFor(r => r.Priority).OneOf(CalendarValues.Priorities, "Priority");
        RuleFor(r => r.AssigneeIds).NotEmpty().WithMessage("A task needs at least one responsible person.")
            .Must(ids => ids.Count <= 20).WithMessage("A task can have at most 20 assignees.");
        RuleFor(r => r.Version).GreaterThanOrEqualTo(0);
    }
}

public sealed class AssignTaskRequestValidator : AbstractValidator<AssignTaskRequest>
{
    public AssignTaskRequestValidator() => RuleFor(r => r.UserId).NotEmpty();
}
