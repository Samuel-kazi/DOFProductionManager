namespace DOF.Desktop.ViewModels;

/// <summary>
/// Opens the original record behind a calendar or dashboard entry. Implemented by the shell so any page
/// can navigate without knowing about the others.
/// </summary>
public interface IRecordNavigator
{
    Task OpenRecordAsync(string sourceType, Guid sourceId);

    Task CreateEventAsync(DateOnly? date = null);
}
