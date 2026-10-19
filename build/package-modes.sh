#!/bin/bash
# Usage: build/package-modes.sh. The two ways to reference the package, with the test host:
#   core-rid: ChromaDotNet.Local.Core and the runtime package of this machine only; Chroma starts, and the output has only the
#             executables of this platform;
#   core:     ChromaDotNet.Local.Core alone, with the download turned off; the start fails with the message that names the packages.
#             build/download-tests.sh then uses this build for the download.
# The way with everything, ChromaDotNet.Local, is the one of the tests and of the suite of the client.
set -uo pipefail
cd "$(dirname "$0")/.."
fails=0
check() { if [ "$2" = "$3" ]; then echo "ok   $1"; else echo "FAIL $1: expected $3, got $2"; fails=$((fails + 1)); fi; }
version=$(dotnet msbuild src/ChromaDotNet.Local.Core -getProperty:Version -nologo | tr -d '\r')
root=$(pwd)
for mode in core-rid core; do
  # A copy of the test host in a directory of its own, under artifacts/, which builds without the Directory.Build files of the
  # repository: the version and the package cache come as properties.
  out=artifacts/modes/$mode
  rm -rf "$out"; mkdir -p "$out"
  cp tests/ChromaDotNet.Local.TestHost/*.cs tests/ChromaDotNet.Local.TestHost/*.csproj "$out/"
  for f in Directory.Build.props Directory.Build.targets; do [ -f "artifacts/$f" ] || echo '<Project />' > "artifacts/$f"; done
  (cd "$out" && dotnet build -c Release -f net8.0 -v q -nologo -o bin -p:LocalPackages=$mode -p:ChromaDotNetLocalVersion=$version \
    -p:ImplicitUsings=enable -p:Nullable=enable -p:LangVersion=latest "-p:RestorePackagesPath=$root/artifacts/nuget-cache" 2>&1 \
    | grep -E " error |warning" || true)
  if [ "$mode" = core ]; then export CHROMA_LOCAL_NO_DOWNLOAD=1; else unset CHROMA_LOCAL_NO_DOWNLOAD; fi
  run=$(dotnet "$out/bin/ChromaDotNet.Local.TestHost.dll" smoke 2>&1); code=$?
  unset CHROMA_LOCAL_NO_DOWNLOAD
  natives=$(cd "$out/bin" && find . -name 'chroma' -o -name 'chroma.exe' | sort | tr '\n' ' ')
  echo "== $mode: exit $code; executables: ${natives:-none}"
  echo "$run" | head -3
  if [ "$mode" = core-rid ]; then
    check "core-rid: smoke" "$code" 0
    check "core-rid: one executable" "$(echo "$natives" | wc -w | tr -d ' ')" 1
  else
    check "core: start fails" "$([ "$code" -ne 0 ] && echo failed || echo started)" failed
    check "core: message names the packages" "$(echo "$run" | grep -c 'Reference ChromaDotNet.Local, which brings')" 1
    check "core: no executable" "${natives:-none}" none
  fi
done
echo "failures: $fails"
exit $fails
