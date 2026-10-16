#!/bin/bash
# Usage: build/build-guard-macos.sh. Builds chroma-local-guard for osx-arm64 and osx-x64 with zig cc, in a Linux container: zig
# carries the headers and the libraries of macOS, and its linker signs arm64 executables ad hoc, as macOS requires. Output in
# artifacts/guard/<rid>/.
set -euo pipefail
export ZIG_VERSION=0.14.1
export ZIG_SHA256=24aeeec8af16c381934a6cd7d95c807a8cb2cf7df9fa40d359aa884195c4716c
root=$(cd "$(dirname "$0")/.." && (pwd -W 2>/dev/null || pwd))
mkdir -p "$root/artifacts/guard"
export MSYS_NO_PATHCONV=1
docker rm -f chromadotnet-local-guard-macos >/dev/null 2>&1 || true
docker run --rm --name chromadotnet-local-guard-macos --platform linux/amd64 \
  -v "$root/guard:/guard:ro" -v "$root/artifacts/guard:/out" -e ZIG_VERSION -e ZIG_SHA256 \
  quay.io/pypa/manylinux_2_28_x86_64 bash -c '
set -euo pipefail
cd /tmp
curl -sSfL -o zig.tar.xz "https://ziglang.org/download/$ZIG_VERSION/zig-x86_64-linux-$ZIG_VERSION.tar.xz"
echo "$ZIG_SHA256  zig.tar.xz" | sha256sum -c - >/dev/null
tar -xJf zig.tar.xz
zig=/tmp/zig-x86_64-linux-$ZIG_VERSION/zig
for pair in osx-arm64:aarch64-macos osx-x64:x86_64-macos; do
  rid=${pair%%:*}; target=${pair#*:}
  mkdir -p /out/$rid
  "$zig" cc -target "$target" -O2 -Wall -Wextra -Werror -o /out/$rid/chroma-local-guard /guard/chroma-local-guard.c
  # LC_CODE_SIGNATURE (0x1d) among the load commands of the Mach-O file.
  signed=$(python3 -c "
import struct, sys
data = open(sys.argv[1], \"rb\").read()
ncmds = struct.unpack_from(\"<I\", data, 16)[0]
offset, found = 32, False
for _ in range(ncmds):
    cmd, size = struct.unpack_from(\"<II\", data, offset)
    found |= cmd == 0x1d
    offset += size
print(\"yes\" if found else \"no\")
" /out/$rid/chroma-local-guard)
  {
    echo "source: guard/chroma-local-guard.c"
    echo "compiler: zig $ZIG_VERSION cc -target $target -O2"
    echo "code signature (LC_CODE_SIGNATURE): $signed"
    echo "sha256: $(sha256sum /out/$rid/chroma-local-guard | cut -d" " -f1)"
  } | tee /out/$rid/BUILD-INFO.txt
done
'
