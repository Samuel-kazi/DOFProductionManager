using DOF.Domain.Entities;

namespace DOF.UnitTests;

public class EntityTests
{
    private sealed class SampleEntity : Entity;

    [Fact]
    public void New_entities_get_distinct_version7_ids()
    {
        var first = new SampleEntity();
        var second = new SampleEntity();

        Assert.NotEqual(Guid.Empty, first.Id);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(7, first.Id.Version);
    }
}
