#!/bin/bash
# Usage: build/release-assets.sh. The files of the GitHub release of ChromaDotNet.Local.ChromaDB, in artifacts/release/: the Chroma
# executable of each runtime package, from artifacts/stage/ (build/stage.sh), compressed with gzip as chromadb-<rid>.gz
# (chromadb-win-x64.exe.gz, chromadb-linux-x64.gz...), and SHA256SUMS of the .gz files. ChromaDotNet.Local.ChromaDB has the hash of
# each file uncompressed, and the download at the first start checks it. The guard is in the base package, and is not downloaded.
set -euo pipefail
cd "$(dirname "$0")/.."
out=artifacts/release
rm -rf "$out"; mkdir -p "$out"
for rid in win-x64 linux-x64 linux-arm64 osx-x64 osx-arm64; do
  case "$rid" in win-*) extension=.exe ;; *) extension= ;; esac
  file=artifacts/stage/chromadb/$rid/chromadb$extension
  test -f "$file" || { echo "missing $file: run build/stage.sh" >&2; exit 1; }
  # -n: without the name and the time of the file, so that the same file gives the same bytes.
  gzip -n -9 -c "$file" > "$out/chromadb-$rid$extension.gz"
done
(cd "$out" && sha256sum -- *.gz > SHA256SUMS)
cat "$out/SHA256SUMS"
ls -l "$out"
