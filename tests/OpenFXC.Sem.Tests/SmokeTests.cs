using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace OpenFXC.Sem.Tests;

public class SmokeTests
{
    [Fact]
    public void Analyze_outputs_stub_semantic_model()
    {
        var projectPath = RepoPath("src", "openfxc-sem", "openfxc-sem.csproj");
        var inputPath = RepoPath("tests", "fixtures", "sm1-smoke.ast.json");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{projectPath}\" analyze --profile vs_2_0 --input \"{inputPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start process.");
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        Assert.Equal(0, proc.ExitCode == 0 ? 0 : proc.ExitCode); // ensure explicit evaluation
        Assert.True(proc.ExitCode == 0, $"Process exited with {proc.ExitCode}. stderr: {stderr}");

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;

        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal("vs_2_0", root.GetProperty("profile").GetString());

        Assert.True(root.TryGetProperty("syntax", out var syntax));
        Assert.Equal(1, syntax.GetProperty("rootId").GetInt32());

        Assert.True(root.TryGetProperty("symbols", out var symbols));
        Assert.Equal(JsonValueKind.Array, symbols.ValueKind);

        Assert.True(root.TryGetProperty("types", out var types));
        Assert.Equal(JsonValueKind.Array, types.ValueKind);

        Assert.True(root.TryGetProperty("entryPoints", out var entryPoints));
        Assert.Equal(JsonValueKind.Array, entryPoints.ValueKind);

        Assert.True(root.TryGetProperty("diagnostics", out var diagnostics));
        Assert.Equal(JsonValueKind.Array, diagnostics.ValueKind);
    }

    private static string RepoPath(params string[] parts)
    {
        var baseDir = AppContext.BaseDirectory;
        var repoRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        return Path.Combine(new[] { repoRoot }.Concat(parts).ToArray());
    }
}
