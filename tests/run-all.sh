#!/usr/bin/env bash
set -euo pipefail

if [[ -z "${OPENFXC_SEM_FX_SWEEP:-}" ]]; then
  echo "OPENFXC_SEM_FX_SWEEP not set: running fast suite (single DXSDK sample)."
else
  echo "OPENFXC_SEM_FX_SWEEP=${OPENFXC_SEM_FX_SWEEP}: enabling DXSDK sweep."
fi

dotnet test tests/OpenFXC.Sem.Tests/OpenFXC.Sem.Tests.csproj "$@"
