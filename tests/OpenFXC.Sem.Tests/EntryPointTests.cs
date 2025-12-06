using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Xunit;

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
