# OPC UA for Asset Administration Shell V3 (I4AAS)

This repository implements the draft **OPC UA for Asset Administration Shell V3**
companion specification (release `3.00-draft3`, namespace
`http://opcfoundation.org/UA/I4AAS/v3/`) through model, serialization,
server, client, registry, package-integrity, DPP and WoT bridge libraries.

| Project | Purpose |
|---------|---------|
| `Opc.Ua.Aas` | Source-generated I4AAS V3 model from the pinned NodeSet in `src/Opc.Ua.Aas/Design`, the object model, AAS JSON/XML/AASX serialization, clause 6.1.3 identity, clause 6.3.1 xsd type mapping, lexical canonicalization and clause 6.4 round-trip support. |
| `Opc.Ua.Aas.Server` | Server-side metamodel projection over `INodeManagerLifecycle`, registry service and projection on `Opc.Ua.XRegistry.Server`, generational materialization, environment export, federation, package integrity and provider contracts. |
| `Opc.Ua.Aas.Client` | High-level clients for deterministic metamodel NodeIds and the AAS registry, plus typed group/resource clients. |
| `Opc.Ua.Aas.WoT` | The informative Annex F bridge between an AAS environment and a WoT Connectivity Thing Description projection. |
| `Opc.Ua.Aas.Tests` | NUnit tests for the model, identity, values, serialization, registry, materialization, packages, federation, DPP and WoT bridge. |

The AAS implementation builds on the shared [xRegistry](XRegistry.md) registry
base and reuses the runtime NodeManager lifecycle described in
[Runtime NodeSets](RuntimeNodeSets.md). The IDTA HTTP API described by Annex G
of the draft is intentionally out of scope; this implementation exposes AAS
through OPC UA services and the xRegistry-compatible registry AddressSpace.

---

## 1. Core architecture

The companion specification has two independent halves:

* **Metamodel half** — shells, submodels, concept descriptions and submodel
  elements are materialized as typed OPC UA nodes, following clauses 6.1 and
  6.2. The metamodel surface is Read + Write + Call: value Variables are served
  through an injectable `IAasValueProvider`, and `AASOperationType.Invoke` is
  served through an injectable `IAasOperationHandler`.
* **Registry half** — a catalogue of shell, submodel, concept-description,
  package and environment documents is served as folders of `FileType` resources
  using the xRegistry model (clause 6.5). The registry answers discovery and
  document-retrieval questions even when the live metamodel tree is hosted
  elsewhere.

Where both halves are hosted, the stored documents are canonical and the live
nodes are derived. A write to a materialized value writes back to the document
and bumps a version; a refresh derives a new generation from the document. The
default retirement policy is `AasProjectionRetirementPolicy.Graceful`, so
existing monitored items can keep reading the old generation until they drain.

The implementation also adds the OPC UA `Decimal` DataType wire encoding needed
by clause 6.3.1, and the source generator fix needed for recursive structure
DataTypes.

---

## 2. Model, identity and value fidelity

### Deterministic NodeIds and BrowseNames

Clause 6.1.3 assigns deterministic String NodeIds. `AasNodeIdEncoding` is the
public implementation of the reversible, node-kind-discriminated encoding:

```csharp
string shellId = AasNodeIdEncoding.CreateIdentifiableId(
    AasNodeKind.Shell,
    "https://example.com/aas/42");

string elementId = AasNodeIdEncoding.CreateElementId(
    "https://example.com/submodels/nameplate",
    "Nameplate.ManufacturerName");

if (AasNodeIdEncoding.TryParse(elementId, out AasParsedNodeId parsed))
{
    Console.WriteLine(parsed.IdShortPath);
}
```

`AasIdShortPath` builds and parses the metamodel `idShortPath` convention, and
`AasBrowseNameAllocator` implements the derived BrowseName rule for
identifiables that do not carry `idShort`.

### xsd value mapping

`AasXsdTypeMap` maps every `AASDataTypeDefXsdDataType` value to exactly one OPC
UA DataType, as required by clauses 6.1.2 and 6.3.1. `AasLexicalCanonicalizer`
parses and canonicalizes xsd lexical values, and `AasValueSpaceComparer`
compares values in the XML Schema value space rather than by string equality:

```csharp
ExpandedNodeId dataTypeId = AasXsdTypeMap.ToDataTypeId(
    AASDataTypeDefXsdDataType.Decimal);

bool parsed = AasLexicalCanonicalizer.TryParse(
    "1.500000",
    AASDataTypeDefXsdDataType.Decimal,
    out Variant value,
    out string? error);

bool equivalent = AasValueSpaceComparer.AreEquivalent(
    "1.500000",
    "1.5",
    AASDataTypeDefXsdDataType.Decimal);
```

The object model uses `AasOptional<T>` so absent remains distinct from
present-but-empty (clause 6.1.5). Serializers and round-trip comparison preserve
that distinction.

---

## 3. Reading, writing and materializing AAS documents

AAS JSON, XML and AASX are read into the same object model. JSON and XML readers
return diagnostics instead of throwing for malformed documents:

```csharp
await using FileStream input = File.OpenRead("environment.json");
AasDocumentReadResult read = await new AasJsonReader().ReadAsync(input, ct);

if (!read.Succeeded)
{
    Console.WriteLine(read.Error);
    return;
}

AasMaterializationResult materialized = AasEnvironmentMaterializer.Materialize(
    read.Environment!);

await using FileStream output = File.Create("environment.aasx");
await new AasxPackageWriter().WriteAsync(output, read.Environment!, ct);
```

The inverse path serializes a clause 6.1.6 NodeSet back to an AAS environment:

```csharp
AasSerializationResult serialized = AasEnvironmentSerializer.Serialize(
    materialized.NodeSet);

await using FileStream json = File.Create("roundtrip.json");
await new AasJsonWriter().WriteAsync(json, serialized.Environment, ct);
```

The round-trip guarantee in clause 6.4 is equivalence, not byte identity. A
canonical value rewrite such as `"1.500000"` to `"1.5"` is equivalent for
`xs:decimal`; losing digits, conflating absent with empty or changing ordered
arrays is not equivalent.

---

## 4. Hosting the metamodel server

`AddAasServer` registers the environment NodeManager, default providers and the
runtime projection host. It must be used with the normal server hosting feature:

```csharp
services.AddSingleton<IAasValueProvider, MyAasValueProvider>();

services
    .AddOpcUa()
    .AddServer(options =>
    {
        options.EndpointUrls.Add("opc.tcp://localhost:4840/AasServer");
    })
    .Services
    .AddOpcUa()
    .AddAasServer(options =>
    {
        options.EnvironmentFolder = "aas-environments";
        options.RetirementPolicy = AasProjectionRetirementPolicy.Graceful;
    })
    .AddOperationHandler<MyAasOperationHandler>();
```

`EnvironmentFolder` is read by `FolderAasEnvironmentProvider`; when it is not
set, `InMemoryAasEnvironmentProvider` is used. The metamodel server can also be
constructed directly with `AasEnvironmentNodeManagerFactory` when an application
manages `StandardServer.NodeManagerFactories` itself.

The runtime uses `IAasValueProvider` for reads and writes of materialized value
Variables:

```csharp
public sealed class MyAasValueProvider : IAasValueProvider
{
    public ValueTask<AasValueReadResult> ReadValueAsync(
        NodeId valueNodeId,
        CancellationToken cancellationToken = default)
    {
        return new ValueTask<AasValueReadResult>(new AasValueReadResult(
            ServiceResult.Good,
            Variant.From("running"),
            StatusCodes.Good,
            DateTime.UtcNow));
    }

    public ValueTask<ServiceResult> WriteValueAsync(
        NodeId valueNodeId,
        Variant value,
        CancellationToken cancellationToken = default)
    {
        return new ValueTask<ServiceResult>(ServiceResult.Good);
    }
}
```

`IAasOperationHandler.InvokeAsync` receives already-marshalled input and
in-output values and returns the four outputs required by `AASOperationType.Invoke`.
A failed AAS operation is represented as a Good OPC UA Call with `Success = false`
and a diagnostic, which is distinct from a failed Call service result.

---

## 5. Using the metamodel client

`AasClient` computes the deterministic NodeIds of clause 6.1.3, opens the
generated `*TypeClient` proxies, reads values with their declared xsd type and
writes lexical values using that type:

```csharp
var client = new AasClient(session, aasNamespaceIndex, telemetry);

AASPropertyTypeClient temperature = client.OpenProperty(
    "https://example.com/submodels/process",
    "Temperature");

AasValueReadResult current = await client.ReadValueAsync(temperature.ObjectId, ct);
Console.WriteLine($"{current.ValueType}: {current.LexicalValue}");

StatusCode writeStatus = await client.WriteLexicalValueAsync(
    temperature.ObjectId,
    "42.5",
    ct);
```

Operations keep the OPC UA Call status separate from the AAS success flag:

```csharp
AASOperationTypeClient reset = client.OpenOperation(
    "https://example.com/submodels/process",
    "Reset");

AasOperationInvokeResult result = await client.InvokeAsync(
    reset.ObjectId,
    ArrayOf<Variant>.Empty,
    ArrayOf<Variant>.Empty,
    0,
    ct);

if (StatusCode.IsGood(result.CallStatusCode) && !result.Success)
{
    Console.WriteLine(result.Diagnostic);
}
```

Dependency injection registers either a factory over an already connected
`ManagedSession`, or a lazy factory that asks the registered OPC UA client for a
session:

```csharp
services
    .AddOpcUa()
    .AddClient(options => { /* endpoint and application options */ })
    .AddAasClient(options =>
    {
        options.InstanceNamespaceUri = "urn:example:aas:instances";
    });

Func<CancellationToken, Task<AasClient>> clientFactory =
    sp.GetRequiredService<Func<CancellationToken, Task<AasClient>>>();
AasClient aas = await clientFactory(ct);
```

---

## 6. AAS registry

The AAS registry is a concrete xRegistry specialization (clause 6.5). It exposes
the well-known `AASRegistry` object under `Server`, projects shell groups,
submodel files, concept dictionaries, packages and environment documents, and
wires the AAS discovery methods:

* `LookupShellsByAssetLink(Name, Value)` returns matching shell group NodeIds.
* `GetSubmodel(SubmodelIdentifier)` returns the document bytes, `Format` and
  `ContentType`, while preserving `Bad_UserAccessDenied` and `Bad_NotFound` as
  meaningful outcomes.

The service keeps immutable snapshots. Every mutation advances `Generation`, and
existing readers keep their previous snapshot:

```csharp
var registry = new AasRegistryService();
await registry.UpsertResourceAsync(new AasUpsertResourceRequest
{
    GroupSourceIdentity = "https://example.com/aas/42",
    ResourceSourceIdentity = "https://example.com/submodels/nameplate",
    GroupKind = AasRegistryEntityKind.Shell,
    ResourceKind = AasRegistryEntityKind.Submodel,
    Content = ByteString.From(File.ReadAllBytes("nameplate.json")),
    ContentType = "application/aas+json",
    Format = "aas/3.0+json"
});

AasGetSubmodelResult document = await registry.GetSubmodelAsync(
    "https://example.com/submodels/nameplate");
```

To expose the registry in a manually configured server, add the registry node
manager factory to the server's node manager factories:

```csharp
var registry = new AasRegistryService();
server.NodeManagerFactories.Add(new AasRegistryNodeManagerFactory(registry));
```

The AAS registry projection uses the shared `XRegistryProjectionEngine` from
`Opc.Ua.XRegistry.Server`, so base xRegistry lifecycle, labels and file-transfer
semantics match the WoT registry implementation.

### Registry client

`AasRegistryClient` derives from `XRegistryClient`. It resolves the well-known
root, inherits the base xRegistry lifecycle helpers, and adds the AAS discovery
methods and typed resource clients:

```csharp
AasRegistryClient registry = await AasRegistryClient.ForServerAsync(
    session,
    telemetry,
    ct);

ArrayOf<NodeId> shells = await registry.LookupShellsByAssetLinkAsync(
    "serial",
    "42",
    ct);

AasGetSubmodelDocumentResult submodel = await registry.GetSubmodelAsync(
    "https://example.com/submodels/nameplate",
    ct);

if (StatusCode.IsGood(submodel.StatusCode))
{
    File.WriteAllBytes("nameplate.json", submodel.Document.ToArray());
}
```

The DI shortcut is `AddAasRegistryClient`:

```csharp
services
    .AddOpcUa()
    .AddClient(options => { /* endpoint and application options */ })
    .AddAasRegistryClient();
```

---

## 7. Updateable registry and environment export

`AasMaterializationCoordinator` implements the optional updateable-registry
profile of clause 6.5.9. It reads `AasMaterializationDocument` entries from an
`IAasMaterializationDocumentStore`, prepares shadow generations, atomically
switches them through an `IAasEnvironmentProjectionHost`, records per-document
outcomes and skips unchanged digests unless `Force` is set.

Important behaviours:

* **Documents are canonical.** Materialized nodes can be rebuilt from the stored
  documents and the specification. No extra per-node state is required.
* **Switches are atomic.** Shadow generations are not browsable and do not emit
  model-change events until committed.
* **Failures retain the previous generation.** Invalid desired versions stay in
  the registry with diagnostics while the previous active generation continues
  serving.
* **Graceful retirement is the default.** Existing monitored items survive an
  update until retained work drains; `Immediate` invalidates affected monitored
  items with `BadNodeIdUnknown`.
* **Value write-back bumps versions.** Writes through materialized nodes update
  the stored document and avoid redundant projection when the digest is unchanged.

`AasEnvironmentExporter` implements clause 6.5.10. It exports the whole
materialized environment as AAS JSON, XML or AASX and filters the result per the
calling session's read access. A filtered export does not publish a digest,
because the bytes depend on the caller.

---

## 8. Federation, packages, DPP and WoT

### Federation

AAS federation follows clause 6.5.6. `AasFederationIdentity` derives proxy
identity from the remote AAS identifier attributes, never from the local endpoint.
`AasResourceUrlFederationResolver` and `AasOpcUaFederationResolver` apply a
fail-closed egress policy: host/scheme controls, DNS and connected-address
checks, redirect/time/size bounds, no ambient credentials and OPC UA peer
identity validation.

### Packages

`AasPackageIntegrity` and `IAasPackageStore` implement the package integrity
rules in clause 6.5.4. Package versions publish immutable `Digest` and
`DigestAlg`; consumers verify the returned blob by default. OCI-backed packages
verify the manifest digest, require exactly one package layer descriptor, keep
mutable tags separate from immutable `VersionId`s and keep referrers out of the
package version collection.

### Digital Product Passport vocabulary

The `Dpp/` namespace provides a small Digital Product Passport vocabulary layer.
`AasDppDisclosurePolicy` maps DPP regulatory classes to the AAS `Public` and
`Controlled` disclosure tiers, while preserving the finer disclosure class and
authorization text for consumers.

### WoT bridge

`Opc.Ua.Aas.WoT` implements the informative Annex F projection. `AasWotBridge`
reads a bundle of Thing Descriptions carrying the AAS and OPC UA WoT Binding
terms, resolves the type binding (`@type`, `ua:HasTypeDefinition` or both), and
projects the object graph described by Annex F. It does not replace the WoT
Connectivity registry; it is the AAS-specific bridge between the two companion
specifications.

---

## 9. Security and disclosure notes

Registry writes and file updates inherit the xRegistry security model. The AAS
`GetSubmodel` convenience method deliberately re-checks authorization on the
target resource; permission to call the method is never substituted for
permission to read the file. Denied responses return no bytes, no parse metadata
and no size or digest. Concealed targets and nonexistent targets follow the same
externally visible branch.

`DisclosureTier` and `Authorization` are metadata, not credentials. The
implemented DPP policy maps regulatory classes to public or controlled tiers,
and tests assert that `AASAuthorizationOptionDataType` carries configuration
only, not passwords, tokens, keys or secrets.

Federation resolves untrusted metadata and therefore fails closed. A resolver
policy, DNS, redirect, credential, certificate, identity or resource-bound
failure terminates resolution without returning or caching bytes from the
rejected destination.

---

## 10. Conformance matrix

The following table maps the sixteen clause 10 conformance units to tests in
`tests/Opc.Ua.Aas.Tests`. Each cited test was checked against the behaviour it
asserts; no unit is intentionally left unmapped.

A Server publishes the units it actually enables. Both NodeManagers implement
`IConformanceContributor`, and the server aggregates their contributions into
`Server/ServerCapabilities/ConformanceUnits`, so the published set follows what
was wired up rather than what the assemblies could in principle do:
`AAS-OperationInvoke` appears only when an operation handler other than
`DefaultAasOperationHandler` is configured, and `AAS-Packages` (with
`AAS-PackageIntegrity`, which clause 10 requires alongside it) only when a
package group is present. The unit names are held in `AasConformanceUnits`.

`ServerProfileArray` gains no AAS entry. Clause 10 names conformance units but
assigns no server profile URIs, and the IDTA profile identifier of Annex G is
qualified on also implementing the HTTP binding, which this stack does not — so
publishing it would claim conformance that is not met.

| Conformance unit | Demonstrating tests |
|------------------|---------------------|
| `AAS-Metamodel` | `Materialization.AasEnvironmentMaterializerTests.EverySubmodelElementTypeMaterializesWithItsAnnexBMembers`; `Client.AasClientTests.OpenMethodsResolveByIdentifierAndIdShortPath` |
| `AAS-SubmodelElements` | `Materialization.AasEnvironmentMaterializerTests.EverySubmodelElementTypeMaterializesWithItsAnnexBMembers`; `Serialization.AasSerializationTests.JsonRoundTripPreservesEverySubmodelElementType` |
| `AAS-ValueFidelity` | `Values.AasXsdTypeMapTests.EveryDeclaredXsdTypeIsAssignedADataType`; `Values.AasXsdTypeMapTests.NoDataTypeIsAssignedToTwoXsdTypes`; `Values.AasLexicalCanonicalizerTests.CanonicalizationIsIdempotent`; `Client.AasClientTests.ReadAndWriteValueUseDeclaredXsdType` |
| `AAS-InstanceMaterialization` | `Materialization.AasEnvironmentMaterializerTests.OrderedListUsesHasOrderedComponentAndUnorderedListUsesHasComponent`; `Materialization.AasEnvironmentMaterializerTests.ListMembersAreNamedByIndexAndCarrySequentialIndex`; `Materialization.AasEnvironmentMaterializerTests.MaterializingTheSameEnvironmentTwiceProducesByteIdenticalNodeSet` |
| `AAS-LosslessRoundTrip` | `Materialization.AasEnvironmentRoundTripTests.MaterializeThenSerializeProducesEquivalentEnvironment`; `Materialization.AasEnvironmentRoundTripTests.SerializeThenMaterializeProducesEquivalentNodeSet`; `Materialization.AasEnvironmentRoundTripTests.ConflatingAbsentWithEmptyIsReported`; `Materialization.AasEnvironmentRoundTripTests.RewritingValueIntoCanonicalLexicalFormIsNotReported` |
| `AAS-Registry` | `Registry.AasRegistryProjectionTests.ProjectionExposesEverySourceIdentityVerbatim`; `Registry.AasRegistryProjectionTests.ConcreteRegistryMethodsCarryDeclarationIds`; `Registry.AasRegistryServiceTests.UpsertResourceAdvancesGenerationAndPreservesExistingSnapshots` |
| `AAS-RegistryIdentity` | `Registry.AasRegistryServiceTests.IdentifierConstructionMatchesXRegistryAndIsInvariantAcrossVersions`; `Registry.AasRegistryProjectionTests.ProjectionExposesEverySourceIdentityVerbatim`; `Client.AasRegistryClientTests.TypedGroupAndResourceClientsExposeSourceIdentities` |
| `AAS-RegistryVersioning` | `Registry.AasRegistryServiceTests.VersionOrderingResolvesNewestVersionNotLaterThanMoment`; `Client.AasRegistryClientTests.VersionsResolveNewestVersionNotLaterThanMoment` |
| `AAS-Discovery` | `Registry.AasRegistryServiceTests.LookupShellsByAssetLinkFindsShellAndBoundsUnauthenticatedResults`; `Registry.AasRegistryGetSubmodelContractTests.AuthorizedCallerGetsDocumentFormatAndContentType`; `Client.AasRegistryClientTests.LookupShellsByAssetLinkReturnsHitAndMiss`; `Client.AasRegistryClientTests.GetSubmodelReturnsDocumentOnSuccess` |
| `AAS-OperationInvoke` | `Server.AasRuntimeInvocationTests.InvokeWithCorrectArityReturnsOperationOutputsAsync`; `Server.AasRuntimeInvocationTests.InvokeWithArityMismatchReturnsBadInvalidArgumentAsync`; `Server.AasRuntimeInvocationTests.InvokeOperationFailureReturnsGoodCallStatusAndFalseSuccessAsync`; `Client.AasClientTests.InvokeMarshalsArguments` |
| `AAS-Federation` | `Federation.AasFederationTests.ProxyIdentityRetainsRemoteAttributesAndIgnoresLocalEndpoint`; `Federation.AasFederationTests.EgressPolicyRejectsRestrictedAddressAndReturnsNoBytes`; `Federation.AasFederationTests.ConnectedAddressRevalidationBlocksDnsRebinding`; `Federation.AasFederationTests.OpcUaPeerIdentityMismatchTerminatesWithoutRemoteRead` |
| `AAS-DisclosureTiers` | `Registry.AasRegistryServiceTests.DisclosureTierAndAuthorizationAdvertiseConfigurationOnly`; `Registry.AasRegistryServiceTests.ControlledDisclosureTierRequiresAuthenticationButPublicDoesNot`; `Registry.AasRegistryGetSubmodelContractTests.TargetRolePermissionsDenialReturnsUserAccessDenied`; `Dpp.AasDppDisclosurePolicyTests.RegulatoryClassesMapToDisclosureTiers` |
| `AAS-UpdateableRegistry` | `Updateable.AasUpdateableRegistryMaterializationTests.ShadowGenerationStaysInvisibleUntilAtomicSwitch`; `Updateable.AasUpdateableRegistryMaterializationTests.GracefulRetirementServesExistingMonitoredItemUntilDrain`; `Updateable.AasUpdateableRegistryMaterializationTests.ValidationFailureKeepsPreviousGenerationAndDivergesVersions`; `Updateable.AasUpdateableRegistryMaterializationTests.ValueWriteBackBumpsVersionWithoutRedundantMaterialization` |
| `AAS-EnvironmentExport` | `Updateable.AasUpdateableRegistryMaterializationTests.EnvironmentExportIsFilteredPerSessionWithNoDigest`; `Serialization.AasxPackageTests.JsonPackageRoundTripsEnvironmentAndSupplementaryFiles`; `Serialization.AasxPackageTests.XmlPackageReadsEnvironment` |
| `AAS-Packages` | `Packaging.AasPackageIntegrityTests.PublishedVersionExposesImmutableDigestAndDigestAlg`; `Packaging.AasPackageIntegrityTests.MovingTagToNewManifestRetainsDistinctVersions`; `Packaging.AasPackageIntegrityTests.ReferrerDoesNotMutatePackageVersionCollectionOrLifecycle`; `Client.AasRegistryClientTests.PackageDownloadVerifiesDigestByDefault` |
| `AAS-PackageIntegrity` | `Packaging.AasPackageIntegrityTests.DigestAlgorithmsAcceptOnlyExactAasSpellings`; `Packaging.AasPackageIntegrityTests.MismatchedBlobIsRejectedBeforeItIsReadable`; `Packaging.AasPackageIntegrityTests.ConsumerSideVerificationCatchesTamperedBlob`; `Packaging.AasPackageIntegrityTests.OciBindingPublishesManifestDigestWithPrefixAndDigestWithoutPrefix`; `Packaging.AasPackageIntegrityTests.OciManifestRequiresExactlyOnePackageLayerDescriptor`; `Client.AasRegistryClientTests.PackageDownloadRejectsTamperedBlob` |

---

## 11. Limitations and migration notes

* Annex G / IDTA-01002 Part 2 HTTP APIs are not implemented by this branch.
* The AAS registry server currently exposes direct construction through
  `AasRegistryService` and `AasRegistryNodeManagerFactory`; the client has a DI
  helper (`AddAasRegistryClient`), while the metamodel server has `AddAasServer`.
* This branch adds new AAS packages and the OPC UA `Decimal` DataType. No manual
  migration from 1.5.378 is required solely to consume the new AAS libraries;
  the general 2.0 migration guidance remains in [Migration Guide](MigrationGuide.md).

---

## 12. References

* OPC UA for Asset Administration Shell V3 draft, release `3.00-draft3`, namespace `http://opcfoundation.org/UA/I4AAS/v3/`.
* [xRegistry — abstract registry base model](XRegistry.md).
* [OPC UA WoT Connectivity](WoTConnectivity.md).
* [Dependency Injection](DependencyInjection.md).
* [Runtime NodeSets](RuntimeNodeSets.md).

