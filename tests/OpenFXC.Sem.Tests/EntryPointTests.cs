using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
using OpenFXC.Sem;

namespace OpenFXC.Sem.Tests;

public class EntryPointTests
{
    [Fact]
    public void Missing_entry_reports_diagnostic()
    {
        var source = @"
float4 VSMain(float4 p : POSITION0) : SV_Position { return p; }";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "vs_2_0"));
        var diagnostics = doc.RootElement.GetProperty("diagnostics").EnumerateArray();
        Assert.Contains(diagnostics, d => d.GetProperty("id").GetString() == "HLSL3001");
        var entries = doc.RootElement.GetProperty("entryPoints").EnumerateArray().ToList();
        Assert.NotEmpty(entries);
    }

    [Fact]
    public void Entry_point_resolves_stage()
    {
        var source = @"
float4 main(float4 p : POSITION0) : SV_Position { return p; }";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "ps_2_0"));
        var entry = doc.RootElement.GetProperty("entryPoints").EnumerateArray().First();
        Assert.Equal("Pixel", entry.GetProperty("stage").GetString());
    }

    [Fact]
    public void Semantics_are_normalized_and_return_semantic_present()
    {
        var source = @"
float4 main(float4 pos : position0) : sv_target { return pos; }";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "ps_2_0"));
        var symbols = doc.RootElement.GetProperty("symbols").EnumerateArray().ToList();
        var func = symbols.First(s => s.GetProperty("kind").GetString() == "Function");
        var param = symbols.First(s => s.GetProperty("kind").GetString() == "Parameter");

        Assert.Equal("SV_TARGET", func.GetProperty("returnSemantic").GetProperty("name").GetString());
        Assert.Equal("POSITION", param.GetProperty("semantic").GetProperty("name").GetString());
    }

    [Fact]
    public void System_value_semantics_blocked_before_sm4()
    {
        var source = @"
float4 main(float4 pos : POSITION0) : SV_Target { return pos; }";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "vs_2_0"));
        var diagnostics = doc.RootElement.GetProperty("diagnostics").EnumerateArray();
        Assert.Contains(diagnostics, d => d.GetProperty("id").GetString() == "HLSL3002");
    }

    [Fact]
    public void Duplicate_semantics_on_entry_parameters_reported()
    {
        var source = @"
float4 main(float4 a : POSITION0, float4 b : POSITION0) : SV_Position { return a + b; }";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "vs_2_0"));
        var diagnostics = doc.RootElement.GetProperty("diagnostics").EnumerateArray();
        Assert.Contains(diagnostics, d => d.GetProperty("id").GetString() == "HLSL3003");
    }

    [Fact]
    public void Missing_semantics_on_entry_param_reported()
    {
        var source = @"
float4 main(float4 a, float4 b : POSITION0) : SV_Position { return a + b; }";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "vs_2_0"));
        var diagnostics = doc.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();
        var diag = diagnostics.First(d => d.GetProperty("id").GetString() == "HLSL3004");
        var span = diag.GetProperty("span");
        Assert.True(span.GetProperty("start").GetInt32() >= 0);
        Assert.True(span.GetProperty("end").GetInt32() >= span.GetProperty("start").GetInt32());
    }

    [Fact]
    public void Pixel_shader_invalid_legacy_semantic_reports_error()
    {
        var source = @"
float4 main(float2 uv : TEXCOORD0) : POSITION { return float4(uv, 0, 1); }";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "ps_2_0"));
        var diagnostics = doc.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();
        Assert.Contains(diagnostics, d => d.GetProperty("id").GetString() == "HLSL3002");
    }

    [Fact]
    public void Sm2_pixel_shader_sv_semantics_report_error()
    {
        var source = @"
float4 main(float4 pos : SV_POSITION) : SV_Target { return pos; }";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "ps_2_0"));
        var diagnostics = doc.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();
        Assert.Contains(diagnostics, d => d.GetProperty("id").GetString() == "HLSL3002");
    }

    [Fact]
    public void Sm4_vertex_must_return_sv_position()
    {
        var source = @"
float4 main(float4 p : POSITION) : SV_Target { return p; }";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "vs_4_0"));
        var diagnostics = doc.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();
        Assert.Contains(diagnostics, d => d.GetProperty("id").GetString() == "HLSL3002");
    }

    [Fact]
    public void Sm4_pixel_return_sv_position_reports_error()
    {
        var source = @"
float4 main(float2 uv : SV_Position) : SV_Position { return float4(uv, 0, 1); }";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "ps_4_0"));
        var diagnostics = doc.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();
        Assert.Contains(diagnostics, d => d.GetProperty("id").GetString() == "HLSL3002");
    }

    [Fact]
    public void Sm4_pixel_return_sv_target_is_allowed()
    {
        var source = @"
float4 main(float2 uv : SV_Position) : SV_Target { return float4(uv, 0, 1); }";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "ps_4_0"));
        var diagnostics = doc.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();
        Assert.DoesNotContain(diagnostics, d => d.GetProperty("id").GetString() == "HLSL3002");
    }

    [Fact]
    public void Fx_technique_emits_diagnostic()
    {
        var source = @"
float4 main(float4 p : POSITION) : SV_Position { return p; }

technique T {
    pass P {
        VertexShader = compile vs_2_0 main();
    }
};";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "vs_2_0"));
        var diagnostics = doc.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();
        Assert.Contains(diagnostics, d => d.GetProperty("id").GetString() == "HLSL5001");
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
