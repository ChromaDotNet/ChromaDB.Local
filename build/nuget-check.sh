#!/bin/bash
# Usage: build/nuget-check.sh chromadb|vecturadb <version>. Inside a container of the .NET SDK, once the packages are on nuget.org: a new
# application with the engine package from nuget.org, and nothing else; the first start downloads the executable from the GitHub
# release of the engine, the second takes it from the cache. ChromaDB with ChromaDotNetLocalChromaDBDownload, which takes the
# executables of its runtime packages out of the output.
set -euo pipefail
engine=${1:-}; version=${2:-}
case "$engine" in
  chromadb) package=ChromaDotNet.Local.ChromaDB; property='<ChromaDotNetLocalChromaDBDownload>true</ChromaDotNetLocalChromaDBDownload>' ;;
  vecturadb) package=ChromaDotNet.Local.VecturaDB; property= ;;
  *) echo "usage: build/nuget-check.sh chromadb|vecturadb <version>" >&2; exit 2 ;;
esac
cd /tmp && rm -rf app && mkdir app && cd app
dotnet new console -o . >/dev/null
dotnet add package "$package" --version "$version" >/dev/null
if [ -n "$property" ]; then sed -i "s|</PropertyGroup>|  $property\n  </PropertyGroup>|" app.csproj; fi
cat > Program.cs <<'EOF'
using System.Diagnostics;
using ChromaDotNet.Local;

var stopwatch = Stopwatch.StartNew();
await using var server = await ChromaLocalServer.StartAsync();
var started = stopwatch.ElapsedMilliseconds;
using var client = server.CreateClient();
var collection = await client.GetOrCreateCollectionAsync("check");
Console.WriteLine($"{server.Engine} {server.EngineVersion} on {server.Uri}, started in {started} ms, collection {collection.Name}");
EOF
for run in first second; do
  echo "== $run start"
  dotnet run -c Release
done
find "${XDG_CACHE_HOME:-$HOME/.cache}/chromadotnet-local" -type f | xargs ls -l
