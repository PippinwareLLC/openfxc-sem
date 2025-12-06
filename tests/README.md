# Tests (openfxc-sem)

Suite layout mirrors `openfxc-hlsl`:
- `OpenFXC.Sem.Tests/`: unit, negative, and integration tests for semantic analysis.
- `fixtures/`: small HLSL AST inputs (JSON) and expected diagnostics.
- `snapshots/`: golden semantic JSON outputs for representative shaders across SM1–SM5.
- `run-all.*`: convenience scripts to run the full suite locally.

Run locally:
- Windows: `tests\\run-all.cmd`
- Bash: `tests/run-all.sh`

Note: initial suite is scaffolded; populate fixtures/snapshots and expand tests alongside semantic features (see `docs/TDD.md`).
