# Managed OneFuzz publication

`OneFuzz=true` changes each existing fuzz host into a `net10.0` class library.
The host compiles its normal target sources, but neither linked `Program.cs` nor
`FuzzMethods.cs`, and has no direct SharpFuzz reference. Default local executable
publication is unchanged. The service drop is **not preinstrumented**; the internal
service owns instrumentation and its actual version must be recorded by the canary owner.

## Public build and relocation gate

From the repository root, invoke the script with PowerShell 7:

```powershell
.\fuzzing\Scripts\build-onefuzz.ps1 `
    -Areas Encoders `
    -OutputDirectory 'C:\temp\opcua-onefuzz-drop' `
    -WorkDirectory 'C:\temp\opcua-onefuzz-build'
```

Omit `-Areas` to require every manifest area. Output and work directories must not
already exist. Paths inside the script are independent of the caller's working directory.
No directory is deleted or reused. All project outputs **and restore
assets**, including project references, use the SDK's isolated `--artifacts-path`;
passing only `OutputPath` or a shared `BaseIntermediateOutputPath` is insufficient.
Direct publication must supply `-p:OneFuzz=true -p:CustomTestTarget=net10.0`,
`-f net10.0`, and `--artifacts-path` too.
Also pass `-p:FuzzCoverage=true`, as the script does, to make generated protocol
serialization code visible to instrumentation. Ordinary builds keep their existing
coverage exclusions; no collector binary or compiled assembly is rewritten to remove them.

The script merges actual publish outputs, rejects conflicting files, preserves the
manifest-relative corpus/dictionary layout, relocates the payload, and invokes the
published callbacks from that relocated directory. It does not generate a partial
target list from whichever callbacks happened to build. A missing declared target,
unexpected exported span callback, missing required bucket, empty corpus, absent
dictionary, or incomplete dependency closure is a failure.

`-CorpusRoot` explicitly selects a different root for **all** selected corpus paths.
It must contain their complete repository-relative layout; there is no silent fallback
to checked-in inputs. Dictionaries still come from the repository. Without this override,
the publisher runs the selected manifest's declared generators in the isolated work directory
and requires every generated output bucket to be nonempty. The Certificates generator creates
positive certificate/chain/CRL fixtures and checks them through the real parser/oracle cores;
no new certificates or keys are added to source control. Only declared output buckets enter
the drop, and `build.json` records their actual SHA-256 inventories.
For example, a certificate corpus under an external root still lives at
`<CorpusRoot>\fuzzing\Opc.Ua.Certificates.Fuzz.Corpus\...`, not directly at that root.
When an external root is supplied, publication copies the declared inputs and records their
actual hashes without labeling them freshly generated or promising deterministic
cryptographic fixture bytes. This is the external-corpus alternative to per-target
profile overrides; ownership profiles remain ownership/routing metadata only.

The drop contains the selected snapshot of `fuzz-targets.json`, flat managed DLL
closure with native/runtime subdirectories as published, `.deps.json`,
`.runtimeconfig.json`, matching portable PDBs, corpora, dictionaries, `build.json`,
`validation.json`, `publication.json`, and `files.sha256`. A failed build does not write
`publication.json`. Work directories retain per-target completion receipts and logs.
The original manifest hash and source revision/dirty state are recorded. The checksum
inventory covers all final files except the inventory itself.

Revalidate a copied drop independently:

```powershell
.\fuzzing\Scripts\validate-onefuzz.ps1 `
    -DropDirectory 'C:\temp\copied-onefuzz-drop'
```

The BCL-only validator may also be published separately and passed with
`-ValidatorPath`. It never resolves application dependencies from the checkout or a
NuGet cache. Each exact `public static void(ReadOnlySpan<byte>)` callback runs in its
own process under the published host's runtime configuration, with a hard watchdog.
The default 120-second budget covers a target's entire seed replay, including child
startup; override it explicitly with `-TimeoutSeconds`. A successful exit without a
complete, hash-matched input receipt is a failure. There are no skip switches.

## Explicit service configuration

Public builds need no ownership profile and emit **no `OneFuzzConfig.json`**.
For an internally owned drop, pass `-OwnershipProfile` pointing to a protected file
matching [ownership-profile.schema.json](ownership-profile.schema.json). Do not place
credentials in that file: it carries non-secret ownership/routing metadata only.

The profile must explicitly provide the intended `azurelinux3`/`net10.0` worker
architecture and service instrumentation, notification email, SDL work item,
complete ADO ownership/template and coverage-pipeline binding, and exactly one
target record per selected manifest target. Each record supplies its `ProjectName`,
`TargetName`, and provisioned, stack-owned `SeedCorpusContainer`. There are no
default emails, IDs, project names, or containers. The private profile is not copied
into the drop; only its hash and the required final service configuration are retained.
Including seeds in `JobDependencies` does not populate that cloud container. The
internal owner must provision/seed it through the authorized service mechanism;
publication never uploads to or overwrites an existing corpus.

The generator emits the reference integration's `ConfigVersion: 3` contract:
`Fuzzer.$type = libfuzzerDotNet`, exact `Dll` / `Class` / `Method`, `Skip: false`,
`MinAvailableMemoryMB: 100`, and `FuzzerTimeoutInSeconds: 120`. The memory field is
**not** an RSS cap. `JobDependencies` explicitly lists real closure/seed/dictionary
files rather than guessed package-name globs. Multiple dictionaries are merged
deterministically into one `-dict={setup_dir}/...` option without dropping tokens.
The generated configuration is checked against actual published callback discovery
before any complete drop is reported.

For an unfinalized published payload, the low-level generator is also available as:

```powershell
dotnet Opc.Ua.OneFuzz.Validator.dll configure `
    --drop C:\temp\opcua-onefuzz-drop --profile C:\protected\onefuzz-profile.json
```

It refuses to replace an existing configuration or modify a completed drop carrying
`publication.json`, whose hashes and evidence would become stale. Use the build
script with `-OwnershipProfile` for a finalized service drop. The build script
validates the generated configuration before publishing completion evidence.

## Solution and CI integration

`UA.slnx` includes these support projects:

- `fuzzing\OneFuzz\Opc.Ua.OneFuzz.Validator\Opc.Ua.OneFuzz.Validator.csproj`
- `fuzzing\OneFuzz\Opc.Ua.OneFuzz.Validator.Tests\Opc.Ua.OneFuzz.Validator.Tests.csproj`
- `fuzzing\OneFuzz\Opc.Ua.OneFuzz.TestTarget\Opc.Ua.OneFuzz.TestTarget.csproj`

The last project is a **synthetic negative-control fixture, never a service target**.
The GitHub public drop gate runs the NUnit contracts on `net10.0` and publishes a drop with
the scripts above. Manifest, corpus, dictionary, host, shared harness, OneFuzz support and
publication-script changes trigger this gate. Zero failed/skipped cases are required;
the retained drop and local evidence do not constitute a service canary.

[.azurepipelines\onefuzz.yml](../../.azurepipelines/onefuzz.yml) is opt-in: no PR,
branch, or scheduled triggers. Its default builds/tests only. An authorized internal
owner can explicitly set `submit: true`, provide the ownership profile/corpora,
install and authorize `onefuzz-task@0`, and supply the task's secret variables.
Use the explicit `ownershipProfileSecureFile` parameter to download an authorized
Azure secure file without committing private ownership metadata. Alternatively,
provide `ownershipProfilePath` for a file made available by the owner's
`prepareSteps`. Those optional steps can prepare a complete external `corpusRoot`;
without that override, publication materializes declared fixture generators automatically.
The task uses `onefuzzOSes: azurelinux3` and `onefuzzDropDirectory`. Its Ubuntu
build agent is **not** evidence of the resolved Azure Linux worker image.

No script submits or uploads anything. Build/publication checks do not establish
the hidden service instrumenter's compatibility, provision cloud corpora, verify
worker architecture/image selection, or prove a successful internal canary.
Those remain service-owner prerequisites, with actual execution/coverage evidence
required by the approved parity plan.

## Separate local driver argument contract

The local SharpFuzz runner uses `Metalnem/libfuzzer-dotnet`, not the internal service
runner. In release `v2025.05.02.0904`, both its
[Unix parser](https://github.com/Metalnem/libfuzzer-dotnet/blob/v2025.05.02.0904/libfuzzer-dotnet.cc)
and [Windows parser](https://github.com/Metalnem/libfuzzer-dotnet/blob/v2025.05.02.0904/libfuzzer-dotnet-windows.cc)
retain only the **first nonempty `--target_arg=...`**. Repeating that flag does not
construct an argument list. Unix passes that value as a single `execlp` argument;
Windows concatenates that one value into the child command line.

The portable local adapter is a published executable apphost passed as
`--target_path=...`, with one `--target_arg=<callback>` value. Do not attempt to pass
`dotnet`, a DLL, and a callback using repeated `--target_arg` flags. This local
driver contract does not establish the internal OneFuzz instrumenter's behavior.
