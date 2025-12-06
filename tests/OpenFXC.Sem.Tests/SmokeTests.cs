using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace OpenFXC.Sem.Tests;

public class SmokeTests
{
    [Fact]
    public void Analyze_outputs_function_and_parameter_symbols()
    {
        var semJson = RunParseThenAnalyze(@"samples/dxsdk/DXSDK_Aug08/DXSDK/Samples/C++/Direct3D/StateManager/snow.fx", profile: "vs_2_0");

        using var doc = JsonDocument.Parse(semJson);
        var root = doc.RootElement;

        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal("vs_2_0", root.GetProperty("profile").GetString());

        Assert.True(root.TryGetProperty("syntax", out var syntax));
        Assert.True(syntax.GetProperty("rootId").GetInt32() > 0);

        Assert.True(root.TryGetProperty("symbols", out var symbolsEl));
        Assert.Equal(JsonValueKind.Array, symbolsEl.ValueKind);

        Assert.True(root.TryGetProperty("types", out var types));
        Assert.Equal(JsonValueKind.Array, types.ValueKind);

        Assert.True(root.TryGetProperty("entryPoints", out var entryPoints));
        Assert.Equal(JsonValueKind.Array, entryPoints.ValueKind);

        Assert.True(root.TryGetProperty("diagnostics", out var diagnostics));
        Assert.Equal(JsonValueKind.Array, diagnostics.ValueKind);

        var symbols = root.GetProperty("symbols").EnumerateArray().ToList();
        Assert.True(symbols.Count >= 2);

        var func = symbols.First(s => s.GetProperty("kind").GetString() == "Function" && s.GetProperty("name").GetString() == "VS");
        Assert.Contains("void(", func.GetProperty("type").GetString() ?? string.Empty);

        var param = symbols.First(s => s.GetProperty("kind").GetString() == "Parameter"
            && s.GetProperty("name").GetString() == "pos");
        Assert.Equal("float3", param.GetProperty("type").GetString());
        Assert.Equal(func.GetProperty("id").GetInt32(), param.GetProperty("parentSymbolId").GetInt32());

        var semantic = param.GetProperty("semantic");
        Assert.Equal("POSITION", semantic.GetProperty("name").GetString());
        Assert.Equal(0, semantic.GetProperty("index").GetInt32());
    }

    [Fact]
    public void Analyze_outputs_globals_struct_and_sampler_symbols()
    {
        var semJson = RunParseThenAnalyze(@"samples/dxsdk/DXSDK_Aug08/DXSDK/Samples/C++/Direct3D/StateManager/snow.fx", profile: "vs_2_0");

        using var doc = JsonDocument.Parse(semJson);
        var symbols = doc.RootElement.GetProperty("symbols").EnumerateArray().ToList();

        Assert.Contains(symbols, s => s.GetProperty("kind").GetString() == "GlobalVariable"
            && s.GetProperty("name").GetString() == "lightDir"
            && s.GetProperty("type").GetString() == "float3");

        Assert.Contains(symbols, s => s.GetProperty("kind").GetString() == "Resource"
            && s.GetProperty("name").GetString() == "Texture0");
    }

    private static string RunParseThenAnalyze(string hlslRelativePath, string profile)
    {
        var repoRoot = RepoPath();
        var hlslPath = Path.Combine(repoRoot, hlslRelativePath);

        var parsePsi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{RepoPath("openfxc-hlsl", "src", "openfxc-hlsl", "openfxc-hlsl.csproj")}\" parse -i \"{hlslPath}\"",
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

        var tempAstPath = Path.Combine(Path.GetTempPath(), $"openfxc-sem-test-{Guid.NewGuid():N}.ast.json");
        File.WriteAllText(tempAstPath, astJson);

        var semPsi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{RepoPath("src", "openfxc-sem", "openfxc-sem.csproj")}\" analyze --profile {profile} --input \"{tempAstPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var semProc = Process.Start(semPsi) ?? throw new InvalidOperationException("Failed to start openfxc-sem analyze.");
        var semOut = semProc.StandardOutput.ReadToEnd();
        var semErr = semProc.StandardError.ReadToEnd();
        semProc.WaitForExit();
        Assert.True(semProc.ExitCode == 0, $"openfxc-sem analyze failed with {semProc.ExitCode}. stderr: {semErr}");

        try
        {
            return semOut;
        }
        finally
        {
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
