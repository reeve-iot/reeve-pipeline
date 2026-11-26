# Contributing

## Building and testing

You need the .NET 11 SDK.

```bash
dotnet build Reeve.Pipeline.slnx -c Release
dotnet pack src/Reeve.Pipeline -c Release -o artifacts/nupkg --no-build
dotnet test tests/Reeve.Pipeline.Tests -c Release --no-build
```

Pack before testing: `PackageTests` inspects the produced package and fails if there is none.

For how the library works internally, see [docs/architecture](docs/architecture/README.md).

## Pull requests

Open pull requests against `main`. The CI workflow (`.github/workflows/ci.yml`) builds, packs and tests every
pull request and every push to `main`. Add a CHANGELOG entry for user-visible changes.

Renovate keeps NuGet packages and GitHub Actions versions up to date. .NET major versions are bumped by hand
together with the target framework.

## Releasing

1. Move the changes under a new version heading in `CHANGELOG.md` and set `<Version>` in
   `Directory.Build.props` to the same version.
2. Merge to `main`, then push the tag `vX.Y.Z`.

The Release workflow (`.github/workflows/release.yml`) builds, packs and tests with the version taken from
the tag, publishes the package to nuget.org using trusted publishing, and then creates the GitHub Release
for the tag with that version's `CHANGELOG.md` section as its notes. A stable version is published once; to
correct a bad release, publish the next version.

Publishing needs the `NUGET_USER` Actions variable (the nuget.org account name) and a nuget.org trusted
publishing policy for this repository and `release.yml`.
