Preview of ChromaDotNet.Local VERSION, the base without an engine. Community project, not affiliated with Chroma.

[ChromaDotNet.Local](https://www.nuget.org/packages/ChromaDotNet.Local/VERSION) starts the server of an engine package as a child
process on 127.0.0.1, for ChromaDotNet.Client, and stops it with Dispose or when the application ends. An application references an
engine, which brings the base:

- [ChromaDotNet.Local.ChromaDB](https://www.nuget.org/packages/ChromaDotNet.Local.ChromaDB), for Chroma;
- [ChromaDotNet.Local.VecturaDB](https://www.nuget.org/packages/ChromaDotNet.Local.VecturaDB), for VecturaDB.

In this preview:

- ChromaDotNet.Local has no engine of its own. An application on ChromaDotNet.Local 1.5.9.1-preview.5 adds ChromaDotNet.Local.ChromaDB,
  and updates ChromaDotNet.Local to 1.5.9.1-preview.6 or removes it: left at 1.5.9.1-preview.5, the restore fails with NU1605. Its
  code stays the same. Without an engine, the build of an application or of a project of tests gives the warning CDNL001, and
  ChromaLocalServer.StartAsync fails with a message that names the two packages, unless ChromaLocalOptions.ExecutablePath names an
  executable.
- With the two engines in one application, ChromaLocalOptions.Engine chooses the one of each server.
- ChromaLocalServer.Engine and ChromaLocalServer.EngineVersion say the engine of the server. ChromaLocalServer.ChromaVersion is
  obsolete.
- The guard of Linux and macOS, which stops the server when the application is killed, is in this package.

The package of this release is signed with Sigstore, with its build provenance:

```
cosign verify-blob --bundle ChromaDotNet.Local.VERSION.nupkg.sigstore.json --certificate-identity-regexp '^https://github[.]com/ChromaDotNet/ChromaDB[.]Local/[.]github/workflows/ci[.]yml@refs/tags/' --certificate-oidc-issuer https://token.actions.githubusercontent.com ChromaDotNet.Local.VERSION.nupkg
gh attestation verify ChromaDotNet.Local.VERSION.nupkg --repo ChromaDotNet/ChromaDB.Local --signer-workflow ChromaDotNet/ChromaDB.Local/.github/workflows/ci.yml --source-ref refs/tags/vVERSION
```
