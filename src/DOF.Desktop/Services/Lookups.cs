using DOF.Contracts.Common;
using DOF.Contracts.Responses;
using DOF.Desktop.Services.Api;

namespace DOF.Desktop.Services;

/// <summary>Look-ups used by pickers: active people, open (non-archived) projects and open episodes.</summary>
public sealed class Lookups(ApiClient api)
{
    public async Task<IReadOnlyList<UserResponse>> PeopleAsync() =>
        (await api.GetAsync<PagedResponse<UserResponse>>("api/v1/users?pageSize=100&isActive=true")).Items;

    public async Task<IReadOnlyList<ProjectSummaryResponse>> OpenProjectsAsync() =>
        (await api.GetAsync<PagedResponse<ProjectSummaryResponse>>("api/v1/projects?pageSize=100")).Items;

    public async Task<IReadOnlyList<EpisodeSummaryResponse>> OpenEpisodesAsync() =>
        (await api.GetAsync<PagedResponse<EpisodeSummaryResponse>>("api/v1/episodes?open=true&pageSize=100")).Items;

    public async Task<IReadOnlyList<ShowSummaryResponse>> ShowsAsync() =>
        (await api.GetAsync<PagedResponse<ShowSummaryResponse>>("api/v1/shows?pageSize=100")).Items;

    public static string EpisodeLabel(EpisodeSummaryResponse e) => $"{e.ShowName} {e.Code}".TrimEnd() + $": {e.Title}";
}
