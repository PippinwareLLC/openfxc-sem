using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenFXC.Hlsl;

namespace OpenFXC.Sem.Tests;

internal static class ParseHelper
{
    private const int FormatVersion = 1;

    public static string BuildAstJsonFromPath(string path)
    {
        var source = File.ReadAllText(path);
        var fileName = Path.GetFileName(path);
        return BuildAstJson(source, fileName);
    }

    public static string BuildAstJson(string source, string fileName = "stdin")
    {
        var (tokens, lexDiagnostics) = HlslLexer.Lex(source);
        var (root, parseDiagnostics) = Parser.Parse(tokens, source.Length);
        var allDiagnostics = lexDiagnostics.Concat(parseDiagnostics).ToArray();

        var parseResult = new ParseResult(
            FormatVersion,
            new SourceInfo(fileName, source.Length),
            root,
            tokens,
            allDiagnostics);

        return JsonSerializer.Serialize(parseResult, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
}
