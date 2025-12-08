using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
using OpenFXC.Sem;

namespace OpenFXC.Sem.Tests;

public class SmokeTests
{
    [Fact]
    public void Analyze_outputs_function_and_parameter_symbols()
    {
        var semJson = RunParseThenAnalyze(@"samples/sm2/vs_passthrough/main.hlsl", profile: "vs_2_0");

        using var doc = JsonDocument.Parse(semJson);
        var root = doc.RootElement;

        Assert.Equal(3, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal("vs_2_0", root.GetProperty("profile").GetString());

        Assert.True(root.TryGetProperty("syntax", out var syntax));
        Assert.True(syntax.GetProperty("rootId").GetInt32() > 0);

        Assert.True(root.TryGetProperty("symbols", out var symbolsEl));
        Assert.Equal(JsonValueKind.Array, symbolsEl.ValueKind);

        Assert.True(root.TryGetProperty("types", out var types));
        Assert.Equal(JsonValueKind.Array, types.ValueKind);

        Assert.True(root.TryGetProperty("entryPoints", out var entryPoints));
        Assert.Equal(JsonValueKind.Array, entryPoints.ValueKind);
        var firstEntry = entryPoints.EnumerateArray().FirstOrDefault();
        Assert.False(firstEntry.ValueKind == JsonValueKind.Undefined);
        Assert.Equal("Vertex", firstEntry.GetProperty("stage").GetString());

        Assert.True(root.TryGetProperty("diagnostics", out var diagnostics));
        Assert.Equal(JsonValueKind.Array, diagnostics.ValueKind);
        AssertNoErrors(diagnostics, "samples/sm2/vs_passthrough/main.hlsl");

        var symbols = root.GetProperty("symbols").EnumerateArray().ToList();
        Assert.True(symbols.Count >= 2);

        var func = symbols.First(s => s.GetProperty("kind").GetString() == "Function" && s.GetProperty("name").GetString() == "main");
        Assert.Contains("float4(", func.GetProperty("type").GetString() ?? string.Empty);

        var param = symbols.First(s => s.GetProperty("kind").GetString() == "Parameter"
            && s.GetProperty("name").GetString() == "pos");
        Assert.Equal("float4", param.GetProperty("type").GetString());
        Assert.Equal(func.GetProperty("id").GetInt32(), param.GetProperty("parentSymbolId").GetInt32());

        var semantic = param.GetProperty("semantic");
        Assert.Equal("POSITION", semantic.GetProperty("name").GetString());
        Assert.Equal(0, semantic.GetProperty("index").GetInt32());

        var typeList = root.GetProperty("types").EnumerateArray().Select(t => t.GetProperty("type").GetString()).ToList();
        Assert.Contains("float4", typeList);
        Assert.Contains("float4", typeList);
        Assert.Contains("float4x4", typeList);
    }

    [Fact]
    public void Analyze_outputs_globals_struct_and_sampler_symbols()
    {
        var semJson = RunParseThenAnalyze(@"samples/sm2/vs_passthrough/main.hlsl", profile: "vs_2_0");

        using var doc = JsonDocument.Parse(semJson);
        var symbols = doc.RootElement.GetProperty("symbols").EnumerateArray().ToList();

        Assert.Contains(symbols, s => s.GetProperty("kind").GetString() == "GlobalVariable"
            && s.GetProperty("name").GetString() == "WorldViewProj"
            && s.GetProperty("type").GetString() == "float4x4");

        Assert.Contains(symbols, s => s.GetProperty("kind").GetString() == "Sampler"
            && s.GetProperty("name").GetString() == "DiffuseSampler");
    }

    [Fact]
    public void Analyze_infers_types_beyond_symbols_on_small_sample()
    {
        var semJson = RunParseThenAnalyze(@"samples/sm2/vs_passthrough/main.hlsl", profile: "vs_2_0");

        using var doc = JsonDocument.Parse(semJson);
        var root = doc.RootElement;
        var symbols = root.GetProperty("symbols").EnumerateArray().ToList();
        var types = root.GetProperty("types").EnumerateArray().ToList();

        Assert.True(types.Count > symbols.Count, "Expected more type entries than symbols (expression typing present).");
        Assert.Contains(types, t => (t.GetProperty("type").GetString() ?? string.Empty).Contains("float4"));
        Assert.Contains(types, t => (t.GetProperty("type").GetString() ?? string.Empty).Contains("float4x4"));
        AssertNoErrors(root.GetProperty("diagnostics"), "vs_passthrough");
    }

    [Theory]
    [MemberData(nameof(FxSamplePaths))]
    public void Analyze_parses_all_dx_sdk_fx_samples(string relativePath)
    {
        Console.WriteLine($"Parsing/analyzing: {relativePath}");
        var astJson = BuildAstJson(relativePath);
        if (IsIncludeOnlyAst(astJson))
        {
            Console.WriteLine($"Skipping include-only FX (no techniques/passes): {relativePath}");
            return;
        }

        var semJson = AnalyzeAstJson(astJson, profile: "vs_2_0");
        using var doc = JsonDocument.Parse(semJson);
        var root = doc.RootElement;

        Assert.Equal(3, root.GetProperty("formatVersion").GetInt32());
        Assert.True(root.GetProperty("symbols").ValueKind == JsonValueKind.Array);
        Assert.True(root.GetProperty("syntax").GetProperty("rootId").GetInt32() > 0);
        AssertNoErrors(root.GetProperty("diagnostics"), relativePath);
    }

    public static IEnumerable<object[]> FxSamplePaths()
    {
        // Default: single representative DXSDK sample to keep runs fast.
        yield return new object[] { @"samples/sm2/vs_passthrough/main.hlsl" };

        // Optional sweep: set OPENFXC_SEM_FX_SWEEP=all to walk every .fx under samples/dxsdk.
        var sweep = Environment.GetEnvironmentVariable("OPENFXC_SEM_FX_SWEEP");
        if (string.Equals(sweep, "all", StringComparison.OrdinalIgnoreCase))
        {
            var root = RepoPath("samples", "dxsdk");
            if (Directory.Exists(root))
            {
                foreach (var path in Directory.GetFiles(root, "*.fx", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(RepoPath(), path);
                    yield return new object[] { relative };
                }
            }
        }
    }

    private static string RunParseThenAnalyze(string hlslRelativePath, string profile)
    {
        var astJson = BuildAstJson(hlslRelativePath);
        return AnalyzeAstJson(astJson, profile);
    }

    private static string BuildAstJson(string hlslRelativePath)
    {
        BuildHelper.EnsureBuilt();

        var repoRoot = RepoPath();
        var hlslPath = Path.Combine(repoRoot, hlslRelativePath);
        return ParseHelper.BuildAstJsonFromPath(hlslPath);
    }

    private static string AnalyzeAstJson(string astJson, string profile)
    {
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

    private static void AssertNoErrors(JsonElement diagnostics, string context)
    {
        var errors = diagnostics.EnumerateArray()
            .Where(d => string.Equals(d.GetProperty("severity").GetString(), "Error", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(errors.Count == 0, $"{context} produced error diagnostics: {string.Join(", ", errors.Select(e => e.GetProperty("id").GetString()))}");
    }

    private static bool IsIncludeOnlyAst(string astJson)
    {
        using var doc = JsonDocument.Parse(astJson);
        if (!doc.RootElement.TryGetProperty("root", out var root))
        {
            return false;
        }

        var stack = new Stack<JsonElement>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            var kind = node.GetProperty("kind").GetString();
            if (string.Equals(kind, "TechniqueDeclaration", StringComparison.OrdinalIgnoreCase)
                || string.Equals(kind, "Technique10Declaration", StringComparison.OrdinalIgnoreCase)
                || string.Equals(kind, "PassDeclaration", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in children.EnumerateArray())
                {
                    if (child.TryGetProperty("node", out var childNode))
                    {
                        stack.Push(childNode);
                    }
                }
            }
        }

        return true;
    }
}
