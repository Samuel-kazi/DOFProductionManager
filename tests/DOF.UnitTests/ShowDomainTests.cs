using DOF.Contracts.Common;
using DOF.Domain.Entities;
using DOF.Domain.Enums;
using DOF.Domain.Exceptions;

namespace DOF.UnitTests;

public class ShowDomainTests
{
    private static readonly DateTimeOffset Now = new(2034, 3, 6, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2034, 3, 6);

    private static (Show Show, Season Season) NewShow(int? duration = null)
    {
        var show = Show.Create("Faith Talk", null, ShowType.Recurring, null, duration);
        return (show, Season.Create(show.Id, 1, null, null, null));
    }

    private static Episode NewEpisode()
    {
        var (show, season) = NewShow();
        return Episode.Create(show, season, 1, "Grace", null, null);
    }

    [Fact]
    public void Value_lists_match_the_domain_enums()
    {
        Assert.Equal(Enum.GetNames<ShowType>(), ShowValues.ShowTypes);
        Assert.Equal(Enum.GetNames<ShowStatus>(), ShowValues.ShowStatuses);
        Assert.Equal(Enum.GetNames<SeasonStatus>(), ShowValues.SeasonStatuses);
        Assert.Equal(Enum.GetNames<EpisodeStatus>(), ShowValues.EpisodeStatuses);
        Assert.Equal(Enum.GetNames<EpisodeResponsibility>(), ShowValues.Responsibilities);
        Assert.All(ShowValues.PublicationStatuses, s => Assert.True(Episode.IsPublicationStatus(Enum.Parse<EpisodeStatus>(s))));
    }

    [Fact]
    public void Episodes_only_join_seasons_of_their_own_show()
    {
        var (show, _) = NewShow();
        var (_, foreignSeason) = NewShow();

        Assert.Throws<DomainException>(() => Episode.Create(show, foreignSeason, 1, "Wrong", null, null));
        Assert.Throws<DomainException>(() => Episode.Create(show, null, 0, "Zero", null, null));
        var oneOff = Episode.Create(show, null, null, "Special", null, null);
        Assert.Null(oneOff.SeasonId);
    }

    [Fact]
    public void Recording_uses_the_show_default_duration_when_no_end_is_given()
    {
        var (show, season) = NewShow(duration: 45);
        var episode = Episode.Create(show, season, 1, "Grace", null, null);

        episode.ScheduleRecording(Now, null, " Studio A ", show.DefaultDurationMinutes);
        Assert.Equal(Now.AddMinutes(45), episode.RecordingEnd);
        Assert.Equal("Studio A", episode.RecordingLocation);

        episode.ScheduleRecording(Now, null, null, null);
        Assert.Equal(Now.AddHours(1), episode.RecordingEnd);
        Assert.Throws<DomainException>(() => episode.ScheduleRecording(Now, Now, null, null));
        Assert.Throws<DomainException>(() => episode.ScheduleRecording(null, Now, null, null));
    }

    [Fact]
    public void Status_changes_record_and_clear_the_recording_time()
    {
        var episode = NewEpisode();
        Assert.Throws<DomainException>(() => episode.ChangeStatus(EpisodeStatus.Scheduled, Now));

        episode.ScheduleRecording(Now.AddHours(-3), Now.AddHours(-2), null, null);
        episode.ChangeStatus(EpisodeStatus.Scheduled, Now);
        Assert.Null(episode.RecordedAt);

        // Skipping straight to editing: the past session's end is taken as the recording time.
        episode.ChangeStatus(EpisodeStatus.Editing, Now);
        Assert.Equal(Now.AddHours(-2), episode.RecordedAt);

        episode.ChangeStatus(EpisodeStatus.Cancelled, Now);
        Assert.Equal(Now.AddHours(-2), episode.RecordedAt);
        Assert.Equal(Now, episode.ArchivedAt);

        episode.ChangeStatus(EpisodeStatus.Planned, Now);
        Assert.Null(episode.RecordedAt);
        Assert.Null(episode.ArchivedAt);
        Assert.Throws<DomainException>(() => episode.ChangeStatus(EpisodeStatus.Recorded, Now, Now.AddMinutes(1)));
    }

    [Fact]
    public void Deadlines_are_met_by_reaching_the_next_stage()
    {
        var episode = NewEpisode();
        Assert.Throws<DomainException>(() => episode.SetDeadlines(Today, Today.AddDays(-1), null));
        Assert.Throws<DomainException>(() => episode.SetDeadlines(Today, null, Today.AddDays(-1)));

        episode.SetDeadlines(Today.AddDays(-1), Today.AddDays(2), Today.AddDays(4));
        Assert.Equal(["Editing", "Review", "Approval"], episode.PendingDeadlines().Select(d => d.Label));
        Assert.True(episode.IsOverdue(Today, Now));

        episode.ChangeStatus(EpisodeStatus.InReview, Now);
        Assert.Equal(["Review", "Approval"], episode.PendingDeadlines().Select(d => d.Label));
        Assert.False(episode.IsOverdue(Today, Now));

        episode.ChangeStatus(EpisodeStatus.Approved, Now);
        Assert.Empty(episode.PendingDeadlines());
    }

    [Fact]
    public void A_past_session_without_a_recording_status_is_overdue()
    {
        var episode = NewEpisode();
        episode.ScheduleRecording(Now.AddHours(-3), Now.AddHours(-2), null, null);
        episode.ChangeStatus(EpisodeStatus.Scheduled, Now);
        Assert.True(episode.IsOverdue(Today, Now));

        episode.ChangeStatus(EpisodeStatus.Recorded, Now);
        Assert.False(episode.IsOverdue(Today, Now));
    }

    [Fact]
    public void Assignments_are_unique_per_responsibility_and_the_producer_keeps_theirs()
    {
        var (show, season) = NewShow();
        var producer = Guid.NewGuid();
        var editor = Guid.NewGuid();
        var episode = Episode.Create(show, season, 1, "Grace", null, producer);

        Assert.True(episode.Assign(producer, EpisodeResponsibility.Producer, Now));
        Assert.True(episode.Assign(editor, EpisodeResponsibility.Editor, Now));
        Assert.True(episode.Assign(editor, EpisodeResponsibility.Presenter, Now));
        Assert.False(episode.Assign(editor, EpisodeResponsibility.Editor, Now));
        Assert.Equal(3, episode.Assignments.Count);

        var producerAssignment = episode.Assignments.Single(a => a.Responsibility == EpisodeResponsibility.Producer);
        Assert.Throws<DomainException>(() => episode.Unassign(producerAssignment.Id));
        episode.Unassign(episode.Assignments.Single(a => a.Responsibility == EpisodeResponsibility.Presenter).Id);
        Assert.True(episode.IsAssigned(editor));
    }

    [Theory]
    [InlineData(2, 5, "S2 E5")]
    [InlineData(null, 3, "E3")]
    [InlineData(null, null, "")]
    public void Episode_codes(int? season, int? episode, string expected) =>
        Assert.Equal(expected, Episode.Code(season, episode));

    [Fact]
    public void Seasons_and_shows_validate_their_fields()
    {
        var (show, _) = NewShow();
        Assert.Throws<DomainException>(() => Season.Create(show.Id, 0, null, null, null));
        Assert.Throws<DomainException>(() => Season.Create(show.Id, 1, null, Today, Today.AddDays(-1)));
        Assert.Throws<DomainException>(() => Show.Create(" ", null, ShowType.OneOff, null, null));
        Assert.Throws<DomainException>(() => Show.Create("Long", null, ShowType.OneOff, null, Show.MaxDurationMinutes + 1));

        show.ChangeStatus(ShowStatus.Archived, Now);
        Assert.Throws<DomainException>(() => Episode.Create(show, null, null, "Archived", null, null));
    }

    [Fact]
    public void A_task_cannot_belong_to_both_a_project_and_an_episode() =>
        Assert.Throws<DomainException>(() => WorkTask.Create("Both", null, Guid.NewGuid(), Guid.NewGuid(), null, Priority.Normal));
}
