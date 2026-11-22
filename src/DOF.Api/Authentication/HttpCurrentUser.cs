using DOF.Application.Common.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;

namespace DOF.Api.Authentication;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? UserId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : null;
}
