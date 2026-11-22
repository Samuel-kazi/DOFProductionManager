using DOF.Application.Work;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Domain.Entities;
using FluentValidation;

namespace DOF.Application.Reminders;

public sealed class CreateReminderRequestValidator : AbstractValidator<CreateReminderRequest>
{
    public CreateReminderRequestValidator()
    {
        RuleFor(r => r.SourceType).OneOf(ReminderValues.SourceTypes, "Source type");
        RuleFor(r => r.SourceId).NotEmpty();
        RuleFor(r => r.Channel).OneOf(ReminderValues.Channels, "Channel");
        RuleFor(r => r).Must(r => (r.OffsetMinutes is null) != (r.At is null))
            .WithName("When").WithMessage("Give either minutes before the record's time or a fixed time, not both.");
        RuleFor(r => r.OffsetMinutes).InclusiveBetween(0, Reminder.MaxOffsetMinutes).When(r => r.OffsetMinutes is not null);
    }
}

public sealed class UpdateReminderRequestValidator : AbstractValidator<UpdateReminderRequest>
{
    public UpdateReminderRequestValidator()
    {
        RuleFor(r => r.Channel).OneOf(ReminderValues.Channels, "Channel");
        RuleFor(r => r).Must(r => (r.OffsetMinutes is null) != (r.At is null))
            .WithName("When").WithMessage("Give either minutes before the record's time or a fixed time, not both.");
        RuleFor(r => r.OffsetMinutes).InclusiveBetween(0, Reminder.MaxOffsetMinutes).When(r => r.OffsetMinutes is not null);
        RuleFor(r => r.Version).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateNotificationPreferencesRequestValidator : AbstractValidator<UpdateNotificationPreferencesRequest>
{
    public UpdateNotificationPreferencesRequestValidator()
    {
        RuleFor(r => r.ReminderOffsets).NotNull().Must(o => o.Count <= 6).WithMessage("Choose up to six default reminders.");
        RuleForEach(r => r.ReminderOffsets).InclusiveBetween(0, Reminder.MaxOffsetMinutes);
    }
}
