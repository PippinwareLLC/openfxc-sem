# Milestones: Techniques & Passes Support

- [x] **TP1: FX AST traversal**
  - [x] Traverse technique/pass nodes from `openfxc-hlsl` AST.
  - [x] Collect pass shader bindings (VS/PS/GS/HS/DS/CS entry references).
  - [x] Capture state blocks/assignments (render states, sampler states) in a neutral form.

- [x] **TP2: Semantic model & schema**
  - [x] Define semantic JSON extensions for techniques/passes: technique list, passes, shaders per stage, state assignments, annotations (if any).
  - [x] Integrate into `SemanticOutput` with version bump if necessary.
  - [x] Document the schema and add sample outputs.

- [x] **TP3: Validation**
  - [x] Validate required shaders per pass (e.g., VS/PS present where applicable); profile compatibility of bound shaders.
  - [x] Validate state legality per profile/stage (basic guards).
  - [x] Emit diagnostics for missing/duplicate/invalid shader bindings and illegal states.

- [x] **TP4: Tests & snapshots**
  - [x] Add unit/integration tests covering techniques with multiple passes and mixed shader stages.
  - [x] Add negative tests (missing shaders, invalid states, duplicate passes).
  - [x] Add snapshot fixtures for representative FX files.

- [x] **TP5: Docs & CLI**
  - [x] Update docs (README, SEMANTIC_ANALYZER.md) with FX support and schema.
  - [x] Clarify CLI/library behavior for FX constructs; note any limitations.
  - [x] Review compatibility matrix to reflect FX support level.
