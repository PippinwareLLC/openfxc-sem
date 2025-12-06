using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

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
