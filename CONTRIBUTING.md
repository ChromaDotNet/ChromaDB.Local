# Contributing

Issues and pull requests are welcome. To report a vulnerability, follow [SECURITY.md](SECURITY.md) instead.

## Build and test

The scripts and the tests are described in [Build](README.md#build), in the README. The tests use the packages in
`artifacts/packages`, as an application does: the executables and `build/pack.sh` come first.

## Pull requests

- A fix comes with a test that fails without it.
- New functionality comes with tests in the automated test suite, which the CI runs on every pull request.
- The public API is listed in `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`: a new public member goes in
  `PublicAPI.Unshipped.txt`, otherwise the build fails with RS0016.
- Public types and members have XML documentation, which goes in the package.
- The build treats warnings as errors.
- A change to the build of the executables keeps the Linux builds reproducible: two builds of the same tag give the same bytes.

The CI, in [.github/workflows/ci.yml](.github/workflows/ci.yml), runs on Windows, Linux x64 and arm64, and macOS arm64 and x64.
Only the version tags publish the release and the packages.
