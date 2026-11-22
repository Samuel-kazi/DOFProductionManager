using DOF.Application.Common;

namespace DOF.UnitTests;

public class OrganisationOptionsTests
{
    [Fact]
    public void Default_timezone_is_Nairobi_and_valid()
    {
        var options = new OrganisationOptions();

        Assert.Equal("Africa/Nairobi", options.DefaultTimeZone);
        Assert.True(options.HasValidTimeZone());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Not/AZone")]
    public void Invalid_timezones_are_rejected(string timeZone)
    {
        var options = new OrganisationOptions { DefaultTimeZone = timeZone };

        Assert.False(options.HasValidTimeZone());
    }

    [Fact]
    public void Nairobi_is_utc_plus_three()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Nairobi");
        var utc = new DateTimeOffset(2026, 12, 1, 7, 0, 0, TimeSpan.Zero);

        var local = TimeZoneInfo.ConvertTime(utc, zone);

        Assert.Equal(TimeSpan.FromHours(3), local.Offset);
        Assert.Equal(10, local.Hour);
    }
}
