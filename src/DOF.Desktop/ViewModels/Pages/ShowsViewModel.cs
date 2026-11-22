using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Desktop.Services;
using DOF.Desktop.Services.Api;
using DOF.Desktop.ViewModels.Shows;
using DOF.Desktop.ViewModels.Work;

namespace DOF.Desktop.ViewModels.Pages;

/// <summary>Shows & Episodes: the show list (or my episodes), a show with its seasons, and an episode.</summary>
public sealed partial class ShowsViewModel(ApiClient api, AuthSession session, UserClock clock, Lookups lookups) : PageViewModel
{
    public override string Title => "Shows & Episodes";

    public bool CanCreate => session.CurrentUser?.Permissions.Contains("episodes.manage") == true;

    public ObservableCollection<ShowCardViewModel> Shows { get; } = [];
    public ObservableCollection<EpisodeRowViewModel> MyEpisodes { get; } = [];

    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IncludeArchived { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowingShowCards), nameof(ShowingMyEpisodes))]
    public partial bool OnlyMyEpisodes { get; set; }

    partial void OnIncludeArchivedChanged(bool value) => _ = LoadAsync();
    partial void OnOnlyMyEpisodesChanged(bool value) => _ = LoadAsync();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowingList), nameof(ShowingShow), nameof(ShowingEpisode), nameof(ShowingShowCards),
        nameof(ShowingMyEpisodes))]
    public partial ShowDetailViewModel? ShowDetail { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowingList), nameof(ShowingShow), nameof(ShowingEpisode), nameof(ShowingShowCards),
        nameof(ShowingMyEpisodes))]
    public partial EpisodeDetailViewModel? EpisodeDetail { get; private set; }

    public bool ShowingEpisode => EpisodeDetail is not null;
    public bool ShowingShow => ShowDetail is not null && EpisodeDetail is null;
    public bool ShowingList => ShowDetail is null && EpisodeDetail is null;
    public bool ShowingShowCards => ShowingList && !OnlyMyEpisodes;
    public bool ShowingMyEpisodes => ShowingList && OnlyMyEpisodes;

    public bool IsEmpty => !IsLoading && !HasError && ShowingList && (OnlyMyEpisodes ? MyEpisodes.Count == 0 : Shows.Count == 0);
    public string EmptyText => OnlyMyEpisodes ? "You are not producing or crewing any open episodes." : "No shows yet.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSidePanel))]
    public partial object? SidePanel { get; private set; }

    public bool HasSidePanel => SidePanel is not null;

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            if (OnlyMyEpisodes)
            {
                var mine = await api.GetAsync<PagedResponse<EpisodeSummaryResponse>>("api/v1/episodes?mine=true&open=true&pageSize=100");
                MyEpisodes.Clear();
                foreach (var e in mine.Items.OrderBy(e => e.RecordingStart ?? DateTimeOffset.MaxValue))
                {
                    MyEpisodes.Add(new EpisodeRowViewModel(e, clock));
                }
                return;
            }

            var path = "api/v1/shows?pageSize=100";
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                path += $"&search={Uri.EscapeDataString(SearchText.Trim())}";
            }
            if (IncludeArchived)
            {
                path += "&includeArchived=true";
            }
            var page = await api.GetAsync<PagedResponse<ShowSummaryResponse>>(path);
            Shows.Clear();
            foreach (var show in page.Items)
            {
                Shows.Add(new ShowCardViewModel(show, clock));
            }
        });
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
    }

    [RelayCommand]
    private Task SearchAsync() => LoadAsync();

    [RelayCommand]
    private Task OpenCardAsync(ShowCardViewModel card) => OpenShowAsync(card.Show.Id);

    [RelayCommand]
    private Task OpenEpisodeRowAsync(EpisodeRowViewModel row) => OpenEpisodeAsync(row.Id);

    /// <summary>Opens a show (also used by the shell for Show records).</summary>
    public async Task OpenShowAsync(Guid id)
    {
        await RunAsync(async () =>
        {
            var show = await api.GetAsync<ShowResponse>($"api/v1/shows/{id}");
            var episodes = await api.GetAsync<List<EpisodeSummaryResponse>>($"api/v1/shows/{id}/episodes");
            var history = await api.GetAsync<List<ActivityEntryResponse>>($"api/v1/shows/{id}/history");
            var detail = new ShowDetailViewModel(show, episodes, history, clock);
            detail.StatusChanged = status => ChangeShowStatusAsync(detail, status);
            EpisodeDetail = null;
            ShowDetail = detail;
            SidePanel = null;
        });
    }

    /// <summary>Opens an episode (also used by the shell for calendar and dashboard entries).</summary>
    public async Task OpenEpisodeAsync(Guid id)
    {
        await RunAsync(async () =>
        {
            var episode = await api.GetAsync<EpisodeResponse>($"api/v1/episodes/{id}");
            var history = await api.GetAsync<List<ActivityEntryResponse>>($"api/v1/episodes/{id}/history");
            var people = episode.CanEdit ? (await lookups.PeopleAsync()).Select(u => new ChoiceOption(u.Id, u.FullName)) : [];
            var detail = new EpisodeDetailViewModel(episode, history, clock, people);
            detail.StatusChanged = status => ChangeEpisodeStatusAsync(detail, status);
            if (ShowDetail?.Show.Id != episode.Show.Id)
            {
                ShowDetail = null;
            }
            EpisodeDetail = detail;
            SidePanel = null;
        });
    }

    [RelayCommand]
    private async Task BackAsync()
    {
        SidePanel = null;
        if (EpisodeDetail is { } episode)
        {
            EpisodeDetail = null;
            await OpenShowAsync(episode.Episode.Show.Id);
            return;
        }
        ShowDetail = null;
        await LoadAsync();
    }

    public string BackText => ShowingEpisode ? $"‹ {EpisodeDetail?.ShowName}" : "‹ All shows";

    partial void OnEpisodeDetailChanged(EpisodeDetailViewModel? value) => OnPropertyChanged(nameof(BackText));

    partial void OnShowDetailChanged(ShowDetailViewModel? value) => OnPropertyChanged(nameof(BackText));

    [RelayCommand]
    private void CloseSidePanel() => SidePanel = null;

    // ---- shows ----

    [RelayCommand]
    public async Task NewShowAsync() =>
        await RunAsync(async () => SidePanel = new ShowEditorViewModel(await PeopleAsync(), null));

    [RelayCommand]
    private async Task EditShowAsync()
    {
        if (ShowDetail is { } detail)
        {
            await RunAsync(async () => SidePanel = new ShowEditorViewModel(await PeopleAsync(), detail.Show));
        }
    }

    [RelayCommand]
    private Task SaveShowAsync(ShowEditorViewModel editor) => SaveAsync(editor, async () =>
    {
        var saved = editor.Existing is { } existing
            ? await api.PutAsync<ShowResponse>($"api/v1/shows/{existing.Id}", editor.ToUpdate(existing))
            : await api.PostAsync<ShowResponse>("api/v1/shows", editor.ToCreate());
        AppLog.Info($"Saved show {saved.Id}");
        await OpenShowAsync(saved.Id);
    });

    private async Task ChangeShowStatusAsync(ShowDetailViewModel detail, string status)
    {
        await RunAsync(() => api.PatchAsync<ShowResponse>($"api/v1/shows/{detail.Show.Id}/status",
            new ChangeStatusRequest(status, detail.Show.Version)));
        await OpenShowAsync(detail.Show.Id);
    }

    // ---- seasons ----

    [RelayCommand]
    private void NewSeason()
    {
        if (ShowDetail is { } detail)
        {
            var next = detail.Show.Seasons.Select(s => s.SeasonNumber).DefaultIfEmpty(0).Max() + 1;
            SidePanel = new SeasonEditorViewModel(detail.Show.Id, null, next);
        }
    }

    [RelayCommand]
    private void EditSeason(SeasonGroupViewModel group)
    {
        if (ShowDetail is { } detail && group.Season is { } season)
        {
            SidePanel = new SeasonEditorViewModel(detail.Show.Id, season, season.SeasonNumber);
        }
    }

    [RelayCommand]
    private Task SaveSeasonAsync(SeasonEditorViewModel editor) => SaveAsync(editor, async () =>
    {
        if (editor.Existing is { } existing)
        {
            await api.PutAsync<SeasonResponse>($"api/v1/seasons/{existing.Id}", new UpdateSeasonRequest(
                editor.Number ?? existing.SeasonNumber, editor.Name, EditorBase.ToDate(editor.StartDate),
                EditorBase.ToDate(editor.EndDate), editor.Status, existing.Version));
        }
        else
        {
            await api.PostAsync<SeasonResponse>($"api/v1/shows/{editor.ShowId}/seasons", new CreateSeasonRequest(
                editor.Number, editor.Name, EditorBase.ToDate(editor.StartDate), EditorBase.ToDate(editor.EndDate)));
        }
        await OpenShowAsync(editor.ShowId);
    });

    // ---- episodes ----

    [RelayCommand]
    private async Task NewEpisodeAsync(SeasonGroupViewModel group)
    {
        if (ShowDetail is { } detail)
        {
            await RunAsync(async () =>
                SidePanel = new EpisodeEditorViewModel(clock, detail.Show, group.Season?.Id, await PeopleAsync(), null));
        }
    }

    [RelayCommand]
    private async Task EditEpisodeAsync()
    {
        if (EpisodeDetail is { } detail)
        {
            await RunAsync(async () =>
            {
                var show = await api.GetAsync<ShowResponse>($"api/v1/shows/{detail.Episode.Show.Id}");
                SidePanel = new EpisodeEditorViewModel(clock, show, detail.Episode.Season?.Id, await PeopleAsync(), detail.Episode);
            });
        }
    }

    [RelayCommand]
    private Task SaveEpisodeAsync(EpisodeEditorViewModel editor) => SaveAsync(editor, async () =>
    {
        EpisodeResponse saved;
        if (editor.Existing is { } existing)
        {
            saved = await api.PutAsync<EpisodeResponse>($"api/v1/episodes/{existing.Id}", editor.ToUpdate(existing));
        }
        else if (editor.SelectedSeason.Id is { } seasonId)
        {
            saved = await api.PostAsync<EpisodeResponse>($"api/v1/seasons/{seasonId}/episodes", editor.ToCreate());
        }
        else
        {
            saved = await api.PostAsync<EpisodeResponse>($"api/v1/shows/{editor.Show.Id}/episodes", editor.ToCreate());
        }
        AppLog.Info($"Saved episode {saved.Id}");
        await OpenEpisodeAsync(saved.Id);
    });

    private async Task ChangeEpisodeStatusAsync(EpisodeDetailViewModel detail, string status)
    {
        await RunAsync(() => api.PatchAsync<EpisodeResponse>($"api/v1/episodes/{detail.Episode.Id}/status",
            new ChangeEpisodeStatusRequest(status, detail.Episode.Version)));
        await OpenEpisodeAsync(detail.Episode.Id);
    }

    [RelayCommand]
    private async Task AssignAsync()
    {
        if (EpisodeDetail is { NewPerson.Id: { } userId } detail
            && await RunAsync(() => api.PostAsync<EpisodeResponse>($"api/v1/episodes/{detail.Episode.Id}/assignments",
                new AssignEpisodeRequest(userId, detail.NewResponsibility))))
        {
            await OpenEpisodeAsync(detail.Episode.Id);
        }
    }

    [RelayCommand]
    private async Task UnassignAsync(EpisodeAssignmentResponse assignment)
    {
        if (EpisodeDetail is { } detail
            && await RunAsync(() => api.DeleteAsync($"api/v1/episodes/{detail.Episode.Id}/assignments/{assignment.Id}")))
        {
            await OpenEpisodeAsync(detail.Episode.Id);
        }
    }

    [RelayCommand]
    private Task OpenTaskAsync(TaskItemViewModel task) => Navigator?.OpenRecordAsync("Task", task.Id) ?? Task.CompletedTask;

    [RelayCommand]
    private Task NewTaskAsync() =>
        EpisodeDetail is { } detail && Navigator is { } navigator
            ? navigator.CreateEpisodeTaskAsync(detail.Episode.Id)
            : Task.CompletedTask;

    private async Task<IEnumerable<ChoiceOption>> PeopleAsync() =>
        (await lookups.PeopleAsync()).Select(u => new ChoiceOption(u.Id, u.FullName));

    private static async Task SaveAsync(EditorBase editor, Func<Task> save)
    {
        editor.ErrorMessage = null;
        try
        {
            await save();
        }
        catch (ApiException ex)
        {
            editor.ErrorMessage = ex.Message;
        }
        catch (ApiUnavailableException ex)
        {
            editor.ErrorMessage = ex.Message;
        }
    }
}
