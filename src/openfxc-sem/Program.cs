using System.Linq;
using System.Globalization;
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
        var analyzer = new SemanticAnalyzer(options.Profile!, options.Entry ?? "main", inputJson);
        var output = analyzer.Analyze();

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

    private sealed class SemanticAnalyzer
    {
        private readonly string _profile;
        private readonly string _entry;
        private readonly string _inputJson;
        private readonly TypeInference _typeInference = new();

        public SemanticAnalyzer(string profile, string entry, string inputJson)
        {
            _profile = profile;
            _entry = entry;
            _inputJson = inputJson;
        }

        public SemanticOutput Analyze()
        {
            int? rootId = null;
            List<SymbolInfo> symbols = new();
            List<TypeInfo> types = new();
            List<DiagnosticInfo> diagnostics = new();
            List<EntryPointInfo> entryPoints = new();

            try
            {
                using var doc = JsonDocument.Parse(_inputJson);
                var root = doc.RootElement;
                rootId = TryGetRootId(root);
                var tokens = TokenLookup.From(root);
                if (root.TryGetProperty("root", out var rootNodeEl))
                {
                    var rootNode = NodeInfo.FromJson(rootNodeEl);
                    var build = SymbolBuilder.Build(rootNode, tokens, _typeInference);
                    ExpressionTypeAnalyzer.Infer(rootNode, tokens, build.Symbols, build.Types, _typeInference);
                    entryPoints = EntryPointResolver.Resolve(build.Symbols, _entry, _profile, _typeInference);
                    SemanticValidator.Validate(build.Symbols, entryPoints, _profile, _typeInference);
                    diagnostics.AddRange(_typeInference.Diagnostics);
                    symbols = build.Symbols;
                    types = build.Types;
                }
            }
            catch
            {
                // On parse failure, emit minimal model with empty symbols.
                symbols = new List<SymbolInfo>();
                types = new List<TypeInfo>();
                diagnostics = new List<DiagnosticInfo>();
            }

            return new SemanticOutput
            {
                FormatVersion = 1,
                Profile = _profile,
                Syntax = rootId is null ? null : new SyntaxInfo { RootId = rootId.Value },
                Symbols = symbols,
                Types = types,
                EntryPoints = entryPoints,
                Diagnostics = diagnostics
            };
        }

        private static int? TryGetRootId(JsonElement root)
        {
            if (root.TryGetProperty("root", out var rootElement))
            {
                if (rootElement.TryGetProperty("id", out var idElement) && idElement.TryGetInt32(out var id))
                {
                    return id;
                }
            }

            return null;
        }
    }

    private static class SymbolBuilder
    {
        public static SymbolBuildResult Build(NodeInfo rootNode, TokenLookup tokens, TypeInference typeInference)
        {
            var symbols = new List<SymbolInfo>();
            var typeCollector = new TypeCollector();
            Traverse(rootNode, parentKind: null, currentFunctionId: null, currentStructId: null, symbols, typeCollector, tokens, typeInference);
            return new SymbolBuildResult(
                symbols.OrderBy(s => s.Id ?? int.MaxValue).ToList(),
                typeCollector.ToList());
        }

        private static void Traverse(NodeInfo node, string? parentKind, int? currentFunctionId, int? currentStructId, List<SymbolInfo> symbols, TypeCollector types, TokenLookup tokens, TypeInference typeInference)
        {
            switch (node.Kind)
            {
                case "FunctionDeclaration":
                    HandleFunction(node, symbols, types, tokens, typeInference);
                    currentFunctionId = node.Id;
                    break;
                case "Parameter":
                    {
                        var param = BuildParameterSymbol(node, tokens, currentFunctionId, types, typeInference);
                        if (param is not null)
                        {
                            symbols.Add(param);
                        }
                    }
                    break;
                case "VariableDeclaration":
                    {
                        var variable = BuildVariableSymbol(node, parentKind, currentFunctionId, currentStructId, tokens, types, typeInference);
                        if (variable is not null)
                        {
                            symbols.Add(variable);
                        }
                    }
                    break;
                case "StructDeclaration":
                    {
                        var structSym = BuildStructSymbol(node, tokens);
                        if (structSym is not null)
                        {
                            symbols.Add(structSym);
                            currentStructId = structSym.Id;
                        }
                        types.Add(node.Id, tokens.GetChildIdentifierText(node, "identifier"));
                    }
                    break;
                case "CBufferDeclaration":
                    {
                        var cbufSym = BuildCBufferSymbol(node, tokens);
                        if (cbufSym is not null)
                        {
                            symbols.Add(cbufSym);
                            currentStructId = cbufSym.Id;
                        }
                        types.Add(node.Id, tokens.GetChildIdentifierText(node, "identifier"));
                    }
                    break;
                case "Type":
                    types.Add(node.Id, tokens.GetText(node.Span));
                    break;
                default:
                    break;
            }

            foreach (var child in node.Children)
            {
                Traverse(child.Node, node.Kind, currentFunctionId, currentStructId, symbols, types, tokens, typeInference);
            }
        }

        private static void HandleFunction(NodeInfo node, List<SymbolInfo> symbols, TypeCollector types, TokenLookup tokens, TypeInference typeInference)
        {
            if (node.Id is null) return;
            var name = tokens.GetChildIdentifierText(node, "identifier");
            var returnType = tokens.GetChildTypeText(node, "type") ?? "void";
            var returnSemantic = ParseSemanticString(tokens.GetAnnotationText(node));

            var parameterTypes = new List<string>();
            foreach (var child in node.Children.Where(c => string.Equals(c.Role, "parameter", StringComparison.OrdinalIgnoreCase)))
            {
                var pType = tokens.GetChildTypeText(child.Node, "type");
                if (!string.IsNullOrWhiteSpace(pType))
                {
                    var arraySuffix = child.Node.Span is null ? null : tokens.ExtractArraySuffix(child.Node.Span.Value);
                    parameterTypes.Add(string.IsNullOrWhiteSpace(arraySuffix) ? pType! : $"{pType}{arraySuffix}");
                }
            }

            var typeNode = GetChildNode(node, "type");
            var typeNodeId = typeNode?.Id ?? node.Id;
            var normalizedReturn = typeInference.ParseType(returnType) ?? SemType.Scalar("void");
            types.Add(typeNodeId, normalizedReturn.ToNormalizedString());
            typeInference.AddNodeType(typeNodeId, normalizedReturn);

            var parameterSemTypes = parameterTypes.Select(p => typeInference.ParseType(p) ?? SemType.Scalar("void")).ToList();
            var funcType = BuildFunctionType(returnType, parameterTypes);
            var normalizedFuncType = typeInference.NormalizeFunctionType(normalizedReturn, parameterSemTypes);
            types.Add(node.Id, normalizedFuncType);
            typeInference.AddNodeType(node.Id, SemType.Function(normalizedReturn, parameterSemTypes));

            symbols.Add(new SymbolInfo
            {
                Id = node.Id,
                Kind = "Function",
                Name = name,
                Type = funcType,
                DeclNodeId = node.Id,
                ReturnSemantic = returnSemantic
            });
        }

        private static SymbolInfo? BuildParameterSymbol(NodeInfo node, TokenLookup tokens, int? parentSymbolId, TypeCollector types, TypeInference typeInference)
        {
            if (node.Id is null) return null;
            var name = tokens.GetChildIdentifierText(node, "identifier");
            var type = tokens.GetChildTypeText(node, "type");
            var arraySuffix = node.Span is null ? null : tokens.ExtractArraySuffix(node.Span.Value);
            if (!string.IsNullOrWhiteSpace(type) && !string.IsNullOrWhiteSpace(arraySuffix))
            {
                type = $"{type}{arraySuffix}";
            }
            var semanticText = tokens.GetAnnotationText(node);
            var semantic = ParseSemanticString(semanticText);

            if (node.Span is not null && (name is null || type is null || TokenLookup.IsModifier(type) || string.Equals(name, type, StringComparison.OrdinalIgnoreCase)))
            {
                (name, type) = tokens.InferParameterNameAndType(node.Span.Value, fallbackName: name, fallbackType: type);
            }

            if (semantic is null && node.Span is not null)
            {
                semantic = tokens.InferSemantic(node.Span.Value);
            }

            var typeNode = GetChildNode(node, "type");
            var typeNodeId = typeNode?.Id ?? node.Id;
            var normalizedType = typeInference.ParseType(type);
            if (normalizedType is not null)
            {
                types.Add(typeNodeId, normalizedType.ToNormalizedString());
                typeInference.AddNodeType(typeNodeId, normalizedType);
            }

            return new SymbolInfo
            {
                Id = node.Id,
                Kind = "Parameter",
                Name = name,
                Type = type,
                DeclNodeId = node.Id,
                ParentSymbolId = parentSymbolId,
                Semantic = semantic
            };
        }

        private static SymbolInfo? BuildVariableSymbol(NodeInfo node, string? parentKind, int? currentFunctionId, int? currentStructId, TokenLookup tokens, TypeCollector types, TypeInference typeInference)
        {
            if (node.Id is null) return null;

            var name = tokens.GetChildIdentifierText(node, "identifier");
            var type = tokens.GetChildTypeText(node, "type");
            var arraySuffix = node.Span is null ? null : tokens.ExtractArraySuffix(node.Span.Value);
            if (!string.IsNullOrWhiteSpace(type) && !string.IsNullOrWhiteSpace(arraySuffix))
            {
                type = $"{type}{arraySuffix}";
            }
            var semantic = ParseSemanticString(tokens.GetAnnotationText(node));

            var kind = parentKind switch
            {
                "CompilationUnit" => ClassifyGlobalKind(type),
                "TypeBody" => "StructMember",
                _ when currentFunctionId is not null => "LocalVariable",
                _ => "LocalVariable"
            };

            var parentSymbolId = parentKind == "TypeBody" ? currentStructId : currentFunctionId;

            var typeNode = GetChildNode(node, "type");
            var typeNodeId = typeNode?.Id ?? node.Id;
            var normalizedType = typeInference.ParseType(type);
            if (normalizedType is not null)
            {
                types.Add(typeNodeId, normalizedType.ToNormalizedString());
                typeInference.AddNodeType(typeNodeId, normalizedType);
            }

            return new SymbolInfo
            {
                Id = node.Id,
                Kind = kind,
                Name = name,
                Type = type,
                DeclNodeId = node.Id,
                ParentSymbolId = parentSymbolId,
                Semantic = semantic
            };
        }

        private static string ClassifyGlobalKind(string? type)
        {
            if (!string.IsNullOrWhiteSpace(type) && type.StartsWith("sampler", StringComparison.OrdinalIgnoreCase))
            {
                return "Sampler";
            }
            if (!string.IsNullOrWhiteSpace(type) && type.StartsWith("texture", StringComparison.OrdinalIgnoreCase))
            {
                return "Resource";
            }
            return "GlobalVariable";
        }

        private static SymbolInfo? BuildStructSymbol(NodeInfo node, TokenLookup tokens)
        {
            if (node.Id is null) return null;
            var name = tokens.GetChildIdentifierText(node, "identifier");
            return new SymbolInfo
            {
                Id = node.Id,
                Kind = "Struct",
                Name = name,
                DeclNodeId = node.Id
            };
        }

        private static SymbolInfo? BuildCBufferSymbol(NodeInfo node, TokenLookup tokens)
        {
            if (node.Id is null) return null;
            var name = tokens.GetChildIdentifierText(node, "identifier");
            return new SymbolInfo
            {
                Id = node.Id,
                Kind = "CBuffer",
                Name = name,
                DeclNodeId = node.Id
            };
        }

        private static string BuildFunctionType(string returnType, IReadOnlyList<string> parameterTypes)
        {
            var parameters = string.Join(", ", parameterTypes.Select(p => p ?? "void"));
            return $"{returnType ?? "void"}({parameters})";
        }

        private static SemanticInfo? ParseSemanticString(string? semantic)
        {
            if (string.IsNullOrWhiteSpace(semantic))
            {
                return null;
            }

            var trimmed = semantic.TrimStart(' ', ':');
            var span = trimmed.AsSpan();
            var i = span.Length - 1;
            while (i >= 0 && char.IsDigit(span[i]))
            {
                i--;
            }

            var namePart = span[..(i + 1)].ToString();
            int? index = null;
            if (i < span.Length - 1)
            {
                var indexPart = span[(i + 1)..].ToString();
                if (int.TryParse(indexPart, out var idx))
                {
                    index = idx;
                }
            }

            index ??= 0;
            var normalizedName = namePart.StartsWith("SV_", StringComparison.OrdinalIgnoreCase)
                ? namePart.ToUpperInvariant()
                : namePart.ToUpperInvariant();

            return new SemanticInfo { Name = normalizedName, Index = index };
        }

        private static NodeInfo? GetChildNode(NodeInfo node, string role)
        {
            foreach (var child in node.Children)
            {
                if (string.Equals(child.Role, role, StringComparison.OrdinalIgnoreCase))
                {
                    return child.Node;
                }
            }
            return null;
        }
    }

    private sealed class TokenLookup
    {
        private readonly List<TokenInfo> _tokens;

        private TokenLookup(List<TokenInfo> tokens)
        {
            _tokens = tokens;
        }

        public static TokenLookup From(JsonElement root)
        {
            var tokens = new List<TokenInfo>();
            if (root.TryGetProperty("tokens", out var tokensEl) && tokensEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var token in tokensEl.EnumerateArray())
                {
                    if (!TryGetSpan(token, out var span)) continue;
                    var text = token.TryGetProperty("text", out var tEl) ? tEl.GetString() : null;
                    if (text is null) continue;
                    tokens.Add(new TokenInfo(span.Start, span.End, text));
                }
            }
            return new TokenLookup(tokens);
        }

        public string? GetChildIdentifierText(NodeInfo node, string role)
        {
            foreach (var child in node.Children)
            {
                if (!string.Equals(child.Role, role, StringComparison.OrdinalIgnoreCase)) continue;
                if (child.Node.Span is null) continue;
                var text = GetText(child.Node.Span.Value);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
            return null;
        }

        public string? GetChildTypeText(NodeInfo node, string role)
        {
            foreach (var child in node.Children)
            {
                if (!string.Equals(child.Role, role, StringComparison.OrdinalIgnoreCase)) continue;
                if (child.Node.Span is null) continue;
                var text = GetText(child.Node.Span.Value);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
            return null;
        }

        public string? GetAnnotationText(NodeInfo node)
        {
            foreach (var child in node.Children)
            {
                if (!string.Equals(child.Role, "annotation", StringComparison.OrdinalIgnoreCase)) continue;
                if (child.Node.Span is null) continue;
                var text = GetText(child.Node.Span.Value);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
            return null;
        }

        private string? GetText(Span span)
        {
            var text = string.Concat(_tokens
                .Where(t => t.Start >= span.Start && t.End <= span.End)
                .OrderBy(t => t.Start)
                .Select(t => t.Text));
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        public string? GetText(Span? span)
        {
            if (span is null) return null;
            return GetText(span.Value);
        }

        public (string? name, string? type) InferParameterNameAndType(Span span, string? fallbackName, string? fallbackType)
        {
            var tokens = GetTokens(span);
            var beforeColon = tokens.TakeWhile(t => t.Text != ":").ToList();
            var filtered = beforeColon.Where(t => !IsModifier(t.Text)).ToList();

            string? candidateType = null;
            string? candidateName = null;

            if (filtered.Count >= 2)
            {
                candidateType = filtered[^2].Text;
                candidateName = filtered[^1].Text;
            }
            else if (filtered.Count == 1)
            {
                candidateType = filtered[0].Text;
                candidateName = filtered[0].Text;
            }

            var afterTokens = GetNextTokens(span.End, 3);
            var afterIdent = afterTokens.FirstOrDefault(t => !IsDelimiter(t.Text));
            if (afterIdent.Text is not null && candidateName == candidateType)
            {
                candidateName = afterIdent.Text;
            }

            var chosenType = (!string.IsNullOrWhiteSpace(fallbackType) && !IsModifier(fallbackType))
                ? fallbackType
                : candidateType ?? fallbackType;

            var useCandidateName = string.IsNullOrWhiteSpace(fallbackName)
                || string.Equals(fallbackName, candidateType, StringComparison.OrdinalIgnoreCase)
                || string.Equals(fallbackName, fallbackType, StringComparison.OrdinalIgnoreCase);

            var chosenName = useCandidateName ? candidateName : fallbackName ?? candidateName;

            return (chosenName, chosenType);
        }

        private List<TokenInfo> GetTokens(Span span)
        {
            return _tokens
                .Where(t => t.Start >= span.Start && t.End <= span.End)
                .OrderBy(t => t.Start)
                .ToList();
        }

        public string? ExtractArraySuffix(Span span)
        {
            var startIndex = _tokens.FindIndex(t => t.Start >= span.Start && t.Start <= span.End + 1 && t.Text == "[");
            if (startIndex < 0) return null;

            var endIndex = _tokens.FindIndex(startIndex, t => t.Text == "]");
            if (endIndex < 0 || endIndex < startIndex) return null;

            return string.Concat(_tokens.Skip(startIndex).Take(endIndex - startIndex + 1).Select(t => t.Text));
        }

        private List<TokenInfo> GetNextTokens(int position, int count)
        {
            return _tokens.Where(t => t.Start >= position).OrderBy(t => t.Start).Take(count).ToList();
        }

        public SemanticInfo? InferSemantic(Span span)
        {
            var tokens = GetNextTokens(span.End, 4);
            var colonIndex = tokens.FindIndex(t => t.Text == ":");
            if (colonIndex >= 0 && colonIndex + 1 < tokens.Count)
            {
                var nameText = tokens[colonIndex + 1].Text.TrimStart(':');
                return new SemanticInfo { Name = nameText.ToUpperInvariant(), Index = 0 };
            }

            return null;
        }

        internal static bool IsModifier(string text)
        {
            return string.Equals(text, "in", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "out", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "inout", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDelimiter(string text)
        {
            return text is ":" or "," or ")" or "(";
        }

        internal static bool TryGetSpan(JsonElement element, out Span span)
        {
            span = default;
            if (!element.TryGetProperty("span", out var spanEl)) return false;
            if (!spanEl.TryGetProperty("start", out var sEl) || !sEl.TryGetInt32(out var start)) return false;
            if (!spanEl.TryGetProperty("end", out var eEl) || !eEl.TryGetInt32(out var end)) return false;
            span = new Span(start, end);
            return true;
        }

        private readonly record struct TokenInfo(int Start, int End, string Text);
    }

    private sealed class TypeCollector
    {
        private readonly Dictionary<int, string> _types = new();

        public void Add(int? nodeId, string? type)
        {
            if (nodeId is null) return;
            if (string.IsNullOrWhiteSpace(type)) return;
            if (_types.ContainsKey(nodeId.Value)) return;
            _types[nodeId.Value] = type!;
        }

        public List<TypeInfo> ToList()
        {
            return _types
                .OrderBy(kv => kv.Key)
                .Select(kv => new TypeInfo { NodeId = kv.Key, Type = kv.Value })
                .ToList();
        }
    }

    private enum TypeKind
    {
        Unknown,
        Scalar,
        Vector,
        Matrix,
        Array,
        Resource,
        Function
    }

    private sealed record IntrinsicSignature
    {
        public string Name { get; init; } = string.Empty;
        public IReadOnlyList<SemType> Parameters { get; init; } = System.Array.Empty<SemType>();
        public Func<IReadOnlyList<SemType>, SemType?> ReturnResolver { get; init; } = _ => null;
    }

    private static class Intrinsics
    {
        private static readonly IReadOnlyList<IntrinsicSignature> Catalog = new List<IntrinsicSignature>
        {
            new IntrinsicSignature
            {
                Name = "dot",
                Parameters = new [] { SemType.Vector("float", 3), SemType.Vector("float", 3) },
                ReturnResolver = _ => SemType.Scalar("float")
            },
            new IntrinsicSignature
            {
                Name = "dot",
                Parameters = new [] { SemType.Vector("float", 4), SemType.Vector("float", 4) },
                ReturnResolver = _ => SemType.Scalar("float")
            },
            new IntrinsicSignature
            {
                Name = "normalize",
                Parameters = new [] { SemType.Vector("float", 3) },
                ReturnResolver = args => args.FirstOrDefault()
            },
            new IntrinsicSignature
            {
                Name = "normalize",
                Parameters = new [] { SemType.Vector("float", 4) },
                ReturnResolver = args => args.FirstOrDefault()
            },
            new IntrinsicSignature
            {
                Name = "saturate",
                Parameters = new [] { SemType.Scalar("float") },
                ReturnResolver = args => args.FirstOrDefault()
            },
            new IntrinsicSignature
            {
                Name = "saturate",
                Parameters = new [] { SemType.Vector("float", 2) },
                ReturnResolver = args => args.FirstOrDefault()
            },
            new IntrinsicSignature
            {
                Name = "saturate",
                Parameters = new [] { SemType.Vector("float", 3) },
                ReturnResolver = args => args.FirstOrDefault()
            },
            new IntrinsicSignature
            {
                Name = "saturate",
                Parameters = new [] { SemType.Vector("float", 4) },
                ReturnResolver = args => args.FirstOrDefault()
            },
            new IntrinsicSignature
            {
                Name = "mul",
                Parameters = new [] { SemType.Matrix("float", 4, 4), SemType.Vector("float", 4) },
                ReturnResolver = args => DetermineMulReturn(args)
            },
            new IntrinsicSignature
            {
                Name = "mul",
                Parameters = new [] { SemType.Vector("float", 4), SemType.Matrix("float", 4, 4) },
                ReturnResolver = args => DetermineMulReturn(args)
            },
            new IntrinsicSignature
            {
                Name = "mul",
                Parameters = new [] { SemType.Matrix("float", 3, 3), SemType.Vector("float", 3) },
                ReturnResolver = args => DetermineMulReturn(args)
            },
            new IntrinsicSignature
            {
                Name = "mul",
                Parameters = new [] { SemType.Vector("float", 3), SemType.Matrix("float", 3, 3) },
                ReturnResolver = args => DetermineMulReturn(args)
            },
            new IntrinsicSignature
            {
                Name = "tex2D",
                Parameters = new [] { SemType.Resource("sampler2D"), SemType.Vector("float", 2) },
                ReturnResolver = _ => SemType.Vector("float", 4)
            }
        };

        public static SemType? Resolve(string name, IReadOnlyList<SemType?> args, TypeInference inference)
        {
            var matches = Catalog.Where(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0)
            {
                return null;
            }

            foreach (var sig in matches)
            {
                if (sig.Parameters.Count != args.Count)
                {
                    continue;
                }

                var compatible = true;
                for (var i = 0; i < sig.Parameters.Count; i++)
                {
                    var arg = args[i];
                    if (arg is null || !TypeCompatibility.CanPromote(arg, sig.Parameters[i]))
                    {
                        compatible = false;
                        break;
                    }
                }

                if (!compatible)
                {
                    continue;
                }

                return sig.ReturnResolver(args.Select((a, i) => a ?? sig.Parameters[i]).ToList());
            }

            inference.AddDiagnostic("HLSL2001", $"No matching intrinsic overload for '{name}'.");
            return null;
        }

        private static SemType? DetermineMulReturn(IReadOnlyList<SemType> args)
        {
            if (args.Count != 2) return null;
            var a = args[0];
            var b = args[1];

            if (a.Kind == TypeKind.Matrix && b.Kind == TypeKind.Vector && a.Columns == b.VectorSize)
            {
                return SemType.Vector(a.BaseType, a.Rows);
            }

            if (a.Kind == TypeKind.Vector && b.Kind == TypeKind.Matrix && a.VectorSize == b.Rows)
            {
                return SemType.Vector(b.BaseType, b.Columns);
            }

            if (a.Kind == TypeKind.Matrix && b.Kind == TypeKind.Matrix && a.Columns == b.Rows)
            {
                return SemType.Matrix(a.BaseType, a.Rows, b.Columns);
            }

            return b.Kind == TypeKind.Vector ? b : a;
        }
    }

    private sealed record SemType
    {
        public TypeKind Kind { get; init; }
        public string BaseType { get; init; } = string.Empty;
        public int Rows { get; init; }
        public int Columns { get; init; }
        public int? ArrayLength { get; init; }
        public SemType? ElementType { get; init; }
        public IReadOnlyList<SemType> ParameterTypes { get; init; } = System.Array.Empty<SemType>();
        public SemType? ReturnType { get; init; }

        public bool IsNumeric => Kind is TypeKind.Scalar or TypeKind.Vector or TypeKind.Matrix;
        public bool IsVector => Kind == TypeKind.Vector;
        public bool IsMatrix => Kind == TypeKind.Matrix;
        public bool IsScalar => Kind == TypeKind.Scalar;
        public int VectorSize => Columns;

        public static SemType Scalar(string baseType) => new SemType
        {
            Kind = TypeKind.Scalar,
            BaseType = NormalizeBase(baseType),
            Rows = 1,
            Columns = 1,
            ParameterTypes = System.Array.Empty<SemType>()
        };

        public static SemType Vector(string baseType, int width) => new SemType
        {
            Kind = TypeKind.Vector,
            BaseType = NormalizeBase(baseType),
            Rows = 1,
            Columns = width,
            ParameterTypes = System.Array.Empty<SemType>()
        };

        public static SemType Matrix(string baseType, int rows, int cols) => new SemType
        {
            Kind = TypeKind.Matrix,
            BaseType = NormalizeBase(baseType),
            Rows = rows,
            Columns = cols,
            ParameterTypes = System.Array.Empty<SemType>()
        };

        public static SemType Array(SemType elementType, int? length) => new SemType
        {
            Kind = TypeKind.Array,
            BaseType = elementType.BaseType,
            Rows = elementType.Rows,
            Columns = elementType.Columns,
            ArrayLength = length,
            ElementType = elementType,
            ParameterTypes = System.Array.Empty<SemType>()
        };

        public static SemType Resource(string name) => new SemType
        {
            Kind = TypeKind.Resource,
            BaseType = NormalizeBase(name),
            Rows = 1,
            Columns = 1,
            ParameterTypes = System.Array.Empty<SemType>()
        };

        public static SemType Function(SemType returnType, IReadOnlyList<SemType> parameters) => new SemType
        {
            Kind = TypeKind.Function,
            BaseType = returnType.BaseType,
            Rows = 1,
            Columns = 1,
            ReturnType = returnType,
            ParameterTypes = parameters?.ToArray() ?? System.Array.Empty<SemType>()
        };

        public string? ToNormalizedString()
        {
            return Kind switch
            {
                TypeKind.Scalar => BaseType,
                TypeKind.Vector => $"{BaseType}{Columns}",
                TypeKind.Matrix => $"{BaseType}{Rows}x{Columns}",
                TypeKind.Array => $"{ElementType?.ToNormalizedString()}[{(ArrayLength.HasValue ? ArrayLength.Value.ToString() : string.Empty)}]",
                TypeKind.Resource => BaseType,
                TypeKind.Function => $"{ReturnType?.ToNormalizedString()}({string.Join(", ", ParameterTypes.Select(p => p.ToNormalizedString()))})",
                _ => BaseType
            };
        }

        public override string ToString() => ToNormalizedString() ?? BaseType;

        public static SemType? Parse(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var text = raw.Trim();
            var lower = text.ToLowerInvariant();

            var openIdx = text.IndexOf('(');
            var closeIdx = text.LastIndexOf(')');
            if (openIdx > 0 && closeIdx == text.Length - 1)
            {
                var retText = text[..openIdx];
                var argsText = text[(openIdx + 1)..closeIdx];
                var retType = Parse(retText) ?? Scalar("void");
                var args = new List<SemType>();
                if (!string.IsNullOrWhiteSpace(argsText))
                {
                    foreach (var arg in argsText.Split(','))
                    {
                        var parsedArg = Parse(arg);
                        if (parsedArg is not null)
                        {
                            args.Add(parsedArg);
                        }
                    }
                }
                return Function(retType, args);
            }

            var bracketIdx = text.IndexOf('[');
            if (bracketIdx > 0 && text.EndsWith("]", StringComparison.Ordinal))
            {
                var basePart = text[..bracketIdx];
                var lenPart = text[(bracketIdx + 1)..^1];
                var element = Parse(basePart);
                if (element is null) return null;
                int? len = null;
                if (int.TryParse(lenPart, out var parsedLen))
                {
                    len = parsedLen;
                }
                return Array(element, len);
            }

            var xIdx = lower.IndexOf('x');
            if (xIdx > 0 && xIdx < lower.Length - 1 && char.IsDigit(lower[xIdx - 1]))
            {
                var left = lower[..xIdx];
                var right = lower[(xIdx + 1)..];
                if (TryParseBaseWithNumber(left, out var baseName, out var rows) && int.TryParse(right, out var cols))
                {
                    return Matrix(baseName, rows, cols);
                }
            }

            if (TryParseBaseWithNumber(lower, out var baseType, out var width))
            {
                if (width > 1)
                {
                    return Vector(baseType, width);
                }
                return Scalar(baseType);
            }

            if (IsResourceName(lower))
            {
                return Resource(lower);
            }

            if (IsNumericBase(lower) || string.Equals(lower, "void", StringComparison.OrdinalIgnoreCase))
            {
                return Scalar(lower);
            }

            return Resource(lower);
        }

        private static bool TryParseBaseWithNumber(string text, out string baseName, out int number)
        {
            baseName = NormalizeBase(text);
            number = 1;

            var i = baseName.Length - 1;
            while (i >= 0 && char.IsDigit(baseName[i]))
            {
                i--;
            }

            if (i == baseName.Length - 1)
            {
                return false;
            }

            var numPart = baseName[(i + 1)..];
            baseName = baseName[..(i + 1)];
            return int.TryParse(numPart, out number);
        }

        private static string NormalizeBase(string baseType) => baseType.Trim().ToLowerInvariant();

        private static bool IsResourceName(string name)
        {
            return name.StartsWith("sampler", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("texture", StringComparison.OrdinalIgnoreCase)
                || name.Contains("buffer", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNumericBase(string name)
        {
            return name is "float" or "half" or "double" or "int" or "uint" or "bool";
        }
    }

    private static class TypeCompatibility
    {
        private static readonly Dictionary<string, int> ScalarRank = new(StringComparer.OrdinalIgnoreCase)
        {
            { "bool", 0 },
            { "int", 1 },
            { "uint", 1 },
            { "half", 2 },
            { "float", 3 },
            { "double", 4 }
        };

        public static SemType? PromoteBinary(SemType? left, SemType? right)
        {
            if (left is null) return right;
            if (right is null) return left;

            var l = left;
            var r = right;

            if (!l.IsNumeric || !r.IsNumeric)
            {
                return null;
            }

            if (l.Kind == TypeKind.Function || r.Kind == TypeKind.Function)
            {
                return null;
            }

            if (l.Kind == TypeKind.Array && r.Kind == TypeKind.Array && l.ElementType is not null && r.ElementType is not null)
            {
                var element = PromoteBinary(l.ElementType, r.ElementType);
                if (element is null) return null;
                return SemType.Array(element, l.ArrayLength ?? r.ArrayLength);
            }

            if (l.Kind == TypeKind.Vector && r.Kind == TypeKind.Vector)
            {
                if (l.VectorSize != r.VectorSize) return null;
                return SemType.Vector(PromoteBase(l.BaseType, r.BaseType), l.VectorSize);
            }

            if (l.Kind == TypeKind.Matrix && r.Kind == TypeKind.Matrix)
            {
                if (l.Rows != r.Rows || l.Columns != r.Columns) return null;
                return SemType.Matrix(PromoteBase(l.BaseType, r.BaseType), l.Rows, l.Columns);
            }

            if (l.Kind == TypeKind.Vector && r.Kind == TypeKind.Scalar)
            {
                return SemType.Vector(PromoteBase(l.BaseType, r.BaseType), l.VectorSize);
            }

            if (l.Kind == TypeKind.Scalar && r.Kind == TypeKind.Vector)
            {
                return SemType.Vector(PromoteBase(l.BaseType, r.BaseType), r.VectorSize);
            }

            if (l.Kind == TypeKind.Matrix && r.Kind == TypeKind.Scalar)
            {
                return SemType.Matrix(PromoteBase(l.BaseType, r.BaseType), l.Rows, l.Columns);
            }

            if (l.Kind == TypeKind.Scalar && r.Kind == TypeKind.Matrix)
            {
                return SemType.Matrix(PromoteBase(l.BaseType, r.BaseType), r.Rows, r.Columns);
            }

            if (l.Kind == TypeKind.Scalar && r.Kind == TypeKind.Scalar)
            {
                return SemType.Scalar(PromoteBase(l.BaseType, r.BaseType));
            }

            return null;
        }

        public static bool CanPromote(SemType from, SemType to)
        {
            if (from.Kind == TypeKind.Function || to.Kind == TypeKind.Function)
            {
                return false;
            }

            if (from.Kind == to.Kind)
            {
                return from.Kind switch
                {
                    TypeKind.Scalar => CanPromoteBase(from.BaseType, to.BaseType),
                    TypeKind.Vector => from.VectorSize == to.VectorSize && CanPromoteBase(from.BaseType, to.BaseType),
                    TypeKind.Matrix => from.Rows == to.Rows && from.Columns == to.Columns && CanPromoteBase(from.BaseType, to.BaseType),
                    TypeKind.Resource => string.Equals(from.BaseType, to.BaseType, StringComparison.OrdinalIgnoreCase),
                    TypeKind.Array when from.ElementType is not null && to.ElementType is not null =>
                        (!to.ArrayLength.HasValue || from.ArrayLength == to.ArrayLength)
                        && CanPromote(from.ElementType, to.ElementType),
                    _ => false
                };
            }

            if (from.Kind == TypeKind.Scalar && (to.Kind == TypeKind.Vector || to.Kind == TypeKind.Matrix))
            {
                return CanPromoteBase(from.BaseType, to.BaseType);
            }

            if (from.Kind == TypeKind.Vector && to.Kind == TypeKind.Matrix)
            {
                return from.VectorSize == to.Columns && CanPromoteBase(from.BaseType, to.BaseType);
            }

            if (from.Kind == TypeKind.Scalar && to.Kind == TypeKind.Array && to.ElementType is not null)
            {
                return CanPromote(from, to.ElementType);
            }

            return false;
        }

        private static string PromoteBase(string a, string b)
        {
            var rankA = RankOf(a);
            var rankB = RankOf(b);
            if (rankA < 0) return b;
            if (rankB < 0) return a;
            return rankA >= rankB ? a : b;
        }

        private static bool CanPromoteBase(string from, string to)
        {
            var rankFrom = RankOf(from);
            var rankTo = RankOf(to);
            return rankFrom >= 0 && rankTo >= 0 && rankFrom <= rankTo;
        }

        private static int RankOf(string name) => ScalarRank.TryGetValue(name, out var rank) ? rank : -1;
    }

    private sealed class TypeInference
    {
        private readonly Dictionary<int, SemType> _nodeTypes = new();
        private readonly List<DiagnosticInfo> _diagnostics = new();

        public SemType? ParseType(string? type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return null;
            }

            return SemType.Parse(type);
        }

        public string? NormalizeType(string? type)
        {
            var parsed = ParseType(type);
            return NormalizeType(parsed);
        }

        public string? NormalizeType(SemType? type)
        {
            return type?.ToNormalizedString();
        }

        public string NormalizeFunctionType(SemType returnType, IReadOnlyList<SemType> parameters) =>
            SemType.Function(returnType, parameters).ToNormalizedString() ?? string.Empty;

        public void AddNodeType(int? nodeId, SemType? type)
        {
            if (nodeId is null || type is null)
            {
                return;
            }

            if (_nodeTypes.ContainsKey(nodeId.Value))
            {
                return;
            }

            _nodeTypes[nodeId.Value] = type;
        }

        public SemType? GetNodeType(int? nodeId)
        {
            if (nodeId is null) return null;
            return _nodeTypes.TryGetValue(nodeId.Value, out var t) ? t : null;
        }

        public string? GetNodeTypeString(int? nodeId)
        {
            var t = GetNodeType(nodeId);
            return NormalizeType(t);
        }

        public void AddDiagnostic(string id, string message)
        {
            _diagnostics.Add(new DiagnosticInfo
            {
                Severity = "Error",
                Id = id,
                Message = message
            });
        }

        public IReadOnlyList<DiagnosticInfo> Diagnostics => _diagnostics;
    }

    private static class ExpressionTypeAnalyzer
    {
        public static void Infer(NodeInfo root, TokenLookup tokens, List<SymbolInfo> symbols, List<TypeInfo> types, TypeInference typeInference)
        {
            var symbolTypes = symbols
                .Where(s => !string.IsNullOrWhiteSpace(s.Name) && !string.IsNullOrWhiteSpace(s.Type))
                .GroupBy(s => s.Name!)
                .ToDictionary(g => g.Key, g => typeInference.ParseType(g.First().Type!));

            Traverse(root);

            void Traverse(NodeInfo node)
            {
                foreach (var child in node.Children)
                {
                    Traverse(child.Node);
                }

                switch (node.Kind)
                {
                    case "Identifier":
                        {
                            var name = tokens.GetText(node.Span);
                            if (name is not null && symbolTypes.TryGetValue(name, out var t) && t is not null)
                            {
                                AddType(types, typeInference, node.Id, t);
                            }
                        }
                        break;
                    case "Literal":
                        AddType(types, typeInference, node.Id, InferLiteralType(tokens.GetText(node.Span)));
                        break;
                    case "MemberAccessExpression":
                        {
                            var target = node.Children.FirstOrDefault(c => string.Equals(c.Role, "expression", StringComparison.OrdinalIgnoreCase)).Node;
                            var baseType = typeInference.GetNodeType(target?.Id);
                            var text = tokens.GetText(node.Span);
                            var swizzle = text?.Split('.').LastOrDefault();
                            var inferred = InferSwizzleType(baseType, swizzle);
                            AddType(types, typeInference, node.Id, inferred);
                        }
                        break;
                    case "CallExpression":
                        InferCall(node, tokens, symbolTypes, typeInference, types);
                        break;
                    case "BinaryExpression":
                        InferBinary(node, typeInference, types);
                        break;
                    case "CastExpression":
                        {
                            var typeNode = node.Children.FirstOrDefault(c => string.Equals(c.Role, "type", StringComparison.OrdinalIgnoreCase)).Node;
                            var targetType = typeNode is null ? null : typeInference.ParseType(tokens.GetText(typeNode.Span));
                            AddType(types, typeInference, node.Id, targetType);
                        }
                        break;
                    case "IndexExpression":
                        {
                            var target = node.Children.FirstOrDefault(c => string.Equals(c.Role, "expression", StringComparison.OrdinalIgnoreCase)).Node;
                            var baseType = typeInference.GetNodeType(target?.Id);
                            var elementType = InferIndexedType(baseType);
                            AddType(types, typeInference, node.Id, elementType);
                        }
                        break;
                    default:
                        break;
                }
            }
        }

        private static void InferCall(NodeInfo node, TokenLookup tokens, Dictionary<string, SemType?> symbolTypes, TypeInference typeInference, List<TypeInfo> types)
        {
            var callee = node.Children.FirstOrDefault(c => string.Equals(c.Role, "callee", StringComparison.OrdinalIgnoreCase)).Node;
            var calleeName = callee is null ? null : tokens.GetText(callee.Span);
            var arguments = node.Children.Where(c => string.Equals(c.Role, "argument", StringComparison.OrdinalIgnoreCase)).Select(c => c.Node).ToList();
            var argTypes = arguments.Select(a => typeInference.GetNodeType(a.Id)).ToList();

            SemType? inferred = null;
            if (!string.IsNullOrWhiteSpace(calleeName))
            {
                // Intrinsic resolution first.
                inferred = Intrinsics.Resolve(calleeName!, argTypes, typeInference);

                if (inferred is null)
                {
                    if (symbolTypes.TryGetValue(calleeName!, out var fnType) && fnType is not null && fnType.Kind == TypeKind.Function)
                    {
                        inferred = fnType.ReturnType;
                        CheckCallCompatibility(calleeName!, fnType, argTypes, typeInference);
                    }
                    else
                    {
                        var ctorType = typeInference.ParseType(calleeName);
                        if (ctorType is not null)
                        {
                            inferred = ctorType;
                            CheckConstructorArguments(calleeName!, ctorType, argTypes, typeInference);
                        }
                    }
                }
            }
            else
            {
                typeInference.AddDiagnostic("HLSL2003", "Call expression missing callee.");
            }

            AddType(types, typeInference, node.Id, inferred);
        }

        private static void CheckCallCompatibility(string calleeName, SemType functionType, IReadOnlyList<SemType?> args, TypeInference typeInference)
        {
            var parameters = functionType.ParameterTypes ?? Array.Empty<SemType>();
            if (parameters.Count != args.Count)
            {
                typeInference.AddDiagnostic("HLSL2001", $"Function '{calleeName}' expects {parameters.Count} arguments but got {args.Count}.");
                return;
            }

            for (var i = 0; i < parameters.Count; i++)
            {
                var arg = args[i];
                var param = parameters[i];
                if (arg is null || !TypeCompatibility.CanPromote(arg, param))
                {
                    typeInference.AddDiagnostic("HLSL2001", $"Cannot convert argument {i} to parameter of type '{param}'.");
                    return;
                }
            }
        }

        private static void CheckConstructorArguments(string calleeName, SemType targetType, IReadOnlyList<SemType?> args, TypeInference typeInference)
        {
            if (args.Count == 0) return;

            if (targetType.Kind is TypeKind.Vector or TypeKind.Matrix)
            {
                var required = targetType.Kind == TypeKind.Vector
                    ? targetType.VectorSize
                    : targetType.Rows * targetType.Columns;

                var provided = 0;
                foreach (var arg in args)
                {
                    if (arg is null || !TypeCompatibility.CanPromote(arg, SemType.Scalar(targetType.BaseType)))
                    {
                        typeInference.AddDiagnostic("HLSL2001", $"Cannot type-call '{calleeName}' with provided arguments.");
                        return;
                    }
                    provided += CountComponents(arg);
                }

                if (provided != required)
                {
                    typeInference.AddDiagnostic("HLSL2001", $"Constructor '{calleeName}' expects {required} components but got {provided}.");
                }
                return;
            }

            foreach (var arg in args)
            {
                if (arg is null || !TypeCompatibility.CanPromote(arg, targetType))
                {
                    typeInference.AddDiagnostic("HLSL2001", $"Cannot type-call '{calleeName}' with provided arguments.");
                    return;
                }
            }
        }

        private static int CountComponents(SemType type) =>
            type.Kind switch
            {
                TypeKind.Scalar => 1,
                TypeKind.Vector => type.VectorSize,
                TypeKind.Matrix => type.Rows * type.Columns,
                TypeKind.Array when type.ElementType is not null && type.ArrayLength.HasValue => type.ElementType.Kind == TypeKind.Scalar ? type.ArrayLength.Value : type.ArrayLength.Value * CountComponents(type.ElementType),
                _ => 1
            };

        private static void InferBinary(NodeInfo node, TypeInference typeInference, List<TypeInfo> types)
        {
            var left = node.Children.FirstOrDefault(c => string.Equals(c.Role, "left", StringComparison.OrdinalIgnoreCase)).Node;
            var right = node.Children.FirstOrDefault(c => string.Equals(c.Role, "right", StringComparison.OrdinalIgnoreCase)).Node;
            var lt = typeInference.GetNodeType(left?.Id);
            var rt = typeInference.GetNodeType(right?.Id);

            var merged = TypeCompatibility.PromoteBinary(lt, rt);
            if (merged is null && lt is not null && rt is not null)
            {
                typeInference.AddDiagnostic("HLSL2002", $"Type mismatch in binary expression: '{lt}' vs '{rt}'.");
            }
            AddType(types, typeInference, node.Id, merged);
        }

        private static SemType? InferIndexedType(SemType? baseType)
        {
            if (baseType is null)
            {
                return null;
            }

            return baseType.Kind switch
            {
                TypeKind.Array when baseType.ElementType is not null => baseType.ElementType,
                TypeKind.Vector => SemType.Scalar(baseType.BaseType),
                TypeKind.Matrix => SemType.Vector(baseType.BaseType, baseType.Columns),
                _ => baseType
            };
        }

        private static void AddType(List<TypeInfo> types, TypeInference typeInference, int? nodeId, SemType? type)
        {
            if (nodeId is null || type is null)
            {
                return;
            }
            if (types.Any(t => t.NodeId == nodeId))
            {
                return;
            }
            var normalized = typeInference.NormalizeType(type);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            types.Add(new TypeInfo { NodeId = nodeId, Type = normalized });
            typeInference.AddNodeType(nodeId, type);
        }

        private static bool IsTypeName(string name) => SemType.Parse(name) is not null;
        private static SemType? InferSwizzleType(SemType? baseType, string? swizzle)
        {
            if (baseType is null || string.IsNullOrWhiteSpace(swizzle))
            {
                return null;
            }

            var count = swizzle.Count(ch => "xyzwrgba".Contains(ch, StringComparison.OrdinalIgnoreCase));
            if (count == 0)
            {
                return baseType;
            }

            if (count == 1)
            {
                return SemType.Scalar(baseType.BaseType);
            }

            return SemType.Vector(baseType.BaseType, count);
        }

        private static SemType? InferLiteralType(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var span = text.Trim();
            if (span.StartsWith("true", StringComparison.OrdinalIgnoreCase) || span.StartsWith("false", StringComparison.OrdinalIgnoreCase))
            {
                return SemType.Scalar("bool");
            }

            if (span.Contains(".") || span.IndexOfAny(new[] { 'e', 'E' }) >= 0 || span.EndsWith("f", StringComparison.OrdinalIgnoreCase))
            {
                return SemType.Scalar("float");
            }

            return SemType.Scalar("int");
        }
    }

    private static class EntryPointResolver
    {
        public static List<EntryPointInfo> Resolve(List<SymbolInfo> symbols, string entryName, string profile, TypeInference inference)
        {
            var stage = StageFromProfile(profile);
            var entry = symbols.FirstOrDefault(s => string.Equals(s.Kind, "Function", StringComparison.OrdinalIgnoreCase)
                && string.Equals(s.Name, entryName, StringComparison.OrdinalIgnoreCase));

            if (entry is null)
            {
                inference.AddDiagnostic("HLSL3001", $"No entry point function named '{entryName}' found.");
                entry = symbols.FirstOrDefault(s => string.Equals(s.Kind, "Function", StringComparison.OrdinalIgnoreCase));
                if (entry is null)
                {
                    return new List<EntryPointInfo>();
                }
            }

            return new List<EntryPointInfo>
            {
                new EntryPointInfo
                {
                    Name = entry.Name,
                    SymbolId = entry.Id,
                    Stage = stage,
                    Profile = profile
                }
            };
        }

        private static string StageFromProfile(string profile)
        {
            if (profile.StartsWith("vs", true, CultureInfo.InvariantCulture)) return "Vertex";
            if (profile.StartsWith("ps", true, CultureInfo.InvariantCulture)) return "Pixel";
            if (profile.StartsWith("gs", true, CultureInfo.InvariantCulture)) return "Geometry";
            if (profile.StartsWith("hs", true, CultureInfo.InvariantCulture)) return "Hull";
            if (profile.StartsWith("ds", true, CultureInfo.InvariantCulture)) return "Domain";
            if (profile.StartsWith("cs", true, CultureInfo.InvariantCulture)) return "Compute";
            return "Unknown";
        }
    }

    private static class SemanticValidator
    {
        public static void Validate(List<SymbolInfo> symbols, List<EntryPointInfo> entryPoints, string profile, TypeInference inference)
        {
            if (entryPoints.Count == 0) return;

            var entry = entryPoints[0];
            var stage = entry.Stage ?? "Unknown";
            var smMajor = ParseProfileMajor(profile);

            var function = symbols.FirstOrDefault(s => s.Id == entry.SymbolId);
            if (function is not null)
            {
                ValidateReturnSemantic(function.ReturnSemantic, stage, smMajor, inference);
            }

            foreach (var param in symbols.Where(s => s.ParentSymbolId == entry.SymbolId && string.Equals(s.Kind, "Parameter", StringComparison.OrdinalIgnoreCase)))
            {
                ValidateParameterSemantic(param, stage, smMajor, inference);
            }
        }

        private static void ValidateReturnSemantic(SemanticInfo? semantic, string stage, int smMajor, TypeInference inference)
        {
            if (semantic is null) return;
            if (smMajor < 4 && IsSystemValue(semantic.Name))
            {
                inference.AddDiagnostic("HLSL3002", $"System-value semantic '{semantic.Name}' is not allowed before SM4.");
            }
            if (stage == "Vertex" && string.Equals(semantic.Name, "SV_TARGET", StringComparison.OrdinalIgnoreCase))
            {
                inference.AddDiagnostic("HLSL3002", "Vertex shaders cannot return SV_TARGET.");
            }
        }

        private static void ValidateParameterSemantic(SymbolInfo param, string stage, int smMajor, TypeInference inference)
        {
            if (param.Semantic is null) return;
            var name = param.Semantic.Name;

            if (smMajor < 4 && IsSystemValue(name))
            {
                inference.AddDiagnostic("HLSL3002", $"System-value semantic '{name}' is not allowed before SM4.");
            }

            if (stage == "Pixel" && string.Equals(name, "SV_POSITION", StringComparison.OrdinalIgnoreCase))
            {
                inference.AddDiagnostic("HLSL3002", "Pixel shader parameters should not use SV_POSITION (use input TEXCOORD/position semantics).");
            }
        }

        private static bool IsSystemValue(string name) => name.StartsWith("SV_", StringComparison.OrdinalIgnoreCase);

        private static int ParseProfileMajor(string profile)
        {
            var parts = profile.Split('_');
            if (parts.Length >= 2 && int.TryParse(parts[1].Split('.')[0], out var major))
            {
                return major;
            }
            return 0;
        }
    }

    private sealed record NodeInfo(int? Id, string? Kind, Span? Span, List<NodeChild> Children)
    {
        public static NodeInfo FromJson(JsonElement element)
        {
            int? id = null;
            if (element.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var idVal))
            {
                id = idVal;
            }

            string? kind = element.TryGetProperty("kind", out var kEl) ? kEl.GetString() : null;
            Span? span = null;
            if (TokenLookup.TryGetSpan(element, out var s))
            {
                span = s;
            }

            var children = new List<NodeChild>();
            if (element.TryGetProperty("children", out var childrenEl) && childrenEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in childrenEl.EnumerateArray())
                {
                    var role = child.TryGetProperty("role", out var rEl) ? rEl.GetString() : string.Empty;
                    if (child.TryGetProperty("node", out var nodeEl))
                    {
                        children.Add(new NodeChild(role ?? string.Empty, FromJson(nodeEl)));
                    }
                }
            }

            return new NodeInfo(id, kind, span, children);
        }
    }

    private readonly record struct NodeChild(string Role, NodeInfo Node);

    private readonly record struct Span(int Start, int End);

    private sealed record SymbolBuildResult(List<SymbolInfo> Symbols, List<TypeInfo> Types);

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

    private sealed record SymbolInfo
    {
        [JsonPropertyName("id")]
        public int? Id { get; init; }

        [JsonPropertyName("kind")]
        public string? Kind { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("declNodeId")]
        public int? DeclNodeId { get; init; }

        [JsonPropertyName("parentSymbolId")]
        public int? ParentSymbolId { get; init; }

        [JsonPropertyName("semantic")]
        public SemanticInfo? Semantic { get; init; }

        [JsonPropertyName("returnSemantic")]
        public SemanticInfo? ReturnSemantic { get; init; }
    }

    private sealed record SemanticInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("index")]
        public int? Index { get; init; }
    }

    private sealed record TypeInfo
    {
        [JsonPropertyName("nodeId")]
        public int? NodeId { get; init; }

        [JsonPropertyName("type")]
        public string? Type { get; init; }
    }

    private sealed record DiagnosticInfo
    {
        [JsonPropertyName("severity")]
        public string Severity { get; init; } = "Error";

        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("message")]
        public string Message { get; init; } = string.Empty;
    }
}
