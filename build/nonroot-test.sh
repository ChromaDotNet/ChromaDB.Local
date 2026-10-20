#!/bin/bash
# Usage: build/nonroot-test.sh [x64|arm64]. As the Dockerfiles of .NET deploy: dotnet publish as root in the SDK image, then the
# application started as the user app (uid 1654) of the .NET images, with the files as the publish leaves them: mode 744, owned by
# root. Published for linux-<arch>, with the executables next to the application, and portable, with them in runtimes/<rid>/native/;
# then without a cache the user can write, where the start must fail and say what to do.
set -euo pipefail
arch=${1:-x64}
case "$arch" in x64) platform=linux/amd64 ;; arm64) platform=linux/arm64 ;; *) exit 2 ;; esac
root=$(cd "$(dirname "$0")/.." && (pwd -W 2>/dev/null || pwd))
export MSYS_NO_PATHCONV=1
docker rm -f chromadotnet-local-nonroot >/dev/null 2>&1 || true
docker run --rm --name chromadotnet-local-nonroot --platform "$platform" -v "$root:/repo:ro" \
  -e DOTNET_NOLOGO=1 -e DOTNET_CLI_TELEMETRY_OPTOUT=1 mcr.microsoft.com/dotnet/sdk:10.0 bash -c '
set -uo pipefail
version=$(sed -n "s:.*<VersionPrefix>\(.*\)</VersionPrefix>.*:\1:p" /repo/Directory.Build.props | sed "s/\$(ChromaVersion)/$(sed -n "s:.*<ChromaVersion>\(.*\)</ChromaVersion>.*:\1:p" /repo/Directory.Build.props)/; s/\$(PackageRevision)/$(sed -n "s:.*<PackageRevision>\(.*\)</PackageRevision>.*:\1:p" /repo/Directory.Build.props)/")-$(sed -n "s:.*<VersionSuffix>\(.*\)</VersionSuffix>.*:\1:p" /repo/Directory.Build.props)
rid=linux-$( [ "$(uname -m)" = x86_64 ] && echo x64 || echo arm64 )
echo "== $(id -un) builds ChromaDotNet.Local $version, $rid; the application runs as $(id -un app) ($(id -u app))"
mkdir -p /src && cp /repo/tests/ChromaDotNet.Local.TestHost/*.cs /repo/tests/ChromaDotNet.Local.TestHost/*.csproj /src/
cat > /src/nuget.config <<EOF
<configuration><packageSources><clear /><add key="local" value="/repo/artifacts/packages" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources>
<packageSourceMapping><packageSource key="local"><package pattern="ChromaDotNet.Local*" /></packageSource><packageSource key="nuget.org"><package pattern="*" /></packageSource></packageSourceMapping></configuration>
EOF
fails=0
for mode in rid portable; do
  out=/pub-$mode
  if [ $mode = rid ]; then extra="-r $rid --self-contained false"; else extra=""; fi
  (cd /src && dotnet publish -c Release -f net8.0 $extra -o $out -v q -nologo -p:ChromaDotNetLocalVersion=$version \
    -p:ImplicitUsings=enable -p:Nullable=enable -p:LangVersion=latest 2>&1 | grep -E " error " ) && { echo "publish failed"; exit 1; }
  echo "== $mode: $(find $out -name chroma -o -name chroma-local-guard | xargs ls -l | awk "{print \$1, \$3, \$NF}" | tr "\n" ";")"
  output=$(su -s /bin/bash app -c "cd /tmp && HOME=/home/app DOTNET_ROLL_FORWARD=Major dotnet $out/ChromaDotNet.Local.TestHost.dll smoke" 2>&1); code=$?
  echo "$output" | head -3
  if [ $code -eq 0 ]; then echo "ok   $mode: started as app"; else echo "FAIL $mode: exit $code"; fails=$((fails + 1)); fi
done
# Without a cache it can write, the start fails with what to do.
output=$(su -s /bin/bash app -c "cd /tmp && HOME=/nonexistent XDG_CACHE_HOME= DOTNET_ROLL_FORWARD=Major dotnet /pub-rid/ChromaDotNet.Local.TestHost.dll smoke" 2>&1); code=$?
echo "$output" | grep -m1 "Exception" | cut -c1-300
if [ $code -ne 0 ] && echo "$output" | grep -q "chmod a+x"; then echo "ok   no cache: the message says chmod a+x"; else echo "FAIL no cache: exit $code"; fails=$((fails + 1)); fi
echo "failures: $fails"
exit $fails
'
