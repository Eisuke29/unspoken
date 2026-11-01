<#
  BEFORE gesture sidecar — bench launcher.
  Runs the skeleton-only diagnostic recognition bench using the project-local
  venv. Unity Editor must NOT be running / using the camera at the same time.

    powershell -ExecutionPolicy Bypass -File Tools\BeforeGestureSidecar\run_bench.ps1
#>
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$vpy = Join-Path $here '.venv\Scripts\python.exe'
if (-not (Test-Path $vpy)) { throw 'venv not found. Run install.ps1 first.' }
$env:PYTHONPATH = Join-Path $here 'src'
& $vpy -m before_gesture.bench_app @args
