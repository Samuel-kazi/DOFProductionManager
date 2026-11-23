using DOF.Application.Common.Abstractions;
using DOF.Contracts.Responses;
using DOF.Domain.Entities;
using DOF.Domain.Enums;
using DOF.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace DOF.Application.Reports;

/// <summary>
/// Global search (spec 6.1): projects, shows, episodes, tasks, events and releases whose names contain the
/// text, a few of each, open records first. Every hit opens its original record.
/// </summary>
public sealed class SearchService(IApplicationDbContext db)
{
    public const int PerType = 6;

    public async Task<IReadOnlyList<SearchResultResponse>> SearchAsync(string? text, CancellationToken ct)
    {
        var term = text?.Trim().ToLowerInvariant() ?? string.Empty;
        if (term.Length < 2)
        {
            throw new DomainException("Type at least two characters to search.");
        }
        if (term.Length > 100)
        {
            term = term[..100];
        }

        var results = new List<SearchResultResponse>();
        results.AddRange((await db.Projects.AsNoTracking().Where(p => p.Name.ToLower().Contains(term))
                .OrderBy(p => p.Status == ProjectStatus.Archived).ThenBy(p => p.Name).Take(PerType)
                .Select(p => new { p.Id, p.Name, p.Status }).ToListAsync(ct))
            .Select(p => new SearchResultResponse("Project", p.Id, p.Name, $"Project · {p.Status}")));
        results.AddRange((await db.Shows.AsNoTracking().Where(s => s.Name.ToLower().Contains(term))
                .OrderBy(s => s.Status == ShowStatus.Archived).ThenBy(s => s.Name).Take(PerType)
                .Select(s => new { s.Id, s.Name, s.ShowType }).ToListAsync(ct))
            .Select(s => new SearchResultResponse("Show", s.Id, s.Name, $"Show · {s.ShowType}")));
        results.AddRange((await db.Episodes.AsNoTracking().Where(e => e.Title.ToLower().Contains(term))
                .OrderBy(e => e.Status == EpisodeStatus.Cancelled || e.Status == EpisodeStatus.Published).ThenByDescending(e => e.CreatedAt)
                .Take(PerType)
                .Select(e => new { e.Id, e.Title, Show = e.Show.Name, Season = (int?)e.Season!.SeasonNumber, e.EpisodeNumber, e.Status })
                .ToListAsync(ct))
            .Select(e => new SearchResultResponse("Episode", e.Id, e.Title,
                $"Episode · {$"{e.Show} {Episode.Code(e.Season, e.EpisodeNumber)}".TrimEnd()} · {e.Status}")));
        results.AddRange((await db.Tasks.AsNoTracking().Where(t => t.Title.ToLower().Contains(term))
                .OrderBy(t => t.Status == WorkStatus.Completed || t.Status == WorkStatus.Cancelled).ThenBy(t => t.DueAt == null).ThenBy(t => t.DueAt)
                .Take(PerType)
                .Select(t => new { t.Id, t.Title, t.Status }).ToListAsync(ct))
            .Select(t => new SearchResultResponse("Task", t.Id, t.Title, $"Task · {t.Status}")));
        results.AddRange((await db.CalendarEvents.AsNoTracking().Where(e => e.Title.ToLower().Contains(term))
                .OrderBy(e => e.Status != CalendarEventStatus.Scheduled).ThenByDescending(e => e.StartAt).Take(PerType)
                .Select(e => new { e.Id, e.Title, e.EventType }).ToListAsync(ct))
            .Select(e => new SearchResultResponse("CalendarEvent", e.Id, e.Title, $"Event · {e.EventType}")));
        results.AddRange((await db.Releases.AsNoTracking()
                .Where(r => (r.Episode != null && r.Episode.Title.ToLower().Contains(term))
                            || (r.Title != null && r.Title.ToLower().Contains(term)) || r.Channel.Name.ToLower().Contains(term))
                .OrderBy(r => r.Status == ReleaseStatus.Published || r.Status == ReleaseStatus.Cancelled).ThenBy(r => r.ScheduledAt).Take(PerType)
                .Select(r => new { r.Id, Channel = r.Channel.Name, Title = r.Title ?? r.Episode!.Title, r.Status }).ToListAsync(ct))
            .Select(r => new SearchResultResponse("Release", r.Id, $"{r.Channel} · {r.Title}", $"Release · {r.Status}")));
        return results;
    }
}
