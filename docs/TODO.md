# TODO

## Foundation
- [x] Stand up `openfxc-sem analyze` CLI that reads `openfxc-hlsl` AST JSON from stdin/file and emits semantic JSON per `docs/TDD.md`.
- [x] Establish semantic JSON schema (formatVersion, profile metadata, symbols, types, entryPoints, diagnostics) and pin sample outputs (smoke tests cover shape).
- [x] Stabilize diagnostic IDs/messages (e.g., HLSL2xxx), spans, and error recovery so partial models are always produced.

## Library Split (M8.3)
- [x] Create class library project `src/OpenFXC.Sem.Core/OpenFXC.Sem.Core.csproj`.
- [x] Move semantic analyzer types (SemanticAnalyzer, SymbolBuilder, TokenLookup, TypeInference, Intrinsics, SemType/TypeCompatibility, ExpressionTypeAnalyzer, EntryPointResolver, SemanticValidator) into the core library.
- [x] Expose output-facing records as public: SemanticOutput, SyntaxInfo, EntryPointInfo, SymbolInfo, SemanticInfo, TypeInfo, DiagnosticInfo, DiagnosticSpan.
- [x] Keep helper types internal: TokenLookup, TypeCollector, IntrinsicSignature, SemType, TypeInference, etc.
- [x] Add a public entry point in the core (e.g., `SemanticAnalyzer.Analyze()` instance method; optional `SemanticApi` static wrapper if desired).
- [x] Add ProjectReference from CLI to core; remove analyzer code from CLI project.
- [x] Refactor CLI `Program.cs` to thin wrapper: parse args, read input, instantiate SemanticAnalyzer, serialize output.
- [ ] Update tests to build core once and reference the library; ensure no CLI duplication.
- [x] Update README with library usage (namespace/API sample) and build instructions reflecting the split.
- [ ] Add devlog entry and mark TODO/MILESTONES complete when done.

## Symbol Table
- [x] Collect symbols for globals, locals, parameters, functions, structs/typedefs, samplers/resources, cbuffers/tbuffers.
- [x] Capture parent/child relationships (e.g., parameters under functions) and declaration node links.
- [ ] Tests: symbol presence for each category across SM1-SM5.
- [x] SM4/SM5 resource shapes: capture cbuffer/tbuffer contents and structured/RW resources with normalized types.

## Type System
- [x] Type construction for scalars, vectors, matrices, arrays, resource types, and function signatures (structured SemType).
- [x] Expression inference for arithmetic, swizzles, indexing, calls, constructors, casts.
- [x] Tests: positive and negative inference cases; mismatches flagged with diagnostics (constructor overfill, binary mismatch, array declarators).
- [x] SM4/SM5 type coverage for structured/RW resources and cbuffer/tbuffer members.

## Intrinsics and Builtins
- [x] Implement intrinsic table (initial set: mul, dot, normalize, saturate, tex2D).
- [x] Type-check intrinsic calls, enforce argument counts, and choose result types deterministically.
- [x] Tests: correct usages per intrinsic plus negative misuse (bad arity/types).
- [ ] Extend coverage to more intrinsics and texture variants (SM3+/SM4+), including SM4/SM5 resource overloads.

## Semantics and Entry Points
- [ ] Normalize legacy semantics (POSITION0, COLOR0) and SV_* forms; associate indices where present, validate against stage/profile. (Basic uppercasing and SM4 system-value guards done; broaden validation/index handling, legacy vs SV compatibility per stage/profile.)
- [x] Bind parameter/return semantics and record in symbols/types; uppercase normalization applied.
- [x] Entry resolution: default main or --entry, stage derived from profile (with diagnostic when missing).
- [ ] Tests: parameter/return semantics validation, SV targets, entry selection/ambiguity, missing/invalid semantics diagnostics. (Initial coverage for normalization, missing/duplicate semantics, missing entry; expand stage/profile cases across SM1-SM5.)
- [ ] FX constructs: either compute technique/pass semantics or document/diagnose non-support; add tests to lock behavior.

## Profile Awareness
- [x] Carry profile metadata through analysis for entry-point stage mapping.
- [ ] Optional guards for profile/semantic mismatches (e.g., SV semantics under SM2-only, stage-semantic compatibility).
- [ ] Tests: profile propagation and enforced guardrails across SM1-SM5.

## Diagnostics
- [x] Unknown identifier, type mismatch, wrong argument count, duplicate symbol, intrinsic misuse.
- [x] JSON span stability (0 <= start <= end <= length) across all diagnostics and semantic bindings.
- [ ] Tests: targeted negative cases and snapshot stability.

## Integration and Snapshots
- [ ] Golden semantic JSON snapshots for representative shaders (VS passthrough, texture PS, SM4/5 cbuffer, SM5 structured/RW resource cases). (Snapshots added for SM2 VS/PS, SM4 cbuffer, SM5 structured buffer.)
- [x] Integration path: `openfxc-hlsl parse` -> `openfxc-sem analyze` smoke runs for SM1-SM5 (DXSDK sweep gated; full sweep via env opt-in).
- [x] CLI smoke tests for stdin/file IO and required options (--profile, --entry).
- [ ] Source fixtures from samples/ in this repo; generate AST JSON via the `openfxc-hlsl` submodule to feed semantic tests.
- [x] Compatibility matrix kept in sync with snapshot coverage and SM-era feature completion.

## Tooling and CI
- [x] Test runner scripts (tests/run-all.* ) covering unit, negative, snapshot, and CLI smoke suites (DXSDK sweep gated via env).
- [x] Update README and docs as surfaces evolve; keep TODO/MILESTONES in sync with progress.
- [x] Add devlog entries for significant changes and test runs.
- [x] Release checklist added (`docs/RELEASE_CHECKLIST.md`).
- [ ] Add build/use docs for the new semantic core library (namespace/API sample) and ensure CLI references it.
