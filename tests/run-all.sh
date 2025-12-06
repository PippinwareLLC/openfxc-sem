#!/usr/bin/env bash
set -euo pipefail
dotnet test tests/OpenFXC.Sem.Tests/OpenFXC.Sem.Tests.csproj "$@"
