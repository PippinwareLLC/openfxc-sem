using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace OpenFXC.Sem.Tests;

public class SnapshotTests
{
    [Theory]
    [MemberData(nameof(Cases))]
    public void Semantic_output_matches_snapshot(string hlslPath, string profile, string snapshotPath)
    {
        BuildHelper.EnsureBuilt();

        var actual = RunParseThenAnalyze(hlslPath, profile);
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
        Assert.True(parseProc.ExitCode == 0, $"openfxc-hlsl parse failed with {parseProc.ExitCode}. stderr: {parseErr}");

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
        Assert.True(semProc.ExitCode == 0, $"openfxc-sem analyze failed with {semProc.ExitCode}. stderr: {semErr}");

        return semOut;
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
