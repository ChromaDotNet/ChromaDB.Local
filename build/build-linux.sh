#!/bin/bash
# Builds the Chroma CLI from its tag, with no change to the code, on manylinux_2_28 (AlmaLinux 8, glibc 2.28), in release
# without debug info, and the guard next to it. Runs inside quay.io/pypa/manylinux_2_28_x86_64 or quay.io/pypa/manylinux_2_28_aarch64:
# see build-linux.ps1, which starts the container. Output in /out/<rid>/: chroma, chroma-local-guard, BUILD-INFO.txt.
set -euo pipefail

CLI_TAG=${CLI_TAG:-cli-1.4.4}
CLI_COMMIT=${CLI_COMMIT:-11f3c743}
RUST_TOOLCHAIN=${RUST_TOOLCHAIN:-1.92.0}
PROTOC_VERSION=${PROTOC_VERSION:-31.1}
RUSTUP_VERSION=1.29.1

case "$(uname -m)" in
  x86_64) RID=linux-x64; PROTOC_ARCH=x86_64; RUST_HOST=x86_64-unknown-linux-gnu
    RUSTUP_SHA256=dda7234360b7f578ca8b0ddcb80145646fa61a67c1720a5abc7051b35c9fcb71
    PROTOC_SHA256=96553041f1a91ea0efee963cb16f462f5985b4d65365f3907414c360044d8065 ;;
  aarch64) RID=linux-arm64; PROTOC_ARCH=aarch_64; RUST_HOST=aarch64-unknown-linux-gnu
    RUSTUP_SHA256=15f6e4ce9f583b929c996c91562bad6d4454f3281de858b02cdfdef615fac433
    PROTOC_SHA256=6c554de11cea04c56ebf8e45b54434019b1cd85223d4bbd25c282425e306ecc2 ;;
  *) echo "unsupported machine $(uname -m)" >&2; exit 1 ;;
esac

dnf install -y -q git unzip file binutils >/dev/null 2>&1 || true
echo "rid: $RID, $(ldd --version | head -1)"

export RUSTUP_HOME=/opt/rustup CARGO_HOME=/opt/cargo PATH=/opt/cargo/bin:$PATH
if [ ! -x /opt/cargo/bin/rustup ]; then
  # rustup-init of a fixed version, checked against its digest.
  curl -sSfL -o /tmp/rustup-init "https://static.rust-lang.org/rustup/archive/$RUSTUP_VERSION/$RUST_HOST/rustup-init"
  echo "$RUSTUP_SHA256  /tmp/rustup-init" | sha256sum -c - >/dev/null
  chmod +x /tmp/rustup-init
  /tmp/rustup-init -y --profile minimal --default-toolchain none >/dev/null 2>&1
  # rustup stays at the version checked above.
  /opt/cargo/bin/rustup set auto-self-update disable >/dev/null
fi
rustup toolchain install --no-self-update "$RUST_TOOLCHAIN" --profile minimal >/dev/null 2>&1
if [ ! -x /opt/protoc/bin/protoc ]; then
  curl -sSfL -o /tmp/protoc.zip "https://github.com/protocolbuffers/protobuf/releases/download/v$PROTOC_VERSION/protoc-$PROTOC_VERSION-linux-$PROTOC_ARCH.zip"
  echo "$PROTOC_SHA256  /tmp/protoc.zip" | sha256sum -c - >/dev/null
  unzip -oq /tmp/protoc.zip -d /opt/protoc
fi
export PROTOC=/opt/protoc/bin/protoc

if [ ! -d /src/chroma/.git ]; then
  git clone -q --filter=blob:none https://github.com/chroma-core/chroma /src/chroma
fi
cd /src/chroma
git fetch -q --tags origin
git checkout -q --force "$CLI_TAG"
commit=$(git rev-parse HEAD)
case "$commit" in "$CLI_COMMIT"*) ;; *) echo "tag $CLI_TAG is $commit, expected $CLI_COMMIT" >&2; exit 1 ;; esac
test -z "$(git status --porcelain)"
echo "commit: $commit, no local change"
# The binary embeds files of the tree with their modification time: all of them get the time of the commit, so that two builds of
# the same tag give the same bytes.
commit_time=$(git log -1 --format=%ct)
git ls-files -z | xargs -0 touch -h -d "@$commit_time"
export SOURCE_DATE_EPOCH=$commit_time

# The release profile of the workspace has debug = 2, which makes the Linux binary of the release of Chroma 510 MB: the environment overrides it,
# with no change to the code. The toolchain is the one of rust-toolchain.toml at the tag.
export CARGO_PROFILE_RELEASE_DEBUG=0 CARGO_PROFILE_RELEASE_STRIP=symbols CARGO_TARGET_DIR=/target
start=$(date +%s)
cargo "+$RUST_TOOLCHAIN" build --locked --bin chroma --release --manifest-path rust/cli/Cargo.toml 2>&1 | tail -3
echo "build seconds: $(( $(date +%s) - start ))"

mkdir -p "/out/$RID"
cp /target/release/chroma "/out/$RID/chroma"
# The guard: plain C, with the compiler of the image, so that it needs the same glibc as chroma.
gcc -O2 -Wall -Wextra -Werror -o "/out/$RID/chroma-local-guard" /guard/chroma-local-guard.c
strip "/out/$RID/chroma-local-guard"

glibc=$(objdump -T "/out/$RID/chroma" "/out/$RID/chroma-local-guard" | grep -o 'GLIBC_[0-9.]*' | sort -V | uniq | tail -1)
{
  echo "tag: $CLI_TAG"
  echo "commit: $commit"
  echo "image: $(. /etc/os-release; echo "$PRETTY_NAME"), $(ldd --version | head -1)"
  echo "rustc: $(rustc "+$RUST_TOOLCHAIN" --version)"
  echo "protoc: $PROTOC_VERSION"
  echo "gcc: $(gcc --version | head -1)"
  echo "profile: release, CARGO_PROFILE_RELEASE_DEBUG=0, CARGO_PROFILE_RELEASE_STRIP=symbols"
  echo "highest glibc symbol: $glibc"
  echo "chroma sha256: $(sha256sum "/out/$RID/chroma" | cut -d' ' -f1)"
  echo "chroma-local-guard sha256: $(sha256sum "/out/$RID/chroma-local-guard" | cut -d' ' -f1)"
  echo "version: $("/out/$RID/chroma" --version)"
} | tee "/out/$RID/BUILD-INFO.txt"
