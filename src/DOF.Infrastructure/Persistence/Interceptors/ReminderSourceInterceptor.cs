using DOF.Application.Reminders;
using DOF.Domain.Entities;
using DOF.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DOF.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Whenever a record that reminders depend on is added, changed or removed (or its people change), queues
/// a "source changed" message in the outbox within the same transaction. The outbox worker then
/// recalculates that record's reminders, so no code path can move a date and leave stale reminders.
/// </summary>
public sealed class ReminderSourceInterceptor(TimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var sources = new HashSet<(ReminderSourceType, Guid)>();
        foreach (var entry in context.ChangeTracker.Entries()
                     .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            (ReminderSourceType Type, Guid Id)? source = entry.Entity switch
            {
                WorkTask t => (ReminderSourceType.Task, t.Id),
                TaskAssignment a => (ReminderSourceType.Task, a.TaskId),
                ProjectMilestone m => (ReminderSourceType.Milestone, m.Id),
                CalendarEvent e => (ReminderSourceType.CalendarEvent, e.Id),
                CalendarEventAssignment a => (ReminderSourceType.CalendarEvent, a.CalendarEventId),
                Episode e => (ReminderSourceType.Episode, e.Id),
                EpisodeAssignment a => (ReminderSourceType.Episode, a.EpisodeId),
                Release r => (ReminderSourceType.Release, r.Id),
                _ => null,
            };
            if (source is { } s)
            {
                sources.Add(s);
            }
        }

        var now = clock.GetUtcNow();
        foreach (var (type, id) in sources)
        {
            context.Add(new OutboxMessage(OutboxMessage.SourceChanged, OutboxProcessor.SourceChanged(type, id),
                $"source:{type}:{id}:{Guid.CreateVersion7()}", now));
        }
    }
}
