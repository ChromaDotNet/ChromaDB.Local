#!/bin/bash
# Usage: build/third-party-notices.sh. Writes THIRD-PARTY-NOTICES.md with cargo about, in the container and the volumes of the x64
# build (run build/build-linux-docker.sh x64 first).
set -euo pipefail
export ABOUT_VERSION=0.8.4
root=$(cd "$(dirname "$0")/.." && (pwd -W 2>/dev/null || pwd))
export MSYS_NO_PATHCONV=1
docker rm -f chromadotnet-local-notices >/dev/null 2>&1 || true
docker run --rm --name chromadotnet-local-notices --platform linux/amd64 \
  -v chromadotnet-local-src-x64:/src -v chromadotnet-local-opt-x64:/opt -v chromadotnet-local-about:/about \
  -v "$root/build/notices:/notices:ro" -v "$root:/repo" -e ABOUT_VERSION \
  quay.io/pypa/manylinux_2_28_x86_64@sha256:1e7b8460ad8fb3fb0fbe1cd818845159ac896b4fd17452d56e892c291f3b97a8 bash -c '
set -euo pipefail
export RUSTUP_HOME=/opt/rustup CARGO_HOME=/opt/cargo PATH=/about/bin:/opt/cargo/bin:$PATH
[ -x /about/bin/cargo-about ] || cargo +1.92.0 install --locked --root /about cargo-about --version "$ABOUT_VERSION" 2>&1 | tail -2
cd /src/chroma/rust/cli
cargo +1.92.0 about generate --locked -c /notices/about.toml /notices/notices.hbs -o /repo/THIRD-PARTY-NOTICES.md 2>&1 | grep -v "^\s*$" | tail -30
'
