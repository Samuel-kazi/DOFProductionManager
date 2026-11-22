namespace DOF.Contracts.Responses;

public sealed record UserResponse(
    Guid Id,
    string FullName,
    string Email,
    bool IsActive,
    string TimeZone,
    IReadOnlyList<RoleSummaryResponse> Roles,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Version);

public sealed record RoleSummaryResponse(Guid Id, string Name);

public sealed record RoleResponse(Guid Id, string Name, string Description, IReadOnlyList<string> Permissions);

public sealed record PermissionResponse(string Name, string Description);
