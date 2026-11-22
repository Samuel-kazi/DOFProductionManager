using CommunityToolkit.Mvvm.ComponentModel;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Desktop.Services;
using DOF.Desktop.ViewModels.Work;

namespace DOF.Desktop.ViewModels.Shows;

public abstract partial class EditorBase : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => ErrorMessage is not null;

    public static DateOnly? ToDate(DateTime? value) => value is { } v ? DateOnly.FromDateTime(v) : null;

    public static DateTime? FromDate(DateOnly? value) => value?.ToDateTime(TimeOnly.MinValue);
}

public sealed partial class ShowEditorViewModel : EditorBase
{
    public ShowEditorViewModel(IEnumerable<ChoiceOption> people, ShowResponse? existing)
    {
        Existing = existing;
        Producers = [new ChoiceOption(null, "No producer yet"), .. people];
        Name = existing?.Name ?? string.Empty;
        Description = existing?.Description;
        ShowType = existing?.ShowType ?? "Recurring";
        SelectedProducer = Producers.FirstOrDefault(p => p.Id == existing?.Producer?.Id) ?? Producers[0];
        DurationMinutes = existing is null ? 60 : existing.DefaultDurationMinutes;
    }

    public ShowResponse? Existing { get; }
    public string Heading => Existing is null ? "New show" : "Edit show";
    public IReadOnlyList<string> ShowTypes => ShowValues.ShowTypes;
    public IReadOnlyList<ChoiceOption> Producers { get; }

    [ObservableProperty] public partial string Name { get; set; }
    [ObservableProperty] public partial string? Description { get; set; }
    [ObservableProperty] public partial string ShowType { get; set; }
    [ObservableProperty] public partial ChoiceOption SelectedProducer { get; set; }
    [ObservableProperty] public partial decimal? DurationMinutes { get; set; }

    public int? Duration => DurationMinutes is { } d ? (int)d : null;

    public CreateShowRequest ToCreate() => new(Name, Description, ShowType, SelectedProducer.Id, Duration);

    public UpdateShowRequest ToUpdate(ShowResponse e) => new(Name, Description, ShowType, SelectedProducer.Id, Duration, e.Version);
}

public sealed partial class SeasonEditorViewModel : EditorBase
{
    public SeasonEditorViewModel(Guid showId, SeasonResponse? existing, int nextNumber)
    {
        ShowId = showId;
        Existing = existing;
        SeasonNumber = existing?.SeasonNumber ?? nextNumber;
        Name = existing?.Name;
        StartDate = FromDate(existing?.StartDate);
        EndDate = FromDate(existing?.EndDate);
        Status = existing?.Status ?? "Planned";
    }

    public Guid ShowId { get; }
    public SeasonResponse? Existing { get; }
    public bool IsExisting => Existing is not null;
    public string Heading => Existing is null ? "New season" : $"Edit {Existing.DisplayName}";
    public IReadOnlyList<string> Statuses => ShowValues.SeasonStatuses;

    [ObservableProperty] public partial decimal? SeasonNumber { get; set; }
    [ObservableProperty] public partial string? Name { get; set; }
    [ObservableProperty] public partial DateTime? StartDate { get; set; }
    [ObservableProperty] public partial DateTime? EndDate { get; set; }
    [ObservableProperty] public partial string Status { get; set; }

    public int? Number => SeasonNumber is { } n ? (int)n : null;
}

/// <summary>Create or edit an episode. Times are entered in the user's timezone.</summary>
public sealed partial class EpisodeEditorViewModel : EditorBase
{
    private readonly UserClock _clock;

    public EpisodeEditorViewModel(UserClock clock, ShowResponse show, Guid? seasonId, IEnumerable<ChoiceOption> people,
        EpisodeResponse? existing)
    {
        _clock = clock;
        Show = show;
        Existing = existing;
        Seasons = [new ChoiceOption(null, "No season (one-off)"), .. show.Seasons.Select(s => new ChoiceOption(s.Id, s.DisplayName))];
        Producers = [new ChoiceOption(null, "No producer"), .. people];

        Title = existing?.Title ?? string.Empty;
        Description = existing?.Description;
        var season = existing is null ? seasonId : existing.Season?.Id;
        SelectedSeason = Seasons.FirstOrDefault(s => s.Id == season) ?? Seasons[0];
        EpisodeNumber = existing?.EpisodeNumber;
        var producer = existing is null ? show.Producer?.Id : existing.Producer?.Id;
        SelectedProducer = Producers.FirstOrDefault(p => p.Id == producer) ?? Producers[0];
        Location = existing?.RecordingLocation;

        if (existing?.RecordingStart is { } start && existing.RecordingEnd is { } end)
        {
            HasRecording = true;
            var local = clock.ToLocal(start);
            RecordingDate = local.Date;
            RecordingTime = local.TimeOfDay;
            DurationMinutes = (decimal)(end - start).TotalMinutes;
        }
        else
        {
            RecordingDate = clock.Today.AddDays(7).ToDateTime(TimeOnly.MinValue);
            RecordingTime = new TimeSpan(10, 0, 0);
            DurationMinutes = show.DefaultDurationMinutes ?? 60;
        }
        EditingDue = FromDate(existing?.EditingDueDate);
        ReviewDue = FromDate(existing?.ReviewDueDate);
        ApprovalDue = FromDate(existing?.ApprovalDueDate);
    }

    public ShowResponse Show { get; }
    public EpisodeResponse? Existing { get; }
    public bool IsNew => Existing is null;
    public string Heading => IsNew ? $"New episode of {Show.Name}" : "Edit episode";
    public IReadOnlyList<ChoiceOption> Seasons { get; }
    public IReadOnlyList<ChoiceOption> Producers { get; }
    public string NumberHint => IsNew && SelectedSeason?.Id is not null ? "Leave empty to number it next" : "Optional";
    public string NumberPlaceholder => IsNew && SelectedSeason?.Id is not null ? "Next" : "None";

    [ObservableProperty] public partial string Title { get; set; }
    [ObservableProperty] public partial string? Description { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NumberHint), nameof(NumberPlaceholder))]
    public partial ChoiceOption SelectedSeason { get; set; }
    [ObservableProperty] public partial decimal? EpisodeNumber { get; set; }
    [ObservableProperty] public partial ChoiceOption SelectedProducer { get; set; }
    [ObservableProperty] public partial bool HasRecording { get; set; }
    [ObservableProperty] public partial DateTime? RecordingDate { get; set; }
    [ObservableProperty] public partial TimeSpan? RecordingTime { get; set; }
    [ObservableProperty] public partial decimal? DurationMinutes { get; set; }
    [ObservableProperty] public partial string? Location { get; set; }
    [ObservableProperty] public partial DateTime? EditingDue { get; set; }
    [ObservableProperty] public partial DateTime? ReviewDue { get; set; }
    [ObservableProperty] public partial DateTime? ApprovalDue { get; set; }

    public int? Number => EpisodeNumber is { } n ? (int)n : null;

    public DateTimeOffset? RecordingStart => HasRecording && RecordingDate is { } d
        ? _clock.FromLocal(d.Date + (RecordingTime ?? new TimeSpan(10, 0, 0)))
        : null;

    public DateTimeOffset? RecordingEnd => RecordingStart is { } s ? s.AddMinutes((double)(DurationMinutes ?? 60)) : null;

    public CreateEpisodeRequest ToCreate() => new(Title, Description, Number, SelectedProducer.Id, RecordingStart, RecordingEnd,
        Location, ToDate(EditingDue), ToDate(ReviewDue), ToDate(ApprovalDue));

    public UpdateEpisodeRequest ToUpdate(EpisodeResponse e) => new(Title, Description, SelectedSeason.Id, Number,
        SelectedProducer.Id, RecordingStart, RecordingEnd, Location, ToDate(EditingDue), ToDate(ReviewDue), ToDate(ApprovalDue),
        e.Version);
}
