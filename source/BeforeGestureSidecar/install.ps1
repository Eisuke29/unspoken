<#
  BEFORE gesture sidecar — install script.
  Creates a PROJECT-LOCAL virtual environment (.venv) and installs the pinned
  dependencies. Does NOT modify the global Python installation.

  After install, verifies: Python version, mediapipe.__version__, import cv2,
  cv2.__version__, import numpy. If cv2 cannot be imported it STOPS and reports
  rather than stacking additional OpenCV packages.

  Run from anywhere:
    powershell -ExecutionPolicy Bypass -File Tools\BeforeGestureSidecar\install.ps1
#>
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$venv = Join-Path $here '.venv'

# Pick a Python 3.13 interpreter without touching global state.
$py = $null
if (Get-Command py -ErrorAction SilentlyContinue) { $py = 'py -3.13' }
elseif (Get-Command python -ErrorAction SilentlyContinue) { $py = 'python' }
else { throw 'No Python launcher found (py / python).' }

Write-Host "==> Creating project-local venv at $venv"
if (-not (Test-Path $venv)) {
    Invoke-Expression "$py -m venv `"$venv`""
}

$vpy = Join-Path $venv 'Scripts\python.exe'
if (-not (Test-Path $vpy)) { throw "venv python not found at $vpy" }

Write-Host '==> Upgrading pip inside the venv'
& $vpy -m pip install --upgrade pip

Write-Host '==> Installing pinned requirements'
& $vpy -m pip install -r (Join-Path $here 'requirements.txt')

Write-Host '==> Verifying environment'
& $vpy (Join-Path $here 'src\before_gesture\verify_env.py')
Write-Host '==> install.ps1 complete'
