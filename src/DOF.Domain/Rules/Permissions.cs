namespace DOF.Domain.Rules;

/// <summary>
/// Catalogue of fine-grained permissions. Roles are granted sets of these; the API checks them on
/// every protected operation. New permissions are added here and synchronised into the database at startup.
/// </summary>
public static class Permissions
{
    public const string ProjectsCreate = "projects.create";
    public const string ProjectsEdit = "projects.edit";
    public const string ProjectsArchive = "projects.archive";
    public const string EpisodesManage = "episodes.manage";
    public const string ReleasesSchedule = "releases.schedule";
    public const string TasksAssign = "tasks.assign";
    public const string CalendarManage = "calendar.manage";
    public const string ReportsView = "reports.view";
    public const string UsersManage = "users.manage";
    public const string SettingsManage = "settings.manage";

    public static IReadOnlyDictionary<string, string> All { get; } = new Dictionary<string, string>
    {
        [ProjectsCreate] = "Create projects",
        [ProjectsEdit] = "Edit projects, milestones and members",
        [ProjectsArchive] = "Archive projects",
        [EpisodesManage] = "Manage shows, seasons and episodes",
        [ReleasesSchedule] = "Schedule, postpone, cancel and confirm releases",
        [TasksAssign] = "Assign tasks to other people",
        [CalendarManage] = "Create and edit standalone calendar events",
        [ReportsView] = "View management reports",
        [UsersManage] = "Manage user accounts, roles and permissions",
        [SettingsManage] = "Manage system configuration",
    };
}
