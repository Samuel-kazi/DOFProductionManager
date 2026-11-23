using System.Net.Http.Json;
using System.Text;
using System.Web;
using Avalonia.Controls;
using Avalonia.VisualTree;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Desktop.Services;
using DOF.Desktop.ViewModels;
using DOF.Desktop.ViewModels.Calendar;
using DOF.Desktop.ViewModels.Pages;
using DOF.Domain.Rules;
using static DOF.IntegrationTests.Desktop.Ui;
using static DOF.IntegrationTests.TestApi;
using CalendarMode = DOF.Desktop.ViewModels.Calendar.CalendarMode;

namespace DOF.IntegrationTests.Desktop;

/// <summary>
/// Plays the browser in the Google sign-in: instead of showing Google's page it immediately sends the
/// redirect Google would send, with an authorization code and the state from the sign-in address.
/// </summary>
internal sealed class FakeBrowser(string code = FakeGoogleCalendarApi.GoodCode) : IBrowserLauncher
{
    public string? OpenedUrl { get; private set; }

    public void Open(string url)
    {
        OpenedUrl = url;
        var query = HttpUtility.ParseQueryString(new Uri(url).Query);
        var redirect = query["redirect_uri"]!;
        var state = query["state"]!;
        _ = Task.Run(async () =>
        {
            using var http = new HttpClient();
            await http.GetAsync($"{redirect}?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}");
        });
    }
}

/// <summary>Schedule PDFs from the calendar, Google Calendar invites on events, and connecting Google in settings.</summary>
public class ScheduleAndGoogleUiTests(DofApiFactory factory, HeadlessSession headless)
{
    private static readonly DateOnly Week = new(2032, 9, 6);

    private static async Task<CalendarViewModel> OpenCalendarAsync(ShellViewModel shell, DateOnly anchor, CalendarMode mode)
    {
        await shell.NavigateCommand.ExecuteAsync(shell.PrimaryItems.Single(i => i.Title == "Calendar"));
        var calendar = Assert.IsType<CalendarViewModel>(shell.CurrentPage);
        calendar.AnchorDate = anchor;
        await calendar.SetModeCommand.ExecuteAsync(mode);
        return calendar;
    }

    [Fact]
    public Task The_calendar_downloads_the_schedule_as_a_pdf() => Ui.Run(headless, async () =>
    {
        var admin = await factory.AdminClientAsync();
        var start = new DateTimeOffset(Week.ToDateTime(new TimeOnly(9, 0)), TimeSpan.FromHours(3));
        await admin.PostAsJsonAsync("/api/v1/calendar/events", new CreateCalendarEventRequest(
            "PDF planning meeting", null, "Meeting", start, start.AddHours(1), false, "Board room", null, "Normal", []), Ct);
        var folder = Directory.CreateTempSubdirectory("dof-pdf-").FullName;

        using var harness = new DesktopHarness(factory, exportFolder: folder);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);
        var calendar = await OpenCalendarAsync(await SignInAsync(main), Week, CalendarMode.Week);

        Assert.Equal(folder, calendar.ExportDirectory);
        Assert.Equal("Week · 6 – 12 September 2032", calendar.PdfWeekLabel);
        Assert.Equal("Month · September 2032", calendar.PdfMonthLabel);
        Assert.Equal("DOF schedule - week of 6 Sep 2032.pdf", calendar.SuggestedPdfName("Week"));
        Assert.Contains(Find<Button>(window), b => b.Name == "DownloadPdfButton" && b.IsEffectivelyVisible);

        var path = Path.Combine(folder, calendar.SuggestedPdfName("Week"));
        await using (var file = File.Create(path))
        {
            await calendar.DownloadPdfAsync("Week", file, Path.GetFileName(path), path);
        }

        Assert.False(calendar.HasError, calendar.ErrorMessage);
        Assert.Equal("%PDF", Encoding.ASCII.GetString((await File.ReadAllBytesAsync(path, Ct))[..4]));
        Assert.Equal(path, calendar.SavedPdfPath);
        Assert.Contains("week of 6 Sep 2032", calendar.PdfMessage);
        Assert.Contains("eventTypes=Meeting", PathWithMeetingsFilter(calendar));
        Snapshot(window, "64-calendar-pdf-saved");

        calendar.DismissPdfMessageCommand.Execute(null);
        Assert.False(calendar.HasPdfMessage);
        window.Close();
        Directory.Delete(folder, recursive: true);
    });

    /// <summary>The export uses the same filters as the calendar.</summary>
    private static string PathWithMeetingsFilter(CalendarViewModel calendar)
    {
        var meetings = calendar.EventTypeFilters.Single(f => f.Value == "Meeting");
        meetings.IsSelected = true;
        var path = calendar.PdfPath("Week");
        meetings.IsSelected = false;
        return path;
    }

    [Fact]
    public Task Google_invites_are_sent_and_removed_from_the_event_details() => Ui.Run(headless, async () =>
    {
        var admin = await factory.AdminClientAsync();
        await GoogleCalendarTests.ConnectAsync(factory, admin);
        var member = await factory.CreateUserAsync(SystemRoles.TeamMember);
        var start = new DateTimeOffset(Week.AddDays(2).ToDateTime(new TimeOnly(14, 0)), TimeSpan.FromHours(3));
        var created = await (await admin.PostAsJsonAsync("/api/v1/calendar/events", new CreateCalendarEventRequest(
                "Season launch rehearsal", null, "Meeting", start, start.AddHours(2), false, "Studio A", null, "High",
                [new CalendarAssignmentRequest(member.Id, "Attendee")]), Ct))
            .DataAsync<CalendarEventResponse>();
        factory.Google.Answers[member.Email] = "accepted";

        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);
        var calendar = await OpenCalendarAsync(await SignInAsync(main), Week, CalendarMode.Week);
        await calendar.OpenEventAsync(created.Id);
        var details = Assert.IsType<EventDetailsViewModel>(calendar.SidePanel);
        var invites = Assert.IsType<GoogleInvitesViewModel>(details.Invites);

        Assert.True(invites.IsConnected);
        Assert.True(invites.CanSend);
        Assert.Equal("Not sent to Google Calendar.", invites.SummaryText);

        await invites.StartEditingCommand.ExecuteAsync(null);
        Assert.True(invites.IsEditing);
        Assert.True(invites.People.Single(p => p.Id == member.Id).IsSelected); // the event's people are suggested
        invites.ExtraEmails = "guest.preacher@example.org";
        Snapshot(window, "65-google-invites-choose-guests");

        await invites.SendCommand.ExecuteAsync(null);
        Assert.False(invites.IsEditing);
        Assert.Equal("Synced", invites.Invite?.Status);
        Assert.Equal(2, invites.Guests.Count);
        Assert.Equal("Going", invites.Guests.Single(g => g.Email == member.Email.ToLowerInvariant()).ResponseText);
        Assert.StartsWith("On 2 people's Google Calendar", invites.SummaryText);
        Assert.True(invites.CanEditGuests);
        Assert.Contains(Find<Border>(window), b => b.Name == "GoogleInvites" && b.IsEffectivelyVisible);
        Snapshot(window, "66-google-invites-sent");

        invites.AskRemoveCommand.Execute(null);
        Assert.True(invites.IsConfirmingRemove);
        await invites.RemoveCommand.ExecuteAsync(null);
        Assert.Equal("Removed", invites.Invite?.Status);
        Assert.True(invites.CanSend);
        Assert.Equal("Send Google invites again…", invites.SendButtonText);
        window.Close();
    });

    [Fact]
    public Task Team_members_see_invites_but_cannot_send_them() => Ui.Run(headless, async () =>
    {
        var admin = await factory.AdminClientAsync();
        await GoogleCalendarTests.ConnectAsync(factory, admin);
        var start = new DateTimeOffset(Week.AddDays(3).ToDateTime(new TimeOnly(10, 0)), TimeSpan.FromHours(3));
        var created = await (await admin.PostAsJsonAsync("/api/v1/calendar/events", new CreateCalendarEventRequest(
            "Read-only invites", null, "Meeting", start, start.AddHours(1), false, null, null, "Normal", []), Ct)).DataAsync<CalendarEventResponse>();
        await admin.PutAsJsonAsync($"/api/v1/calendar/events/{created.Id}/google-invites",
            new SendGoogleInvitesRequest(null, ["someone@example.org"]), Ct);
        var member = await factory.CreateUserAsync(SystemRoles.TeamMember);

        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);
        var calendar = await OpenCalendarAsync(await SignInAsync(main, member.Email, member.Password), Week, CalendarMode.Week);
        await calendar.OpenEventAsync(created.Id);
        var invites = Assert.IsType<EventDetailsViewModel>(calendar.SidePanel).Invites!;

        Assert.Equal("Synced", invites.Invite?.Status);
        Assert.False(invites.CanSend);
        Assert.False(invites.CanEditGuests);
        Assert.False(invites.CanRemove);
        Assert.False(invites.ShowConnectHint);
        window.Close();
    });

    [Fact]
    public Task An_administrator_connects_google_calendar_from_settings() => Ui.Run(headless, async () =>
    {
        var browser = new FakeBrowser();
        factory.Google.NextAccount = "media.team@gmail.com";
        using var harness = new DesktopHarness(factory, browser: browser);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);
        var shell = await SignInAsync(main);
        await shell.OpenSettingsCommand.ExecuteAsync(null);
        var settings = Assert.IsType<SettingsViewModel>(shell.CurrentPage);
        await WaitForAsync(() => settings.GoogleStatus);

        await settings.ConnectGoogleCommand.ExecuteAsync(null);

        Assert.NotNull(browser.OpenedUrl);
        Assert.True(settings.IsGoogleConnected, settings.GoogleMessage);
        Assert.Equal("media.team@gmail.com", settings.GoogleStatus?.AccountEmail);
        Assert.StartsWith("Invites are sent from media.team@gmail.com", settings.GoogleStatusText);
        Assert.StartsWith("http://127.0.0.1:", factory.Google.LastRedirectUri);
        var card = Find<Border>(window).Single(b => b.Name == "GoogleCard");
        card.BringIntoView();
        Snapshot(window, "67-settings-google-connected");

        settings.AskDisconnectGoogleCommand.Execute(null);
        await settings.DisconnectGoogleCommand.ExecuteAsync(null);
        Assert.False(settings.IsGoogleConnected);
        Assert.True(settings.CanConnectGoogle);

        // A cancelled sign-in is reported and leaves nothing connected.
        using var cancelled = new DesktopHarness(factory, browser: new CancellingBrowser());
        var shell2 = await SignInAsync(cancelled.Get<MainWindowViewModel>());
        await shell2.OpenSettingsCommand.ExecuteAsync(null);
        var settings2 = Assert.IsType<SettingsViewModel>(shell2.CurrentPage);
        await WaitForAsync(() => settings2.GoogleStatus);
        await settings2.ConnectGoogleCommand.ExecuteAsync(null);
        Assert.Equal("Google sign-in was cancelled.", settings2.GoogleMessage);
        window.Close();
    });

    private sealed class CancellingBrowser : IBrowserLauncher
    {
        public void Open(string url)
        {
            var redirect = HttpUtility.ParseQueryString(new Uri(url).Query)["redirect_uri"]!;
            _ = Task.Run(async () =>
            {
                using var http = new HttpClient();
                await http.GetAsync($"{redirect}?error=access_denied");
            });
        }
    }
}
