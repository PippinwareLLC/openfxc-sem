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
