using DOF.Domain.Enums;
using DOF.Domain.Exceptions;

namespace DOF.Domain.Entities;

/// <summary>A place episodes are published (YouTube, Spotify, television...). Deactivated, never deleted.</summary>
public sealed class DistributionChannel : AuditableEntity
{
    private DistributionChannel() { }

    public string Name { get; private set; } = null!;
    public ChannelType ChannelType { get; private set; }
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }

    public static DistributionChannel Create(string name, ChannelType type, string? description)
    {
        var channel = new DistributionChannel { IsActive = true };
        channel.Update(name, type, description, true);
        return channel;
    }

    public void Update(string name, ChannelType type, string? description, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("A channel needs a name.");
        }
        Name = name.Trim();
        ChannelType = type;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        IsActive = isActive;
    }
}
