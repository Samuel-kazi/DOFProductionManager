using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DOF.Contracts.Common;
using DOF.Contracts.Responses;
using DOF.Desktop.Services;

namespace DOF.Desktop.ViewModels.Work;

/// <summary>Create or edit a task. The due time is entered in the user's timezone.</summary>
public sealed partial class TaskEditorViewModel : ObservableObject
{
    private readonly UserClock _clock;

    public TaskEditorViewModel(UserClock clock, IEnumerable<PersonOption> people, IEnumerable<ChoiceOption> projects,
        IEnumerable<ChoiceOption> episodes, bool canAssignOthers, Guid me, TaskResponse? existing, Guid? projectId,
        Guid? episodeId = null)
    {
        _clock = clock;
        Existing = existing;
        CanAssignOthers = canAssignOthers;
        Me = me;
        People = new ObservableCollection<PersonOption>(people);
        Links =
        [
            WorkLinkOption.None,
            .. projects.Select(p => new WorkLinkOption(p.Id, null, $"Project · {p.Label}")),
            .. episodes.Select(e => new WorkLinkOption(null, e.Id, $"Episode · {e.Label}")),
        ];

        Title = existing?.Title ?? string.Empty;
        Description = existing?.Description;
        Priority = existing?.Priority ?? "Normal";
        var wantedProject = existing is null ? projectId : existing.Project?.Id;
        var wantedEpisode = existing is null ? episodeId : existing.Episode?.Id;
        // A linked record that is no longer offered (archived project, cancelled episode) stays selectable.
        if (wantedProject is { } p && Links.All(l => l.ProjectId != p) && existing?.Project is { } project)
        {
            Links.Add(new WorkLinkOption(project.Id, null, $"Project · {project.Name}"));
        }
        if (wantedEpisode is { } ep && Links.All(l => l.EpisodeId != ep) && existing?.Episode is { } episode)
        {
            Links.Add(new WorkLinkOption(null, episode.Id, $"Episode · {episode.ShowName} {episode.Code}: {episode.Title}"));
        }
        SelectedLink = Links.FirstOrDefault(l => wantedEpisode is not null ? l.EpisodeId == wantedEpisode
                           : wantedProject is not null && l.ProjectId == wantedProject) ?? WorkLinkOption.None;

        if (existing?.DueAt is { } due)
        {
            var local = clock.ToLocal(due);
            HasDueDate = true;
            DueDate = local.Date;
            DueTime = local.TimeOfDay;
        }
        else
        {
            DueDate = clock.Today.AddDays(1).ToDateTime(TimeOnly.MinValue);
            DueTime = new TimeSpan(17, 0, 0);
        }

        var assigned = existing?.Assignees ?? [new TaskAssigneeResponse(me, string.Empty, true)];
        foreach (var person in People)
        {
            var assignment = assigned.FirstOrDefault(a => a.UserId == person.Id);
            person.IsSelected = assignment is not null;
            person.IsPrimary = assignment?.IsPrimary == true;
            person.PropertyChanged += (_, e) =>
            {
                // Exactly one responsible person: choosing one clears the others (not just in the view's radio group).
                if (e.PropertyName == nameof(PersonOption.IsPrimary) && person.IsPrimary)
                {
                    foreach (var other in People.Where(p => !ReferenceEquals(p, person)))
                    {
                        other.IsPrimary = false;
                    }
                }
            };
        }
    }

    public TaskResponse? Existing { get; }
    public bool IsNew => Existing is null;
    public string Heading => IsNew ? "New task" : "Edit task";
    public bool CanAssignOthers { get; }
    public Guid Me { get; }
    public IReadOnlyList<string> Priorities => CalendarValues.Priorities;
    public List<WorkLinkOption> Links { get; }
    public ObservableCollection<PersonOption> People { get; }

    [ObservableProperty] public partial string Title { get; set; }
    [ObservableProperty] public partial string? Description { get; set; }
    [ObservableProperty] public partial string Priority { get; set; }
    [ObservableProperty] public partial WorkLinkOption SelectedLink { get; set; }

    public Guid? ProjectId => SelectedLink?.ProjectId;
    public Guid? EpisodeId => SelectedLink?.EpisodeId;
    [ObservableProperty] public partial bool HasDueDate { get; set; }
    [ObservableProperty] public partial DateTime? DueDate { get; set; }
    [ObservableProperty] public partial TimeSpan? DueTime { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => ErrorMessage is not null;

    [ObservableProperty]
    public partial bool IsSaving { get; set; }

    public DateTimeOffset? Due => HasDueDate && DueDate is { } date
        ? _clock.FromLocal(date.Date + (DueTime ?? new TimeSpan(17, 0, 0)))
        : null;

    /// <summary>Selected people; the primary first. Without the assign permission it is always just "me".</summary>
    public (List<Guid> Ids, Guid? Primary) Assignees()
    {
        var selected = People.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0)
        {
            return ([Me], Me);
        }
        var primary = selected.FirstOrDefault(p => p.IsPrimary) ?? selected[0];
        return ([.. selected.Select(p => p.Id)], primary.Id);
    }
}
