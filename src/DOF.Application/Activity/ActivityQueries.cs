using System.Linq.Expressions;
using System.Text.Json;
using DOF.Application.Common.Abstractions;
using DOF.Contracts.Responses;
using DOF.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DOF.Application.Activity;

/// <summary>Reads history entries and phrases them for people.</summary>
public sealed class ActivityQueries(IApplicationDbContext db)
{
    public const int MaxEntries = 300;

    public async Task<IReadOnlyList<ActivityEntryResponse>> GetAsync(
        Expression<Func<ActivityLog, bool>> filter, CancellationToken ct)
    {
        var rows = await db.ActivityLogs.AsNoTracking()
            .Where(filter)
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Take(MaxEntries)
            .Select(a => new { Log = a, UserName = db.Users.Where(u => u.Id == a.UserId).Select(u => u.FullName).FirstOrDefault() })
            .ToListAsync(ct);

        return rows.Select(r =>
        {
            var oldValues = Parse(r.Log.OldValues);
            var newValues = Parse(r.Log.NewValues);
            return new ActivityEntryResponse(r.Log.Id, r.Log.EntityType, r.Log.EntityId, r.Log.Action,
                Summarise(r.Log.EntityType, r.Log.Action, oldValues, newValues),
                r.Log.UserId is { } id && r.UserName is { } name ? new UserSummaryResponse(id, name) : null,
                r.Log.CreatedAt, oldValues, newValues);
        }).ToList();
    }

    public Task<IReadOnlyList<ActivityEntryResponse>> ForEntityAsync(string entityType, Guid entityId, CancellationToken ct) =>
        GetAsync(a => a.EntityType == entityType && a.EntityId == entityId, ct);

    private static Dictionary<string, string?> Parse(string? json)
    {
        if (json is null)
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind != JsonValueKind.Object
            ? []
            : document.RootElement.EnumerateObject().ToDictionary(
                p => char.ToUpperInvariant(p.Name[0]) + p.Name[1..],
                p => p.Value.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.String => p.Value.GetString(),
                    JsonValueKind.Array => string.Join(", ", p.Value.EnumerateArray().Select(v => v.ToString())),
                    _ => p.Value.ToString(),
                });
    }

    internal static string Summarise(string entityType, string action, IReadOnlyDictionary<string, string?> old,
        IReadOnlyDictionary<string, string?> @new)
    {
        string Val(IReadOnlyDictionary<string, string?> values, string key) => values.GetValueOrDefault(key) ?? "none";
        var label = entityType switch
        {
            ActivityEntities.CalendarEvent => "Event",
            _ => entityType,
        };

        return action switch
        {
            ActivityActions.Created => $"{label} created",
            ActivityActions.StatusChanged => $"Status changed from {Val(old, "Status")} to {Val(@new, "Status")}"
                                             + (@new.GetValueOrDefault("Note") is { } note ? $": {note}" : ""),
            ActivityActions.Rescheduled => @new.Keys.FirstOrDefault() is { } key
                ? $"{Humanise(key)} changed from {Val(old, key)} to {Val(@new, key)}"
                : "Rescheduled",
            ActivityActions.AssigneesChanged => $"Responsibility changed to {Val(@new, "Assignees")}",
            ActivityActions.MemberAdded when entityType == ActivityEntities.Episode =>
                $"{Val(@new, "Member")} assigned as {Val(@new, "Role")}",
            ActivityActions.MemberAdded => $"{Val(@new, "Member")} joined as {Val(@new, "Role")}",
            ActivityActions.MemberRoleChanged => $"{Val(@new, "Member")}'s role changed from {Val(old, "Role")} to {Val(@new, "Role")}",
            ActivityActions.MemberRemoved when entityType == ActivityEntities.Episode =>
                $"{Val(old, "Member")} removed as {Val(old, "Role")}",
            ActivityActions.MemberRemoved => $"{Val(old, "Member")} left the project",
            ActivityActions.Cancelled => $"{label} cancelled" + (@new.GetValueOrDefault("Reason") is { } why ? $": {why}" : ""),
            ActivityActions.Published => old.ContainsKey("PublishedAt")
                ? $"Publication time corrected from {Val(old, "PublishedAt")} to {Val(@new, "PublishedAt")}"
                : $"Publication confirmed: published {Val(@new, "PublishedAt")} (scheduled {Val(@new, "ScheduledAt")})",
            ActivityActions.Postponed => $"Postponed from {Val(old, "ScheduledAt")} to "
                                         + (@new.GetValueOrDefault("ScheduledAt") ?? "further notice") + $": {Val(@new, "Reason")}",
            ActivityActions.RolesChanged => $"Roles changed to {Val(@new, "Roles")}",
            ActivityActions.PermissionsChanged => $"Permissions set to {Val(@new, "Permissions")}",
            ActivityActions.Escalated => $"Escalated as overdue to {Val(@new, "Notified")}",
            ActivityActions.Updated => $"Updated {string.Join(", ", @new.Keys.Select(Humanise))}",
            _ => action,
        };
    }

    private static string Humanise(string key) => key switch
    {
        "DueAt" or "DueDate" => "Due date",
        "StartAt" => "Start",
        "TargetEndDate" => "Target end date",
        "StartDate" => "Start date",
        "ProjectType" or "ShowType" => "type",
        "RecordingStart" => "Recording",
        "ScheduledAt" => "Release time",
        "ExternalUrl" => "link",
        "AssignedTo" => "assignee",
        "RecordingEnd" => "Recording end",
        "RecordedAt" => "Recorded at",
        "RecordingLocation" => "location",
        "EditingDueDate" => "Editing deadline",
        "ReviewDueDate" => "Review deadline",
        "ApprovalDueDate" => "Approval deadline",
        "EpisodeNumber" => "episode number",
        "SeasonNumber" => "season number",
        "DefaultDurationMinutes" => "default duration",
        "EndDate" => "End date",
        "IsActive" => "active status",
        _ => key.ToLowerInvariant(),
    };
}
