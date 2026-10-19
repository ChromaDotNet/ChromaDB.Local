#!/bin/bash
# Builds the Chroma CLI from its tag, with no change to the code, on manylinux_2_28 (AlmaLinux 8, glibc 2.28), in release
# without debug info, and the guard next to it. Runs inside quay.io/pypa/manylinux_2_28_x86_64 or quay.io/pypa/manylinux_2_28_aarch64:
# see build-linux.ps1, which starts the container. Output in /out/<rid>/: chroma, chroma-local-guard, BUILD-INFO.txt.
set -euo pipefail

CLI_TAG=${CLI_TAG:-cli-1.4.4}
CLI_COMMIT=${CLI_COMMIT:-11f3c743}
RUST_TOOLCHAIN=${RUST_TOOLCHAIN:-1.92.0}
PROTOC_VERSION=${PROTOC_VERSION:-31.1}

case "$(uname -m)" in
  x86_64) RID=linux-x64; PROTOC_ARCH=x86_64 ;;
  aarch64) RID=linux-arm64; PROTOC_ARCH=aarch_64 ;;
  *) echo "unsupported machine $(uname -m)" >&2; exit 1 ;;
esac

dnf install -y -q git unzip file binutils >/dev/null 2>&1 || true
echo "rid: $RID, $(ldd --version | head -1)"

export RUSTUP_HOME=/opt/rustup CARGO_HOME=/opt/cargo PATH=/opt/cargo/bin:$PATH
if [ ! -x /opt/cargo/bin/rustup ]; then
  curl -sSf https://sh.rustup.rs | sh -s -- -y --profile minimal --default-toolchain none >/dev/null 2>&1
fi
rustup toolchain install "$RUST_TOOLCHAIN" --profile minimal >/dev/null 2>&1
if [ ! -x /opt/protoc/bin/protoc ]; then
  curl -sSfL -o /tmp/protoc.zip "https://github.com/protocolbuffers/protobuf/releases/download/v$PROTOC_VERSION/protoc-$PROTOC_VERSION-linux-$PROTOC_ARCH.zip"
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
