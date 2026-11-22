namespace DOF.Application.Common.Abstractions;

public interface ICurrentUser
{
    Guid? UserId { get; }

    Guid RequiredUserId => UserId ?? throw new InvalidOperationException("No authenticated user.");

    /// <summary>Whether the signed-in user currently holds the permission (checked against the database per request).</summary>
    bool HasPermission(string permission);
}
