using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Desktop.Services;
using DOF.Desktop.Services.Api;
using DOF.Desktop.ViewModels.Calendar;
using DOF.Desktop.ViewModels.Releases;
using DOF.Desktop.ViewModels.Shows;
using DOF.Desktop.ViewModels.Work;

namespace DOF.Desktop.ViewModels.Pages;

public sealed class WorkloadRow(WorkloadSummaryResponse summary)
{
    public WorkloadSummaryResponse Summary { get; } = summary;
    public string FullName => Summary.FullName;
    public string OpenText => $"{Summary.OpenTasks} open";
    public bool HasOverdue => Summary.OverdueTasks > 0;
    public string OverdueText => $"{Summary.OverdueTasks} overdue";
    public string DueSoonText => $"{Summary.DueThisWeek} due this week";
    public string MilestonesText => Summary.OpenMilestones == 1 ? "1 milestone" : $"{Summary.OpenMilestones} milestones";
}

/// <summary>
/// Personal dashboard (today, coming up, my tasks, reviews waiting for me, my episodes) and, for people with
/// <c>reports.view</c>, the organisation view. Every entry opens its original record.
/// </summary>
public sealed partial class DashboardViewModel(ApiClient api, AuthSession session, UserClock clock) : PageViewModel
{
    public override string Title => "Dashboard";

    public string Greeting
    {
        get
        {
            var hour = clock.Now.Hour;
            var part = hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening";
            var firstName = session.CurrentUser?.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return firstName is null ? part : $"{part}, {firstName}";
        }
    }

    public string TodayText => clock.Now.ToString("dddd d MMMM yyyy");
    public string TimeZoneText => $"Times shown in {clock.Zone.Id}";
    public bool CanViewOrganisation => session.CurrentUser?.Permissions.Contains("reports.view") == true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowingPersonal))]
    public partial bool ShowingOrganisation { get; set; }

    public bool ShowingPersonal => !ShowingOrganisation;

    public ObservableCollection<CalendarItemViewModel> TodaySchedule { get; } = [];
    public ObservableCollection<CalendarItemViewModel> Upcoming { get; } = [];
    public ObservableCollection<TaskItemViewModel> MyTasks { get; } = [];
    public ObservableCollection<TaskItemViewModel> PendingReviews { get; } = [];
    public ObservableCollection<CalendarItemViewModel> OrganisationUpcoming { get; } = [];
    public ObservableCollection<CalendarItemViewModel> Unassigned { get; } = [];
    public ObservableCollection<ProjectCardViewModel> ActiveProjects { get; } = [];
    public ObservableCollection<MilestoneItemViewModel> UpcomingMilestones { get; } = [];
    public ObservableCollection<TaskItemViewModel> OverdueTasks { get; } = [];
    public ObservableCollection<MilestoneItemViewModel> OverdueMilestones { get; } = [];
    public ObservableCollection<WorkloadRow> TeamWorkload { get; } = [];
    public ObservableCollection<EpisodeRowViewModel> MyEpisodes { get; } = [];
    public ObservableCollection<EpisodeRowViewModel> OverdueEpisodes { get; } = [];
    public ObservableCollection<EpisodeRowViewModel> AwaitingPublication { get; } = [];
    public ObservableCollection<ReleaseRowViewModel> UpcomingReleases { get; } = [];
    public ObservableCollection<ReleaseRowViewModel> OverdueReleases { get; } = [];

    public bool TodayIsEmpty => TodaySchedule.Count == 0;
    public bool UpcomingIsEmpty => Upcoming.Count == 0;
    public bool MyTasksIsEmpty => MyTasks.Count == 0;
    public bool HasPendingReviews => PendingReviews.Count > 0;
    public bool UnassignedIsEmpty => Unassigned.Count == 0;
    public bool OrganisationUpcomingIsEmpty => OrganisationUpcoming.Count == 0;
    public bool ActiveProjectsIsEmpty => ActiveProjects.Count == 0;
    public bool UpcomingMilestonesIsEmpty => UpcomingMilestones.Count == 0;
    public bool NothingOverdue => OverdueTasks.Count == 0 && OverdueMilestones.Count == 0 && OverdueEpisodes.Count == 0
                                  && OverdueReleases.Count == 0;
    public bool UpcomingReleasesIsEmpty => UpcomingReleases.Count == 0;
    public bool MyEpisodesIsEmpty => MyEpisodes.Count == 0;
    public bool AwaitingPublicationIsEmpty => AwaitingPublication.Count == 0;

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            var personal = await api.GetAsync<PersonalDashboardResponse>("api/v1/dashboard/personal");
            Fill(TodaySchedule, personal.TodaySchedule.Select(i => new CalendarItemViewModel(i, clock)));
            Fill(Upcoming, personal.Upcoming.Select(i => new CalendarItemViewModel(i, clock)));
            Fill(MyTasks, personal.MyTasks.Select(t => new TaskItemViewModel(t, clock)));
            Fill(PendingReviews, personal.PendingReviews.Select(t => new TaskItemViewModel(t, clock)));
            Fill(MyEpisodes, personal.MyEpisodes.Select(e => new EpisodeRowViewModel(e, clock)));

            if (CanViewOrganisation)
            {
                var m = await api.GetAsync<ManagementDashboardResponse>("api/v1/dashboard/management");
                var today = clock.Today;
                Fill(OrganisationUpcoming, m.UpcomingActivities.Select(i => new CalendarItemViewModel(i, clock)));
                Fill(Unassigned, m.Unassigned.Select(i => new CalendarItemViewModel(i, clock)));
                Fill(ActiveProjects, m.ActiveProjects.Select(p => new ProjectCardViewModel(p, today)));
                Fill(UpcomingMilestones, m.UpcomingMilestones.Select(x => new MilestoneItemViewModel(x, today)));
                Fill(OverdueTasks, m.Overdue.Tasks.Select(t => new TaskItemViewModel(t, clock)));
                Fill(OverdueMilestones, m.Overdue.Milestones.Select(x => new MilestoneItemViewModel(x, today)));
                Fill(OverdueEpisodes, m.Overdue.Episodes.Select(e => new EpisodeRowViewModel(e, clock)));
                Fill(AwaitingPublication, m.AwaitingPublication.Select(e => new EpisodeRowViewModel(e, clock)));
                Fill(UpcomingReleases, m.UpcomingReleases.Select(r => new ReleaseRowViewModel(r, clock)));
                Fill(OverdueReleases, m.Overdue.Releases.Select(r => new ReleaseRowViewModel(r, clock)));
                Fill(TeamWorkload, m.TeamWorkload.Where(w => w.OpenTasks + w.OpenMilestones > 0).Select(w => new WorkloadRow(w)));
            }
        });

        foreach (var name in new[] { nameof(TodayIsEmpty), nameof(UpcomingIsEmpty), nameof(MyTasksIsEmpty), nameof(HasPendingReviews),
                     nameof(UnassignedIsEmpty), nameof(OrganisationUpcomingIsEmpty), nameof(ActiveProjectsIsEmpty),
                     nameof(UpcomingMilestonesIsEmpty), nameof(NothingOverdue), nameof(MyEpisodesIsEmpty),
                     nameof(AwaitingPublicationIsEmpty), nameof(UpcomingReleasesIsEmpty) })
        {
            OnPropertyChanged(name);
        }
    }

    [RelayCommand] private void ShowPersonal() => ShowingOrganisation = false;
    [RelayCommand] private void ShowOrganisation() => ShowingOrganisation = true;

    [RelayCommand]
    private Task OpenItemAsync(CalendarItemViewModel item) =>
        Navigator?.OpenRecordAsync(item.Item.SourceType, item.Item.SourceId) ?? Task.CompletedTask;

    [RelayCommand]
    private Task OpenTaskAsync(TaskItemViewModel task) => Navigator?.OpenRecordAsync("Task", task.Id) ?? Task.CompletedTask;

    [RelayCommand]
    private Task OpenMilestoneAsync(MilestoneItemViewModel milestone) =>
        Navigator?.OpenRecordAsync("Milestone", milestone.Id) ?? Task.CompletedTask;

    [RelayCommand]
    private Task OpenReleaseAsync(ReleaseRowViewModel release) =>
        Navigator?.OpenRecordAsync("Release", release.Id) ?? Task.CompletedTask;

    [RelayCommand]
    private Task OpenEpisodeAsync(EpisodeRowViewModel episode) =>
        Navigator?.OpenRecordAsync("Episode", episode.Id) ?? Task.CompletedTask;

    [RelayCommand]
    private Task OpenProjectAsync(ProjectCardViewModel project) =>
        Navigator?.OpenRecordAsync("Project", project.Project.Id) ?? Task.CompletedTask;

    [RelayCommand]
    private Task OpenPersonAsync(WorkloadRow row) =>
        Navigator?.ShowTasksForAsync(row.Summary.UserId, row.FullName) ?? Task.CompletedTask;

    /// <summary>Ticks one of my tasks done from the dashboard.</summary>
    [RelayCommand]
    private async Task CompleteTaskAsync(TaskItemViewModel task)
    {
        if (task.CanEdit && await RunAsync(() => api.PatchAsync<TaskResponse>($"api/v1/tasks/{task.Id}/status",
                new ChangeStatusRequest(task.IsCompleted ? "NotStarted" : "Completed"))))
        {
            await LoadAsync();
        }
    }

    private static void Fill<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
