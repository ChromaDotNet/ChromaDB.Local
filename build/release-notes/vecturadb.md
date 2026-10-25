Preview of ChromaDotNet.Local.VecturaDB VERSION, with VecturaDB VECTURADB_VERSION, for ChromaDotNet.Local 1.5.9.1-preview.7. Independent project. Not affiliated with, endorsed by or sponsored by Chroma Inc. CHROMA is a registered trademark of Chroma Inc., used here only to describe compatibility.

[ChromaDotNet.Local.VecturaDB](https://www.nuget.org/packages/ChromaDotNet.Local.VecturaDB/VERSION) starts VecturaDB. VecturaDB is a
vector database compatible with the Chroma API, bringing Chroma Cloud's features to open source: sparse vectors, BM25 and the Search
API. The first start downloads the executable of its platform from the GitHub release
[VECTURADB_TAG](https://github.com/VecturaDB/VecturaDB/releases/tag/VECTURADB_TAG) of VecturaDB, for Windows x64, Linux x64 and arm64,
macOS x64 and arm64, into the cache of the user. The package has the SHA-256 hash of each executable, the digest GitHub gives the
file: it checks it after the download and before each start. The notices of VecturaDB are in THIRD-PARTY-NOTICES.md, in the package.

The package of this release is signed with Sigstore, with its build provenance:

```
cosign verify-blob --bundle ChromaDotNet.Local.VecturaDB.VERSION.nupkg.sigstore.json --certificate-identity-regexp '^https://github[.]com/ChromaDotNet/ChromaDB[.]Local/[.]github/workflows/ci[.]yml@refs/tags/' --certificate-oidc-issuer https://token.actions.githubusercontent.com ChromaDotNet.Local.VecturaDB.VERSION.nupkg
gh attestation verify ChromaDotNet.Local.VecturaDB.VERSION.nupkg --repo ChromaDotNet/ChromaDB.Local --signer-workflow ChromaDotNet/ChromaDB.Local/.github/workflows/ci.yml --source-ref refs/tags/vecturadb-vVERSION
```
