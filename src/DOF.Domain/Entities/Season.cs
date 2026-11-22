using DOF.Domain.Enums;
using DOF.Domain.Exceptions;

namespace DOF.Domain.Entities;

/// <summary>A numbered season of a show. Numbers are unique within the show.</summary>
public sealed class Season : AuditableEntity, IVersioned
{
    private Season() { }

    public Guid ShowId { get; private set; }
    public int SeasonNumber { get; private set; }
    public string? Name { get; private set; }
    public DateOnly? StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public SeasonStatus Status { get; private set; }
    public int Version { get; private set; }
    public Show Show { get; private set; } = null!;

    public static Season Create(Guid showId, int seasonNumber, string? name, DateOnly? startDate, DateOnly? endDate)
    {
        var season = new Season { ShowId = showId, Status = SeasonStatus.Planned };
        season.Update(seasonNumber, name, startDate, endDate, SeasonStatus.Planned);
        return season;
    }

    public void Update(int seasonNumber, string? name, DateOnly? startDate, DateOnly? endDate, SeasonStatus status)
    {
        if (seasonNumber < 1)
        {
            throw new DomainException("Season numbers start at 1.");
        }
        if (startDate is not null && endDate is not null && endDate < startDate)
        {
            throw new DomainException("A season cannot end before it starts.");
        }

        SeasonNumber = seasonNumber;
        Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        StartDate = startDate;
        EndDate = endDate;
        Status = status;
    }

    public string DisplayName => Name is null ? $"Season {SeasonNumber}" : $"Season {SeasonNumber}: {Name}";
}
