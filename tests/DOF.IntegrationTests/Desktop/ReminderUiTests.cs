using System.Net.Http.Json;
using DOF.Application.Reminders;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Desktop.ViewModels;
using DOF.Desktop.ViewModels.Pages;
using DOF.Desktop.ViewModels.Work;
using DOF.Desktop.Views.Pages;
using Microsoft.Extensions.DependencyInjection;
using static DOF.IntegrationTests.Desktop.Ui;
using static DOF.IntegrationTests.TestApi;

namespace DOF.IntegrationTests.Desktop;

/// <summary>Notifications and the reminder centre in the desktop client, against the real API and jobs.</summary>
public class ReminderUiTests(DofApiFactory factory, HeadlessSession headless)
{
    private static async Task<T> PageAsync<T>(ShellViewModel shell, string title) where T : PageViewModel
    {
        await shell.NavigateCommand.ExecuteAsync(shell.AllItems.Single(i => i.Title == title));
        var page = Assert.IsType<T>(shell.CurrentPage);
        await WaitForAsync(() => page.IsLoading ? null : page);
        Assert.False(page.HasError, page.ErrorMessage);
        return page;
    }

    [Fact]
    public Task Server_notifications_show_on_the_bell_pop_up_and_open_their_record() => Ui.Run(headless, async () =>
    {
        var admin = await factory.AdminClientAsync();
        var (member, _) = await factory.UserClientAsync(DOF.Domain.Rules.SystemRoles.TeamMember);
        var first = await (await admin.PostAsJsonAsync("/api/v1/tasks", new CreateTaskRequest(
            "Send the rough cut", null, null, DateTimeOffset.UtcNow.AddHours(-2), "High", [member.Id], member.Id), Ct)).DataAsync<TaskResponse>();
        await factory.InScopeAsync(sp => sp.GetRequiredService<OverdueDetector>().RunAsync(DateTimeOffset.UtcNow, Ct));

        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);
        var shell = await SignInAsync(main, member.Email, member.Password);
        var bell = shell.Notifications;
        await bell.RefreshAsync();
        Assert.Equal(1, bell.UnreadCount);
        var item = Assert.Single(bell.Items);
        Assert.Equal("Overdue: Send the rough cut", item.Title);
        Render(window);
        Snapshot(window, "50-notification-badge");

        // A notification that arrives while the app is open pops up.
        var arrived = new List<NotificationResponse>();
        bell.Arrived += (_, n) => arrived.AddRange(n);
        var second = await (await admin.PostAsJsonAsync("/api/v1/tasks", new CreateTaskRequest(
            "Book the narrator", null, null, DateTimeOffset.UtcNow.AddHours(-1), "Normal", [member.Id], member.Id), Ct)).DataAsync<TaskResponse>();
        await factory.InScopeAsync(sp => sp.GetRequiredService<OverdueDetector>().RunAsync(DateTimeOffset.UtcNow, Ct));
        await bell.RefreshAsync();
        Assert.Equal(second.Id, Assert.Single(arrived).SourceId);
        Assert.Equal(2, bell.UnreadCount);

        // Opening it marks it read and goes to the task.
        await bell.OpenCommand.ExecuteAsync(bell.Items.Single(i => i.Notification.SourceId == first.Id));
        var tasks = Assert.IsType<TasksViewModel>(shell.CurrentPage);
        Assert.Equal(first.Id, Assert.IsType<TaskDetailsViewModel>(tasks.SidePanel).Task.Id);
        Assert.Equal(1, bell.UnreadCount);
        await bell.MarkAllReadCommand.ExecuteAsync(null);
        Assert.False(bell.HasUnread);
        window.Close();
    });

    [Fact]
    public Task The_reminder_centre_lists_reminders_saves_preferences_and_adds_personal_reminders() => Ui.Run(headless, async () =>
    {
        var admin = await factory.AdminClientAsync();
        var (member, memberClient) = await factory.UserClientAsync(DOF.Domain.Rules.SystemRoles.TeamMember);
        var task = await (await admin.PostAsJsonAsync("/api/v1/tasks", new CreateTaskRequest(
            "Record the voice-over", null, null, DateTimeOffset.UtcNow.AddDays(5), "Normal", [member.Id], member.Id), Ct)).DataAsync<TaskResponse>();
        await factory.InScopeAsync(sp => sp.GetRequiredService<OutboxProcessor>().RunAsync(DateTimeOffset.UtcNow, Ct));

        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);
        var shell = await SignInAsync(main, member.Email, member.Password);
        var reminders = await PageAsync<RemindersViewModel>(shell, "Reminders");
        Render(window);
        Assert.Single(Find<RemindersView>(window));
        Assert.Equal(2, reminders.Items.Count(r => r.Reminder.SourceId == task.Id));
        Assert.Equal(["1 hour before", "1 day before"], reminders.Offsets.Where(o => o.IsSelected).Select(o => o.Label));
        Snapshot(window, "51-reminder-centre");

        // New defaults recalculate upcoming reminders on the server.
        reminders.Offsets.Single(o => o.Minutes == 60).IsSelected = false;
        reminders.Offsets.Single(o => o.Minutes == 4320).IsSelected = true;
        reminders.DesktopEnabled = false;
        await reminders.SavePreferencesCommand.ExecuteAsync(null);
        Assert.NotNull(reminders.PreferencesMessage);
        Assert.False(shell.Notifications.DesktopPopups);
        await factory.InScopeAsync(sp => sp.GetRequiredService<OutboxProcessor>().RunAsync(DateTimeOffset.UtcNow, Ct));
        await reminders.LoadAsync();
        Assert.Equal(["1 day before · default", "3 days before · default"],
            reminders.Items.Where(r => r.Reminder.SourceId == task.Id).Select(r => r.RuleText).Order());

        // A personal reminder from the task's details, then cancel it from the centre.
        await shell.OpenRecordAsync("Task", task.Id);
        var tasks = Assert.IsType<TasksViewModel>(shell.CurrentPage);
        var details = Assert.IsType<TaskDetailsViewModel>(tasks.SidePanel);
        Assert.True(details.RemindMe.IsAvailable);
        details.RemindMe.Selected = details.RemindMe.Options.Single(o => o.Minutes == 15);
        await tasks.AddReminderCommand.ExecuteAsync(details.RemindMe);
        Assert.Equal("You'll be reminded 15 minutes before.", details.RemindMe.Message);

        reminders = await PageAsync<RemindersViewModel>(shell, "Reminders");
        var personal = reminders.Items.Single(r => r.RuleText == "15 minutes before · personal");
        await reminders.CancelReminderCommand.ExecuteAsync(personal);
        reminders.SelectedView = reminders.Views.Single(v => v.Label == "Cancelled");
        await WaitForAsync(() => reminders.IsLoading ? null : reminders.Items.FirstOrDefault(r => r.Id == personal.Id));
        Snapshot(window, "52-reminders-cancelled");
        window.Close();
    });
}
