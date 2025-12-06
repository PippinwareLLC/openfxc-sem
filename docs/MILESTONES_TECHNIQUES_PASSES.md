# Milestones: Techniques & Passes Support

- [ ] **TP1: FX AST traversal**
  - [ ] Traverse technique/pass nodes from `openfxc-hlsl` AST.
  - [ ] Collect pass shader bindings (VS/PS/GS/HS/DS/CS entry references).
  - [ ] Capture state blocks/assignments (render states, sampler states) in a neutral form.

- [ ] **TP2: Semantic model & schema**
  - [ ] Define semantic JSON extensions for techniques/passes: technique list, passes, shaders per stage, state assignments, annotations (if any).
  - [ ] Integrate into `SemanticOutput` with version bump if necessary.
  - [ ] Document the schema and add sample outputs.

- [ ] **TP3: Validation**
  - [ ] Validate required shaders per pass (e.g., VS/PS present where applicable); profile compatibility of bound shaders.
  - [ ] Validate state legality per profile/stage (basic guards).
  - [ ] Emit diagnostics for missing/duplicate/invalid shader bindings and illegal states.

- [ ] **TP4: Tests & snapshots**
  - [ ] Add unit/integration tests covering techniques with multiple passes and mixed shader stages.
  - [ ] Add negative tests (missing shaders, invalid states, duplicate passes).
  - [ ] Add snapshot fixtures for representative FX files.

- [ ] **TP5: Docs & CLI**
  - [ ] Update docs (README, SEMANTIC_ANALYZER.md) with FX support and schema.
  - [ ] Clarify CLI/library behavior for FX constructs; note any limitations.
  - [ ] Review compatibility matrix to reflect FX support level.
