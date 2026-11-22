using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DOF.Contracts.Responses;
using DOF.Desktop.Services;
using DOF.Desktop.Services.Api;
using DOF.Desktop.ViewModels.Calendar;

namespace DOF.Desktop.ViewModels.Pages;

/// <summary>
/// Personal dashboard (today's schedule and what's coming up for me) and, for people with
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
    public ObservableCollection<CalendarItemViewModel> OrganisationUpcoming { get; } = [];
    public ObservableCollection<CalendarItemViewModel> Unassigned { get; } = [];

    public bool TodayIsEmpty => TodaySchedule.Count == 0;
    public bool UpcomingIsEmpty => Upcoming.Count == 0;
    public bool OrganisationUpcomingIsEmpty => OrganisationUpcoming.Count == 0;
    public bool UnassignedIsEmpty => Unassigned.Count == 0;

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            var personal = await api.GetAsync<PersonalDashboardResponse>("api/v1/dashboard/personal");
            Fill(TodaySchedule, personal.TodaySchedule);
            Fill(Upcoming, personal.Upcoming);

            if (CanViewOrganisation)
            {
                var management = await api.GetAsync<ManagementDashboardResponse>("api/v1/dashboard/management");
                Fill(OrganisationUpcoming, management.UpcomingActivities);
                Fill(Unassigned, management.Unassigned);
            }
        });

        OnPropertyChanged(nameof(TodayIsEmpty));
        OnPropertyChanged(nameof(UpcomingIsEmpty));
        OnPropertyChanged(nameof(OrganisationUpcomingIsEmpty));
        OnPropertyChanged(nameof(UnassignedIsEmpty));
    }

    [RelayCommand]
    private void ShowPersonal() => ShowingOrganisation = false;

    [RelayCommand]
    private void ShowOrganisation() => ShowingOrganisation = true;

    [RelayCommand]
    private Task OpenItemAsync(CalendarItemViewModel item) =>
        Navigator?.OpenRecordAsync(item.Item.SourceType, item.Item.SourceId) ?? Task.CompletedTask;

    private void Fill(ObservableCollection<CalendarItemViewModel> target, IEnumerable<CalendarItemResponse> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(new CalendarItemViewModel(item, clock));
        }
    }
}
