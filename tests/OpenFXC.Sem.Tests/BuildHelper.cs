using System;
using System.Diagnostics;
using Xunit;

namespace OpenFXC.Sem.Tests;

internal static class BuildHelper
{
    private static bool _built;
    private static readonly object _lock = new();

    public static void EnsureBuilt()
    {
        if (_built) return;
        lock (_lock)
        {
            if (_built) return;
            BuildProject(RepoPath("openfxc-hlsl", "src", "openfxc-hlsl", "openfxc-hlsl.csproj"));
            BuildProject(RepoPath("src", "openfxc-sem", "openfxc-sem.csproj"));
            _built = true;
        }
    }

    private static void BuildProject(string projectPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{projectPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start build for {projectPath}.");
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        Assert.True(proc.ExitCode == 0, $"dotnet build failed for {projectPath} with {proc.ExitCode}. stdout: {stdout} stderr: {stderr}");
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
