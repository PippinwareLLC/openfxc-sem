# Tests (openfxc-sem)

Suite layout mirrors `openfxc-hlsl`:
- `OpenFXC.Sem.Tests/`: unit, negative, snapshot, and integration tests for semantic analysis.
- `fixtures/`: small HLSL AST inputs (JSON) and expected diagnostics.
- `snapshots/`: golden semantic JSON outputs for representative shaders (e.g., SM2 passthrough VS, SM4 cbuffer VS).
- `run-all.*`: convenience scripts to run the full suite locally.
- Integration smoke parses DXSDK sample shaders via the `openfxc-hlsl` submodule (e.g., `samples/dxsdk/.../snow.fx`) to produce ASTs before semantic analysis.
- DXSDK sweep: `SmokeTests` walks all `samples/dxsdk/**/*.fx` files, parses them with `openfxc-hlsl`, and ensures `openfxc-sem analyze` succeeds (default is a single sample; set `OPENFXC_SEM_FX_SWEEP=all` to enable full sweep).

Run locally:
- Windows: `tests\\run-all.cmd`
- Bash: `tests/run-all.sh`

Note: expand fixtures/snapshots and tests alongside semantic features (see `docs/TDD.md`).
