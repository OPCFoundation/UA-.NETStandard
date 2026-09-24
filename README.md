# UA-.NETStandard fuzz crash corpus

This orphan branch holds the crash, timeout and slow inputs that earlier fuzzing campaigns found
for the OPC UA .NET Standard fuzz targets. It shares no history with `master` and is never merged.

Each top-level folder is copied over the `fuzzing/<folder>` project of the same name on `master`,
where `FuzzTargetTestsBase` replays `Assets/crash*`, `Assets/timeout*` and `Assets/slow*` through
every fuzz target. The nightly workflow checks out a pinned commit of this branch; see
`fuzzing/CrashCorpus.md` on `master` for how to use and update it.

## Provenance

The initial commit imports the former Azure DevOps secure file `FuzzingArtifacts.zip`
(SHA-256 `85198e46518217241de5dc1eefcb5a4e40b18e62df1a2da1f333c3a39fe1d61b`, 22,293 files,
created April to June 2024) unchanged: 22,267 `crash-*`, 25 `slow-unit-*` and 1 `timeout-*` input.
Inputs are named `<kind>-<sha1 of content>` by libFuzzer; `crash-424b616be64096da284789ddc2c6516391eff98e`
was edited after it was named and is kept byte-for-byte as it was in the archive.

## Security

Only add inputs whose underlying bug is fixed and released. A new, unfixed crash is a potential
vulnerability: report it privately as described in SECURITY.md on `master`, and add it here once the
fix has shipped.