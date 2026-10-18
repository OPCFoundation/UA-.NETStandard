# OPC UA native HTTP3 / WebTransport draft-16 backend

This **optional native-only** package provides a bounded Sans-I/O HTTP/3/WebTransport
state engine. It is not a web server, QUIC implementation, TLS implementation, or
authentication provider. Core, ordinary Client/Server, and base PubSub do not depend
on it. No Go runtime is embedded or linked here.

The managed WebTransport binding must separately use the one native QUIC runtime,
the stack's certificate manager, actual peer application verification, and asynchronous
transport pumps. A native operation succeeding means a state transition occurred,
**not** that a queued send reached the transport.

## Supported assets

| RID | Requirements |
| --- | --- |
| `win-x64` | Supported Windows/.NET host; native compiler floor Windows 10 / Server 2016. Static MSVC CRT. |
| `linux-x64` | glibc **2.34 or later**, `libgcc_s.so.1` with GCC ABI 3.0/3.3/4.2.0. No bundled system libraries. |

These are the only accepted RIDs. There is no claim for musl, ARM, macOS, browsers,
older WebTransport drafts, or execution on the minimum OS version. The lower glibc
symbol requirement is not a minimum-host execution test.

## Integration

Use `include/opcua_http3.h` and `docs/ABI.md`. Check ABI/layouts before creation.
Pass an explicit immutable authority/resource/Origin/application-protocol policy,
resource limits, actual authenticated transport facts and externally allocated stream IDs.
One application pair and one CONNECT lifetime are pinned to each TLS connection;
cross-origin/application pooling and 0-RTT CONNECT are forbidden.

All calls are serialized, nonblocking and callback-free. Buffers belong to the caller.
Polling a write leases a token: command 20 must acknowledge actual cumulative accepted
bytes and release. Cancellation credit is reconciled only by command 22 using the
backend's actual **local** reset final/reliable sizes. Keep leased memory alive until
the transport releases it, including during cancellation and connection teardown.

## Source and servicing

The package includes selected pinned HTTP3 source, the exact eight-file receive overlay,
production adapter source, public toolchain pins, build recipes, per-RID hashes/SBOMs,
and complete selected-runtime notices. See `THIRD-PARTY-NOTICES.txt`; the aggregate
package is **not MIT-only**.

The selected upstream is `shiguredo/http3-rs` commit
`99a8c71d396b718a922f5108abe9c453c2671905`, Apache-2.0, version
`2026.1.0-canary.12`. The reviewed receive overlay SHA256 is
`ea50f349e4a46bf979d8f1d48190ca832eca3c27f886c1aa48ad09a0707591da`.
Static Rust/CRT fixes require rebuild and redeployment. Reassess any dependency,
source, overlay, toolchain, flags or RID change; retain overlay regression ownership.

Repository build commands (no implicit tool installation/download):

```powershell
.\Build-Native.ps1 -ToolchainRoot <verified-public-Rust-1.97.0-prefix>
.\Build-Linux.ps1
.\Pack-Native.ps1 -ValidationOnly
.\Pack-Native.ps1 -ValidationOnly -Configuration Debug
```

Windows uses public Rust 1.97.0, MSVC 14.51.36231 / linker 14.51.36260.0 and
Windows SDK 10.0.26100.0. The Linux script requires the already cached, digest-pinned
public Rust image. `build/public-inputs.json` contains exact archive/tool/runtime pins.
Neither `.msrustup` nor mutable default Rust selection is used.

Release and Debug validation packages have distinct IDs: the Debug ID ends in
`.Debug`. Both contain the same qualified native images and no managed library.
The packaging project's `netstandard2.0` target is compatibility metadata, not
a claim of a managed HTTP/3 implementation or additional platform support.
Its ordinary build needs no native compiler; packaging still requires both
verified native assets and their current receipts.

`native-src` permits library-only reproduction (`cargo build --offline --locked --release --lib`).
Full tests and managed NativeAOT probes live in the repository's
`tests\Opc.Ua.Native.Http3.Tests`; test binaries and test identities are not shipped.

**Release gate:** local validation packages are unsigned prereleases. Product signing,
final-hash SCA/VEX, and advertised minimum-host acceptance remain separately required.
The build/pack tools do not publish or claim distribution approval.
