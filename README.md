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

## Build
- Prereq: initialize the parser submodule: `git submodule update --init --recursive`
- Build (Debug): `dotnet build src/openfxc-sem/openfxc-sem.csproj`
- Build (Release single-file):
  - Windows (x64): `dotnet publish src/openfxc-sem/openfxc-sem.csproj -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true`
  - Linux (x64): `dotnet publish src/openfxc-sem/openfxc-sem.csproj -c Release -r linux-x64 -p:PublishSingleFile=true -p:SelfContained=true`
  - macOS Intel: `dotnet publish src/openfxc-sem/openfxc-sem.csproj -c Release -r osx-x64 -p:PublishSingleFile=true -p:SelfContained=true`
  - macOS Apple Silicon: `dotnet publish src/openfxc-sem/openfxc-sem.csproj -c Release -r osx-arm64 -p:PublishSingleFile=true -p:SelfContained=true`
- Artifacts land under `src/openfxc-sem/bin/<Configuration>/net8.0/<rid>/publish/`; add `-p:PublishTrimmed=true` if you want smaller binaries (verify before distributing).

## Semantic Model (output)
High-level shape of `output.sem.json`:
- Metadata: `formatVersion`, selected `profile`, and a pointer back to the syntax root.
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
- Convenience: `tests/run-all.cmd` (Windows) or `tests/run-all.sh` (bash)
- Suite layout: see `tests/README.md` (fixtures, snapshots, and unit/negative/integration tests). The suite is scaffolded; expand alongside semantic features per `docs/TDD.md`.
- Sample shaders for tests live under `samples/` (owned by this repo); generate AST fixtures from them via the `openfxc-hlsl` submodule before semantic snapshotting.

## Docs
- Full spec/TDD: `docs/TDD.md`
- Work queue: `docs/TODO.md`
- Milestones: `docs/MILESTONES.md`

## Compatibility Matrix (Semantics)

| Shader Model / Era | Semantics Coverage | Notes |
| --- | --- | --- |
| SM1.x (legacy D3D9) | Planned/early | Symbols/types/semantics for legacy samplers/textures and basic expressions; no backend decisions |
| SM2.x / SM3.x | Planned/early | Parameter/return semantics, sampler/resource symbols, intrinsic resolution for texture math; backend-agnostic |
| SM4.x | Planned/early | cbuffer/tbuffer symbols, resource types, semantics binding (including SV_*), entry resolution |
| SM5.x | Planned/early | Structured/RW resources, semantics binding, intrinsics/type inference; backend-agnostic |
| FX constructs (.fx) | Planned/early | Technique/pass semantics not computed; semantic analysis focuses on shader entry functions |
