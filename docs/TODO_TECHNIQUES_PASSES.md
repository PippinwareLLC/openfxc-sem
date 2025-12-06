# TODO: Techniques & Passes Support

## TP1: FX AST traversal
- [ ] Walk `Technique`/`Pass` nodes from the `openfxc-hlsl` AST.
- [ ] Capture per-pass shader bindings (VS/PS/GS/HS/DS/CS entry names/profiles).
- [ ] Capture state blocks/assignments (render states, sampler states, annotations if present).

## TP2: Semantic model & schema
- [ ] Extend `SemanticOutput` to include techniques/passes:
  - Techniques list, passes per technique.
  - Shaders per stage (entry name/profile) for each pass.
  - State assignments (as a neutral key/value/state-name model).
  - Optional annotations (if modeled).
- [ ] Bump `formatVersion` if schema changes materially.
- [ ] Add doc examples and sample JSON.

## TP3: Validation
- [ ] Diagnostics for missing required shaders per pass (e.g., VS/PS expected).
- [ ] Diagnostics for duplicate pass/technique names.
- [ ] Validate state legality per profile/stage (basic guards).
- [ ] Validate shader/profile compatibility for bound entries.

## TP4: Tests & snapshots
- [ ] Unit/integration tests for techniques with multiple passes and mixed shader stages.
- [ ] Negative tests: missing shaders, invalid states, duplicate passes, incompatible profiles.
- [ ] Snapshot fixtures for representative FX files (positive + negative).

## TP5: Docs & CLI
- [ ] Update README and `docs/SEMANTIC_ANALYZER.md` with FX support, schema, and limitations.
- [ ] Clarify CLI/library usage and outputs when FX constructs are present.
- [ ] Update compatibility matrix to reflect FX support level once implemented.
