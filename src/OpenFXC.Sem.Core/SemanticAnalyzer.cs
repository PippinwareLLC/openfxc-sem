using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenFXC.Sem;

public sealed class SemanticAnalyzer
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
        NodeInfo? rootNode = null;
        TokenLookup? tokens = null;
        List<SymbolInfo> symbols = new();
        List<TypeInfo> types = new();
        List<DiagnosticInfo> diagnostics = new();
        List<EntryPointInfo> entryPoints = new();
        List<FxTechniqueInfo> techniques = new();

        try
        {
            using var doc = JsonDocument.Parse(_inputJson);
            var root = doc.RootElement;
            rootId = TryGetRootId(root);
            tokens = TokenLookup.From(root);
            _typeInference.SetDocumentLength(tokens.DocumentLength);
            if (root.TryGetProperty("root", out var rootNodeEl))
            {
                rootNode = NodeInfo.FromJson(rootNodeEl);
                var build = SymbolBuilder.Build(rootNode, tokens, _typeInference);
                ExpressionTypeAnalyzer.Infer(rootNode, tokens, build.Symbols, build.Types, _typeInference);
                entryPoints = EntryPointResolver.Resolve(build.Symbols, _entry, _profile, _typeInference, build.Spans);
                SemanticValidator.Validate(build.Symbols, entryPoints, _profile, _typeInference, build.Spans);
                techniques = FxModelBuilder.Build(rootNode, tokens, _typeInference, build.Symbols);
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
            FormatVersion = 3,
            Profile = _profile,
                Syntax = rootNode is null || rootId is null || tokens is null
                    ? null
                    : new SyntaxInfo
                    {
                        RootId = rootId.Value,
                        Nodes = BuildSyntaxNodes(rootNode, tokens, symbols)
                    },
            Symbols = symbols,
            Types = types,
            EntryPoints = entryPoints,
            Techniques = techniques,
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

    private static IReadOnlyList<SyntaxNodeInfo> BuildSyntaxNodes(NodeInfo root, TokenLookup tokens, IReadOnlyList<SymbolInfo> symbols)
    {
        var nodes = new List<SyntaxNodeInfo>();
        var symbolLookup = symbols
            .Where(s => s.Id is not null && !string.IsNullOrWhiteSpace(s.Name))
            .GroupBy(s => s.Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id!.Value, StringComparer.OrdinalIgnoreCase);
        var symbolKindById = symbols
            .Where(s => s.Id is not null && !string.IsNullOrWhiteSpace(s.Kind))
            .ToDictionary(s => s.Id!.Value, s => s.Kind!);

        void Traverse(NodeInfo node)
        {
            var children = node.Children
                .Select(c => new SyntaxNodeChild
                {
                    Role = c.Role,
                    NodeId = c.Node.Id
                })
                .ToList();

            int? referencedSymbolId = null;
            string? referencedSymbolKind = null;
            string? op = null;
            string? swizzle = null;
            string? calleeName = null;
            string? calleeKind = null;

            if (string.Equals(node.Kind, "Identifier", StringComparison.OrdinalIgnoreCase))
            {
                var name = tokens.GetText(node.Span);
                if (name is not null && symbolLookup.TryGetValue(name, out var symId))
                {
                    referencedSymbolId = symId;
                    symbolKindById.TryGetValue(symId, out referencedSymbolKind);
                }
            }
            else if (string.Equals(node.Kind, "CallExpression", StringComparison.OrdinalIgnoreCase))
            {
                var callee = node.Children.FirstOrDefault(c => string.Equals(c.Role, "callee", StringComparison.OrdinalIgnoreCase)).Node;
                calleeName = callee is null ? null : tokens.GetText(callee.Span);
                if (calleeName is not null && symbolLookup.TryGetValue(calleeName, out var symId))
                {
                    referencedSymbolId = symId;
                    symbolKindById.TryGetValue(symId, out referencedSymbolKind);
                }
                calleeKind = calleeName is not null && Intrinsics.IsIntrinsic(calleeName) ? "Intrinsic" : "User";
            }
            else if (string.Equals(node.Kind, "MemberAccessExpression", StringComparison.OrdinalIgnoreCase))
            {
                var text = tokens.GetText(node.Span);
                var memberName = text?.Split('.').LastOrDefault();
                if (!string.IsNullOrWhiteSpace(memberName) && symbolLookup.TryGetValue(memberName!, out var symId))
                {
                    referencedSymbolId = symId;
                    symbolKindById.TryGetValue(symId, out referencedSymbolKind);
                }
                swizzle = memberName;
            }
            else if (string.Equals(node.Kind, "UnaryExpression", StringComparison.OrdinalIgnoreCase))
            {
                var opNode = node.Children.FirstOrDefault(c => string.Equals(c.Role, "operator", StringComparison.OrdinalIgnoreCase)).Node;
                op = opNode is null ? null : tokens.GetText(opNode.Span);
            }
            else if (string.Equals(node.Kind, "BinaryExpression", StringComparison.OrdinalIgnoreCase))
            {
                var opNode = node.Children.FirstOrDefault(c => string.Equals(c.Role, "operator", StringComparison.OrdinalIgnoreCase)).Node;
                op = opNode is null ? null : tokens.GetText(opNode.Span);
            }
            else if (string.Equals(node.Kind, "CastExpression", StringComparison.OrdinalIgnoreCase))
            {
                op = "cast";
            }

            nodes.Add(new SyntaxNodeInfo
            {
                Id = node.Id,
                Kind = node.Kind ?? string.Empty,
                Span = node.Span is null ? null : new SyntaxSpan { Start = node.Span.Value.Start, End = node.Span.Value.End },
                Children = children,
                ReferencedSymbolId = referencedSymbolId,
                ReferencedSymbolKind = referencedSymbolKind,
                Operator = op,
                Swizzle = swizzle,
                CalleeName = calleeName,
                CalleeKind = calleeKind
            });

            foreach (var child in node.Children)
            {
                Traverse(child.Node);
            }
        }

        Traverse(root);
        return nodes;
    }
}

internal static class FxModelBuilder
{
    public static List<FxTechniqueInfo> Build(NodeInfo rootNode, TokenLookup tokens, TypeInference inference, List<SymbolInfo> symbols)
    {
        var techniques = new List<FxTechniqueInfo>();

        void Traverse(NodeInfo node)
        {
            if (string.Equals(node.Kind, "TechniqueDeclaration", StringComparison.OrdinalIgnoreCase)
                || string.Equals(node.Kind, "Technique10Declaration", StringComparison.OrdinalIgnoreCase))
            {
                var technique = BuildTechnique(node, tokens, inference, symbols);
                if (technique is not null)
                {
                    techniques.Add(technique);
                }
            }

            foreach (var child in node.Children)
            {
                Traverse(child.Node);
            }
        }

        Traverse(rootNode);
        Validate(techniques, inference);
        return techniques;
    }

    private static FxTechniqueInfo? BuildTechnique(NodeInfo node, TokenLookup tokens, TypeInference inference, List<SymbolInfo> symbols)
    {
        var name = tokens.GetChildIdentifierText(node, "identifier");
        var body = GetChildNode(node, "body");
        var passes = new List<FxPassInfo>();

        if (body is not null)
        {
            foreach (var child in body.Children.Where(c => string.Equals(c.Role, "pass", StringComparison.OrdinalIgnoreCase)))
            {
                var pass = BuildPass(child.Node, tokens, symbols);
                if (pass is not null)
                {
                    passes.Add(pass);
                }
            }
        }

        if (name is null && node.Span is not null)
        {
            inference.AddDiagnostic("HLSL5001", "Technique is missing a name.", node.Span);
        }

        return new FxTechniqueInfo
        {
            Name = name,
            DeclNodeId = node.Id,
            Passes = passes,
            Span = node.Span
        };
    }

    private static FxPassInfo? BuildPass(NodeInfo node, TokenLookup tokens, List<SymbolInfo> symbols)
    {
        var name = tokens.GetChildIdentifierText(node, "identifier");
        var body = GetChildNode(node, "body");
        var shaders = new List<FxShaderBinding>();
        var states = new List<FxStateAssignment>();

        if (body?.Span is Span span)
        {
            foreach (var statement in tokens.ExtractStatements(span))
            {
                if (statement.Count == 0) continue;

                if (TryParseShaderBinding(statement, symbols, out var shader))
                {
                    shaders.Add(shader);
                    continue;
                }

                if (TryParseState(statement, out var state))
                {
                    states.Add(state);
                }
            }
        }

        return new FxPassInfo
        {
            Name = name,
            DeclNodeId = node.Id,
            Shaders = shaders,
            States = states,
            Span = node.Span
        };
    }

    private static bool TryParseShaderBinding(IReadOnlyList<TokenInfo> tokens, List<SymbolInfo> symbols, out FxShaderBinding shader)
    {
        shader = new FxShaderBinding();
        if (tokens.Count < 5) return false;

        var stage = StageFromIdentifier(tokens[0].Text);
        if (stage is null) return false;

        if (!string.Equals(tokens[1].Text, "=", StringComparison.Ordinal)) return false;
        if (!string.Equals(tokens[2].Text, "compile", StringComparison.OrdinalIgnoreCase)) return false;

        var profile = tokens[3].Text;
        var entry = tokens[4].Text;
        var symbolId = symbols.FirstOrDefault(s => string.Equals(s.Kind, "Function", StringComparison.OrdinalIgnoreCase)
            && string.Equals(s.Name, entry, StringComparison.OrdinalIgnoreCase))?.Id;

        shader = new FxShaderBinding
        {
            Stage = stage,
            Profile = profile,
            Entry = entry,
            EntrySymbolId = symbolId
        };
        return true;
    }

    private static bool TryParseState(IReadOnlyList<TokenInfo> tokens, out FxStateAssignment state)
    {
        state = new FxStateAssignment();
        if (tokens.Count < 3) return false;
        if (!string.Equals(tokens[1].Text, "=", StringComparison.Ordinal)) return false;

        state = new FxStateAssignment
        {
            Name = tokens[0].Text,
            Value = string.Join(" ", tokens.Skip(2).Select(t => t.Text))
        };
        return true;
    }

    private static void Validate(List<FxTechniqueInfo> techniques, TypeInference inference)
    {
        var techniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var technique in techniques)
        {
            if (!string.IsNullOrWhiteSpace(technique.Name) && !techniqueNames.Add(technique.Name))
            {
                inference.AddDiagnostic("HLSL5002", $"Duplicate technique '{technique.Name}'.", technique.Span);
            }

            var passNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pass in technique.Passes)
            {
                if (!string.IsNullOrWhiteSpace(pass.Name) && !passNames.Add(pass.Name))
                {
                    inference.AddDiagnostic("HLSL5003", $"Duplicate pass '{pass.Name}' in technique '{technique.Name}'.", pass.Span);
                }

                ValidatePass(pass, inference);
            }
        }
    }

        private static void ValidatePass(FxPassInfo pass, TypeInference inference)
        {
            var hasVs = pass.Shaders.Any(s => string.Equals(s.Stage, "Vertex", StringComparison.OrdinalIgnoreCase));
            var hasPs = pass.Shaders.Any(s => string.Equals(s.Stage, "Pixel", StringComparison.OrdinalIgnoreCase));
            var hasGs = pass.Shaders.Any(s => string.Equals(s.Stage, "Geometry", StringComparison.OrdinalIgnoreCase));
            var hasHs = pass.Shaders.Any(s => string.Equals(s.Stage, "Hull", StringComparison.OrdinalIgnoreCase));
            var hasDs = pass.Shaders.Any(s => string.Equals(s.Stage, "Domain", StringComparison.OrdinalIgnoreCase));
            var hasCs = pass.Shaders.Any(s => string.Equals(s.Stage, "Compute", StringComparison.OrdinalIgnoreCase));

            if (!pass.Shaders.Any())
            {
                inference.AddDiagnostic("HLSL5004", $"Pass '{pass.Name}' has no shader bindings.", pass.Span);
            }

        if (hasVs ^ hasPs)
        {
                inference.AddDiagnostic("HLSL5005", $"Pass '{pass.Name}' is missing a {(hasVs ? "Pixel" : "Vertex")} shader binding.", pass.Span);
            }

            // If any of GS/HS/DS/CS are present, warn if PS is missing (common FX expectation).
            if ((hasGs || hasHs || hasDs || hasCs) && !hasPs)
            {
                inference.AddDiagnostic("HLSL5007", $"Pass '{pass.Name}' includes advanced stages but is missing a Pixel shader binding.", pass.Span);
            }

            foreach (var shader in pass.Shaders)
            {
                var stageFromProfile = StageFromProfile(shader.Profile);
                if (!string.IsNullOrWhiteSpace(stageFromProfile) && !string.IsNullOrWhiteSpace(shader.Stage)
                && !string.Equals(stageFromProfile, shader.Stage, StringComparison.OrdinalIgnoreCase))
            {
                inference.AddDiagnostic("HLSL5006", $"Shader '{shader.Stage}' uses profile '{shader.Profile}' (stage '{stageFromProfile}').", pass.Span);
            }
        }
    }

    private static string StageFromProfile(string? profile)
    {
        if (string.IsNullOrWhiteSpace(profile)) return string.Empty;
        if (profile.StartsWith("vs", true, CultureInfo.InvariantCulture)) return "Vertex";
        if (profile.StartsWith("ps", true, CultureInfo.InvariantCulture)) return "Pixel";
        if (profile.StartsWith("gs", true, CultureInfo.InvariantCulture)) return "Geometry";
        if (profile.StartsWith("hs", true, CultureInfo.InvariantCulture)) return "Hull";
        if (profile.StartsWith("ds", true, CultureInfo.InvariantCulture)) return "Domain";
        if (profile.StartsWith("cs", true, CultureInfo.InvariantCulture)) return "Compute";
        return string.Empty;
    }

    private static string? StageFromIdentifier(string identifier)
    {
        if (identifier.Equals("VertexShader", StringComparison.OrdinalIgnoreCase)) return "Vertex";
        if (identifier.Equals("PixelShader", StringComparison.OrdinalIgnoreCase)) return "Pixel";
        if (identifier.Equals("GeometryShader", StringComparison.OrdinalIgnoreCase)) return "Geometry";
        if (identifier.Equals("HullShader", StringComparison.OrdinalIgnoreCase)) return "Hull";
        if (identifier.Equals("DomainShader", StringComparison.OrdinalIgnoreCase)) return "Domain";
        if (identifier.Equals("ComputeShader", StringComparison.OrdinalIgnoreCase)) return "Compute";
        return null;
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

internal static class SymbolBuilder
{
    public static SymbolBuildResult Build(NodeInfo rootNode, TokenLookup tokens, TypeInference typeInference)
    {
        var symbols = new List<SymbolInfo>();
        var typeCollector = new TypeCollector();
        var spans = new Dictionary<int, Span>();
        Traverse(rootNode, parentKind: null, currentFunctionId: null, currentStructId: null, symbols, typeCollector, spans, tokens, typeInference);
        return new SymbolBuildResult(
            symbols.OrderBy(s => s.Id ?? int.MaxValue).ToList(),
            typeCollector.ToList(),
            spans);
    }

    private static void Traverse(NodeInfo node, string? parentKind, int? currentFunctionId, int? currentStructId, List<SymbolInfo> symbols, TypeCollector types, Dictionary<int, Span> spans, TokenLookup tokens, TypeInference typeInference)
    {
        if (node.Id is not null && node.Span is not null)
        {
            if (!spans.ContainsKey(node.Id.Value))
            {
                spans[node.Id.Value] = node.Span.Value;
            }
        }

        switch (node.Kind)
        {
            case "BufferDeclaration":
                {
                    var cbufSym = BuildCBufferSymbol(node, tokens);
                    if (cbufSym is not null)
                    {
                        symbols.Add(cbufSym);
                        currentStructId = cbufSym.Id;
                    }
                }
                break;
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
            case "BufferBody":
                {
                    foreach (var member in tokens.ExtractBufferMembers(node.Span))
                    {
                        symbols.Add(new SymbolInfo
                        {
                            Kind = "CBufferMember",
                            Name = member.Name,
                            Type = member.Type,
                            ParentSymbolId = currentStructId,
                            DeclNodeId = node.Id
                        });
                    }
                }
                break;
            default:
                break;
        }

        foreach (var child in node.Children)
        {
            Traverse(child.Node, node.Kind, currentFunctionId, currentStructId, symbols, types, spans, tokens, typeInference);
        }
    }

    private static void HandleFunction(NodeInfo node, List<SymbolInfo> symbols, TypeCollector types, TokenLookup tokens, TypeInference typeInference)
    {
        if (node.Id is null) return;
        var name = tokens.GetChildIdentifierText(node, "identifier");
        var returnType = tokens.GetChildTypeText(node, "type") ?? "void";
        var returnSemantic = ParseSemanticString(tokens.GetAnnotationText(node));

        if (!string.IsNullOrWhiteSpace(name) && symbols.Any(s => string.Equals(s.Kind, "Function", StringComparison.OrdinalIgnoreCase) && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            typeInference.AddDiagnostic("HLSL1003", $"Duplicate function '{name}'.", node.Span);
            return;
        }

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
        if (!string.IsNullOrWhiteSpace(type) && (type.StartsWith("texture", StringComparison.OrdinalIgnoreCase) || type.Contains("buffer", StringComparison.OrdinalIgnoreCase)))
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

internal sealed class TokenLookup
{
    private readonly List<TokenInfo> _tokens;
    private readonly int _documentEnd;

    private TokenLookup(List<TokenInfo> tokens)
    {
        _tokens = tokens;
        _documentEnd = tokens.Count == 0 ? 0 : tokens.Max(t => t.End);
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

    public int DocumentLength => _documentEnd;

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

    public IReadOnlyList<IReadOnlyList<TokenInfo>> ExtractStatements(Span span)
    {
        var slice = _tokens
            .Where(t => t.Start >= span.Start && t.End <= span.End)
            .OrderBy(t => t.Start)
            .ToList();

        var statements = new List<IReadOnlyList<TokenInfo>>();
        var current = new List<TokenInfo>();
        foreach (var token in slice)
        {
            if (token.Text is "{" or "}")
            {
                continue;
            }

            if (token.Text == ";")
            {
                if (current.Count > 0)
                {
                    statements.Add(current.ToList());
                    current.Clear();
                }
                continue;
            }

            current.Add(token);
        }

        if (current.Count > 0)
        {
            statements.Add(current);
        }

        return statements;
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

    public IReadOnlyList<(string Type, string Name)> ExtractBufferMembers(Span? span)
    {
        if (span is null) return Array.Empty<(string Type, string Name)>();
        var slice = _tokens
            .Where(t => t.Start >= span.Value.Start && t.End <= span.Value.End)
            .OrderBy(t => t.Start)
            .ToList();

        var members = new List<(string Type, string Name)>();
        var current = new List<TokenInfo>();
        foreach (var token in slice)
        {
            if (token.Text == ";")
            {
                AddMember(current, members);
                current.Clear();
            }
            else
            {
                current.Add(token);
            }
        }
        AddMember(current, members);
        return members;
    }

    private static void AddMember(List<TokenInfo> tokens, List<(string Type, string Name)> members)
    {
        if (tokens.Count == 0) return;
        var filtered = tokens.Where(t => !IsDelimiter(t.Text) && !string.Equals(t.Text, "register", StringComparison.OrdinalIgnoreCase)).ToList();
        if (filtered.Count < 2) return;
        var type = filtered[0].Text;
        var name = filtered[1].Text;
        members.Add((type, name));
    }

    internal static bool IsModifier(string text)
    {
        return string.Equals(text, "in", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "out", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "inout", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDelimiter(string text)
    {
        return text is ":" or "," or ")" or "(" or "{" or "}";
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

}

internal sealed class TypeCollector
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

internal enum TypeKind
{
    Unknown,
    Scalar,
    Vector,
    Matrix,
    Array,
    Resource,
    Function
}

internal sealed record IntrinsicSignature
{
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<SemType> Parameters { get; init; } = System.Array.Empty<SemType>();
    public Func<IReadOnlyList<SemType>, SemType?> ReturnResolver { get; init; } = _ => null;
}

internal static class Intrinsics
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
        },
        new IntrinsicSignature
        {
            Name = "tex2D",
            Parameters = new [] { SemType.Resource("sampler2D"), SemType.Vector("float", 3) },
            ReturnResolver = _ => SemType.Vector("float", 4)
        },
        new IntrinsicSignature
        {
            Name = "tex2Dlod",
            Parameters = new [] { SemType.Resource("sampler2D"), SemType.Vector("float", 4) },
            ReturnResolver = _ => SemType.Vector("float", 4)
        },
        new IntrinsicSignature
        {
            Name = "tex2Dgrad",
            Parameters = new [] { SemType.Resource("sampler2D"), SemType.Vector("float", 2), SemType.Vector("float", 2), SemType.Vector("float", 2) },
            ReturnResolver = _ => SemType.Vector("float", 4)
        },
        new IntrinsicSignature
        {
            Name = "sin",
            Parameters = new [] { SemType.Scalar("float") },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "sin",
            Parameters = new [] { SemType.Vector("float", 2) },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "sin",
            Parameters = new [] { SemType.Vector("float", 3) },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "sin",
            Parameters = new [] { SemType.Vector("float", 4) },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "cos",
            Parameters = new [] { SemType.Scalar("float") },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "cos",
            Parameters = new [] { SemType.Vector("float", 2) },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "cos",
            Parameters = new [] { SemType.Vector("float", 3) },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "cos",
            Parameters = new [] { SemType.Vector("float", 4) },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "abs",
            Parameters = new [] { SemType.Scalar("float") },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "abs",
            Parameters = new [] { SemType.Vector("float", 2) },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "abs",
            Parameters = new [] { SemType.Vector("float", 3) },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "abs",
            Parameters = new [] { SemType.Vector("float", 4) },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "length",
            Parameters = new [] { SemType.Vector("float", 2) },
            ReturnResolver = _ => SemType.Scalar("float")
        },
        new IntrinsicSignature
        {
            Name = "length",
            Parameters = new [] { SemType.Vector("float", 3) },
            ReturnResolver = _ => SemType.Scalar("float")
        },
        new IntrinsicSignature
        {
            Name = "length",
            Parameters = new [] { SemType.Vector("float", 4) },
            ReturnResolver = _ => SemType.Scalar("float")
        },
        new IntrinsicSignature
        {
            Name = "cross",
            Parameters = new [] { SemType.Vector("float", 3), SemType.Vector("float", 3) },
            ReturnResolver = _ => SemType.Vector("float", 3)
        },
        new IntrinsicSignature
        {
            Name = "min",
            Parameters = new [] { SemType.Scalar("float"), SemType.Scalar("float") },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "min",
            Parameters = new [] { SemType.Vector("float", 2), SemType.Vector("float", 2) },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "min",
            Parameters = new [] { SemType.Vector("float", 3), SemType.Vector("float", 3) },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "min",
            Parameters = new [] { SemType.Vector("float", 4), SemType.Vector("float", 4) },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "max",
            Parameters = new [] { SemType.Scalar("float"), SemType.Scalar("float") },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "max",
            Parameters = new [] { SemType.Vector("float", 2), SemType.Vector("float", 2) },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "max",
            Parameters = new [] { SemType.Vector("float", 3), SemType.Vector("float", 3) },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "max",
            Parameters = new [] { SemType.Vector("float", 4), SemType.Vector("float", 4) },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "clamp",
            Parameters = new [] { SemType.Scalar("float"), SemType.Scalar("float"), SemType.Scalar("float") },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "clamp",
            Parameters = new [] { SemType.Vector("float", 2), SemType.Vector("float", 2), SemType.Vector("float", 2) },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "clamp",
            Parameters = new [] { SemType.Vector("float", 3), SemType.Vector("float", 3), SemType.Vector("float", 3) },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "clamp",
            Parameters = new [] { SemType.Vector("float", 4), SemType.Vector("float", 4), SemType.Vector("float", 4) },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "lerp",
            Parameters = new [] { SemType.Vector("float", 2), SemType.Vector("float", 2), SemType.Scalar("float") },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "lerp",
            Parameters = new [] { SemType.Vector("float", 3), SemType.Vector("float", 3), SemType.Scalar("float") },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "lerp",
            Parameters = new [] { SemType.Vector("float", 4), SemType.Vector("float", 4), SemType.Scalar("float") },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "ddx",
            Parameters = new [] { SemType.Scalar("float") },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "ddx",
            Parameters = new [] { SemType.Vector("float", 2) },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "ddx",
            Parameters = new [] { SemType.Vector("float", 3) },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "ddx",
            Parameters = new [] { SemType.Vector("float", 4) },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "ddy",
            Parameters = new [] { SemType.Scalar("float") },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "ddy",
            Parameters = new [] { SemType.Vector("float", 2) },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "ddy",
            Parameters = new [] { SemType.Vector("float", 3) },
            ReturnResolver = args => args.FirstOrDefault()
        },
        new IntrinsicSignature
        {
            Name = "ddy",
            Parameters = new [] { SemType.Vector("float", 4) },
            ReturnResolver = args => args.FirstOrDefault()
        }
    };

    public static SemType? Resolve(string name, IReadOnlyList<SemType?> args, TypeInference inference, Span? span)
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

        inference.AddDiagnostic("HLSL2001", $"No matching intrinsic overload for '{name}'.", span);
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

    public static bool IsIntrinsic(string name) =>
        Catalog.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
}

internal sealed record SemType
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

internal static class TypeCompatibility
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

internal sealed class TypeInference
{
    private readonly Dictionary<int, SemType> _nodeTypes = new();
    private readonly List<DiagnosticInfo> _diagnostics = new();
    private int? _documentLength;

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

    public void SetDocumentLength(int length)
    {
        _documentLength = length >= 0 ? length : null;
    }

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

    public void AddDiagnostic(string id, string message, Span? span = null)
    {
        _diagnostics.Add(new DiagnosticInfo
        {
            Severity = "Error",
            Id = id,
            Message = message,
            Span = NormalizeSpan(span)
        });
    }

    public IReadOnlyList<DiagnosticInfo> Diagnostics => _diagnostics;

    private DiagnosticSpan? NormalizeSpan(Span? span)
    {
        if (span is null)
        {
            return null;
        }

        var start = Math.Max(0, span.Value.Start);
        var end = Math.Max(start, span.Value.End);
        if (_documentLength is int length)
        {
            start = Math.Min(start, length);
            end = Math.Min(end, length);
        }

        return new DiagnosticSpan { Start = start, End = end };
    }
}

internal static class ExpressionTypeAnalyzer
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
                        else if (name is not null && (Intrinsics.IsIntrinsic(name) || IsTypeName(name)))
                        {
                            // Intrinsic or type name will be handled by call/constructor inference; suppress unknown-id diagnostic here.
                        }
                        else
                        {
                            typeInference.AddDiagnostic("HLSL2005", $"Unknown identifier '{name ?? "<unknown>"}'.", node.Span);
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
            inferred = Intrinsics.Resolve(calleeName!, argTypes, typeInference, node.Span);

            if (inferred is null)
            {
                if (symbolTypes.TryGetValue(calleeName!, out var fnType) && fnType is not null && fnType.Kind == TypeKind.Function)
                {
                    inferred = fnType.ReturnType;
                    CheckCallCompatibility(calleeName!, fnType, argTypes, typeInference, node.Span);
                }
                else
                {
                    var ctorType = typeInference.ParseType(calleeName);
                    if (ctorType is not null)
                    {
                        inferred = ctorType;
                        CheckConstructorArguments(calleeName!, ctorType, argTypes, typeInference, node.Span);
                    }
                }
            }
        }
        else
        {
            typeInference.AddDiagnostic("HLSL2003", "Call expression missing callee.", node.Span);
        }

        AddType(types, typeInference, node.Id, inferred);
    }

    private static void CheckCallCompatibility(string calleeName, SemType functionType, IReadOnlyList<SemType?> args, TypeInference typeInference, Span? span)
    {
        var parameters = functionType.ParameterTypes ?? Array.Empty<SemType>();
        if (parameters.Count != args.Count)
        {
            typeInference.AddDiagnostic("HLSL2001", $"Function '{calleeName}' expects {parameters.Count} arguments but got {args.Count}.", span);
            return;
        }

        for (var i = 0; i < parameters.Count; i++)
        {
            var arg = args[i];
            var param = parameters[i];
            if (arg is null || !TypeCompatibility.CanPromote(arg, param))
            {
                typeInference.AddDiagnostic("HLSL2001", $"Cannot convert argument {i} to parameter of type '{param}'.", span);
                return;
            }
        }
    }

        private static void CheckConstructorArguments(string calleeName, SemType targetType, IReadOnlyList<SemType?> args, TypeInference typeInference, Span? span)
        {
            if (args.Count == 0) return;

            static bool ArgCompatible(SemType? arg, string baseType)
            {
                if (arg is null) return false;
                return arg.Kind switch
                {
                    TypeKind.Scalar => TypeCompatibility.CanPromote(arg, SemType.Scalar(baseType)),
                    TypeKind.Vector or TypeKind.Matrix => string.Equals(arg.BaseType, baseType, StringComparison.OrdinalIgnoreCase),
                    _ => TypeCompatibility.CanPromote(arg, SemType.Scalar(baseType))
                };
            }

            if (targetType.Kind is TypeKind.Vector or TypeKind.Matrix)
            {
                var required = targetType.Kind == TypeKind.Vector
                    ? targetType.VectorSize
                    : targetType.Rows * targetType.Columns;

                var provided = 0;
                foreach (var arg in args)
                {
                    if (!ArgCompatible(arg, targetType.BaseType))
                    {
                        typeInference.AddDiagnostic("HLSL2001", $"Cannot type-call '{calleeName}' with provided arguments.", span);
                        return;
                    }
                    provided += CountComponents(arg);
            }

            if (provided != required)
            {
                typeInference.AddDiagnostic("HLSL2001", $"Constructor '{calleeName}' expects {required} components but got {provided}.", span);
            }
            return;
            }

            foreach (var arg in args)
            {
                if (!ArgCompatible(arg, targetType.BaseType))
                {
                    typeInference.AddDiagnostic("HLSL2001", $"Cannot type-call '{calleeName}' with provided arguments.", span);
                    return;
                }
            }
    }

    private static int CountComponents(SemType? type) =>
        type is null
            ? 0
            : type.Kind switch
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
            typeInference.AddDiagnostic("HLSL2002", $"Type mismatch in binary expression: '{lt}' vs '{rt}'.", node.Span);
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

internal static class EntryPointResolver
{
    public static List<EntryPointInfo> Resolve(List<SymbolInfo> symbols, string entryName, string profile, TypeInference inference, Dictionary<int, Span> spans)
    {
        var stage = StageFromProfile(profile);
        var entry = symbols.FirstOrDefault(s => string.Equals(s.Kind, "Function", StringComparison.OrdinalIgnoreCase)
            && string.Equals(s.Name, entryName, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            var fallback = symbols.FirstOrDefault(s => s.Kind == "Function");
            inference.AddDiagnostic("HLSL3001", $"No entry point function named '{entryName}' found.", spans.TryGetValue(fallback?.DeclNodeId ?? -1, out var span) ? span : null);
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

internal static class SemanticValidator
{
    public static void Validate(List<SymbolInfo> symbols, List<EntryPointInfo> entryPoints, string profile, TypeInference inference, Dictionary<int, Span> spans)
    {
        if (entryPoints.Count == 0) return;

        var entry = entryPoints[0];
        var stage = entry.Stage ?? "Unknown";
        var smMajor = ParseProfileMajor(profile);

        var function = symbols.FirstOrDefault(s => s.Id == entry.SymbolId);
        if (function is not null)
        {
            ValidateReturnSemantic(function.ReturnSemantic, stage, smMajor, inference, TryFindSpan(spans, function.DeclNodeId));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var param in symbols.Where(s => s.ParentSymbolId == entry.SymbolId && string.Equals(s.Kind, "Parameter", StringComparison.OrdinalIgnoreCase)))
        {
            var paramSpan = TryFindSpan(spans, param.DeclNodeId);
            ValidateParameterSemantic(param, stage, smMajor, inference, paramSpan);
            var key = SemanticKey(param.Semantic);
            if (!string.IsNullOrEmpty(key))
            {
                if (!seen.Add(key))
                {
                    inference.AddDiagnostic("HLSL3003", $"Duplicate semantic '{key}' on entry parameters.", paramSpan);
                }
            }
            else
            {
                inference.AddDiagnostic("HLSL3004", "Entry parameter missing semantic.", paramSpan);
            }
        }

        if (function is not null && function.ReturnSemantic is null)
        {
            inference.AddDiagnostic("HLSL3004", "Entry return value missing semantic.", TryFindSpan(spans, function.DeclNodeId));
        }
    }

        private static void ValidateReturnSemantic(SemanticInfo? semantic, string stage, int smMajor, TypeInference inference, Span? span)
        {
            if (semantic is null) return;
            if (smMajor < 4 && IsSystemValue(semantic.Name))
            {
                inference.AddDiagnostic("HLSL3002", $"System-value semantic '{semantic.Name}' is not allowed before SM4.", span);
            }
            if (stage == "Vertex" && string.Equals(semantic.Name, "SV_TARGET", StringComparison.OrdinalIgnoreCase))
            {
                inference.AddDiagnostic("HLSL3002", "Vertex shaders cannot return SV_TARGET.", span);
            }
            if (stage == "Pixel" && smMajor < 4)
            {
                var upper = semantic.Name.ToUpperInvariant();
                var isColor = upper.StartsWith("COLOR", StringComparison.OrdinalIgnoreCase);
                var isDepth = string.Equals(upper, "DEPTH", StringComparison.OrdinalIgnoreCase);
                if (!isColor && !isDepth && !IsSystemValue(upper))
                {
                    inference.AddDiagnostic("HLSL3002", $"Pixel shader return semantic '{semantic.Name}' is not valid for SM{smMajor}. Use COLORn or DEPTH.", span);
                }
                if (IsSystemValue(upper))
                {
                    inference.AddDiagnostic("HLSL3002", $"System-value semantic '{semantic.Name}' is not allowed before SM4.", span);
                }
            }

            if (smMajor >= 4 && !IsReturnSemanticAllowed(stage, semantic.Name))
            {
                inference.AddDiagnostic("HLSL3002", $"Semantic '{semantic.Name}' is not valid for {stage} shader return in SM{smMajor}.", span);
            }
        }

        private static void ValidateParameterSemantic(SymbolInfo param, string stage, int smMajor, TypeInference inference, Span? span)
        {
            if (param.Semantic is null) return;
            var name = param.Semantic.Name;

            if (smMajor < 4 && IsSystemValue(name))
            {
                inference.AddDiagnostic("HLSL3002", $"System-value semantic '{name}' is not allowed before SM4.", span);
            }

            if (smMajor < 4 && stage == "Pixel" && name.StartsWith("SV_", StringComparison.OrdinalIgnoreCase))
            {
                inference.AddDiagnostic("HLSL3002", $"Pixel shader parameter semantic '{name}' is not valid for SM{smMajor}. Use legacy TEXCOORD/COLOR semantics.", span);
            }

            if (smMajor < 4 && stage == "Pixel" && string.Equals(name, "SV_POSITION", StringComparison.OrdinalIgnoreCase))
            {
                inference.AddDiagnostic("HLSL3002", "Pixel shader parameters should not use SV_POSITION (use input TEXCOORD/position semantics).", span);
            }

            if (smMajor >= 4 && !IsParameterSemanticAllowed(stage, name))
            {
                inference.AddDiagnostic("HLSL3002", $"Semantic '{name}' is not valid for {stage} shader parameters in SM{smMajor}.", span);
            }
        }

        private static bool IsSystemValue(string name) => name.StartsWith("SV_", StringComparison.OrdinalIgnoreCase);

        private static string SemanticKey(SemanticInfo? semantic)
    {
        if (semantic is null) return string.Empty;
        return $"{semantic.Name}:{semantic.Index ?? 0}";
    }

    private static Span? TryFindSpan(Dictionary<int, Span> spans, int? nodeId)
    {
        if (nodeId is null) return null;
        return spans.TryGetValue(nodeId.Value, out var span) ? span : null;
    }

    private static int ParseProfileMajor(string profile)
    {
        var parts = profile.Split('_');
        if (parts.Length >= 2 && int.TryParse(parts[1].Split('.')[0], out var major))
        {
            return major;
            }
            return 0;
        }

        private static bool IsParameterSemanticAllowed(string stage, string name)
        {
            var upper = name.ToUpperInvariant();
            return stage switch
            {
                "Pixel" => upper is "SV_POSITION" or "SV_PRIMITIVEID" or "SV_SAMPLEINDEX" or "SV_ISFRONTFACE" or "SV_RENDERTARGETARRAYINDEX",
                "Vertex" => upper is "SV_VERTEXID" or "SV_INSTANCEID" or "SV_POSITION",
                _ => true // Other stages are left permissive for now.
            };
        }

        private static bool IsReturnSemanticAllowed(string stage, string name)
        {
            var upper = name.ToUpperInvariant();
            return stage switch
            {
                "Pixel" => upper is "SV_TARGET" or "SV_TARGET0" or "SV_TARGET1" or "SV_TARGET2" or "SV_TARGET3" or "SV_TARGET4" or "SV_TARGET5" or "SV_TARGET6" or "SV_TARGET7" or "SV_DEPTH" or "SV_COVERAGE",
                "Vertex" => upper is "SV_POSITION",
                _ => true // Other stages are left permissive for now.
            };
        }
    }

internal sealed record NodeInfo(int? Id, string? Kind, Span? Span, List<NodeChild> Children)
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

internal readonly record struct NodeChild(string Role, NodeInfo Node);

internal readonly record struct TokenInfo(int Start, int End, string Text);

internal readonly record struct Span(int Start, int End);

internal sealed record SymbolBuildResult(List<SymbolInfo> Symbols, List<TypeInfo> Types, Dictionary<int, Span> Spans);

public sealed record SemanticOutput
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

    [JsonPropertyName("techniques")]
    public IReadOnlyList<FxTechniqueInfo> Techniques { get; init; } = Array.Empty<FxTechniqueInfo>();

    [JsonPropertyName("diagnostics")]
    public IReadOnlyList<DiagnosticInfo> Diagnostics { get; init; } = Array.Empty<DiagnosticInfo>();
}

public sealed record SyntaxInfo
{
    [JsonPropertyName("rootId")]
    public int RootId { get; init; }

    [JsonPropertyName("nodes")]
    public IReadOnlyList<SyntaxNodeInfo> Nodes { get; init; } = Array.Empty<SyntaxNodeInfo>();
}

public sealed record SyntaxNodeInfo
{
    [JsonPropertyName("id")]
    public int? Id { get; init; }

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("span")]
    public SyntaxSpan? Span { get; init; }

    [JsonPropertyName("referencedSymbolId")]
    public int? ReferencedSymbolId { get; init; }

    [JsonPropertyName("referencedSymbolKind")]
    public string? ReferencedSymbolKind { get; init; }

    [JsonPropertyName("operator")]
    public string? Operator { get; init; }

    [JsonPropertyName("swizzle")]
    public string? Swizzle { get; init; }

    [JsonPropertyName("calleeName")]
    public string? CalleeName { get; init; }

    [JsonPropertyName("calleeKind")]
    public string? CalleeKind { get; init; }

    [JsonPropertyName("children")]
    public IReadOnlyList<SyntaxNodeChild> Children { get; init; } = Array.Empty<SyntaxNodeChild>();
}

public sealed record SyntaxNodeChild
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    [JsonPropertyName("nodeId")]
    public int? NodeId { get; init; }
}

public sealed record SyntaxSpan
{
    [JsonPropertyName("start")]
    public int Start { get; init; }

    [JsonPropertyName("end")]
    public int End { get; init; }
}

public sealed record EntryPointInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("symbolId")]
    public int? SymbolId { get; init; }

    [JsonPropertyName("stage")]
    public string? Stage { get; init; }

    [JsonPropertyName("profile")]
    public string? Profile { get; init; }
}

public sealed record FxTechniqueInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("declNodeId")]
    public int? DeclNodeId { get; init; }

    [JsonPropertyName("passes")]
    public IReadOnlyList<FxPassInfo> Passes { get; init; } = Array.Empty<FxPassInfo>();

    [JsonIgnore]
    internal Span? Span { get; init; }
}

public sealed record FxPassInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("declNodeId")]
    public int? DeclNodeId { get; init; }

    [JsonPropertyName("shaders")]
    public IReadOnlyList<FxShaderBinding> Shaders { get; init; } = Array.Empty<FxShaderBinding>();

    [JsonPropertyName("states")]
    public IReadOnlyList<FxStateAssignment> States { get; init; } = Array.Empty<FxStateAssignment>();

    [JsonIgnore]
    internal Span? Span { get; init; }
}

public sealed record FxShaderBinding
{
    [JsonPropertyName("stage")]
    public string Stage { get; init; } = string.Empty;

    [JsonPropertyName("profile")]
    public string? Profile { get; init; }

    [JsonPropertyName("entry")]
    public string? Entry { get; init; }

    [JsonPropertyName("entrySymbolId")]
    public int? EntrySymbolId { get; init; }
}

public sealed record FxStateAssignment
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public string? Value { get; init; }
}

public sealed record SymbolInfo
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

public sealed record SemanticInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("index")]
    public int? Index { get; init; }
}

public sealed record TypeInfo
{
    [JsonPropertyName("nodeId")]
    public int? NodeId { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }
}

public sealed record DiagnosticInfo
{
    [JsonPropertyName("severity")]
    public string Severity { get; init; } = "Error";

    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("span")]
    public DiagnosticSpan? Span { get; init; }
}

public sealed record DiagnosticSpan
{
    [JsonPropertyName("start")]
    public int Start { get; init; }

    [JsonPropertyName("end")]
    public int End { get; init; }
}

