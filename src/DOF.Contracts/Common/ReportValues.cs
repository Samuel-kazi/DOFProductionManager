namespace DOF.Contracts.Common;

/// <summary>The reports (spec 5.10), keyed by their URL segment under <c>/api/v1/reports/</c>.</summary>
public static class ReportValues
{
    public static readonly IReadOnlyList<(string Key, string Title)> Reports =
    [
        ("weekly", "Weekly report"),
        ("monthly", "Monthly report"),
        ("upcoming-releases", "Upcoming releases"),
        ("unpublished-episodes", "Recorded but not yet published"),
        ("overdue-tasks", "Overdue tasks"),
        ("project-status", "Project status"),
        ("team-workload", "Team workload"),
        ("publication-history", "Publication history"),
    ];

    public const string DateTimeFormat = "yyyy-MM-dd HH:mm";
    public const string DateFormat = "yyyy-MM-dd";
}
