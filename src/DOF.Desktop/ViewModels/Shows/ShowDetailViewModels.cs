using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DOF.Contracts.Common;
using DOF.Contracts.Responses;
using DOF.Desktop.Services;
using DOF.Desktop.ViewModels.Work;

namespace DOF.Desktop.ViewModels.Shows;

/// <summary>One show: header, seasons with their episodes, episodes without a season, and history.</summary>
public sealed partial class ShowDetailViewModel : ObservableObject
{
    private bool _saving;

    public ShowDetailViewModel(ShowResponse show, IReadOnlyList<EpisodeSummaryResponse> episodes,
        IReadOnlyList<ActivityEntryResponse> history, UserClock clock)
    {
        Show = show;
        var editable = show.CanEdit && !IsArchived;
        var rows = episodes.Select(e => new EpisodeRowViewModel(e, clock)).ToList();
        Groups =
        [
            .. show.Seasons.Select(s => new SeasonGroupViewModel(s, [.. rows.Where(r => r.Episode.Season?.Id == s.Id)], editable)),
        ];
        var withoutSeason = rows.Where(r => r.Episode.Season is null).ToList();
        if (withoutSeason.Count > 0 || editable)
        {
            Groups.Add(new SeasonGroupViewModel(null, withoutSeason, editable));
        }
        History = [.. history.Select(h => HistoryLine.From(h, clock))];
        StatusOptions = show.CanManage ? ShowValues.ShowStatuses : [.. ShowValues.ShowStatuses.Where(s => s != ShowValues.Archived)];
        SelectedStatus = show.Status;
    }

    public ShowResponse Show { get; }
    public string Name => Show.Name;
    public string? Description => Show.Description;
    public string TypeText => ShowLabels.Label(Show.ShowType);
    public string ProducerText => Show.Producer?.FullName ?? "No producer";
    public string DurationText => Show.DefaultDurationMinutes is { } m ? $"Usual length {m} min" : "No usual length set";
    public bool IsArchived => Show.Status == ShowValues.Archived;
    public bool CanEdit => Show.CanEdit;
    public bool CanEditStatus => Show.CanEdit && (!IsArchived || Show.CanManage);
    public bool CanAddSeasons => Show.CanEdit && !IsArchived;
    public IReadOnlyList<string> StatusOptions { get; }
    public ObservableCollection<SeasonGroupViewModel> Groups { get; }
    public IReadOnlyList<HistoryLine> History { get; }
    public bool HasNoSeasons => Show.Seasons.Count == 0;

    [ObservableProperty] public partial string SelectedStatus { get; set; }

    public Func<string, Task>? StatusChanged { get; set; }

    /// <summary>Only real choices are saved (see TaskDetailsViewModel for why empty values are ignored).</summary>
    partial void OnSelectedStatusChanged(string value)
    {
        if (_saving || string.IsNullOrEmpty(value) || value == Show.Status || !StatusOptions.Contains(value)
            || StatusChanged is not { } save)
        {
            return;
        }
        _saving = true;
        _ = save(value);
    }
}

/// <summary>
/// One episode: header (show, season, number, title, producer, status), production timeline, production
/// information, crew, tasks and history.
/// </summary>
public sealed partial class EpisodeDetailViewModel : ObservableObject
{
    private bool _saving;

    public EpisodeDetailViewModel(EpisodeResponse episode, IReadOnlyList<ActivityEntryResponse> history, UserClock clock,
        IEnumerable<ChoiceOption> people)
    {
        Episode = episode;
        Stages = [.. episode.Timeline.Select(s => new StageViewModel(s, clock.Today))];
        Crew = [.. episode.Assignments];
        Tasks = [.. episode.Tasks.Select(t => new TaskItemViewModel(t, clock))];
        History = [.. history.Select(h => HistoryLine.From(h, clock))];
        People = [.. people];
        NewResponsibility = "Editor";

        string Names(string responsibility) =>
            episode.Assignments.Where(a => a.Responsibility == responsibility).Select(a => a.FullName) is var names && names.Any()
                ? string.Join(", ", names)
                : "Not assigned";
        string Date(DateOnly? d) => d is { } value ? WorkLabels.DueDate(value, clock.Today) : "Not set";
        var crew = episode.Assignments.Where(a => a.Responsibility is "Director" or "Reviewer" or "PublishingManager" or "Crew")
            .Select(a => $"{a.FullName} ({ShowLabels.Label(a.Responsibility)})").ToList();

        Information =
        [
            new("Recording", ShowLabels.Recording(episode.RecordingStart, episode.RecordingEnd, clock)),
            new("Location", episode.RecordingLocation ?? "Not set"),
            new("Recorded", episode.RecordedAt is { } r ? ShowLabels.When(r, clock) : "Not yet"),
            new("Producer", episode.Producer?.FullName ?? "No producer"),
            new("Editor", Names("Editor")),
            new("Presenter", Names("Presenter")),
            new("Crew", crew.Count == 0 ? "No other crew" : string.Join(", ", crew)),
            new("Editing deadline", Date(episode.EditingDueDate)),
            new("Review deadline", Date(episode.ReviewDueDate)),
            new("Approval deadline", Date(episode.ApprovalDueDate)),
        ];

        // Publication statuses follow release confirmations (Phase 6); they are shown, never offered.
        StatusOptions = ShowValues.PublicationStatuses.Contains(episode.Status)
            ? [episode.Status]
            : [.. ShowValues.EpisodeStatuses.Where(s => !ShowValues.PublicationStatuses.Contains(s))];
        SelectedStatus = episode.Status;
    }

    public EpisodeResponse Episode { get; }
    public string ShowName => Episode.Show.Name;
    public string SeasonText => Episode.Season?.DisplayName ?? "No season";
    public string NumberText => Episode.EpisodeNumber is { } n ? $"Episode {n}" : "Unnumbered";
    public string Title => Episode.Title;
    public string? Description => Episode.Description;
    public string ProducerText => Episode.Producer?.FullName ?? "No producer";
    public string StatusText => ShowLabels.Label(Episode.Status);
    public bool IsOverdue => Episode.IsOverdue;
    public bool IsCancelled => Episode.Status == ShowValues.Cancelled;
    public bool CanEdit => Episode.CanEdit;
    public bool CanEditCrew => Episode.CanEdit && !IsCancelled;
    public bool CanChangeStatus => Episode.CanChangeStatus && !ShowValues.PublicationStatuses.Contains(Episode.Status);
    public bool CanAddTasks => !IsCancelled;
    public IReadOnlyList<string> StatusOptions { get; }
    public IReadOnlyList<string> Responsibilities => ShowValues.Responsibilities;
    public IReadOnlyList<StageViewModel> Stages { get; }
    public IReadOnlyList<InfoLine> Information { get; }
    public ObservableCollection<EpisodeAssignmentResponse> Crew { get; }
    public ObservableCollection<TaskItemViewModel> Tasks { get; }
    public IReadOnlyList<HistoryLine> History { get; }
    public IReadOnlyList<ChoiceOption> People { get; }
    public bool HasNoTasks => Tasks.Count == 0;

    [ObservableProperty] public partial string SelectedStatus { get; set; }
    [ObservableProperty] public partial ChoiceOption? NewPerson { get; set; }
    [ObservableProperty] public partial string NewResponsibility { get; set; }

    public Func<string, Task>? StatusChanged { get; set; }

    /// <summary>Only real choices are saved (see TaskDetailsViewModel for why empty values are ignored).</summary>
    partial void OnSelectedStatusChanged(string value)
    {
        if (_saving || string.IsNullOrEmpty(value) || value == Episode.Status || !StatusOptions.Contains(value)
            || StatusChanged is not { } save)
        {
            return;
        }
        _saving = true;
        _ = save(value);
    }
}
