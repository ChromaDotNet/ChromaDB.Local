# Security

## Supported versions

Security fixes go into the latest release of the `ChromaDotNet.Local` packages on NuGet. Earlier versions do not get fixes: update to the latest one.

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
- The build pins its images, rustup and protoc to digests.
- `ChromaDotNet.Local.Core` contains the SHA-256 of each executable: the download at the first start checks it before using the file.
- Only the CI publishes the release and the packages, from the version tags of this repository, with [NuGet trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing): no NuGet API key is kept by a person or in the repository.
- The organization requires two-factor authentication.
- The workflows pin their actions to commit hashes. Dependabot and CodeQL check the dependencies and the code.

## Verifying a release

Each GitHub release has the compressed executables, their `SHA256SUMS`, and the packages that the CI built, with:

- their Sigstore signatures (`.sigstore.json`), made by the CI of this repository;
- their build provenance (`provenance-<tag>.intoto.jsonl`), attested by the CI.

nuget.org adds its repository signature (`.signature.p7s`) to the packages it serves, so the signatures and the provenance apply to the files of the release.

To verify a file downloaded from the release, with Cosign 3 or later:

```
cosign verify-blob chroma-linux-x64.gz --bundle chroma-linux-x64.gz.sigstore.json --certificate-identity-regexp '^https://github.com/ChromaDotNet/ChromaDB.Local/[.]github/workflows/ci[.]yml@refs/tags/v' --certificate-oidc-issuer https://token.actions.githubusercontent.com
```

With the GitHub CLI:

```
gh attestation verify chroma-linux-x64.gz --bundle provenance-v1.5.9.1-preview.4.intoto.jsonl --repo ChromaDotNet/ChromaDB.Local --signer-workflow ChromaDotNet/ChromaDB.Local/.github/workflows/ci.yml --source-ref refs/tags/v1.5.9.1-preview.4
```
