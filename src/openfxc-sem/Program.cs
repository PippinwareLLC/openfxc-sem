using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenFXC.Sem;

internal static class Program
{
    private const int InternalErrorExitCode = 1;
    private const int SuccessExitCode = 0;

    public static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch
        {
            // M0: treat any uncaught failure as internal error.
            return InternalErrorExitCode;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length == 0 || !string.Equals(args[0], "analyze", StringComparison.OrdinalIgnoreCase))
        {
            PrintUsage();
            return InternalErrorExitCode;
        }

        var options = ParseOptions(args[1..]);
        if (!options.IsValid(out var error))
        {
            Console.Error.WriteLine(error);
            PrintUsage();
            return InternalErrorExitCode;
        }

        var inputJson = ReadAllInput(options.InputPath);
        var syntaxRootId = TryGetRootId(inputJson);

        var output = new SemanticOutput
        {
            FormatVersion = 1,
            Profile = options.Profile!,
            Syntax = syntaxRootId is null ? null : new SyntaxInfo { RootId = syntaxRootId.Value },
            Symbols = Array.Empty<SymbolInfo>(),
            Types = Array.Empty<TypeInfo>(),
            EntryPoints = Array.Empty<EntryPointInfo>(),
            Diagnostics = Array.Empty<DiagnosticInfo>()
        };

        var writerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        Console.Out.Write(JsonSerializer.Serialize(output, writerOptions));
        Console.Out.WriteLine();

        return SuccessExitCode;
    }

    private static Options ParseOptions(string[] args)
    {
        string? profile = null;
        string? entry = "main";
        string? input = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--profile":
                case "-p":
                    profile = NextValue(args, ref i);
                    break;
                case "--entry":
                case "-e":
                    entry = NextValue(args, ref i);
                    break;
                case "--input":
                case "-i":
                    input = NextValue(args, ref i);
                    break;
                default:
                    break;
            }
        }

        return new Options(profile, entry, input);
    }

    private static string? NextValue(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            return null;
        }
        index++;
        return args[index];
    }

    private static string ReadAllInput(string? inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return Console.In.ReadToEnd();
        }

        return File.ReadAllText(inputPath);
    }

    private static int? TryGetRootId(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("root", out var rootElement))
            {
                if (rootElement.TryGetProperty("id", out var idElement) && idElement.TryGetInt32(out var id))
                {
                    return id;
                }
            }
        }
        catch
        {
            // Swallow parse errors here; diagnostics will be empty in M0.
        }

        return null;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: openfxc-sem analyze --profile <name> [--entry <name>] [--input <path>] < input.ast.json > output.sem.json");
    }

    private sealed record Options(string? Profile, string? Entry, string? InputPath)
    {
        public bool IsValid(out string? error)
        {
            if (string.IsNullOrWhiteSpace(Profile))
            {
                error = "Missing required option: --profile";
                return false;
            }

            error = null;
            return true;
        }
    }

    private sealed record SemanticOutput
    {
        [JsonPropertyName("formatVersion")]
        public int FormatVersion { get; init; }

        [JsonPropertyName("profile")]
        public string? Profile { get; init; }

        [JsonPropertyName("syntax")]
        public SyntaxInfo? Syntax { get; init; }

        [JsonPropertyName("symbols")]
        public IReadOnlyList<SymbolInfo> Symbols { get; init; } = Array.Empty<SymbolInfo>();

        [JsonPropertyName("types")]
        public IReadOnlyList<TypeInfo> Types { get; init; } = Array.Empty<TypeInfo>();

        [JsonPropertyName("entryPoints")]
        public IReadOnlyList<EntryPointInfo> EntryPoints { get; init; } = Array.Empty<EntryPointInfo>();

        [JsonPropertyName("diagnostics")]
        public IReadOnlyList<DiagnosticInfo> Diagnostics { get; init; } = Array.Empty<DiagnosticInfo>();
    }

    private sealed record SyntaxInfo
    {
        [JsonPropertyName("rootId")]
        public int RootId { get; init; }
    }

    private sealed record SymbolInfo;

    private sealed record TypeInfo;

    private sealed record EntryPointInfo;

    private sealed record DiagnosticInfo;
}
