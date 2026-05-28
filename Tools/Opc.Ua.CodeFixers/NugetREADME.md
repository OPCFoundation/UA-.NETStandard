# OPC UA migration analyzers and code fixers

This package ships Roslyn analyzers and code fixers that help consumers
migrate from OPC UA .NET Standard 1.5.378 to 2.0.

Install in your consumer project:

```xml
<PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.CodeFixers" Version="x.y.z" PrivateAssets="all" />
```

Then rebuild — the analyzer will flag each of the 17 patterns covered by the
[`Docs/MigrationGuide.md`](../../Docs/MigrationGuide.md) and, where safe,
offer an automatic code fix.

## Severity model

| Severity | Meaning                                                                                       |
| -------- | --------------------------------------------------------------------------------------------- |
| Warning  | Mechanical fix that is safe to apply across the whole solution in one go.                     |
| Info     | Fix requires manual review (e.g. UA0001 telemetry plumbing, UA0011 async signature promotion). |

## Rules

| ID     | Default  | Replaces                                                                                |
| ------ | -------- | ----------------------------------------------------------------------------------------|
| UA0001 | Info     | `Utils.Trace` / `Utils.LogX`                                                            |
| UA0002 | Warning  | Removed `<Type>Collection` wrappers                                                     |
| UA0003 | Warning  | `x == null` on now-struct built-in types                                                |
| UA0004 | Warning  | `?.` on now-struct built-in types                                                       |
| UA0005 | Warning  | `byte[]` where `ByteString` is now expected                                             |
| UA0006 | Warning  | `new Variant(object\|DateTime\|Guid\|byte[])`                                           |
| UA0007 | Warning  | `new NodeId(string)` / `new ExpandedNodeId(string)`                                     |
| UA0008 | Warning  | `Session.Call(..., params object[])` argument wrapping                                  |
| UA0009 | Warning  | `[DataContract]`/`[DataMember]` on configuration extensions                             |
| UA0010 | Warning  | `using`/`Dispose` on `CertificateIdentifier`, `UserIdentity`, `IUserIdentityTokenHandler` |
| UA0011 | Info     | Sync `IUserIdentityTokenHandler.Encrypt/Decrypt/Sign/Verify`                            |
| UA0012 | Warning  | `CertificateFactory.*` static helpers                                                   |
| UA0014 | Warning  | `DataValue.IsGood(dv)` static helper                                                    |
| UA0015 | Info     | Sync / APM members on GDS / LDS clients                                                 |
| UA0018 | Info     | `CertificateIdentifier.Certificate` getter                                              |
| UA0019 | Warning  | `new DataValue(StatusCode[, ts])`                                                       |
| UA0020 | Warning  | `EncodeableFactory.GlobalFactory` / `Create()`                                          |

To suppress an individual rule for a single line:

```csharp
#pragma warning disable UA0008 // Wrap Session.Call arguments with Variant.From
session.Call(objectId, methodId, "legacy");
#pragma warning restore UA0008
```

To set a project-wide severity, add to your `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.UA0001.severity = none      # silence UA0001 entirely
dotnet_diagnostic.UA0008.severity = error     # treat UA0008 as an error
```
