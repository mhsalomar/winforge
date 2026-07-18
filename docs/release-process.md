# Release process

1. Run `scripts/build.ps1` and `scripts/test.ps1`.
2. Run `scripts/package.ps1 -Runtime win-x64`; repeat for `win-arm64` on an arm64-capable validation pipeline.
3. Run `scripts/verify-release.ps1`.
4. Sign artifacts through `scripts/sign.ps1` using a certificate outside the repository.
5. Tag `vX.Y.Z`. GitHub Actions rebuilds and publishes checksums and unsigned artifacts when signing secrets are absent.
