using DOF.Application.Work;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Domain.Entities;
using FluentValidation;

namespace DOF.Application.Shows;

public sealed class CreateShowRequestValidator : AbstractValidator<CreateShowRequest>
{
    public CreateShowRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Description).MaximumLength(4000);
        RuleFor(r => r.ShowType).OneOf(ShowValues.ShowTypes, "Show type");
        RuleFor(r => r.DefaultDurationMinutes).InclusiveBetween(1, Show.MaxDurationMinutes)
            .When(r => r.DefaultDurationMinutes is not null);
    }
}

public sealed class UpdateShowRequestValidator : AbstractValidator<UpdateShowRequest>
{
    public UpdateShowRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Description).MaximumLength(4000);
        RuleFor(r => r.ShowType).OneOf(ShowValues.ShowTypes, "Show type");
        RuleFor(r => r.DefaultDurationMinutes).InclusiveBetween(1, Show.MaxDurationMinutes)
            .When(r => r.DefaultDurationMinutes is not null);
        RuleFor(r => r.Version).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateSeasonRequestValidator : AbstractValidator<CreateSeasonRequest>
{
    public CreateSeasonRequestValidator()
    {
        RuleFor(r => r.SeasonNumber).InclusiveBetween(1, 999).When(r => r.SeasonNumber is not null);
        RuleFor(r => r.Name).MaximumLength(200);
        RuleFor(r => r.EndDate).GreaterThanOrEqualTo(r => r.StartDate)
            .When(r => r.StartDate is not null && r.EndDate is not null)
            .WithMessage("A season cannot end before it starts.");
    }
}

public sealed class UpdateSeasonRequestValidator : AbstractValidator<UpdateSeasonRequest>
{
    public UpdateSeasonRequestValidator()
    {
        RuleFor(r => r.SeasonNumber).InclusiveBetween(1, 999);
        RuleFor(r => r.Name).MaximumLength(200);
        RuleFor(r => r.Status).OneOf(ShowValues.SeasonStatuses, "Season status");
        RuleFor(r => r.EndDate).GreaterThanOrEqualTo(r => r.StartDate)
            .When(r => r.StartDate is not null && r.EndDate is not null)
            .WithMessage("A season cannot end before it starts.");
        RuleFor(r => r.Version).GreaterThanOrEqualTo(0);
    }
}

internal static class EpisodeRules
{
    public static void Apply<T>(AbstractValidator<T> v, Func<T, string> title, Func<T, string?> description,
        Func<T, int?> number, Func<T, DateTimeOffset?> start, Func<T, DateTimeOffset?> end, Func<T, string?> location)
    {
        v.RuleFor(r => title(r)).NotEmpty().MaximumLength(200).OverridePropertyName("Title");
        v.RuleFor(r => description(r)).MaximumLength(4000).OverridePropertyName("Description");
        v.RuleFor(r => number(r)).InclusiveBetween(1, 9999).When(r => number(r) is not null).OverridePropertyName("EpisodeNumber");
        v.RuleFor(r => location(r)).MaximumLength(200).OverridePropertyName("RecordingLocation");
        v.RuleFor(r => end(r)).Must((r, e) => e > start(r))
            .When(r => start(r) is not null && end(r) is not null)
            .WithMessage("The recording must end after it starts.").OverridePropertyName("RecordingEnd");
        v.RuleFor(r => end(r)).Null().When(r => start(r) is null)
            .WithMessage("A recording end needs a recording start.").OverridePropertyName("RecordingEnd");
    }
}

public sealed class CreateEpisodeRequestValidator : AbstractValidator<CreateEpisodeRequest>
{
    public CreateEpisodeRequestValidator() => EpisodeRules.Apply(this, r => r.Title, r => r.Description, r => r.EpisodeNumber,
        r => r.RecordingStart, r => r.RecordingEnd, r => r.RecordingLocation);
}

public sealed class UpdateEpisodeRequestValidator : AbstractValidator<UpdateEpisodeRequest>
{
    public UpdateEpisodeRequestValidator()
    {
        EpisodeRules.Apply(this, r => r.Title, r => r.Description, r => r.EpisodeNumber,
            r => r.RecordingStart, r => r.RecordingEnd, r => r.RecordingLocation);
        RuleFor(r => r.Version).GreaterThanOrEqualTo(0);
    }
}

public sealed class ChangeEpisodeStatusRequestValidator : AbstractValidator<ChangeEpisodeStatusRequest>
{
    public ChangeEpisodeStatusRequestValidator()
    {
        RuleFor(r => r.Status).OneOf(ShowValues.EpisodeStatuses, "Episode status");
        RuleFor(r => r.Note).MaximumLength(500);
        RuleFor(r => r.Version).GreaterThanOrEqualTo(0).When(r => r.Version is not null);
    }
}

public sealed class AssignEpisodeRequestValidator : AbstractValidator<AssignEpisodeRequest>
{
    public AssignEpisodeRequestValidator()
    {
        RuleFor(r => r.UserId).NotEmpty();
        RuleFor(r => r.Responsibility).OneOf(ShowValues.Responsibilities, "Responsibility");
    }
}
