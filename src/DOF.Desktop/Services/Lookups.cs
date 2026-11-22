using DOF.Contracts.Common;
using DOF.Contracts.Responses;
using DOF.Desktop.Services.Api;

namespace DOF.Desktop.Services;

/// <summary>Look-ups used by pickers: active people and open (non-archived) projects.</summary>
public sealed class Lookups(ApiClient api)
{
    public async Task<IReadOnlyList<UserResponse>> PeopleAsync() =>
        (await api.GetAsync<PagedResponse<UserResponse>>("api/v1/users?pageSize=100&isActive=true")).Items;

    public async Task<IReadOnlyList<ProjectSummaryResponse>> OpenProjectsAsync() =>
        (await api.GetAsync<PagedResponse<ProjectSummaryResponse>>("api/v1/projects?pageSize=100")).Items;
}
