# TODO

## Foundation
- [ ] Stand up `openfxc-sem analyze` CLI that reads `openfxc-hlsl` AST JSON from stdin/file and emits semantic JSON per `docs/TDD.md`.
- [ ] Establish semantic JSON schema (`formatVersion`, profile metadata, symbols, types, entryPoints, diagnostics) and pin sample outputs.
- [ ] Stabilize diagnostic IDs/messages (e.g., `HLSL2xxx`), spans, and error recovery so partial models are always produced.

## Symbol Table
- [ ] Collect symbols for globals, locals, parameters, functions, structs/typedefs, samplers/resources, cbuffers/tbuffers.
- [ ] Capture parent/child relationships (e.g., parameters under functions) and declaration node links.
- [ ] Tests: symbol presence for each category across SM1–SM5.

## Type System
- [ ] Type construction for scalars, vectors, matrices, arrays, resource types, and function signatures.
- [ ] Expression inference for arithmetic, swizzles, indexing, calls, constructors, casts.
- [ ] Tests: positive and negative inference cases; mismatches flagged with diagnostics.

## Intrinsics and Builtins
- [ ] Implement intrinsic table (e.g., `mul`, `dot`, `normalize`, `saturate`, `tex2D`, texture sampling variants).
- [ ] Type-check intrinsic calls, enforce argument counts, and choose result types deterministically.
- [ ] Tests: correct usages per intrinsic plus negative misuse (bad arity/types).

## Semantics and Entry Points
- [ ] Normalize legacy semantics (`POSITION0`, `COLOR0`) and `SV_*` forms; associate indices where present.
- [ ] Bind parameter/return semantics and record in symbols/types as needed.
- [ ] Entry resolution: default `main` or `--entry`, stage derived from profile.
- [ ] Tests: parameter/return semantics, SV targets, entry selection, missing entry diagnostics.

## Profile Awareness
- [ ] Carry profile metadata through analysis for diagnostics and entry-point stage mapping.
- [ ] Optional guards for profile/semantic mismatches (e.g., SV semantics under SM2-only).
- [ ] Tests: profile propagation and any enforced guardrails.

## Diagnostics
- [ ] Unknown identifier, type mismatch, wrong argument count, duplicate symbol, intrinsic misuse.
- [ ] JSON span stability (`0 <= start <= end <= length`) across all diagnostics and semantic bindings.
- [ ] Tests: targeted negative cases and snapshot stability.

## Integration and Snapshots
- [ ] Golden semantic JSON snapshots for representative shaders (VS passthrough, texture PS, SM4/5 cbuffer).
- [ ] Integration path: `openfxc-hlsl parse` -> `openfxc-sem analyze` smoke runs for SM1–SM5.
- [ ] CLI smoke tests for stdin/file IO and required options (`--profile`, `--entry`).

## Tooling and CI
- [ ] Test runner scripts (`tests/run-all.*`) covering unit, negative, snapshot, and CLI smoke suites.
- [ ] Update README and docs as surfaces evolve; keep TODO/MILESTONES in sync with progress.
- [ ] Add devlog entries for significant changes and test runs.
