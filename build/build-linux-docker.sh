#!/bin/bash
# Usage: build/build-linux-docker.sh x64|arm64. Starts build-linux.sh in the manylinux_2_28 image of the architecture, with Docker;
# arm64 runs under emulation on an x64 machine. The sources, the toolchain and the cargo target stay in named volumes between runs.
# Output in artifacts/linux/<rid>/. BUILD_ID=name builds with a cargo target of its own into artifacts/linux-name/, to compare two
# builds from scratch.
set -euo pipefail
arch=${1:?x64 or arm64}
case "$arch" in
  x64) image=quay.io/pypa/manylinux_2_28_x86_64; platform=linux/amd64 ;;
  arm64) image=quay.io/pypa/manylinux_2_28_aarch64; platform=linux/arm64 ;;
  *) echo "x64 or arm64" >&2; exit 2 ;;
esac
root=$(cd "$(dirname "$0")/.." && (pwd -W 2>/dev/null || pwd))
id=${BUILD_ID:+-$BUILD_ID}
name=chromadotnet-local-build-$arch$id
mkdir -p "$root/artifacts/linux$id"
export MSYS_NO_PATHCONV=1
docker rm -f "$name" >/dev/null 2>&1 || true
docker run --rm --name "$name" --platform "$platform" \
  -v "chromadotnet-local-src-$arch:/src" -v "chromadotnet-local-target-$arch$id:/target" -v "chromadotnet-local-opt-$arch:/opt" \
  -v "$root/build:/build:ro" -v "$root/guard:/guard:ro" -v "$root/artifacts/linux$id:/out" \
  "$image" bash /build/build-linux.sh
