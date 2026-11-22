using DOF.Desktop.Services.Api;

namespace DOF.Desktop.ViewModels.Pages;

public sealed class DashboardViewModel(AuthSession session, TimeProvider clock) : PageViewModel
{
    public override string Title => "Dashboard";

    public string Greeting
    {
        get
        {
            var hour = LocalNow.Hour;
            var part = hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening";
            var firstName = session.CurrentUser?.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return firstName is null ? part : $"{part}, {firstName}";
        }
    }

    public string TodayText => LocalNow.ToString("dddd d MMMM yyyy");

    public string TimeZoneText => $"Times shown in {session.CurrentUser?.TimeZone ?? "local time"}";

    private DateTimeOffset LocalNow
    {
        get
        {
            var now = clock.GetUtcNow();
            var zoneId = session.CurrentUser?.TimeZone;
            return zoneId is not null && TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out var zone)
                ? TimeZoneInfo.ConvertTime(now, zone)
                : now.ToLocalTime();
        }
    }
}
