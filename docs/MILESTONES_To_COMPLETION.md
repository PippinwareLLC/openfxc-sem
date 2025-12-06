# Milestones To Completion (Semantics)

- [x] **MSC1: SM1.x / SM2.x / SM3.x tightening**
  - [x] Tighten semantics validation by stage/profile (legacy vs SV rules) with diagnostics.
  - [x] Broaden intrinsic coverage for SM1–3 era builtins/texture ops.
  - [x] Add targeted tests locking stage/profile semantic rules and intrinsic behaviors.

- [x] **MSC2: SM4 completion**
  - [x] Stage 1: Finish semantics binding/validation for all SV_* cases with stage/profile guards (SM4-only), with focused tests.
  - [x] Stage 2: Expand SM4 intrinsic/resource coverage and add positive/negative tests.
  - [x] Stage 3: Refresh/add SM4 snapshots (VS/PS with SV_* semantics, cbuffer/tbuffer) plus negative cases.
  - [x] Stage 4: Update docs/compatibility matrix and mark SM4 complete.

- [x] **MSC3: SM5 completion**
  - [x] Complete structured/RW resource edge cases and intrinsic/type coverage.
  - [x] Add SM5 snapshots and negative tests to lock behavior.
  - [x] Ensure profile-aware semantics validation matches SM5 expectations.

- [x] **MSC4: FX constructs (.fx) stance**
  - [x] Decide on technique/pass semantics: implement or explicitly diagnose/disable.
  - [x] Document stance and add tests to lock behavior.

- [x] **MSC5: Docs & test backstops**
  - [x] Update compatibility matrix, TODO, dev logs as milestones close.
  - [x] Ensure tests cover profile/semantic guardrails and intrinsic/resource matrices before marking complete.
