# openfxc-sem
The semantic analyzer stage for OpenFXC. It consumes the JSON AST produced by `openfxc-hlsl`, builds a semantic model (symbols, types, semantics), and emits JSON ready for IR lowering.

## Overview
- Pipeline: `openfxc-hlsl parse` -> `openfxc-sem analyze` -> IR lowering.
- Goals: accept SM1-SM5 era HLSL/FX, produce a semantic model plus diagnostics without crashing (partial models are allowed when errors exist).
- Non-goals: IR/DXBC generation, optimization, full profile legalization, or preprocessor evaluation beyond what is represented in the AST.

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

## Definition of Done (initial)
- CLI contract honored with required `--profile` and optional `--entry`.
- Symbols cover all declaration kinds; types cover expressions and declarations.
- Intrinsic resolution and semantics binding implemented with diagnostics.
- Entry point resolution works and is recorded in the output.
- Diagnostics are stable (IDs like `HLSL2xxx`) and emitted instead of crashing.
- Interoperates cleanly with `openfxc-hlsl` JSON outputs.

## Docs
- Full spec/TDD: `docs/TDD.md`
