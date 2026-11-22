namespace DOF.Desktop.ViewModels;

/// <summary>
/// Opens the original record behind a calendar or dashboard entry, and starts creation flows. Implemented
/// by the shell so any page can navigate without knowing about the others.
/// </summary>
public interface IRecordNavigator
{
    /// <summary>Source types: CalendarEvent, Milestone, Task, Project.</summary>
    Task OpenRecordAsync(string sourceType, Guid sourceId);

    Task CreateEventAsync(DateOnly? date = null);

    Task CreateTaskAsync(Guid? projectId = null);

    /// <summary>The Tasks page showing everything assigned to one person.</summary>
    Task ShowTasksForAsync(Guid userId, string fullName);
}
