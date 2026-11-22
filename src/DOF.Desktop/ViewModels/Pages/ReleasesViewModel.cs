using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Desktop.Services;
using DOF.Desktop.Services.Api;
using DOF.Desktop.ViewModels.Releases;
using DOF.Desktop.ViewModels.Work;

namespace DOF.Desktop.ViewModels.Pages;

public sealed record ReleaseViewOption(string View, string Label);

/// <summary>
/// Releases in release-focused views (spec 6.4): all, ready to publish, scheduled, published, postponed,
/// overdue, cancelled. Publication is only ever recorded by an explicit confirmation.
/// </summary>
public sealed partial class ReleasesViewModel(ApiClient api, AuthSession session, UserClock clock, Lookups lookups) : PageViewModel
{
    public override string Title => "Releases";

    public bool CanSchedule => session.CurrentUser?.Permissions.Contains("releases.schedule") == true;

    public IReadOnlyList<ReleaseViewOption> Views { get; } =
    [
        new("All", "All"), new("Ready", "Ready to publish"), new("Scheduled", "Scheduled"), new("Published", "Published"),
        new("Postponed", "Postponed"), new("Overdue", "Overdue"), new("Cancelled", "Cancelled"),
    ];

    [ObservableProperty] public partial ReleaseViewOption SelectedView { get; set; } = null!;
    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool OnlyMine { get; set; }

    partial void OnSelectedViewChanged(ReleaseViewOption value) => _ = LoadAsync();
    partial void OnOnlyMineChanged(bool value) => _ = LoadAsync();

    public ObservableCollection<ReleaseRowViewModel> Items { get; } = [];

    public bool IsEmpty => !IsLoading && !HasError && Items.Count == 0;
    public string EmptyText => SelectedView?.View switch
    {
        "Overdue" => "No missed releases.",
        "Ready" => "Nothing is waiting to be published.",
        _ => "No releases here.",
    };

    /// <summary>A line that keeps production completion and distribution completion apart.</summary>
    [ObservableProperty] public partial string? UnpublishedSummary { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSidePanel), nameof(ShowNewButton))]
    public partial object? SidePanel { get; private set; }

    public bool HasSidePanel => SidePanel is not null;

    /// <summary>The toolbar has no room for the button while the side panel is open.</summary>
    public bool ShowNewButton => CanSchedule && !HasSidePanel;

    private int _loadGeneration;

    /// <summary>
    /// Loads the selected view. Switching views while a load is running starts another; only the latest
    /// load's results are shown, so a slower earlier load cannot overwrite them.
    /// </summary>
    public override async Task LoadAsync()
    {
        SelectedView ??= Views[0];
        var generation = ++_loadGeneration;
        var path = $"api/v1/releases?pageSize=100&view={SelectedView.View}";
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            path += $"&search={Uri.EscapeDataString(SearchText.Trim())}";
        }
        if (OnlyMine)
        {
            path += "&mine=true";
        }
        await RunAsync(async () =>
        {
            var page = await api.GetAsync<PagedResponse<ReleaseResponse>>(path);
            if (generation != _loadGeneration)
            {
                return;
            }
            Items.Clear();
            foreach (var release in page.Items)
            {
                Items.Add(new ReleaseRowViewModel(release, clock));
            }
            var unpublished = await api.GetAsync<UnpublishedContentResponse>("api/v1/releases/unpublished");
            var episodes = unpublished.EpisodesWithoutReleases.Count;
            UnpublishedSummary = unpublished.Releases.Count + episodes == 0
                ? "Every finished episode is fully distributed."
                : $"Finished production, not yet distributed: {Plural(unpublished.Releases.Count, "open release")}"
                  + (episodes > 0 ? $", {Plural(episodes, "episode")} with no release planned" : "") + ".";
        });
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
    }

    private static string Plural(int n, string what) => n == 1 ? $"1 {what}" : $"{n} {what}s";

    [RelayCommand]
    private Task SearchAsync() => LoadAsync();

    [RelayCommand]
    private Task OpenRowAsync(ReleaseRowViewModel row) => OpenReleaseAsync(row.Id);

    /// <summary>Opens a release in the side panel (also used by the shell for calendar and dashboard entries).</summary>
    public async Task OpenReleaseAsync(Guid id)
    {
        await RunAsync(async () =>
        {
            var release = await api.GetAsync<ReleaseResponse>($"api/v1/releases/{id}");
            var history = await api.GetAsync<List<ActivityEntryResponse>>($"api/v1/releases/{id}/history");
            SidePanel = new ReleaseDetailsViewModel(release, history, clock);
        });
    }

    [RelayCommand]
    private void CloseSidePanel() => SidePanel = null;

    [RelayCommand]
    private Task AddReminderAsync(Reminders.RemindMeViewModel remind) => RemindMe.AddAsync(api, remind);

    // ---- create and edit ----

    [RelayCommand]
    public async Task NewReleaseAsync(Guid? episodeId)
    {
        await RunAsync(async () =>
        {
            var episodes = (await lookups.OpenEpisodesAsync()).Select(e => new ChoiceOption(e.Id, Lookups.EpisodeLabel(e))).ToList();
            if (episodeId is { } wanted && episodes.All(e => e.Id != wanted))
            {
                var episode = await api.GetAsync<EpisodeResponse>($"api/v1/episodes/{wanted}");
                episodes.Insert(0, new ChoiceOption(episode.Id, $"{episode.Show.Name} {episode.Code}".TrimEnd() + $": {episode.Title}"));
            }
            SidePanel = new ReleaseEditorViewModel(clock, episodes, await lookups.ChannelsAsync(), await PeopleAsync(), null, episodeId);
        });
    }

    [RelayCommand]
    private async Task EditReleaseAsync(ReleaseDetailsViewModel details) =>
        await RunAsync(async () => SidePanel = new ReleaseEditorViewModel(clock,
            [new ChoiceOption(details.Release.Episode.Id, details.EpisodeText)],
            [details.Release.Channel], await PeopleAsync(), details.Release, null));

    [RelayCommand]
    private async Task SaveReleaseAsync(ReleaseEditorViewModel editor)
    {
        editor.ErrorMessage = null;
        await GuardAsync(async () =>
        {
            ReleaseResponse saved;
            if (editor.Existing is { } existing)
            {
                saved = await api.PutAsync<ReleaseResponse>($"api/v1/releases/{existing.Id}", new UpdateReleaseRequest(
                    editor.Status, editor.SelectedPerson.Id, existing.Project?.Id, editor.ExternalUrl, editor.Notes, existing.Version));
            }
            else
            {
                if (editor.SelectedEpisode?.Id is not { } episodeId || editor.SelectedChannel?.Id is not { } channelId)
                {
                    editor.ErrorMessage = "Choose an episode and a channel.";
                    return;
                }
                saved = await api.PostAsync<ReleaseResponse>("api/v1/releases", new CreateReleaseRequest(
                    episodeId, channelId, editor.ScheduledAt, editor.HasTime ? editor.Status : NoTimeStatus(editor.Status),
                    editor.SelectedPerson.Id, null, editor.ExternalUrl, editor.Notes));
            }
            AppLog.Info($"Saved release {saved.Id}");
            await LoadAsync();
            await OpenReleaseAsync(saved.Id);
        }, message => editor.ErrorMessage = message);
    }

    /// <summary>A release without a time cannot be "Scheduled"; it is saved as a draft instead.</summary>
    private static string NoTimeStatus(string status) => status == "Scheduled" ? "Draft" : status;

    // ---- actions ----

    [RelayCommand]
    private void BeginPublish(ReleaseDetailsViewModel details) => Begin(details, ReleaseAction.Publish);
    [RelayCommand]
    private void BeginPostpone(ReleaseDetailsViewModel details) => Begin(details, ReleaseAction.Postpone);
    [RelayCommand]
    private void BeginCancel(ReleaseDetailsViewModel details) => Begin(details, ReleaseAction.Cancel);
    [RelayCommand]
    private void BeginReschedule(ReleaseDetailsViewModel details) => Begin(details, ReleaseAction.Reschedule);
    [RelayCommand]
    private void EndAction(ReleaseDetailsViewModel details) => Begin(details, ReleaseAction.None);

    private static void Begin(ReleaseDetailsViewModel details, ReleaseAction action)
    {
        details.ErrorMessage = null;
        details.Action = action;
    }

    [RelayCommand]
    private Task ConfirmActionAsync(ReleaseDetailsViewModel details) => GuardAsync(async () =>
    {
        var id = details.Release.Id;
        var version = details.Release.Version;
        switch (details.Action)
        {
            case ReleaseAction.Publish:
                if (!details.Confirmed)
                {
                    details.ErrorMessage = "Tick the box to confirm the release has actually been published.";
                    return;
                }
                await api.PatchAsync<ReleaseResponse>($"api/v1/releases/{id}/publish",
                    new PublishReleaseRequest(details.PublishedAt, true, details.PublishedUrl, version));
                break;
            case ReleaseAction.Postpone:
                await api.PatchAsync<ReleaseResponse>($"api/v1/releases/{id}/postpone",
                    new PostponeReleaseRequest(details.ActionReason ?? string.Empty, details.HasNewTime ? details.NewScheduledAt : null, version));
                break;
            case ReleaseAction.Cancel:
                await api.PatchAsync<ReleaseResponse>($"api/v1/releases/{id}/cancel",
                    new CancelReleaseRequest(details.ActionReason ?? string.Empty, version));
                break;
            case ReleaseAction.Reschedule:
                await api.PatchAsync<ReleaseResponse>($"api/v1/releases/{id}/schedule",
                    new RescheduleReleaseRequest(details.NewScheduledAt, version));
                break;
            default:
                return;
        }
        AppLog.Info($"Release {id}: {details.Action}");
        await LoadAsync();
        await OpenReleaseAsync(id);
    }, message => details.ErrorMessage = message);

    [RelayCommand]
    private Task OpenEpisodeAsync(ReleaseDetailsViewModel details) =>
        Navigator?.OpenRecordAsync("Episode", details.Release.Episode.Id) ?? Task.CompletedTask;

    private async Task<IEnumerable<ChoiceOption>> PeopleAsync() =>
        (await lookups.PeopleAsync()).Select(u => new ChoiceOption(u.Id, u.FullName));

    private static async Task GuardAsync(Func<Task> work, Action<string> showError)
    {
        try
        {
            await work();
        }
        catch (ApiException ex)
        {
            showError(ex.Message);
        }
        catch (ApiUnavailableException ex)
        {
            showError(ex.Message);
        }
    }
}
