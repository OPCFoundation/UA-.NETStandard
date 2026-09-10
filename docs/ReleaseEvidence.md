# Release evidence contract

## Configured release workflow

This guide describes the configured release process for current `master`/major 2.
It is for release maintainers running releases and engineers maintaining the
build, evidence and verification adapters. Administrator setup and qualification
are documented separately in the
[administrator setup checklist](../plans/ReleaseEvidenceAdministration.md).

The **active contract** uses `currentMajor: 2`, `stage: required` and
`requiredChannel: stable`. Stable publication proceeds only when the protected
controller verifies every applicable control. Preview and development are release
channels with advisory applicability for these evidence controls, not maturity
stages of the contract. Existing signing, build, test and security failures remain
blocking in every channel.

| Step | What happens for each release |
| --- | --- |
| Select the candidate | The release maintainer selects the producer run and attempt, source revision, artifact group and version. The release authority supplies signed intent bound to that exact candidate. |
| Produce the evidence | Build jobs create and sign the artifacts, capture their final build inputs, and generate inventories, provenance and immutable evidence indexes. Raw diagnostics remain in restricted storage. |
| Complete assurance | Test and analysis jobs provide same-source execution proofs. Security reviewers issue authenticated dispositions for the complete CodeQL finding population. |
| Verify and publish | The protected controller checks the current policy, trust checkpoint, signatures, complete group membership and assurance. The isolated publication job rechecks authorization before writing the exact verified bytes. |
| Confirm delivery | The publication job reads back delivered content and writes immutable receipts. Release maintainers review partial failures and retain the reviewed public evidence and restricted audit records. |

The [Security Stewardship annex](SecurityStewardship.md) covers the Foundation's
OSS steward policy, cooperation and reporting processes. These release gates are
engineering controls, not a legal approval. Confidential findings follow
[SECURITY.md](../SECURITY.md).

## Canonical files and release lifecycle

The following source files define the contract. They are not rewritten with a
new package version for every release.

| File under `.azurepipelines` | Use in each release | When it changes |
| --- | --- | --- |
| `release/policy.json` | The protected controller selects the current approved revision and enforces its scope, channel rules, trust requirements and public projection. Evidence records its exact-byte digest. | A reviewed change alters release scope, enforcement, verification requirements or public-data rules. Administrators refresh the corresponding protected policy checkpoint and approval records. |
| `release/evidence.schema.json` | Producers and readers validate the closed v2 envelope against the same supported wire contract. It defines fields and types, not the candidate's version or results. | The wire format changes. Schema, readers, producers and compatibility fixtures are updated together; supported older records retain their defined meaning. It is unchanged for an ordinary release. |
| `release/artifacts.json` | The controller expands the selected group's expected packages, variants, images and platforms before reconciling produced output. The frozen catalog is included in the evidence inputs. | A producer, artifact group, package-variant rule, image, registry mapping or supported platform changes. The catalog and affected build/publish definitions are reviewed together. |
| `assurance/profiles.json` | Assurance collection expands the seven required jobs, framework scope, result criteria and permitted N/A rules. `additionalReplayProjects` supplies baseline-only corpus definitions without enrolling extra release jobs. | Required assurance scope, target/corpus definitions or result criteria change. Producer wiring and fixtures change in the same review; release runs generate new proofs, not edited profile definitions. |
| `nuget/expected-packages.txt` | NuGet packing and verification use its unique modern package IDs to derive the complete Release set and applicable Debug/symbol variants. Its exact bytes are frozen with the catalog. | A shipped modern package is added, removed or renamed. The list and evaluated project/pack metadata are updated together. Versions and successful build outputs do not replace membership in this list. |

The [pipeline helper layout](../.azurepipelines/README.md) groups scripts and
contracts by task; shared release, verification, readiness, review and delivery
schemas live together in `release/`.

Release maintainers review coordinated contract and catalog revisions; security
reviewers check verification and public-data rules, and publication administrators
check changes to authority and destinations. At release time the controller uses
the approved revisions rather than modifying them to match observed outputs.

Configuration `schemaVersion` identifies the configuration format; its `version`
identifies the contract revision, not the released package version. The evidence
envelope independently uses `schemaVersion: 2`. Capture freezes and hashes the
policy, catalog and profiles before building their referencing indexes; no file
contains its own hash. Release-specific versions, subjects, results, intent and
delivery observations belong in generated evidence and signed records.

Source-data paths use portable, root-relative `/` separators. The tooling resolves
source paths against the checkout and evidence paths against the bundle root. It
rejects rooted paths, traversal, empty segments, symlink/reparse-point escapes and
network retrieval through a path. Schema validation and containment checks are
separate. A schema `$id` identifies a format; it is not a schema-download endpoint.

## Groups and artifact membership

A release group is the complete set verified and promoted together, not an
artifact-format classification:

| Group ID | Artifact family | Contents |
| --- | --- | --- |
| `nuget` | NuGet packages | Modern Release and applicable Debug packages, metapackages and corresponding symbol packages |
| `containers` | Container images | Nine sample/tool images under `ghcr.io/opcfoundation/uanetstandard`, each with amd64 and arm64 runnable manifests |
| `pump` | Container images | The Pump sample at `ghcr.io/opcfoundation/pumpdeviceintegrationserver`, with an amd64 runnable manifest |

**Pump is a container image.** Its separate group preserves its owner-level
registry path, single-platform scope and independent promotion. Both container
groups use the same Docker workflow; `pump` is not a separate artifact technology.

For the group selected by the release maintainer, the controller expands expected
membership from the approved catalog before reconciling produced files. Missing
output is a failed completeness check, not a smaller expected group. The protected
current policy also checks the candidate's catalog revision, so an older or
caller-supplied catalog cannot remove required subjects. Groups are promoted
independently; there is no cross-group atomic release.

### NuGet

The existing modern catalog currently contains **75** unique IDs. This count is
an observation, not a second membership list. Parse nonblank, noncomment trimmed
lines; duplicates are an error. Resolve project-to-ID/variant mappings through
the evaluated packable projects and imported pack targets at the source SHA.

* **Release:** every catalog ID, identified from the embedded nuspec.
* **Debug:** only catalog-backed projects whose evaluated Debug package ID ends
  in `.Debug`. Remove that suffix for catalog lookup. Do **not** append `.Debug`
  to all 75 IDs. The current publisher's *Keep only distinct Debug package IDs*
  step removes unsuffixed Debug archives and their matching `.snupkg` files.
  Evaluate expected mappings before this filtering; observing a subset cannot
  establish completeness. Require at least one distinct Debug package.
* **Metapackages:** the publisher packs `nuget/Opc.*.nuspec` only in its Release
  job, currently `Opc.Ua.nuspec` and `Opc.Ua.Symbols.nuspec`. Read metadata IDs
  from these files and use the actual pack version. They contain metadata and
  ancillary content with consumer dependency declarations, not copies of all
  dependencies. The Symbols-named metapackage is a normal `.nupkg`, not a
  `.snupkg`; its mixed ordinary/Debug dependencies remain as declared.
* **Symbols:** `common.props` defaults to `IncludeSymbols=true` and
  `SymbolPackageFormat=snupkg`; generator packaging and MigrationAnalyzer have
  explicit overrides. Evaluate these and actual eligible symbol payloads.
  Require every applicable retained package's symbols and reject orphaned or
  duplicate pairs. Absence needs evaluated non-applicability, not a guessed
  universal symbol count. The configured signing path verifies each artifact kind
  separately: `.azurepipelines/nuget/validate-package-set.ps1` checks `.nupkg`
  signatures, and the qualified symbol-verification path supplies independent
  `.snupkg` proof. Ordinary-package verification never counts as symbol verification.

Inventories must reconcile the final `project.assets.json` after the last
restore (including implicit build restore), evaluated mappings, embedded nuspec
and actual signed contents. Keep TFM/RID/Roslyn scopes distinct. Include private
bundled generator dependencies and nonpackable contributors when shipped;
`PrivateAssets` alone does not mean build-only. Distinguish shipped components,
declared consumer ranges, build-only dependencies and external prerequisites.
Unknown component ownership or licence semantics remain visible gaps.
Use CycloneDX JSON 1.6 for this inventory, not assembly versions as guessed
NuGet identities.

### Containers

The shared Docker workflow's `containers` group publishes nine image repositories under
`ghcr.io/opcfoundation/uanetstandard`: `refserver`, `ldsserver`, `boilerserver`,
`calcserver`, `mcpserver`, `redundantserver`, `redundantclient`,
`redundantpubsub`, `pubsubclient`. Each declares `linux/amd64` and
`linux/arm64/v8`. Its repository transformation lowercases `owner/repository`
and removes `-` and `.`.

The same workflow preserves the independent `pump` group:
`ghcr.io/opcfoundation/pumpdeviceintegrationserver`,
`linux/amd64` only. Its transformation lowercases the owner and image, without
the main group's repository segment. The catalog preserves both identities.
Fork-derived names are not automatically authorized official destinations.

The catalog-driven selector builds both groups on master pushes and pull requests
(build-only for pull requests), only `containers` on release/docker branch pushes,
and only `pump` on manual dispatch. Status artifacts and membership manifests
are aggregated separately for each selected group; sharing a workflow does not
combine their release eligibility or require an unrelated group's members.

These are **19 runnable platform subjects**. Inspect actual OCI media types:
indexes, runnable manifests and attestation descriptors are different objects.
An attestation descriptor is not a platform; even Pump may have a wrapping
index. Verify every runnable subject, its native BuildKit SPDX inventory and
provenance, and final index-to-child digest relationships. Record the actual
supported document versions; no scanner/signing version is approved merely
by appearing in research. Only the ten configured images are enrolled.
Their NuGet relationship is **same-source**, not built-from-published-package:
the Dockerfiles build project references.

## Channel and enforcement semantics

Derive the version from embedded package metadata or verified image version
inputs/output linkage, never solely a mutable tag. Official stable means
major 2, no prerelease component and a protected authorized **stable intent**
bound to repository, source SHA, group, exact version and artifact-set digest.
An official prerelease with matching protected preview intent is `preview`.
Without official intent, a development build is `development`, including a
locally produced numerically stable-looking version.

Contradictory or unverifiable claimed official intent is `RELEASE_INTENT` unmet;
it must not silently become development to evade enforcement. A dispatch field,
NBGV `PublicRelease`, branch/tag name, `latest`, or Debug/Release configuration
alone cannot authorize stable publication. Actual version, intent and requested
official destination must agree. Invalid/unknown identity is incomplete.

| Policy and release | New controls unmet | Existing baseline failure |
| --- | --- | --- |
| Required, stable major 2 | Block before official promotion | Still fails |
| Required, preview/development channels | Report `incomplete`; these controls remain advisory | Still fails |

`assessment.status=complete` means all applicable contract controls were
independently verified, **not** that publication occurred or a legal/security
certification was obtained. Advisory channel applicability does not mask baseline
signing, build or test failures. Missing trust, producer qualification or
publication-boundary records leave the assessment incomplete even when an
individual producer ran, and block in-scope stable publication.

## V2 wire contract

Write **`release-evidence.json`** beside, never over,
**`release-manifest.json`**. The envelope has exactly these top-level fields:

| Field | Required semantics |
| --- | --- |
| `schemaVersion` | Integer `2` |
| `source` | `repository`, `actualSha`, `actualRef`, `trackedClean`; optional distinct event/PR-head/merge SHAs |
| `producer` | `system`, `workflow`, `definitionSha`, `runId`, `attempt`, `job`, observed `tools`; optional real run times |
| `release` | `group`, actual `version`, derived `channel` |
| `policy` | `id`, `version`, exact-byte SHA-256 `digest`, `stage` |
| `artifacts` | Kind, ID, version, configuration, digest and actual `scopes`; optional byte size |
| `documents` | `type`, `format`, `version`, bundle-relative `path`, digest and exact `subject` |
| `assurance` | Profile IDs, status counts, job records and input identities |
| `assessment` | `status` (`complete`/`incomplete`) and `unmetControls` codes |

The assessment object keeps its original two-field v2 shape. Pending versus
observed findings are recorded in the separate
[`producer-assessment` v1 companion](../.azurepipelines/release/producer-assessment.schema.json),
referenced as a `producer-record` document. Its source, producer, release,
artifacts and complete control partition must match the envelope. The document's
exact bytes are covered by the producer's authenticated index/record.
Classifications in an unrelated scope cannot clear findings; legacy envelopes
without this companion keep their unclassified findings. An observed failure
cannot be relabelled pending or removed by subsequent verification.

All digests are `sha256:` plus 64 lowercase hex characters. Git SHAs are
separately typed full lowercase object IDs, not file digests. `source.actualSha`
is the real checked-out build commit; `producer.definitionSha` is the workflow
definition revision, which may differ. A protected promoter building evidence
for older artifacts must not replace their source SHA with its controller SHA.
`trackedClean` concerns tracked sources only; generated/restored build inputs
still require explicit identities. An unobserved identity cannot be filled
with a zero SHA or event default. If mandatory identity is unavailable, report
the error outside the envelope rather than manufacturing schema-valid evidence.

`producer.system` is `github-actions` or `azure-pipelines`. Verify run ID,
attempt, job, checkout and definition against authenticated CI records and
approved immutable producer records; YAML names and self-asserted JSON are
not authentication. Azure template paths also require the resolved outer
definition/template chain in a producer record. The protected trust snapshot
enrolls the approved producers and their immutable definition/tool identities;
an observed tool version alone is not an approval pin.

Artifact kinds: `nuget-package`, `nuget-symbols`, `oci-index`, `oci-manifest`.
NuGet `id` is the embedded ID; OCI `id` is the full registry repository without
tag/digest. Configuration is `Release`, `Debug` or `not-applicable`; Release-job
metapackages remain Release even if their dependencies are Debug. OCI build
configuration must be observed, not inferred from a tag. `scopes` always has
`tfms`, `rids`, `roslyn`, `platforms` arrays; use empty arrays only for axes that
are genuinely inapplicable, not unknown. A NuGet metadata-only package has no
invented library TFM. A runnable OCI manifest has exactly its actual platform.
Duplicate artifact identity/scope records are rejected by semantic validation.

Documents use the closed type/format enums in the schema. Format versions
describe the document, not the evidence schema. SBOM subjects must match exact
artifact digests; do not attach one package's BOM to every package. An
`artifact-set` subject hashes a separate immutable membership payload (the v1
manifest for NuGet), never this envelope. A `source` subject uses a hashed
source-input manifest and identifies the repository/commit, not a Git SHA
mislabelled SHA-256. Each referenced path/digest must resolve; reject missing,
altered or conflicting documents and wrong subjects. Multiple subjects can
reference the same document only if that document really covers each subject.

Freeze documents first, then this hash-linked envelope/index, then externally
sign/attest its exact bytes. Do not embed the envelope's own hash, hash a file
that will subsequently be rewritten, or insert sidecars into signed packages.
Schema validation alone does not prove contents, signatures, counts or trust.
The schema deliberately uses simple closed records, no overlapping `oneOf`
branches or polymorphic arbitrary dictionaries; source-generated JSON readers
can use ordinary typed records. Optional fields are omitted, not set to null.

## Assurance accounting and freshness

All three groups use the profiles `security-net10`,
`fuzz-replay-net10`, `codeql-net10`, `aot-net10`. This is a Windows x64 .NET 10
security-focused profile, **not full suite, all-TFM, all-OS, container
runtime or coverage assurance**. Windows native AOT execution says nothing
about Linux ARM runtime testing. Existing broader jobs remain baseline work.

Expand the seven expected jobs from profiles before filtering:
`core-security`, `certificates-security`, `fuzz-encoders`, `fuzz-certificates`,
`fuzz-network`, `codeql-csharp`, `native-aot`. Their envelope IDs equal the
profile job IDs with `shard: all`. Approved sharding changes require
unique IDs and an explicit expected shard inventory; callers cannot reduce
expectations through a PR filter. Profile digests refer to the whole frozen
`.azurepipelines/assurance/profiles.json`, with selected IDs recorded separately.

`assurance` counts are integers over unique expected job records:

* `expected = completed + failed + missing + notApplicable = jobs.length`.
* `selected` counts `selected: true`, regardless of terminal outcome.
* `completed` means verified successful execution meeting profile criteria;
  it **excludes** failures. `failed` is observed failure, not a skip.
* `missing` includes unselected, cancelled, skipped, partial, unreported or
  unauthenticated applicable results. Success of a workflow summary is not
  evidence that all its jobs ran.
* `notApplicable` requires a profile-defined rule and verified scope.
  No applicable required net10 job can be N/A.

Each completed/failed job needs matching source SHA, independently verified
producer/run attempt, referenced sanitized `resultDocument`, and actual counts
for its kind. For test jobs require `total`, `executed`, `passed`, `failed`,
`skipped`; reconcile `executed = passed + failed` and
`total = executed + skipped`. Zero executed tests fail an executed applicable
suite. Missing jobs may omit unknown producer/counts; do not invent zero tests
or successful outcomes to populate a record. Source SHA must match the release
source even when assurance is collected from an independent workflow.

Fuzz replay additionally requires expected/executed target and input counts,
coverage mappings in reviewed input/result documents, every applicable target
with good-seed execution, and all frozen known regressions replayed. Aggregate
counts alone cannot prove mapping coverage. Preserve source corpus bucket and
relative path through copying to detect collisions. A legitimately empty crash,
timeout or slow-regression set needs a verified frozen zero inventory; it does
not exempt good seeds or make the whole job N/A. Only runner skips conclusively
mapped to that empty set are allowed by the fuzz profile. A missing seed
directory, an omitted known input or an unverified private-input fetch is a gap.

Network replay supports library TFMs `net8.0`, `net9.0`, `net10.0`;
`network-unsupported-tfm` may classify other requested library targets only.
Its net48/legacy shell is not a passing network test. AOT is explicitly
`net10.0`, `win-x64` in this profile; `aot-unsupported-tfm` cannot excuse
its required native .NET 10 run. Record **host TFM separately from actual library
TFM**: a future net8 host/netstandard2.1 library profile is not interchangeable
with this net10 profile and does not enable unsupported Network behavior.

CodeQL is analysis, not a test suite: omit test counts, require authenticated
completion, actual extraction-to-project/source reconciliation and `analyzedProjects`,
`findings`, `unresolvedFindings`. Require at least one analyzed project and
zero unresolved findings after the existing confidential review process;
zero findings without execution/coverage evidence is not success. Record actual
query/configuration identities. AOT requires the published native binary to
execute with real results; managed tests or publish-only success are insufficient.

The native proof binds a fresh Windows x64/net10 publish, compiler/SDK identity,
NativeAOT image-header inspection, launch nonce/process identity, matching
produced/launched/observed/post-run image digests and completed structured test
results. The opt-in runtime report records dynamic-code support/compilation as
false. A PE header or missing CLR header alone is not evidence of NativeAOT.
Other platform runs do not inherit the Windows proof classification.

Analysis proofs bind the database, extraction, configuration, suite, packs,
query population/results, source/run/attempt and SARIF upload identity.
Each expected project's source population must match actual extraction, and all
expected queries must complete. Nonzero findings require a separately
authenticated `codeql-disposition` record covering the exact occurrence/alert
population; a producer's `unresolvedFindings: 0` cannot replace it.
Raw extraction, queries, findings and operational review records stay restricted.

`inputIdentities` contains unique IDs, kinds, bundle paths and exact-byte hashes
of reviewed manifests. Job `inputIds` resolve into it. Include the policy,
catalog (including `expected-packages.txt`), profiles, final graphs, target
inventory and sanitized corpus/analysis configuration as applicable. Missing
inputs cannot be excused by an empty array. Keep restricted raw identities and
their detailed mapping in controlled records, not this public envelope.

Freshness is primarily identity, not an arbitrary age limit: verify actual
source, all input digests, full artifact group, run/attempt, approved definition,
current protected policy and nonrevoked signatures at promotion. Reruns create
new immutable attempt records; never combine successful pieces from different
attempts as one successful job. Reuse independent assurance only when all
bindings still match and current policy permits it. Older source revisions
cannot bring a weaker historical policy to the promoter.

## Public projection and distribution receipts

The schema's declared fields are the **public field whitelist version 1**;
unknown properties are rejected, not silently copied. This is a field whitelist,
not automatic approval of arbitrary string values or nested documents.
Only the policy's document types may be published, after transitive payload
review. Publish sanitized summaries, inventories and appropriate provenance,
not raw test logs, SARIF findings, crash inputs or confidential case details.
Do not expose private input names, paths/digests, operational principals,
credentials or unsanitized provenance/build arguments. Input references hash
reviewed sanitized manifests, not restricted raw inputs. A safe outer envelope
cannot make an unsafe referenced payload safe. Secrets are never build arguments.

Access, archive/retrieval ownership and restricted audit records remain under
Foundation-approved controls, without an inferred fixed retention term.
Repository files do not prove those controls operate.

The separate [readiness progress template](../.azurepipelines/release/readiness-progress.json)
references the policy's required engineering check IDs alongside the separate
stewardship records. `validate-readiness` checks structure and scope; it cannot
authenticate an approval, establish production setup or relax required stable
gates. See the
[administrator/Foundation operating prerequisites](SecurityStewardship.md#administrator-and-foundation-operating-prerequisites)
for the controlled records used by the process. Initial provisioning, publisher
isolation and qualification steps are listed in the
[administrator setup checklist](../plans/ReleaseEvidenceAdministration.md).
Submitted record shapes are not authorization.

Publication receipts are **later, separate records**, not extra fields
or mutable updates in this envelope. Bind a receipt to the immutable evidence
digest, destination, delivered subject/digest and actual completion/verification
state. Failure/partial publication stays visible; it is not an atomic multi-group
transaction. A receipt is not build provenance.

For NuGet, distinguish author-signed build archive identity from feed-delivered
repository-signed identity: repository signing can change the whole-file hash.
Verify delivered package ID/version, preserved payload and applicable signatures,
then record its delivered digest separately. Do not demand whole-archive byte
equality with the author archive or reattribute transformed bytes to the build.
Same ID/version, an accepted upload, or `--skip-duplicate` alone does not establish
payload equality.

The content comparison command operates on local archives:

```powershell
dotnet $evidenceTool verify-delivery --author .\author-package.nupkg `
  --delivered .\downloaded-package.nupkg --output .\content-comparison.json
```

It compares canonical per-entry contents excluding `.signature.p7s`, and checks
preservation of the primary CMS signed content/signer while allowing the
repository countersignature's unsigned attributes to change. A `content-matched`
result is **not authenticated delivery**: `signatureVerificationPerformed` remains
false and `SIGNATURE_VERIFIED` remains unmet. Both the author and delivered archive
still need independently authenticated verification under approved signer
constraints. Unsupported `.snupkg` inputs return exit 2; verification of ordinary
packages must never be credited to symbol packages. The separately versioned
[content comparison schema](../.azurepipelines/release/nuget-delivery-content.schema.json)
records this limited result without changing the archive or build provenance.
Signatures/attestations supplement, not replace, existing signing checks.

`verify-delivery-approved` additionally authenticates source/index and signature
records under the independent policy, matches the **primary** CMS certificate
fingerprint, and runs the pinned NuGet verifier on both archives. It emits the
separate [delivery-verification record](../.azurepipelines/release/nuget-delivery-verification.schema.json):

```powershell
dotnet $evidenceTool verify-delivery-approved --repository-root . `
  --evidence .\candidate\evidence\release-evidence.json `
  --verification-bundle .\candidate\verification-bundle.json `
  --trust-policy $independentlyProvisionedTrustPolicy `
  --author .\candidate\package.nupkg --delivered .\retrieved\package.nupkg `
  --output .\private-delivery-verification.json
```

`artifact-delivery-verified` establishes the scoped content/signature relationship,
**not release eligibility or CRA readiness**. Missing enrollment remains
incomplete; observed content/signature failure remains blocking. The underlying
content-only report still says it did not verify signatures. Unsupported symbols
retain their own unresolved status and are not credited by ordinary-package
verification.

## Workflow adapters

### NuGet production and promotion

`nuget-publish.yml` builds the nonpackable evidence tool before the final package
restore/build. After signing and baseline package verification,
`.azurepipelines/nuget/evidence.ps1` captures the final inputs and creates configuration-local
sidecars. Raw assets graphs, capture requests and diagnostics stay on the runner;
only the selected public sidecars accompany the signed packages.

The adapter's operations are `PrepareTool`, `Capture`, `Sidecars`, `Aggregate`,
`Assurance`, `Preflight`, `Receipt`, `VerifyFeed` and `Attach`. Release and Debug
sidecars are reconciled against the existing modern catalog, evaluated variant
mappings and hand-authored metapackages. The existing v1 archive manifest is
unchanged. Producer context, configuration provenance and the v2 index are
separate files; the workflow retains attestation bundles with their digests.
`verification-bundle.json.nativeNuget` refers directly to the original Release
and Debug SLSA v1 bundles and the native index bundle. Their signatures are never
relabelled as signatures over a different release-record format. The independent
snapshot must enroll the referenced `nuget-publisher` authority for
`native-nuget-index` and `native-nuget-pack`; the identifier in the candidate
bundle is only a lookup key, not an approval.

The reader verifies exact package sets, configuration/version, clean source,
workflow definition, run/attempt invocation, builder and source-material bindings.
The signed index binds the package-specific inventories and BOMs transitively.
Configuration-wide SLSA subjects do not claim to be per-package SBOM attestations.

`release.yml` runs from the current master controller and obtains the selected
producer run/attempt from the GitHub API. Preflight checks those observations
against the manifest and expected context. Contradictory source/run expectations
are blocking in every channel. A missing collector produces an explicit incomplete
report that blocks required stable publication; preview/development channels retain
their advisory applicability for the new controls. This does not replace baseline
signature/hash checks.

Assurance refresh writes `assurance/assurance.json`, its metadata receipt, and
per-job proof files. Preflight constructs a runner-local evaluation copy with the
current assurance component; it never rewrites the attested producer envelope.
The copy retains every original document and includes the original index as a
hash-linked v2 `producer-record`. The native index proof's `indexPath` identifies
those original bytes. Source, producer, artifacts and observed findings cannot
change, while separately authenticated later assurance may be assessed.
`inputEvidenceDigest` and `producerEvidenceDigest` distinguish the new assessment
input from the immutable producer index. An original observed failure is still
blocking. The copy binds the current profile and each proof's exact bytes. Missing or
unauthenticated jobs remain missing. An accepted push or `--skip-duplicate`
outcome is recorded as `push-accepted-or-duplicate`, not proof of delivery or
equality. The read-only NuGet.org adapter records delivered identity separately from the
author archive; content matching and approved-signature verification are distinct
states. Receipt creation writes an immutable snapshot. Subsequent observations
are separate create-only files in `<receipt>.events`; content-comparison records
are retained in `<receipt>.evidence`. Neither changes the producer index, v1
manifest or signed archives. Resume reads actual delivery state rather than
equating a saved push outcome with successful delivery.

Public evidence attachments require an already existing, non-draft release whose
tag resolves to the producing commit. This adapter does not create releases for
rolling builds or overwrite existing attachments. Registry access and actual
publication/retrieval validation are not performed by local fixture tests.

### Assurance collection

The helpers in `.azurepipelines/assurance` (`discovery.ps1`, `fuzz-inputs.ps1`,
`results.ps1` and `write-job.ps1`) record selected scope,
public corpus identity and actual result counts. `collect.ps1` creates
the assurance component; each completed job keeps its own `<job-id>.proof.json`.
For fuzz replay, the proof binds observed target/input pairs and any permitted
empty-regression skips. Unrelated skipped tests remain invalid.

`.azurepipelines/assurance/get-release.ps1` uses authenticated, read-only GitHub API responses to
select same-source workflow attempts and download bounded, explicitly named
sanitized artifacts. `ExpectedSourceRef` supports master and current `release/2.*`
branches; the policy floor still comes from protected master. It validates
source, event, run/attempt, artifact digest and scope rather than falling back
to an older successful run. A matching API-recorded workflow reference binds
definition identity; for same-repository push events, the adapter additionally
retrieves the workflow file at the event commit, as described by
[GitHub's workflow-trigger semantics](https://docs.github.com/en/actions/concepts/workflows-and-actions/workflows#workflow-triggers).
It does not apply this event-specific rule to arbitrary dispatch or reusable
definitions. Unproven or contradictory definitions remain incomplete. Its
offline-metadata option cannot grant credit and is rejected on CI.

```powershell
.\.azurepipelines\assurance\get-release.ps1 `
    -ExpectedSourceSha $sourceSha `
    -ExpectedSourceRef 'refs/heads/release/2.0.0' `
    -OutputPath .\assurance\assurance.json `
    -WorkDirectory $runnerLocalWork
```

The caller must obtain `$sourceSha` from the actual selected producer, not a
version label. This command is a collector, not a release authorization. It
records missing evidence when workflows race publication or definition identity
cannot be independently established. CodeQL scope/disposition and native AOT
identity still require the additional verification described above; process
success or plausible counters alone do not clear them.

### Container production

The shared `docker-image.yml` workflow emits native BuildKit SBOM/provenance and runs
`.azurepipelines/containers/evidence.ps1` for `Preflight`, `Record`, `Collect`, `VerifyLocal`,
`Sign`, `Status` and `Aggregate`. Its `containers` and `pump` groups retain their
separate registry identities and membership manifests. Runnable platform manifests
are counted independently from OCI indexes and attestation descriptors.

The scanner image and cosign installer are pinned; tool versions still require
producer approval and compatibility evidence. Native SPDX documents are retained
without conversion. The workflows use minimum-detail BuildKit provenance to limit
public build-argument exposure; richer provenance requires review of its inputs.
Collection reads back digest-addressed blobs. Local reconciliation checks their
relationships, and the signing adapter records root/platform signature operations
without claiming an administrator-verified publisher boundary.

Registry identities and tag formats remain group-specific. In the configured
process, the producer writes candidate content and the isolated publication path
controls official destinations. Required-stable verification completes before
official promotion. Preview/development channels retain advisory evidence
applicability without granting producers a route around that boundary. Separate
`oci-assemble` and authenticated `evaluate` commands support complete group
assessments; they do not change tags. Publication setup and recovery requirements
are described below. See
[Container Reference Server](ContainerReferenceServer.md) for image-specific
output and verification details.

### Release checks and operating records

Stable publication requires production trust, authenticated verification and
isolated publication authority. Foundation stewardship approvals and operations
are separate controlled records, not certifications issued by the evaluator.

| Area | Processing | Evidence checked for each release |
| --- | --- | --- |
| NuGet inventory | Producers reconcile frozen inputs, signed payloads, variants and per-package sidecars; the verifier checks them. | Actual package and applicable symbol signatures, complete component ownership/metadata, and separate delivery observations. |
| Container evidence | The OCI adapter traverses complete content/referrer graphs and evaluates each group independently. | Source-bound image proofs, approved producer/scanner/signer identities and all runnable platforms. |
| Assurance | CI jobs emit execution proofs; security reviewers authenticate finding dispositions; the controller checks complete scope. | Matching source/run/attempt, native execution, full extraction/query coverage and the reviewed finding population. |
| Trusted acceptance | The controller authenticates records against its independently provisioned checkpoint and pinned verifiers. | Current policy, definition/tool identities, unexpired authorities and revocations. |
| Publication boundary | The writer rechecks the request-bound grant and performs conditional, read-back-verified transfers. | Approved principal/ref restrictions, complete destinations/aliases, valid lease, durable journal and current boundary qualification. |
| Steward operations | The Foundation's separate process maintains policy, functional assignments, cooperation and reporting records. | The relevant approved operational records; release evaluation does not issue a legal certification. |

Initial establishment and requalification of these controls are covered by the
[administrator setup checklist](../plans/ReleaseEvidenceAdministration.md).
`publisherBoundaryVerified` summarizes verified records; it is never a substitute
for the request-bound publication authorization.

### Authenticated evaluation and independent bootstrap

The separately versioned [verification bundle](../.azurepipelines/release/verification-bundle.schema.json),
[verification record](../.azurepipelines/release/verification-record.schema.json) and
[trusted snapshot](../.azurepipelines/release/trusted-policy-snapshot.schema.json)
describe authentication inputs, not self-authorizing receipts. Records bind
exact evidence, artifact-set, policy and intent digests; source/definition/run/
attempt; complete artifact/document/job scope; validity interval and checkpoint.
Record kinds distinguish intent, producer, signatures, assurance, public review,
producer qualification and publication boundary. The independently signed
[`codeql-review` record](../.azurepipelines/release/codeql-review.schema.json) is a distinct
companion referenced through `verification-bundle.json.codeqlReviews`. It binds
repository/source/ref, analysis producer/run/attempt, query and finding-population
digests, reviewed occurrences/alerts, policy checkpoint and validity interval.
It is created **before** the final result summary/index and does not contain their
digests. The final summary references its exact-byte digest, avoiding circular
evidence dependencies.

```powershell
dotnet $evidenceTool evaluate --repository-root . `
  --evidence .\candidate\evidence\release-evidence.json `
  --expected .\controller\expected-release.json `
  --artifacts-root .\candidate `
  --verification-bundle .\candidate\verification-bundle.json `
  --trust-policy $independentlyProvisionedTrustPolicy --output .\private-assessment.json
```

`--expect` is an alias for `--expected`. The controller must independently obtain
expectations; copying candidate assertions into that file establishes no trust.
The protected bootstrap file, verifier executables and trusted-root material
must be outside candidate-controlled locations. Its exact-byte digest is pinned
through controller-provisioned `OPCUA_RELEASE_TRUST_POLICY_SHA256`, never calculated
from a candidate-supplied bootstrap to make it appear trusted.
Administrators provision these values through the protected controller, as
described in the setup checklist. Bootstrap files containing local paths or
protected identities stay outside public release attachments.

Production record verification requires an approved, independently pinned GitHub
attestation verifier, saved bundles and custom trusted roots. Verification checks
issuer, certificate/workflow/ref/definition identity and the exact signed record
subject/predicate. Artifact signatures are checked separately against actual
archive or OCI bytes. Reimporting a bundle verifies it again; saved command output,
`verified: true`, a digest supplied alongside its unsigned file, and a successful
collector API call are not authorization.

`verify-codeql-review` reauthenticates the independent record and emits a
sanitized projection only on success. `.azurepipelines/assurance/get-release.ps1` can construct
the protected callback with `-ReviewVerificationBundle` and `-TrustPolicy`.
The callback invokes that verifier for each requested ID/digest; it never accepts
a saved projection as proof. Partial populations, different queries/attempts,
revoked/expired approvals and unresolved findings do not complete the job.
Without configured review authority, nonzero findings remain missing rather
than automatically receiving `unresolvedFindings: 0`.

Only cryptographic verification plus semantic binding produces internal verified
claims. Local schema, membership, integrity, inventory and input checks remain
mandatory. A later authenticated claim may resolve its pending verification
requirement, but cannot erase observed failures or unexplained legacy findings.
The new assessment is separate from immutable producer evidence.
Public attachments contain reviewed native producer bundles, not the protected
policy snapshot, organizational approvals, publication-boundary records or
finding-review records.

Exit **0** means complete, or explicitly advisory incomplete for preview/development
release channels; inspect the assessment. Exit **1** means a baseline failure or
unmet current-major required-stable controls. Exit **2** means malformed or
unsupported input or an unusable verification contract. A stable official
candidate cannot escape enforcement by labelling both supplied contexts
`development`. Independent groups never borrow missing members from each other.

### Publication and recovery

The configured `release.yml` controller separates read-only candidate verification
from the official writer operating under the protected `release` authority.
Verification obtains the selected candidate through authenticated acquisition and
produces an immutable assessment. The writer reacquires the same exact candidate,
checks the assessment digest and independently revalidates current authorization.
`.azurepipelines\release\promotion.ps1` separates `Verify`, `Offline` and `Write`;
the corresponding commands are `promotion-verify`, `promotion-offline` and
`promotion-write`. The
[administrator checklist](../plans/ReleaseEvidenceAdministration.md) tracks
production transport integration and safe activation of this configuration.

The coordinator accepts only an internal verified grant, not a deserialized
assessment. A signed publication-boundary record must bind the **entire**
promotion-request digest, including destinations, exact member kind/ID/version/
platform, complete supporting evidence and alias preconditions. Each OCI root
requires its immutable normalized version alias; runnable children cannot retag
the root. It rechecks eligibility before mutations, checks the active lease,
uses immutable create-only content transfers and compare-exchange alias
preconditions, reads back content/evidence and appends individual delivery events.
Matching existing bytes are a verified no-op; different immutable-version bytes
are a refusal. Interrupted transfers and write-before-journal interruption resume
from actual destination state. Publication is not atomic across images or groups.

The file transport is a bounded, isolated **offline exercise adapter**. Production
registry transport enforces remote leases/conditional writes and verifies OCI
manifest/layer/referrer discoverability, not merely blob existence. Candidate
locations, visibility, credentials and grants are bound by administrator-approved
configuration records. All current-line official writers, including preview and
rolling-build writers, operate behind the isolated boundary. Advisory applicability
for preview/development evidence does not permit bypassing stable gates.

## Offline tool commands

Release engineers use these commands to inspect or reproduce a candidate's evidence
outside the publishing workflow. They are not administrator setup commands and do
not grant official write access.

`tools\Opc.Ua.ReleaseEvidence` is a nonpackable **.NET 10-only build tool**.
It uses the centrally pinned CycloneDX.Core **12.1.1** SDK with an explicit CycloneDX
**1.6** output version, centrally pinned System.CommandLine, the existing
JsonSchema.Net validator, and the installed .NET SDK's NuGet.Versioning parser.
No CycloneDX CLI installation, tool restore, package enrichment, publication,
signature verification, or endpoint contact occurs during reconciliation.
These dependencies do not enter shipping stack project graphs.

Prepare it before the final packaging restore/build:

```powershell
dotnet build .\tools\Opc.Ua.ReleaseEvidence\Opc.Ua.ReleaseEvidence.csproj -c Release -f net10.0
dotnet test .\tests\Opc.Ua.ReleaseEvidence.Tests\Opc.Ua.ReleaseEvidence.Tests.csproj -c Release -f net10.0
$evidenceTool = '.\tools\Opc.Ua.ReleaseEvidence\bin\Release\net10.0\Opc.Ua.ReleaseEvidence.dll'
dotnet $evidenceTool --help
```

Do not select this test project on net48 or another unsupported test matrix
entry. It retains its net10 target and sets `IsTestProject=false` for an
incompatible `CustomTestTarget`; there is no empty compatibility test or
claimed net48 behavioral execution. Neither project references stack libraries.

### Capture after the last actual restore

Create a JSON capture request (UTF-8; no comments) with exactly these producer
inputs. The complete JSON examples in this section use **synthetic** SHAs,
run IDs and digests, not verification records. Replace every identity and
version with observed values before execution; never copy `trackedClean`
without checking the actual tracked worktree. Examples use the active
`stage: required` policy and stable version `2.0.0`; missing authentic intent,
trust, membership or assurance leaves evidence incomplete and blocks stable
publication. Writing diagnostic output is not release authorization:

```json
{
  "source": {
    "repository": "OPCFoundation/UA-.NETStandard",
    "actualSha": "1111111111111111111111111111111111111111",
    "actualRef": "refs/heads/master",
    "trackedClean": true
  },
  "producer": {
    "system": "github-actions",
    "workflow": ".github/workflows/nuget-publish.yml",
    "definitionSha": "2222222222222222222222222222222222222222",
    "runId": "42",
    "attempt": 1,
    "job": "build-release",
    "tools": [{"id": "dotnet", "version": "10.0.100"}]
  },
  "version": "2.0.0",
  "configuration": "Release",
  "projects": [
    "src/Opc.Ua.Types/Opc.Ua.Types.csproj",
    "tools/Opc.Ua.SourceGeneration.Pack/Opc.Ua.SourceGeneration.Pack.csproj"
  ]
}
```

The two project paths are an illustration, **not complete release membership**.
Pass all built packable projects for that configuration. The tool recursively
follows evaluated `ProjectReference` items, including nonpackable generator
variants, and evaluates each actual target framework. It runs only MSBuild
property/item evaluation, **not restore/build/pack targets**. Capture must run
after the build's implicit restore as well as explicit restore, before another
configuration can overwrite shared `project.assets.json` files.
Only evaluate projects from the trusted source checkout: MSBuild property
evaluation can execute imported SDK/property-function code even without a
build target. Capture checks `HEAD`, the locally resolvable `actualRef` and
tracked cleanliness both before and after evaluation, and rejects a packable
project whose evaluated package version differs from the request. It does not
invent a clean checkout or authenticate the remote repository/CI identity.
Build-time property overrides must be reproducible during evaluation; the
request's version is a comparison value, not an MSBuild override.

```powershell
dotnet $evidenceTool capture --repository-root . `
  --request .\staging\capture-release.json --output .\staging\frozen-release
dotnet $evidenceTool capture --repository-root . `
  --request .\staging\capture-debug.json --output .\staging\frozen-debug
```

Each destination must be empty. `build-inputs.json` records source, producer,
version/configuration, evaluated mappings/references, target-specific resolved
graphs, license semantics and payload hash candidates. `graphs\*.assets.json`
preserves exact restored assets bytes; `contracts\*` freezes contract bytes.
Every frozen file has its exact-byte digest and size. The original assets may
contain local paths/feed configuration: **keep the frozen bundle private**.
Only sanitized per-package inventories and source-input summaries become
candidate public documents. No private fuzz corpus data belongs here.

### Reconcile signed NuGet archives

Supply this complete aggregation-context shape, recording the real aggregate
job rather than substituting its build job:

```json
{
  "source": {
    "repository": "OPCFoundation/UA-.NETStandard",
    "actualSha": "1111111111111111111111111111111111111111",
    "actualRef": "refs/heads/master",
    "trackedClean": true
  },
  "producer": {
    "system": "github-actions",
    "workflow": ".github/workflows/nuget-publish.yml",
    "definitionSha": "2222222222222222222222222222222222222222",
    "runId": "42",
    "attempt": 1,
    "job": "aggregate",
    "tools": [{"id": "dotnet", "version": "10.0.100"}]
  },
  "release": { "group": "nuget", "version": "2.0.0", "channel": "stable" },
  "policyDigest": "sha256:4444444444444444444444444444444444444444444444444444444444444444",
  "artifacts": []
}
```

`policyDigest` is `sha256:` followed by the lowercase SHA-256 of the exact
protected current `.azurepipelines\release\policy.json` bytes. `channel` is a
claimed intent, never authorization;
unverified official intent is always recorded as unmet. For generation the
empty `artifacts` list is allowed because identities are read from archives.
For subsequent evaluation, replace it with the independent expected artifact
records (`kind`, `id`, `version`, `configuration`, `digest`, `scopes`, optional
`size`). Do not treat copying the output into expectations as authentication.

```powershell
dotnet $evidenceTool nuget --repository-root . `
  --packages .\staging\signed-packages `
  --inputs .\staging\frozen-release .\staging\frozen-debug `
  --context .\staging\aggregate-context.json `
  --manifest .\staging\signed-packages\release-manifest.json `
  --output .\staging\evidence
```

`--manifest` is optional: absence remains incomplete. When present, its v1
source/run/version/archive identities must match and its bytes are copied
unchanged. Signed archives are opened read-only; output contains sidecars, not
rewritten or repacked archives. `--packages` is a flat directory of `.nupkg`
and `.snupkg` files. Both configuration bundles must come from the same actual
source/version. Each project/configuration may occur only once.

Output:

* `release-evidence.json`: closed v2 companion, with all seven expected jobs
  initially `missing`, and explicit unmet controls.
* `release-manifest.json`: unchanged existing v1 manifest, when supplied.
* `source-inputs.json`: sanitized source/graph/mapping identity summary.
* `contracts\*.json`: exact policy/catalog/profile bytes.
* `packages\*.inventory.json`: actual payload hashes and ownership,
  per-project/per-target resolved graphs, exact declared consumer ranges,
  external prerequisites, licenses and unmet inventory controls.
* `packages\*.cdx.json`: per-archive CycloneDX JSON 1.6, bound to that archive's
  SHA-256; build-only components are distinct from shipped payload ownership.

The fat-generator closure includes nonpackable project outputs and private
package dependencies by **content hash**, never assembly version. A missing
or ambiguous binary owner remains `unowned`/`INVENTORY_COMPLETE`. Unknown
licenses remain unknown. Consumer-resolved ranges have no fabricated exact
version. Metapackages can legitimately contain no binaries; Symbols-named
metapackages remain ordinary `.nupkg` files.

### Evaluate against the current protected checkout

`expected-release.json` has the same context shape, but its `artifacts` must
come from the independently established expected group membership and signed
candidate identities. Here is a complete **single-subject shape example**;
it is deliberately not the complete NuGet group:

```json
{
  "source": {
    "repository": "OPCFoundation/UA-.NETStandard",
    "actualSha": "1111111111111111111111111111111111111111",
    "actualRef": "refs/heads/master",
    "trackedClean": true
  },
  "producer": {
    "system": "github-actions",
    "workflow": ".github/workflows/nuget-publish.yml",
    "definitionSha": "2222222222222222222222222222222222222222",
    "runId": "42",
    "attempt": 1,
    "job": "aggregate",
    "tools": [{"id": "dotnet", "version": "10.0.100"}]
  },
  "release": { "group": "nuget", "version": "2.0.0", "channel": "stable" },
  "policyDigest": "sha256:4444444444444444444444444444444444444444444444444444444444444444",
  "artifacts": [{
    "kind": "nuget-package",
    "id": "OPCFoundation.NetStandard.Opc.Ua.Types",
    "version": "2.0.0",
    "configuration": "Release",
    "digest": "sha256:3333333333333333333333333333333333333333333333333333333333333333",
    "scopes": { "tfms": ["net10.0"], "rids": [], "roslyn": [], "platforms": [] }
  }]
}
```

Include every actual applicable TFM/RID/Roslyn scope, not just the illustrative
net10 scope. Add the other required Release, retained Debug, metadata and
symbol subjects; optional `size` is the exact archive byte length. Use
author-signed build archive digests here, **not** the potentially changed
NuGet.org repository-signed delivery digests, which belong in separate receipts.

```powershell
dotnet $evidenceTool evaluate --repository-root . `
  --evidence .\staging\evidence\release-evidence.json `
  --expected .\staging\expected-release.json `
  --artifacts-root .\staging\signed-packages `
  --output .\staging\evaluation.json
```

This local-only invocation supplies no independent trust policy or verification
bundle. Its incomplete required-stable assessment blocks publication. Use
[authenticated evaluation](#authenticated-evaluation-and-independent-bootstrap)
with independently provisioned inputs for production verification; the synthetic,
partial examples do not satisfy that requirement.

`--repository-root` supplies the **current protected** policy/catalog/profiles,
not an old policy bundled with a candidate. The operator must obtain that
checkout and expectations through the protected controller. There is no
`--stage`, dispatch override, or claim that self-asserted policy fields were
authenticated. Bundle paths cannot be absolute, traverse, alias a reparse
point, or retrieve a URL. Duplicate JSON fields, unsupported schemas and
malformed records are invalid input, not successful evidence processing.
Required constructor fields cannot be omitted; omit optional fields rather
than setting them to `null`. Per-archive CycloneDX documents undergo full
SDK 1.6 schema validation and embedded component name/version/SHA-256 checks,
in addition to checking each referenced document's byte digest.

The result document has `schemaVersion: 1`, `status`, `stage`, `channel`,
`group`, `blocking`, `baselineFailed`, `externalVerificationPerformed`,
`unmetControls` (sorted unique codes), `findings` (`code`, `detail`) and
`inputEvidenceDigest`. Native NuGet evaluation also records
`producerEvidenceDigest`, which may differ for a linked later assessment.
Consumers must retain this document, not interpret exit code zero as
`complete`. Exit codes:

* **0**: complete valid assessment, or explicitly advisory incomplete processing
  for preview/development release channels.
* **1**: required stable controls unmet, or an observed baseline assurance
  failure (including a purportedly executed zero-test suite).
* **2**: invalid command arguments or input, unsafe path/archive, malformed JSON/schema, fatal I/O,
  or failed capture evaluation. Never hide this with a success-shaped result.

`--artifacts-root` is optional for document-only evaluation. When supplied,
the tool hashes every archive, rejects unexpected/duplicate subjects and
matches embedded ID/version/size to the envelope. Without it, archive-byte
verification remains explicitly unmet. This is not signature verification.

### Offline OCI descriptor reconciliation

The `oci` command accepts **already downloaded** OCI layout blobs. It does
not resolve tags, fetch layers or install a scanner. Supply the exact published
root digest, not a locally synthesized export `index.json` digest:

```json
{
  "images": [
    {
      "id": "ghcr.io/opcfoundation/pumpdeviceintegrationserver",
      "layout": "pump-layout",
      "rootDigest": "sha256:5555555555555555555555555555555555555555555555555555555555555555"
    }
  ]
}
```

`layout` is relative to the request JSON's directory and contains
`blobs/sha256/<hex>` files. Supply all nine image records for `containers`,
or the one Pump record for `pump`; the context uses the same source/producer/
release/expectation shape, with the corresponding group.
For example, `oci-context.json` for the Pump group is:

```json
{
  "source": {
    "repository": "OPCFoundation/UA-.NETStandard",
    "actualSha": "1111111111111111111111111111111111111111",
    "actualRef": "refs/heads/master",
    "trackedClean": true
  },
  "producer": {
    "system": "github-actions",
    "workflow": ".github/workflows/docker-image.yml",
    "definitionSha": "2222222222222222222222222222222222222222",
    "runId": "43",
    "attempt": 1,
    "job": "build",
    "tools": []
  },
  "release": { "group": "pump", "version": "2.0.0", "channel": "stable" },
  "policyDigest": "sha256:4444444444444444444444444444444444444444444444444444444444444444",
  "artifacts": []
}
```

The OCI reconciliation input may leave `artifacts` empty: discovered subjects
are reconciled against the current catalog, not credited as an authenticated
expected set. A later v2 evaluation needs independent expected `oci-index` and
`oci-manifest` records, with actual digests and one platform per runnable
manifest (`linux/amd64` for Pump); index platform scopes are empty.

```powershell
dotnet $evidenceTool oci --repository-root . `
  --request .\staging\oci-inputs.json --context .\staging\oci-context.json `
  --output .\staging\oci-result.json
```

The result has `schemaVersion`, `status`, `group`, `artifacts`, `attestations`,
`unmetControls`, and `findings`. It verifies local descriptor/config/layer
digests and sizes, distinguishes indexes/runnable manifests/annotated
attestation manifests, checks config platform/source/version linkage, and
reconciles catalog platform membership. Missing local blobs, mismatched
sizes, duplicate subjects and missing platforms are explicit findings.
Actual OCI source labels are claims, not authenticated provenance.
BuildKit attestation descriptors must name a runnable sibling through
`vnd.docker.reference.digest`. Every in-toto layer is hashed and sized; its
advertised predicate type and statement subjects must match the descriptor
and runnable digest. Native SPDX 2.3 is validated against the frozen official
schema, creation fields, unique element IDs, document-described subjects and
relationship references. Final-image file bytes and package ownership are
reconciled after bounded layer-order and whiteout processing. Unclaimed files
and unresolved ownership remain inventory failures. Unsupported SPDX versions
are not silently converted or credited.
`attestations` records locally matched `image`, `manifestDigest`,
`subjectDigest`, `layerDigest`, `predicateType` and `formatVersion` bindings.
These records are **not cryptographic verification results** and do not
certify the whole graph when other findings remain.

The diagnostic `oci` result is **not itself a v2 envelope** or an authorization.
Use `oci-assemble` for the group companion, retaining the original native
predicate bytes:

```powershell
dotnet $evidenceTool oci-assemble --repository-root . `
  --request .\staging\oci-inputs.json --context .\staging\oci-context.json `
  --assurance .\staging\assurance.json --referrers .\staging\referrers.json `
  --output .\staging\evidence
```

Assurance and referrer inputs are optional for observation, not exceptions to
required verification. Assembly exit zero means assembled, not eligible.
`evaluate` consumes the verification bundle's `ociRequestPath` and independently
authenticated producer expectations. Native SLSA predicates must match source,
invocation, Dockerfile, materials and approved BuildKit/scanner identities.
Per-subject signatures remain independent cryptographic checks.

The main group has nine images/eighteen runnable subjects; Pump has one/one.
With one index per image, their exact promotion membership is respectively
27 and 2 artifacts, preserving index versus runnable kind/platform identities.
An authenticated referrer context binds image, subject, manifest and artifact
type; the closure includes layers, configs, native attestations and signature
material. Required referrers cannot be replaced by arbitrary present blobs.

Complete synthetic groups can satisfy required evaluation under isolated,
ephemeral test trust. Production evaluation instead uses the configured protected
trust, producer qualification and publication-boundary records; missing or invalid
records block stable publication. Foundation approvals remain separate from the
engineering assessment.

## Reader compatibility and publication requirements

1. Readers must understand the existing `release-manifest.json`
   **schemaVersion 1** and the separate v2 companion. Preserve every existing
   archive field and v1 validation rule. Never reinterpret v1 as v2 or change the
   signed archive bytes.
2. Absent or v1-only evidence is explicitly incomplete and blocks in-scope
   stable publication under the active required policy.
   A v1 reader continues to consume its unchanged file; a v2 reader verifies
   the v1 digest link and matching source/version/archive identities.
   Malformed or unsupported supplied contracts return exit 2; they do not
   silently become complete or an advisory success.
3. Release assurance revalidates producer qualification against authenticated
   positive/negative records, tool compatibility, complete membership, reproducible
   input capture and public retrieval evidence.
4. Publication administrators maintain the inventory of **all** official
   current-line writers, including external Azure definitions and credentials.
   The controller checks its current principal/ref restrictions, policy identity,
   candidate/official separation and recovery qualification for each release.
5. Controlled records retain scoped approvals, immutable identities, verification
   results and revalidation triggers satisfying the policy's required fields.
   `publisherBoundaryVerified` is a derived summary of verified records, **not
   release authorization**. Empty records or incomplete required checks block
   stable publication even if somebody sets that Boolean true.
6. `stage: required` is the committed policy. In-scope official stable publication
   fails closed on missing v2 evidence or any other unmet required control.
   Preview/development channels remain advisory for these controls; they are not
   contract maturity modes. Keep baseline failures intact. Dispatch parameters
   and historical policy cannot weaken the current required stable gates.

Platform and credential changes follow the separately approved
[administrator setup checklist](../plans/ReleaseEvidenceAdministration.md).
No current exception path exists. If the Foundation later permits
emergency dispositions, add a reviewed protected, attributable, scoped record
design; controls remain visibly unmet/waived, never pass. Integrity, authorization
and source/subject binding cannot be silently waived or dispatch-overridden.

## Contract validation cases

These are required semantic reader cases, **not claimed producer results**.
JSON Schema checks shape; the reader must enforce cross-record invariants.

| Synthetic case | Expected outcome |
| --- | --- |
| Version `2.0.0`, authorized matching stable intent, required stage, missing fuzz job | Stable, incomplete; exit `1`; publication blocked |
| Version `2.0.0-preview.1`, authorized matching preview intent, required stage, missing inventory | Preview, incomplete; exit `0` for advisory processing, not release authorization |
| Version `2.0.0`, no official intent, development destination | Development, never authorized as stable |
| Prerelease bytes with claimed stable intent or dispatch-only authorization | `RELEASE_INTENT` unmet; no official stable eligibility |
| Source/run mismatch, altered sidecar, stale profile, historical weaker policy | Identity/integrity/freshness controls unmet |
| Net10 Network result is empty but job reports success | `ASSURANCE_COMPLETE` unmet; zero tests are not success |
| Net48 Network shell claims completed in required net10 profile | Reject scope substitution; no N/A escape |
| Verified empty regression inventory, all good seeds and targets executed | Empty regression subset allowed; whole job still required |
| Required stable, all local results present but boundary records absent | Incomplete; exit `1`; publication blocked |
| Complete payload shape with empty artifacts/jobs or inconsistent counters | Semantic rejection; schema validity alone cannot pass |
| Unknown top-level property, malformed digest or traversal document path | Schema rejection |
| V1-only evidence under the active required stable policy | Stable fails closed with exit `1`; no silent v1 fallback |

Reader and pipeline fixtures cover these semantics, including immutable v1
compatibility and author/feed identity separation. Release assurance also validates
the actual producer, publication and retrieval paths during qualification.

## Source references

* [NuGet producer](../.github/workflows/nuget-publish.yml),
  [promoter](../.github/workflows/release.yml),
  [archive validator](../.azurepipelines/nuget/validate-package-set.ps1),
  [modern catalog](../.azurepipelines/nuget/expected-packages.txt)
* [Shared container/Pump producer](../.github/workflows/docker-image.yml),
  [catalog-driven image selection](../.azurepipelines/containers/matrix.ps1)
* [Framework mappings](../targets.props),
  [CodeQL](../.github/workflows/codeql-analysis.yml),
  [AOT execution](../.azurepipelines/test-aot.yml),
  [shared fuzz replay](../fuzzing/Common/Fuzz.Tests/FuzzTargetTestsBase.cs)
