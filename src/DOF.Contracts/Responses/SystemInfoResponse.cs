namespace DOF.Contracts.Responses;

public sealed record SystemInfoResponse(
    string Application,
    string Version,
    string Environment,
    DateTimeOffset ServerTimeUtc,
    string DefaultTimeZone);
