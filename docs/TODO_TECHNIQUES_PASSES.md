# TODO: Techniques & Passes Support

## TP1: FX AST traversal
- [x] Walk `Technique`/`Pass` nodes from the `openfxc-hlsl` AST.
- [x] Capture per-pass shader bindings (VS/PS/GS/HS/DS/CS entry names/profiles).
- [x] Capture state blocks/assignments (render states, sampler states, annotations if present).

## TP2: Semantic model & schema
- [x] Extend `SemanticOutput` to include techniques/passes:
  - Techniques list, passes per technique.
  - Shaders per stage (entry name/profile) for each pass.
  - State assignments (as a neutral key/value/state-name model).
  - Optional annotations (if modeled).
- [x] Bump `formatVersion` if schema changes materially.
- [x] Add doc examples and sample JSON.

## TP3: Validation
- [x] Diagnostics for missing required shaders per pass (e.g., VS/PS expected).
- [x] Diagnostics for duplicate pass/technique names.
- [x] Validate state legality per profile/stage (basic guards).
- [x] Validate shader/profile compatibility for bound entries.

## TP4: Tests & snapshots
- [x] Unit/integration tests for techniques with multiple passes and mixed shader stages.
- [x] Negative tests: missing shaders, invalid states, duplicate passes, incompatible profiles.
- [x] Snapshot fixtures for representative FX files (positive + negative).

## TP5: Docs & CLI
- [x] Update README and `docs/SEMANTIC_ANALYZER.md` with FX support, schema, and limitations.
- [x] Clarify CLI/library usage and outputs when FX constructs are present.
- [x] Update compatibility matrix to reflect FX support level once implemented.
