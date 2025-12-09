using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenFXC.Hlsl;

namespace OpenFXC.Sem.Tests;

internal static class ParseHelper
{
    private const int FormatVersion = 1;
    private static readonly Lazy<IReadOnlyList<DefineEntry>> DefineManifest = new(LoadDefineManifest);

    public static string BuildAstJsonFromPath(string path)
    {
        var source = File.ReadAllText(path);
        return BuildAstJson(source, Path.GetFileName(path), path);
    }

    public static string BuildAstJson(string source, string fileName = "stdin", string? filePath = null)
    {
        var defines = ResolveDefines(filePath ?? fileName);
        var includeDirs = ResolveIncludeDirectories(filePath);

        var preOptions = new PreprocessorOptions
        {
            FilePath = filePath ?? fileName,
            IncludeDirectories = includeDirs,
            Defines = defines
        };

        var pre = Preprocessor.Preprocess(source, preOptions);

        var (tokens, lexDiagnostics) = HlslLexer.Lex(pre.Text);
        var (root, parseDiagnostics) = Parser.Parse(tokens, pre.Text.Length);
        var allDiagnostics = pre.Diagnostics.Concat(lexDiagnostics).Concat(parseDiagnostics).ToArray();

        var parseResult = new ParseResult(
            FormatVersion,
            new SourceInfo(fileName, pre.Text.Length),
            root,
            tokens,
            allDiagnostics);

        return JsonSerializer.Serialize(parseResult, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    private static IReadOnlyList<string> ResolveIncludeDirectories(string? filePath)
    {
        var dirs = new List<string>();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return dirs;
        }

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            dirs.Add(dir);
        }

        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var dxsdkRoot = Path.Combine(repoRoot, "samples", "dxsdk");
        var cursor = dir;
        while (!string.IsNullOrEmpty(cursor) && cursor.StartsWith(dxsdkRoot, StringComparison.OrdinalIgnoreCase))
        {
            var configPath = Path.Combine(cursor, "includes.json");
            if (File.Exists(configPath))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var element in doc.RootElement.EnumerateArray())
                        {
                            if (element.ValueKind == JsonValueKind.String)
                            {
                                var rel = element.GetString() ?? string.Empty;
                                if (!string.IsNullOrWhiteSpace(rel))
                                {
                                    var includeDir = Path.GetFullPath(Path.Combine(cursor, rel));
                                    dirs.Add(includeDir);
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // ignore malformed include configs to keep sweeps running
                }
            }

            cursor = Path.GetDirectoryName(cursor);
        }

        return dirs.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyDictionary<string, string?> ResolveDefines(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }

        var normalized = Path.GetFullPath(path);
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var entry in DefineManifest.Value)
        {
            if (string.IsNullOrWhiteSpace(entry.Match))
            {
                continue;
            }

            if (normalized.Contains(entry.Match, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var kvp in entry.Defines)
                {
                    map[kvp.Key] = kvp.Value;
                }
            }
        }

        return map;
    }

    private static IReadOnlyList<DefineEntry> LoadDefineManifest()
    {
        try
        {
            var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
            var manifestPath = Path.Combine(repoRoot, "tests", "data", "defines.json");
            if (!File.Exists(manifestPath))
            {
                return Array.Empty<DefineEntry>();
            }

            var json = File.ReadAllText(manifestPath);
            var entries = JsonSerializer.Deserialize<List<DefineEntry>>(json);
            return entries ?? new List<DefineEntry>();
        }
        catch
        {
            return Array.Empty<DefineEntry>();
        }
    }

    private sealed record DefineEntry(string Match, Dictionary<string, string?> Defines);
}
