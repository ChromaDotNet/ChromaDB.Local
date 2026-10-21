Preview of ChromaDotNet.Local VERSION, with Chroma CHROMA_VERSION (the Chroma CLI CLI_TAG). Community project, not affiliated with Chroma.

The packages are on NuGet: [ChromaDotNet.Local](https://www.nuget.org/packages/ChromaDotNet.Local/VERSION), with the executables of
every platform; [ChromaDotNet.Local.Core](https://www.nuget.org/packages/ChromaDotNet.Local.Core/VERSION), without them, which
downloads the one of its platform from this release at the first start; and the ChromaDotNet.Local.Runtime package of each platform.

In this preview:

- The output of Chroma is read on threads of each server, not on the thread pool: on Windows many servers in one process no longer
  make the start wait past StartupTimeout.
- On Windows a DataPath longer than 206 characters throws a PathTooLongException before anything starts: with a longer one Chroma
  did not load the index of a collection after a restart.
- The lock file of the data says "another ChromaLocalServer" only when another process holds it; other errors say what they are.
- The cache deletes the temporary files of downloads that did not end, and gives errors with the path for a directory in place of
  an executable and for an executable deleted before the start.

The files of this release are the executables of the runtime packages, compressed with gzip. ChromaDotNet.Local.Core has the SHA-256
hash of each of them, uncompressed, and checks it after the download and before each start. To check the files yourself:

```
sha256sum -c SHA256SUMS
cosign verify-blob --bundle chroma-linux-x64.gz.sigstore.json --certificate-identity-regexp '^https://github.com/ChromaDotNet/ChromaDB.Local/' --certificate-oidc-issuer https://token.actions.githubusercontent.com chroma-linux-x64.gz
gh attestation verify chroma-linux-x64.gz --repo ChromaDotNet/ChromaDB.Local
```

On Windows and macOS the executables are those of the GitHub release of the Chroma CLI, checked against its digests. On Linux they
are built from its tag, with no change to the code, for glibc 2.28 and later; two builds give the same bytes.
