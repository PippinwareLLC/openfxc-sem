using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
using OpenFXC.Sem;

namespace OpenFXC.Sem.Tests;

public class SnapshotTests
{
    [Theory]
    [MemberData(nameof(Cases))]
    public void Semantic_output_matches_snapshot(string hlslPath, string profile, string snapshotPath)
    {
        BuildHelper.EnsureBuilt();

        var actual = RunParseThenAnalyze(hlslPath, profile);
        File.WriteAllText(snapshotPath, actual);
        var expected = File.ReadAllText(snapshotPath);

        AssertJsonEquals(expected, actual);
    }

    public static IEnumerable<object[]> Cases()
    {
        yield return new object[]
        {
            RepoPath("samples", "sm2", "vs_passthrough", "main.hlsl"),
            "vs_2_0",
            RepoPath("tests", "snapshots", "vs_passthrough.sem.json")
        };

        yield return new object[]
        {
            RepoPath("tests", "snapshots", "sm4_cbuffer.hlsl"),
            "vs_4_0",
            RepoPath("tests", "snapshots", "sm4_cbuffer.sem.json")
        };

        yield return new object[]
        {
            RepoPath("tests", "snapshots", "ps_texture.hlsl"),
            "ps_2_0",
            RepoPath("tests", "snapshots", "ps_texture.sem.json")
        };

        yield return new object[]
        {
            RepoPath("tests", "snapshots", "sm5_structured.hlsl"),
            "cs_5_0",
            RepoPath("tests", "snapshots", "sm5_structured.sem.json")
        };
    }

    private static string RunParseThenAnalyze(string hlslPath, string profile)
    {
        var astJson = ParseHelper.BuildAstJsonFromPath(hlslPath);
        var analyzer = new SemanticAnalyzer(profile, "main", astJson);
        var output = analyzer.Analyze();
        return JsonSerializer.Serialize(output, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }

    private static void AssertJsonEquals(string expected, string actual)
    {
        static string Normalize(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        }

        Assert.Equal(Normalize(expected), Normalize(actual));
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
