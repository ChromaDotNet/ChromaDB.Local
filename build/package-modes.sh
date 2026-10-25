#!/bin/bash
# Usage: build/package-modes.sh [aot]. The ways to reference the packages, with the test host:
#   chromadb:          ChromaDotNet.Local.ChromaDB; Chroma starts, the output has the executables of the five platforms and the guards
#                      of Linux and macOS; published for this platform, only its executable and its guard;
#   chromadb-download: the same with ChromaDotNetLocalChromaDBDownload, and the download turned off: no executable in the output nor
#                      in the publish, and the start fails with the message that names the property;
#   vecturadb:         ChromaDotNet.Local.VecturaDB, and the download turned off: no executable, and the start fails with the message
#                      that says the download;
#   base:              ChromaDotNet.Local alone: the warning CDNL001 at the build, and the start fails with the message that names the
#                      two engines.
# build/download-tests.sh then uses the builds of chromadb-download and vecturadb for the download. With aot, also each engine
# published with NativeAOT for this platform, which starts, without warnings of trimming or AOT from the packages; VecturaDB downloads
# its executable into the cache of the user, if it is not there.
# The application with the two engines is the one of the tests and of the suite of the client.
# Bash 3.2 too, as on macOS: ${1+"$@"} for arguments that can be none.
set -uo pipefail
cd "$(dirname "$0")/.."
fails=0
check() { if [ "$2" = "$3" ]; then echo "ok   $1"; else echo "FAIL $1: expected $3, got $2"; fails=$((fails + 1)); fi; }
version() { dotnet msbuild "$1" -getProperty:Version -nologo | tr -d '\r'; }
versions="-p:ChromaDotNetLocalVersion=$(version src/ChromaDotNet.Local) -p:ChromaDotNetLocalChromaDBVersion=$(version src/ChromaDotNet.Local.ChromaDB) -p:ChromaDotNetLocalVecturaDBVersion=$(version src/ChromaDotNet.Local.VecturaDB)"
rid=$(dotnet --info | awk '/ RID:/ {print $2; exit}')
case "$rid" in win-*) exe=.exe; guards=0 ;; *) exe=; guards=1 ;; esac
root=$(pwd)
for f in Directory.Build.props Directory.Build.targets; do [ -f "artifacts/$f" ] || echo '<Project />' > "artifacts/$f"; done
# A copy of the test host in a directory of its own, under artifacts/modes/, which builds without the Directory.Build files of the
# repository: the versions and the package cache come as properties. A build, or with more arguments a publish, into out/.
make() {
  local name=$1 packages=$2 out=artifacts/modes/$1; shift 2
  rm -rf "$out"; mkdir -p "$out"
  cp tests/ChromaDotNet.Local.TestHost/*.cs tests/ChromaDotNet.Local.TestHost/*.csproj "$out/"
  local verb=build; [ $# -gt 0 ] && verb=publish
  (cd "$out" && dotnet $verb -c Release -f net8.0 -v q -nologo -o out -p:LocalPackages=$packages $versions -p:ImplicitUsings=enable \
    -p:Nullable=enable -p:LangVersion=latest "-p:RestorePackagesPath=$root/artifacts/nuget-cache" ${1+"$@"} > build.log 2>&1)
  grep -E " error |warning" "$out/build.log" | sort -u
}
# The files of that name in the output, as ./path, one per line.
files() { (cd "artifacts/modes/$1/out" && find . -type f \( -name "$2" -o -name "$2.exe" \) | sort); }
lines() { if [ -z "$1" ]; then echo 0; else echo "$1" | wc -l | tr -d ' '; fi; }
smoke() { local name=$1; shift; env ${1+"$@"} dotnet "artifacts/modes/$name/out/ChromaDotNet.Local.TestHost.dll" smoke 2>&1; }
failed() { [ "$1" -ne 0 ] && echo failed || echo started; }

echo "== chromadb"
make chromadb chromadb
run=$(smoke chromadb); code=$?
echo "$run" | head -2
check "chromadb: smoke" "$code" 0
check "chromadb: the executables of the five platforms" "$(lines "$(files chromadb chromadb)")" 5
check "chromadb: the guards of Linux and macOS" "$(lines "$(files chromadb chromadotnet-local-guard)")" 4

echo "== chromadb, published for $rid"
make chromadb-rid chromadb -r "$rid" --self-contained false
run=$(smoke chromadb-rid); code=$?
echo "$run" | head -2
check "chromadb for $rid: smoke" "$code" 0
check "chromadb for $rid: one executable, next to the application" "$(files chromadb-rid chromadb)" "./chromadb$exe"
check "chromadb for $rid: its guard" "$(lines "$(files chromadb-rid chromadotnet-local-guard)")" $guards

echo "== chromadb-download"
make chromadb-download chromadb-download
run=$(smoke chromadb-download CHROMA_LOCAL_NO_DOWNLOAD=1); code=$?
echo "$run" | grep -m1 "Exception" | cut -c1-300
check "chromadb-download: start fails" "$(failed $code)" failed
check "chromadb-download: the message names the property" "$(echo "$run" | grep -c 'unless ChromaDotNetLocalChromaDBDownload takes them out of the output')" 1
check "chromadb-download: no executable" "$(lines "$(files chromadb-download chromadb)")" 0
check "chromadb-download: the guards" "$(lines "$(files chromadb-download chromadotnet-local-guard)")" 4
make chromadb-download-rid chromadb-download -r "$rid" --self-contained false
check "chromadb-download for $rid: no executable in the publish" "$(lines "$(files chromadb-download-rid chromadb)")" 0

echo "== vecturadb"
make vecturadb vecturadb
run=$(smoke vecturadb CHROMA_LOCAL_NO_DOWNLOAD=1); code=$?
echo "$run" | grep -m1 "Exception" | cut -c1-300
check "vecturadb: start fails" "$(failed $code)" failed
check "vecturadb: the message says the download" "$(echo "$run" | grep -c 'ChromaDotNet.Local.VecturaDB downloads the executables of win-x64, linux-x64, linux-arm64, osx-x64 and osx-arm64 at the first start')" 1
check "vecturadb: no executable" "$(lines "$(files vecturadb vecturadb)")" 0

echo "== an application named vecturadb, as the executable of the engine"
make named-vecturadb vecturadb -p:AssemblyName=vecturadb
run=$(env CHROMA_LOCAL_NO_DOWNLOAD=1 dotnet artifacts/modes/named-vecturadb/out/vecturadb.dll smoke 2>&1); code=$?
echo "$run" | grep -m1 "Exception" | cut -c1-200
check "named vecturadb: its apphost next to it" "$(lines "$(files named-vecturadb vecturadb)")" 1
check "named vecturadb: it does not start itself, the start says the download" "$(failed $code)/$(echo "$run" | grep -c 'No VecturaDB executable for')" failed/1

echo "== an application named chromadb without an apphost, published for $rid"
make named-chromadb-rid chromadb -r "$rid" --self-contained false -p:AssemblyName=chromadb -p:UseAppHost=false
run=$(env CHROMA_LOCAL_NO_DOWNLOAD=1 dotnet artifacts/modes/named-chromadb-rid/out/chromadb.dll smoke 2>&1); code=$?
echo "$run" | head -1
check "named chromadb for $rid: it starts the engine next to it, without the download" "$code" 0

echo "== base"
make base base
check "base: the warning at the build" "$(grep 'warning CDNL001: ChromaDotNet.Local has no engine: add the package ChromaDotNet.Local.ChromaDB for Chroma, or ChromaDotNet.Local.VecturaDB for VecturaDB.' artifacts/modes/base/build.log | sort -u | wc -l | tr -d ' ')" 1
run=$(smoke base); code=$?
echo "$run" | grep -m1 "Exception" | cut -c1-300
check "base: start fails" "$(failed $code)" failed
check "base: the message names the engines" "$(echo "$run" | grep -c 'ChromaDotNet.Local has no engine. Add ChromaDotNet.Local.ChromaDB for Chroma, or ChromaDotNet.Local.VecturaDB for VecturaDB.')" 1

if [ "${1:-}" = aot ]; then
  # The linker of NativeAOT on Windows finds Visual Studio with vswhere.exe, through %ProgramFiles(x86)%, which bash does not pass on.
  [ -n "$exe" ] && PATH="$PATH:/c/Program Files (x86)/Microsoft Visual Studio/Installer"
  for engine in chromadb vecturadb; do
    echo "== $engine, NativeAOT for $rid"
    make $engine-aot $engine -r "$rid" -p:Aot=true
    check "$engine with NativeAOT: no warning of trimming or AOT from the packages" "$(grep -E 'warning IL[0-9]+' artifacts/modes/$engine-aot/build.log | grep -c 'ChromaDotNet\.Local')" 0
    run=$("artifacts/modes/$engine-aot/out/ChromaDotNet.Local.TestHost$exe" smoke 2>&1); code=$?
    echo "$run" | head -2
    check "$engine with NativeAOT: smoke" "$code" 0
  done
fi
echo "failures: $fails"
exit $fails
