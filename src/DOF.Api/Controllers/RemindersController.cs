using DOF.Application.Reminders;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using Microsoft.AspNetCore.Mvc;

namespace DOF.Api.Controllers;

/// <summary>
/// The signed-in person's reminders, notifications and notification preferences. Reminders are created
/// and delivered on the server, so they work with every desktop client closed.
/// </summary>
[ApiController]
[Route("api/v1")]
public sealed class RemindersController(ReminderService reminders, NotificationService notifications) : ControllerBase
{
    /// <summary>My reminders, upcoming first. Filters: <c>statuses</c>, <c>sourceType</c>, <c>sourceId</c>.</summary>
    [HttpGet("reminders")]
    public async Task<ApiResponse<PagedResponse<ReminderResponse>>> List(
        [FromQuery] string[]? statuses, [FromQuery] string? sourceType, [FromQuery] Guid? sourceId,
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        ApiResponse<PagedResponse<ReminderResponse>>.Ok(await reminders.ListAsync(statuses, sourceType, sourceId, page, pageSize, ct));

    /// <summary>Adds a personal reminder: <c>offsetMinutes</c> before the record's time, or at a fixed time <c>at</c>.</summary>
    [HttpPost("reminders")]
    [ProducesResponseType<ApiResponse<ReminderResponse>>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<ReminderResponse>>> Create(CreateReminderRequest request, CancellationToken ct)
    {
        var created = await reminders.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, ApiResponse<ReminderResponse>.Ok(created));
    }

    [HttpGet("reminders/{id:guid}")]
    public async Task<ApiResponse<ReminderResponse>> Get(Guid id, CancellationToken ct) =>
        ApiResponse<ReminderResponse>.Ok(await reminders.GetAsync(id, ct));

    [HttpPut("reminders/{id:guid}")]
    public async Task<ApiResponse<ReminderResponse>> Update(Guid id, UpdateReminderRequest request, CancellationToken ct) =>
        ApiResponse<ReminderResponse>.Ok(await reminders.UpdateAsync(id, request, ct));

    /// <summary>Cancels a pending reminder (kept as cancelled).</summary>
    [HttpDelete("reminders/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await reminders.CancelAsync(id, ct);
        return NoContent();
    }

    [HttpGet("notification-preferences")]
    public async Task<ApiResponse<NotificationPreferencesResponse>> Preferences(CancellationToken ct) =>
        ApiResponse<NotificationPreferencesResponse>.Ok(await reminders.GetPreferencesAsync(ct));

    /// <summary>Channels, overdue alerts and default reminder offsets; upcoming reminders are recalculated.</summary>
    [HttpPut("notification-preferences")]
    public async Task<ApiResponse<NotificationPreferencesResponse>> UpdatePreferences(
        UpdateNotificationPreferencesRequest request, CancellationToken ct) =>
        ApiResponse<NotificationPreferencesResponse>.Ok(await reminders.UpdatePreferencesAsync(request, ct));

    [HttpGet("notifications")]
    public async Task<ApiResponse<PagedResponse<NotificationResponse>>> Notifications(
        [FromQuery] bool unreadOnly, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        ApiResponse<PagedResponse<NotificationResponse>>.Ok(await notifications.ListAsync(unreadOnly, page, pageSize, ct));

    [HttpGet("notifications/unread-count")]
    public async Task<ApiResponse<UnreadCountResponse>> UnreadCount(CancellationToken ct) =>
        ApiResponse<UnreadCountResponse>.Ok(await notifications.UnreadCountAsync(ct));

    [HttpPatch("notifications/{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await notifications.MarkReadAsync(id, ct);
        return NoContent();
    }

    [HttpPatch("notifications/read-all")]
    public async Task<ApiResponse<UnreadCountResponse>> MarkAllRead(CancellationToken ct) =>
        ApiResponse<UnreadCountResponse>.Ok(await notifications.MarkAllReadAsync(ct));
}
