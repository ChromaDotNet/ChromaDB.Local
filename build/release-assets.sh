#!/bin/bash
# Usage: build/release-assets.sh. The files of the GitHub release, in artifacts/release/: each executable of the runtime packages
# compressed with gzip, as <name>-<rid>.gz (chroma-win-x64.exe.gz, chroma-linux-x64.gz, chroma-local-guard-linux-x64.gz...), and
# SHA256SUMS of the .gz files. ChromaDotNet.Local.Core has the hash of each file uncompressed, and checks it after the download.
set -euo pipefail
cd "$(dirname "$0")/.."
out=artifacts/release
rm -rf "$out"; mkdir -p "$out"
add() {
  local file=$1 rid=$2
  local name; name=$(basename "$file")
  local base=${name%.exe}; local extension=${name#"$base"}
  test -f "$file" || { echo "missing $file" >&2; exit 1; }
  # -n: without the name and the time of the file, so that the same file gives the same bytes.
  gzip -n -9 -c "$file" > "$out/$base-$rid$extension.gz"
}
add artifacts/chroma-cli/win-x64/chroma.exe win-x64
for rid in osx-arm64 osx-x64; do
  add "artifacts/chroma-cli/$rid/chroma" "$rid"
  add "artifacts/guard/$rid/chroma-local-guard" "$rid"
done
for rid in linux-x64 linux-arm64; do
  add "artifacts/linux/$rid/chroma" "$rid"
  add "artifacts/linux/$rid/chroma-local-guard" "$rid"
done
(cd "$out" && sha256sum -- *.gz > SHA256SUMS)
cat "$out/SHA256SUMS"
ls -l "$out"
