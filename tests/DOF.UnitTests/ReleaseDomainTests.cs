using DOF.Contracts.Common;
using DOF.Domain.Entities;
using DOF.Domain.Enums;
using DOF.Domain.Exceptions;

namespace DOF.UnitTests;

public class ReleaseDomainTests
{
    private static readonly DateTimeOffset Now = new(2034, 9, 4, 9, 0, 0, TimeSpan.Zero);

    private static Episode NewEpisode()
    {
        var show = Show.Create("Faith Talk", null, ShowType.Recurring, null, null);
        return Episode.Create(show, null, 1, "Grace", null, null);
    }

    private static Release NewRelease(DateTimeOffset? at, ReleaseStatus status = ReleaseStatus.Scheduled) =>
        Release.Create(NewEpisode(), null, DistributionChannel.Create("YouTube", ChannelType.Video, null), null, at, status, null, null, null);

    [Fact]
    public void Value_lists_match_the_domain_enums()
    {
        Assert.Equal(Enum.GetNames<ReleaseStatus>(), ReleaseValues.Statuses);
        Assert.Equal(Enum.GetNames<ChannelType>(), ReleaseValues.ChannelTypes);
        Assert.All(ReleaseValues.WorkflowStatuses, s => Assert.True(Release.IsWorkflowStatus(Enum.Parse<ReleaseStatus>(s))));
    }

    [Fact]
    public void Special_statuses_need_their_own_actions()
    {
        var release = NewRelease(null, ReleaseStatus.Draft);
        foreach (var status in new[] { ReleaseStatus.Published, ReleaseStatus.Postponed, ReleaseStatus.Cancelled, ReleaseStatus.Overdue })
        {
            Assert.Throws<DomainException>(() => release.Update(null, status, null, null, null));
        }
        Assert.Throws<DomainException>(() => release.Update(null, ReleaseStatus.Scheduled, null, null, null));
    }

    [Fact]
    public void A_passed_time_flags_the_release_but_does_not_publish_it()
    {
        var release = NewRelease(Now.AddHours(-1));
        Assert.True(release.IsOverdue(Now));
        Assert.Equal(ReleaseStatus.Scheduled, release.Status);
        Assert.Null(release.ActualPublishedAt);

        release.Reschedule(Now.AddHours(1));
        Assert.False(release.IsOverdue(Now));
    }

    [Fact]
    public void Publication_keeps_the_scheduled_time_and_rejects_future_confirmations()
    {
        var release = NewRelease(Now.AddHours(-2));
        var by = Guid.NewGuid();
        Assert.Throws<DomainException>(() => release.ConfirmPublished(Now.AddHours(1), null, by, Now));

        release.ConfirmPublished(Now.AddMinutes(-30), "https://youtu.be/x", by, Now);
        Assert.Equal((ReleaseStatus.Published, Now.AddHours(-2), Now.AddMinutes(-30)),
            (release.Status, release.ScheduledAt!.Value, release.ActualPublishedAt!.Value));
        Assert.False(release.IsOverdue(Now));
        Assert.Throws<DomainException>(() => release.Reschedule(Now));
        Assert.Throws<DomainException>(() => release.Cancel("No"));
    }

    [Fact]
    public void Postponing_needs_a_reason_and_a_new_time_schedules_it_again()
    {
        var release = NewRelease(Now.AddDays(1));
        Assert.Throws<DomainException>(() => release.Postpone(" ", null));

        release.Postpone("Studio flooded", null);
        Assert.Equal((ReleaseStatus.Postponed, (DateTimeOffset?)null), (release.Status, release.ScheduledAt));
        Assert.False(release.IsOverdue(Now));

        release.Reschedule(Now.AddDays(3));
        Assert.Equal(ReleaseStatus.Scheduled, release.Status);
    }

    [Fact]
    public void Cancelled_episodes_and_inactive_channels_take_no_releases()
    {
        var episode = NewEpisode();
        episode.ChangeStatus(EpisodeStatus.Cancelled, Now);
        var channel = DistributionChannel.Create("YouTube", ChannelType.Video, null);
        Assert.Throws<DomainException>(() => Release.Create(episode, null, channel, null, null, ReleaseStatus.Draft, null, null, null));

        channel.Update("YouTube", ChannelType.Video, null, isActive: false);
        Assert.Throws<DomainException>(() => Release.Create(NewEpisode(), null, channel, null, null, ReleaseStatus.Draft, null, null, null));
    }

    [Fact]
    public void A_release_without_an_episode_is_named_by_its_title()
    {
        var channel = DistributionChannel.Create("Spotify", ChannelType.Audio, null);

        var single = Release.Create(null, "  Worship single: Dawn  ", channel, null, null, ReleaseStatus.Draft, null, null, null);
        Assert.Null(single.EpisodeId);
        Assert.Equal("Worship single: Dawn", single.Title);
        Assert.Equal("Worship single: Dawn", single.DisplayTitle);

        var episodeRelease = Release.Create(NewEpisode(), "ignored", channel, null, null, ReleaseStatus.Draft, null, null, null);
        Assert.Null(episodeRelease.Title);

        Assert.Throws<DomainException>(() => Release.Create(null, " ", channel, null, null, ReleaseStatus.Draft, null, null, null));
        Assert.Throws<DomainException>(() => Release.Create(null, new string('x', 201), channel, null, null, ReleaseStatus.Draft, null, null, null));
    }

    [Theory]
    [InlineData(EpisodeStatus.ReadyForRelease, 3, 0, null)]
    [InlineData(EpisodeStatus.ReadyForRelease, 3, 1, EpisodeStatus.PartiallyPublished)]
    [InlineData(EpisodeStatus.PartiallyPublished, 2, 2, EpisodeStatus.Published)]
    [InlineData(EpisodeStatus.Published, 3, 2, EpisodeStatus.PartiallyPublished)]
    [InlineData(EpisodeStatus.Published, 1, 0, EpisodeStatus.ReadyForRelease)]
    [InlineData(EpisodeStatus.Editing, 0, 0, null)]
    public void Episodes_follow_their_releases(EpisodeStatus start, int active, int published, EpisodeStatus? expected)
    {
        var episode = NewEpisode();
        episode.ChangeStatus(start, Now);

        episode.ApplyReleaseOutcome(active, published, Now);

        Assert.Equal(expected ?? start, episode.Status);
    }
}
