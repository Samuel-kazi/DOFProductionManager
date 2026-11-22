using DOF.Application.Common;
using DOF.Contracts.Requests;
using FluentValidation;

namespace DOF.Application.Users;

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(r => r.FullName).NotEmpty().MaximumLength(150);
        RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(255);
        RuleFor(r => r.Password).StrongPassword();
        RuleFor(r => r.TimeZone).ValidTimeZone();
        RuleFor(r => r.RoleIds).NotEmpty().WithMessage("At least one role is required.");
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(r => r.FullName).NotEmpty().MaximumLength(150);
        RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(255);
        RuleFor(r => r.TimeZone).NotEmpty().ValidTimeZone();
        RuleFor(r => r.RoleIds).NotEmpty().WithMessage("At least one role is required.");
        RuleFor(r => r.Version).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateRolePermissionsRequestValidator : AbstractValidator<UpdateRolePermissionsRequest>
{
    public UpdateRolePermissionsRequestValidator() => RuleFor(r => r.Permissions).NotNull();
}
