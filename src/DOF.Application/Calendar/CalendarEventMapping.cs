using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Domain.Enums;
using DOF.Domain.Exceptions;

namespace DOF.Application.Calendar;

internal static class CalendarEventMapping
{
    public static TEnum Parse<TEnum>(string value, string field) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new DomainException($"'{value}' is not a valid {field}.");

    public static IEnumerable<(Guid, CalendarAssignmentType)> Assignments(IReadOnlyList<CalendarAssignmentRequest>? requests) =>
        (requests ?? []).Select(a => (a.UserId, Parse<CalendarAssignmentType>(a.AssignmentType, "assignment type")));

    public static bool IsKnown(IReadOnlyList<string> allowed, string? value) => value is not null && allowed.Contains(value);

    public static string SourceType => nameof(CalendarSourceType.CalendarEvent);

    public static bool Includes(IReadOnlyCollection<string>? filter, string value) =>
        filter is null || filter.Count == 0 || filter.Contains(value);

    public static readonly string CancelledStatus = CalendarValues.Cancelled;
}
