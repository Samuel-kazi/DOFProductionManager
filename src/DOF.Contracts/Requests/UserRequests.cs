namespace DOF.Contracts.Requests;

public sealed record CreateUserRequest(
    string FullName,
    string Email,
    string Password,
    string? TimeZone,
    IReadOnlyList<Guid> RoleIds);

/// <summary><see cref="Version"/> must be the version the client last loaded (optimistic concurrency).</summary>
public sealed record UpdateUserRequest(
    string FullName,
    string Email,
    string TimeZone,
    IReadOnlyList<Guid> RoleIds,
    int Version);

public sealed record UpdateUserStatusRequest(bool IsActive);

public sealed record UpdateRolePermissionsRequest(IReadOnlyList<string> Permissions);
