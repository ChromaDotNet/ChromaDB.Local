#!/bin/bash
# Usage: build/pack.sh [extra msbuild arguments]. Packs the five runtime packages, ChromaDotNet.Local.Core, then ChromaDotNet.Local, which depends on them,
# into artifacts/packages, and removes ChromaDotNet.Local from the package cache, so that the next restore takes the new packages.
set -euo pipefail
cd "$(dirname "$0")/.."
# The MSBuild nodes keep the files of the packages they have read: a new pack of the same version needs new nodes.
dotnet build-server shutdown >/dev/null 2>&1 || true
rm -f artifacts/packages/ChromaDotNet.Local*.nupkg artifacts/packages/ChromaDotNet.Local*.snupkg
rm -rf artifacts/nuget-cache/chromadotnet.local*
for project in src/runtimes/*/ChromaDotNet.Local.Runtime.*.csproj src/ChromaDotNet.Local.Core/ChromaDotNet.Local.Core.csproj src/ChromaDotNet.Local/ChromaDotNet.Local.csproj; do
  # Core has the hash of every executable, for the download: all of them must be there.
  if ! output=$(dotnet pack "$project" -c Release --nologo -v q -p:RequireExecutableHashes=true "$@" 2>&1); then
    echo "$output" | grep -E "error|warning" >&2
    echo "pack failed: $project" >&2
    exit 1
  fi
  echo "$output" | grep -E "warning|Successfully created" || true
done
rm -rf artifacts/nuget-cache/chromadotnet.local*
dotnet build-server shutdown >/dev/null 2>&1 || true
ls -l artifacts/packages
