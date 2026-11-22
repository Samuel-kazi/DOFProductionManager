using System.Net;
using System.Text;
using DOF.Desktop.Services;
using DOF.Desktop.Services.Api;
using DOF.Desktop.ViewModels.Pages;

namespace DOF.UnitTests;

public class DesktopClientTests
{
    // Keep test runs out of the real user's desktop log folder.
    static DesktopClientTests() => AppLog.Directory = Path.Combine(Path.GetTempPath(), "dof-desktop-tests", "logs");

    [Theory]
    [InlineData("https://api.dof.org", "https://api.dof.org/")]
    [InlineData("http://localhost:5028", "http://localhost:5028/")]
    [InlineData("http://127.0.0.1:5028/", "http://127.0.0.1:5028/")]
    public void Accepts_https_or_local_api_addresses(string url, string expected) =>
        Assert.Equal(expected, DesktopSettings.FromUrl(url).ApiBaseUrl.ToString());

    [Theory]
    [InlineData("http://api.dof.org")]
    [InlineData("not a url")]
    public void Rejects_insecure_or_invalid_api_addresses(string url) =>
        Assert.Throws<InvalidOperationException>(() => DesktopSettings.FromUrl(url));

    [Fact]
    public void Settings_file_is_read_and_environment_variable_overrides_it()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(dir, "appsettings.json"), """{ "ApiBaseUrl": "https://from-file.example" }""");

        Assert.Equal("https://from-file.example/", DesktopSettings.Load(dir).ApiBaseUrl.ToString());

        Environment.SetEnvironmentVariable(DesktopSettings.ApiUrlEnvironmentVariable, "https://from-env.example");
        try
        {
            Assert.Equal("https://from-env.example/", DesktopSettings.Load(dir).ApiBaseUrl.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(DesktopSettings.ApiUrlEnvironmentVariable, null);
        }
    }

    [Fact]
    public void Preferences_survive_a_restart_and_a_damaged_file_falls_back_to_defaults()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        new PreferencesStore(dir).Update(p => p with { Theme = ThemePreference.Dark, SidebarCollapsed = true });

        var reloaded = new PreferencesStore(dir).Current;
        Assert.Equal(ThemePreference.Dark, reloaded.Theme);
        Assert.True(reloaded.SidebarCollapsed);

        File.WriteAllText(Path.Combine(dir, "preferences.json"), "{ not json");
        Assert.Equal(new UserPreferences(), new PreferencesStore(dir).Current);
    }

    [Theory]
    [InlineData("Grace Wanjiru Otieno", "GO")]
    [InlineData("grace", "G")]
    [InlineData("  ", "?")]
    public void Initials_use_first_and_last_names(string name, string expected) =>
        Assert.Equal(expected, TeamViewModel.Initials(name));

    [Fact]
    public async Task Validation_problems_become_readable_messages_with_field_errors()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                """{"title":"One or more validation errors occurred.","status":400,"errors":{"Email":["'Email' is not a valid email address."]}}""",
                Encoding.UTF8, "application/problem+json"),
        };

        var error = await ApiException.FromResponseAsync(response, CancellationToken.None);

        Assert.Equal("'Email' is not a valid email address.", error.Message);
        Assert.Equal(["'Email' is not a valid email address."], error.FieldErrors["Email"]);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "You don't have permission to do that.")]
    [InlineData(HttpStatusCode.InternalServerError, "The server ran into a problem. Please try again shortly.")]
    [InlineData(HttpStatusCode.Conflict, "This record was modified by someone else.")]
    public async Task Error_statuses_map_to_user_facing_messages(HttpStatusCode status, string expected)
    {
        using var response = new HttpResponseMessage(status)
        {
            Content = new StringContent("""{"title":"Conflict","detail":"This record was modified by someone else."}"""),
        };

        var error = await ApiException.FromResponseAsync(response, CancellationToken.None);

        Assert.Equal(expected, error.Message);
    }
}
