#!/usr/bin/env bash
#
# Builds the native ifc-lite FFI library (the `ifc-lite-ffi` crate) from the
# `external/ifc-lite` git submodule and prints the path to the resulting shared
# library.
#
# The `server-release` profile is mandatory: the default `release` profile uses
# `panic = 'abort'`, which would turn the parser's panic guards into no-ops and
# crash the host process instead of returning an error code.
#
# Usage:
#   build/build-native.sh
#
# Requires: a working Rust toolchain (cargo) on PATH.

set -euo pipefail

# Resolve the repository root from this script's location, so the script works
# regardless of the directory it is invoked from.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
IFC_LITE_DIR="${REPO_ROOT}/external/ifc-lite"

if [[ ! -d "${IFC_LITE_DIR}" ]]; then
  echo "error: submodule not found at ${IFC_LITE_DIR}" >&2
  echo "       run: git submodule update --init --recursive" >&2
  exit 1
fi

if ! command -v cargo >/dev/null 2>&1; then
  echo "error: 'cargo' was not found on PATH. Install the Rust toolchain (https://rustup.rs)." >&2
  exit 1
fi

echo "Building ifc-lite-ffi (profile: server-release) in ${IFC_LITE_DIR} ..."
(
  cd "${IFC_LITE_DIR}"
  cargo build --profile server-release -p ifc-lite-ffi
)

TARGET_DIR="${IFC_LITE_DIR}/target/server-release"

# Report whichever platform artifact was produced.
for name in libifc_lite_ffi.so libifc_lite_ffi.dylib ifc_lite_ffi.dll; do
  if [[ -f "${TARGET_DIR}/${name}" ]]; then
    echo "Built native library: ${TARGET_DIR}/${name}"
    exit 0
  fi
done

echo "warning: build completed but no native library was found in ${TARGET_DIR}" >&2
exit 1
