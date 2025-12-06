using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
using OpenFXC.Sem;

namespace OpenFXC.Sem.Tests;

public class ResourceSemanticsTests
{
    [Fact]
    public void Captures_cbuffer_members_and_structured_resources()
    {
        const string source = @"
cbuffer PerFrame : register(b0)
{
    float4x4 World;
    float4 Color;
};
StructuredBuffer<float4> Input : register(t0);
RWStructuredBuffer<float4> Output : register(u0);

float4 main(uint id : SV_DispatchThreadID) : SV_Target
{
    return float4(1, 1, 1, 1);
}";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "cs_5_0"));
        var symbols = doc.RootElement.GetProperty("symbols").EnumerateArray().ToList();

        Assert.Contains(symbols, s => s.GetProperty("kind").GetString() == "CBuffer" && s.GetProperty("name").GetString() == "PerFrame");
        Assert.Contains(symbols, s => s.GetProperty("kind").GetString() == "CBufferMember" && s.GetProperty("name").GetString() == "World");
        Assert.Contains(symbols, s => s.GetProperty("kind").GetString() == "CBufferMember" && s.GetProperty("name").GetString() == "Color");

        Assert.Contains(symbols, s => s.GetProperty("kind").GetString() == "Resource" && s.GetProperty("name").GetString() == "Input" && (s.GetProperty("type").GetString() ?? string.Empty).StartsWith("StructuredBuffer", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(symbols, s => s.GetProperty("kind").GetString() == "Resource" && s.GetProperty("name").GetString() == "Output" && (s.GetProperty("type").GetString() ?? string.Empty).StartsWith("RWStructuredBuffer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Captures_cbuffer_in_sm4()
    {
        const string source = @"
cbuffer PerFrame : register(b0)
{
    float4x4 World;
    float4 Color;
};

float4 main(float4 p : SV_Position) : SV_Position
{
    return mul(World, p) + Color;
}";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "vs_4_0"));
        var symbols = doc.RootElement.GetProperty("symbols").EnumerateArray().ToList();

        Assert.Contains(symbols, s => s.GetProperty("kind").GetString() == "CBuffer" && s.GetProperty("name").GetString() == "PerFrame");
        Assert.Contains(symbols, s => s.GetProperty("kind").GetString() == "CBufferMember" && s.GetProperty("name").GetString() == "World");
        Assert.Contains(symbols, s => s.GetProperty("kind").GetString() == "CBufferMember" && s.GetProperty("name").GetString() == "Color");
    }

    [Fact]
    public void Captures_structured_and_rw_structured_in_sm5()
    {
        var hlslPath = RepoPath("tests", "snapshots", "sm5_structured.hlsl");
        var astJson = ParseHelper.BuildAstJsonFromPath(hlslPath);
        BuildHelper.EnsureBuilt();

        using var doc = JsonDocument.Parse(RunAnalyze(astJson, "cs_5_0"));
        var symbols = doc.RootElement.GetProperty("symbols").EnumerateArray().ToList();

        Assert.Contains(symbols, s => s.GetProperty("kind").GetString() == "Resource"
            && s.GetProperty("name").GetString() == "Input"
            && (s.GetProperty("type").GetString() ?? string.Empty).StartsWith("StructuredBuffer", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(symbols, s => s.GetProperty("kind").GetString() == "Resource"
            && s.GetProperty("name").GetString() == "Output"
            && (s.GetProperty("type").GetString() ?? string.Empty).StartsWith("RWStructuredBuffer", StringComparison.OrdinalIgnoreCase));
    }

    private static string RunAnalyze(string astJson, string profile)
    {
        var analyzer = new SemanticAnalyzer(profile, "main", astJson);
        var output = analyzer.Analyze();
        return JsonSerializer.Serialize(output, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }

    private static string RunParseThenAnalyzeSource(string source, string profile)
    {
        BuildHelper.EnsureBuilt();
        var astJson = ParseHelper.BuildAstJson(source, "inline.hlsl");
        var analyzer = new SemanticAnalyzer(profile, "main", astJson);
        var output = analyzer.Analyze();
        return JsonSerializer.Serialize(output, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
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
