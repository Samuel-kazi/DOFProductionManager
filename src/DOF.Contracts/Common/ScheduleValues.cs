namespace DOF.Contracts.Common;

/// <summary>Periods a schedule PDF can cover (<c>GET /calendar/export?period=…</c>).</summary>
public static class ScheduleValues
{
    public const string Day = "Day";
    public const string Week = "Week";
    public const string Month = "Month";
    public const string Year = "Year";

    public static readonly IReadOnlyList<string> Periods = [Day, Week, Month, Year];
}
