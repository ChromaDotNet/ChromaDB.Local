# ChromaDotNet.Local

Chroma on this machine, without Docker and without Python, for [ChromaDotNet.Client](https://www.nuget.org/packages/ChromaDotNet.Client).
`ChromaLocalServer.StartAsync` starts the official Chroma executable as a child process on 127.0.0.1, waits until it answers, and
stops it with `Dispose` or when the application ends.

Community project, not affiliated with Chroma.

> Preview. The packages are not on NuGet yet: each run of the CI leaves them in its artifacts, as `packages`.

```
dotnet add package ChromaDotNet.Local --prerelease
```

```csharp
using ChromaDotNet.Local;

await using var chroma = await ChromaLocalServer.StartAsync(new() { DataPath = "./chroma-data" });
using var client = chroma.CreateClient();

var collection = await client.GetOrCreateCollectionAsync("docs");
```

`chroma.Uri` is the address of the server, like `http://127.0.0.1:52817`, for `ChromaConfigurationOptions` or `AddChromaClient`.

A sample that runs as it is: [samples/GettingStarted](https://github.com/ChromaDotNet/ChromaDB.Local/blob/main/samples/GettingStarted/Program.cs), with `dotnet run --project samples/GettingStarted`.

## Packages

| Package | |
|---|---|
| `ChromaDotNet.Local` | Everything: `ChromaDotNet.Local.Core` and the runtime packages of the five platforms. Works offline, on any of them. |
| `ChromaDotNet.Local.Core` | The code, without the executables: add the runtime packages of the platforms the application runs on. |
| `ChromaDotNet.Local.Runtime.<rid>` | The Chroma executable of one platform: `win-x64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`. |

The same split as Microsoft.ML.OnnxRuntime and Microsoft.ML.OnnxRuntime.Managed, or the bundles of SQLitePCLRaw. For one platform:

```
dotnet add package ChromaDotNet.Local.Core --prerelease
dotnet add package ChromaDotNet.Local.Runtime.linux-x64 --prerelease
```

An application published for one platform (`-r linux-x64`) gets only the executable of that platform, with either way.

## Options

| `ChromaLocalOptions` | Default | |
|---|---|---|
| `DataPath` | a new temporary directory | Where Chroma keeps its data. The temporary directory is deleted at the stop, as the ephemeral client of Python does. |
| `Port` | a free port | The port on 127.0.0.1. |
| `StartupTimeout` | 30 s | How long the start waits for `/api/v2/heartbeat`. |
| `ShutdownTimeout` | 5 s | Linux and macOS: how long the stop waits after SIGTERM, before SIGKILL. |
| `Logger` | none | Receives the log of Chroma, with its levels. |
| `ExecutablePath` | the runtime package | Another Chroma executable. |

Two servers on the same data can damage it, and Chroma does not stop the second: a `ChromaLocalServer` holds the file
`chromadotnet-local.lock` of its directory open while it runs, and a second start on the same directory throws an
`InvalidOperationException`.

## The stop

`StopAsync`, `DisposeAsync` and `Dispose` stop Chroma and wait for it. Chroma also stops when the application ends without them,
or is killed:

- Windows: Chroma is in a job object that Windows closes with the application. On Windows the stop ends the process at once.
- Linux and macOS: Chroma is the child of `chroma-local-guard`, a small program of this repository, which stops it with SIGTERM, and
  SIGKILL after `ShutdownTimeout`, when the pipe from the application closes. On Linux Chroma also gets PR_SET_PDEATHSIG.

## Platforms

| Runtime package | Chroma executable |
|---|---|
| `ChromaDotNet.Local.Runtime.win-x64` | official, from the GitHub release of the Chroma CLI, hash checked |
| `ChromaDotNet.Local.Runtime.osx-arm64` | official, hash checked |
| `ChromaDotNet.Local.Runtime.osx-x64` | official, hash checked |
| `ChromaDotNet.Local.Runtime.linux-x64` | built from the tag of the Chroma CLI, with no change to the code, for glibc 2.28 and later |
| `ChromaDotNet.Local.Runtime.linux-arm64` | built from the tag, as linux-x64 |

Windows on Arm runs the win-x64 executable through emulation. Linux with musl, like Alpine, is not supported.

.NET 8 and later, and .NET Framework on Windows through netstandard2.0 (tested with 4.8). The library is marked as compatible with
trimming and NativeAOT, and an application published with NativeAOT for win-x64 works.

## Versions

The version is the version of Chroma in the runtime packages, then the revision of these packages: 1.5.9.1 has Chroma 1.5.9 (the
Chroma CLI 1.4.4). `ChromaLocalServer.ChromaVersion` says it.

## Licenses

The code of this repository, `chroma-local-guard` included, is under the MIT License. Chroma is under the Apache License 2.0; the
runtime packages carry its text in `LICENSE.txt`, and the notices of the crates in the executable in `THIRD-PARTY-NOTICES.md`,
which `build/third-party-notices.sh` writes before the pack.

## Build

- `build/fetch-official.sh`: the official executables of Windows and macOS, checked against the digests of the GitHub release.
- `build/build-linux-docker.sh x64|arm64`: the Linux executables and the guard, in manylinux_2_28. Two builds of the same tag give
  the same bytes.
- `build/build-guard-macos.sh`: the guard for macOS, with zig.
- `build/third-party-notices.sh`: THIRD-PARTY-NOTICES.md, with cargo about.
- `build/pack.sh`: the packages, in artifacts/packages.
- Tests: `dotnet test tests/ChromaDotNet.Local.Tests`, `guard/test_guard.py`, `build/package-modes.sh`, and
  `build/client-suite.sh`, the tests of ChromaDotNet.Client against a Chroma started by this package. `build/linux-tests.sh` runs
  them in a Linux container.

The CI, in `.github/workflows/ci.yml`, does all of it on Windows, Linux x64 and arm64, macOS arm64 and x64.
