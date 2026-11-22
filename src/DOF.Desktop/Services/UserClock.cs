using DOF.Desktop.Services.Api;

namespace DOF.Desktop.Services;

/// <summary>
/// Converts between server UTC instants and the signed-in user's local wall-clock time. All dates the
/// user sees or types are in their own timezone (from their profile), not the computer's.
/// </summary>
public sealed class UserClock(AuthSession session, TimeProvider clock)
{
    public TimeZoneInfo Zone =>
        session.CurrentUser?.TimeZone is { } id && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone)
            ? zone
            : TimeZoneInfo.Local;

    public DateTime Now => ToLocal(clock.GetUtcNow());

    public DateOnly Today => DateOnly.FromDateTime(Now);

    public DateTime ToLocal(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Zone).DateTime;

    /// <summary>Interprets a wall-clock time in the user's timezone.</summary>
    public DateTimeOffset FromLocal(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (Zone.IsInvalidTime(unspecified))
        {
            unspecified = unspecified.AddMinutes(30); // skip a daylight-saving gap
        }
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(unspecified, Zone), TimeSpan.Zero);
    }

    public DateTimeOffset StartOfDay(DateOnly date) => FromLocal(date.ToDateTime(TimeOnly.MinValue));
}
