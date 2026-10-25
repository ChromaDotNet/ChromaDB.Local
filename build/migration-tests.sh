#!/bin/bash
# Usage: build/migration-tests.sh. From the packages of 1.5.9.1-preview.5 on nuget.org to those of artifacts/packages, with small
# applications in artifacts/migration/ that build with a nuget.config of their own, both sources and no mapping, so that each version
# comes from where it is:
#   1. an application on ChromaDotNet.Local 1.5.9.1-preview.5 writes data; updated to the base alone, the warning CDNL001 at the build
#      and the error at the start name ChromaDotNet.Local.ChromaDB; with that engine added it starts, and reads the data;
#   2. ChromaDotNet.Local.ChromaDB added and ChromaDotNet.Local left at 1.5.9.1-preview.5: the restore fails with NU1605;
#   3. ChromaDotNet.Local.Core and ChromaDotNet.Local.Runtime.<rid> of preview.5 with ChromaDotNet.Local.ChromaDB: the build and the
#      publish for this platform without NETSDK1152, ChromaDotNet.Local.dll of the base wins over that of Core, which has the same
#      version, as a package that the SDK prefers in a conflict, and the executable that runs is the one of ChromaDotNet.Local.ChromaDB,
#      by its hash in artifacts/release (build/release-assets.sh);
#   4. ChromaDotNet.Local.Core alone, which downloads from its release, replaced by ChromaDotNet.Local.ChromaDB with
#      ChromaDotNetLocalChromaDBDownload: the download from the release of ChromaDB, here the mirror of artifacts/release, goes into a
#      directory of the cache of its own, and the one of Core stays;
#   5. the data of 1 opened by VecturaDB, which writes, and again by preview.5;
#   6. the two engines in one application, the test host: the start fails without a choice, and each engine starts when chosen.
# The 7th case, NativeAOT and trimming with each engine, is in build/package-modes.sh aot. Bash 3.2 too, as on macOS.
set -uo pipefail
cd "$(dirname "$0")/.."
root=$(pwd)
work=artifacts/migration
rm -rf "$work"; mkdir -p "$work"
for f in Directory.Build.props Directory.Build.targets; do [ -f "artifacts/$f" ] || echo '<Project />' > "artifacts/$f"; done
cat > "$work/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
	<packageSources>
		<clear />
		<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
		<add key="local" value="$(pwd -W 2>/dev/null || pwd)/artifacts/packages" />
	</packageSources>
</configuration>
EOF
version() { dotnet msbuild "$1" -getProperty:Version -nologo | tr -d '\r'; }
old=1.5.9.1-preview.5
base=$(version src/ChromaDotNet.Local)
chromadb=$(version src/ChromaDotNet.Local.ChromaDB)
vecturadb=$(version src/ChromaDotNet.Local.VecturaDB)
rid=$(dotnet --info | awk '/ RID:/ {print $2; exit}')
case "$rid" in win-*) exe=.exe ;; *) exe= ;; esac
native() { if command -v cygpath >/dev/null; then cygpath -w "$1"; else echo "$1"; fi; }
fails=0
check() { if [ "$2" = "$3" ]; then echo "ok   $1"; else echo "FAIL $1: expected $3, got $2"; fails=$((fails + 1)); fi; }
failed() { [ "$1" -ne 0 ] && echo failed || echo started; }
data=$(native "$root/$work/data")

# An application of that name with these packages, "id version" each, and the properties after them; built into out/, with the log
# in build.log.
app() {
  local name=$1; shift
  local dir=$work/$name references="" properties=""
  mkdir -p "$dir"
  while [ $# -gt 0 ] && [ "$1" != -- ]; do references="$references<PackageReference Include=\"$1\" Version=\"$2\" />"; shift 2; done
  [ "${1:-}" = -- ] && { shift; properties="${*:-}"; }
  cat > "$dir/$name.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<OutputType>Exe</OutputType>
		<TargetFramework>net8.0</TargetFramework>
		<ImplicitUsings>enable</ImplicitUsings>
		<Nullable>enable</Nullable>
		$properties
	</PropertyGroup>
	<ItemGroup>$references</ItemGroup>
</Project>
EOF
  # The API of preview.5, which the base keeps: the server of the only engine.
  cat > "$dir/Program.cs" <<'EOF'
using System.Diagnostics;
using System.Reflection;
using ChromaDotNet.Local;

// Usage: <write|read> <dataPath>. Starts the server on the data, writes one more record in write, and says which assembly and which
// executable ran, and how many records the collection has.
var options = new ChromaLocalOptions { DataPath = args[1] };
if (Environment.GetEnvironmentVariable("CHROMA_LOCAL_CACHE") is { Length: > 0 } cache)
	options.CacheDirectory = cache;
if (Environment.GetEnvironmentVariable("CHROMA_LOCAL_DOWNLOAD_URI") is { Length: > 0 } uri)
	options.DownloadUri = new Uri(uri);
await using var server = await ChromaLocalServer.StartAsync(options);
using var client = server.CreateClient();
var collection = client.GetCollectionClient(await client.GetOrCreateCollectionAsync("migrated"));
if (args[0] == "write")
{
	var n = await collection.CountAsync();
	await collection.AddAsync([$"id{n}"], [new float[] { n, 1 }]);
}
using var process = Process.GetProcessById(server.ProcessId);
Console.WriteLine($"assembly {typeof(ChromaLocalServer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion}, executable {process.MainModule!.FileName}, count {await collection.CountAsync()}");
EOF
  rm -rf "$dir/out" "$dir/obj"
  (cd "$dir" && dotnet build -c Release -v q -nologo -o out "-p:RestoreConfigFile=$root/$work/nuget.config" \
    "-p:RestorePackagesPath=$root/$work/nuget-cache" > build.log 2>&1)
}
run() { local name=$1 mode=$2 path=$3; shift 3; env ${1+"$@"} dotnet "$work/$name/out/$name.dll" "$mode" "$path" 2>&1; }
field() { echo "$1" | grep -o "$2 [^,]*" | tail -1 | cut -d' ' -f2-; }
# The SHA-256 of a file, or of the standard input; shasum where sha256sum is not there, as on macOS.
hash() { if command -v sha256sum >/dev/null; then sha256sum ${1+"$1"}; else shasum -a 256 ${1+"$1"}; fi | cut -d' ' -f1; }
unix() { if command -v cygpath >/dev/null; then cygpath -u "$1"; else echo "$1"; fi; }
has() { grep -q -- "$1" "$2" && echo yes || echo no; }

echo "== 1. ChromaDotNet.Local $old, then the base $base alone, then with ChromaDotNet.Local.ChromaDB $chromadb"
app p5 ChromaDotNet.Local $old; check "1 preview.5: build" "$?" 0
out=$(run p5 write "$data"); code=$?
echo "$out" | tail -1
check "1 preview.5: writes" "$code" 0
check "1 preview.5: one record" "$(field "$out" count)" 1
out=$(run p5 write "$data"); check "1 preview.5: two records" "$(field "$out" count)" 2
app base-only ChromaDotNet.Local $base; check "1 base alone: build" "$?" 0
check "1 base alone: the warning names ChromaDotNet.Local.ChromaDB" "$(has 'warning CDNL001: ChromaDotNet.Local has no engine: add the package ChromaDotNet.Local.ChromaDB for Chroma' "$work/base-only/build.log")" yes
out=$(run base-only read "$data"); code=$?
check "1 base alone: start fails" "$(failed $code)" failed
check "1 base alone: the error names ChromaDotNet.Local.ChromaDB" "$(echo "$out" | grep -c 'ChromaDotNet.Local has no engine. Add ChromaDotNet.Local.ChromaDB for Chroma')" 1
app with-chromadb ChromaDotNet.Local $base ChromaDotNet.Local.ChromaDB $chromadb; check "1 with ChromaDB: build" "$?" 0
check "1 with ChromaDB: no warning" "$(has CDNL001 "$work/with-chromadb/build.log")" no
out=$(run with-chromadb read "$data"); code=$?
echo "$out" | tail -1
check "1 with ChromaDB: starts" "$code" 0
check "1 with ChromaDB: reads the data of preview.5" "$(field "$out" count)" 2
check "1 with ChromaDB: the assembly of the base" "$(field "$out" assembly | cut -d+ -f1)" "$base"

echo "== 2. ChromaDotNet.Local.ChromaDB with ChromaDotNet.Local left at $old"
app downgrade ChromaDotNet.Local $old ChromaDotNet.Local.ChromaDB $chromadb; code=$?
grep -m1 "NU1605" "$work/downgrade/build.log" | cut -c1-300
check "2 downgrade: the restore fails" "$(failed $code)" failed
check "2 downgrade: NU1605 on ChromaDotNet.Local" "$(has "error NU1605: .*Detected package downgrade: ChromaDotNet.Local from $base to $old" "$work/downgrade/build.log")" yes

echo "== 3. ChromaDotNet.Local.Core and ChromaDotNet.Local.Runtime.$rid $old with ChromaDotNet.Local.ChromaDB $chromadb"
app mixed ChromaDotNet.Local.Core $old ChromaDotNet.Local.Runtime.$rid $old ChromaDotNet.Local.ChromaDB $chromadb; check "3 mixed: build" "$?" 0
check "3 mixed: no NETSDK1152 at the build" "$(grep -c NETSDK1152 "$work/mixed/build.log")" 0
# In detail and in English, for the messages of the conflicts.
(cd "$work/mixed" && DOTNET_CLI_UI_LANGUAGE=en dotnet publish -c Release -v d -nologo -r "$rid" --self-contained false -o pub \
  "-p:RestoreConfigFile=$root/$work/nuget.config" "-p:RestorePackagesPath=$root/$work/nuget-cache" > publish.log 2>&1); code=$?
check "3 mixed: publish for $rid" "$code" 0
check "3 mixed: no NETSDK1152 at the publish" "$(grep -c NETSDK1152 "$work/mixed/publish.log")" 0
# The two ChromaDotNet.Local.dll, at the build and at the publish: each conflict goes to the base by the rule, not by the order.
conflicts=$(grep "Encountered conflict between '[A-Za-z]*:[^']*ChromaDotNet.Local.dll'" "$work/mixed/publish.log")
check "3 mixed: the conflicts of ChromaDotNet.Local.dll" "$([ "$(echo "$conflicts" | grep -c .)" -ge 2 ] && echo found || echo missing)" found
check "3 mixed: each to the base, as a package the SDK prefers" \
  "$(echo "$conflicts" | grep -v -c "Choosing '[A-Za-z]*:[^']*chromadotnet.local.$base.[^']*' because it comes from a package that is preferred")" 0
expected=$(gzip -dc "artifacts/release/chromadb-$rid$exe.gz" | hash)
for output in out pub; do
  out=$(env dotnet "$work/mixed/$output/mixed.dll" read "$(native "$root/$work/data-mixed")" 2>&1); code=$?
  echo "$out" | tail -1
  check "3 mixed, $output: starts" "$code" 0
  check "3 mixed, $output: the assembly of the base" "$(field "$out" assembly | cut -d+ -f1)" "$base"
  executable=$(unix "$(field "$out" executable)")
  check "3 mixed, $output: the executable of ChromaDotNet.Local.ChromaDB" "$(basename "$executable")" "chromadb$exe"
  check "3 mixed, $output: its hash" "$(hash "$executable")" "$expected"
done

echo "== 4. ChromaDotNet.Local.Core $old alone, then ChromaDotNet.Local.ChromaDB with ChromaDotNetLocalChromaDBDownload"
cache=$root/$work/cache
app core ChromaDotNet.Local.Core $old; check "4 Core alone: build" "$?" 0
out=$(run core read "$(native "$root/$work/data-core")" CHROMA_LOCAL_CACHE="$(native "$cache")"); code=$?
echo "$out" | tail -1
check "4 Core alone: downloads from its release and starts" "$code" 0
check "4 Core alone: the cache of preview.5" "$(ls "$cache")" "v$old"
py=$(for c in python3 python; do "$c" -c "" >/dev/null 2>&1 && { echo "$c"; break; }; done)
port=$((20000 + RANDOM % 20000))
"$py" build/serve_release.py artifacts/release $port "$root/$work/requests.log" & mirror=$!
for _ in $(seq 1 100); do curl -s -o /dev/null "http://127.0.0.1:$port/" && break; sleep 0.1; done
app download ChromaDotNet.Local.ChromaDB $chromadb -- '<ChromaDotNetLocalChromaDBDownload>true</ChromaDotNetLocalChromaDBDownload>'
check "4 ChromaDB download: build" "$?" 0
check "4 ChromaDB download: no executable in the output" "$(find "$work/download/out" -name "chromadb$exe" | wc -l | tr -d ' ')" 0
out=$(run download read "$(native "$root/$work/data-core")" CHROMA_LOCAL_CACHE="$(native "$cache")" CHROMA_LOCAL_DOWNLOAD_URI="http://127.0.0.1:$port/"); code=$?
kill $mirror 2>/dev/null; wait $mirror 2>/dev/null
echo "$out" | tail -1
check "4 ChromaDB download: downloads and starts" "$code" 0
check "4 ChromaDB download: the file of its release" "$(tr -d '\r' < "$root/$work/requests.log" | tr '\n' ' ')" "chromadb-$rid$exe.gz "
check "4 ChromaDB download: the cache of each, side by side" "$(ls "$cache" | tr '\n' ' ')" "chromadb-v$chromadb v$old "
check "4 ChromaDB download: the executable from the cache" "$(dirname "$(unix "$(field "$out" executable)")" | xargs basename)" "$rid"

echo "== 5. The data of 1, opened by VecturaDB $vecturadb, then by preview.5"
app with-vecturadb ChromaDotNet.Local.VecturaDB $vecturadb; check "5 VecturaDB: build" "$?" 0
out=$(run with-vecturadb write "$data"); code=$?
echo "$out" | tail -1
check "5 VecturaDB: reads the data of preview.5 and of ChromaDB, and writes" "$code/$(field "$out" count)" "0/3"
out=$(run p5 read "$data"); code=$?
echo "$out" | tail -1
check "5 preview.5: reads the data of VecturaDB" "$code/$(field "$out" count)" "0/3"

echo "== 6. The two engines in one application, the test host"
host=tests/ChromaDotNet.Local.TestHost/bin/Release/net8.0/ChromaDotNet.Local.TestHost.dll
[ -f "$host" ] || dotnet build tests/ChromaDotNet.Local.TestHost -c Release -f net8.0 -v q -nologo > /dev/null
out=$(dotnet "$host" smoke 2>&1); code=$?
check "6 two engines: start fails without a choice" "$(failed $code)" failed
check "6 two engines: the message says how to choose" "$(echo "$out" | grep -c 'The application has two engines, ChromaDotNet.Local.ChromaDB and ChromaDotNet.Local.VecturaDB: set ChromaLocalOptions.Engine')" 1
for engine in ChromaDB VecturaDB; do
  out=$(CHROMA_LOCAL_ENGINE=$engine dotnet "$host" smoke 2>&1); code=$?
  echo "$out" | head -1
  check "6 two engines: $engine starts when chosen" "$code/$(echo "$out" | grep -c "engine $engine ")" "0/1"
done

echo "failures: $fails"
exit $fails
