# Release checklist (openfxc-sem)

- [ ] Ensure `openfxc-hlsl` submodule is initialized and up to date (`git submodule update --init --recursive`).
- [ ] Regenerate snapshots if semantics change: run `dotnet test tests/OpenFXC.Sem.Tests/OpenFXC.Sem.Tests.csproj` and verify snapshot diffs are intentional.
- [ ] Optional: run full DXSDK sweep (`OPENFXC_SEM_FX_SWEEP=all tests/run-all.sh` or `.cmd`) to catch regressions.
- [ ] Update `docs/TODO.md`, `docs/MILESTONES.md`, and README compatibility matrix to reflect current status.
- [ ] Add a dev log entry summarizing changes and test runs.
- [ ] Tag/version only after all tests pass and snapshots are green.
