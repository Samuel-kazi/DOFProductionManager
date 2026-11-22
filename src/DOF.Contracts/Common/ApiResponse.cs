namespace DOF.Contracts.Common;

/// <summary>
/// Standard envelope for successful API responses. Errors are returned as RFC 9457 ProblemDetails.
/// </summary>
public sealed record ApiResponse<T>(bool Success, T? Data, string? Message)
{
    public static ApiResponse<T> Ok(T data, string? message = null) => new(true, data, message);
}
