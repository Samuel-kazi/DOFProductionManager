namespace DOF.Domain.Rules;

/// <summary>
/// The four seeded roles and the permissions each receives by default. Defaults are applied only when
/// a role or permission is first created, so later changes made by administrators are preserved.
/// </summary>
public static class SystemRoles
{
    public const string Administrator = "Administrator";
    public const string ProductionManager = "Production Manager";
    public const string Producer = "Producer";
    public const string TeamMember = "Team Member";

    public static IReadOnlyList<SystemRoleDefinition> All { get; } =
    [
        new(Administrator, "Full access, including accounts, permissions and system settings.",
            [.. Permissions.All.Keys]),
        new(ProductionManager, "Oversees all production work, schedules and reports.",
            [Permissions.ProjectsCreate, Permissions.ProjectsEdit, Permissions.ProjectsArchive,
             Permissions.EpisodesManage, Permissions.ReleasesSchedule, Permissions.TasksAssign,
             Permissions.CalendarManage, Permissions.ReportsView]),
        new(Producer, "Produces shows and episodes and coordinates their teams.",
            [Permissions.ProjectsEdit, Permissions.EpisodesManage, Permissions.ReleasesSchedule,
             Permissions.TasksAssign, Permissions.CalendarManage]),
        new(TeamMember, "Works on assigned tasks, episodes and events.", []),
    ];
}

public sealed record SystemRoleDefinition(string Name, string Description, IReadOnlyList<string> DefaultPermissions);
