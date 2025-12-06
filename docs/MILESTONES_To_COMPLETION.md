# Milestones To Completion (Semantics)

- [x] **MSC1: SM1.x / SM2.x / SM3.x tightening**
  - [x] Tighten semantics validation by stage/profile (legacy vs SV rules) with diagnostics.
  - [x] Broaden intrinsic coverage for SM1–3 era builtins/texture ops.
  - [x] Add targeted tests locking stage/profile semantic rules and intrinsic behaviors.

- [ ] **MSC2: SM4 completion**
  - [ ] Stage 1: Finish semantics binding/validation for all SV_* cases with stage/profile guards (SM4-only), with focused tests.
  - [ ] Stage 2: Expand SM4 intrinsic/resource coverage and add positive/negative tests.
  - [ ] Stage 3: Refresh/add SM4 snapshots (VS/PS with SV_* semantics, cbuffer/tbuffer) plus negative cases.
  - [ ] Stage 4: Update docs/compatibility matrix and mark SM4 complete.

- [ ] **MSC3: SM5 completion**
  - [ ] Complete structured/RW resource edge cases and intrinsic/type coverage.
  - [ ] Add SM5 snapshots and negative tests to lock behavior.
  - [ ] Ensure profile-aware semantics validation matches SM5 expectations.

- [ ] **MSC4: FX constructs (.fx) stance**
  - [ ] Decide on technique/pass semantics: implement or explicitly diagnose/disable.
  - [ ] Document stance and add tests to lock behavior.

- [ ] **MSC5: Docs & test backstops**
  - [ ] Update compatibility matrix, TODO, dev logs as milestones close.
  - [ ] Ensure tests cover profile/semantic guardrails and intrinsic/resource matrices before marking complete.
