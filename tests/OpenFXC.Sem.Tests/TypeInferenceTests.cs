using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
using OpenFXC.Sem;

namespace OpenFXC.Sem.Tests;

public class TypeInferenceTests
{
    [Fact]
    public void Constructor_with_too_many_components_reports_diagnostic()
    {
        var source = @"
float4 main(float4 p : POSITION0) : SV_Position
{
    float3 v = float3(1, 2, 3, 4);
    return float4(v, 1);
}";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "vs_2_0"));
        var diagnostics = doc.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();

        Assert.Contains(diagnostics, d => d.GetProperty("id").GetString() == "HLSL2001");
    }

    [Fact]
    public void Binary_mismatch_reports_diagnostic()
    {
        var source = @"
float4 main(float2 a : POSITION0) : SV_Position
{
    float3 b = a + float3(1, 2, 3);
    return float4(b, 1);
}";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "vs_2_0"));
        var diagnostics = doc.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();

        Assert.Contains(diagnostics, d => d.GetProperty("id").GetString() == "HLSL2002");
    }

    [Fact]
    public void Array_declaration_preserves_brackets_in_symbol_type()
    {
        var source = @"
float4 arr[2];
float4 main(float4 p : POSITION0) : SV_Position
{
    return arr[0];
}";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "vs_2_0"));
        var symbols = doc.RootElement.GetProperty("symbols").EnumerateArray().ToList();

        Assert.Contains(symbols, s => s.GetProperty("name").GetString() == "arr"
            && s.GetProperty("type").GetString() == "float4[2]");
    }

    [Fact]
    public void Intrinsic_dot_returns_scalar()
    {
        var source = @"
float4 main(float3 a : POSITION0, float3 b : NORMAL0) : SV_Target
{
    float x = dot(a, b);
    return float4(x, x, x, 1);
}";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "vs_2_0"));
        var diagnostics = doc.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();
        Assert.DoesNotContain(diagnostics, d => d.GetProperty("id").GetString() == "HLSL2001");

        var types = doc.RootElement.GetProperty("types").EnumerateArray().ToList();
        Assert.Contains(types, t => t.GetProperty("type").GetString() == "float");
    }

    [Fact]
    public void Intrinsic_tex2D_wrong_arity_reports_error()
    {
        var source = @"
sampler2D S;
float4 main(float2 uv : TEXCOORD0) : SV_Target
{
    return tex2D(S);
}";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "ps_2_0"));
        var diagnostics = doc.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();
        Assert.Contains(diagnostics, d => d.GetProperty("id").GetString() == "HLSL2001");
    }

    [Fact]
    public void Intrinsic_tex2D_type_inference()
    {
        var source = @"
sampler2D S;
float4 main(float2 uv : TEXCOORD0) : SV_Target
{
    return tex2D(S, uv);
}";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "ps_2_0"));
        var diagnostics = doc.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();
        Assert.DoesNotContain(diagnostics, d => d.GetProperty("id").GetString() == "HLSL2001");

        var types = doc.RootElement.GetProperty("types").EnumerateArray().ToList();
        Assert.Contains(types, t => t.GetProperty("type").GetString() == "float4");
    }

    [Fact]
    public void Intrinsic_cross_returns_vector3()
    {
        var source = @"
float4 main(float3 a : POSITION0, float3 b : NORMAL0) : COLOR0
{
    float3 c = cross(a, b);
        return float4(c, 1.0f);
}";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "ps_2_0"));
        var diagnostics = doc.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();
        Assert.DoesNotContain(diagnostics, d => d.GetProperty("id").GetString() == "HLSL2001");

        var types = doc.RootElement.GetProperty("types").EnumerateArray().ToList();
        Assert.Contains(types, t => t.GetProperty("type").GetString() == "float3");
    }

    [Fact]
    public void Intrinsic_length_returns_scalar()
    {
        var source = @"
float4 main(float3 a : POSITION0) : COLOR0
{
    float len = length(a);
    return float4(len, len, len, 1);
}";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "ps_2_0"));
        var diagnostics = doc.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();
        Assert.DoesNotContain(diagnostics, d => d.GetProperty("id").GetString() == "HLSL2001");

        var types = doc.RootElement.GetProperty("types").EnumerateArray().ToList();
        Assert.Contains(types, t => t.GetProperty("type").GetString() == "float");
    }

    [Fact]
    public void Intrinsic_sin_returns_same_shape()
    {
        var source = @"
float4 main(float2 a : TEXCOORD0) : COLOR0
{
    float2 s = sin(a);
    return float4(s, 0, 0);
}";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "ps_2_0"));
        var diagnostics = doc.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();
        Assert.DoesNotContain(diagnostics, d => d.GetProperty("id").GetString() == "HLSL2001");

        var types = doc.RootElement.GetProperty("types").EnumerateArray().ToList();
        Assert.Contains(types, t => t.GetProperty("type").GetString() == "float2");
    }

    [Fact]
    public void Intrinsic_clamp_preserves_shape()
    {
        var source = @"
float4 main(float3 a : TEXCOORD0) : COLOR0
{
    float3 c = clamp(a, float3(0,0,0), float3(1,1,1));
    return float4(c, 1);
}";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "ps_2_0"));
        var diagnostics = doc.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();
        Assert.DoesNotContain(diagnostics, d => d.GetProperty("id").GetString() == "HLSL2001");

        var types = doc.RootElement.GetProperty("types").EnumerateArray().ToList();
        Assert.Contains(types, t => t.GetProperty("type").GetString() == "float3");
    }

    [Fact]
    public void Intrinsic_ddx_preserves_shape_sm4()
    {
        var source = @"
float4 main(float2 uv : TEXCOORD0) : SV_Target
{
    float2 g = ddx(uv);
    return float4(g, 0, 1);
}";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "ps_4_0"));
        var diagnostics = doc.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();
        Assert.DoesNotContain(diagnostics, d => d.GetProperty("id").GetString() == "HLSL2001");

        var types = doc.RootElement.GetProperty("types").EnumerateArray().ToList();
        Assert.Contains(types, t => t.GetProperty("type").GetString() == "float2");
    }

    [Fact]
    public void Intrinsic_tex2D_projected_type_inference()
    {
        var source = @"
sampler2D S;
float4 main(float3 uvw : TEXCOORD0) : SV_Target
{
    return tex2D(S, uvw);
}";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "ps_3_0"));
        var diagnostics = doc.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();
        Assert.DoesNotContain(diagnostics, d => d.GetProperty("id").GetString() == "HLSL2001");

        var types = doc.RootElement.GetProperty("types").EnumerateArray().ToList();
        Assert.Contains(types, t => t.GetProperty("type").GetString() == "float4");
    }

    [Fact]
    public void Intrinsic_tex2D_with_wrong_sampler_reports_error()
    {
        var source = @"
texture2D Tex;
float4 main(float2 uv : TEXCOORD0) : SV_Target
{
    return tex2D(Tex, uv);
}";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "ps_2_0"));
        var diagnostics = doc.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();
        Assert.Contains(diagnostics, d => d.GetProperty("id").GetString() == "HLSL2001");
    }

    [Fact]
    public void Intrinsic_tex2Dlod_returns_float4()
    {
        var source = @"
sampler2D S;
float4 main(float2 uv : TEXCOORD0) : SV_Target
{
    return tex2Dlod(S, float4(uv, 0, 1));
}";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "ps_3_0"));
        var diagnostics = doc.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();
        Assert.DoesNotContain(diagnostics, d => d.GetProperty("id").GetString() == "HLSL2001");

        var types = doc.RootElement.GetProperty("types").EnumerateArray().ToList();
        Assert.Contains(types, t => t.GetProperty("type").GetString() == "float4");
    }

    [Fact]
    public void Intrinsic_tex2Dgrad_returns_float4()
    {
        var source = @"
sampler2D S;
float4 main(float2 uv : TEXCOORD0) : SV_Target
{
    return tex2Dgrad(S, uv, float2(1,0), float2(0,1));
}";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "ps_3_0"));
        var diagnostics = doc.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();
        Assert.DoesNotContain(diagnostics, d => d.GetProperty("id").GetString() == "HLSL2001");

        var types = doc.RootElement.GetProperty("types").EnumerateArray().ToList();
        Assert.Contains(types, t => t.GetProperty("type").GetString() == "float4");
    }

    [Fact]
    public void Intrinsic_overloads_cover_sm3_sample()
    {
        var path = RepoPath("samples", "sm3", "intrinsics_overloads", "main.hlsl");
        var astJson = ParseHelper.BuildAstJsonFromPath(path);
        var analyzer = new SemanticAnalyzer("vs_3_0", "main", astJson);
        var output = analyzer.Analyze();

        Assert.DoesNotContain(output.Diagnostics, d => d.Id == "HLSL2001");
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
