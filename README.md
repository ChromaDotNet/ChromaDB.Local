# ChromaDotNet.Local

[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/ChromaDotNet/ChromaDB.Local/badge)](https://scorecard.dev/viewer/?uri=github.com/ChromaDotNet/ChromaDB.Local)
[![OpenSSF Best Practices](https://www.bestpractices.dev/projects/15735/badge)](https://www.bestpractices.dev/projects/15735)
[![Open in GitHub Codespaces](https://github.com/codespaces/badge.svg)](https://codespaces.new/ChromaDotNet/ChromaDB.Local)

Chroma, or VecturaDB, on this machine, without Docker and without Python, for
[ChromaDotNet.Client](https://www.nuget.org/packages/ChromaDotNet.Client). `ChromaLocalServer.StartAsync` starts the server of an
engine as a child process on 127.0.0.1, waits until it answers, and stops it with `Dispose` or when the application ends.

ChromaDotNet is an independent community project that provides .NET libraries and tools for Chroma. Independent project. Not affiliated with, endorsed by or sponsored by Chroma Inc. CHROMA is a registered trademark of Chroma Inc., used here only to describe compatibility. We use the name "Chroma" only to say which software our projects work with. It does not mean that Chroma Inc. made, approved or supports them.

> Preview: the API and the packages can still change.

```
dotnet add package ChromaDotNet.Local.ChromaDB --prerelease
```

```csharp
using ChromaDotNet.Local;

await using var chroma = await ChromaLocalServer.StartAsync(new() { DataPath = "./chroma-data" });
using var client = chroma.CreateClient();

var collection = await client.GetOrCreateCollectionAsync("docs");
```

`chroma.Uri` is the address of the server, like `http://127.0.0.1:52817`, for `ChromaConfigurationOptions` or `AddChromaClient`.

A sample that runs as it is: [samples/GettingStarted](https://github.com/ChromaDotNet/ChromaDB.Local/blob/main/samples/GettingStarted/Program.cs), with `dotnet run --project samples/GettingStarted`.

### Like the PersistentClient of Python

[samples/SearchByText/SearchByText.cs](https://github.com/ChromaDotNet/ChromaDB.Local/blob/main/samples/SearchByText/SearchByText.cs) does what
`chromadb.PersistentClient(path="chroma-data")` does in Python: Chroma with the data in `chroma-data`, and the default embedding
function of Chroma, with [ChromaDotNet.Embeddings](https://www.nuget.org/packages/ChromaDotNet.Embeddings). It adds documents
without embeddings and queries with a text. A second run finds the data of the first, and Python opens the same collection.
A single file, with the .NET 10 SDK:

```
dotnet run samples/SearchByText/SearchByText.cs
```

The Codespace of this repository runs it as it is: no Docker, no Python.

## Engines

An application references the package of an engine, which brings the base, ChromaDotNet.Local:

| Package | Server | Executables |
|---|---|---|
| [ChromaDotNet.Local.ChromaDB](https://www.nuget.org/packages/ChromaDotNet.Local.ChromaDB) | Chroma 1.5.9 (the Chroma CLI 1.4.4) | the five platforms, in its runtime packages |
| [ChromaDotNet.Local.VecturaDB](https://www.nuget.org/packages/ChromaDotNet.Local.VecturaDB) | [VecturaDB](https://github.com/VecturaDB/VecturaDB) 1.0.0-preview.3, compatible with the Chroma API, with sparse vectors, BM25, the Search API and collection fork | downloaded at the first start, from the GitHub release of VecturaDB |

The code is the same with each engine. [ChromaDotNet.Local](https://www.nuget.org/packages/ChromaDotNet.Local), the base, starts
and stops the server, and downloads and checks the executables; it has no engine of its own. A library can reference the base
alone, and leave the engine to the application.

With the two engines in one application, `ChromaLocalOptions.Engine` chooses the engine of each server, and the start throws an
`InvalidOperationException` without it:

```csharp
await using var vectura = await ChromaLocalServer.StartAsync(new() { Engine = ChromaLocalEngine.VecturaDB });
```

`Engine` and `EngineVersion` of the server say its engine and the version of the engine package.

Without an engine, the build of an application or of a project of tests gives the warning CDNL001, and the start throws an
`InvalidOperationException` that names the two packages, unless `ExecutablePath` names an executable.
`<ChromaDotNetLocalNoEngineWarning>false</ChromaDotNetLocalNoEngineWarning>` in the project turns the warning off.

### From 1.5.9.1-preview.5

| 1.5.9.1-preview.5 | 1.5.9.1-preview.7 |
|---|---|
| ChromaDotNet.Local | ChromaDotNet.Local.ChromaDB |
| ChromaDotNet.Local.Core and `ChromaDotNet.Local.Runtime.<rid>` | ChromaDotNet.Local.ChromaDB |
| ChromaDotNet.Local.Core alone | ChromaDotNet.Local.ChromaDB, with `ChromaDotNetLocalChromaDBDownload` |

The code stays the same. `ChromaLocalServer.ChromaVersion` is obsolete: `EngineVersion` of the server says the version. A reference
to ChromaDotNet.Local 1.5.9.1-preview.5 next to ChromaDotNet.Local.ChromaDB fails at the restore with NU1605: update it to
1.5.9.1-preview.7, or remove it.

The data of 1.5.9.1-preview.5 opens with both engines, and the data of each engine opens with the other.

> In our tests, Chroma 1.5.9 reads the data of VecturaDB except its sparse vectors, which stay in the data. What Chroma writes there
> has no BM25 vectors: back on VecturaDB, BM25 does not find the records that Chroma added, and a document that Chroma changed keeps
> the BM25 vector of its old text. Do not write with ChromaDotNet.Local.ChromaDB on data of VecturaDB with sparse vectors or BM25.

## The executables of ChromaDotNet.Local.ChromaDB

ChromaDotNet.Local.ChromaDB brings the runtime package of each platform, with its executable, as Microsoft.ML.OnnxRuntime and the
bundles of SQLitePCLRaw do:
[win-x64](https://www.nuget.org/packages/ChromaDotNet.Local.ChromaDB.Runtime.win-x64),
[linux-x64](https://www.nuget.org/packages/ChromaDotNet.Local.ChromaDB.Runtime.linux-x64),
[linux-arm64](https://www.nuget.org/packages/ChromaDotNet.Local.ChromaDB.Runtime.linux-arm64),
[osx-x64](https://www.nuget.org/packages/ChromaDotNet.Local.ChromaDB.Runtime.osx-x64) and
[osx-arm64](https://www.nuget.org/packages/ChromaDotNet.Local.ChromaDB.Runtime.osx-arm64). An application published for one
platform (`-r linux-x64`) gets only the executable of that platform.

With `<ChromaDotNetLocalChromaDBDownload>true</ChromaDotNetLocalChromaDBDownload>` in the project, the executables are not in the
output, and the first start downloads the one of its platform from the
[GitHub release](https://github.com/ChromaDotNet/ChromaDB.Local/releases) of the same version, about 20 MB. The restore downloads
the runtime packages all the same, about 100 MB in all. On .NET Framework a library that references ChromaDotNet.Local.ChromaDB
brings the executable into the output of the application that references it: the property goes into the library too, or into a
`Directory.Build.props`.

## The download at the first start

ChromaDotNet.Local.VecturaDB downloads the executable of its platform, about 50 MB, from the
[GitHub release of VecturaDB](https://github.com/VecturaDB/VecturaDB/releases) of its version; ChromaDotNet.Local.ChromaDB does
with `ChromaDotNetLocalChromaDBDownload`. The executable goes into a directory of the user, one for each engine and version. The
engine package has the SHA-256 hash of each executable: the start checks the file after the download and before each start, and
does not keep a file with another hash. Two processes that start at the same time download it once.

| `ChromaLocalOptions` | Default | |
|---|---|---|
| `DownloadExecutable` | `true` | `false` turns the download off: then the executable of a runtime package, or `ExecutablePath`, is needed. |
| `CacheDirectory` | `%LOCALAPPDATA%\ChromaDotNet.Local`, `~/Library/Caches/ChromaDotNet.Local`, `~/.cache/chromadotnet-local` | Where the executables go. |
| `DownloadUri` | the GitHub release of the engine | A mirror with the files of that release, like `https://mirror.example/chromadotnet-local/chromadb-v1.5.9.1-preview.7/`. |

Without the network and without the file in the cache, the start throws an `InvalidOperationException` with the address it tried
and what to add to start without the network. Each release of ChromaDotNet.Local.ChromaDB has `SHA256SUMS` of the compressed
executables, and a Sigstore signature and build provenance for each of them and for each package.

## Options

| `ChromaLocalOptions` | Default | |
|---|---|---|
| `Engine` | the engine package of the application | The engine of the server, when the application has the two. |
| `DataPath` | a new temporary directory | Where the server keeps its data. The temporary directory is deleted at the stop; when the application is killed before, a later start with temporary data deletes it. On Windows, at most 206 characters with Chroma and 200 with VecturaDB; with an `ExecutablePath` and no engine package, 206. |
| `Port` | a free port | The port on 127.0.0.1. |
| `StartupTimeout` | 30 s | How long the start waits for `/api/v2/heartbeat`. |
| `ShutdownTimeout` | 5 s | Linux and macOS: how long the stop waits after SIGTERM, before SIGKILL. |
| `Logger` | none | Receives the log of the server, with its levels. |
| `ExecutablePath` | the executable of the engine | Another executable of the server. |

Two servers on the same data can damage it, and the server does not stop the second: a `ChromaLocalServer` holds the file
`chromadotnet-local.lock` of its directory open while it runs, and a second start on the same directory throws an
`InvalidOperationException`.

## The stop

`StopAsync`, `DisposeAsync` and `Dispose` stop the server and wait for it. The server also stops when the application ends without
them, or is killed:

- Windows: the server is in a job object that Windows closes with the application. On Windows the stop ends the process at once.
- Linux and macOS: the server is the child of `chromadotnet-local-guard`, a small program of this repository in the base package,
  which stops it with SIGTERM, and SIGKILL after `ShutdownTimeout`, when the pipe from the application closes. On Linux the server
  also gets PR_SET_PDEATHSIG. The start finds the guard, as the executable of the engine, also in a single file with its native
  libraries inside, and in a plugin of another application, like a module of PowerShell.

## Platforms

| Runtime package | Chroma executable |
|---|---|
| `ChromaDotNet.Local.ChromaDB.Runtime.win-x64` | from the GitHub release of the Chroma CLI, hash checked |
| `ChromaDotNet.Local.ChromaDB.Runtime.osx-arm64` | from the GitHub release of the Chroma CLI, hash checked |
| `ChromaDotNet.Local.ChromaDB.Runtime.osx-x64` | from the GitHub release of the Chroma CLI, hash checked |
| `ChromaDotNet.Local.ChromaDB.Runtime.linux-x64` | built from the tag of the Chroma CLI, with no change to the code, for glibc 2.28 and later |
| `ChromaDotNet.Local.ChromaDB.Runtime.linux-arm64` | built from the tag, as linux-x64 |

The executables of VecturaDB are those of its GitHub release, for Windows x64, Linux x64 and arm64 (glibc 2.17 and later), macOS
x64 and arm64; the CI checks their hashes against the digests of the release.

In a container published as root and started as another user, as the Dockerfiles of .NET do: NuGet extracts the executables
with the mode 744, so only root can run them. Then ChromaDotNet.Local starts a copy in the cache of the user, checked against the
hashes of the packages. Without a cache it can write, the start says to run `chmod a+x` on the files, or to copy them with
`COPY --chmod=755`.

Windows on Arm runs the win-x64 executable through emulation. Linux with musl, like Alpine, is not supported.

.NET 8 and later, and .NET Framework on Windows through netstandard2.0 (tested with 4.8). The packages are marked as compatible
with trimming and NativeAOT, and the CI starts an application published with NativeAOT with each engine, on each platform.

## Versions

Each package has a line of versions, with tags of its own:

| Package | Version | Tags |
|---|---|---|
| ChromaDotNet.Local | 1.5.9.1-preview.N | `v1.5.9.1-preview.N` |
| ChromaDotNet.Local.ChromaDB and its runtime packages | the version of Chroma, then the revision of these packages: 1.5.9.1 has Chroma 1.5.9 (the Chroma CLI 1.4.4) | `chromadb-v1.5.9.1-preview.N` |
| ChromaDotNet.Local.VecturaDB | the version of VecturaDB, then the revision of this package: 1.0.0-preview.3.1 has VecturaDB 1.0.0-preview.3 | `vecturadb-v1.0.0-preview.3.N` |

The engines work with the base from 1.5.9.1-preview.7 to before 2.0.

## Licenses

The code of this repository, the guard included, is under the MIT License. Chroma is under the Apache License 2.0; the runtime
packages of ChromaDotNet.Local.ChromaDB carry its text in `LICENSE.txt`, and the notices of the crates in the executable in
`THIRD-PARTY-NOTICES.md`, which `build/third-party-notices.sh` writes before the pack. VecturaDB is under the Apache License 2.0;
ChromaDotNet.Local.VecturaDB carries its LICENSE, NOTICE and THIRD-PARTY-NOTICES, at its tag, in `THIRD-PARTY-NOTICES.md`, which
`build/vecturadb-notices.sh` writes before the pack.

## Build

- `build/fetch-chroma-cli.sh`: the executables of Windows and macOS of the GitHub release of the Chroma CLI, checked against its digests.
- `build/build-linux-docker.sh x64|arm64`: the Linux executables and the guard, in manylinux_2_28. Two builds of the same tag give
  the same bytes.
- `build/build-guard-macos.sh`: the guard for macOS, with zig.
- `build/third-party-notices.sh`: THIRD-PARTY-NOTICES.md, with cargo about.
- `build/check-vecturadb.py`: the hashes of the executables of VecturaDB against the digests of its release; `--write` takes them
  for a new tag.
- `build/vecturadb-notices.sh`: the notices of VecturaDB at its tag.
- `build/pack.sh`: the packages, in artifacts/packages, after `build/stage.sh`, which gives the executables their names in the
  packages.
- `build/release-assets.sh`: the files of the release of ChromaDotNet.Local.ChromaDB.
- `build/first-start.sh <executable> [guard]`: the time of three starts of an executable until the heartbeat, inside a container.
- Tests: `dotnet test tests/ChromaDotNet.Local.Tests`, with the two engines; `guard/test_guard.py`; `build/package-modes.sh [aot]`,
  the ways to reference the packages, and NativeAOT; `build/migration-tests.sh`, from the packages of 1.5.9.1-preview.5;
  `build/download-tests.sh chromadb|vecturadb`, the download at the first start; `build/nonroot-test.sh`, an application published
  as root and started as another user; `build/guard-hosts-test.sh`, the guard in a single file, in a plugin and in a module of
  PowerShell; and `build/client-suite.sh`, the tests of ChromaDotNet.Client against the engine of
  `CHROMA_LOCAL_ENGINE`. `build/linux-tests.sh` runs the tests in a Linux container.

The CI, in `.github/workflows/ci.yml`, does all of it on Windows, Linux x64 and arm64, macOS arm64 and x64.
