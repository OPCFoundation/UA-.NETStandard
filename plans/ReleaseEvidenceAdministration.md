# Release-evidence administration checklist

This checklist tracks the implementation, platform configuration and qualification
needed to deploy the configured workflow described in
[Release Evidence](../docs/ReleaseEvidence.md). It applies to current
`master`/major 2 and its `nuget`, `containers` and `pump` release groups.
Pump is a container image; its separate group preserves its registry path and
single-platform release scope.

All items start unchecked. Completion is recorded only after the indicated
evidence exists and is reviewed. This document does not authorize credential
changes, production publication or notification to authorities. Personal
assignments, credentials, infrastructure inventories and approval details stay
in controlled records, not in this repository.

## Starting conditions

The required stable-release policy is already active. Initial deployment is not
complete:

| Current surface | Work remaining before activation |
| --- | --- |
| `candidate-verification` and `candidate-writer` in [release.yml](../.github/workflows/release.yml) | Both jobs have `if: ${{ false }}`. Authenticated candidate acquisition, independent expectations/trust provisioning and the isolated writer need integration and qualification before a reviewed activation change. |
| [Promotion entrypoint](../.azurepipelines/release/promotion.ps1) and release-evidence tool | `Verify`, `Offline` and `Write` are separate operations. The file transport supports isolated exercises; the official production transport is not registered. Registry delivery is an implementation prerequisite, not an administrator Boolean. |
| [Policy](../.azurepipelines/release/policy.json) and [readiness records](../.azurepipelines/release/readiness-progress.json) | Production roots/tool pins, authenticated producer records and publisher isolation are not established by the checked-in settings. Readiness records remain pending. |
| Package signatures | The policy identifies applicable NuGet symbol-signature verification as unresolved. Ordinary `.nupkg` verification does not qualify `.snupkg` files. |

Until these prerequisites are completed, leave the job guards and fail-closed
stable gates intact. In particular, setting `publisherBoundaryVerified` or a
readiness field does not produce a verified grant.

## 1. Approve scope and inventory every publication route

- [ ] **Release administration:** select the official destinations and separate
  candidate locations for each group: NuGet.org, GitHub Packages, GitHub release
  assets and the GHCR repositories in
  [artifacts.json](../.azurepipelines/release/artifacts.json). Record visibility,
  namespace boundaries and the principals that can create content or change tags.
  **Completion evidence:** a reviewed group/destination/permission inventory.
- [ ] **GitHub administration:** inventory the write routes in
  [nuget-publish.yml](../.github/workflows/nuget-publish.yml),
  [docker-image.yml](../.github/workflows/docker-image.yml) and
  [release.yml](../.github/workflows/release.yml), including the existing `publish`
  job, preview/development publishing, older workflow revisions and manual
  dispatch. Include `GITHUB_TOKEN` package/release permissions, NuGet trusted
  publishing and any independently granted package or registry access.
  **Completion evidence:** effective grants and authorized workflow/ref identities,
  not just the YAML declarations.
- [ ] **Azure administration:** inventory external pipeline definitions, template
  revisions, service connections, secure files, variable groups and credentials
  that can sign or publish current-line artifacts. Include routes outside the
  checked-in [CI](../.azurepipelines/ci.yml) and
  [preview](../.azurepipelines/preview.yml) templates.
  **Completion evidence:** the resolved definition chain and access review for
  every external writer.
- [ ] **Publication administration:** separate shared credentials or grants before
  narrowing current-line authority; verify that unrelated release routes retain
  their approved behavior. Do not revoke shared access as an unreviewed shortcut.
  **Completion evidence:** before/after permission tests and an approved rollback
  procedure.

## 2. Complete the production integration

These items are release-engineering work coordinated by the administrator. They
cannot be completed solely through a GitHub settings page.

- [ ] **Release engineering:** implement authenticated acquisition of the selected
  producer run/attempt and complete candidate artifact set for
  `candidate-verification` and `candidate-writer`. Wire the candidate paths used
  in `release.yml`, including `evidence/release-evidence.json`,
  `verification-bundle.json` and `promotion-request.json`. Obtain `expected.json`
  from independently established source, membership and release intent; do not
  copy candidate assertions into it as proof.
  **Completion evidence:** exact-byte acquisition tests rejecting a changed run,
  attempt, source, member, size or digest.
- [ ] **Release engineering:** integrate the official production transport behind
  `promotion-write`. Enforce destination-scoped leases, conditional writes,
  immutable version aliases, complete OCI manifest/config/layer/referrer
  discoverability and content readback. Preserve the distinct NuGet author-archive
  and feed-delivery identities.
  **Completion evidence:** successful and interrupted non-production transfers,
  collision refusals and actual remote state verification. The offline file
  transport alone does not satisfy this item.
- [ ] **Release engineering and publication administration:** route every inventoried
  official writer through the isolated publication authority. Reconcile the
  existing `publish` job and producer-side NuGet/GHCR pushes with that boundary;
  ordinary source builds retain only candidate access. Include preview and rolling
  development writers even though their evidence applicability is advisory.
  **Completion evidence:** a negative test showing that a source build, old
  workflow or candidate credential cannot write an official destination.
- [ ] **Signing administration and release engineering:** qualify an approved
  signature-verification path for every enrolled artifact kind, including applicable
  `.snupkg` files. Preserve existing package signing checks and the distinction
  between primary author signatures and repository countersignatures.
  **Completion evidence:** valid, invalid and unsupported signature cases for each
  kind; no ordinary-package result credited to a symbol package.
- [ ] **Infrastructure administration:** provision durable writer journal storage
  for `OPCUA_RELEASE_JOURNAL`, with access separate from candidate content and
  read-only verifier work. Set lease and recovery policies for the production
  transport.
  **Completion evidence:** restart recovery from actual destination state,
  including a write that succeeded before its journal event was recorded.

## 3. Configure protected authority and least privilege

- [ ] **GitHub administration:** configure the `release` environment under
  **Settings > Environments**. Review required reviewers, self-review prevention,
  deployment branch restrictions and bypass permissions. The official controller
  is `.github/workflows/release.yml` at `refs/heads/master` in
  `OPCFoundation/UA-.NETStandard`; denied refs and forks do not obtain its authority.
  **Completion evidence:** exported effective settings and allowed/denied deployment
  tests. An environment name alone is insufficient.
- [ ] **Repository administration:** protect the controller, policy, catalog and
  verification code through the applicable branch/ruleset and review requirements.
  Build controller binaries from the protected checkout before acquiring candidate
  data; do not execute candidate scripts or candidate-provided verifier binaries.
  **Completion evidence:** a reviewed controller revision and a denied
  candidate-code execution test.
- [ ] **NuGet administration:** configure the trusted publishing policy for owner
  `OPCFoundation`, repository `UA-.NETStandard`, workflow file `release.yml`,
  environment `release`. Provision the existing `NUGET_USER` setting and verify
  the permitted package scope. Tokens are obtained by the isolated writer, not
  stored in source or supplied by candidates.
  **Completion evidence:** authenticated non-production qualification and rejection
  of an unapproved workflow/ref/environment identity.
- [ ] **Registry and signing administration:** grant only the selected writer
  identity official package/image write access. Restrict candidate build access
  and preserve protected signing-service settings used by `nuget-publish.yml`.
  Review GHCR/GitHub Packages permissions separately from NuGet.org trusted
  publishing.
  **Completion evidence:** principal-by-destination tests covering both container
  groups and NuGet variants, with raw credentials excluded from the report.

## 4. Provision independent trust and record authorities

- [ ] **Release-assurance administration:** produce the protected snapshot described
  by [trusted-policy-snapshot.schema.json](../.azurepipelines/release/trusted-policy-snapshot.schema.json).
  Populate actual `authority`, `checkpointSequence`, `issuedAt`, `expiresAt`,
  `policyDigest`, `contractFiles`, `expectedRelease`, `expectedIntentDigest`,
  `authorities`, `producers` and `revokedRecordIds`. Use the reviewed source and
  exact policy/catalog/profile bytes.
  **Completion evidence:** an independently approved snapshot/checkpoint; neither
  a filename nor its own unsigned digest establishes trust.
- [ ] **Infrastructure administration:** provision the snapshot, verifier binaries
  and trusted-root material outside controller build, candidate and working
  directories as required by the path guards. Supply `OPCUA_RELEASE_TRUST_POLICY`
  and the independently approved `OPCUA_RELEASE_TRUST_POLICY_SHA256` only to the
  protected controller. Do not calculate the trusted digest from an untrusted
  candidate file during the run.
  **Completion evidence:** accepted protected placement and rejected
  candidate-controlled, aliased or digest-mismatched placements.
- [ ] **Release-assurance administration:** enroll the actual `tool` and
  `trustedRoot` pins and the applicable `nugetVerifier`, `cosignVerifier` and
  `nugetAuthorFingerprints`. Record exact executable/root digests and compatible
  versions; install them through protected provisioning.
  **Completion evidence:** verification with the pinned tools and refusal after
  an executable/root change.
- [ ] **Security and release-assurance administration:** enroll issuer,
  certificate identity, repository, workflow, definition SHA, ref and permitted
  record kinds for each authority. Cover release intent, producer, artifact
  signatures, assurance, public review, producer qualification and publication
  boundary. Native NuGet enrollment covers `native-nuget-index` and
  `native-nuget-pack`; separately qualify CodeQL review authentication.
  **Completion evidence:** valid scoped records and rejection of wrong identities,
  kinds, sources, attempts, expired records and revoked records.
- [ ] **Security review:** establish the controlled CodeQL disposition process.
  The independently signed review binds the complete query/finding population
  before the final summary/index is generated. Raw findings and review records
  stay restricted.
  **Completion evidence:** complete and partial-population tests, including a
  nonzero finding population that is not silently converted to zero unresolved
  findings.

## 5. Qualify execution, delivery and recovery

- [ ] **Release assurance:** run the seven expected jobs from
  [profiles.json](../.azurepipelines/assurance/profiles.json) against the same
  source and verify run/attempt, host/library TFM and producer definition/tool
  identities. Inspect real test counts, all fuzz target/input pairs, native
  image/runtime identity and CodeQL extraction/query coverage. PubSub's
  supplemental baseline replay does not replace a required profile job.
  **Completion evidence:** authenticated completed job proofs and negative controls
  for a skipped job, zero tests, missing seed/input, wrong TFM and mixed attempts.
- [ ] **Release assurance:** qualify complete NuGet and both container groups in
  approved non-production destinations. Check Release/Debug/metapackage/symbol
  membership, per-package ownership and inventories, all 19 runnable container
  subjects, their indexes and complete referrer/content graphs.
  **Completion evidence:** accepted complete groups and refusals for missing
  members, platforms, signatures, layers, referrers and altered bytes.
- [ ] **Publication administration:** exercise expiry/revocation between
  verification and writing, destination conflicts, lease loss, reruns and
  interrupted delivery. Verify immutable version alias preconditions and
  create-only journals/receipts rather than weakening them to permit a retry.
  **Completion evidence:** no unauthorized mutation and successful recovery from
  the actual remote state for each supported transport.
- [ ] **Release maintainers:** retrieve delivered packages, images and public
  evidence through their normal read paths. Verify NuGet content and approved
  signatures despite repository-signing byte changes; verify container manifests
  and referrers by digest.
  **Completion evidence:** separate delivery-verification records, not merely an
  accepted upload or `--skip-duplicate` exit.
- [ ] **Information-governance administration:** configure access, retention,
  retrieval and deletion for public release evidence versus restricted bootstrap,
  finding, approval, raw diagnostic and incident records. Use the Foundation's
  approved retention policy rather than an inferred statutory number of years.
  **Completion evidence:** public-payload review, denied unauthorized access and
  successful authorized retrieval of the restricted records.

## 6. Record approval and activate through review

- [ ] **Programme approval and release administration:** create controlled
  completion records for every ID in `policy.json`'s
  `graduation.requiredChecks`. Include scoped groups/destinations, approved
  principal/ref restrictions, immutable policy/controller/definition identities,
  reviewer and approval time, evidence digests, limitations and revalidation
  triggers. Preserve shared-authority isolation checks as well as local technical
  checks.
  **Completion evidence:** authenticated records accepted by the protected
  verifier, not a locally edited readiness status.
- [ ] **Foundation programme functions:** complete the separate policy,
  assignment, provided-system, cooperation, reporting-access and exercise records
  in the [Security Stewardship process](../docs/SecurityStewardship.md#administrator-and-foundation-operating-prerequisites).
  **Completion evidence:** controlled approvals and performed exercise results;
  engineering CI success is not a substitute.
- [ ] **Release engineering:** submit the activation change only after the
  preceding evidence is accepted. Replace the disabled guards on
  `candidate-verification` and `candidate-writer` with reviewed repository/ref/event
  restrictions, wire the protected inputs and production transport, and confirm
  the existing `publish` path cannot bypass them. Keep `stage: required`, all
  baseline gates and the separate group boundaries.
  **Completion evidence:** reviewed code/configuration plus a complete
  non-production run under the same effective permissions.
- [ ] **Publication administration:** approve the first official execution for the
  exact source, group, version and artifact set; confirm pre-write revalidation,
  delivery readback, journal and receipts.
  **Completion evidence:** authenticated production execution and delivery records.
  Update derived setup indicators only to reflect those verified records.
- [ ] **Release assurance:** schedule and record revalidation on policy, controller,
  producer, tool, grant, destination or scope changes, expiry and revocation.
  **Completion evidence:** a current checkpoint and qualification record are
  checked for each subsequent release; obsolete approvals do not remain effective.

The public [readiness template](../.azurepipelines/release/readiness-progress.json)
can carry scoped opaque references to these controlled records. Its validator
checks structure and membership only:

```powershell
$tool = '.\tools\Opc.Ua.ReleaseEvidence\bin\Release\net10.0\Opc.Ua.ReleaseEvidence.dll'
dotnet $tool validate-readiness --repository-root . `
  --input .\.azurepipelines\release\readiness-progress.json `
  --output .\readiness-validation.json
```

`pending`, submitted claims and successful schema validation do not authenticate an
approval. Keep personal details, secret values, protected paths and raw evidence
out of public references and completion notes.
