using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DOF.Contracts.Common;
using DOF.Contracts.Responses;
using DOF.Desktop.Services.Api;

namespace DOF.Desktop.ViewModels.Pages;

public sealed record TeamMemberRow(Guid Id, string FullName, string Email, string Roles, bool IsActive, string Initials)
{
    public string StatusText => IsActive ? "Active" : "Deactivated";
}

/// <summary>The team directory, loaded from <c>GET /api/v1/users</c>.</summary>
public sealed partial class TeamViewModel(ApiClient api) : PageViewModel
{
    private const int PageSize = 100;

    public override string Title => "Team";

    public ObservableCollection<TeamMemberRow> Members { get; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IncludeDeactivated { get; set; }

    [ObservableProperty]
    public partial string SummaryText { get; private set; } = string.Empty;

    public bool IsEmpty => !IsLoading && !HasError && Members.Count == 0;

    public override Task LoadAsync() => SearchAsync();

    partial void OnIncludeDeactivatedChanged(bool value) => _ = SearchAsync();

    [RelayCommand]
    private async Task SearchAsync()
    {
        var query = $"api/v1/users?pageSize={PageSize}";
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query += $"&search={Uri.EscapeDataString(SearchText.Trim())}";
        }
        if (!IncludeDeactivated)
        {
            query += "&isActive=true";
        }

        await RunAsync(async () =>
        {
            var page = await api.GetAsync<PagedResponse<UserResponse>>(query);
            Members.Clear();
            foreach (var user in page.Items)
            {
                Members.Add(new TeamMemberRow(user.Id, user.FullName, user.Email,
                    string.Join(", ", user.Roles.Select(r => r.Name)), user.IsActive, Initials(user.FullName)));
            }

            SummaryText = page.TotalCount > page.Items.Count
                ? $"Showing {page.Items.Count} of {page.TotalCount} people — refine the search to see others"
                : page.TotalCount == 1 ? "1 person" : $"{page.TotalCount} people";
        });
        OnPropertyChanged(nameof(IsEmpty));
    }

    public static string Initials(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant(),
        };
    }
}
