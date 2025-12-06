using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

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
        var diag = doc.RootElement.GetProperty("diagnostics").EnumerateArray().First(d => d.GetProperty("id").GetString() == "HLSL2005");
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
        Assert.Contains(diagnostics, d => d.GetProperty("id").GetString() == "HLSL2001");
        Assert.Contains(diagnostics, d => d.GetProperty("id").GetString() == "HLSL2002");
        Assert.Contains(diagnostics, d => d.GetProperty("id").GetString() == "HLSL2005");

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
        var hlslPath = Path.Combine(Path.GetTempPath(), $"openfxc-sem-inline-{Guid.NewGuid():N}.hlsl");
        File.WriteAllText(hlslPath, source);

        BuildHelper.EnsureBuilt();

        var tempAstPath = Path.Combine(Path.GetTempPath(), $"openfxc-sem-test-{Guid.NewGuid():N}.ast.json");

        try
        {
            var parsePsi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --no-build --project \"{RepoPath("openfxc-hlsl", "src", "openfxc-hlsl", "openfxc-hlsl.csproj")}\" parse -i \"{hlslPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var parseProc = Process.Start(parsePsi) ?? throw new InvalidOperationException("Failed to start openfxc-hlsl parse.");
            var astJson = parseProc.StandardOutput.ReadToEnd();
            var parseErr = parseProc.StandardError.ReadToEnd();
            parseProc.WaitForExit();
            if (parseProc.ExitCode != 0)
            {
                throw new InvalidOperationException($"openfxc-hlsl parse failed with {parseProc.ExitCode}. stderr: {parseErr}");
            }

            File.WriteAllText(tempAstPath, astJson);

            var semPsi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --no-build --project \"{RepoPath("src", "openfxc-sem", "openfxc-sem.csproj")}\" analyze --profile {profile} --input \"{tempAstPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var semProc = Process.Start(semPsi) ?? throw new InvalidOperationException("Failed to start openfxc-sem analyze.");
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
            if (File.Exists(tempAstPath))
            {
                File.Delete(tempAstPath);
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
