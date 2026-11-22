namespace DOF.Contracts.Requests;

/// <summary>Self-service profile changes (spec 6.7): display name and timezone.</summary>
public sealed record UpdateMyProfileRequest(string FullName, string TimeZone);
