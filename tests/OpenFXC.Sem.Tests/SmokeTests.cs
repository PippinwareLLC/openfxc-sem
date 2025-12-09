using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
        var profile = DetectProfile(relativePath);
        var astJson = BuildAstJson(relativePath);
        if (IsIncludeOnlyAst(astJson))
        {
            Console.WriteLine($"Skipping include-only FX (no techniques/passes): {relativePath}");
            return;
        }

        var semJson = AnalyzeAstJson(astJson, profile);
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

    private static string DetectProfile(string hlslRelativePath)
    {
        try
        {
            var path = Path.Combine(RepoPath(), hlslRelativePath);
            var source = File.ReadAllText(path);
            var regex = new Regex(@"\b(vs|ps|gs|hs|ds|cs)_(\d)_(\d)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            var matches = regex.Matches(source);
            if (matches.Count == 0)
            {
                return "vs_2_0";
            }

            (int major, int minor, string stage) best = (0, 0, "vs");
            foreach (Match match in matches)
            {
                var stage = match.Groups[1].Value.ToLowerInvariant();
                var major = int.Parse(match.Groups[2].Value);
                var minor = int.Parse(match.Groups[3].Value);
                if (major > best.major || (major == best.major && minor > best.minor))
                {
                    best = (major, minor, stage);
                }
            }

            return $"{best.stage}_{best.major}_{best.minor}";
        }
        catch
        {
            return "vs_2_0";
        }
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
        if (errors.Count > 0)
        {
            var details = errors.Select(e => FormatDiagnostic(e, context)).ToList();
            Assert.Fail($"{context} produced error diagnostics:{Environment.NewLine}{string.Join(Environment.NewLine, details)}");
        }
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

    private static string FormatDiagnostic(JsonElement diag, string context)
    {
        var id = diag.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "unknown" : "unknown";
        var message = diag.TryGetProperty("message", out var msgEl) ? msgEl.GetString() ?? string.Empty : string.Empty;
        var span = diag.TryGetProperty("span", out var spanEl)
            && spanEl.TryGetProperty("start", out var startEl)
            && spanEl.TryGetProperty("end", out var endEl)
            && startEl.TryGetInt32(out var start)
            && endEl.TryGetInt32(out var end)
            ? (Start: start, End: end)
            : (Start: 0, End: 0);

        var (line, column, lineText) = ResolveLineInfo(context, span.Start);
        var caret = column > 1 && lineText.Length >= column - 1
            ? new string(' ', Math.Max(0, column - 1)) + "^"
            : "^";

        return $"{id} @ {line}:{column} {message}\n    {lineText}\n    {caret}";
    }

    private static (int Line, int Column, string LineText) ResolveLineInfo(string relativePath, int offset)
    {
        try
        {
            var repoRoot = RepoPath();
            var path = Path.Combine(repoRoot, relativePath);
            if (!File.Exists(path))
            {
                return (1, offset + 1, string.Empty);
            }

            var text = File.ReadAllText(path);
            var line = 1;
            var lineStart = 0;
            for (var i = 0; i < text.Length && i < offset; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                    lineStart = i + 1;
                }
            }

            var column = Math.Max(1, offset - lineStart + 1);
            var lineEnd = text.IndexOf('\n', lineStart);
            if (lineEnd < 0) lineEnd = text.Length;
            var lineText = text[lineStart..Math.Min(lineEnd, text.Length)].Replace("\r", string.Empty);
            return (line, column, lineText);
        }
        catch
        {
            return (1, offset + 1, string.Empty);
        }
    }
}
