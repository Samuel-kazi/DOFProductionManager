using DOF.Domain.Exceptions;

namespace DOF.Application.Work;

internal static class WorkParsing
{
    public static TEnum Parse<TEnum>(string value, string field) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new DomainException($"'{value}' is not a valid {field}.");

    public static List<TEnum>? ParseAll<TEnum>(IReadOnlyCollection<string>? values) where TEnum : struct, Enum =>
        values is null || values.Count == 0
            ? null
            : values.Select(v => Enum.TryParse<TEnum>(v, out var p) ? p : (TEnum?)null).OfType<TEnum>().ToList();
}
