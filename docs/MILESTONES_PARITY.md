# Milestones: SM1–SM5 Parity Lock

- [x] **P1: Intrinsics/Builtins Audit**
  - [x] Catalog remaining SM1–3 texture/builtin variants (tex2D*, tex*lod/grad) and SM4/5 sample/LOD/grad/resource intrinsics.
  - [x] Add positive/negative tests to pin accepted/denied signatures; update snapshots where outputs change.

- [x] **P2: Semantics Edge Cases**
  - [x] Add guardrail tests for SV/legacy semantics per stage/profile: PS SV_POSITION usage, SV_SampleIndex/SV_IsFrontFace/SV_PrimitiveID, VS SV_Target rejection, legacy semantics on SM4+, reserved SV_* in SM1–3.
  - [x] Ensure diagnostics (HLSL3002/3003/3004) fire correctly across SM1–SM5.

- [x] **P3: FX State Validation (optional)**
  - [x] Validate pass shader completeness beyond VS/PS when present (GS/HS/DS/CS), and basic state legality per profile.
  - [x] Add tests for invalid/duplicate/missing state assignments if modeled.

- [x] **P4: Snapshot Expansion**
  - [x] Add SM1–3 golden outputs (projected texture sampling, mixed legacy semantics) and SM4–5 SV_*/resource cases.
  - [x] Add negative snapshots for out-of-profile intrinsics/semantics.

- [x] **P5: Clean Build & Docs**
  - [x] Resolve remaining nullable warning in SemanticAnalyzer.
  - [x] Update README/SEMANTIC_ANALYZER with covered intrinsic/semantic guardrails and any schema tweaks.
  - [x] Confirm compatibility matrix reflects parity status and bump formatVersion only if schema changes.
