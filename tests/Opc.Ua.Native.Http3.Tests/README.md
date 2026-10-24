# Native HTTP3 backend tests

These tests exercise the production `opcua_http3_*` C interface and the pinned HTTP3
engine. They do not implement QUIC, create test certificates, or claim that synthetic
transport facts are authenticated network evidence.

## Native tests

From the repository root, build with the already provisioned, hash-verified public
Rust toolchain and the cached Linux builder:

```powershell
.\src\Opc.Ua.Native.Http3\Build-Native.ps1 -ToolchainRoot $publicRustPrefix
.\src\Opc.Ua.Native.Http3\Build-Linux.ps1
```

The Windows script runs Rust tests, rustfmt and Clippy before producing the DLL.
The pinned Linux image runs Rust tests, a C11 layout check and the release build;
rustfmt/Clippy are deliberately not downloaded into that image. Both scripts verify
the exact upstream/overlay/notice hashes and production-only exports.

`native\regressions.rs` retains the adopted candidate/protocol/resource/lease tests.
`native\policy.rs` covers configurable policy, denial paths, allocation/descriptor/
receipt/lifetime limits, cancellation, large-session teardown and response-policy
gating of buffered data. `native\abi.rs` covers caller buffers, layout checks and
typed creation/operation errors. The maintained upstream suite is run unchanged.

## Managed and NativeAOT interface checks

```powershell
.\tests\Opc.Ua.Native.Http3.Tests\Run-Managed.ps1
.\tests\Opc.Ua.Native.Http3.Tests\Run-Managed.ps1 -NativeAot
.\tests\Opc.Ua.Native.Http3.Tests\Run-LinuxManaged.ps1 -BuilderImage $cachedAotBuilder
```

The Linux builder must already contain the accepted public .NET 10 SDK, NativeAOT
prerequisites and runtime packs. Only the resolved, cached test-package archives are
copied; network access and implicit image pulls are disabled. The runtime container
uses the existing digest-pinned runtime-deps image. Executed native hashes and TRX
results are retained under `artifacts`.

The project-local SDK file permits an installed 10.0.4xx patch without changing the
repository's SDK selection. Artifacts and isolated build snapshots are excluded from
compilation. Tests use TUnit only; the native state tests use Rust's standard harness.

## Package checks

```powershell
.\src\Opc.Ua.Native.Http3\Pack-Native.ps1 -ValidationOnly
python .\tests\Opc.Ua.Native.Http3.Tests\packaging_tests.py
```

Package tests reject extra fixture/unsupported-RID files, missing notices/SBOMs,
tampered binaries, duplicate entries and stale source receipts. The package contains
only allowlisted assets/source/notices, not these tests or their build outputs.

The package-local `ABI.md` defines the production contract. Real transport/flow
integration, final product signing/SCA/VEX and minimum-host execution remain separate
acceptance work; these interface checks are not a substitute for those checks.
