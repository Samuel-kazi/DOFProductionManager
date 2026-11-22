using DOF.Application.Common;
using DOF.Contracts.Requests;
using FluentValidation;

namespace DOF.Application.Auth;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().MaximumLength(255);
        RuleFor(r => r.Password).NotEmpty().MaximumLength(ValidationRules.MaxPasswordLength);
        RuleFor(r => r.DeviceName).MaximumLength(100);
    }
}

public sealed class RefreshTokenRequestValidator : AbstractValidator<RefreshTokenRequest>
{
    public RefreshTokenRequestValidator() => RuleFor(r => r.RefreshToken).NotEmpty().MaximumLength(200);
}

public sealed class LogoutRequestValidator : AbstractValidator<LogoutRequest>
{
    public LogoutRequestValidator() => RuleFor(r => r.RefreshToken).NotEmpty().MaximumLength(200);
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(r => r.CurrentPassword).NotEmpty();
        RuleFor(r => r.NewPassword).StrongPassword()
            .NotEqual(r => r.CurrentPassword).WithMessage("The new password must differ from the current password.");
    }
}
