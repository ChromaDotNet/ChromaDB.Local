#!/bin/bash
# Usage: build/guard-hosts-test.sh [x64|arm64]. The guard where the files of the packages are not in runtimes/<rid>/native/ of
# AppContext.BaseDirectory: in a container of the .NET SDK, the hosts of tests/GuardHosts with ChromaDotNet.Local.ChromaDB and the
# download turned off, so the executable and the guard come from the files of the packages:
#   an application, published as usual;
#   a single file with its native libraries inside, which extracts them at the start;
#   a plugin, loaded by a host in another directory in a load context of its own;
#   a binary module of PowerShell.
# Each starts the server and is killed with SIGKILL: the guard must be the parent of the server, and the server must end with it.
set -euo pipefail
arch=${1:-x64}
case "$arch" in x64) platform=linux/amd64 ;; arm64) platform=linux/arm64 ;; *) exit 2 ;; esac
cd "$(dirname "$0")/.."
root=$(pwd -W 2>/dev/null || pwd)
version() { dotnet msbuild "$1" -getProperty:Version -nologo | tr -d '\r'; }
export MSYS_NO_PATHCONV=1
docker rm -f chromadotnet-local-guard-hosts >/dev/null 2>&1 || true
docker run --rm --name chromadotnet-local-guard-hosts --platform "$platform" -v "$root:/repo:ro" \
  -e DOTNET_NOLOGO=1 -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e CHROMADB_VERSION="$(version src/ChromaDotNet.Local.ChromaDB)" \
  mcr.microsoft.com/dotnet/sdk:10.0 bash -c '
set -uo pipefail
rid=linux-$( [ "$(uname -m)" = x86_64 ] && echo x64 || echo arm64 )
mkdir -p /work && cp -r /repo/tests/GuardHosts/. /work/
cat > /work/nuget.config <<EOF
<configuration><packageSources><clear /><add key="local" value="/repo/artifacts/packages" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources>
<packageSourceMapping><packageSource key="local"><package pattern="ChromaDotNet.Local*" /></packageSource><packageSource key="nuget.org"><package pattern="*" /></packageSource></packageSourceMapping></configuration>
EOF
cd /work
fails=0
publish() {
  local project=$1 out=$2; shift 2
  if ! dotnet publish "$project" -c Release -o "$out" -v q -nologo -p:ChromaDotNetLocalChromaDBVersion=$CHROMADB_VERSION "$@" > /tmp/publish.log 2>&1; then
    grep -E " error " /tmp/publish.log | sort -u; echo "publish failed: $project"; exit 1
  fi
}
# Starts the command, which writes "ready <pid>", kills it with SIGKILL, and looks at the server 3 seconds later.
probe() {
  local name=$1; shift
  "$@" > /tmp/probe.out 2>&1 & local app=$!
  for _ in $(seq 1 600); do grep -q "^ready " /tmp/probe.out && break; kill -0 $app 2>/dev/null || break; sleep 0.1; done
  local pid; pid=$(awk "/^ready / {print \$2}" /tmp/probe.out)
  if [ -z "$pid" ]; then echo "FAIL $name: did not start: $(tail -3 /tmp/probe.out | tr "\n" " " | cut -c1-300)"; fails=$((fails + 1)); kill -9 $app 2>/dev/null; return; fi
  local parent; parent=$(cat /proc/$(awk "{print \$4}" /proc/$pid/stat)/comm)
  kill -9 $app; wait $app 2>/dev/null; sleep 3
  local alive=no; if kill -0 "$pid" 2>/dev/null; then alive=yes; kill -9 "$pid"; fi
  # /proc/<pid>/comm keeps the first 15 characters of the name.
  if [ "$parent" = chromadotnet-lo ] && [ $alive = no ]; then echo "ok   $name: the guard is the parent of the server, which ends with the application"
  else echo "FAIL $name: parent of the server $parent, server alive after the kill: $alive"; fails=$((fails + 1)); fi
}
publish App /out/app
probe "application" dotnet /out/app/GuardHostsApp.dll /data/app
publish App /out/single -r $rid --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
echo "== the single file: $(ls /out/single | tr "\n" " ")"
probe "single file with its native libraries inside" /out/single/GuardHostsApp /data/single
publish Plugin /out/plugin
publish Host /out/host
probe "plugin of a host in another directory" dotnet /out/host/GuardHostsHost.dll /out/plugin/GuardHostsPlugin.dll /data/plugin
publish Module /out/module
probe "binary module of PowerShell" pwsh -NoProfile -NonInteractive -Command "Import-Module /out/module/GuardHostsModule.dll; \$p = Start-GuardHostsServer -DataPath /data/module; Write-Output \"ready \$p\"; Start-Sleep -Seconds 600"
echo "failures: $fails"
exit $fails
'
