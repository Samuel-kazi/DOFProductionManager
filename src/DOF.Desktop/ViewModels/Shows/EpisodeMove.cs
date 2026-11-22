using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Desktop.Services;

namespace DOF.Desktop.ViewModels.Shows;

/// <summary>Builds the episode update for a calendar drag: the recording keeps its length; a deadline takes the new day.</summary>
public static class EpisodeMove
{
    public static UpdateEpisodeRequest Apply(EpisodeResponse e, string eventType, DateTime newLocalStart, UserClock clock)
    {
        var request = ShowEditing.Update(e);
        var day = DateOnly.FromDateTime(newLocalStart);
        return eventType switch
        {
            CalendarValues.RecordingItem when e.RecordingStart is { } start && e.RecordingEnd is { } end =>
                request with { RecordingStart = clock.FromLocal(newLocalStart), RecordingEnd = clock.FromLocal(newLocalStart) + (end - start) },
            CalendarValues.EditingDeadlineItem => request with { EditingDueDate = day },
            CalendarValues.ReviewDeadlineItem => request with { ReviewDueDate = day },
            CalendarValues.ApprovalDeadlineItem => request with { ApprovalDueDate = day },
            _ => request,
        };
    }
}

public static class ShowEditing
{
    /// <summary>An update request that leaves the episode as it is (callers change what they need).</summary>
    public static UpdateEpisodeRequest Update(EpisodeResponse e) => new(e.Title, e.Description, e.Season?.Id, e.EpisodeNumber,
        e.Producer?.Id, e.RecordingStart, e.RecordingEnd, e.RecordingLocation, e.EditingDueDate, e.ReviewDueDate,
        e.ApprovalDueDate, e.Version);
}
