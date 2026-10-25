#!/bin/bash
# Usage: build/pack.sh [extra msbuild arguments]. Puts the executables under their names (build/stage.sh), then packs into
# artifacts/packages, in the order of the dependencies: the base ChromaDotNet.Local, the five runtime packages of ChromaDotNet.Local.ChromaDB,
# ChromaDotNet.Local.ChromaDB, which depends on them and on the base, and ChromaDotNet.Local.VecturaDB, which depends on the base and
# needs the notices of build/vecturadb-notices.sh. Then it removes the packages of this repository from the package cache, so that the
# next restore takes the new packages.
set -euo pipefail
cd "$(dirname "$0")/.."
bash build/stage.sh > /dev/null
# The MSBuild nodes keep the files of the packages they have read: a new pack of the same version needs new nodes.
dotnet build-server shutdown >/dev/null 2>&1 || true
rm -f artifacts/packages/ChromaDotNet.Local*.nupkg artifacts/packages/ChromaDotNet.Local*.snupkg
rm -rf artifacts/nuget-cache/chromadotnet.local*
for project in src/ChromaDotNet.Local/ChromaDotNet.Local.csproj src/runtimes/*/ChromaDotNet.Local.ChromaDB.Runtime.*.csproj \
  src/ChromaDotNet.Local.ChromaDB/ChromaDotNet.Local.ChromaDB.csproj src/ChromaDotNet.Local.VecturaDB/ChromaDotNet.Local.VecturaDB.csproj; do
  # The base and ChromaDotNet.Local.ChromaDB have the hash of every executable they describe: all of them must be there.
  if ! output=$(dotnet pack "$project" -c Release --nologo -v q -p:RequireExecutableHashes=true -p:RequireNotices=true "$@" 2>&1); then
    echo "$output" | grep -E "error|warning" >&2
    echo "pack failed: $project" >&2
    exit 1
  fi
  echo "$output" | grep -E "warning|Successfully created" || true
done
rm -rf artifacts/nuget-cache/chromadotnet.local*
dotnet build-server shutdown >/dev/null 2>&1 || true
ls -l artifacts/packages
