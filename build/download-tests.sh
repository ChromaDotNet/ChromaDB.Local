#!/bin/bash
# Usage: build/download-tests.sh. The download at the first start, with the test host built by build/package-modes.sh with
# ChromaDotNet.Local.Core alone, and a mirror of the release (build/serve_release.py on artifacts/release, from
# build/release-assets.sh):
#   1. no mirror, empty cache: the start fails with the message that names the packages and the mirror;
#   2. mirror, empty cache: the files are downloaded, and Chroma starts;
#   3. no mirror, the cache of 2: Chroma starts, without the network;
#   4. two processes at the same time, empty cache: both start, and each file is downloaded once;
#   5. a mirror that serves files with another hash: the start fails, and no executable stays in the cache;
#   6. an executable of the cache changed: the start downloads it again;
#   7. the download turned off: the start fails, as with Core alone.
# With CHROMA_LOCAL_RELEASE=1, also 8: the GitHub release itself, without DownloadUri.
set -uo pipefail
cd "$(dirname "$0")/.."
host=artifacts/modes/core/bin/ChromaDotNet.Local.TestHost.dll
[ -f "$host" ] || { echo "run build/package-modes.sh first" >&2; exit 1; }
[ -f artifacts/release/SHA256SUMS ] || { echo "run build/release-assets.sh first" >&2; exit 1; }
# The first python that runs: on Windows python3 can be the stub of the Store.
py=$(for c in python3 python; do "$c" -c "" >/dev/null 2>&1 && { echo "$c"; break; }; done)
native() { if command -v cygpath >/dev/null; then cygpath -w "$1"; else echo "$1"; fi; }
fails=0
check() { if [ "$2" = "$3" ]; then echo "ok   $1"; else echo "FAIL $1: expected $3, got $2"; fails=$((fails + 1)); fi; }
work=$(mktemp -d)
rid=$(dotnet --info | awk '/ RID:/ {print $2; exit}')
case "$rid" in win-*) files=1; exe=chroma.exe ;; *) files=2; exe=chroma ;; esac
port=$((20000 + RANDOM % 20000))
mirror_pid=
start_mirror() {
  : > "$work/requests.log"
  "$py" build/serve_release.py artifacts/release $port "$work/requests.log" "$@" &
  mirror_pid=$!
  for _ in $(seq 1 100); do curl -s -o /dev/null "http://127.0.0.1:$port/SHA256SUMS" && break; sleep 0.1; done
  : > "$work/requests.log"
}
stop_mirror() { [ -n "$mirror_pid" ] && kill "$mirror_pid" 2>/dev/null; wait "$mirror_pid" 2>/dev/null; mirror_pid=; }
trap stop_mirror EXIT
run() { CHROMA_LOCAL_CACHE="$(native "$1")" dotnet "$host" smoke 2>&1; }
requests() { grep -c . "$work/requests.log" | tr -d ' '; }
cached() { find "$1" -name "$exe" -type f | wc -l | tr -d ' '; }
export CHROMA_LOCAL_DOWNLOAD_URI="http://127.0.0.1:$port/"

# 1. No mirror, empty cache.
out=$(run "$work/cache1"); code=$?
check "1 no network: start fails" "$([ $code -ne 0 ] && echo failed || echo started)" failed
check "1 no network: the message names the runtime package" "$(echo "$out" | grep -c "Could not download the Chroma executable for $rid")" 1
echo "$out" | grep -m1 "Could not download" | cut -c1-240

# 2. Mirror, empty cache.
start_mirror
t0=$(date +%s)
out=$(run "$work/cache2"); code=$?
check "2 download: start" "$code" 0
check "2 download: files downloaded" "$(requests)" $files
check "2 download: executable in the cache" "$(cached "$work/cache2")" 1
echo "$out" | head -1; echo "     download and start: $(( $(date +%s) - t0 )) s"
stop_mirror

# 3. No mirror, the cache of 2.
out=$(run "$work/cache2"); code=$?
check "3 offline from the cache: start" "$code" 0
echo "$out" | head -1

# 4. Two processes at the same time, empty cache.
start_mirror
run "$work/cache4" > "$work/a.out" 2>&1 & a=$!
run "$work/cache4" > "$work/b.out" 2>&1 & b=$!
wait $a; ca=$?; wait $b; cb=$?
check "4 two processes: first starts" "$ca" 0
check "4 two processes: second starts" "$cb" 0
check "4 two processes: each file downloaded once" "$(requests)" $files
stop_mirror

# 5. Files with another hash.
start_mirror --corrupt
out=$(run "$work/cache5"); code=$?
check "5 wrong hash: start fails" "$([ $code -ne 0 ] && echo failed || echo started)" failed
check "5 wrong hash: the message says SHA-256" "$(echo "$out" | grep -c "SHA-256 hash")" 1
check "5 wrong hash: nothing kept in the cache" "$(cached "$work/cache5")" 0
echo "$out" | grep -m1 "SHA-256 hash" | cut -c1-240
stop_mirror

# 6. An executable of the cache changed.
cp -r "$work/cache2" "$work/cache6"
file=$(find "$work/cache6" -name "$exe" -type f)
printf 'changed' | dd of="$file" bs=1 seek=1000 conv=notrunc 2>/dev/null
start_mirror
out=$(run "$work/cache6"); code=$?
check "6 changed in the cache: start" "$code" 0
check "6 changed in the cache: downloaded again" "$(grep -c "^$(basename "$file" .exe)" "$work/requests.log" | tr -d ' ')" 1
stop_mirror

# 7. The download turned off.
out=$(CHROMA_LOCAL_NO_DOWNLOAD=1 run "$work/cache7"); code=$?
check "7 download off: start fails" "$([ $code -ne 0 ] && echo failed || echo started)" failed
check "7 download off: no cache" "$([ -d "$work/cache7" ] && echo created || echo none)" none

# 8. The GitHub release.
if [ "${CHROMA_LOCAL_RELEASE:-}" = 1 ]; then
  out=$(CHROMA_LOCAL_DOWNLOAD_URI= run "$work/cache8"); code=$?
  check "8 GitHub release: start" "$code" 0
  echo "$out" | head -1
fi

rm -rf "$work"
echo "failures: $fails"
exit $fails
