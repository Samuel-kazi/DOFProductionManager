using System.Net;
using System.Net.Http.Json;
using DOF.Application.Reminders;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Domain.Rules;
using DOF.Infrastructure.Persistence;
using Hangfire;
using Hangfire.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static DOF.IntegrationTests.TestApi;

namespace DOF.IntegrationTests;

public class ReminderTests(DofApiFactory factory)
{
    private static DateTimeOffset Now => DateTimeOffset.UtcNow;

    private Task<int> ProcessOutboxAsync(DateTimeOffset now) =>
        factory.InScopeAsync(sp => sp.GetRequiredService<OutboxProcessor>().RunAsync(now, Ct));

    private Task<int> DispatchAsync(DateTimeOffset now) =>
        factory.InScopeAsync(sp => sp.GetRequiredService<ReminderDispatcher>().RunAsync(now, Ct));

    private static async Task<List<ReminderResponse>> RemindersAsync(HttpClient client, Guid sourceId, string? status = null) =>
        (await (await client.GetAsync($"/api/v1/reminders?sourceId={sourceId}&pageSize=100{(status is null ? "" : $"&statuses={status}")}", Ct))
            .DataAsync<PagedResponse<ReminderResponse>>()).Items.ToList();

    private static async Task SetPreferencesAsync(HttpClient client, bool email, params int[] offsets) =>
        await (await client.PutAsJsonAsync("/api/v1/notification-preferences",
            new UpdateNotificationPreferencesRequest(email, true, true, offsets), Ct)).DataAsync<NotificationPreferencesResponse>();

    private static async Task<TaskResponse> TaskForAsync(HttpClient admin, Guid userId, DateTimeOffset due, string title = "Edit the trailer") =>
        await (await admin.PostAsJsonAsync("/api/v1/tasks",
            new CreateTaskRequest(title, null, null, due, "Normal", [userId], userId), Ct)).DataAsync<TaskResponse>();

    private static async Task<TaskResponse> RescheduleAsync(HttpClient admin, TaskResponse task, DateTimeOffset due) =>
        await (await admin.PutAsJsonAsync($"/api/v1/tasks/{task.Id}", new UpdateTaskRequest(task.Title, null, null, due, "Normal",
            [.. task.Assignees.Select(a => a.UserId)], task.Assignees.Single(a => a.IsPrimary).UserId, task.Version), Ct)).DataAsync<TaskResponse>();

    // ---------- acceptance: a changed source date recalculates pending reminders ----------

    [Fact]
    public async Task Automatic_reminders_are_recalculated_when_a_task_is_rescheduled_or_completed()
    {
        var admin = await factory.AdminClientAsync();
        var (member, memberClient) = await factory.UserClientAsync(SystemRoles.TeamMember);
        await SetPreferencesAsync(memberClient, false, 60, 1440);
        var due = Now.AddDays(10).AddTicks(-(Now.Ticks % TimeSpan.TicksPerSecond));
        var task = await TaskForAsync(admin, member.Id, due);
        await ProcessOutboxAsync(Now);

        var pending = await RemindersAsync(memberClient, task.Id, "Pending");
        Assert.Equal([due.AddDays(-1), due.AddHours(-1)], pending.Select(r => r.ScheduledAt).Order());
        Assert.All(pending, r => Assert.True(r.IsAutomatic));
        Assert.Equal(due, pending[0].SourceAt);

        // Moving the task cancels the old reminders (kept for history) and schedules new ones.
        var newDue = due.AddDays(2);
        task = await RescheduleAsync(admin, task, newDue);
        await ProcessOutboxAsync(Now);
        var all = await RemindersAsync(memberClient, task.Id);
        Assert.Equal([newDue.AddDays(-1), newDue.AddHours(-1)], all.Where(r => r.Status == "Pending").Select(r => r.ScheduledAt).Order());
        var cancelled = all.Where(r => r.Status == "Cancelled").ToList();
        Assert.Equal(2, cancelled.Count);
        Assert.All(cancelled, r => Assert.Equal(ReminderReconciler.Rescheduled, r.CancellationReason));

        // Running reconciliation again changes nothing (idempotent).
        await factory.InScopeAsync(async sp =>
        {
            await sp.GetRequiredService<ReminderReconciler>().ReconcileAsync(Domain.Enums.ReminderSourceType.Task, task.Id, Now, Ct);
            return 0;
        });
        Assert.Equal(4, (await RemindersAsync(memberClient, task.Id)).Count);

        // Completing the task cancels what is left.
        await admin.PatchAsJsonAsync($"/api/v1/tasks/{task.Id}/status", new ChangeStatusRequest("Completed"), Ct);
        await ProcessOutboxAsync(Now);
        Assert.Empty(await RemindersAsync(memberClient, task.Id, "Pending"));
    }

    /// <summary>Phase 6 acceptance criterion: rescheduled releases update their reminders.</summary>
    [Fact]
    public async Task Rescheduled_and_postponed_releases_update_their_reminders()
    {
        var admin = await factory.AdminClientAsync();
        var (owner, ownerClient) = await factory.UserClientAsync(SystemRoles.TeamMember);
        await SetPreferencesAsync(ownerClient, false, 60);
        var show = await (await admin.PostAsJsonAsync("/api/v1/shows", new CreateShowRequest("Reminder show", null, "OneOff", null, null), Ct))
            .DataAsync<ShowResponse>();
        var episode = await (await admin.PostAsJsonAsync($"/api/v1/shows/{show.Id}/episodes",
            new CreateEpisodeRequest("Special", null, null, null, null, null, null, null, null, null), Ct)).DataAsync<EpisodeResponse>();
        var channel = (await (await admin.GetAsync("/api/v1/distribution-channels", Ct)).DataAsync<List<DistributionChannelResponse>>())
            .Single(c => c.Name == "YouTube").Id;
        var at = Now.AddDays(5).AddTicks(-(Now.Ticks % TimeSpan.TicksPerSecond));
        var release = await (await admin.PostAsJsonAsync("/api/v1/releases",
            new CreateReleaseRequest(episode.Id, channel, at, "Scheduled", owner.Id), Ct)).DataAsync<ReleaseResponse>();
        await ProcessOutboxAsync(Now);
        Assert.Equal(at.AddHours(-1), Assert.Single(await RemindersAsync(ownerClient, release.Id, "Pending")).ScheduledAt);

        var moved = at.AddDays(1);
        await admin.PatchAsJsonAsync($"/api/v1/releases/{release.Id}/schedule", new RescheduleReleaseRequest(moved), Ct);
        await ProcessOutboxAsync(Now);
        Assert.Equal(moved.AddHours(-1), Assert.Single(await RemindersAsync(ownerClient, release.Id, "Pending")).ScheduledAt);

        // Postponed until further notice: no time, so no pending reminders.
        await admin.PatchAsJsonAsync($"/api/v1/releases/{release.Id}/postpone", new PostponeReleaseRequest("Waiting for approval"), Ct);
        await ProcessOutboxAsync(Now);
        Assert.Empty(await RemindersAsync(ownerClient, release.Id, "Pending"));
        Assert.Equal(2, (await RemindersAsync(ownerClient, release.Id, "Cancelled")).Count);
    }

    // ---------- acceptance: reminders work when desktop clients are closed ----------

    [Fact]
    public async Task Reminders_are_delivered_by_the_server_in_app_and_by_email_with_no_client_open()
    {
        var admin = await factory.AdminClientAsync();
        var (member, memberClient) = await factory.UserClientAsync(SystemRoles.TeamMember);
        await SetPreferencesAsync(memberClient, true, 60);
        var due = Now.AddHours(3);
        var task = await TaskForAsync(admin, member.Id, due, "Upload the masters");
        await ProcessOutboxAsync(Now);
        var pending = await RemindersAsync(memberClient, task.Id, "Pending");
        Assert.Equal(["Email", "InApp"], pending.Select(r => r.Channel).Order());

        // No client involved from here: the server's jobs fire at the reminder time.
        var fireAt = due.AddHours(-1).AddSeconds(1);
        await DispatchAsync(fireAt);
        await ProcessOutboxAsync(fireAt);

        var notifications = await (await memberClient.GetAsync("/api/v1/notifications?unreadOnly=true", Ct))
            .DataAsync<PagedResponse<NotificationResponse>>();
        var notification = Assert.Single(notifications.Items, n => n.SourceId == task.Id);
        Assert.Equal(("Reminder: Upload the masters", "Reminder", "Task"), (notification.Title, notification.NotificationType, notification.SourceType));
        Assert.StartsWith("Task due ", notification.Message);
        Assert.Contains(factory.Email.Sent, m => m.To == member.Email && m.Subject == "Reminder: Upload the masters");
        Assert.All(await RemindersAsync(memberClient, task.Id), r => Assert.Equal("Delivered", r.Status));

        Assert.Equal(1, (await (await memberClient.GetAsync("/api/v1/notifications/unread-count", Ct)).DataAsync<UnreadCountResponse>()).Count);
        Assert.Equal(HttpStatusCode.NoContent, (await memberClient.PatchAsync($"/api/v1/notifications/{notification.Id}/read", null, Ct)).StatusCode);
        Assert.Equal(0, (await (await memberClient.GetAsync("/api/v1/notifications/unread-count", Ct)).DataAsync<UnreadCountResponse>()).Count);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PatchAsync($"/api/v1/notifications/{notification.Id}/read", null, Ct)).StatusCode);
    }

    // ---------- acceptance: failed notifications are recorded ----------

    [Fact]
    public async Task Failed_emails_are_retried_with_backoff_and_then_recorded_as_failed()
    {
        var admin = await factory.AdminClientAsync();
        var (member, memberClient) = await factory.UserClientAsync(SystemRoles.TeamMember);
        await SetPreferencesAsync(memberClient, true, 60);
        var due = Now.AddHours(3);
        var task = await TaskForAsync(admin, member.Id, due, "Approve the cut");
        await ProcessOutboxAsync(Now);
        factory.Email.FailFor(member.Email, "SMTP server refused the connection");
        try
        {
            var at = due.AddHours(-1).AddSeconds(1);
            await DispatchAsync(at);
            await ProcessOutboxAsync(at);
            var email = Assert.Single(await RemindersAsync(memberClient, task.Id), r => r.Channel == "Email");
            Assert.Equal(("Processing", 1, "SMTP server refused the connection"), (email.Status, email.RetryCount, email.LastError));

            // Retries follow the back-off schedule until the limit, then the failure is final and visible.
            foreach (var delay in new[] { 1, 5, 15, 60 })
            {
                at = at.AddMinutes(delay).AddSeconds(1);
                await ProcessOutboxAsync(at);
            }
            email = Assert.Single(await RemindersAsync(memberClient, task.Id), r => r.Channel == "Email");
            Assert.Equal(("Failed", 5), (email.Status, email.RetryCount));
            Assert.Contains(await RemindersAsync(memberClient, task.Id, "Failed"), r => r.Id == email.Id);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DofDbContext>();
            var outbox = await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.IdempotencyKey == $"email:reminder:{email.Id}", Ct);
            Assert.Equal((Domain.Enums.OutboxStatus.Failed, 5), (outbox.Status, outbox.RetryCount));
            // The in-app reminder was unaffected.
            Assert.Equal("Delivered", Assert.Single(await RemindersAsync(memberClient, task.Id), r => r.Channel == "InApp").Status);
        }
        finally
        {
            factory.Email.StopFailing(member.Email);
        }
    }

    // ---------- acceptance: duplicate notifications are prevented ----------

    [Fact]
    public async Task Concurrent_dispatchers_and_repeated_jobs_never_notify_twice()
    {
        var admin = await factory.AdminClientAsync();
        var (member, memberClient) = await factory.UserClientAsync(SystemRoles.TeamMember);
        await SetPreferencesAsync(memberClient, false, 15);
        var due = Now.AddHours(2);
        var task = await TaskForAsync(admin, member.Id, due, "Check the audio");
        await ProcessOutboxAsync(Now);

        var at = due.AddMinutes(-15).AddSeconds(1);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => DispatchAsync(at)));
        await DispatchAsync(at.AddMinutes(1));

        var notifications = (await (await memberClient.GetAsync("/api/v1/notifications?pageSize=100", Ct))
            .DataAsync<PagedResponse<NotificationResponse>>()).Items;
        Assert.Single(notifications, n => n.SourceId == task.Id && n.NotificationType == "Reminder");
    }

    [Fact]
    public async Task Overdue_alerts_and_escalations_are_sent_once_per_due_time_and_recorded()
    {
        var admin = await factory.AdminClientAsync();
        var (member, memberClient) = await factory.UserClientAsync(SystemRoles.TeamMember);
        var (manager, managerClient) = await factory.UserClientAsync(SystemRoles.ProductionManager);
        var task = await TaskForAsync(admin, member.Id, Now.AddHours(-30), "Deliver captions");

        await factory.InScopeAsync(sp => sp.GetRequiredService<OverdueDetector>().RunAsync(Now, Ct));
        await factory.InScopeAsync(sp => sp.GetRequiredService<OverdueDetector>().RunAsync(Now.AddMinutes(15), Ct));
        var mine = (await (await memberClient.GetAsync("/api/v1/notifications?pageSize=100", Ct)).DataAsync<PagedResponse<NotificationResponse>>()).Items;
        Assert.Single(mine, n => n.SourceId == task.Id && n.NotificationType == "Overdue");

        // Overdue for longer than 24 hours: escalated to production managers, once, and recorded on the task.
        await factory.InScopeAsync(sp => sp.GetRequiredService<EscalationJob>().RunAsync(Now, Ct));
        await factory.InScopeAsync(sp => sp.GetRequiredService<EscalationJob>().RunAsync(Now.AddHours(1), Ct));
        var managerNotes = (await (await managerClient.GetAsync("/api/v1/notifications?pageSize=100", Ct))
            .DataAsync<PagedResponse<NotificationResponse>>()).Items;
        var escalation = Assert.Single(managerNotes, n => n.SourceId == task.Id);
        Assert.Equal(("Escalation", "Escalated: Deliver captions"), (escalation.NotificationType, escalation.Title));
        var history = await (await admin.GetAsync($"/api/v1/tasks/{task.Id}/history", Ct)).DataAsync<List<ActivityEntryResponse>>();
        Assert.Single(history, h => h.Action == "Escalated" && h.Summary.Contains("Test User"));

        // Rescheduled and missed again: a new alert for the new due time.
        task = await RescheduleAsync(admin, task, Now.AddMinutes(-5));
        await factory.InScopeAsync(sp => sp.GetRequiredService<OverdueDetector>().RunAsync(Now, Ct));
        mine = (await (await memberClient.GetAsync("/api/v1/notifications?pageSize=100", Ct)).DataAsync<PagedResponse<NotificationResponse>>()).Items;
        Assert.Equal(2, mine.Count(n => n.SourceId == task.Id && n.NotificationType == "Overdue"));
    }

    // ---------- personal reminders and preferences ----------

    [Fact]
    public async Task People_add_change_and_cancel_their_own_reminders()
    {
        var admin = await factory.AdminClientAsync();
        var (member, memberClient) = await factory.UserClientAsync(SystemRoles.TeamMember);
        var (_, other) = await factory.UserClientAsync(SystemRoles.TeamMember);
        await SetPreferencesAsync(memberClient, false);
        var due = Now.AddDays(4).AddTicks(-(Now.Ticks % TimeSpan.TicksPerSecond));
        var task = await TaskForAsync(admin, member.Id, due);

        var created = await memberClient.PostAsJsonAsync("/api/v1/reminders",
            new CreateReminderRequest("Task", task.Id, 4320, null), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var reminder = await created.DataAsync<ReminderResponse>();
        Assert.Equal((due.AddDays(-3), false, 4320), (reminder.ScheduledAt, reminder.IsAutomatic, reminder.OffsetMinutes!.Value));
        Assert.Equal(HttpStatusCode.Conflict, (await memberClient.PostAsJsonAsync("/api/v1/reminders",
            new CreateReminderRequest("Task", task.Id, 4320, null), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await memberClient.PostAsJsonAsync("/api/v1/reminders",
            new CreateReminderRequest("Task", task.Id, 60, Now.AddDays(1)), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await memberClient.PostAsJsonAsync("/api/v1/reminders",
            new CreateReminderRequest("Task", task.Id, null, Now.AddHours(-1)), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await memberClient.PostAsJsonAsync("/api/v1/reminders",
            new CreateReminderRequest("Task", Guid.NewGuid(), 60, null), Ct)).StatusCode);

        // A personal offset reminder follows the task when it moves.
        await RescheduleAsync(admin, task, due.AddDays(1));
        await ProcessOutboxAsync(Now);
        var personal = Assert.Single(await RemindersAsync(memberClient, task.Id, "Pending"));
        Assert.Equal(due.AddDays(-2), personal.ScheduledAt);

        // Change to a fixed time, then cancel; other people cannot see or touch it.
        var fixedAt = Now.AddDays(2).AddTicks(-(Now.Ticks % TimeSpan.TicksPerSecond));
        var changed = await (await memberClient.PutAsJsonAsync($"/api/v1/reminders/{personal.Id}",
            new UpdateReminderRequest(null, fixedAt, "InApp", personal.Version), Ct)).DataAsync<ReminderResponse>();
        Assert.Equal((fixedAt, (int?)null), (changed.ScheduledAt, changed.OffsetMinutes));
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/v1/reminders/{personal.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await memberClient.DeleteAsync($"/api/v1/reminders/{personal.Id}", Ct)).StatusCode);
        Assert.Equal("Cancelled", (await (await memberClient.GetAsync($"/api/v1/reminders/{personal.Id}", Ct)).DataAsync<ReminderResponse>()).Status);

        // Preferences are validated and returned.
        Assert.Equal(HttpStatusCode.BadRequest, (await memberClient.PutAsJsonAsync("/api/v1/notification-preferences",
            new UpdateNotificationPreferencesRequest(false, true, true, [1, 2, 3, 4, 5, 6, 7]), Ct)).StatusCode);
        var prefs = await (await memberClient.GetAsync("/api/v1/notification-preferences", Ct)).DataAsync<NotificationPreferencesResponse>();
        Assert.True(prefs.EmailAvailable);
        Assert.Empty(prefs.ReminderOffsets);
    }

    [Fact]
    public async Task Hangfire_registers_the_recurring_jobs_in_postgresql()
    {
        await using var hosted = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("BackgroundJobs:Enabled", "true");
            b.UseSetting("BackgroundJobs:RunServer", "false");
        });
        var storage = hosted.Services.GetRequiredService<JobStorage>();
        using var connection = storage.GetConnection();
        var ids = connection.GetRecurringJobs().Select(j => j.Id).ToList();
        Assert.Equal(["maintenance", "overdue-detection", "overdue-escalation", "reminders-dispatch"], ids.Order());
    }
}
