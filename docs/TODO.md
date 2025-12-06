# TODO

## Foundation
- [x] Stand up `openfxc-sem analyze` CLI that reads `openfxc-hlsl` AST JSON from stdin/file and emits semantic JSON per `docs/TDD.md`.
- [x] Establish semantic JSON schema (formatVersion, profile metadata, symbols, types, entryPoints, diagnostics) and pin sample outputs (smoke tests cover shape).
- [ ] Stabilize diagnostic IDs/messages (e.g., HLSL2xxx), spans, and error recovery so partial models are always produced.

## Symbol Table
- [x] Collect symbols for globals, locals, parameters, functions, structs/typedefs, samplers/resources, cbuffers/tbuffers.
- [x] Capture parent/child relationships (e.g., parameters under functions) and declaration node links.
- [ ] Tests: symbol presence for each category across SM1-SM5.

## Type System
- [x] Type construction for scalars, vectors, matrices, arrays, resource types, and function signatures (structured SemType).
- [x] Expression inference for arithmetic, swizzles, indexing, calls, constructors, casts.
- [x] Tests: positive and negative inference cases; mismatches flagged with diagnostics (constructor overfill, binary mismatch, array declarators).

## Intrinsics and Builtins
- [x] Implement intrinsic table (initial set: mul, dot, normalize, saturate, tex2D).
- [x] Type-check intrinsic calls, enforce argument counts, and choose result types deterministically.
- [x] Tests: correct usages per intrinsic plus negative misuse (bad arity/types).
- [ ] Extend coverage to more intrinsics and texture variants (SM3+/SM4+).

## Semantics and Entry Points
- [ ] Normalize legacy semantics (POSITION0, COLOR0) and SV_* forms; associate indices where present, validate against stage/profile. (Basic uppercasing and SM4 system-value guards done; broaden validation/index handling.)
- [x] Bind parameter/return semantics and record in symbols/types; uppercase normalization applied.
- [x] Entry resolution: default main or --entry, stage derived from profile (with diagnostic when missing).
- [ ] Tests: parameter/return semantics validation, SV targets, entry selection/ambiguity, missing/invalid semantics diagnostics. (Initial coverage for normalization, missing/duplicate semantics, missing entry; expand stage/profile cases.)

## Profile Awareness
- [x] Carry profile metadata through analysis for entry-point stage mapping.
- [ ] Optional guards for profile/semantic mismatches (e.g., SV semantics under SM2-only).
- [ ] Tests: profile propagation and any enforced guardrails.

## Diagnostics
- [ ] Unknown identifier, type mismatch, wrong argument count, duplicate symbol, intrinsic misuse.
- [ ] JSON span stability (0 <= start <= end <= length) across all diagnostics and semantic bindings.
- [ ] Tests: targeted negative cases and snapshot stability.

## Integration and Snapshots
- [ ] Golden semantic JSON snapshots for representative shaders (VS passthrough, texture PS, SM4/5 cbuffer).
- [ ] Integration path: `openfxc-hlsl parse` -> `openfxc-sem analyze` smoke runs for SM1-SM5 (DXSDK sweep gated).
- [ ] CLI smoke tests for stdin/file IO and required options (--profile, --entry).
- [ ] Source fixtures from samples/ in this repo; generate AST JSON via the `openfxc-hlsl` submodule to feed semantic tests.

## Tooling and CI
- [ ] Test runner scripts (tests/run-all.* ) covering unit, negative, snapshot, and CLI smoke suites.
- [ ] Update README and docs as surfaces evolve; keep TODO/MILESTONES in sync with progress.
- [ ] Add devlog entries for significant changes and test runs.
