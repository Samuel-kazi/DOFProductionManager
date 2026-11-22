namespace DOF.Application.Common.Abstractions;

public interface ICurrentUser
{
    Guid? UserId { get; }

    Guid RequiredUserId => UserId ?? throw new InvalidOperationException("No authenticated user.");
}
