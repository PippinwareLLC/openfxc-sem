using System.Text.Json;
using System.Text.RegularExpressions;
using OpenFXC.Hlsl;

var root = args.FirstOrDefault() ?? "samples";
if (!Directory.Exists(root))
{
    Console.Error.WriteLine($"Root directory '{root}' not found.");
    return 1;
}

var pattern = new Regex(@"\[[^\]]*?\b([A-Z][A-Z0-9_]+)\b[^\]]*\]", RegexOptions.Compiled);
var definePattern = new Regex(@"#\s*define\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
var results = new List<ResultItem>();
var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
    .Where(f => f.EndsWith(".fx", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase))
    .ToList();

foreach (var file in files)
{
    string text;
    try
    {
        text = File.ReadAllText(file);
    }
    catch
    {
        continue;
    }

    var includeDir = Path.GetDirectoryName(file);
    var options = new PreprocessorOptions
    {
        FilePath = file,
        IncludeDirectories = string.IsNullOrWhiteSpace(includeDir) ? Array.Empty<string>() : new[] { includeDir }
    };

    PreprocessResult preprocessed;
    try
    {
        preprocessed = Preprocessor.Preprocess(text, options);
    }
    catch
    {
        continue;
    }

    var preText = preprocessed.Text;

    var definedNames = new HashSet<string>(StringComparer.Ordinal);
    foreach (Match defMatch in definePattern.Matches(preText))
    {
        var name = defMatch.Groups[1].Value;
        if (!string.IsNullOrWhiteSpace(name))
        {
            definedNames.Add(name);
        }
    }

    var matches = pattern.Matches(preText);
    if (matches.Count == 0)
    {
        continue;
    }

    var macros = new HashSet<string>(StringComparer.Ordinal);
    foreach (Match match in matches)
    {
        var name = match.Groups[1].Value;
        if (string.IsNullOrWhiteSpace(name))
        {
            continue;
        }

        if (definedNames.Contains(name))
        {
            continue;
        }

        macros.Add(name);
    }

    if (macros.Count > 0)
    {
        results.Add(new ResultItem
        {
            File = Path.GetRelativePath(Directory.GetCurrentDirectory(), file),
            Macros = macros.OrderBy(m => m, StringComparer.Ordinal).ToArray(),
            Diagnostics = preprocessed.Diagnostics.Select(d => d.Id).Distinct().OrderBy(id => id, StringComparer.Ordinal).ToArray()
        });
    }
}

var json = JsonSerializer.Serialize(results, new JsonSerializerOptions
{
    WriteIndented = true
});

Console.Out.WriteLine(json);
return 0;

record ResultItem
{
    public string File { get; init; } = string.Empty;
    public string[] Macros { get; init; } = Array.Empty<string>();
    public string[] Diagnostics { get; init; } = Array.Empty<string>();
}
