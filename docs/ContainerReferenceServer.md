# Container support and remote debugging for Reference Server

## Overview

There are multiple options to run the reference server in a Docker container:

- Latest and release builds from the GitHub container registry [here](https://github.com/OPCFoundation/UA-.NETStandard/pkgs/container/uanetstandard%2Frefserver). These builds support only Linux targets.
- Since [this](https://github.com/OPCFoundation/UA-.NETStandard/commit/61a61081f6060804b12b8f351e32a8075703263d) commit the container image has amd64 and arm64 support. The build of the sample has been moved to containers, no more need to install the .NET SDK.
- Local build without .NET 6.0 SDK on Linux or Windows with Docker Desktop. The target OS is chosen based on the settings in Docker Desktop for Linux or Windows containers.
- Although with VS 2019 and greater there is built in Container support, so far issues in the UA Reference solution prevent build/startup/connection (under investigation).
- VS2022 supports native debugging on a Linux distribution with WSL.

## Other published sample images

The `Docker Sample Images CI` workflow builds nine images at
`ghcr.io/opcfoundation/uanetstandard/<image>`. Each has `linux/amd64` and
`linux/arm64/v8` runnable subjects and keeps its existing version/`latest`
tagging scheme:

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

For example: `docker pull ghcr.io/opcfoundation/uanetstandard/ldsserver:latest`. Each image has a Dockerfile under its application folder that is built from the repository root as context (for example `docker build -f samples/Lds/ConsoleLdsServer/Dockerfile -t opcua-lds-server .`).

Pump is the **tenth, separately published image**:
`ghcr.io/opcfoundation/pumpdeviceintegrationserver`, built by
`pump-device-integration-server-docker.yml` from
`samples/DI/PumpDeviceIntegrationServer/Dockerfile`, for `linux/amd64` only.
There is no enrolled `uanetstandard/pumpserver` image. The two artifact groups
can publish independently; the catalog contains **19 runnable platform subjects**.

## Container release-evidence pilot

This is a repository-side **pilot checkpoint**, not a completed release gate.
The shared [release-evidence contract](ReleaseEvidence.md) remains authoritative;
no policy graduation or 1.5 pipeline rollout is performed here. Existing tags
and repository identities are unchanged. Baseline build/push failures still
fail. A cosign, collection or reconciliation failure emits an explicitly
`incomplete` status and is nonblocking for new pilot controls.

Both workflows request actual BuildKit `provenance: mode=min` and native SPDX
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
The upstream release, installer input and `sign`/`verify` flags were checked
against their public source. After reading back the complete graph, the pilot
signs the immutable root digest and every runnable manifest digest separately.
Verification requires the exact GitHub workflow certificate identity, GitHub
OIDC issuer, and signed source/workflow-definition SHA annotations. The signed
root binds its child and attestation descriptors; platform in-toto subjects
must name the corresponding runnable digest, not the wrapping index. An
attestation descriptor (`unknown/unknown`) is never counted as a platform.
Successful live cryptographic verification is recorded as
`verified-identity-boundary-pending`, **not** release authorization.

The Dockerfiles consume full version, assembly/file version, informational
version and source revision inputs, and label the output with the source and
version. Their relationship to NuGet is **same-source**, not
built-from-published-package. Base-image digest updates need reviewed
multi-platform availability; a tag is not an immutable base pin.

### Runner-local and offline helper

`.azurepipelines\container-evidence.ps1` has these operations:

| Operation | Behavior |
| --- | --- |
| `Preflight` | Reads the committed policy; refuses current-major, stable-looking official publication through the existing producer path in future `required` mode while isolated promotion remains inactive. No stage override exists. |
| `Record` | Saves actual checkout/workflow context and the build action's root digest; writes the tool's `images[{id,layout,rootDigest}]` request. |
| `Collect` | Reads GHCR manifests, configs, runnable layers and native attestation blobs by digest into a complete local OCI layout, checking every digest/size. |
| `VerifyLocal` | Checks local descriptor relationships, runnable platform membership, source/version labels and native predicate subjects; invokes the existing tool's `oci` command when available. |
| `Sign` | Attempts public identity-bound cosign signing and verification of the recorded root and runnable subjects, retaining a separate proof result per subject. |
| `Status` | Writes only fixed, sanitized fields and observed digests/platforms; never copies arbitrary capture fields or raw logs. |
| `Aggregate` | Reconciles only the selected group: nine images/eighteen runnable subjects for `containers`, or one/one for `pump`. Other workflow artifacts are not required for that group's eligibility. |
| `Assemble` | Invokes `oci-assemble` to retain native documents and create a group v2 companion; accepts an optional referrer context. Assembly is not eligibility. |
| `Evaluate` | Invokes the shared authenticated evaluator with the verification bundle and independently protected trust; the assessment remains runner-local. |

`Collect` and `Sign` require the official repository's authenticated
`push`/`workflow_dispatch` branch job, an approved branch pattern and a
runner-provided `GHCR_TOKEN`. They reject PR execution before network access.
No helper operation logs in, builds, pushes an image, changes tags, or changes
platform permissions. Registry download uses the distribution manifest/blob
endpoints, not `imagetools --raw` as a substitute for layer/config retrieval.
Redirected blob downloads do not forward Authorization. These registry paths
are workflow wiring only; they have not been executed as part of this checkpoint.

For an existing **offline** layout and context in the contract's documented
shape (use observed identities, not the synthetic documentation examples):

```powershell
$helper = '.\.azurepipelines\container-evidence.ps1'
& $helper -Operation VerifyLocal -Group pump -Image pumpdeviceintegrationserver `
  -Request .\staging\oci-inputs.json -Context .\staging\oci-context.json `
  -Version 2.0.0-preview.1 -Work .\staging\container-work `
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

Only `public\status.json` and `artifact-group-manifest.json` are uploaded.
Build records, full OCI layouts, raw predicates, tool logs and cosign output
remain runner-local and are not artifacts. Native BuildKit attestations remain
attached to their registry image; transitive public-payload review remains
unmet. Cross-workflow Pump/main observations are not silently combined as one
authenticated attempt, and absent/cancelled matrix results remain missing.
The aggregate manifest is a pilot observation index, **not a v2 evidence envelope**
or independently authenticated membership record. The separate `Assemble` and
`Evaluate` paths provide the versioned companion and authenticated assessment.
See [Release Evidence](ReleaseEvidence.md#authenticated-evaluation-and-independent-bootstrap)
for the protected trust boundary and complete native-document/referrer closure.

Helper exit codes are **0** for processed, nonblocking but incomplete evidence,
**1** for a required-stable refusal or recorded baseline failure, and **2** for
invalid input/collection failure. Read `status`, never infer completion from
exit zero or an Actions step conclusion. Workflow fallback statuses explicitly
say `unavailable`/`incomplete`, even if the helper itself cannot run.

### Validation and dormant enforcement

Pure PowerShell fixtures use synthetic OCI blobs and no registry, Docker,
credentials or .NET build:

```powershell
pwsh -NoProfile -File .\tests\Opc.Ua.Tools.Tests\ContainerEvidencePipeline.fixture.ps1 -Scenario valid-pump
```

Other cases: `wrong-digest`, `missing-platform`, `attestation-descriptor`,
`wrong-attestation-subject`, `private-path-filter`, `private-field-filter`, `pilot-incomplete`,
`required-stable`, `required-preview`, `baseline-failure`, `pr-no-publish`,
`catalog-membership`, `path-traversal`, `valid-dual-platform`, `record-request`,
`recorded-root-mismatch`, `required-four-part-version`, `required-context-version`, `aggregate-observed`.
The dedicated NUnit fixture is
`Opc.Ua.Tools.Tests.ContainerEvidencePipelineTests` (net10.0 only), for the
parent's batched .NET run; no .NET build/test is performed in this checkpoint.

**Still unmet:** authenticated producer/tool approval records, independently
verified provenance and complete inventories, all assurance profiles,
protected release intent/current policy, public retrieval/retention review,
and administrator-verified publisher isolation. Neither source-controlled
YAML nor an identity-bound signature establishes that ordinary/old workflows
and external Azure credentials cannot publish official tags.

Future required stable publication through the current producer path is refused
even if the boundary Boolean alone is changed. The separate dormant promoter
supports exact-content transfer, conditional aliases, append-only recovery records
and repeated eligibility verification, with an offline file transport for
exercises. The existing release controller's candidate jobs are literally disabled
and no official registry transport or candidate namespace is configured.

An eventual registry transport must prove exact manifests/layers and native/
signature referrer discoverability after copying, and enforce remote serialization
without cancelling active promotion. Matching immutable content is a no-op;
different content is a collision, not permission to overwrite. All current-line
official writers, including preview writers, need the isolated authority at
cutover while preview assurance remains advisory. Environment protection, grants,
production trust and real recovery/retrieval exercises remain administrator-owned
activation blockers; deferred 1.5 delivery must remain unaffected.

## Building the local containers

1. Open a command prompt which can execute docker commands.
2. Navigate to the folder `samples/Reference/ConsoleReferenceServer`.
3. Build the docker container by executing the command `dockerbuild.cmd`.

On Linux,

1. Open a shell which can execute docker commands.
2. Navigate to the folder `samples/Reference/ConsoleReferenceServer`.
3. Build the docker container by executing the command `./dockerbuild.sh`.

## Run the reference server container

The following samples run the server in interactive mode, hostname is the same as the host, the certificate store, the log output and the configuration file (see option `-s`) are mapped to a folder called `./OPC Foundation`.

the following defaults are used:

- the certificate store is mapped to './OPC Foundation/pki'
- A log file is created in './OPC Foundation/Logs'
- The shadow configuration file is created in './OPC Foundation/Quickstarts.ReferenceServer.Config.xml'

With the option `-s` the configuration file is first copied to the root of the mapped folders. In subsequent restarts the shadowed configuration file is used when the server is started and all settings can be changed from the mapped configuration file.

### Run the local build of the Docker container

To run the local containers, batch files are provided called `dockerrun.bat` for Windows and `dockerrun.sh` for Linux.

### Run the prebuilt Docker container hosted on Github

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
