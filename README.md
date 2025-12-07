# openfxc-sem
The semantic analyzer stage for OpenFXC. It consumes the JSON AST produced by `openfxc-hlsl`, builds a semantic model (symbols, types, semantics), and emits JSON ready for IR lowering.

## Origin
openfxc-sem is a distilled standalone CLI tool peeled from the larger (currently private) OpenFXC project.

## Overview
- Pipeline: `openfxc-hlsl parse` -> `openfxc-sem analyze` -> IR lowering.
- Goals: accept SM1-SM5 era HLSL/FX, produce a semantic model plus diagnostics without crashing (partial models are allowed when errors exist).
- Non-goals: IR/DXBC generation, optimization, full profile legalization, or preprocessor evaluation beyond what is represented in the AST.

## Architectural Boundary
- `openfxc-sem` is backend-agnostic: no DX9/DXBC opcode/register decisions, packing rules, or hardware limits.
- Downstream passes handle legalization and lowering (e.g., profile checks, DX9 instruction selection, DXBC emission).
- Semantic analysis focuses solely on HLSL meaning: symbols, types, semantics, intrinsic resolution, and entry discovery.

## CLI
```
openfxc-sem analyze [options] < input.ast.json > output.sem.json
```
- `--profile <name>` (required), e.g., `vs_2_0`, `ps_5_0`.
- `--entry <name>` (optional, default `main`).
- Exit codes: `0` = analysis completed (diagnostics allowed); `1` = internal error such as I/O or JSON parse failure.
- Typical pipeline:
  ```
  openfxc-hlsl parse file.hlsl --format json > file.ast.json
  openfxc-sem analyze --profile vs_3_0 < file.ast.json > file.sem.json
  ```
- AST inputs are produced by the `openfxc-hlsl` submodule (see `openfxc-hlsl/`), keeping parsing and semantics decoupled.

## Parser (openfxc-hlsl) usage
- Core library: `openfxc-hlsl/src/OpenFXC.Hlsl/OpenFXC.Hlsl.csproj` produces `OpenFXC.Hlsl.dll` with `HlslLexer` and `Parser`.
- CLI wrapper: `openfxc-hlsl/src/openfxc-hlsl/openfxc-hlsl.csproj` references the library; use it for `lex`/`parse` commands.
- Example (C#):
  ```csharp
  var text = File.ReadAllText("shader.hlsl");
  var (tokens, lexDiagnostics) = HlslLexer.Lex(text);
  var (root, parseDiagnostics) = Parser.Parse(tokens, text.Length);
  ```
  Reference the project directly or the built DLL to consume the lexer/parser from other tools.

## Semantic core (library)
- Core library: `src/OpenFXC.Sem.Core/OpenFXC.Sem.Core.csproj` produces `OpenFXC.Sem.Core.dll` with the analyzer API.
- Public entry point: `var analyzer = new SemanticAnalyzer(profile: "vs_3_0", entry: "main", inputJson); var output = analyzer.Analyze();`
- Input JSON should come from `openfxc-hlsl` (do not synthesize ASTs manually). You can call the parser API directly and pass the serialized AST string to the analyzer without writing to disk.
- CLI wrapper (`src/openfxc-sem/openfxc-sem.csproj`) references the core library and just handles argument parsing and I/O.
- Output `formatVersion`: `3` (includes `techniques` list for FX files and a flattened syntax node table for lowering).

## Build
- Prereq: initialize the parser submodule: `git submodule update --init --recursive`
- Build core (Debug): `dotnet build src/OpenFXC.Sem.Core/OpenFXC.Sem.Core.csproj`
- Build CLI (Debug): `dotnet build src/openfxc-sem/openfxc-sem.csproj`
- Build (Release single-file):
  - Windows (x64): `dotnet publish src/openfxc-sem/openfxc-sem.csproj -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true`
  - Linux (x64): `dotnet publish src/openfxc-sem/openfxc-sem.csproj -c Release -r linux-x64 -p:PublishSingleFile=true -p:SelfContained=true`
- macOS Intel: `dotnet publish src/openfxc-sem/openfxc-sem.csproj -c Release -r osx-x64 -p:PublishSingleFile=true -p:SelfContained=true`
  - macOS Apple Silicon: `dotnet publish src/openfxc-sem/openfxc-sem.csproj -c Release -r osx-arm64 -p:PublishSingleFile=true -p:SelfContained=true`
- Artifacts land under `src/openfxc-sem/bin/<Configuration>/net8.0/<rid>/publish/`; add `-p:PublishTrimmed=true` if you want smaller binaries (verify before distributing).
- AOT (no single-file): `dotnet publish src/openfxc-sem/openfxc-sem.csproj -c Release -r win-x64 -p:PublishAot=true -p:SelfContained=true` (do not combine `PublishAot` with `PublishSingleFile`). System.Text.Json will warn under trim/AOT; use a source-generated `JsonSerializerContext` if you need to suppress it.

## Semantic Model (output)
High-level shape of `output.sem.json`:
- Metadata: `formatVersion`, selected `profile`, and syntax info (`rootId` plus flattened `nodes` with kind/children/spans and `referencedSymbolId` for lowering).
- Symbols: functions, parameters, locals, globals, structs/typedefs, samplers/resources, and cbuffers/tbuffers (SM4/5).
- Types: assigned to every declaration and expression.
- Entry points: resolved function, stage inferred from profile, entry metadata.
- Diagnostics: stable IDs/messages/spans; emitted even when only a partial model can be built.

## Responsibilities
- Build a symbol table covering functions, parameters, locals, globals, structs, resources, and buffers.
- Infer types for expressions, declarations, and returns across scalars, vectors, matrices, arrays, and resource types.
- Resolve intrinsics/builtins (e.g., `mul`, `dot`, `normalize`, `tex2D`) with correct signatures and diagnostics on misuse.
- Parse and normalize semantics (`POSITION0`, `COLOR0`, `SV_Position`, `SV_Target1`, etc.) for parameters and returns.
- Resolve entry points (default `main` or `--entry`), tie them to the selected profile, and record stage information.
- Never crash on malformed input; always return diagnostics alongside any partial semantic model.

## Error Coverage
- Unknown identifier, type mismatch, wrong argument counts, duplicate symbols.
- Ill-typed intrinsic calls (bad argument types or counts).
- Optional profile-aware checks (e.g., SV semantics on SM2-only profiles) if enforced at this stage.

## Testing Strategy (from `docs/TDD.md`)
- Unit tests for symbol table construction, type inference patterns, and intrinsic resolution.
- Snapshot/golden tests for full shaders comparing entire semantic JSON outputs.
- Negative tests for ill-typed shaders and missing entries.
- Profile-aware cases across SM1-SM5 expectations.
- Integration smoke: `openfxc-hlsl parse` -> `openfxc-sem analyze` on representative shaders (passthrough VS, texture PS, SM4/5 cbuffer examples).

## Testing (commands)
- Run all tests: `dotnet test tests/OpenFXC.Sem.Tests/OpenFXC.Sem.Tests.csproj`
- Convenience: `tests/run-all.cmd` (Windows) or `tests/run-all.sh` (bash) — respects `OPENFXC_SEM_FX_SWEEP=all` for DXSDK sweep.
- Suite layout: see `tests/README.md` (fixtures, snapshots, and unit/negative/integration tests).
- Sample shaders for tests live under `samples/` (owned by this repo); integration smokes parse DXSDK samples via the `openfxc-hlsl` submodule before semantic analysis.
- Default fast test target: a single DXSDK sample (`snow.fx`). Set `OPENFXC_SEM_FX_SWEEP=all` to sweep all `samples/dxsdk/**/*.fx` files.
- Latest full run: 1314 tests, 0 failed, 0 skipped (16.2s).

## Docs
- Full spec/TDD: `docs/TDD.md`
- Work queue: `docs/TODO.md`
- Milestones: `docs/MILESTONES.md`
- Techniques/Passes: `docs/MILESTONES_TECHNIQUES_PASSES.md`, `docs/TODO_TECHNIQUES_PASSES.md`
- Parity: `docs/MILESTONES_PARITY.md`

## Attribution
- DXSDK samples under `samples/dxsdk/` are from Microsoft DirectX SDK (Aug 2008), used here for integration testing.
- Additional test shaders and snapshots under `tests/snapshots/` are adapted for coverage within this project.

## Compatibility Matrix (Semantics)

| Shader Model / Era | Semantics Coverage | Notes |
| --- | --- | --- |
| SM1.x (legacy D3D9) | Complete | Symbols/types recorded; structured SemType model for swizzles/binaries/indexing; constructor arity/shape validation with diagnostics; semantics normalized/bound with stage/profile guards on SV/legacy semantics; intrinsics expanded (sin/cos/abs/length/cross/min/max/clamp/lerp/tex2D variants); snapshots/negatives; entryPoints emitted; backend-agnostic |
| SM2.x / SM3.x | Complete | Symbols/types/semantics for params/globals/resources; structured typing for expressions; constructor/binary mismatch negatives covered; semantics normalized/bound with stage/profile guards (SV blocked pre-SM4 for VS/PS params/returns); intrinsics expanded (including tex2D projected); snapshots/negatives; entryPoints emitted; backend-agnostic |
| SM4.x | Complete | cbuffer symbols/members captured; semantics binding/validation for SV_* with stage/profile guards; SM4 intrinsics/resources covered; snapshots included; backend-agnostic |
| SM5.x | Complete | Structured/RW resources recognized with symbols/types; SM5 semantics/intrinsics/resource coverage locked by snapshots/tests; backend-agnostic |
| FX constructs (.fx) | Complete | Techniques/passes captured with shader bindings and render states; diagnostics for missing VS/PS bindings, duplicate techniques/passes, and profile mismatches |
