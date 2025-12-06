using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
using OpenFXC.Sem;

namespace OpenFXC.Sem.Tests;

public class DiagnosticsSpanTests
{
    [Fact]
    public void Unknown_identifier_diagnostic_has_span()
    {
        var source = @"
float4 main() : SV_Target
{
    return missingThing;
}";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "ps_2_0"));
        var diag = doc.RootElement.GetProperty("diagnostics").EnumerateArray().First();
        var span = diag.GetProperty("span");
        var start = span.GetProperty("start").GetInt32();
        var end = span.GetProperty("end").GetInt32();
        Assert.True(start >= 0);
        Assert.True(end >= start);
    }

    [Fact]
    public void Binary_mismatch_diagnostic_has_span()
    {
        var source = @"
float4 main() : SV_Target
{
    float2 a = 1;
    float3 b = 2;
    return a + b;
}";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "ps_2_0"));
        var diag = doc.RootElement.GetProperty("diagnostics").EnumerateArray().First(d => d.GetProperty("id").GetString() == "HLSL2002");
        var span = diag.GetProperty("span");
        var start = span.GetProperty("start").GetInt32();
        var end = span.GetProperty("end").GetInt32();
        Assert.True(start >= 0);
        Assert.True(end >= start);
    }

    [Fact]
    public void All_diagnostics_include_valid_spans_within_source()
    {
        var source = @"
float4 main(float2 a : POSITION0) : SV_Position
{
    float3 v = float3(1, 2, 3, 4);
    float3 w = a + float3(1, 2, 3);
    return missingThing;
}";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "vs_2_0"));
        var diagnostics = doc.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();
        Assert.True(diagnostics.Count > 0);

        foreach (var diag in diagnostics)
        {
            Assert.True(diag.TryGetProperty("span", out var span));
            var start = span.GetProperty("start").GetInt32();
            var end = span.GetProperty("end").GetInt32();
            Assert.True(start >= 0);
            Assert.True(end >= start);
            Assert.True(end <= source.Length, $"Diagnostic {diag.GetProperty("id").GetString()} end {end} exceeds source length {source.Length}.");
        }
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
}
