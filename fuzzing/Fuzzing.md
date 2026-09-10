# Fuzz testing for UA.NET Standard

This project provides integration of [SharpFuzz](https://github.com/Metalnem/sharpfuzz) with the
UA.NET Standard libraries, with support for both [afl-fuzz](https://lcamtuf.coredump.cx/afl/) and
[libFuzzer](https://llvm.org/docs/LibFuzzer.html). Each fuzz area lives directly under `fuzzing/`
as four sibling folders — host (`*.Fuzz`), corpus (`*.Fuzz.Corpus`), test fixture (`*.Fuzz.Tests`),
and tools (`*.Fuzz.Tools`) — and shares a generic NUnit replay harness plus the SharpFuzz host
under `Common/`.

## Areas

| Area | Surface | Status |
|------|---------|--------|
| `Opc.Ua.Encoders.Fuzz` | Contiguous/segmented binary, JSON/XML round-trips, binary/XML-to-JSON conversions, built-in type readers and string parsers | shipped |
| `Opc.Ua.Certificates.Fuzz` | Certificate loading, default/ASN.1 chain parsing, signed CRL decoding and canonical TBS encoding, X509 extensions, PEM, PKCS#10 and ASN.1 helpers | shipped |
| `Opc.Ua.Network.Fuzz` | Offline TCP/UA-SC framing, reassembly and `TcpMessageParsers`; the secure-channel fixture uses SecurityPolicy None | shipped |
| `Opc.Ua.PubSub.Fuzz` | Metadata-backed JSON, UADP decoding and chunk reassembly | modern .NET |

`Encoders` and `Certificates` build for the repo's standard `TestsTargetFrameworks` matrix
(`net48`, `net8.0`, `net9.0`, `net10.0`). `Network` and `PubSub` are modern .NET only;
`Network` uses `net8.0;net9.0;net10.0` because
`Opc.Ua.Core.Diagnostics` does not target .NET Framework.

## Directory layout

```
fuzzing/
  Fuzzing.md                                                # this file

  Common/                                                   # shared source (no csproj)
    Fuzz/
      Program.cs, FuzzMethods.cs                            # SharpFuzz host
    Fuzz.Tests/
      FuzzTargetTestsBase.cs, FuzzTargetFunction.cs
      TestcaseAsset.cs, TestAssetUtils.cs                   # generic NUnit harness
    Fuzz.Tools/
      Program.cs, Playback.cs, Logging.cs, Testcases.cs     # Tools host

  Dictionaries/                                             # libFuzzer / afl dictionaries
    asn1.dict  binary.dict  json.dict  nodeid.dict
    tcp.dict   uasc.dict    xml.dict

  Scripts/                                                  # area-agnostic runners
    fuzz-afl.ps1  fuzz-libfuzzer.ps1  fuzz-menu.ps1
    install.sh  readme.txt

  Opc.Ua.Encoders.Fuzz/                                     # csproj + FuzzableCode partials
  Opc.Ua.Encoders.Fuzz.Corpus/                              # seed corpus (Testcases.*/)
  Opc.Ua.Encoders.Fuzz.Tests/                               # deterministic NUnit replay
  Opc.Ua.Encoders.Fuzz.Tools/                               # corpus generator + playback

  Opc.Ua.Certificates.Fuzz/   Opc.Ua.Certificates.Fuzz.Corpus/
  Opc.Ua.Certificates.Fuzz.Tests/   Opc.Ua.Certificates.Fuzz.Tools/

  Opc.Ua.Network.Fuzz/   Opc.Ua.Network.Fuzz.Corpus/
  Opc.Ua.Network.Fuzz.Tests/   Opc.Ua.Network.Fuzz.Tools/

  Opc.Ua.PubSub.Fuzz/   Opc.Ua.PubSub.Fuzz.Corpus/
  Opc.Ua.PubSub.Fuzz.Tests/   Opc.Ua.PubSub.Fuzz.Tools/

  Opc.Ua.Fuzzing.Tests/                                   # shared harness negative controls
  OneFuzz/                                               # publication and callback validation
  fuzz-targets.json                                       # behavior/corpus/oracle inventory
  fuzz-parity.runsettings                                 # fuzz-only production coverage
```

The flat layout matches the rest of the repository (`tests/`, `src/`, `src/` also list
projects directly under the root with no domain grouping). Every csproj folder name equals the
csproj filename (without the `.csproj` extension), and every assembly is prefixed with `Opc.Ua.`
in line with the repo's `AssemblyPrefix` convention.

Each area's `*.Fuzz` project hosts `FuzzableCode.*.cs` partials whose `public static` methods
with a single parameter of type `Stream` (afl), `string` (afl), or `ReadOnlySpan<byte>`
(libFuzzer), returning `void` and without generic parameters, are auto-discovered by:

* the SharpFuzz host (`Common/Fuzz/Program.cs`) at fuzz-run time, and
* the generic NUnit harness (`Common/Fuzz.Tests/FuzzTargetTestsBase.cs`) at test time —
  every target is replayed against the area's `*.Fuzz.Corpus/Testcases.*/` corpus plus
  crash/timeout assets, with no per-area test code required.

Each area's `*.Fuzz.Tests` is therefore a single thin subclass:

```csharp
[TestFixture]
[Category("Fuzzing")]
public class EncoderTests : FuzzTargetTestsBase
{
    [DatapointSource]
    public static readonly FuzzTargetFunction[] FuzzableFunctions =
        CreateFuzzTargetFunctions(typeof(FuzzableCode));

    protected override Type FuzzableCodeType => typeof(FuzzableCode);
}
```

## How to add a new fuzz area

1. **Pick a surface.** Anything that takes untrusted input (bytes, string, or stream) is a
   candidate. Prefer surfaces with deterministic output and bounded resource usage.
2. **Copy the four `Opc.Ua.Encoders.Fuzz*` folders** to `Opc.Ua.<NewArea>.Fuzz*`. Rename the
   three csproj files and update `<AssemblyName>` (keep the `Opc.Ua.` prefix) and
   `<RootNamespace>`.
3. **Replace `FuzzableCode.*.cs`** with your area's targets. Naming convention:
   `Aflfuzz<Target>(Stream|string)` for afl-fuzz and `Libfuzz<Target>(ReadOnlySpan<byte>)`
   for libFuzzer. Each target must define a narrow, parser-specific malformed-input policy.
   Do not use a generic exception allowlist: an `ArgumentException` can also be a programming
   error. Inspect wrapped exceptions before accepting a status code, and never suppress a
   failed oracle or an exception while decoding output the target itself just generated.
4. **Subclass the harness.** `Opc.Ua.<NewArea>.Fuzz.Tests/<NewArea>Tests.cs` inherits from
   `FuzzTargetTestsBase`, sets `FuzzableCodeType => typeof(FuzzableCode)`, and exposes the
   `[DatapointSource]` `FuzzableFunctions` field with
   `CreateFuzzTargetFunctions(typeof(FuzzableCode))`.
5. **Generate seeds.** Add `<NewArea>.Testcases.cs` to `Opc.Ua.<NewArea>.Fuzz.Tools/` that
   builds valid sample inputs through the real producer code and writes them under
   `Opc.Ua.<NewArea>.Fuzz.Corpus/Testcases.<Bucket>/`. Each `Testcases.*/` subdirectory becomes
   a logical seed bucket and is auto-discovered by
   `TestAssetUtils.DiscoverTestcaseEncoderSuffixes`.
6. **Add a dictionary.** Put `<NewArea>.dict` under `fuzzing/Dictionaries/` with common
   tokens / magic bytes / length sentinels — this dramatically improves libFuzzer coverage
   progression. Every line must be a loadable libFuzzer entry (`name="token"` or `"token"`)
   with a nonempty token and escaped inner quotes. libFuzzer aborts the entire campaign on a
   single malformed line, so an unloadable dictionary silently disables every target that
   declares it; `validate-fuzz-manifest.ps1` enforces this. Dictionaries are optional per target.
7. **Wire into `UA.slnx`.** Add the three new projects to the `/fuzzing/` folder, the
   `*.dict` file under `/fuzzing/Dictionaries/`, and the seed loose files you want visible in
   the IDE.
8. **Register the target.** Update `fuzz-targets.json` with its adapters, positive corpora,
   dictionaries, oracle and fork behavior mapping. Add success-path assertions in addition
   to cross-target malformed-input replay. Validate both the local host and published callback.
9. **Update this `Fuzzing.md`.** Add a row to the area table.

The Azure pipeline test template recursively discovers fuzz test projects. GitHub Actions
also has an explicit fuzz replay matrix, including changes only to seeds, dictionaries and
scripts. `Scripts/test-fuzzing.ps1` runs the applicable projects and rejects empty or skipped
test runs. `fuzz-parity.runsettings` includes generated protocol methods in fuzz-only production
coverage; it does not change the repository-wide coverage policy.
Fuzz scripts pass `-p:FuzzCoverage=true` so generated protocol data types omit their
`ExcludeFromCodeCoverage` attributes in that build only. A runsettings include filter
cannot override those compiled attributes. Ordinary builds retain the exclusions.

## Areas in detail

### Encoder profiles and oracles

Binary, JSON and XML canonical targets retain exact re-encoded output checks and decoded-value
equality. Conversion targets use a known message type and explicit namespace context. RawData
conversion restores only omitted type artifacts from known metadata before typed decoding;
it does not repair payload values or claim arbitrary metadata-free JSON is reversible.

| Profile | Behavior |
|---|---|
| Verbose / Compact | Current supported options with semantic and canonical round-trip assertions |
| RawData | Current artifact-suppressed JSON, with metadata-backed decoding |
| LegacyReversible | Current Compact-based option profile exercising defaults and namespace indices |
| LegacyNonReversible | Alias of current RawData behavior; not additional independent production coverage |

The `Legacy*` names identify comparison profiles, **not legacy wire-format support**.
Retired object-form identifiers, old Type/Body envelopes and other unrepresented distinctions
remain partial mappings in the manifest. Do not declare them equivalent from their names or
count the RawData alias twice. Full parity requires resolving or proving those behavior mappings,
alongside matched-input production coverage and internal worker evidence.

Rich deterministic ReadRequest, ReadResponse, PublishResponse and WriteRequest fixtures exercise
multiple NodeId forms, namespaces, diagnostics, matrices, structured ExtensionObjects and arrays.
Existing seeds and original crash/timeout/slow reproducers are retained.
The 12 original fork message files are hash-pinned under `Assets/ForkMessages` and are
included in matching published-target corpora, without normalization or regeneration.
All four binary originals and the original XML requests have positive typed-decoding checks.
The pinned fork itself rejects the two response XML files' nested `ListOf` matrix encoding;
those files remain explicit rejection cases, not claimed successful-path coverage.
Original JSON TypeId/Body envelopes remain unsupported by the stack; replaying their expected
rejection does not close that compatibility gap.
Canonical binary (contiguous and segmented), JSON and XML targets also declare the three
historical regression inputs explicitly for published replay.

### Network / Transport area — `Opc.Ua.Core.Diagnostics` + Core UA-SC seam

The Network area is unusual because it's split across two complementary entry points:

* Fuzzes the public surfaces of
  `src/Opc.Ua.Core.Diagnostics`: `OpcUaFrameParser.Process` (TCP → UA-SC chunk splitter),
  `TcpStreamReassembler.Process` (raw TCP), `OfflineSecureChannel.ReadChunk` (UA-SC
  parsing with the existing **SecurityPolicy None** fixture), and `ServiceCallReassembler.Push`
  (chunk → service-call assembly with mixed sequence numbers, request ids, oversize bodies).
  Mock client/server construction is lifecycle coverage, not evidence of stateful replay,
  decryption, certificate validation or live network execution.

* Uses the public `TcpMessageParsers` in
  `src/Opc.Ua.Core/Stack/Tcp/TcpMessageParsers.cs` covering the pre-crypto, pre-auth chunk
  surface the pcap binding does not expose: `TryParseChunkHeader`, `ReadHelloMessage`,
  `ReadAcknowledgeMessage`, `ReadErrorMessage`, `ReadReverseHelloMessage`,
  `ReadAsymmetricMessageHeader`.

#### Seed-corpus and key-material discipline

`Network.Testcases.cs` and `Transport.Testcases.cs` generate seeds from an
in-process handshake (Hello → OPN → MSG (Read/Browse) → CLO) using test certificates and
the pcap binding's `LoopbackFrameBuilder` / `CapturingMessageSocketFactory`. Outputs:

* raw TCP segments → `Opc.Ua.Network.Fuzz.Corpus/Testcases.Tcp/`
* UA-SC chunks → `Opc.Ua.Network.Fuzz.Corpus/Testcases.Chunks/`,
  `Testcases.Tcp.Hello/`, `Testcases.Tcp.Ack/`, `Testcases.Tcp.Err/`, `Testcases.Tcp.Rhe/`,
  `Testcases.Tcp.AsymHdr/`
* paired `ChannelKeyMaterial` JSON → `Opc.Ua.Network.Fuzz.Corpus/Testcases.Keys/`

**Never commit real keylog material.** Seed corpora use only key material generated from
the existing fixture test certificates via the binding's own multi-TFM replay helpers.

#### Dependency hygiene

The Network fuzz host references `Opc.Ua.Core.Diagnostics` (which transitively pulls
PacketDotNet + SharpPcap) but **does not** instantiate `NicCaptureSource` so the
AFL/libFuzzer process never opens raw sockets.

## Installation

### Linux (afl-fuzz + libFuzzer)

Both fuzzers are supported on Linux. afl-fuzz can be compiled on any Linux system; for
libFuzzer prebuilt binaries are available for Debian / Ubuntu / Windows from the
[libfuzzer-dotnet releases](https://github.com/Metalnem/libfuzzer-dotnet/releases).

```bash
cd <repo>/fuzzing
sudo apt-get update
sudo apt-get install -y build-essential cmake git dotnet-sdk-10.0
# Powershell on Linux (required by the helper scripts):
# https://learn.microsoft.com/powershell/scripting/install/install-ubuntu
./Scripts/install.sh                    # builds afl-fuzz + installs SharpFuzz.CommandLine
```

`install.sh` downloads afl-2.52b, runs `make install`, then
`dotnet tool install --global SharpFuzz.CommandLine`. Validate with:

```bash
afl-fuzz --help
sharpfuzz
```

### Windows (libFuzzer via WSL or native)

Install the latest .NET 10 SDK / runtime, then:

```powershell
dotnet tool install --global SharpFuzz.CommandLine
```

For afl-fuzz, use WSL with the Linux instructions above.

## Running a fuzzer

The dynamic menu script lists every `FuzzableCode` static target in a built area assembly
without hardcoding target names:

```powershell
powershell -File fuzzing/Scripts/fuzz-menu.ps1 `
    -AssemblyPath fuzzing/Opc.Ua.Network.Fuzz/bin/Debug/net10.0/Opc.Ua.Network.Fuzz.dll
# -Filter <regex> narrows the list; -Index <n> selects a target without prompting.
```

`Scripts/fuzz-libfuzzer.ps1` and `Scripts/fuzz-afl.ps1` accept a `-fuzztarget` parameter
matching one of the listed names:

```powershell
cd fuzzing
powershell -File Scripts/fuzz-libfuzzer.ps1 `
    -libFuzzer ./libfuzzer-dotnet-windows.exe `
    -project ./Opc.Ua.Encoders.Fuzz/Opc.Ua.Encoders.Fuzz.csproj `
    -fuzztarget LibfuzzBinaryDecoder `
    -corpus ./Opc.Ua.Encoders.Fuzz.Corpus/Testcases.Binary/
```

The libFuzzer helper defaults to a bounded `-runs 10000` campaign and a per-input
`-timeout 120` seconds; supply an explicit budget for reproducible comparisons. Each invocation
uses a fresh temporary work directory and prints its location. Findings and mutated corpora are
retained there, not written over the checked-in seeds. Existing output directories are never
recursively cleared. Restore the selected host before running the helper.

The driver accepts exactly one target argument. The helper publishes an apphost and passes the
callback name to it; passing `"assembly.dll callback"` to `dotnet` is not portable to the Linux
driver. Local instrumentation covers actual `Opc.Ua.*.dll` modules, not NuGet package-name globs.

## Replay of crashes and timeouts

Run the area's `*.Fuzz.Tools` project with `-p` / `-s` for playback with stack traces:

```bash
dotnet run --project fuzzing/Opc.Ua.Network.Fuzz.Tools -- --playback --stacktrace --target LibfuzzTcpChunkHeader --input <input-directory>
```

`--input` is required and accepts a file, directory or glob. Missing/empty input sets, unknown
targets and failed callbacks return a failure. Without `--target`, playback attempts every
libFuzzer callback and aggregates failures rather than logging them as success.

The local host also supports `--list` and `--replay <target> <file-or-directory>` without starting
SharpFuzz. The NUnit slow/timeout regressions run in child processes with a 30-second watchdog;
`CancelAfter` alone cannot stop a synchronous callback. Tools playback itself is in-process,
so use the isolated replay gate for suspected hangs.

## Recreate or improve seeds

Run the area's `*.Fuzz.Tools` project with `-t` to (re)generate the area's `Testcases.*/`
seed corpus:

```bash
dotnet run --project fuzzing/Opc.Ua.Network.Fuzz.Tools -- --testcases --output <artifact-directory>/Testcases
```

Generators use the real producers. Freeze semantic inputs such as times, namespace tables and
identifiers when comparing runs, but do not claim newly generated cryptographic material is
byte-identical. Record the actual SHA-256 inventory. `--output` is a path prefix; generators
append format suffixes. Without it, output is under the executable directory, independent of CWD.
Never commit new certificates, private keys, operational captures or cloud corpora.

## OneFuzz publication

Create a public validation drop without internal credentials:

```powershell
.\fuzzing\Scripts\validate-fuzz-manifest.ps1 -SelfTest
.\fuzzing\Scripts\build-onefuzz.ps1 -OutputDirectory <new-drop-directory>
```

The publisher uses isolated SDK outputs, materializes declared generated corpora in controlled
artifacts, and replays the published callbacks. It never uploads or submits jobs. Without
`-OwnershipProfile <approved-profile.json>`, it does **not** emit a service-submittable
`OneFuzzConfig.json`. See `fuzzing/OneFuzz` and `.azurepipelines/onefuzz.yml` for the ownership
profile and internal pipeline contract. Existing output/work directories are rejected, not deleted.

The service mode is `OneFuzz=true`: managed net10.0 class libraries from the same source,
without the local runner or direct SharpFuzz dependency. Internal worker configuration uses
`azurelinux3` and `libfuzzerDotNet`. The build host OS is not evidence of the resolved worker
image. Keep service drops uninstrumented; instrument separate copies only for local campaigns.

The public GitHub drop job exercises publication without credentials. An internal canary must
also prove that every configured worker actually ran the intended callbacks and corpora, loaded
the production dependencies and produced meaningful coverage. Missing owner configuration,
authorized cloud-corpus snapshots or worker evidence blocks a full parity claim.

## Automation — `fuzz-tester` custom agent

The [fuzz-tester agent](../.github/agents/fuzz-tester.agent.md) uses the manifest to select
targets, required corpora and dictionaries. It preserves each reproducer and its target/oracle
association, fixes the root cause, then repeats regression and bounded campaign gates.
Test counts alone do not prove parity: retain positive-path assertions and compare corresponding
production methods/branches with matched input, runtime and mutation budgets.

Campaign processes remain attached to the session unless the user explicitly asks otherwise.
No agent runbook authorizes automatic commits, pushes, cloud uploads or modifications to a
reference fork. Internal OneFuzz execution requires separate authorized configuration and worker
execution evidence; a successful local publish or upload is not a successful canary.

## Bounded campaign results

Every declared continuous target has been driven with a bounded, reproducible budget
(1,000 inputs, fixed seed 1, 120-second per-input timeout, serial execution), after replaying
each target's full required corpus. Reproducers are retained as `Assets/crash-*` regression
inputs, so `FuzzCrashAssets` replays them through every target on each test run.

These campaigns found genuine defects that are now fixed, including unvalidated UADP length
arithmetic, encoder paths leaking third-party parser exceptions, JSON `ExtensionObject`
representations that silently lost a non-null type identifier, and a dictionary that
libFuzzer refused to load — which had silently prevented four parser targets from executing
at all. A finite campaign proves neither the absence of future bugs nor coverage parity.
