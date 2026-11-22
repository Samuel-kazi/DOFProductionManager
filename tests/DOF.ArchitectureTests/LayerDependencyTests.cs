using System.Reflection;

namespace DOF.ArchitectureTests;

/// <summary>
/// Enforces the dependency boundaries in the technical specification (section 3.1).
/// </summary>
public class LayerDependencyTests
{
    private static readonly Assembly Domain = typeof(DOF.Domain.Entities.Entity).Assembly;
    private static readonly Assembly Application = typeof(DOF.Application.Common.OrganisationOptions).Assembly;
    private static readonly Assembly Contracts = typeof(DOF.Contracts.Common.ApiResponse<>).Assembly;
    private static readonly Assembly Infrastructure = typeof(DOF.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly Desktop = typeof(DOF.Desktop.App).Assembly;

    private static IEnumerable<string> ReferencedNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);

    private static void AssertNoReferenceTo(Assembly assembly, params string[] forbiddenPrefixes)
    {
        var violations = ReferencedNames(assembly)
            .Where(name => forbiddenPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
            .ToList();

        Assert.True(violations.Count == 0,
            $"{assembly.GetName().Name} must not reference: {string.Join(", ", violations)}");
    }

    [Fact]
    public void Domain_depends_on_no_other_layer_or_persistence_framework() =>
        AssertNoReferenceTo(Domain,
            "DOF.Application", "DOF.Infrastructure", "DOF.Api", "DOF.Contracts", "DOF.Desktop", "DOFProductionManager",
            "Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.AspNetCore");

    [Fact]
    public void Application_does_not_depend_on_infrastructure_api_or_desktop() =>
        AssertNoReferenceTo(Application,
            "DOF.Infrastructure", "DOF.Api", "DOF.Desktop", "DOFProductionManager", "Npgsql");

    [Fact]
    public void Contracts_are_self_contained() =>
        AssertNoReferenceTo(Contracts,
            "DOF.Domain", "DOF.Application", "DOF.Infrastructure", "DOF.Api", "DOF.Desktop",
            "Microsoft.EntityFrameworkCore", "Npgsql");

    [Fact]
    public void Infrastructure_does_not_depend_on_api_or_desktop() =>
        AssertNoReferenceTo(Infrastructure, "DOF.Api", "DOF.Desktop", "DOFProductionManager");

    [Fact]
    public void Desktop_never_touches_the_database_or_server_layers() =>
        AssertNoReferenceTo(Desktop,
            "DOF.Domain", "DOF.Application", "DOF.Infrastructure", "DOF.Api",
            "Microsoft.EntityFrameworkCore", "Npgsql");
}
