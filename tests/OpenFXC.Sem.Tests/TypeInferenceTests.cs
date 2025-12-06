using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

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

    private static string RunParseThenAnalyzeSource(string source, string profile)
    {
        var hlslPath = Path.Combine(Path.GetTempPath(), $"openfxc-sem-inline-{Guid.NewGuid():N}.hlsl");
        File.WriteAllText(hlslPath, source);

        BuildHelper.EnsureBuilt();

        try
        {
        var astJson = ParseHelper.BuildAstJsonFromPath(hlslPath);

        var semPsi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --no-build --project \"{RepoPath("src", "openfxc-sem", "openfxc-sem.csproj")}\" analyze --profile {profile}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var semProc = Process.Start(semPsi) ?? throw new InvalidOperationException("Failed to start openfxc-sem analyze.");
            semProc.StandardInput.Write(astJson);
            semProc.StandardInput.Close();
            var semOut = semProc.StandardOutput.ReadToEnd();
            var semErr = semProc.StandardError.ReadToEnd();
            semProc.WaitForExit();
            if (semProc.ExitCode != 0)
            {
                throw new InvalidOperationException($"openfxc-sem analyze failed with {semProc.ExitCode}. stderr: {semErr}");
            }

            return semOut;
        }
        finally
        {
            if (File.Exists(hlslPath))
            {
                File.Delete(hlslPath);
            }
        }
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
