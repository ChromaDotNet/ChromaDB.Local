#!/bin/bash
# Downloads the official binaries of the Chroma CLI for Windows and macOS from the GitHub release, and checks each against the
# SHA-256 digest GitHub publishes for the asset. Output in artifacts/official/<rid>/.
set -euo pipefail
CLI_TAG=${CLI_TAG:-cli-1.4.4}
root=$(cd "$(dirname "$0")/.." && pwd)
api="https://api.github.com/repos/chroma-core/chroma/releases/tags/$CLI_TAG"
# python first: on Windows python3 can be the stub of the Store.
py=$(command -v python || command -v python3)
# In GitHub Actions the token of the run avoids the limit of the requests without authentication.
auth=()
[ -n "${GH_TOKEN:-}" ] && auth=(-H "Authorization: Bearer $GH_TOKEN")
release=$(curl -sSfL -H 'Accept: application/vnd.github+json' "${auth[@]}" "$api")

fetch() {
  local asset=$1 rid=$2 file=$3
  local out="$root/artifacts/official/$rid"
  mkdir -p "$out"
  local digest url
  digest=$(printf '%s' "$release" | "$py" -c "import sys,json; print(next(a['digest'] for a in json.load(sys.stdin)['assets'] if a['name']=='$asset'))")
  url=$(printf '%s' "$release" | "$py" -c "import sys,json; print(next(a['browser_download_url'] for a in json.load(sys.stdin)['assets'] if a['name']=='$asset'))")
  curl -sSfL -o "$out/$file.download" "$url"
  local actual="sha256:$(sha256sum "$out/$file.download" | cut -d' ' -f1)"
  if [ "$actual" != "$digest" ]; then
    echo "$asset: digest $digest, file $actual" >&2
    rm -f "$out/$file.download"
    exit 1
  fi
  mv "$out/$file.download" "$out/$file"
  printf 'tag: %s\nasset: %s\nurl: %s\ndigest: %s\n' "$CLI_TAG" "$asset" "$url" "$digest" > "$out/BUILD-INFO.txt"
  echo "$rid: $asset, $digest"
}

fetch chroma-windows.exe win-x64 chroma.exe
fetch chroma-macos-arm64 osx-arm64 chroma
fetch chroma-macos-intel osx-x64 chroma
