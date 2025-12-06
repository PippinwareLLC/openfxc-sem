using System.Linq;
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
                EntryPoints = Array.Empty<EntryPointInfo>(),
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

            var parameterTypes = new List<string>();
            foreach (var child in node.Children.Where(c => string.Equals(c.Role, "parameter", StringComparison.OrdinalIgnoreCase)))
            {
                var pType = tokens.GetChildTypeText(child.Node, "type");
                if (!string.IsNullOrWhiteSpace(pType))
                {
                    parameterTypes.Add(pType!);
                }
            }

            var typeNode = GetChildNode(node, "type");
            var typeNodeId = typeNode?.Id ?? node.Id;
            var normalizedReturn = typeInference.NormalizeType(returnType);
            types.Add(typeNodeId, normalizedReturn);
            typeInference.AddNodeType(typeNodeId, normalizedReturn);

            var funcType = BuildFunctionType(returnType, parameterTypes);
            var normalizedFuncType = typeInference.NormalizeFunctionType(normalizedReturn, parameterTypes);
            types.Add(node.Id, normalizedFuncType);
            typeInference.AddNodeType(node.Id, normalizedFuncType);

            symbols.Add(new SymbolInfo
            {
                Id = node.Id,
                Kind = "Function",
                Name = name,
                Type = funcType,
                DeclNodeId = node.Id
            });
        }

        private static SymbolInfo? BuildParameterSymbol(NodeInfo node, TokenLookup tokens, int? parentSymbolId, TypeCollector types, TypeInference typeInference)
        {
            if (node.Id is null) return null;
            var name = tokens.GetChildIdentifierText(node, "identifier");
            var type = tokens.GetChildTypeText(node, "type");
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
            var normalizedType = typeInference.NormalizeType(type);
            types.Add(typeNodeId, normalizedType);
            typeInference.AddNodeType(typeNodeId, normalizedType);

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
            var normalizedType = typeInference.NormalizeType(type);
            types.Add(typeNodeId, normalizedType);
            typeInference.AddNodeType(typeNodeId, normalizedType);

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
            var parameters = string.Join(", ", parameterTypes);
            return $"{returnType}({parameters})";
        }

        private static SemanticInfo? ParseSemanticString(string? semantic)
        {
            if (string.IsNullOrWhiteSpace(semantic))
            {
                return null;
            }

            var span = semantic.AsSpan();
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
            return new SemanticInfo { Name = namePart, Index = index };
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
                var nameText = tokens[colonIndex + 1].Text;
                return new SemanticInfo { Name = nameText, Index = 0 };
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

    private sealed class TypeInference
    {
        private readonly Dictionary<int, string> _nodeTypes = new();
        private readonly List<DiagnosticInfo> _diagnostics = new();

        public string? NormalizeType(string? type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return null;
            }

            var trimmed = type.Trim();
            return trimmed;
        }

        public string NormalizeFunctionType(string? returnType, IReadOnlyList<string?> parameters)
        {
            var normalizedReturn = NormalizeType(returnType) ?? "void";
            var normalizedParams = parameters.Select(p => NormalizeType(p) ?? "void");
            return $"{normalizedReturn}({string.Join(", ", normalizedParams)})";
        }

        public void AddNodeType(int? nodeId, string? type)
        {
            if (nodeId is null || string.IsNullOrWhiteSpace(type))
            {
                return;
            }

            if (_nodeTypes.ContainsKey(nodeId.Value))
            {
                return;
            }

            _nodeTypes[nodeId.Value] = type!;
        }

        public string? GetNodeType(int? nodeId)
        {
            if (nodeId is null) return null;
            return _nodeTypes.TryGetValue(nodeId.Value, out var t) ? t : null;
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
        private static readonly HashSet<string> ScalarTypes = new(StringComparer.OrdinalIgnoreCase) { "float", "int", "bool", "half", "double" };

        public static void Infer(NodeInfo root, TokenLookup tokens, List<SymbolInfo> symbols, List<TypeInfo> types, TypeInference typeInference)
        {
            var symbolTypes = symbols
                .Where(s => !string.IsNullOrWhiteSpace(s.Name) && !string.IsNullOrWhiteSpace(s.Type))
                .GroupBy(s => s.Name!)
                .ToDictionary(g => g.Key, g => g.First().Type!);

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
                            if (name is not null && symbolTypes.TryGetValue(name, out var t))
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
                        InferBinary(node, tokens, typeInference, types);
                        break;
                    case "CastExpression":
                        {
                            var typeNode = node.Children.FirstOrDefault(c => string.Equals(c.Role, "type", StringComparison.OrdinalIgnoreCase)).Node;
                            var targetType = typeNode is null ? null : tokens.GetText(typeNode.Span);
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

        private static void InferCall(NodeInfo node, TokenLookup tokens, Dictionary<string, string> symbolTypes, TypeInference typeInference, List<TypeInfo> types)
        {
            var callee = node.Children.FirstOrDefault(c => string.Equals(c.Role, "callee", StringComparison.OrdinalIgnoreCase)).Node;
            var calleeName = callee is null ? null : tokens.GetText(callee.Span);
            var arguments = node.Children.Where(c => string.Equals(c.Role, "argument", StringComparison.OrdinalIgnoreCase)).Select(c => c.Node).ToList();
            var argTypes = arguments.Select(a => typeInference.GetNodeType(a.Id)).ToList();

            string? inferred = null;
            if (!string.IsNullOrWhiteSpace(calleeName))
            {
                if (IsTypeName(calleeName!))
                {
                    inferred = calleeName;
                }
                else if (symbolTypes.TryGetValue(calleeName!, out var fnType))
                {
                    inferred = ExtractReturnType(fnType);
                }
                else if (string.Equals(calleeName, "mul", StringComparison.OrdinalIgnoreCase) && argTypes.Count >= 1)
                {
                    inferred = argTypes.Last() ?? argTypes.First();
                }
                else
                {
                    // Constructor-style: if callee is a type name, expect args to be convertible.
                    if (IsTypeName(calleeName))
                    {
                        inferred = calleeName;
                        if (argTypes.Any(a => a is null))
                        {
                            typeInference.AddDiagnostic("HLSL2001", $"Cannot type-call '{calleeName}' with provided arguments.");
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

        private static void InferBinary(NodeInfo node, TokenLookup tokens, TypeInference typeInference, List<TypeInfo> types)
        {
            var left = node.Children.FirstOrDefault(c => string.Equals(c.Role, "left", StringComparison.OrdinalIgnoreCase)).Node;
            var right = node.Children.FirstOrDefault(c => string.Equals(c.Role, "right", StringComparison.OrdinalIgnoreCase)).Node;
            var lt = typeInference.GetNodeType(left?.Id);
            var rt = typeInference.GetNodeType(right?.Id);

            var merged = MergeBinaryTypes(lt, rt);
            if (merged is null && lt is not null && rt is not null)
            {
                typeInference.AddDiagnostic("HLSL2002", $"Type mismatch in binary expression: '{lt}' vs '{rt}'.");
            }
            AddType(types, typeInference, node.Id, merged);
        }

        private static string? InferIndexedType(string? baseType)
        {
            if (string.IsNullOrWhiteSpace(baseType)) return null;
            if (IsVector(baseType))
            {
                return baseType.TrimEnd('2', '3', '4', 'x', 'X', 'y', 'Y', 'z', 'Z', 'w', 'W', 'r', 'g', 'b', 'a');
            }
            if (IsMatrix(baseType))
            {
                var parts = baseType.Split('x', 'X');
                if (parts.Length == 2 && int.TryParse(parts[1], out var cols))
                {
                    return $"{parts[0]}{cols}";
                }
            }
            return baseType;
        }

        private static void AddType(List<TypeInfo> types, TypeInference typeInference, int? nodeId, string? type)
        {
            if (nodeId is null || string.IsNullOrWhiteSpace(type))
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
            typeInference.AddNodeType(nodeId, normalized);
        }

        private static bool IsTypeName(string name)
        {
            return ScalarTypes.Contains(name)
                   || IsVector(name)
                   || IsMatrix(name);
        }

        private static string? ExtractReturnType(string fnType)
        {
            var idx = fnType.IndexOf('(');
            return idx > 0 ? fnType[..idx] : fnType;
        }

        private static string? InferSwizzleType(string? baseType, string? swizzle)
        {
            if (string.IsNullOrWhiteSpace(baseType) || string.IsNullOrWhiteSpace(swizzle))
            {
                return null;
            }

            var count = swizzle.Count(ch => "xyzwrgba".Contains(ch, StringComparison.OrdinalIgnoreCase));
            if (count == 0)
            {
                return baseType;
            }

            var scalar = baseType.TrimEnd('1', '2', '3', '4');
            return count == 1 ? scalar : $"{scalar}{count}";
        }

        private static string? InferLiteralType(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var span = text.Trim();
            if (span.Contains(".") || span.IndexOfAny(new[] { 'e', 'E' }) >= 0 || span.EndsWith("f", StringComparison.OrdinalIgnoreCase))
            {
                return "float";
            }

            if (span.StartsWith("true", StringComparison.OrdinalIgnoreCase) || span.StartsWith("false", StringComparison.OrdinalIgnoreCase))
            {
                return "bool";
            }

            return "int";
        }

        private static string? MergeBinaryTypes(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left)) return right;
            if (string.IsNullOrWhiteSpace(right)) return left;
            if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase)) return left;

            if (IsVectorOrMatrix(left) && IsVectorOrMatrix(right))
            {
                return left.Length >= right.Length ? left : right;
            }

            if (IsVectorOrMatrix(left)) return left;
            if (IsVectorOrMatrix(right)) return right;
            if (ScalarTypes.Contains(left)) return left;
            if (ScalarTypes.Contains(right)) return right;

            return left;
        }

        private static bool IsVectorOrMatrix(string type) => IsVector(type) || IsMatrix(type);

        private static bool IsVector(string type)
        {
            return type.StartsWith("float", StringComparison.OrdinalIgnoreCase) && type.Length > "float".Length
                || type.StartsWith("int", StringComparison.OrdinalIgnoreCase) && type.Length > "int".Length;
        }

        private static bool IsMatrix(string type) => type.IndexOf('x') >= 0 || type.IndexOf('X') >= 0;
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

    private sealed record EntryPointInfo;
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
