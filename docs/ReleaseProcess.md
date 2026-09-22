# Release process

This is the authoritative procedure for every release operation on this
repository: cutting a release line, shipping a patch or minor version,
backporting a fix, promoting a stable candidate, and recovering from a
failed or partial release. Follow it exactly as written; it is designed to
be followed by a developer or an agent working from the repository alone,
without any other context.

> **Read this before**: changing a release version, cutting or retiring a
> release branch, backporting a fix, running `release.yml`, or recovering
> from a failed release.

## Terms

- **Root version** - the package version computed from the committed
  `version.json` for the commit being built (see
  [Versioning](DeveloperGuide.md#versioning)). Every package in the solution
  shares the same root version except the **preview-only families**.
- **Preview-only families** - the XRegistry, WoT Connectivity, Vision,
  Robotics, Redundancy, Positioning, OpenUSD, ISA95, AI, and DI package
  families, the Robotics/Vision MCP extensions, and the OpenUSD connector
  tools. `version.targets` keeps these on a `-preview.N` suffix even when the
  root version is an exact stable release. The full, single source of truth
  for which package IDs this covers is `_IsPreviewPackage` in
  `version.targets` (build-time) and `Test-PreviewPackageId` in
  `.azurepipelines/package-version-policy.ps1` (validation-time) - both must
  always list the same families.
- **Channel** - `stable` when the root version has no prerelease label or
  build metadata (e.g. `2.0.0`); `preview` otherwise (e.g. `2.0.0-preview.6`,
  `2.0.1-preview.4+gabc123`). `validate-nuget-package-set.ps1` writes this as
  `channel` in its manifest; `Test-StablePackageVersion` in
  `package-version-policy.ps1` is the single source of truth.
- **Canonical release branch** - a branch named exactly
  `release/<major>.<minor>` (e.g. `release/2.0`, `release/2.1`) - the only
  branch shape `version.json`'s `publicReleaseRefSpec` and
  `Test-CanonicalReleaseBranchRef` recognize as eligible to produce a
  `stable` channel package set. The historical three-component naming
  (`release/2.0.0`) is retired and not canonical; see
  [Historical `release/2.0.0` branch](#historical-release200-branch).
- **Candidate** - a signed, validated package set produced by a
  `nuget-publish.yml` run, retained as its `opcua-packages-<run id>` GitHub
  Actions artifact.

## Common preflight

Every procedure below starts here.

1. **Resolve the repository and remotes.**
   ```powershell
   git rev-parse --show-toplevel
   git remote -v
   ```
   Confirm you are in a clone of `OPCFoundation/UA-.NETStandard` (or a fork
   with `origin`/`upstream` pointing at it) before continuing.

2. **Require a clean worktree.**
   ```powershell
   git status --short
   ```
   *Completion evidence*: no output. Commit, stash, or discard changes before
   continuing - every procedure below assumes a clean starting point.

3. **Fetch and confirm the source/target branches exist.**
   ```powershell
   git fetch origin --prune
   git branch --all --list 'origin/release/*'
   ```
   *Completion evidence*: the branch(es) the procedure you are about to run
   names are present (or, for a branch-creation step, deliberately absent).

4. **Confirm required tooling and authentication.**
   ```powershell
   dotnet --version
   gh auth status
   ```
   *Completion evidence*: a .NET 10 SDK version, and `gh` reports an
   authenticated account with access to `OPCFoundation/UA-.NETStandard`.
   Promotion (`release.yml`) additionally requires the `release` GitHub
   Environment's approval; you cannot grant that to yourself; a repository
   maintainer with environment-reviewer access must approve the run.

5. **Discover the currently published preview numbers before touching
   `preview-version.props`.** Every procedure that changes the root version
   to an exact stable release must confirm the committed
   `PreviewPackageBuildNumber` (see `preview-version.props`) is still greater
   than every already-published `-preview.N` (or legacy `-preview.YYYYMMDD`)
   identifier for the preview-only families, on **both** feeds:
   ```powershell
   Invoke-RestMethod 'https://api.nuget.org/v3-flatcontainer/opcfoundation.netstandard.opc.ua.xregistry/index.json' |
       Select-Object -ExpandProperty versions
   gh api '/orgs/OPCFoundation/packages/nuget/OPCFoundation.NetStandard.Opc.Ua.XRegistry/versions' --paginate |
       ConvertFrom-Json | ForEach-Object name
   ```
   Repeat for a representative sample of the preview-only families (at
   minimum XRegistry, DI, and one MCP/connector package); they are versioned
   together but published independently, so check more than one. If any
   published number is greater than or equal to the committed
   `PreviewPackageBuildNumber`, raise it (in the same PR that changes the
   root version to stable) before proceeding.

   This manual sampling is only a headroom estimate. The authoritative check
   is `.azurepipelines/validate-preview-package-ordering.ps1`, which queries
   **every** preview-family package ID in a candidate's manifest against both
   feeds. `nuget-publish.yml` and `release.yml` both run it automatically for
   stable candidates, and both fail if any already-published version is not
   strictly below the candidate's `-preview.N`. Note that ordinary `master`
   development keeps publishing `<base>-preview.{height}` packages with a
   growing height, so a number that was correct last week can go stale on its
   own - always re-run the check against the **actual merged candidate**
   ([Candidate and dry run](#candidate-and-dry-run) step 3) before approving
   a promotion.

## Which procedure do I need?

| Situation | Procedure |
| --- | --- |
| Shipping the very first stable `2.0.0` | [First stable release](#first-stable-release) |
| Shipping the next patch on an existing line (e.g. `2.0.1`, `2.0.2`) | [Patch release](#patch-release) |
| Shipping the next minor line (e.g. `2.1.0`) | [Minor release](#minor-release) |
| Porting an already-reviewed `master` fix onto a release branch | [Routine backport](#routine-backport) |
| A production-only bug needs a fix that did not start on `master` | [Emergency hotfix](#emergency-hotfix) |
| You have a `nuget-publish.yml` run and want to inspect it before/without publishing | [Candidate and dry run](#candidate-and-dry-run) |
| A maintainer has approved a stable candidate for publication | [Approved promotion](#approved-promotion) |
| A release or promotion attempt errored, or you are unsure what published | [Failed or partial release](#failed-or-partial-release) |
| You just promoted a release and want to confirm everything is consistent | [Post-release verification](#post-release-verification) |

## Branch and channel model

- `master` always carries a `-preview.{height}` root version for the next
  minor release under development (e.g. `2.0.0-preview.{height}` while `2.0`
  is being prepared, `2.1.0-preview.{height}` once `2.0` has shipped).
  Every `master` build is `channel: preview` and publishes automatically to
  GitHub Packages; it never produces a stable release.
- Each maintained minor line lives on its own canonical release branch
  (`release/2.0`, `release/2.1`, ...). While stabilizing, its root version is
  also `-preview.{height}` (e.g. `2.0.1-preview.{height}` between the `2.0.0`
  and `2.0.1` releases) and publishes automatically to GitHub Packages, same
  as `master`. At the exact commit approved for release, its root version is
  set to the plain `<major>.<minor>.<patch>` (e.g. `2.0.0`, `2.0.1`, `2.1.0`)
  - `channel: stable` - and that specific package set is only ever published
  by the [Approved promotion](#approved-promotion) procedure, never
  automatically.
- Patch numbers only ever increase by exactly one per release on their line
  (`2.0.0` → `2.0.1` → `2.0.2`, ...); a minor bump resets the patch to zero
  (`2.1.0`, not `2.1.<height>`). Nothing about git height, commit count, or
  CI retries changes these numbers - they are explicit, committed decisions
  in `version.json`.
- A release is **never** produced from `master`, a tag, a PR branch, or any
  branch other than its own canonical `release/<major>.<minor>` line. This is
  enforced in multiple independent places: `version.json`'s
  `publicReleaseRefSpec` (so only that ref shape ever computes
  `NBGV_PublicRelease == 'True'`), the `Aggregate and validate packages` step
  in `nuget-publish.yml` (refuses to build a stable set from a non-canonical
  ref), `release.yml` (refuses to promote from a non-canonical ref or a
  non-stable candidate), and the `release` GitHub Environment's
  `deployment_branch_policy` (restricts which branch may even execute the
  `release.yml` job).
- The branch and stable package version must agree: `release/2.0` may
  release `2.0.p` only, while `release/2.1` may release `2.1.p` only.
  `Test-CanonicalReleaseBranchForPackageVersion` enforces this in both the
  candidate and promotion workflows, so a valid-looking `2.1.0` package set
  cannot be released from `release/2.0`.
- Azure Pipelines validates the same signed package-set policy as GitHub
  Actions. Its internal preview-feed upload runs only when the manifest
  channel is `preview`; a manually queued stable release-line build remains
  artifact-only and must go through the same approved `release.yml`
  promotion procedure.
- Container image tags follow the same line model, enforced by the
  `Determine release line precedence` step in
  `.github/workflows/docker-image.yml`. Every build gets its exact version
  tag; a stable release additionally gets `<major>.<minor>` and the plain
  `<major>.<minor>.<patch>`. The unqualified `latest` and `release` aliases
  are claimed **only** by the newest canonical `release/<major>.<minor>`
  branch that exists on `origin`, so a later `2.0.x` maintenance release
  publishes `latest-2.0` instead and cannot move `latest`/`release` back
  from an already published `2.1.x`. The check fails closed: if the newest
  line cannot be determined, the build is treated as superseded and only
  publishes line-specific aliases. Non-release builds keep their existing
  branch-suffixed `latest-<branch>` alias, and the master-only pump image
  publishes `preview` rather than `latest`.

## First stable release

Ships the very first stable version on a brand-new minor line (in this
repository's history, `2.0.0`).

**Owner**: a repository maintainer with permission to create protected
branches and merge to `master`.

**Preconditions**: the [common preflight](#common-preflight) is complete; the
preparation work for the release (dependency/version-model changes, this
document) is already merged to `master`; `master`'s root version is
`2.0.0-preview.{height}`.

1. Confirm `master`'s current committed version:
   ```powershell
   git show HEAD:version.json
   ```
   *Completion evidence*: `"version": "2.0.0-preview.{height}"`.

2. Choose the exact `master` commit to branch from (normally its current
   `HEAD`, after its required checks have passed) and create the canonical
   release branch from it:
   ```powershell
   git fetch origin master
   git branch release/2.0 origin/master
   git push origin release/2.0
   ```
   *Completion evidence*: `gh api repos/OPCFoundation/UA-.NETStandard/branches/release/2.0`
   returns the expected commit SHA.

3. Immediately advance `master` to the next minor line's preview version so
   no further commit is mistaken for `2.0` servicing work. Open a PR (do not
   push directly to the protected branch):
   ```powershell
   git switch -c bump-version-2.1-preview origin/master
   ```
   Edit `version.json`'s `"version"` to `"2.1.0-preview.{height}"`. Commit,
   push, and open a PR titled something like "Advance master to 2.1.0
   preview". Merge once required checks pass.
   *Completion evidence*: `master`'s `version.json` shows
   `2.1.0-preview.{height}`.

4. On `release/2.0`, open a **second** PR that sets the stable version and
   completes the release preflight (do not combine this with step 3 - they
   target different branches):
   ```powershell
   git switch -c release-2.0.0 origin/release/2.0
   ```
   Edit `version.json`'s `"version"` to `"2.0.0"` (no prerelease label).
   Re-run the preview-number check from
   [Common preflight](#common-preflight) step 5 against the actual current
   published state and adjust `preview-version.props` if needed. Commit,
   push, and open a PR against `release/2.0`.
   *Completion evidence*: the PR's diff changes only `version.json` (and, if
   needed, `preview-version.props`); required checks pass.

5. Merge the PR from step 4 once its checks pass and it has the required
   approval. This is the release commit.

6. Continue with [Candidate and dry run](#candidate-and-dry-run) using the
   `nuget-publish.yml` run triggered by that merge, then
   [Approved promotion](#approved-promotion).

7. After promotion, reopen `release/2.0` for the next patch's development by
   repeating step 3's pattern on `release/2.0` itself, setting `version.json`
   to `"2.0.1-preview.{height}"` via a PR.

## Patch release

Ships the next patch on an already-shipped line (for example `2.0.1` after
`2.0.0`, or `2.0.2` after `2.0.1`).

**Owner**: a repository maintainer.

**Preconditions**: the [common preflight](#common-preflight) is complete;
`release/<major>.<minor>` already exists and its current committed
`version.json` is the next patch's `-preview.{height}` version (see step 7 of
[First stable release](#first-stable-release) or step 3 below, from a prior
run of this same procedure).

1. Confirm the last released version on this line and the next patch number:
   ```powershell
   gh api repos/OPCFoundation/UA-.NETStandard/releases --paginate --jq '.[].tag_name' |
       Where-Object { $_ -match '^2\.0\.\d+$' } | Sort-Object { [version]$_ }
   ```
   The next patch is exactly one more than the highest result - never skip a
   number and never reuse one.

2. Collect and backport the fixes intended for this patch (see
   [Routine backport](#routine-backport) for each one) before continuing.

3. Open a PR against `release/<major>.<minor>` that sets `version.json`'s
   `"version"` to the exact next patch (e.g. `"2.0.1"`, no prerelease label),
   re-verifying the preview-number check from
   [Common preflight](#common-preflight) step 5. Merge once checks pass. This
   is the release commit.

4. Continue with [Candidate and dry run](#candidate-and-dry-run), then
   [Approved promotion](#approved-promotion).

5. After promotion, open a PR against the same release branch advancing
   `version.json` to the *following* patch's preview version (e.g.
   `"2.0.2-preview.{height}"`) before accepting further servicing work.

## Minor release

Ships the next minor line (for example `2.1.0` after `2.0.x`).

**Owner**: a repository maintainer.

**Preconditions**: the [common preflight](#common-preflight) is complete;
`master`'s current committed root version is the new minor's preview version
(e.g. `2.1.0-preview.{height}`, set when the *previous* minor line was cut -
see step 3 of [First stable release](#first-stable-release)).

This procedure is identical to [First stable release](#first-stable-release)
with two substitutions: create `release/<new-minor>` (e.g. `release/2.1`)
instead of `release/2.0`, and advance `master` to the minor *after* that
(e.g. `2.2.0-preview.{height}`) instead of `2.1.0-preview.{height}`. Every
older maintained line (e.g. `release/2.0`) is unaffected and continues to
receive its own patches independently through
[Patch release](#patch-release); do not merge the new minor's branch into or
out of an older line.

## Routine backport

Ports an already-merged, already-tested fix from `master` onto a release
branch for an upcoming patch.

**Owner**: any contributor.

**Preconditions**: the fix already has a merged commit on `master`; the
[common preflight](#common-preflight) is complete; you know the target
`release/<major>.<minor>` branch and the commit SHA to port.

1. Branch from the release line, not from `master`:
   ```powershell
   git fetch origin release/2.0
   git switch -c backport-<short-description> origin/release/2.0
   ```

2. Cherry-pick the fix with traceability to its source commit. Never
   cherry-pick a version-only or release-preparation commit (that would
   collide with the target branch's own version state):
   ```powershell
   git cherry-pick -x <source-commit-sha>
   ```
   Resolve conflicts so only the intended fix lands - do not pull in
   unrelated `master`-only changes, and do not alter `version.json` or
   `preview-version.props` in this PR.

3. Push and open a PR **against the release branch**
   (`release/<major>.<minor>`, not `master`). Reference the original `master`
   PR/commit in the description.
   *Completion evidence*: required checks pass on the release branch build;
   the PR is reviewed and merged.

4. Confirm the fix is included in the next
   [Patch release](#patch-release) for that line.

## Emergency hotfix

For a production-only defect that cannot wait for the normal
`master`-first flow (for example a security fix needed on an older
maintained line before it can land anywhere else).

**Owner**: a repository maintainer, in coordination with whoever authors the
fix.

**Preconditions**: the [common preflight](#common-preflight) is complete; you
have identified every maintained release line the defect affects (there may
be more than one) and the current state of `master`.

1. Start on the **oldest** affected maintained release branch. Branch, fix,
   and open a PR against that release branch exactly as in
   [Routine backport](#routine-backport) steps 1-3, except the fix itself is
   authored fresh here rather than cherry-picked from an existing commit.

2. Once merged there, forward-port the same fix (cherry-pick with `-x` from
   the release-branch commit this time) into every newer affected maintained
   release line, in ascending order, each through its own PR against that
   line.

3. Forward-port the same fix into `master` last, through an ordinary PR.

4. Track the forward-port chain to completion explicitly (for example in the
   original issue or PR description): list every affected line, and do not
   consider the hotfix done until each one has a merged PR. A hotfix that
   never reaches `master` will keep reappearing on every future release of
   the older line.

5. Include the fix in the next [Patch release](#patch-release) for each
   affected line.

## Candidate and dry run

Produces (or locates) a validated, signed package set from `nuget-publish.yml`
and inspects it without publishing anything. Every release and every dry run
starts here.

**Owner**: any contributor can run this read-only inspection; only a
maintainer can subsequently approve [Approved promotion](#approved-promotion).

**Preconditions**: the [common preflight](#common-preflight) is complete.

1. Find the `nuget-publish.yml` run for the commit you care about:
   ```powershell
   gh run list --workflow nuget-publish.yml --branch release/2.0 --limit 5
   ```
   *Completion evidence*: a `run id` with `status: completed` and
   `conclusion: success`. If none exists yet for your commit, either wait for
   the automatic run (pushes to `master` and canonical release branches
   trigger it) or start one manually:
   ```powershell
   gh workflow run nuget-publish.yml --ref release/2.0
   ```

2. Download that run's `opcua-packages-<run id>` artifact and inspect its
   manifest:
   ```powershell
   gh run download <run-id> --name opcua-packages-<run-id> --dir ./candidate
   Get-Content ./candidate/release-manifest.json | ConvertFrom-Json |
       Select-Object basePackageVersion, channel, packageCount, debugPackageCount, ref, commit
   ```
   *Completion evidence*: `basePackageVersion`, `channel`, and `ref` match
   what you expect (a stable version only from a canonical `release/*`
   branch; a preview version from `master` or an in-progress release
   branch). `debugPackageCount` is greater than zero and less than
   `packageCount` (both Release and Debug package IDs are present).

3. To prove the set is eligible for promotion **without publishing anything**,
   run the exact validation `release.yml` itself performs:
   ```powershell
   . ./.azurepipelines/package-version-policy.ps1
   ./.azurepipelines/validate-nuget-package-set.ps1 `
     -PackageDirectory ./candidate `
     -ManifestPath ./candidate/current-package-set.json `
     -ExpectedVersion (Get-Content ./candidate/release-manifest.json | ConvertFrom-Json).basePackageVersion `
     -RequireDebug `
     -VerifySignatures
   Test-CanonicalReleaseBranchRef -Ref (Get-Content ./candidate/release-manifest.json | ConvertFrom-Json).ref
   ```
   *Completion evidence*: both commands succeed with no thrown error, and the
   `Test-CanonicalReleaseBranchRef` call prints `True` for a release you
   intend to promote.

   For a stable candidate, also prove that the preview-only families in the
   set still sort strictly above everything already published, using the same
   script both workflows run:
   ```powershell
   $env:GITHUB_TOKEN = (gh auth token)
   ./.azurepipelines/validate-preview-package-ordering.ps1 `
     -ManifestPath ./candidate/current-package-set.json `
     -GitHubPackagesOwner OPCFoundation
   ```
   *Completion evidence*: the script prints that every preview-family package
   ID sorts above its published versions and exits `0`. It returns early
   (also `0`) when the manifest's `channel` is not `stable`, because a
   preview candidate carries no synthesized `-preview.N` number to compare.
   A non-zero exit lists every offending package ID and published version -
   see [Failed or partial release](#failed-or-partial-release) step 2 for
   how to recover.

4. Alternatively, dry-run the actual promotion workflow itself (still no
   publication occurs):
   ```powershell
   gh workflow run release.yml --ref release/<major>.<minor> `
     -f release_run_id=<run-id> -f dry_run=true
   ```
   `--ref` is required and must name the same canonical release branch the
   candidate was built from: `release.yml` refuses to run when its own
   `GITHUB_REF` differs from the candidate run's branch, so that the
   validation policy and the promotion code are source-bound to the bytes
   being promoted. Without `--ref`, `gh` dispatches from the default branch
   and the run stops at "Resolve candidate run".

   *Completion evidence*: the run succeeds through "Validate promotion
   manifest and package bytes"; the summary reports the expected version and
   package count; no "Push to nuget.org" or "Push to GitHub Packages" step
   ran (both are skipped under `dry_run`).

## Approved promotion

Publishes an already-built, already-signed stable candidate to nuget.org and
GitHub Packages. nuget.org receives only non-`.Debug` package IDs (and their
symbol packages); GitHub Packages receives the complete candidate, including
`.Debug` package IDs. This is the **only** path that ever publishes a stable
release; nothing else in this repository's CI does so automatically.

**Owner**: a repository maintainer with `release` GitHub Environment
reviewer access.

**Preconditions**: [Candidate and dry run](#candidate-and-dry-run) is
complete and its manifest shows `channel: stable` and the exact expected
version; the run's source branch is the canonical `release/<major>.<minor>`
line you intend to release; the change has the required approvals.

`release.yml` re-runs `validate-preview-package-ordering.ps1` itself, before
it authenticates to any feed, so an ordering violation that appeared after
your dry run stops the promotion before the first public write rather than
midway through it.

1. Trigger the promotion for real, from the same canonical release branch the
   candidate was built from:
   ```powershell
   gh workflow run release.yml --ref release/<major>.<minor> `
     -f release_run_id=<run-id> -f dry_run=false
   ```
2. Approve the pending deployment to the `release` environment when
   prompted (in the GitHub UI, or `gh run watch <new-run-id>` from the CLI).
   *Completion evidence*: the run proceeds past "Push to nuget.org" and
   "Push to GitHub Packages" without error. The nuget.org log explicitly says
   it is publishing only non-Debug package IDs; the GitHub Packages loop
   reports `--skip-duplicate` success (not a conflict) for the complete
   candidate, including its `.Debug` packages.

3. Confirm the non-Debug packages are live on nuget.org:
   ```powershell
   Invoke-RestMethod 'https://api.nuget.org/v3-flatcontainer/opcfoundation.netstandard.opc.ua.core/index.json' |
       Select-Object -ExpandProperty versions | Select-Object -Last 3
   ```
   *Completion evidence*: the expected exact stable version (e.g. `2.0.0`)
   appears. Do not expect any `.Debug` package ID on nuget.org; inspect
   GitHub Packages for those IDs instead.

4. Tag the released commit and publish the GitHub Release, binding both to
   the exact source SHA the candidate was built from (recorded in the
   promotion manifest as `commit`):
   ```powershell
   git tag 2.0.0 <commit-sha-from-manifest>
   git push origin 2.0.0
   gh release create 2.0.0 --target <commit-sha-from-manifest> --generate-notes
   ```

5. Record the [release handoff](#release-handoff-template) in the GitHub
   Release description or the tracking issue.

6. Return to the calling procedure ([First stable release](#first-stable-release),
   [Patch release](#patch-release), or [Minor release](#minor-release)) to
   complete its final "advance to the next preview version" step.

## Failed or partial release

Recovers from an error anywhere in [Candidate and dry run](#candidate-and-dry-run)
or [Approved promotion](#approved-promotion).

**Owner**: a repository maintainer.

1. **Determine whether any public write already happened.** Check nuget.org
   for a non-Debug package and GitHub Packages for the complete candidate
   (including its `.Debug` package IDs):
   ```powershell
   Invoke-RestMethod 'https://api.nuget.org/v3-flatcontainer/opcfoundation.netstandard.opc.ua.core/index.json' |
       Select-Object -ExpandProperty versions
   gh api '/orgs/OPCFoundation/packages/nuget/OPCFoundation.NetStandard.Opc.Ua.Core/versions' --paginate |
       ConvertFrom-Json | ForEach-Object name
   ```

2. **If nothing published yet** (the failure was in validation, signing, or
   before any `dotnet nuget push`): fix the underlying problem (a code fix
   via [Routine backport](#routine-backport)/[Emergency hotfix](#emergency-hotfix),
   or a version/config fix via a normal PR against the release branch), which
   produces a **new** `nuget-publish.yml` run and a new candidate. Start over
   at [Candidate and dry run](#candidate-and-dry-run) with that new run - do
   not attempt to reuse or repair the failed one.

   Two failures are specific to the preview-ordering gate, and both stop the
   run before any public write:

   | Symptom | Cause and safe next action |
   | --- | --- |
   | `validate-preview-package-ordering.ps1` lists package IDs whose published versions are not below the candidate's `-preview.N` | Ordinary `master` development published a higher `<base>-preview.{height}` while this release was being prepared. Raise `PreviewPackageBuildNumber` in `preview-version.props` above every listed version, via a normal PR against the release branch, then build and validate a **new** candidate. Never lower a published version or delete packages to make room. |
   | The script throws while querying nuget.org or GitHub Packages (for example `401`/`403`, or any non-`404` error status) | The gate deliberately fails closed: it cannot prove ordering, so it refuses to let the release proceed. Do **not** bypass it. Confirm the workflow passes a token with `packages: read`/`packages: write` for the owner being queried, and that the feed is reachable, then re-run the same workflow. A genuinely unpublished package ID returns `404`, which the script treats as "nothing to compare" and allows. |

3. **If nuget.org already has some non-Debug packages, or GitHub Packages has
   some expected packages** for the version (a partial push - `dotnet nuget
   push` is per-package and can fail partway through): re-run
   [Approved promotion](#approved-promotion) against the **same**
   `release_run_id`. `--skip-duplicate` makes re-pushing the packages that
   already succeeded a no-op; only the missing ones are actually written.
   `.Debug` package IDs are intentionally absent from nuget.org, so their
   absence there is not a partial-release symptom.
   Never build a new candidate for a version that already has *any* packages
   published - that would risk two different byte sequences under the same
   immutable version. Investigate why the previous attempt stopped before
   retrying.

4. **If the version needs to change entirely** (e.g. the approved candidate
   turned out to be wrong before any push happened): do not reuse the
   version number. Treat this as returning to the relevant release procedure
   ([First stable release](#first-stable-release)/[Patch release](#patch-release)/[Minor release](#minor-release))
   with a corrected `version.json`, producing a fresh candidate for a
   version nothing has ever published under.

5. **If you cannot determine what happened** (for example the workflow log
   is inconclusive about whether a push completed): stop and escalate to
   another maintainer before taking any further action. Never guess at a
   feed's state by re-running a push blind - check it first as in step 1.

## Post-release verification

Confirms a completed [Approved promotion](#approved-promotion) left the
repository and both feeds consistent.

1. Confirm nuget.org has the exact expected non-Debug version and GitHub
   Packages has the complete candidate (repeat
   [Approved promotion](#approved-promotion) step 3 for GitHub Packages too):
   ```powershell
   gh api '/orgs/OPCFoundation/packages/nuget/OPCFoundation.NetStandard.Opc.Ua.Core/versions' --paginate |
       ConvertFrom-Json | ForEach-Object name | Select-Object -Last 3
   ```
2. Confirm the release tag and GitHub Release point at the exact commit
   recorded in the promotion manifest:
   ```powershell
   git rev-parse 2.0.0
   gh release view 2.0.0 --json targetCommitish
   ```
3. Confirm the preview-only families sorted above every previously published
   preview for the same base version (spot-check at least one, e.g.
   XRegistry):
   ```powershell
   Invoke-RestMethod 'https://api.nuget.org/v3-flatcontainer/opcfoundation.netstandard.opc.ua.xregistry/index.json' |
       Select-Object -ExpandProperty versions | Select-Object -Last 5
   ```
4. Confirm the release branch was advanced to the next servicing preview
   version (the final step of [First stable release](#first-stable-release)/[Patch release](#patch-release)/[Minor release](#minor-release)):
   ```powershell
   git show origin/release/2.0:version.json
   ```
5. Confirm the stable container image aliases (if this release includes
   image-tagged samples - see [Container support](ContainerReferenceServer.md))
   now point at the released commit. For the newest line expect `latest`,
   `release`, `<major>.<minor>` and the exact version; for a superseded
   maintenance line expect `latest-<major>.<minor>`, `<major>.<minor>` and
   the exact version, with `latest`/`release` still on the newer minor (see
   [Branch and channel model](#branch-and-channel-model)).
6. Confirm every backport tracked for this release (see
   [Routine backport](#routine-backport)/[Emergency hotfix](#emergency-hotfix))
   shows as merged on every line it was required for.

*Completion evidence*: all of the above match; if any do not, treat it as a
[failed or partial release](#failed-or-partial-release) and escalate rather
than silently continuing.

## Release handoff template

Record this in the GitHub Release description, the tracking issue, or the
release PR - not in an untracked local note - whenever a release is prepared,
promoted, or handed off between people (or between an agent and a
maintainer).

```markdown
## Release handoff: <version>

- Release branch: release/<major>.<minor>
- Source commit: <sha>
- Candidate run: nuget-publish.yml run <run id>
- Promotion run: release.yml run <run id> (or: not yet promoted)
- Approvals: <who approved the release environment deployment>
- Backports included: <PR links, or "none required">
- Gates completed: [ ] candidate validated  [ ] dry run  [ ] promoted
                    [ ] tagged  [ ] GitHub Release published
                    [ ] next preview version PR opened
- Blockers: <none, or describe>
- Next allowed action: <e.g. "approve release.yml run 12345">
```

## Historical `release/2.0.0` branch

The three-component `release/2.0.0` branch predates this release model and is
retired: `version.json`'s `publicReleaseRefSpec` no longer matches that
naming, so it can never again produce a `channel: stable` package set, and
`nuget-publish.yml`/`release.yml` both refuse to publish from or promote it.
It is kept, unmodified, as historical record of the earlier `2.0.0-preview.*`
line; do not delete it, force-push to it, or attempt to bring it back into
the maintained-line model above. All current and future `2.0.x` work happens
on `release/2.0`.

## Maintaining this document

Update this file, and the worked examples above, in the same pull request as
any change to `version.json`, `version.props`, `version.targets`,
`preview-version.props`, `.azurepipelines/package-version-policy.ps1`,
`.azurepipelines/validate-nuget-package-set.ps1`,
`.azurepipelines/validate-preview-package-ordering.ps1`,
`.github/workflows/nuget-publish.yml`, `.github/workflows/release.yml`, or
the Docker/container tagging workflows. A release procedure that no longer
matches the scripts it describes is worse than no documentation at all.
