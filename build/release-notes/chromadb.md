Preview of ChromaDotNet.Local.ChromaDB VERSION, with Chroma CHROMA_VERSION (the Chroma CLI CLI_TAG), for ChromaDotNet.Local
1.5.9.1-preview.7. Independent project. Not affiliated with, endorsed by or sponsored by Chroma Inc. CHROMA is a registered trademark of Chroma Inc., used here only to describe compatibility.

The packages are on NuGet: [ChromaDotNet.Local.ChromaDB](https://www.nuget.org/packages/ChromaDotNet.Local.ChromaDB/VERSION), with the
ChromaDotNet.Local.ChromaDB.Runtime package of each platform, which has its executable. With
`<ChromaDotNetLocalChromaDBDownload>true</ChromaDotNetLocalChromaDBDownload>` in the project, the executables are not in the output,
and the first start downloads the one of its platform from this release.

In this preview, ChromaDotNet.Local.ChromaDB depends on ChromaDotNet.Local 1.5.9.1-preview.7, with ChromaDotNet.Client 2.18.0 or
later. The executables are those of 1.5.9.1-preview.6, named chromadb. The guard of Linux and macOS is in ChromaDotNet.Local.

From 1.5.9.1-preview.5, Chroma is an engine of ChromaDotNet.Local, the base without an engine. ChromaDotNet.Local.ChromaDB takes the
place of ChromaDotNet.Local 1.5.9.1-preview.5, ChromaDotNet.Local.Core and the ChromaDotNet.Local.Runtime packages: an application on
them references ChromaDotNet.Local.ChromaDB in their place, and its code stays the same. A reference to ChromaDotNet.Local
1.5.9.1-preview.5 left next to it fails at the restore with NU1605: update it to 1.5.9.1-preview.7, or remove it.

The files of this release are the executables of the runtime packages, compressed with gzip. ChromaDotNet.Local.ChromaDB has the
SHA-256 hash of each of them, uncompressed, and checks it after the download and before each start. To check the files yourself:

```
sha256sum -c SHA256SUMS
cosign verify-blob --bundle chromadb-linux-x64.gz.sigstore.json --certificate-identity-regexp '^https://github[.]com/ChromaDotNet/ChromaDB[.]Local/[.]github/workflows/ci[.]yml@refs/tags/' --certificate-oidc-issuer https://token.actions.githubusercontent.com chromadb-linux-x64.gz
gh attestation verify chromadb-linux-x64.gz --repo ChromaDotNet/ChromaDB.Local --signer-workflow ChromaDotNet/ChromaDB.Local/.github/workflows/ci.yml --source-ref refs/tags/chromadb-vVERSION
```

On Windows and macOS the executables are those of the GitHub release of the Chroma CLI, checked against its digests. On Linux they
are built from its tag, with no change to the code, for glibc 2.28 and later; two builds give the same bytes.
