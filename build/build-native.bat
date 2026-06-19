@echo off
REM ===========================================================================
REM Builds the native ifc-lite FFI library (the `ifc-lite-ffi` crate) from the
REM `external/ifc-lite` git submodule and reports the path to ifc_lite_ffi.dll.
REM
REM This is the Command Prompt counterpart to build-native.ps1 / build-native.sh.
REM
REM The `server-release` profile is mandatory: the default `release` profile uses
REM `panic = 'abort'`, which would turn the parser's panic guards into no-ops and
REM crash the host process instead of returning an error code.
REM
REM Usage (from anywhere):
REM     build\build-native.bat
REM
REM Requires a working Rust toolchain (cargo) on PATH.
REM ===========================================================================

setlocal

REM Resolve the repository root from this script's location (%~dp0 is the folder
REM holding this .bat, with a trailing backslash), so it works from any CWD.
set "SCRIPT_DIR=%~dp0"
for %%I in ("%SCRIPT_DIR%..") do set "REPO_ROOT=%%~fI"
set "IFC_LITE_DIR=%REPO_ROOT%\external\ifc-lite"

if not exist "%IFC_LITE_DIR%\" (
    echo error: submodule not found at "%IFC_LITE_DIR%" 1>&2
    echo        run: git submodule update --init --recursive 1>&2
    exit /b 1
)

where cargo >nul 2>nul
if errorlevel 1 (
    echo error: 'cargo' was not found on PATH. Install the Rust toolchain ^(https://rustup.rs^). 1>&2
    exit /b 1
)

echo Building ifc-lite-ffi ^(profile: server-release^) in "%IFC_LITE_DIR%" ...
pushd "%IFC_LITE_DIR%"
cargo build --profile server-release -p ifc-lite-ffi
set "CARGO_EXIT=%errorlevel%"
popd

if not "%CARGO_EXIT%"=="0" (
    echo error: cargo build failed with exit code %CARGO_EXIT%. 1>&2
    exit /b %CARGO_EXIT%
)

set "DLL_PATH=%IFC_LITE_DIR%\target\server-release\ifc_lite_ffi.dll"
if not exist "%DLL_PATH%" (
    echo error: build completed but "%DLL_PATH%" was not found. 1>&2
    exit /b 1
)

echo Built native library: %DLL_PATH%
exit /b 0
