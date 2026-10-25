# Security

## Supported versions

Security fixes go into the latest release of each line of packages on NuGet: `ChromaDotNet.Local`, `ChromaDotNet.Local.ChromaDB` and `ChromaDotNet.Local.VecturaDB`. Earlier versions do not get fixes: update to the latest one.

## Reporting a vulnerability

Please do not report a vulnerability in a public issue, discussion or pull request.

Report it privately on GitHub instead: [Report a vulnerability](https://github.com/ChromaDotNet/ChromaDB.Local/security/advisories/new), in the **Security** tab of this repository. Only the maintainers see the report.

Include the version of the packages, the operating system, and the steps to reproduce the problem.

## What happens next

- We acknowledge the report within 7 days.
- We confirm or rule out the vulnerability within 14 days, and keep you informed of the progress.
- A confirmed vulnerability is fixed in a new release. A GitHub security advisory follows the release, with credit to you unless you prefer otherwise.
- Please keep the details private until the advisory is published, and for at most 90 days from the report.

## How the executables and the packages are made

- The CI builds the Chroma executables for Linux from the tag of the Chroma CLI, with no change to the code, and takes those for Windows and macOS from the release of the Chroma CLI, checked against the digests of that release. It builds the guard from `guard/chroma-local-guard.c`.
- The executables of VecturaDB are not built here: ChromaDotNet.Local.VecturaDB downloads them from the GitHub release of [VecturaDB](https://github.com/VecturaDB/VecturaDB). At each pack, the CI checks the hashes of the package against the digests of that release.
- The build pins its images, rustup and protoc to digests.
- `ChromaDotNet.Local.ChromaDB` and `ChromaDotNet.Local.VecturaDB` contain the SHA-256 of each executable of their engine, and `ChromaDotNet.Local` that of the guard: the download at the first start, and a copy into the cache of the user, check it before using the file.
- Only the CI publishes the releases and the packages, from the tags of this repository (`v*`, `chromadb-v*` and `vecturadb-v*`), with [NuGet trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing): no NuGet API key is kept by a person or in the repository.
- The organization requires two-factor authentication.
- The workflows pin their actions to commit hashes. Dependabot and CodeQL check the dependencies and the code.

## Verifying a release

Each GitHub release has the packages of its tag that the CI built, and the releases of `ChromaDotNet.Local.ChromaDB` also have the compressed executables and their `SHA256SUMS`, with:

- their Sigstore signatures (`.sigstore.json`), made by the CI of this repository;
- their build provenance (`provenance-<tag>.intoto.jsonl`), attested by the CI.

nuget.org adds its repository signature (`.signature.p7s`) to the packages it serves, so the signatures and the provenance apply to the files of the release.

To verify a file downloaded from the release, with Cosign 3 or later:

```
cosign verify-blob chromadb-linux-x64.gz --bundle chromadb-linux-x64.gz.sigstore.json --certificate-identity-regexp '^https://github[.]com/ChromaDotNet/ChromaDB[.]Local/[.]github/workflows/ci[.]yml@refs/tags/' --certificate-oidc-issuer https://token.actions.githubusercontent.com
```

With the GitHub CLI, for the tag of the release:

```
gh attestation verify chromadb-linux-x64.gz --bundle provenance-<tag>.intoto.jsonl --repo ChromaDotNet/ChromaDB.Local --signer-workflow ChromaDotNet/ChromaDB.Local/.github/workflows/ci.yml --source-ref refs/tags/<tag>
```
