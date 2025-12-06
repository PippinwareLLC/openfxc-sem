@echo off
REM Run the full openfxc-sem test suite. Pass OPENFXC_SEM_FX_SWEEP=all to enable the DXSDK sweep.
setlocal
if "%OPENFXC_SEM_FX_SWEEP%"=="" (
  echo OPENFXC_SEM_FX_SWEEP not set: running fast suite (single DXSDK sample).
) else (
  echo OPENFXC_SEM_FX_SWEEP=%OPENFXC_SEM_FX_SWEEP%: enabling DXSDK sweep.
)
dotnet test tests/OpenFXC.Sem.Tests/OpenFXC.Sem.Tests.csproj %*
endlocal
