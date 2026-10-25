#!/bin/bash
# Usage: build/stage.sh. The executables under the names of the packages, in artifacts/stage/, from where the builds leave them:
#   chromadb/<rid>/chromadb (chromadb.exe on win-x64), the Chroma executable of the runtime packages of ChromaDotNet.Local.ChromaDB, from
#     artifacts/chroma-cli/ (build/fetch-chroma-cli.sh) and artifacts/linux/ (build/build-linux-docker.sh);
#   guard/<rid>/chromadotnet-local-guard, the guard of the base package ChromaDotNet.Local, from artifacts/linux/ and artifacts/guard/
#     (build/build-guard-macos.sh);
#   chromadb/<rid>/BUILD-INFO.txt, how the executable of the runtime package was made, from the BUILD-INFO.txt of its build, with
#     the name of the file in the package and without the guard, which is in the base package.
# Names of their own, so that the old runtime packages, with chroma and chroma-local-guard in the same folders, and the new ones can
# be in one application without two files at the same path of the publish.
set -euo pipefail
cd "$(dirname "$0")/.."
rm -rf artifacts/stage
stage() {
  local from=$1 to=$2
  test -f "$from" || { echo "missing $from" >&2; exit 1; }
  mkdir -p "$(dirname "$to")"
  cp -p "$from" "$to"
  chmod a+x "$to"
}
info() {
  local from=$1 rid=$2 name=$3
  test -f "$from" || { echo "missing $from" >&2; exit 1; }
  { grep -v '^chroma-local-guard sha256:' "$from"; echo "file in this package: $name"; } > "artifacts/stage/chromadb/$rid/BUILD-INFO.txt"
}
stage artifacts/chroma-cli/win-x64/chroma.exe artifacts/stage/chromadb/win-x64/chromadb.exe
info artifacts/chroma-cli/win-x64/BUILD-INFO.txt win-x64 chromadb.exe
for rid in osx-arm64 osx-x64; do
  stage "artifacts/chroma-cli/$rid/chroma" "artifacts/stage/chromadb/$rid/chromadb"
  info "artifacts/chroma-cli/$rid/BUILD-INFO.txt" "$rid" chromadb
  stage "artifacts/guard/$rid/chroma-local-guard" "artifacts/stage/guard/$rid/chromadotnet-local-guard"
done
for rid in linux-x64 linux-arm64; do
  stage "artifacts/linux/$rid/chroma" "artifacts/stage/chromadb/$rid/chromadb"
  info "artifacts/linux/$rid/BUILD-INFO.txt" "$rid" chromadb
  stage "artifacts/linux/$rid/chroma-local-guard" "artifacts/stage/guard/$rid/chromadotnet-local-guard"
done
find artifacts/stage -type f | sort
