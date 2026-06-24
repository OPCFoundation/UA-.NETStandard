# PubSub (Part 14)

> **When to read this:** Read this if your application uses any of the
> `Opc.Ua.PubSub.*` namespaces, the legacy `UaPubSubApplication` factory,
> the `JsonEncodingMode` enum, or RawData field encoding. This sub-doc documents the PubSub **breaking** and
> behaviour-affecting changes in 2.0.

For the full Part 14 feature reference, including additive 2.0 capabilities,
see [`PubSub.md`](../../PubSub.md). This sub-doc focuses on migration work
required for existing consumers.

## Contents

1. [PubSub assemblies and NuGet packages renamed and split](#1-pubsub-assemblies-and-nuget-packages-renamed-and-split)
2. [`UaPubSubApplication.Create*` and the legacy 1.04 API are removed](#2-uapubsubapplicationcreate-and-the-legacy-104-api-are-removed)
3. [JSON encoder switched to System.Text.Json](#3-json-encoder-switched-to-systemtextjson)
4. [`JsonEncodingMode` Reversible/Non-Reversible encodings removed](#4-jsonencodingmode-reversiblenon-reversible-encodings-removed)
5. [UADP RawData bounded-field wire-compatibility break](#5-uadp-rawdata-bounded-field-wire-compatibility-break)
6. [Compatibility matrix](#6-compatibility-matrix)

## 1. PubSub assemblies and NuGet packages renamed and split

The monolithic 1.5.378 PubSub library has been refactored into one core
assembly plus dedicated transport and server-integration assemblies. Each
assembly ships as its own NuGet package under the
`OPCFoundation.NetStandard.Opc.Ua.PubSub*` package prefix:

| Assembly                | NuGet package                                    | Contents                                                        |
| ----------------------- | ------------------------------------------------ | --------------------------------------------------------------- |
| `Opc.Ua.PubSub`         | `OPCFoundation.NetStandard.Opc.Ua.PubSub`        | Core application, encoding, scheduling, security, and DataSets. |
| `Opc.Ua.PubSub.Udp`     | `OPCFoundation.NetStandard.Opc.Ua.PubSub.Udp`    | UDP datagram transport (Part 14 §7.3.2).                        |
| `Opc.Ua.PubSub.Mqtt`    | `OPCFoundation.NetStandard.Opc.Ua.PubSub.Mqtt`   | MQTT broker transport (Part 14 §7.3.4).                         |
| `Opc.Ua.PubSub.Server`  | `OPCFoundation.NetStandard.Opc.Ua.PubSub.Server` | Server-side address-space integration (Part 14 §9).             |
| `Opc.Ua.PubSub.Adapter` | `OPCFoundation.NetStandard.Opc.Ua.PubSub.Adapter` | External-server publisher/subscriber/Action binding over `ManagedSession`. |

Consumers that previously referenced the single `Opc.Ua.PubSub` package must add
the transport package(s) they use (`...PubSub.Udp` and/or `...PubSub.Mqtt`) and,
for address-space integration, the `...PubSub.Server` package. Add `...PubSub.Adapter` when a PubSub application must bind DataSets or Actions to an external OPC UA server through a managed client session. The root
namespaces follow the assembly names (`Opc.Ua.PubSub`, `Opc.Ua.PubSub.Udp`,
`Opc.Ua.PubSub.Mqtt`, `Opc.Ua.PubSub.Server`).

The legacy 1.04 types listed in §2 are **removed** in 2.0 — there is no
compatibility shim assembly or package. Existing code that uses the obsolete API
must be migrated to the modern fluent builder / DI surface as described below.

## 2. `UaPubSubApplication.Create*` and the legacy 1.04 API are removed

`UaPubSubApplication.Create(...)` (and its overloads) and the legacy 1.04 PubSub
types are **removed** in 2.0. They are not shipped as `[Obsolete]` shims and there
is no `Opc.Ua.PubSub.Legacy` compatibility package — migrate to the fluent builder
or the DI extensions:

| Removed legacy type               | New replacement                                              |
| --------------------------------- | ------------------------------------------------------------ |
| `UaPubSubApplication`             | `IPubSubApplication` (built via `PubSubApplicationBuilder`)  |
| `IUaPubSubConnection`             | `PubSubConnection` (sealed, immutable)                       |
| `UaPubSubConnection`              | `PubSubConnection`                                           |
| `IUaPublisher` / `UaPublisher`    | `IPubSubScheduler` + `WriterGroup` (engine-driven)           |
| `UaPubSubConfigurator`            | `PubSubApplicationBuilder` (fluent) + `IPubSubConfigurationStore` |
| `PubSubJsonEncoder` / `PubSubJsonDecoder` | `Opc.Ua.PubSub.Encoding.Json.JsonEncoder` / `JsonDecoder` (System.Text.Json) |

> **`IUaPubSubDataStore`** remains in `Opc.Ua.PubSub` (marked `[Obsolete]`,
> `UA0023`) because the modern bridge still consumes it; migrate to
> `IPublishedDataSetSource` (per-DataSet provider model) when convenient.

Codemod recipe:

```csharp
// Before (1.5.378)
var app = UaPubSubApplication.Create("publisher.xml");
app.Start();
// ...
app.Stop();

// After (2.0)
await using var app = await new PubSubApplicationBuilder()
    .ConfigureFromXml("publisher.xml")
    .BuildAsync();
await app.StartAsync();
// ...
await app.StopAsync();
```

See [`PubSub.md` §Fluent builder](../../PubSub.md#fluent-builder-walkthrough)
for the in-code form. Cites [Part 14 §6.2](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2).

## 3. JSON encoder switched to System.Text.Json

The Newtonsoft-based encoder (`Opc.Ua.PubSub.Encoding.JsonNetworkMessage` v1) is
replaced with a `System.Text.Json`-backed encoder under
`Libraries/Opc.Ua.PubSub/Encoding/Json/`. Behaviour changes that may surface in
callers:

- The `Newtonsoft.Json` dependency is dropped from the PubSub layer (it remains
  transitively available via `Opc.Ua.Core` for legacy Variant JSON).
- Numeric round-trips honour the .NET native precision instead of the Newtonsoft
  default (e.g. `double` → 17 significant digits, not 15).
- The new encoder is `Utf8JsonWriter`-backed; allocations on the hot path drop
  ~70 % vs. the Newtonsoft pipeline.
- The decoder uses `Utf8JsonReader` and validates structurally; it rejects
  trailing junk where the old decoder silently truncated.

## 4. `JsonEncodingMode` Reversible/Non-Reversible encodings removed

`Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode.Reversible` and
`Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode.NonReversible` are removed in
favour of the [Part 6 §5.4.1](https://reference.opcfoundation.org/specs/OPC-10000-6/v1.05.06/5.4.1)
/ [Part 14 §7.2.5](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5)
names:

| Old                              | New                              |
| -------------------------------- | -------------------------------- |
| `JsonEncodingMode.Reversible`    | `JsonEncodingMode.Verbose`       |
| `JsonEncodingMode.NonReversible` | `JsonEncodingMode.Compact`       |
| _(new)_                          | `JsonEncodingMode.RawData`       |

`Verbose` carries the same information as the old `Reversible` mode, and
`Compact` the same as `NonReversible`; the rename is a public-API change. Note
the encoder switch to `System.Text.Json` (§3) can change incidental formatting
(e.g. number precision), so output is not guaranteed byte-identical to the 1.04
Newtonsoft encoder. No `[Obsolete]` aliases exist — consumers update enum
references at upgrade time. Background:
[#3609](https://github.com/OPCFoundation/UA-.NETStandard/issues/3609).

## 5. UADP RawData bounded-field wire-compatibility break

This is an on-wire compatibility break for consumers of bounded RawData fields. Per [Part 14 §7.2.4.5.11](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.5.11), `String`, `ByteString`, `XmlElement`, and array fields encoded via `DataSetFieldContentMask.RawData` are now padded to the maximum size declared in `FieldMetaData.MaxStringLength` or `FieldMetaData.ArrayDimensions`. The on-wire length prefix is suppressed for padded fields; consumers receive the exact `MaxStringLength` bytes with trailing NULs as the spec mandates, and decoders trim the trailing NUL fill on read.

If your configuration uses RawData but does not declare `MaxStringLength` or
`ArrayDimensions`, the encoder falls back to the legacy length-prefixed form
(variable size) and the configuration validator surfaces issue code `PSC0025`
(`SpecClause = "7.2.4.5.11"`) so the missing bound is reported at configuration
time. Closes [#3566](https://github.com/OPCFoundation/UA-.NETStandard/issues/3566).

## 6. Compatibility matrix

| Surface                                                      | 2.0 outcome                                                       |
| ------------------------------------------------------------ | ----------------------------------------------------------------- |
| `UaPubSubApplication.Create(string)` from XML config         | Compiles unchanged + `[Obsolete]` warning. Behaviour identical.   |
| `UaPubSubApplication.Start()` / `.Stop()`                    | Compiles + `[Obsolete]`. Internally delegates to `IPubSubApplication`. |
| Direct construction of `UaPubSubConnection` etc.             | Compiles + `[Obsolete]`. Migrate to the fluent builder.           |
| Newtonsoft-based PubSub JSON formatting assumptions          | **Behavioural break.** `System.Text.Json` precision and validation rules apply. |
| `JsonEncodingMode.Reversible` / `NonReversible`              | **Source break.** Rename to `Verbose` / `Compact`.                |
| `DataSetFieldContentMask.RawData` with bounded strings/arrays | **Wire break.** Fields are padded and length prefixes suppressed per spec. |

## See also

- [Library reference (PubSub.md)](../../PubSub.md)
- [Dependency injection](../../DependencyInjection.md)
- [Profiles](../../Profiles.md) — Datagram-v2, SKS pull/push, AES-CTR
- [Native AOT](../../NativeAoT.md)
- [What's New in 2.0](../../WhatsNewIn2.0.md#part-14-pubsub-modernization)
