#!/bin/bash
# Usage: build/client-suite.sh [dotnet test arguments]. Runs the tests of ChromaDotNet.Client (a clone in artifacts/client) against a
# Chroma started by ChromaDotNet.Local: the test host, an application that uses the package, starts it, and stops it at the end.
# Bash 3.2 too, as on macOS: the host writes to a file, and stops when the stop file appears.
set -uo pipefail
cd "$(dirname "$0")/.."
CLIENT_TAG=${CLIENT_TAG:-v2.14.0}
if [ ! -d artifacts/client ]; then
  git clone -q --depth 1 --branch "$CLIENT_TAG" https://github.com/ChromaDotNet/ChromaDB.Client artifacts/client
fi
# The clone builds with its own rules, not with the Directory.Build files of this repository.
for f in Directory.Build.props Directory.Build.targets; do
  [ -f "artifacts/$f" ] || echo '<Project />' > "artifacts/$f"
done
host=tests/ChromaDotNet.Local.TestHost/bin/Release/net8.0/ChromaDotNet.Local.TestHost.dll
[ -f "$host" ] || dotnet build tests/ChromaDotNet.Local.TestHost -c Release -f net8.0 -v q -nologo
data=$(mktemp -d)
# Paths as the host sees them: on Windows, cygpath turns /tmp/... into C:\...
native() { if command -v cygpath >/dev/null; then cygpath -w "$1"; else echo "$1"; fi; }
dotnet "$host" serve "$(native "$data/chroma")" "$(native "$data/stop")" > "$data/host.out" 2>&1 &
host_pid=$!
for _ in $(seq 1 600); do grep -q "^ready " "$data/host.out" 2>/dev/null && break; kill -0 "$host_pid" 2>/dev/null || break; sleep 0.1; done
read -r ready uri pid ms < "$data/host.out"
[ "${ready:-}" = ready ] || { echo "host did not start:" >&2; cat "$data/host.out" >&2; exit 1; }
echo "chroma at $uri, pid $pid, started in $ms ms"
rss() {
  if [ -r "/proc/$pid/status" ]; then grep VmRSS "/proc/$pid/status" | awk '{print $2 " kB"}'
  elif command -v powershell >/dev/null; then powershell -NoProfile -Command "(Get-Process -Id $pid).WorkingSet64 / 1KB" 2>/dev/null | tr -d '\r' | sed 's/$/ kB (working set)/'
  else ps -o rss= -p "$pid" | awk '{print $1 " kB"}'; fi
}
echo "memory after the start: $(rss)"
start=$(date +%s)
CHROMA_TEST_URI="${uri%/}" CHROMA_IMAGE=chromadb/chroma:1.5.9 dotnet test artifacts/client/ChromaDB.Client.Tests -c Release "$@" > "$data/suite.log" 2>&1
suite=$?
grep -E " error |^\s+Failed |Passed!|Failed!" "$data/suite.log"
echo "suite seconds: $(( $(date +%s) - start )), exit code $suite"
echo "memory after the suite: $(rss)"
touch "$data/stop"
wait "$host_pid"
grep "^stopped" "$data/host.out"
[ -n "${SUITE_LOG:-}" ] && cp "$data/suite.log" "$SUITE_LOG"
rm -rf "$data"
exit $suite
