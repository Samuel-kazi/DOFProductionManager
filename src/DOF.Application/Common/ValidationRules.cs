using FluentValidation;

namespace DOF.Application.Common;

public static class ValidationRules
{
    public const int MinPasswordLength = 10;
    public const int MaxPasswordLength = 128;

    public static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .MinimumLength(MinPasswordLength)
            .MaximumLength(MaxPasswordLength);

    public static IRuleBuilderOptions<T, string?> ValidTimeZone<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(tz => tz is null || TimeZoneInfo.TryFindSystemTimeZoneById(tz, out _))
            .WithMessage("'{PropertyValue}' is not a recognised IANA timezone.");
}
