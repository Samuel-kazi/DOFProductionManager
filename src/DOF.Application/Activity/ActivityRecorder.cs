using System.Text.Json;
using DOF.Application.Common.Abstractions;
using DOF.Domain.Entities;

namespace DOF.Application.Activity;

/// <summary>Entity type names used in the activity log.</summary>
public static class ActivityEntities
{
    public const string Project = "Project";
    public const string Milestone = "Milestone";
    public const string Task = "Task";
    public const string CalendarEvent = "CalendarEvent";
    public const string User = "User";
    public const string Role = "Role";
}

/// <summary>Actions recorded in the activity log.</summary>
public static class ActivityActions
{
    public const string Created = "Created";
    public const string Updated = "Updated";
    public const string StatusChanged = "StatusChanged";
    public const string Rescheduled = "Rescheduled";
    public const string AssigneesChanged = "AssigneesChanged";
    public const string MemberAdded = "MemberAdded";
    public const string MemberRoleChanged = "MemberRoleChanged";
    public const string MemberRemoved = "MemberRemoved";
    public const string Cancelled = "Cancelled";
    public const string RolesChanged = "RolesChanged";
    public const string PermissionsChanged = "PermissionsChanged";
}

/// <summary>
/// Adds activity-log entries to the current unit of work, so a change and its history are saved in the
/// same transaction. Values are stored as JSON objects of the fields that changed.
/// </summary>
public sealed class ActivityRecorder(IApplicationDbContext db, ICurrentUser currentUser)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Record(string entityType, Guid entityId, string action, object? oldValues = null, object? newValues = null) =>
        db.ActivityLogs.Add(new ActivityLog(currentUser.UserId, entityType, entityId, action,
            oldValues is null ? null : JsonSerializer.Serialize(oldValues, Json),
            newValues is null ? null : JsonSerializer.Serialize(newValues, Json)));

    /// <summary>Records an update listing only the fields whose values differ.</summary>
    public void RecordChanges(string entityType, Guid entityId, string action,
        IReadOnlyDictionary<string, object?> before, IReadOnlyDictionary<string, object?> after)
    {
        var changed = after.Keys.Where(k => !Equals(before.GetValueOrDefault(k), after[k])).ToList();
        if (changed.Count > 0)
        {
            Record(entityType, entityId, action,
                changed.ToDictionary(k => k, k => before.GetValueOrDefault(k)),
                changed.ToDictionary(k => k, k => after[k]));
        }
    }
}
