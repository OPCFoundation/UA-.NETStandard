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
| `Opc.Ua.Encoders.Fuzz` | BinaryDecoder / JsonDecoder / XmlDecoder + idempotent round-trip, individual built-in type readers (NodeId, ExpandedNodeId, Variant, ExtensionObject, DataValue, DiagnosticInfo, QualifiedName, LocalizedText) and parser entry points (`NodeId.Parse`, `ExpandedNodeId.Parse`, `RelativePathFormatter.Parse`, `QualifiedName.Parse`, `NumericRange.Parse`, `Uuid` round-trip) | shipped |
| `Opc.Ua.Certificates.Fuzz` | `X509CRL` decode + extensions (`X509SubjectAltNameExtension`, `X509AuthorityKeyIdentifierExtension`, `X509CrlNumberExtension`), `PEMReader` cert/key import, `Pkcs10CertificationRequest`, low-level `AsnUtils` helpers | shipped |
| `Opc.Ua.Network.Fuzz` | OPC UA UA-SC / TCP framing via `Opc.Ua.Core.Diagnostics` (`OpcUaFrameParser`, `TcpStreamReassembler`, `OfflineSecureChannel.ReadChunk`, `ServiceCallReassembler`, `MockServerReplay`/`MockClientReplay` stateful drivers) and the `Opc.Ua.Core` UA-SC parse seam (`TcpMessageParsers.TryParseChunkHeader` / `ReadHelloMessage` / `ReadAcknowledgeMessage` / `ReadErrorMessage` / `ReadReverseHelloMessage` / `ReadAsymmetricMessageHeader`) | shipped |

`Encoders` and `Certificates` build for the repo's standard `TestsTargetFrameworks` matrix
(`net48`, `net8.0`, `net9.0`, `net10.0`). `Network` is `net8.0;net9.0;net10.0` only because
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
```

The flat layout matches the rest of the repository (`tests/`, `src/`, `src/` also list
projects directly under the root with no domain grouping). Every csproj folder name equals the
csproj filename (without the `.csproj` extension), and every assembly is prefixed with `Opc.Ua.`
in line with the repo's `AssemblyPrefix` convention.

Each area's `*.Fuzz` project hosts `FuzzableCode.*.cs` partials whose `public static` methods
with a single parameter of type `Stream` (afl), `string` (afl), or `ReadOnlySpan<byte>`
(libFuzzer) are auto-discovered by:

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
   for libFuzzer. Each target should swallow expected exceptions (`ServiceResultException`
   with `BadDecodingError` / `BadEncodingLimitsExceeded`, `CryptographicException`,
   `FormatException`, `ArgumentException`) and let unexpected ones bubble — that's what
   the fuzzer finds.
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
   progression.
7. **Wire into `UA.slnx`.** Add the three new projects to the `/fuzzing/` folder, the
   `*.dict` file under `/fuzzing/Dictionaries/`, and the seed loose files you want visible in
   the IDE.
8. **Update this `Fuzzing.md`.** Add a row to the area table.

The Azure pipelines (`test.yml` / `testcc.yml`) automatically run every `*.Fuzz.Tests` project
under the `[Category("Fuzzing")]` filter.

## Areas in detail

### Network / Transport area — `Opc.Ua.Core.Diagnostics` + Core UA-SC seam

The Network area is unusual because it's split across two complementary entry points:

* **Phase 4a (no Core changes).** Fuzzes the public surfaces of
  `src/Opc.Ua.Core.Diagnostics`: `OpcUaFrameParser.Process` (TCP → UA-SC chunk splitter),
  `TcpStreamReassembler.Process` (raw TCP), `OfflineSecureChannel.ReadChunk` (UA-SC
  symmetric decrypt + verify using the stack's own `UaSCUaBinaryChannel.ReadSymmetricMessage`,
  so every security profile is covered for free), and `ServiceCallReassembler.Push` (chunk
  → service-call assembly with mixed sequence numbers, request ids, oversize bodies). Two
  stateful replay drivers (`MockServerReplay`, `MockClientReplay`) are also wired as
  libFuzzer-only targets but kept in a separate target list so the cheap stateless targets
  dominate throughput.

* **Phase 4b (internal Core seam).** Adds `internal static class TcpMessageParsers` in
  `src/Opc.Ua.Core/Stack/Tcp/TcpMessageParsers.cs` covering the pre-crypto, pre-auth chunk
  surface the pcap binding does not expose: `TryParseChunkHeader`, `ReadHelloMessage`,
  `ReadAcknowledgeMessage`, `ReadErrorMessage`, `ReadReverseHelloMessage`,
  `ReadAsymmetricMessageHeader`. Surfaced to the fuzz area via
  `<InternalsVisibleTo Include="Opc.Ua.Network.Fuzz" />` (and matching `*.Tools` / `*.Tests`
  assemblies because the linked partials compile from every host).

#### Seed-corpus and key-material discipline

`Network.Testcases.cs` and `Transport.Testcases.cs` generate seeds from a deterministic
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

The fuzzer runs until a crash / timeout / Ctrl-C. libFuzzer writes findings to the current
directory with `crash-` / `timeout-` prefixes; afl-fuzz writes them to its `findings/`
directory.

## Replay of crashes and timeouts

Run the area's `*.Fuzz.Tools` project with `-p` / `-s` for playback with stack traces:

```bash
dotnet run --project fuzzing/Opc.Ua.Network.Fuzz.Tools -- --playback --stacktrace
```

The playback tool finds all crashes / timeouts in the default folders and replays them
against every libFuzzer target. Equivalent afl-fuzz seeds are skipped because they are
duplicates of the libFuzzer findings.

## Recreate or improve seeds

Run the area's `*.Fuzz.Tools` project with `-t` to (re)generate the area's `Testcases.*/`
seed corpus:

```bash
dotnet run --project fuzzing/Opc.Ua.Network.Fuzz.Tools -- --testcases
```

This re-runs the deterministic seed-generation pipeline (handshake recorder for Network,
encoder round-trip for Encoders, cert/CRL/CSR builder for Certificates) and emits
byte-stable artifacts the harness's `Testcases.*/` discovery picks up automatically. The
default output path for each area's Tools project is its own sibling
`Opc.Ua.<Area>.Fuzz.Corpus/Testcases` folder, derived from the running Tools assembly name
in `Common/Fuzz.Tools/Program.cs`.

## Automation — `fuzz-tester` custom agent

`.github/agents/fuzz-tester.agent.md` defines a GitHub Copilot custom agent that drives
this entire toolchain autonomously. It detects the host OS, picks the available engines
(libFuzzer everywhere, afl-fuzz on Linux when installed), publishes + SharpFuzz-instruments
each area's host project, enumerates `FuzzableCode` targets via
`Scripts/fuzz-menu.ps1`, launches one detached fuzz process per (area × engine × target)
tuple, and polls the per-instance work dirs under `fuzzing/.runs/` for new findings.

When the agent picks up a novel `crash-*` / `timeout-*` / `slow-unit-*` (or afl
`crashes/` / `hangs/`) file it:

1. SHA-1 dedups against every `Opc.Ua.<area>.Fuzz.Tests/Assets/<prefix>-<sha1>` already
   in the repo so it never re-investigates a known regression seed.
2. Reproduces the crash locally via `dotnet run --project fuzzing/Opc.Ua.<area>.Fuzz.Tools -- --playback --stacktrace`.
3. Designs a minimal fix following every repo guideline (no SYNC-over-ASYNC,
   `Span<byte>` / `ReadOnlySpan<byte>` / `ByteString` in public API, Allman + 4-space +
   CRLF + MIT header on new files, no `#region`, no `[Obsolete]` usage, NativeAOT-safe,
   `TreatWarningsAsErrors` clean, no exposed locks).
4. Runs a rubber-duck agent review (max 2 rounds) on the proposed diff.
5. Copies the failing input to `fuzzing/Opc.Ua.<area>.Fuzz.Tests/Assets/<prefix>-<sha1>`
   so the existing harness (`FuzzCrashAssets` / `FuzzTimeoutAssets` / `FuzzSlowAssets`)
   replays it through every target on every future `dotnet test` run.
6. Rebuilds and runs the three `*.Fuzz.Tests` projects on `net10.0`; the run must end at
   or above the baseline 4681-test count, 0 failed.
7. Briefly re-runs the originating fuzz instance with the new asset in its corpus to
   confirm the same crash is no longer reachable.
8. Commits one fix per commit (`Fuzz fix [<area>]: <root cause>`), pushes to `fuzzing`,
   and resumes the still-running fuzz instances.

If a fix introduces a regression, requires a public-API change, or breaks compatibility
with the 1.5.378 baseline, the agent **pauses and prompts the user** via `ask_user`
with four options (revert / accept the regression and migrate the affected seed /
provide an alternative fix / accept the API break).

Trigger phrases: *"run the fuzz tests"*, *"start fuzzing"*, *"fuzz the encoders"*,
*"fuzz the network"*, *"fuzz the certificates"*, *"fuzz until I say stop"*,
*"react to fuzz findings"*, *"find fuzz crashes and fix them"*,
*"run libfuzzer and fix what it finds"*, *"autonomous fuzz loop"*. Stop the agent at
any time with *"stop"*, *"halt"*, *"stop fuzzing"*, *"that's enough"*.

The agent's own toolchain setup mirrors the manual `Installation` section above:
`SharpFuzz.CommandLine` global tool, the `libfuzzer-dotnet` driver under
`fuzzing/.tools/` (gitignored), and `afl-fuzz` on PATH for the AFL engine on Linux.
If any are missing AND cannot be auto-installed (air-gapped box, `sudo` not available,
…), the agent surfaces the gap and asks the user how to proceed.
