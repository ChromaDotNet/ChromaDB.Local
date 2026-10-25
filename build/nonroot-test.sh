#!/bin/bash
# Usage: build/nonroot-test.sh [x64|arm64]. As the Dockerfiles of .NET deploy: dotnet publish as root in the SDK image, then the
# application started as the user app (uid 1654) of the .NET images, with the files as the publish leaves them: mode 744, owned by
# root. ChromaDotNet.Local.ChromaDB published for linux-<arch>, with the executable and the guard next to the application, and
# portable, with them in runtimes/<rid>/native/: the user starts copies in its cache. ChromaDotNet.Local.VecturaDB portable: the
# guard copied the same way, the executable downloaded into the cache of the user. Then without a cache the user can write, where the
# start must fail and say what to do.
set -euo pipefail
arch=${1:-x64}
case "$arch" in x64) platform=linux/amd64 ;; arm64) platform=linux/arm64 ;; *) exit 2 ;; esac
cd "$(dirname "$0")/.."
root=$(pwd -W 2>/dev/null || pwd)
version() { dotnet msbuild "$1" -getProperty:Version -nologo | tr -d '\r'; }
export MSYS_NO_PATHCONV=1
docker rm -f chromadotnet-local-nonroot >/dev/null 2>&1 || true
docker run --rm --name chromadotnet-local-nonroot --platform "$platform" -v "$root:/repo:ro" \
  -e DOTNET_NOLOGO=1 -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e LOCAL_VERSION="$(version src/ChromaDotNet.Local)" \
  -e CHROMADB_VERSION="$(version src/ChromaDotNet.Local.ChromaDB)" -e VECTURADB_VERSION="$(version src/ChromaDotNet.Local.VecturaDB)" \
  mcr.microsoft.com/dotnet/sdk:10.0 bash -c '
set -uo pipefail
rid=linux-$( [ "$(uname -m)" = x86_64 ] && echo x64 || echo arm64 )
echo "== $(id -un) builds ChromaDotNet.Local $LOCAL_VERSION, ChromaDB $CHROMADB_VERSION, VecturaDB $VECTURADB_VERSION, $rid; the application runs as $(id -un app) ($(id -u app))"
mkdir -p /src && cp /repo/tests/ChromaDotNet.Local.TestHost/*.cs /repo/tests/ChromaDotNet.Local.TestHost/*.csproj /src/
cat > /src/nuget.config <<EOF
<configuration><packageSources><clear /><add key="local" value="/repo/artifacts/packages" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources>
<packageSourceMapping><packageSource key="local"><package pattern="ChromaDotNet.Local*" /></packageSource><packageSource key="nuget.org"><package pattern="*" /></packageSource></packageSourceMapping></configuration>
EOF
fails=0
for mode in chromadb-rid chromadb-portable vecturadb-portable; do
  out=/pub-$mode
  packages=${mode%-*}
  if [ $mode = chromadb-rid ]; then extra="-r $rid --self-contained false"; else extra=""; fi
  if ! (cd /src && rm -rf obj bin && dotnet publish -c Release -f net8.0 $extra -o $out -v q -nologo -p:LocalPackages=$packages \
    -p:ChromaDotNetLocalVersion=$LOCAL_VERSION -p:ChromaDotNetLocalChromaDBVersion=$CHROMADB_VERSION \
    -p:ChromaDotNetLocalVecturaDBVersion=$VECTURADB_VERSION -p:ImplicitUsings=enable -p:Nullable=enable -p:LangVersion=latest > /tmp/publish.log 2>&1); then
    grep -E " error " /tmp/publish.log | sort -u; echo "publish failed: $mode"; exit 1
  fi
  echo "== $mode: $(find $out -name chromadb -o -name vecturadb -o -name chromadotnet-local-guard | sort | xargs ls -l | awk "{print \$1, \$3, \$NF}" | tr "\n" ";")"
  output=$(su -s /bin/bash app -c "cd /tmp && HOME=/home/app DOTNET_ROLL_FORWARD=Major dotnet $out/ChromaDotNet.Local.TestHost.dll smoke" 2>&1); code=$?
  echo "$output" | head -3
  if [ $code -eq 0 ]; then echo "ok   $mode: started as app"; else echo "FAIL $mode: exit $code"; fails=$((fails + 1)); fi
done
echo "== the cache of app: $(find /home/app/.cache/chromadotnet-local -type f ! -name .lock | sort | tr "\n" " ")"
# Without a cache it can write, the start fails with what to do.
output=$(su -s /bin/bash app -c "cd /tmp && HOME=/nonexistent XDG_CACHE_HOME= DOTNET_ROLL_FORWARD=Major dotnet /pub-chromadb-rid/ChromaDotNet.Local.TestHost.dll smoke" 2>&1); code=$?
echo "$output" | grep -m1 "Exception" | cut -c1-300
if [ $code -ne 0 ] && echo "$output" | grep -q "cannot execute /pub-chromadb-rid/chromadb and /pub-chromadb-rid/chromadotnet-local-guard" && echo "$output" | grep -q "chmod a+x"; then
  echo "ok   no cache: the message names the engine and the guard, and says chmod a+x"
else
  echo "FAIL no cache: exit $code"; fails=$((fails + 1))
fi
echo "failures: $fails"
exit $fails
'
