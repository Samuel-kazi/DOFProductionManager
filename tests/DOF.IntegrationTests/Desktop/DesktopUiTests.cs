using System.Net.Http.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DOF.Contracts.Requests;
using DOF.Desktop.Services.Api;
using DOF.Desktop.ViewModels;
using DOF.Desktop.ViewModels.Pages;
using DOF.Desktop.Views;
using DOF.Desktop.Views.Pages;
using DOF.Domain.Rules;
using static DOF.IntegrationTests.TestApi;
using static DOF.IntegrationTests.Desktop.Ui;

namespace DOF.IntegrationTests.Desktop;

/// <summary>
/// End-to-end tests of the desktop client: real views and view models in a headless window, talking
/// to the real API. Screenshots are written to <c>bin/…/ui-snapshots</c> for visual review.
/// </summary>
public class DesktopUiTests(DofApiFactory factory, HeadlessSession headless)
{
    [Fact]
    public Task Sign_in_opens_the_shell_and_every_section_can_be_reached() => OnUiThread(async () =>
    {
        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);

        await main.InitializeAsync();
        var login = Assert.IsType<LoginViewModel>(main.Content);
        Assert.Single(Find<LoginView>(window));
        Snapshot(window, "01-login");

        login.Email = DofApiFactory.AdminEmail;
        login.Password = DofApiFactory.AdminPassword;
        await login.SignInCommand.ExecuteAsync(null);

        var shell = await WaitForAsync(() => main.Content as ShellViewModel);
        await WaitForAsync(() => shell.CurrentPage as DashboardViewModel);
        Assert.Single(Find<ShellView>(window));
        Assert.Single(Find<DashboardView>(window));
        Assert.Equal(ConnectionState.Online, shell.Connection.State);
        Assert.Equal(DofApiFactory.AdminEmail, harness.Preferences.Current.LastEmail);
        Snapshot(window, "02-dashboard");

        // Every sidebar entry opens a page and marks itself selected.
        foreach (var item in shell.AllItems)
        {
            await shell.NavigateCommand.ExecuteAsync(item);
            Assert.True(item.IsSelected);
            Assert.Single(shell.AllItems, i => i.IsSelected);
            Assert.Equal(item.Title, shell.CurrentPage!.Title);
        }

        await shell.NavigateCommand.ExecuteAsync(shell.FooterItems.Single(i => i.Title == "Team"));
        var team = Assert.IsType<TeamViewModel>(shell.CurrentPage);
        Assert.False(team.HasError);
        Assert.Contains(team.Members, m => m.Email == DofApiFactory.AdminEmail);
        Render(window);
        Assert.Single(Find<TeamView>(window));
        Snapshot(window, "03-team");

        await shell.NavigateCommand.ExecuteAsync(shell.PrimaryItems.Single(i => i.Title == "Projects"));
        Assert.IsType<PlaceholderPageViewModel>(shell.CurrentPage);
        Snapshot(window, "04-projects-placeholder");

        await shell.OpenSettingsCommand.ExecuteAsync(null);
        Assert.IsType<SettingsViewModel>(shell.CurrentPage);
        Snapshot(window, "05-settings");

        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        await shell.NavigateCommand.ExecuteAsync(shell.FooterItems.Single(i => i.Title == "Team"));
        Snapshot(window, "06-team-dark");
        Avalonia.Application.Current.RequestedThemeVariant = ThemeVariant.Default;

        await shell.SignOutCommand.ExecuteAsync(null);
        await WaitForAsync(() => main.Content as LoginViewModel);
        Assert.Empty(harness.Store.Items);
        window.Close();
    });

    [Fact]
    public Task Keyboard_shortcuts_switch_sections_and_toggle_the_sidebar() => OnUiThread(async () =>
    {
        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);
        var shell = await SignInAsync(main);
        var modifier = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

        window.KeyPress(Key.D3, modifier, PhysicalKey.Digit3, null);
        await WaitForAsync(() => shell.CurrentPage?.Title == "Projects" ? shell : null);

        window.KeyPress(Key.B, modifier, PhysicalKey.B, null);
        Assert.True(shell.IsSidebarCollapsed);
        Assert.True(harness.Preferences.Current.SidebarCollapsed);
        Snapshot(window, "07-sidebar-collapsed");

        window.KeyPress(Key.OemComma, modifier, PhysicalKey.Comma, null);
        await WaitForAsync(() => shell.CurrentPage as SettingsViewModel);
        window.Close();
    });

    [Fact]
    public Task Wrong_password_is_reported_on_the_sign_in_screen() => OnUiThread(async () =>
    {
        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);
        await main.InitializeAsync();
        var login = Assert.IsType<LoginViewModel>(main.Content);

        login.Email = DofApiFactory.AdminEmail;
        login.Password = "definitely-wrong";
        await login.SignInCommand.ExecuteAsync(null);

        Assert.Equal("Invalid email or password.", login.ErrorMessage);
        Assert.IsType<LoginViewModel>(main.Content);
        Snapshot(window, "08-login-error");
        window.Close();
    });

    [Fact]
    public Task A_stored_session_is_restored_on_startup() => OnUiThread(async () =>
    {
        var store = new InMemorySecureStore();
        using (var first = new DesktopHarness(factory, store))
        {
            await SignInAsync(first.Get<MainWindowViewModel>());
        }

        using var restarted = new DesktopHarness(factory, store);
        var main = restarted.Get<MainWindowViewModel>();
        await main.InitializeAsync();

        var shell = await WaitForAsync(() => main.Content as ShellViewModel);
        Assert.Equal(DofApiFactory.AdminEmail, shell.UserEmail);
    });

    [Fact]
    public Task An_unreachable_server_shows_offline_and_keeps_the_stored_session() => OnUiThread(async () =>
    {
        var store = new InMemorySecureStore();
        store.Set(AuthSession.RefreshTokenKey, "kept-for-later");
        using var offline = DesktopHarness.Offline(store);
        var main = offline.Get<MainWindowViewModel>();

        await main.InitializeAsync();

        var login = Assert.IsType<LoginViewModel>(main.Content);
        Assert.Equal(ConnectionState.Offline, offline.Get<ConnectionMonitor>().State);
        Assert.Equal("kept-for-later", store.Get(AuthSession.RefreshTokenKey));

        login.Email = "someone@test.local";
        login.Password = "whatever-password";
        await login.SignInCommand.ExecuteAsync(null);
        Assert.Contains("Can't reach the DOF server", login.ErrorMessage);
    });

    [Fact]
    public Task A_deactivated_user_is_returned_to_sign_in() => OnUiThread(async () =>
    {
        var user = await factory.CreateUserAsync(SystemRoles.TeamMember);
        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        await main.InitializeAsync();
        var login = Assert.IsType<LoginViewModel>(main.Content);
        login.Email = user.Email;
        login.Password = user.Password;
        await login.SignInCommand.ExecuteAsync(null);
        var shell = await WaitForAsync(() => main.Content as ShellViewModel);

        var admin = await factory.AdminClientAsync();
        (await admin.PatchAsJsonAsync($"/api/v1/users/{user.Id}/status", new UpdateUserStatusRequest(false), Ct))
            .EnsureSuccessStatusCode();

        // The next API call is rejected; the app ends the session and shows sign-in.
        await shell.NavigateCommand.ExecuteAsync(shell.FooterItems.Single(i => i.Title == "Team"));
        await WaitForAsync(() => main.Content as LoginViewModel);
        Assert.Empty(harness.Store.Items);
    });

    [Fact]
    public Task Changing_password_from_settings_keeps_the_user_signed_in() => OnUiThread(async () =>
    {
        var user = await factory.CreateUserAsync(SystemRoles.TeamMember);
        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        await main.InitializeAsync();
        var login = (LoginViewModel)main.Content!;
        login.Email = user.Email;
        login.Password = user.Password;
        await login.SignInCommand.ExecuteAsync(null);
        var shell = await WaitForAsync(() => main.Content as ShellViewModel);
        await shell.OpenSettingsCommand.ExecuteAsync(null);
        var settings = Assert.IsType<SettingsViewModel>(shell.CurrentPage);

        settings.CurrentPassword = "wrong-current-password";
        settings.NewPassword = settings.ConfirmPassword = "Brand-New-Password-1";
        await settings.ChangePasswordCommand.ExecuteAsync(null);
        Assert.False(settings.PasswordChanged);
        Assert.Equal("The current password is incorrect.", settings.PasswordMessage);

        settings.CurrentPassword = user.Password;
        settings.NewPassword = "Brand-New-Password-1";
        settings.ConfirmPassword = "Does-Not-Match-1";
        await settings.ChangePasswordCommand.ExecuteAsync(null);
        Assert.Equal("The new passwords don't match.", settings.PasswordMessage);

        settings.ConfirmPassword = "Brand-New-Password-1";
        await settings.ChangePasswordCommand.ExecuteAsync(null);
        Assert.True(settings.PasswordChanged);
        Assert.Same(shell, main.Content);
        await factory.LoginAsync(user.Email, "Brand-New-Password-1");

        settings.SelectedTheme = DOF.Desktop.Services.ThemePreference.Dark;
        Assert.Equal(DOF.Desktop.Services.ThemePreference.Dark, harness.Theme.Applied);
        Assert.Equal(DOF.Desktop.Services.ThemePreference.Dark, harness.Preferences.Current.Theme);
    });

    private Task OnUiThread(Func<Task> test) => Ui.Run(headless, test);
}
