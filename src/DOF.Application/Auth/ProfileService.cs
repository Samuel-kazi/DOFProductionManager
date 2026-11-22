using DOF.Application.Activity;
using DOF.Application.Common.Abstractions;
using DOF.Application.Common.Exceptions;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Domain.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace DOF.Application.Auth;

/// <summary>Self-service profile changes (spec 6.7): name and timezone. E-mail and roles stay with administrators.</summary>
public sealed class ProfileService(IApplicationDbContext db, UserProfileQueries profiles, ActivityRecorder activity)
{
    public async Task<CurrentUserResponse> UpdateAsync(Guid userId, UpdateMyProfileRequest request, CancellationToken ct)
    {
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(request.TimeZone, out _))
        {
            throw new DomainException($"'{request.TimeZone}' is not a recognised IANA timezone.");
        }
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct) ?? throw new NotFoundException("User", userId);
        var before = new Dictionary<string, object?> { ["FullName"] = user.FullName, ["TimeZone"] = user.TimeZone };
        user.UpdateProfile(request.FullName, user.Email, request.TimeZone);
        activity.RecordChanges(ActivityEntities.User, userId, ActivityActions.Updated, before,
            new Dictionary<string, object?> { ["FullName"] = user.FullName, ["TimeZone"] = user.TimeZone });
        await db.SaveChangesAsync(ct);
        return await profiles.GetCurrentUserAsync(userId, ct);
    }
}

public sealed class UpdateMyProfileRequestValidator : AbstractValidator<UpdateMyProfileRequest>
{
    public UpdateMyProfileRequestValidator()
    {
        RuleFor(r => r.FullName).NotEmpty().MaximumLength(200);
        RuleFor(r => r.TimeZone).NotEmpty().MaximumLength(100);
    }
}
