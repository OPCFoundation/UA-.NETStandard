# Runtime compatibility shim — `Opc.Ua.MigrationAnalyzer.Core.dll`

The migration NuGet ships a multi-TFM runtime shim assembly
(`Opc.Ua.MigrationAnalyzer.Core.dll`, `net472` / `net48` / `netstandard2.1` /
`net8.0` / `net9.0` / `net10.0`) that re-exposes the 1.5.378 obsolete extension
surface so 1.5.378-style call sites continue to compile against 2.0.

Together with the
[source generator](source-generator.md) (which covers `<Type>Collection`
construction sites), the shim makes "install package → restore → build" a
green build for most 1.5.378 consumers, leaving only `[Obsolete]` warnings + the
`UA00xx` diagnostics behind.

## What the shim covers

### Moved obsolete extensions

API the 2.0 stack still exposes but no longer carries inline — the shim
re-supplies them as `[Obsolete]` C# 14 `extension` members in the `Opc.Ua`
namespace:

- `NodeId` / `Variant` / `DataValue` null-check helpers (`IsNull`,
  `IsNullOrEmpty`, …)
- `Session` sync helpers (`Reconnect`, `Open`, `Close`, …)
- `Subscription` sync helpers (`Create`, `Modify`, `Delete`, …)
- `ApplicationInstance` helpers
- `ServerBase.Start` / `Stop`
- `TransportChannel` APM (`BeginX` / `EndX`)
- `ChannelBase` static factory methods
- A handful of less-frequently-used 1.5.378 conveniences

### Newly-shimmed (genuinely-removed) members

API 2.0 genuinely removed — the shim re-introduces it under the original name
so call sites bind:

- `EncodeableFactory.GlobalFactory` (UA0020) — returns a process-singleton
  factory backed by `ServiceMessageContext.GlobalContext.Factory`
- `CertificateIdentifier.Certificate` (UA0018) — throws `NotSupportedException`
  with a message pointing at `LoadCertificate2Async`. The shim exists so the
  reference compiles; the analyzer fires UA0018 so the caller migrates.
- Sync wrappers for `IUserIdentityTokenHandler.{Encrypt, Decrypt, Sign,
  Verify}` (UA0011)
- Sync + APM wrappers for the `GlobalDiscoveryServerClient` /
  `LocalDiscoveryServerClient` / `ServerPushConfigurationClient` (UA0015)
- `GlobalDiscoverySampleServer` 1.5.378-shape ctor (in-tree at
  `src/Opc.Ua.Gds.Server.Common/GlobalDiscoverySampleServer.cs`) — the 5-arg
  variant without `ITelemetryContext`

### Conversion helpers

- `byte[].ToByteString()` extension (UA0005 auto-fix targets this)
- `(Uuid)guid` cast operators
- `Variant.From(object)` overloads (UA0006 auto-fix targets these)

## Sync-over-async caveat

The sync shims wrap their `*Async` counterparts via:

```csharp
Task.Run(() => xxxAsync(...)).GetAwaiter().GetResult()
```

This is intended as a **migration aid only** — keep the build green while you
port the call chain to `async`/`await`. Do **not** leave these calls on
production hot paths because:

- Each sync call thread-pool-hops twice (`Task.Run` + `GetResult()`'s
  synchronous wait); under load this exhausts the thread pool.
- The wrapper synchronously blocks a thread that 2.0's async code path
  otherwise would have released. On constrained hosts (containers with low
  vCPU budget, Kestrel under load) this surfaces as request timeouts.
- Any exceptions are wrapped in `AggregateException` and the stack trace
  loses the original async causality chain.

The analyzers `UA0011` (token-handler sync wrappers) and `UA0015` (GDS / LDS
sync + APM) report each sync shim use at **Info** severity, with a message
naming the `*Async` replacement.

## What the shim does NOT cover (must use analyzer fixes)

Source-level changes the shim has no syntactic foothold for — use the listed
analyzer fix:

- `== null` / `!= null` on now-struct types → **UA0003**
- `?.` on now-struct types → **UA0004**
- `using var x = new CertificateIdentifier(...)` → **UA0010** (drops the `using`)
- `[DataContract]` / `[DataMember]` on configuration extensions → **UA0009**
- Removed `<Type>Collection` wrappers → **UA0002** (and the source generator)
- `CertificateValidator` type rename → **UA0021** (structural — manual)

## Removing the shim

The shim only ships in the migration package. Removing the
`<PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer">`
removes both the analyzers and the shim DLL at once. Any remaining shim use is
flagged by the analyzers; address those first, then remove the package.

After removal, the consumer's compile output references only the 2.0 stack —
no `Opc.Ua.MigrationAnalyzer.Core.dll` is shipped or loaded at runtime.

## TFM coverage

| TFM | Shim ships? | Notes |
|---|---|---|
| `net472` | ✅ | Lowest legacy TFM supported by the migration window |
| `net48` | ✅ | Recommended for legacy WinForms consumers |
| `netstandard2.1` | ✅ | Covers Xamarin / Unity / other non-.NET-Framework legacy |
| `net8.0` | ✅ | LTS |
| `net9.0` | ✅ | STS |
| `net10.0` | ✅ | LTS (current) |

If your consumer targets a TFM not in this list (e.g. `net6.0`), the migration
package still installs but the shim DLL won't apply — the runtime fallback is
the analyzer + source generator only, which is sufficient for ~70% of the
migration patterns.
