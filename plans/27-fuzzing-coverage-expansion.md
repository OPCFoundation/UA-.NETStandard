# Fuzzing coverage expansion under `fuzzing/`

## Problem & goal

The `fuzzing/` tree currently fuzzes only the **Encoders** area (Binary / JSON / XML
decode + encode) via SharpFuzz (afl-fuzz + libFuzzer), with a deterministic NUnit
replay suite (`Encoders.Fuzz.Tests`) and corpus/playback tooling (`*.Fuzz.Tools`).
Large untrusted-input surfaces are not fuzzed, and the per-area scaffolding is
copy-paste, so adding areas is costly.

**Goal:** broaden fuzz coverage across the highest-value attack surfaces, in
priority order, while refactoring the shared scaffolding so each new area only
supplies its own `FuzzableCode` + `Testcases` partials. New deterministic replay
test projects are wired into `UA.slnx` and CI.

**Scope (agreed):**

* Areas (priority order): **1) Encoder hardening, 2) Certificates/CRL/ASN.1,
  3) Transport UA-SC/TCP framing, 4) String parsers.** PubSub is out of scope for now.
* Refactor shared scaffolding: **yes.**
* Enrich seed corpora + add dictionaries: **yes.**
* Wire new `*.Fuzz.Tests` into `UA.slnx` + CI as regression tests: **yes.**

## Current state (baseline)

* `fuzzing/common/Fuzz/` — shared host: `Program.cs`, `FuzzMethods.cs`
  (reflection-driven target discovery over `FuzzableCode`).
* `fuzzing/common/Fuzz.Tools/` — shared `Program.cs`, `Playback.cs`, `Logging.cs`,
  and `Testcases.cs` (sample `IEncodeable` builders `ReadRequest`/`ReadResponse`).
* `fuzzing/Encoders/Fuzz` — `Encoders.Fuzz.csproj` + `FuzzableCode.*` partials.
* `fuzzing/Encoders/Fuzz.Tests` — generic NUnit harness `EncoderTests.cs`
  (replays `Testcases/` corpus + `Assets/` crash/timeout/slow blobs through every
  `FuzzableCode` target). Already `[Category("Fuzzing")]`.
* `fuzzing/Encoders/Fuzz.Tools` — `Encoders.Testcases.cs` corpus generator.
* `fuzzing/dictionaries/` — `json.dict`, `xml.dict` (no `binary.dict`).
* `fuzzing/scripts/` — `fuzz-afl.ps1`, `fuzz-libfuzzer.ps1`, `install.sh`.
* `UA.slnx` lists the 3 Encoders projects + loose files. Azure `test.yml` / `testcc.yml`
  overlay `FuzzingArtifacts.zip` into `fuzzing/Encoders/Fuzz.Tests` and run the suite.
* `Opc.Ua.Core.csproj` already uses `<InternalsVisibleTo>` items (seam pattern exists).
* `src/Opc.Ua.Core.Diagnostics` is new — multi-targets `net8.0;net9.0;net10.0`
  and depends on PacketDotNet + SharpPcap. Public surfaces relevant to fuzzing:
  `OpcUaFrameParser` (TCP → UA-SC chunk splitter), `TcpStreamReassembler`
  (raw TCP), `OfflineSecureChannel` (re-uses the stack's
  `UaSCUaBinaryChannel.ReadSymmetricMessage` so all security profiles are
  covered for free), `ServiceCallReassembler` (chunk → service-call assembly),
  and `MockServerReplay` / `MockClientReplay` (stateful loopback replay drivers).
* `tests/Opc.Ua.Core.Diagnostics.Tests` (net10.0-only test project) contains mock
  handshake / replay helpers (`ReplayTestHelpers`, `MockServerReplayTests`,
  `MockClientReplayTests`, `OfflineSecureChannelSmokeTests`). These cannot be
  project-referenced from a multi-TFM fuzz area; the equivalent live helpers
  on the binding itself (`ReplayCaptureSource`, `MockServerReplay`,
  `MockClientReplay`) are multi-TFM and used directly.

### Confirmed defects / gaps to fix during hardening

* `FuzzableCode.XmlDecoder.cs` `AflfuzzXmlEncoder` re-encodes with **`JsonEncoder`**
  instead of `XmlEncoder` (the libFuzzer variant is correct) — bug.
* No idempotent targets for JSON/XML (only Binary has them).
* JSON encoder targets only exercise `JsonEncoderOptions.Verbose` (non-reversible /
  compact paths unfuzzed).
* Only full-message decode is fuzzed; individual built-in type readers
  (NodeId/ExpandedNodeId/Variant/ExtensionObject/DataValue/DiagnosticInfo) are not
  directly targeted.
* `Fuzzing.md` lists XML + CRL/Certificate as "planned" (XML is done; ASN.1 not started).

## Proposed target structure

```
fuzzing/
  Fuzzing.md                         # updated: areas, priorities, "how to add an area"
  common/
    Fuzz/                            # unchanged host
    Fuzz.Tests/                      # NEW shared harness (moved from EncoderTests.cs)
      FuzzTargetTestsBase.cs         #   generic reflection-driven base fixture
      TestcaseAsset.cs               #   asset/formatter types
    Fuzz.Tools/                      # shared Program/Playback/Logging + Testcases base
  dictionaries/
    binary.dict (NEW) json.dict xml.dict asn1.dict (NEW) nodeid.dict (NEW)
    uasc.dict (NEW) tcp.dict (NEW)
  scripts/  fuzz-afl.ps1 fuzz-libfuzzer.ps1 install.sh
            fuzz-menu.ps1 (NEW: dynamic menu from host target list)
  Encoders/      Fuzz | Fuzz.Tests | Fuzz.Tools  (hardened; + Parsers targets)
  Certificates/  Fuzz | Fuzz.Tests | Fuzz.Tools  (NEW area)
  Network/       Fuzz | Fuzz.Tests | Fuzz.Tools  (NEW area; refs Opc.Ua.Core.Diagnostics;
                                                  hosts both pcap-binding targets [4a]
                                                  and the optional Core UA-SC seam [4b])
```

Each area's `Fuzz.Tests` becomes a thin concrete `[TestFixture]` deriving from the
shared base and pointing at its own `Testcases/` + `Assets/` folders. Corpus suffixes
are discovered from disk instead of the hardcoded `[".Binary", ".Json", ".Xml"]`.

## Phased work (priority order)

### Phase 0 — Refactor shared scaffolding (enables clean area addition)

* Extract the generic NUnit harness from `Encoders/Fuzz.Tests/EncoderTests.cs` into
  `common/Fuzz.Tests/FuzzTargetTestsBase.cs` (+ `TestcaseAsset` / `FuzzTargetFunction`).
  Keep behavior identical; the Encoders test becomes a thin subclass.
* Replace hardcoded testcase-suffix arrays with directory discovery in the harness
  and in `Fuzz.Tools` playback.
* Add `scripts/fuzz-menu.ps1` that lists targets dynamically (the host already
  enumerates `FuzzableCode` targets); keep per-area `libfuzz.*` / `aflfuzz.*` as thin
  wrappers calling it.
* Verify the Encoders area still builds and tests stay green (no regression).

### Phase 1 — Harden Encoders area (highest ROI, lowest risk)

* Fix `AflfuzzXmlEncoder` to use `XmlEncoder`.
* Add idempotent targets: `*JsonEncoderIndempotent`, `*XmlEncoderIndempotent`
  (mirror the existing Binary idempotent core).
* Add JSON encoder option coverage: target(s) exercising non-reversible / compact
  `JsonEncoderOptions` in addition to Verbose.
* Add `FuzzableCode.BuiltInTypes.cs`: targets that decode individual built-in types
  via the Binary/JSON/XML decoders (NodeId, ExpandedNodeId, Variant, ExtensionObject,
  DataValue, DiagnosticInfo, QualifiedName, LocalizedText).
* Add `binary.dict`; enrich `Testcases` (more message types + nested / extension-object
  samples) so seeds cover the new targets.
* The existing generic harness auto-picks up new targets; add a few curated seed blobs
  under `Testcases.*`.

### Phase 2 — String parsers (cheap, folded into Encoders area)

* Add `FuzzableCode.Parsers.cs` (afl string + libFuzzer span targets) for
  `NodeId.Parse`, `ExpandedNodeId.Parse`, `RelativePathFormatter.Parse`,
  `QualifiedName.Parse`, `NumericRange.Parse`, `Uuid` round-trip.
* Add `nodeid.dict`; seed corpus of representative identifier strings.

### Phase 3 — Certificates / CRL / ASN.1 area (NEW, pre-auth surface)

* New `fuzzing/Certificates/{Fuzz,Fuzz.Tests,Fuzz.Tools}` referencing
  `Opc.Ua.Security.Certificates` (+ Core).
* Targets (libFuzzer span + afl stream):
  * `X509CRL(byte[])` → force `EnsureDecoded` (TBS, revoked entries, extensions,
    `X509Signature` parse).
  * X509 extension parsers: `X509SubjectAltNameExtension`,
    `X509AuthorityKeyIdentifierExtension`, `X509CrlNumberExtension` from raw ASN.1.
  * `PEMReader` import (cert + key) from PEM bytes.
  * `Pkcs10CertificationRequest` decode.
  * `AsnUtils` low-level helpers where they take raw input.
* `Certificates.Testcases.cs`: generate seeds by building a small cert / CRL / CSR with
  the existing builders, then emitting their raw DER/PEM bytes.
* Add `asn1.dict`. Note the BouncyCastle path on net48 — keep targets behind the same
  TFMs as the cert library; add an AOT consideration note.

### Phase 4 — Network / Transport fuzzing (NEW area)

> Two complementary sub-phases. **4a goes first** — it requires no Core
> changes and lands the bulk of network-layer coverage. 4b is additive and
> stays gated on owner OK.

#### Phase 4a — Network-layer fuzzing via `Opc.Ua.Core.Diagnostics` (no Core changes)

* New `fuzzing/Network/{Fuzz,Fuzz.Tests,Fuzz.Tools}`, referencing
  `Opc.Ua.Core.Diagnostics` (+ `Opc.Ua.Core` transitively). Multi-targets
  `net8.0;net9.0;net10.0` to match the binding's matrix (no net48/net472 — the
  pcap binding does not support .NET Framework, which is consistent with how
  modern areas are added elsewhere in the repo).

* **Stateless targets** (afl stream + libFuzzer span):
  * `OpcUaFrameParser.Process` — fuzz TCP segment payloads with attacker-
    controlled boundaries and message-size headers; assert no undocumented
    exceptions, no unbounded allocation (cap via `MaxChunkSize`).
  * `TcpStreamReassembler` — feed mutated `TcpFlowSegment`s including bad
    sequence numbers, overlapping ranges, mixed flow keys.
  * `OfflineSecureChannel.ReadChunk(chunkBytes, fromClient)` — instantiate
    one decoder per fuzz iteration over a fixed test `ChannelKeyMaterial`;
    covers symmetric decrypt/verify, sequence-window validation, and the
    asymmetric-chunk passthrough path. Because this calls
    `UaSCUaBinaryChannel.ReadSymmetricMessage`, every security profile is
    exercised through the binding without per-profile harness code.
  * `ServiceCallReassembler` — drive sequences of `OfflineDecodedChunk`s with
    bad/missing/duplicate sequence numbers, oversize requests, mismatched
    request IDs.

* **Stateful replay targets** (libFuzzer-only, separate target list):
  * `MockServerReplay` driver — mutate server-side bytes from a recorded
    capture, replay against a real `Opc.Ua.Client` channel on loopback;
    assert no hang/crash on mutated server traffic, finite reconnect
    attempts, no accidental state corruption across iterations.
  * `MockClientReplay` driver — inverted: mutate client-side bytes against a
    real server (e.g. the `ReferenceServer`). Catches handshake-state-machine
    bugs the stateless targets miss.
  * Keep these in a separate target list so libFuzzer can be told to focus
    on cheap stateless targets first (throughput matters).

* **Seeds (`Network.Testcases.cs`)**: drive a deterministic Hello → OPN →
  MSG (Read/Browse) → CLO handshake against an in-process reference server
  using the binding's own `MockServerReplay` / `MockClientReplay` +
  capturing message-socket factory (the same pattern
  `Opc.Ua.Core.Diagnostics.Tests` uses, but inlined into `Network.Fuzz.Tools`
  so it stays multi-TFM and doesn't depend on the net10.0-only test
  project). Emit:
  * raw TCP segments → `Testcases.Tcp/`
  * UA-SC chunks → `Testcases.Chunks/`
  * paired `ChannelKeyMaterial` JSON → `Testcases.Keys/` (fixture-generated,
    test certs only — **never commit real keylog material**).

* **Dictionaries:** `uasc.dict` (MSG/OPN/CLO/HEL/ACK/ERR/RHE magic, chunk-
  type bytes, common message-size sentinels) and `tcp.dict` (raw TCP markers).

* Generic harness from Phase 0 auto-discovers the new targets;
  `Network.Fuzz.Tests` becomes a thin subclass of `FuzzTargetTestsBase`.

* **Dependency hygiene:** PacketDotNet + SharpPcap come along through the
  pcap binding but are only used by capture sources. The `Network.Fuzz`
  host must avoid pulling in `NicCaptureSource` (live-capture surface) so
  the AFL/libFuzzer process doesn't open raw sockets.

#### Phase 4b — Transport UA-SC / TCP framing seam in `Opc.Ua.Core` (complements 4a)

* The pcap binding covers post-Hello chunk framing and symmetric decrypt; it
  does **not** expose `ProcessHelloMessage`, `ProcessReverseHelloMessage`,
  `ProcessErrorMessage`, or the asymmetric-header field decode that runs
  before crypto. Phase 4b fills those gaps.
* Most parse logic is in `private` / `protected` channel methods
  (`ProcessHelloMessage`, `ReadAsymmetricMessageHeader`, …) — not isolatable
  as-is. Add a minimal **internal static** parse seam in `Opc.Ua.Core` for
  the pre-crypto, pre-auth chunk surface: chunk header
  (`messageType` + `messageSize` bounds via `TcpMessageType` /
  `TcpMessageLimits`) and Hello / Acknowledge / Error / ReverseHello
  **message-body** decoding from an `ArraySegment<byte>`. Refactor existing
  private code to call the new helper (no behavior change).
* Expose via `<InternalsVisibleTo Include="Network.Fuzz" />` — add the new
  `FuzzableCode.Transport.cs` partials to the existing `Network/Fuzz`
  project from 4a rather than spinning up a separate `Transport.Fuzz`
  project (one Network area covers both sub-phases).
* `Transport.Testcases.cs` (under `Network/Fuzz.Tools`): emit valid Hello /
  Ack / Error / ReverseHello chunks as seeds.
* **Decision flag:** confirm the product-code seam (internal static helper +
  `InternalsVisibleTo`) before implementing Phase 4b; **4a delivers the
  majority of network-layer coverage without it**, so 4b can ship later or
  be deferred entirely if owner consent isn't obtained.

### Phase 5 — Integration: solution, CI, docs

* Add all new `Fuzz`, `Fuzz.Tests`, `Fuzz.Tools` projects + loose files to `UA.slnx`
  under the right `fuzzing/<Area>/` folders. Specifically: Encoders (hardened),
  Certificates (new), Network (new — hosts both Phase 4a pcap-binding targets
  and the optional Phase 4b Core UA-SC seam targets in one project tree).
* Ensure new `*.Fuzz.Tests` carry `[Category("Fuzzing")]`, are deterministic, and run
  under the same TFMs / filters as `Encoders.Fuzz.Tests`; confirm Azure `test.yml` picks
  them up (they live under `fuzzing/<Area>/Fuzz.Tests`).
* `Network.Fuzz.Tests` multi-targets `net8.0;net9.0;net10.0` (matches pcap binding
  matrix); net48/net472 stays in scope for Encoders / Parsers / Certificates areas.
* Rewrite `Fuzzing.md`: mark XML done, document new areas + targets, dictionaries, the
  dynamic menu, a step-by-step "add a new fuzz area" recipe, and the Network area's
  replay-driven seed pipeline + `Opc.Ua.Core.Diagnostics` dependency note
  (TFM matrix + PacketDotNet/SharpPcap).
* Cross-link from `docs/README.md` if a fuzzing doc is referenced there; cross-link
  the pcap binding's diagnostics docs (if present) so they point to the fuzz benefit.

## Validation strategy

* After each phase: `dotnet build` the touched fuzz projects + `dotnet test` the
  affected `*.Fuzz.Tests` (net48 + net10.0 per repo PR policy; Network area is
  net8.0/net9.0/net10.0 only) — must stay green and deterministic.
* Run `Fuzz.Tools --testcases` to (re)generate corpora and `--playback` to confirm no
  seed crashes, for every area.
* Smoke-run at least one libFuzzer target per new area locally (short duration) to
  confirm the host wiring + dictionaries load. For Network area, smoke-run ≥1
  stateless target (e.g. `OpcUaFrameParser`) and ≥1 replay-driven target to
  confirm `Opc.Ua.Core.Diagnostics` loads cleanly under the fuzz host without
  initialising any live capture source.
* Keep code coverage of `*.Fuzz.Tests` from regressing; new targets are exercised by
  the generic replay harness.

## Risks / notes

* Phase 0 refactor must not break the existing green Encoders suite — do it first and
  verify before adding areas.
* Phase 4b requires touching product code (`Opc.Ua.Core`); keep the seam internal,
  behavior-preserving, and compatible (no breaking public API). Gate on owner OK.
  Phase 4a is unblocked by this — it can land first via `Opc.Ua.Core.Diagnostics`'s
  existing public surface.
* `Opc.Ua.Core.Diagnostics` multi-targets `net8.0;net9.0;net10.0` and depends on
  PacketDotNet + SharpPcap (not AOT-verified). Network fuzz area follows the same
  TFM matrix; it is excluded from net48/net472 and from any AOT matrix.
* `OfflineSecureChannel.ReadChunk` exercises real crypto via the stack — use
  deterministic test keys, and pre-warm one channel per fixture iteration to
  keep per-iter setup off the hot path.
* Stateful replay targets (`MockServerReplay` / `MockClientReplay`) are slower
  than direct parser targets; keep them in a separate libFuzzer target list so
  cheap stateless targets are prioritised for throughput.
* **Never commit real keylog / key material.** Seed corpora use only material
  generated from the existing fixture test certificates via the binding's own
  multi-TFM replay helpers.
* If `PcapFileReader` / `PcapNgFileWriter` / `UaKeyLogTextReader` /
  `UaKeyLogJsonReader` fuzzing is added later (defensive, untrusted-file
  parsers), fold into the same `fuzzing/Network/` area as additional
  `FuzzableCode.*` partials rather than spinning up a new area.
* ASN.1 / cert targets must respect the TFM / BouncyCastle split and NativeAOT rules.
* Idempotent / round-trip targets can surface real encoder bugs — triage findings as
  product issues, not test bugs.
