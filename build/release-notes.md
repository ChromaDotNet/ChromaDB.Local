Preview of ChromaDotNet.Local VERSION, with Chroma CHROMA_VERSION (the Chroma CLI CLI_TAG). Community project, not affiliated with Chroma.

The packages are on NuGet: [ChromaDotNet.Local](https://www.nuget.org/packages/ChromaDotNet.Local/VERSION), with the executables of
every platform; [ChromaDotNet.Local.Core](https://www.nuget.org/packages/ChromaDotNet.Local.Core/VERSION), without them, which
downloads the one of its platform from this release at the first start; and the ChromaDotNet.Local.Runtime package of each platform.

The files of this release are the executables of the runtime packages, compressed with gzip. ChromaDotNet.Local.Core has the SHA-256
hash of each of them, uncompressed, and checks it after the download and before each start. To check the files yourself:

```
sha256sum -c SHA256SUMS
cosign verify-blob --bundle chroma-linux-x64.gz.sigstore.json --certificate-identity-regexp '^https://github.com/ChromaDotNet/ChromaDB.Local/' --certificate-oidc-issuer https://token.actions.githubusercontent.com chroma-linux-x64.gz
gh attestation verify chroma-linux-x64.gz --repo ChromaDotNet/ChromaDB.Local
```

On Windows and macOS the executables are those of the GitHub release of the Chroma CLI, checked against its digests. On Linux they
are built from its tag, with no change to the code, for glibc 2.28 and later; two builds give the same bytes.
