# Container support and remote debugging for Reference Server

## Overview

Run the Reference Server in a Docker container using a published image or a
local build:

- Pull the latest or a release image from the [GitHub Container Registry](https://github.com/OPCFoundation/UA-.NETStandard/pkgs/container/uanetstandard%2Frefserver). These images support Linux targets.
- The image supports `amd64` and `arm64`. Local source builds run the SDK inside Docker; published images use the native `publish-samples` CI job described below.
- Build locally with Docker Desktop on Linux or Windows. Docker Desktop settings select the target container OS.
- Visual Studio 2019 and later include container support, but issues in the UA Reference solution currently prevent build, startup, or connection.
- Visual Studio 2022 supports native debugging of a Linux distribution through WSL.

## Contents

- [Overview](#overview)
- [Other published sample images](#other-published-sample-images)
- [Building the local containers](#building-the-local-containers)
- [Run the reference server container](#run-the-reference-server-container)
  - [Run the local build of the Docker container](#run-the-local-build-of-the-docker-container)
  - [Run the prebuilt Docker container hosted on GitHub](#run-the-prebuilt-docker-container-hosted-on-github)
- [Known limitations and issues](#known-limitations-and-issues)

## Other published sample images

The [`Images CI`](../.github/workflows/docker-image.yml) workflow builds
and publishes the sample images below to the GitHub Container Registry
(`ghcr.io/opcfoundation/uanetstandard/<image>`). It builds each image for
`linux/amd64` and `linux/arm64/v8`. Image identities, project paths, Dockerfiles,
platforms and independent evidence groups come from one
[release artifact catalog](../.azurepipelines/release/artifacts.json).

| Image | Sample application |
| --- | --- |
| `refserver` | `samples/Reference/ConsoleReferenceServer` |
| `ldsserver` | `samples/Lds/ConsoleLdsServer` |
| `boilerserver` | `samples/MinimalApi/MinimalBoilerServer` |
| `calcserver` | `samples/MinimalApi/MinimalCalcServer` |
| `mcpserver` | `tools/Opc.Ua.Mcp` |
| `redundantserver` | `samples/Redundancy/RedundantServer` |
| `redundantclient` | `samples/Redundancy/RedundantClient` |
| `redundantpubsub` | `samples/Redundancy/RedundantPubSub` |
| `pubsubclient` | `samples/PubSub/ConsoleReferencePubSubClient` |
| `pumpserver` | `samples/DI/PumpDeviceIntegrationServer` |

The workflow does not compile anything under emulation. Its
`publish-samples` job publishes every sample above once, natively, as
framework-dependent portable IL (no runtime identifier and no app host). The
same output then runs on both platforms. Each application has its own
`artifacts/docker/publish/<image>` directory. Each `build-and-push-image` leg
downloads the immutable artifact ID returned by the native job, checks its
source repository, commit, originating run/attempt, version metadata and
selected payload hashes, and builds its Dockerfile with
`--build-arg PUBLISH_SOURCE=prebuilt --build-arg PUBLISH_DIR=artifacts/docker/publish/<image>`.
The target-platform stages only copy files onto their .NET runtime base image,
so QEMU is not needed. The transfer manifest is not an independent provenance
attestation or publication authorization.

The ten catalog Dockerfiles use immutable version-and-digest pins for the
.NET `10.0.401` SDK and `10.0.12` runtime or ASP.NET bases. Pump uses the
Azure Linux 3 variants and prepares its OpenSSL configuration and writable
`/app` and `/diag` directories in a `$BUILDPLATFORM` layout stage. Its final
stage copies the parent directory tree, retaining ownership and group-write
modes without running target-architecture commands.

A local `docker build` without these arguments compiles from source in the
Dockerfile's `build` stage. That stage is pinned to `$BUILDPLATFORM`, so a
multi-platform `docker buildx build --platform linux/amd64,linux/arm64/v8` also
compiles natively.

Image tags follow the [release-branch-only publication model](ReleaseProcess.md):

- **Stable tags** include `<image>:latest`, `<image>:release`, and version
  tags such as `<major>.<minor>` and `<major>.<minor>.<patch>`. Update them
  only from a stable commit on a canonical `release/<major>.<minor>` branch
  (see [Release process](ReleaseProcess.md)). A preview build (`-preview.N`)
  does **not** move these tags.
- **`<image>:latest-<branch>`** (for example `refserver:latest-master`) tracks the most recent development build on that branch. `Images CI` builds these from `master` and from `release/*` branches while they are pre-release.
- **`<image>:<version>[-<branch>]`** identifies the source-derived NBGV version
  used for the assemblies and image metadata. Preview/development builds
  retain the branch suffix; stable release builds use the unqualified version.
  SemVer build metadata is removed for Docker tag compatibility.

For example, pull the most recently approved stable Lds server image with:

```sh
docker pull ghcr.io/opcfoundation/uanetstandard/ldsserver:latest
```

Pull the most recent development build with:

```sh
docker pull ghcr.io/opcfoundation/uanetstandard/ldsserver:latest-master
```

Each application folder contains a Dockerfile. Build it from the repository
root so the build can use the repository as context, for example:

```sh
docker build -f samples/Lds/ConsoleLdsServer/Dockerfile -t opcua-lds-server .
```

Pump is the **tenth image**, in the independent `pump` group:
`ghcr.io/opcfoundation/uanetstandard/pumpserver`, built by the same
`docker-image.yml` workflow from
`samples/DI/PumpDeviceIntegrationServer/Dockerfile`, for both `linux/amd64` and
`linux/arm64/v8`. The two evidence groups can be promoted independently;
the catalog contains **20 runnable platform subjects**.

Master, release/docker pushes and manual dispatch select both groups. Pull requests
validate all ten images without publishing. Pump uses the same version, branch and
release-line alias rules as the other images. Per-image jobs serialize overlapping manual and automatic runs,
and each group has its own status artifacts and membership manifest.

## Container release evidence

The shared [release-evidence contract](ReleaseEvidence.md) is **active** with
`stage: required`: unmet controls block in-scope stable major-2 publication.
Preview/development are release channels with advisory evidence applicability,
not contract maturity modes. Registry identities and tag formats remain
group-specific.
Baseline build/push failures still fail. A cosign, collection or reconciliation
failure remains explicitly incomplete and cannot satisfy required stable gates.

The shared workflow requests actual BuildKit `provenance: mode=min` and native SPDX
SBOM attestations for each runnable platform. Minimum provenance intentionally
avoids maximum-detail build arguments. The scanner is
`docker.io/docker/buildkit-syft-scanner:1.12.0`, selected by immutable index
digest `sha256:ae4f3b554449e7e25548e7d8ccc029d17357348e30c6e3df01b92bc93654d6a9`.
On 8 September 2026 the public Docker registry returned that digest and its
exact response bytes hashed to the same value; its index includes Linux amd64
and arm64. This lookup is not a successful container publication or a protected
tool-approval record. BuildKit SBOM generation is part of the build invocation;
there is no unsigned/no-SBOM retry masking a failed build.

Cosign **3.1.3** is installed through
`sigstore/cosign-installer@faadad0cce49287aee09b3a48701e75088a2c6ad` (v4.0.0).
After reading back the complete graph, the adapter signs the immutable root
digest and every runnable manifest digest separately.
Verification requires the exact GitHub workflow certificate identity, GitHub
OIDC issuer, and signed source/workflow-definition SHA annotations. The signed
root binds its child and attestation descriptors; platform in-toto subjects
must name the corresponding runnable digest, not the wrapping index. An
attestation descriptor (`unknown/unknown`) is never counted as a platform.
Successful live cryptographic verification is recorded as
`verified-identity-boundary-pending`, **not** release authorization.

The native publisher and local Dockerfile builds use matching version,
numeric assembly/file version, informational version, source revision and
repository URL inputs. The final images retain the source and version labels
even when their assemblies come from the prebuilt artifact. Their relationship to NuGet is **same-source**, not
built-from-published-package. Base-image digest updates need reviewed
multi-platform availability; a tag is not an immutable base pin.

The published-image Dockerfiles pin SDK `10.0.401` to match `global.json`, with
runtime/ASP.NET `10.0.12` bases. An SDK update also updates the corresponding
multi-platform image digests; changing `global.json` alone leaves digest-pinned
builders on the old SDK. The image SDK regression checks every catalog Dockerfile,
and digest updates are verified against the registry configuration for amd64 and arm64.

### Runner-local and offline helper

`.azurepipelines\containers\evidence.ps1` has these operations:

| Operation | Behavior |
| --- | --- |
| `Preflight` | Reads the active `required` policy; refuses current-major, stable-looking official publication through the existing producer path because isolated promotion is not configured. No stage override exists. |
| `Record` | Saves actual checkout/workflow context and the build action's root digest; writes the tool's `images[{id,layout,rootDigest}]` request. |
| `Collect` | Reads GHCR manifests, configs, runnable layers and native attestation blobs by digest into a complete local OCI layout, checking every digest/size. |
| `VerifyLocal` | Checks local descriptor relationships, runnable platform membership, source/version labels and native predicate subjects; invokes the existing tool's `oci` command when available. |
| `Sign` | Attempts public identity-bound cosign signing and verification of the recorded root and runnable subjects, retaining a separate proof result per subject. |
| `Status` | Writes only fixed, sanitized fields and observed digests/platforms; never copies arbitrary capture fields or raw logs. |
| `Aggregate` | Reconciles only the selected group: nine images/eighteen runnable subjects for `containers`, or one image/two runnable subjects for `pump`. Other workflow artifacts are not required for that group's eligibility. |
| `Assemble` | Invokes `oci-assemble` to retain native documents and create a group v2 companion; accepts an optional referrer context. Assembly is not eligibility. |
| `Evaluate` | Invokes the shared authenticated evaluator with the verification bundle and independently protected trust; the assessment remains runner-local. |

`Collect` and `Sign` require the official repository's authenticated
`push`/`workflow_dispatch` branch job, an approved branch pattern and a
runner-provided `GHCR_TOKEN`. They reject PR execution before network access.
No helper operation logs in, builds, pushes an image, changes tags, or changes
platform permissions. Registry download uses the distribution manifest/blob
endpoints, not `imagetools --raw` as a substitute for layer/config retrieval.
Redirected blob downloads do not forward Authorization. Workflow wiring and
offline fixtures do not establish production registry execution or publisher
isolation. Actual source-bound verification and retrieval records are required
operating evidence.

For an existing **offline** layout and context in the contract's documented
shape (use observed identities, not the synthetic documentation examples):

```powershell
$helper = '.\.azurepipelines\containers\evidence.ps1'
& $helper -Operation VerifyLocal -Group pump -Image pumpserver `
  -Request .\staging\oci-inputs.json -Context .\staging\oci-context.json `
  -Version 2.0.0 -Work .\staging\container-work `
  -Tool .\tools\Opc.Ua.ReleaseEvidence\bin\Release\net10.0\Opc.Ua.ReleaseEvidence.dll `
  -Output .\staging\public\status.json
```

The invoked tool command is exactly:

```powershell
dotnet $tool oci --repository-root $repositoryRoot --request $request `
  --context $context --output $ociResult
```

The diagnostic `oci` command cannot be made eligible by a `--verify-crypto`
or `--all-assurance` switch. A missing tool is explicitly reported; local byte
checks cannot replace it. A per-image main-workflow report also cannot establish
the other eight images' membership. Raw native SPDX/in-toto bytes stay unchanged
in `layout\blobs\sha256`; no lossy format conversion is performed.

The native validator freezes **SPDX 2.3** and verifies the exact official schema
bytes through an isolated offline registry. It reconciles files and package
ownership against the final filesystem after bounded layer/whiteout processing.
Missing ownership or unclaimed payload remains incomplete. Native SLSA source,
builder, invocation, materials, Dockerfile and scanner checks use independently
authenticated expectations, not signed self-annotations alone.

The short-lived `docker-publish` artifact contains only the native application
outputs and their transfer manifest. Evidence uploads remain restricted to
`public\status.json`, `artifact-group-manifest.json` and explicit per-subject
public signature bundles. Build records, full OCI layouts, raw predicates,
tool logs and raw cosign output remain runner-local and are not artifacts. Native BuildKit attestations remain
attached to their registry image; transitive public-payload review remains
unmet. Cross-workflow Pump/main observations are not silently combined as one
authenticated attempt, and absent/cancelled matrix results remain missing.
The aggregate manifest is an observation index, **not a v2 evidence envelope**
or independently authenticated membership record. The separate `Assemble` and
`Evaluate` paths provide the versioned companion and authenticated assessment.
See [Release Evidence](ReleaseEvidence.md#authenticated-evaluation-and-independent-bootstrap)
for the protected trust boundary and complete native-document/referrer closure.

Helper exit codes are **1** for required-stable refusals or recorded baseline
failures, and **2** for invalid input/collection failure. `Aggregate` and `Assemble`
can return **0** after writing incomplete evidence; that is not an eligibility
decision. Authenticated `Evaluate` returns **0** for a complete assessment or
advisory incomplete preview/development evidence. Read `status`, never infer
completion from exit zero or an Actions step conclusion. The stable example
above does not provide the independent trust, assurance or boundary records
required for publication. Workflow fallback statuses explicitly say
`unavailable`/`incomplete`, even if the helper itself cannot run.

### Offline fixtures and production prerequisites

Pure PowerShell fixtures use synthetic OCI blobs and no registry, Docker,
credentials or .NET build:

```powershell
pwsh -NoProfile -File .\tests\Opc.Ua.Tools.Tests\Fixtures\ContainerEvidencePipeline.fixture.ps1 -Scenario required-stable
```

Other cases: `valid-pump`, `wrong-digest`, `missing-platform`, `attestation-descriptor`,
`wrong-attestation-subject`, `private-path-filter`, `private-field-filter`,
`active-incomplete`, `required-preview`, `baseline-failure`, `pr-no-publish`,
`catalog-membership`, `path-traversal`, `valid-dual-platform`, `record-request`,
`recorded-root-mismatch`, `required-four-part-version`, `required-context-version`, `aggregate-observed`.
The dedicated NUnit fixture is
`Opc.Ua.Tools.Tests.ContainerEvidencePipelineTests` (net10.0 only). These fixtures
cover local behavior and required stable refusals, not production qualification.

The workflow fixtures check all ten catalog project/Dockerfile pairs, immutable
pins, native stage selection, source-bound payload transfer and selected-job
summary failures. Native publication uses synthetic `dotnet` output; no sample
build or registry access occurs:

```powershell
pwsh -NoProfile -File .\tests\Opc.Ua.Tools.Tests\Fixtures\ContainerWorkflow.fixture.ps1 -Scenario native-publish
pwsh -NoProfile -File .\tests\Opc.Ua.Tools.Tests\Fixtures\ContainerWorkflow.fixture.ps1 -Scenario docker-stages
pwsh -NoProfile -File .\tests\Opc.Ua.Tools.Tests\Fixtures\ContainerWorkflow.fixture.ps1 -Scenario workflow-wiring
```

Tag-guard cases execute the workflow's Bash scripts with synthetic release refs
(Git Bash on Windows). A passing fixture does not establish a successful native
sample publish, multi-platform Docker build or hosted workflow run.

In the [configured release process](ReleaseEvidence.md#configured-release-workflow),
the controller authenticates producer/tool approval, provenance, complete
inventories and assurance, release intent, the current policy, public-data review
and publisher isolation. Missing or invalid records block stable publication;
neither source-controlled YAML nor a boundary Boolean substitutes for those checks.

The isolated writer transfers exact content, applies conditional aliases, keeps
append-only recovery records and repeats eligibility verification. Production
registry transport verifies manifest/layer and native/signature-referrer
discoverability after copying, with remote serialization that does not cancel
active promotion. Matching immutable content is a no-op; different content is a
collision, not permission to overwrite. Preview writers use the same isolated
authority while preview/development evidence retains its advisory applicability.

Initial transport integration, candidate namespaces, protected environments,
grants, independent trust and recovery/retrieval qualification are tracked in the
[administrator setup checklist](../plans/ReleaseEvidenceAdministration.md).
The offline file transport remains an exercise adapter, not a production registry.

## Building the local containers

1. Open a command prompt which can execute docker commands.
2. Navigate to the folder `samples/Reference/ConsoleReferenceServer`.
3. Build the docker container by executing the command `dockerbuild.cmd`.

On Linux,

1. Open a shell which can execute docker commands.
2. Navigate to the folder `samples/Reference/ConsoleReferenceServer`.
3. Build the docker container by executing the command `./dockerbuild.sh`.

## Run the reference server container

Run the container in interactive mode with the host's hostname. The
certificate store, log output, and configuration file (when you use `-s`)
are mounted in `./OPC Foundation`.

The container uses these paths by default:

- Certificate store: `./OPC Foundation/pki`
- Log file: `./OPC Foundation/Logs`
- Shadow configuration: `./OPC Foundation/Quickstarts.ReferenceServer.Config.xml`

With `-s`, the container first copies the configuration file to the mounted
folder. On later restarts, the server reads this copy, so you can change the
settings there.

### Run the local build of the Docker container

To run the local containers, batch files are provided called `dockerrun.bat` for Windows and `dockerrun.sh` for Linux.

### Run the prebuilt Docker container hosted on GitHub

On Windows, open a command prompt and execute the following commands:

```cmd
docker pull ghcr.io/opcfoundation/uanetstandard/refserver:latest
docker run -it -p 62541:62541 -h %COMPUTERNAME% -v "%CD%/OPC Foundation:/root/.local/share/OPC Foundation" ghcr.io/opcfoundation/uanetstandard/refserver:latest -c -s
```

On Linux, execute the following commands in a shell:

```bash
sudo docker pull ghcr.io/opcfoundation/uanetstandard/refserver:latest
sudo docker run -it -p 62541:62541 -h $HOSTNAME -v "$(pwd)/OPC Foundation:/root/.local/share/OPC Foundation" ghcr.io/opcfoundation/uanetstandard/refserver:latest -c -s
```

## Known limitations and issues

- VS integrated docker build/debug support is not working with the Solution.
