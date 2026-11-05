using System.Xml.Linq;
using Xunit;

namespace FleetOps.Tests;

internal static class ModuleArchitectureRules
{
    // AD-24: the only ordering used by the rule. Earlier modules may depend on later modules.
    public static readonly string[] Ad24Order = ["Views", "Alerts", "Payroll", "Shifts", "Schedule", "Readiness", "Documents", "Absences", "Maintenance", "Fleet", "Identity", "People", "Setup", "Audit", "Notifications"];

    public static IReadOnlyList<string> Validate(IReadOnlyDictionary<string, IReadOnlyCollection<string>> graph)
    {
        var errors = new List<string>();
        var rank = Ad24Order.Select((name, index) => (name, index)).ToDictionary(x => x.name, x => x.index);
        foreach (var (source, dependencies) in graph)
            foreach (var dependency in dependencies)
            {
                if (dependency.Contains("/Domain/", StringComparison.OrdinalIgnoreCase) || dependency.Contains("\\Domain\\", StringComparison.OrdinalIgnoreCase) || dependency.Contains("/Infrastructure/", StringComparison.OrdinalIgnoreCase) || dependency.Contains("\\Infrastructure\\", StringComparison.OrdinalIgnoreCase))
                    errors.Add($"{source} references a forbidden module internal: {dependency}");
                var target = Ad24Order.FirstOrDefault(name => dependency.Contains($"/{name}/", StringComparison.OrdinalIgnoreCase) || dependency.Contains($"\\{name}\\", StringComparison.OrdinalIgnoreCase) || dependency.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (target is not null && rank.TryGetValue(source, out var sourceRank) && rank[target] <= sourceRank)
                    errors.Add($"{source} depends on {target}, violating AD-24 order");
            }
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in graph.Keys) Visit(node, graph, visiting, visited, errors, []);
        return errors;
    }

    private static void Visit(string node, IReadOnlyDictionary<string, IReadOnlyCollection<string>> graph, HashSet<string> visiting, HashSet<string> visited, List<string> errors, List<string> path)
    {
        if (visited.Contains(node)) return;
        if (!visiting.Add(node)) { errors.Add($"Module dependency cycle detected: {string.Join(" -> ", path.Append(node))}"); return; }
        path.Add(node);
        foreach (var dependency in graph.GetValueOrDefault(node, []))
        {
            var target = graph.Keys.FirstOrDefault(name => dependency.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (target is not null) Visit(target, graph, visiting, visited, errors, path);
        }
        path.RemoveAt(path.Count - 1); visiting.Remove(node); visited.Add(node);
    }
}

public class ModuleBoundaryTests
{
    [Fact]
    public void Modules_have_the_spine_internals_and_follow_ad24()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var graph = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);
        var layers = XDocument.Load(Path.Combine(root, "src", "Modules", "Directory.Build.props")).Descendants("Folder").Select(x => (string?)x.Attribute("Include") ?? "").ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in ModuleArchitectureRules.Ad24Order)
        {
            var path = Path.Combine(root, "src", "Modules", name, $"{name}.csproj");
            Assert.True(File.Exists(path), $"Missing module project: {name}");
            graph[name] = XDocument.Load(path).Descendants("ProjectReference").Select(x => (string?)x.Attribute("Include") ?? "").ToArray();
            Assert.All(new[] { "Contracts\\", "Api\\", "Application\\", "Domain\\", "Infrastructure\\" }, layer => Assert.Contains(layer, layers));
        }
        Assert.Empty(ModuleArchitectureRules.Validate(graph));
    }

    [Fact]
    public void Reports_layer_break()
    {
        var graph = new Dictionary<string, IReadOnlyCollection<string>> { ["Views"] = ["../Shifts/Domain/Shifts.Domain.csproj"] };
        Assert.Contains(ModuleArchitectureRules.Validate(graph), error => error.Contains("forbidden", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Reports_wrong_order_and_cycle()
    {
        var graph = new Dictionary<string, IReadOnlyCollection<string>> { ["Schedule"] = ["Views"], ["Views"] = ["Schedule"] };
        var errors = ModuleArchitectureRules.Validate(graph);
        Assert.Contains(errors, error => error.Contains("order", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("cycle", StringComparison.OrdinalIgnoreCase));
    }
}
