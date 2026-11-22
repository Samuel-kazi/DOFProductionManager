using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using FluentValidation;

namespace DOF.Application.Calendar;

internal static class CalendarRules
{
    public static void Apply<T>(AbstractValidator<T> v,
        Func<T, string> title, Func<T, string?> description, Func<T, string> eventType, Func<T, DateTimeOffset> start,
        Func<T, DateTimeOffset> end, Func<T, bool> isAllDay, Func<T, string?> location, Func<T, string?> meetingUrl,
        Func<T, string> priority, Func<T, IReadOnlyList<CalendarAssignmentRequest>?> assignments)
    {
        v.RuleFor(r => title(r)).NotEmpty().MaximumLength(200).OverridePropertyName("Title");
        v.RuleFor(r => description(r)).MaximumLength(4000).OverridePropertyName("Description");
        v.RuleFor(r => eventType(r)).Must(t => CalendarValues.EventTypes.Contains(t))
            .WithMessage($"Event type must be one of: {string.Join(", ", CalendarValues.EventTypes)}.").OverridePropertyName("EventType");
        v.RuleFor(r => priority(r)).Must(p => CalendarValues.Priorities.Contains(p))
            .WithMessage($"Priority must be one of: {string.Join(", ", CalendarValues.Priorities)}.").OverridePropertyName("Priority");
        v.RuleFor(r => end(r)).Must((r, e) => isAllDay(r) ? e >= start(r) : e > start(r))
            .WithMessage("The event must end after it starts.").OverridePropertyName("EndAt");
        v.RuleFor(r => end(r)).Must((r, e) => e - start(r) <= TimeSpan.FromDays(366))
            .WithMessage("An event cannot last longer than a year.").OverridePropertyName("EndAt");
        v.RuleFor(r => location(r)).MaximumLength(500).OverridePropertyName("Location");
        v.RuleFor(r => meetingUrl(r))
            .Must(u => u is null || (Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http"))
            .WithMessage("Meeting link must be a full web address (https://…).").OverridePropertyName("MeetingUrl");
        v.RuleForEach(r => assignments(r) ?? Array.Empty<CalendarAssignmentRequest>())
            .Must(a => CalendarValues.AssignmentTypes.Contains(a.AssignmentType))
            .WithMessage($"Assignment type must be one of: {string.Join(", ", CalendarValues.AssignmentTypes)}.")
            .OverridePropertyName("Assignments");
    }
}

public sealed class CreateCalendarEventRequestValidator : AbstractValidator<CreateCalendarEventRequest>
{
    public CreateCalendarEventRequestValidator() => CalendarRules.Apply(this,
        r => r.Title, r => r.Description, r => r.EventType, r => r.StartAt, r => r.EndAt, r => r.IsAllDay,
        r => r.Location, r => r.MeetingUrl, r => r.Priority, r => r.Assignments);
}

public sealed class UpdateCalendarEventRequestValidator : AbstractValidator<UpdateCalendarEventRequest>
{
    public UpdateCalendarEventRequestValidator()
    {
        CalendarRules.Apply(this,
            r => r.Title, r => r.Description, r => r.EventType, r => r.StartAt, r => r.EndAt, r => r.IsAllDay,
            r => r.Location, r => r.MeetingUrl, r => r.Priority, r => r.Assignments);
        RuleFor(r => r.Status).Must(s => CalendarValues.Statuses.Contains(s))
            .WithMessage($"Status must be one of: {string.Join(", ", CalendarValues.Statuses)}.");
        RuleFor(r => r.Version).GreaterThanOrEqualTo(0);
    }
}
