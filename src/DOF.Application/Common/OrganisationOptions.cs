namespace DOF.Application.Common;

/// <summary>
/// Organisation-wide settings, bound from the <c>Organisation</c> configuration section.
/// </summary>
public sealed class OrganisationOptions
{
    public const string SectionName = "Organisation";

    /// <summary>IANA timezone used to display dates when a user has no timezone of their own.</summary>
    public string DefaultTimeZone { get; set; } = "Africa/Nairobi";

    public bool HasValidTimeZone() =>
        !string.IsNullOrWhiteSpace(DefaultTimeZone)
        && TimeZoneInfo.TryFindSystemTimeZoneById(DefaultTimeZone, out _);
}
