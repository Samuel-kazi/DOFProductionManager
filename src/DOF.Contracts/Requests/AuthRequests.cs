namespace DOF.Contracts.Requests;

public sealed record LoginRequest(string Email, string Password, string? DeviceName = null);

public sealed record RefreshTokenRequest(string RefreshToken);

public sealed record LogoutRequest(string RefreshToken);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
