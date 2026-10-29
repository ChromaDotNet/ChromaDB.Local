#!/bin/bash
# Usage: [MODE=bench|trace] [CHROMA_LOCAL_ENGINE=ChromaDB|VecturaDB] build/linux-tests.sh <image> <short name> [x64|arm64]. In a
# container of the image: the tests of ChromaDotNet.Local and the suite of the client against the engine of CHROMA_LOCAL_ENGINE,
# ChromaDB by default, with the packages of artifacts/packages and the clone of the client that build/client-suite.sh makes in
# artifacts, as the container has no git; bench and trace with that engine.
# The .NET SDK, which dotnet-install.sh installs once for the images of an architecture, and the packages of NuGet stay in two
# volumes for each architecture, not in folders of Windows: the container reads those through the file sharing of Docker, and the
# build and the start of the server were slower. The copy of the repository where the tests build goes into memory.
set -euo pipefail
image=$1; short=$2; arch=${3:-x64}
case "$arch" in x64) platform=linux/amd64 ;; arm64) platform=linux/arm64 ;; *) exit 2 ;; esac
root=$(cd "$(dirname "$0")/.." && (pwd -W 2>/dev/null || pwd))
name=chromadotnet-local-test-$short
export MSYS_NO_PATHCONV=1
docker rm -f "$name" >/dev/null 2>&1 || true
docker run --rm --name "$name" --platform "$platform" \
  -v "$root:/repo:ro" -v "chromadotnet-local-dotnet-$arch:/dotnet" -v "chromadotnet-local-nuget-$arch:/nuget" --tmpfs /work:exec,size=4g \
  -e MODE="${MODE:-}" -e SYNC_FIRST="${SYNC_FIRST:-}" -e CHROMA_LOCAL_ENGINE="${CHROMA_LOCAL_ENGINE:-ChromaDB}" -e CLIENT_TAG -e DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 \
  "$image" bash -c '
set -euo pipefail
if command -v apt-get >/dev/null; then
  export DEBIAN_FRONTEND=noninteractive
  apt-get update -qq >/dev/null && apt-get install -y -qq curl ca-certificates procps >/dev/null 2>&1
elif command -v dnf >/dev/null; then
  dnf install -y -q --allowerasing curl ca-certificates procps-ng tar gzip findutils >/dev/null 2>&1 || true
fi
. /etc/os-release
echo "== $PRETTY_NAME, $(ldd --version | head -1), $(uname -m)"
export PATH=/dotnet:$PATH DOTNET_ROOT=/dotnet
if [ ! -x /dotnet/dotnet ] || ! /dotnet/dotnet --list-sdks | grep -q "^10\."; then
  curl -sSfL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /dotnet >/dev/null
  bash /tmp/dotnet-install.sh --channel 8.0 --runtime dotnet --install-dir /dotnet >/dev/null
fi
# A copy of the repository without the outputs of Windows: of artifacts, only the packages and the clones of the client.
cd /repo
tar --exclude="./artifacts" --exclude="*/bin" --exclude="*/obj" --exclude="./.git" -cf - . | tar -xf - -C /work
tar --exclude="*/bin" --exclude="*/obj" -cf - artifacts/packages artifacts/client-* | tar -xf - -C /work
cd /work
# Each run restores the packages of artifacts/packages again.
export NUGET_PACKAGES=/nuget
rm -rf /nuget/chromadotnet.local*
sed -i "s#<RestorePackagesPath>.*</RestorePackagesPath>##" Directory.Build.props
# NETSDK1188: the locales of the test platform, with invariant globalization.
dotnet build tests/ChromaDotNet.Local.Tests -c Release -v q -nologo 2>&1 | grep -E "error|warn" | grep -v NETSDK1188 || true
if [ "${MODE:-}" = bench ]; then
  dotnet build tests/ChromaDotNet.Local.TestHost -c Release -f net8.0 -v q -nologo 2>&1 | grep -E "error" || true
  dotnet tests/ChromaDotNet.Local.TestHost/bin/Release/net8.0/ChromaDotNet.Local.TestHost.dll bench /tmp/bench
  exit 0
fi
if [ "${MODE:-}" = trace ]; then
  dotnet build tests/ChromaDotNet.Local.TestHost -c Release -f net8.0 -v q -nologo 2>&1 | grep -E "error" || true
  if [ "${SYNC_FIRST:-}" = 1 ]; then t0=$(date +%s%N); sync; echo "sync: $(( ($(date +%s%N) - t0) / 1000000 )) ms"; fi
  for i in 1 2; do
    echo "== smoke $i"
    CHROMA_LOCAL_TRACE=1 dotnet tests/ChromaDotNet.Local.TestHost/bin/Release/net8.0/ChromaDotNet.Local.TestHost.dll smoke 2>&1 | grep -v "Debug:" 
  done
  exit 0
fi
echo "== tests of ChromaDotNet.Local"
dotnet test tests/ChromaDotNet.Local.Tests -c Release --no-build --logger "console;verbosity=normal" 2>&1 | grep -E "Total tests|^\s+Passed: |^\s+Failed: |^\s+Failed |the server gone" || true
echo "== suite of the client"
bash build/client-suite.sh
'
