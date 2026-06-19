#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the native ifc-lite FFI library (the `ifc-lite-ffi` crate) from the
    `external/ifc-lite` git submodule and prints the path to `ifc_lite_ffi.dll`.

.DESCRIPTION
    Intended for Windows CI. The `server-release` profile is mandatory: the
    default `release` profile uses `panic = 'abort'`, which would turn the
    parser's panic guards into no-ops and crash the host process instead of
    returning an error code.

.EXAMPLE
    pwsh build/build-native.ps1

.NOTES
    Requires a working Rust toolchain (cargo) on PATH.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Resolve the repository root from this script's location, so the script works
# regardless of the directory it is invoked from.
$scriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot   = Resolve-Path (Join-Path $scriptDir '..')
$ifcLiteDir = Join-Path $repoRoot 'external/ifc-lite'

if (-not (Test-Path -Path $ifcLiteDir -PathType Container)) {
    Write-Error "Submodule not found at $ifcLiteDir. Run: git submodule update --init --recursive"
}

if (-not (Get-Command cargo -ErrorAction SilentlyContinue)) {
    Write-Error "'cargo' was not found on PATH. Install the Rust toolchain (https://rustup.rs)."
}

Write-Host "Building ifc-lite-ffi (profile: server-release) in $ifcLiteDir ..."

Push-Location $ifcLiteDir
try {
    cargo build --profile server-release -p ifc-lite-ffi
    if ($LASTEXITCODE -ne 0) {
        Write-Error "cargo build failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

$dllPath = Join-Path $ifcLiteDir 'target/server-release/ifc_lite_ffi.dll'

if (-not (Test-Path -Path $dllPath -PathType Leaf)) {
    Write-Error "Build completed but $dllPath was not found."
}

Write-Host "Built native library: $dllPath"
