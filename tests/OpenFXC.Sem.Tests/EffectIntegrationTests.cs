using System;
using System.IO;
using System.Linq;
using OpenFXC.Sem;
using Xunit;

namespace OpenFXC.Sem.Tests;

public class EffectIntegrationTests
{
    [Fact]
    public void Technique10_compile_shaders_are_bound()
    {
        var path = RepoPath("samples", "fx10", "technique10_basic", "main.fx");
        var astJson = ParseHelper.BuildAstJsonFromPath(path);
        var analyzer = new SemanticAnalyzer("vs_4_0", "VSMain", astJson);
        var output = analyzer.Analyze();

        var technique = Assert.Single(output.Techniques);
        Assert.Equal("BasicTech", technique.Name);

        var pass = Assert.Single(technique.Passes);

        var vs = Assert.Single(pass.Shaders.Where(s => string.Equals(s.Stage, "Vertex", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal("vs_4_0", vs.Profile);
        Assert.Equal(output.Symbols.First(s => s.Name == "VSMain").Id, vs.EntrySymbolId);

        var ps = Assert.Single(pass.Shaders.Where(s => string.Equals(s.Stage, "Pixel", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal("ps_4_0", ps.Profile);
        Assert.Equal(output.Symbols.First(s => s.Name == "PSMain").Id, ps.EntrySymbolId);

        Assert.Contains(pass.States, s => s.Name.Equals("RasterizerState", StringComparison.OrdinalIgnoreCase) && s.Value == "NoCull");
        Assert.Contains(pass.States, s => s.Name.Equals("DepthStencilState", StringComparison.OrdinalIgnoreCase) && s.Value == "DepthOn");

        Assert.Contains(output.Symbols, s => string.Equals(s.Kind, "StateObject", StringComparison.OrdinalIgnoreCase) && s.Name == "NoCull");
        Assert.Contains(output.Symbols, s => string.Equals(s.Kind, "StateObject", StringComparison.OrdinalIgnoreCase) && s.Name == "DepthOn");
    }

    [Fact]
    public void Sm1_profile_is_recorded_without_rejection()
    {
        var path = RepoPath("samples", "sm1", "vs_basic", "main.hlsl");
        var astJson = ParseHelper.BuildAstJsonFromPath(path);
        var analyzer = new SemanticAnalyzer("vs_1_1", "main", astJson);
        var output = analyzer.Analyze();

        var entry = Assert.Single(output.EntryPoints);
        Assert.Equal("Vertex", entry.Stage);
        Assert.Equal("vs_1_1", entry.Profile);
        Assert.Equal("main", entry.Name);
        Assert.DoesNotContain(output.Diagnostics, d => string.Equals(d.Id, "HLSL3001", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Technique_binding_drives_entry_selection()
    {
        var path = RepoPath("samples", "fx9", "technique_entry", "main.fx");
        var astJson = ParseHelper.BuildAstJsonFromPath(path);
        var analyzer = new SemanticAnalyzer("vs_2_0", "main", astJson);
        var output = analyzer.Analyze();

        var entry = Assert.Single(output.EntryPoints);
        Assert.Equal("VSFunc", entry.Name);
        Assert.Equal("Vertex", entry.Stage);
        Assert.Equal("vs_2_0", entry.Profile);

        Assert.DoesNotContain(output.Diagnostics, d => d.Id == "HLSL3001");
        Assert.DoesNotContain(output.Diagnostics, d => d.Id == "HLSL2001");
    }

    private static string RepoPath(params string[] parts)
    {
        var baseDir = AppContext.BaseDirectory;
        var repoRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        return parts.Length == 0
            ? repoRoot
            : Path.Combine(new[] { repoRoot }.Concat(parts).ToArray());
    }
}
