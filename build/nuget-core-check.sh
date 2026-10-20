#!/bin/bash
# Usage: build/nuget-core-check.sh <version>. Inside a container of the .NET SDK: a new application with ChromaDotNet.Local.Core from
# nuget.org, and nothing else; the first start downloads the executable from the GitHub release, the second takes it from the cache.
set -euo pipefail
version=$1
cd /tmp && rm -rf app && mkdir app && cd app
dotnet new console -o . >/dev/null
dotnet add package ChromaDotNet.Local.Core --version "$version" >/dev/null
cat > Program.cs <<'EOF'
using System.Diagnostics;
using ChromaDotNet.Local;

var stopwatch = Stopwatch.StartNew();
await using var chroma = await ChromaLocalServer.StartAsync();
var started = stopwatch.ElapsedMilliseconds;
using var client = chroma.CreateClient();
var collection = await client.GetOrCreateCollectionAsync("check");
Console.WriteLine($"Chroma {ChromaLocalServer.ChromaVersion} on {chroma.Uri}, started in {started} ms, collection {collection.Name}");
EOF
for run in first second; do
  echo "== $run start"
  dotnet run -c Release
done
find "${XDG_CACHE_HOME:-$HOME/.cache}/chromadotnet-local" -type f | xargs ls -l
