namespace DOF.Application.Common.Exceptions;

/// <summary>The requested record does not exist (HTTP 404).</summary>
public sealed class NotFoundException(string entity, object key)
    : Exception($"{entity} '{key}' was not found.");

/// <summary>The request conflicts with the current state, e.g. a duplicate or stale version (HTTP 409).</summary>
public sealed class ConflictException(string message) : Exception(message);

/// <summary>Credentials or tokens were rejected (HTTP 401). The message is deliberately generic.</summary>
public sealed class AuthenticationFailedException(string message = "Invalid email or password.")
    : Exception(message);

/// <summary>The signed-in user may not perform this operation on this record (HTTP 403).</summary>
public sealed class ForbiddenException(string message = "You don't have permission to do that.") : Exception(message);
