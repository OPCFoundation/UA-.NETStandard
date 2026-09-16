# Pipeline helpers and contracts

Azure YAML pipelines and templates stay at this directory's root. Supporting
scripts and data are grouped by task and are also used by GitHub Actions, local
commands, and the release-evidence tooling.

| Location | Responsibility |
| --- | --- |
| Root | YAML pipelines/templates and cross-cutting `get-root.ps1`, `get-matrix.ps1`, `set-version.ps1`, `generate-slnx.ps1`, and `validate-migration-plugin.ps1` |
| `assurance/` | Discovery, fuzz-input identity, result and proof collection, NativeAOT and CodeQL helpers, authenticated retrieval/review, and `profiles.json` |
| `containers/` | Container matrix selection and `evidence.ps1` for OCI evidence collection and offline evaluation |
| `nuget/` | `evidence.ps1`, its shared `evidence-functions.ps1`, package validators, `expected-packages.txt`, and Debug/Release signing lists |
| `release/` | `promotion.ps1`, `policy.json`, `artifacts.json`, readiness progress, and all release/verification/readiness/review/delivery JSON schemas |
| `coverage/` | `check.ps1`, the shared coverage gate; its thresholds remain in the repository-root `coverage-thresholds.json` |

`assurance/profiles.json` lists required release jobs under `profiles[].jobs`.
Its `additionalReplayProjects` contains baseline-only replay input definitions,
including PubSub, so selected CI projects receive the same frozen/copy-verified
corpus checks without implicitly expanding the seven-job release profile.

## Paths and local commands

Pipeline command paths are relative to the repository checkout. Helpers in task
folders resolve default repository roots two levels above their own directory;
sibling helpers and data are resolved from `$PSScriptRoot`. Explicit input/output
paths retain each command's documented meaning. Signing-list entries remain
relative to the repository root, not to the `nuget/` directory.

Do not reuse PowerShell automatic variables as local names. In particular,
binding `$input` can make a child `pwsh` wait for standard-input EOF even after
its script body finishes. Use descriptive locals such as `$sourceStream` and
`$replayInput`; fixture tests keep stdin open to detect this CI process-lifetime
regression without extending their deadlines.

From the repository root in PowerShell:

```powershell
.\.azurepipelines\assurance\discovery.ps1

.\.azurepipelines\coverage\check.ps1 `
    -CoberturaPath .\CodeCoverage\Cobertura.xml `
    -BaseRef master -SummaryPath .\coverage-summary.md

.\.azurepipelines\nuget\validate-package-set.ps1 `
    -PackageDirectory .\packages -ManifestPath .\release-manifest.json
```

The NuGet source-generator consumer check is
`.\.azurepipelines\nuget\validate-source-generator-packages.ps1 -PackageDirectory .\packages`.
It reads the adjacent expected-package catalog and writes its validation workspace
under the repository's `artifacts/` directory.

## Contract boundaries

Serialized repository-relative paths use portable `/` separators. Schemas in
`release/` resolve sibling references locally; their versioned `$id` identities
and the v1/v2 document meanings are independent of the directory layout.
Contract snapshots bind exact paths and bytes: a snapshot must match the actual
checkout, not have old paths or digests silently reinterpreted.

The `nuget`, `containers`, and `pump` release groups remain independent.
Directory organization grants no producer trust or publication authority. The
required stable-release contract stays fail-closed; production identities,
approval records, protected tool pins, platform grants, and official promotion
transport remain unconfigured.

See [Release evidence](../docs/ReleaseEvidence.md),
[Container reference server](../docs/ContainerReferenceServer.md), and the
[Developer guide](../docs/DeveloperGuide.md) for behavior and operating requirements.
