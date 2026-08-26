# OPC UA for Asset Administration Shell (I4AAS)

This repository implements two **OPC UA for Asset Administration Shell**
metamodel generations in one assembly set. They are separate alternatives, not
revision levels of one model:

* **OPC 30270, "OPC UA for Asset Administration Shell"** maps the AAS V2.0.1
  metamodel into namespace `http://opcfoundation.org/UA/I4AAS/` and lives in
  `Opc.Ua.Aas.V2`.
* **OPC UA for Asset Administration Shell V3 draft** (release `3.00-draft3`)
  maps the AAS V3 draft metamodel into namespace
  `http://opcfoundation.org/UA/I4AAS/v3/` and lives in `Opc.Ua.Aas.V3`.

The shared root `Opc.Ua.Aas` namespace contains only code that is genuinely
metamodel-neutral: deterministic NodeId encoding and `idShortPath` handling,
`AasOptional<T>`, digest calculation, materialization diagnostics, the AASX
container walk and the value/operation provider contracts.

| Project | Purpose |
|---------|---------|
| `Opc.Ua.Aas` | Source-generated I4AAS V2 and V3 models from `src/Opc.Ua.Aas/Design`, the V2 object model and ingestion readers, the V3 object model and read/write serializers, shared identity/value helpers, materialization diagnostics and AASX container support. |
| `Opc.Ua.Aas.Server` | Server-side projection for both metamodels over `INodeManagerLifecycle`; V2 ingestion materialization and runtime callbacks; V3 registry service, generational materialization, environment export, federation, package integrity and provider contracts. |
| `Opc.Ua.Aas.Client` | High-level V2 and V3 clients for deterministic metamodel NodeIds, plus the V3 AAS registry client and typed group/resource clients. |
| `Opc.Ua.Aas.WoT` | The V3 informative Annex F bridge between an AAS environment and a WoT Connectivity Thing Description projection. |
| `Opc.Ua.Aas.Tests` | NUnit tests for V2 model ingestion/materialization/server/client behavior and V3 model, identity, values, serialization, registry, materialization, packages, federation, DPP and WoT bridge behavior. |

The V3 AAS implementation builds on the shared [xRegistry](XRegistry.md)
registry base and reuses the runtime NodeManager lifecycle described in
[Runtime NodeSets](RuntimeNodeSets.md). V2 is the OPC 30270 metamodel half only:
it has no registry, packages, federation, DPP or WoT bridge. The IDTA HTTP API
described by Annex G of the V3 draft is intentionally out of scope; this
implementation exposes AAS through OPC UA services and, for V3, the
xRegistry-compatible registry AddressSpace.

---

## Choosing V2 or V3

Host **one** AAS generation in a server: either V2 or V3. The stack does not
enforce that rule at startup, because the two NodeManagers can technically be
registered side by side, but the product decision is that a deployment chooses
one alternative and exposes one metamodel generation. Running both would publish
two different AAS metamodels for the same product domain and leave clients to
guess which one is authoritative.

Choose **V2** when the published OPC 30270 specification and the AAS V2.0.1
metamodel are the interoperability target. The implementation reads V2 JSON, XML
and AASX documents, materializes them into the `http://opcfoundation.org/UA/I4AAS/`
AddressSpace, serves Read + Write + Call, and publishes the OPC 30270 Table 83
conformance units. It does not export the AddressSpace back to AAS documents,
because OPC 30270 does not require a lossless round trip.

Choose **V3** when the draft AAS V3 metamodel and everything built on the draft
registry half are required: registry, updateable materialization, environment
export, packages, package integrity, federation, DPP vocabulary and the WoT
bridge. V3 reads and writes JSON, XML and AASX because its draft requires a
lossless document/AddressSpace round trip.

---

## 1. V3 core architecture

The V3 draft companion specification has two independent halves:

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

The V3 half also adds the OPC UA `Decimal` DataType wire encoding needed
by clause 6.3.1, and the source generator fix needed for recursive structure
DataTypes.

---

## 2. Shared identity and V3 value fidelity

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
ExpandedNodeId dataTypeId = Opc.Ua.Aas.V3.AasXsdTypeMap.ToDataTypeId(
    Opc.Ua.Aas.V3.AASDataTypeDefXsdDataType.Decimal);

bool parsed = Opc.Ua.Aas.V3.AasLexicalCanonicalizer.TryParse(
    "1.500000",
    Opc.Ua.Aas.V3.AASDataTypeDefXsdDataType.Decimal,
    out Variant value,
    out string? error);

bool equivalent = Opc.Ua.Aas.V3.AasValueSpaceComparer.AreEquivalent(
    "1.500000",
    "1.5",
    Opc.Ua.Aas.V3.AASDataTypeDefXsdDataType.Decimal);
```

The object model uses `AasOptional<T>` so absent remains distinct from
present-but-empty (clause 6.1.5). Serializers and round-trip comparison preserve
that distinction.

---

## 3. V3 reading, writing and materializing AAS documents

V3 AAS JSON, XML and AASX are read into the same object model. JSON and XML readers
return diagnostics instead of throwing for malformed documents:

```csharp
await using FileStream input = File.OpenRead("environment.json");
Opc.Ua.Aas.V3.AasDocumentReadResult read = await new Opc.Ua.Aas.V3.AasJsonReader()
    .ReadAsync(input, ct);

if (!read.Succeeded)
{
    Console.WriteLine(read.Error);
    return;
}

Opc.Ua.Aas.AasMaterializationResult materialized = Opc.Ua.Aas.V3.AasEnvironmentMaterializer
    .Materialize(read.Environment!);

await using FileStream output = File.Create("environment.aasx");
await new Opc.Ua.Aas.V3.AasxPackageWriter().WriteAsync(output, read.Environment!, ct);
```

The inverse path serializes a clause 6.1.6 NodeSet back to an AAS environment:

```csharp
Opc.Ua.Aas.V3.AasSerializationResult serialized = Opc.Ua.Aas.V3.AasEnvironmentSerializer
    .Serialize(materialized.NodeSet);

await using FileStream json = File.Create("roundtrip.json");
await new Opc.Ua.Aas.V3.AasJsonWriter().WriteAsync(json, serialized.Environment, ct);
```

The round-trip guarantee in clause 6.4 is equivalence, not byte identity. A
canonical value rewrite such as `"1.500000"` to `"1.5"` is equivalent for
`xs:decimal`; losing digits, conflating absent with empty or changing ordered
arrays is not equivalent.

---

## 4. Hosting the V3 metamodel server

`AddAasV3Server` registers the environment NodeManager, default providers and the
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
    .AddAasV3Server(options =>
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

## 5. Using the V3 metamodel client

`Opc.Ua.Aas.Client.AasClient` computes the deterministic NodeIds of clause 6.1.3, opens the
generated `*TypeClient` proxies, reads values with their declared xsd type and
writes lexical values using that type:

```csharp
var client = new Opc.Ua.Aas.Client.AasClient(session, aasNamespaceIndex, telemetry);

Opc.Ua.Aas.V3.AASPropertyTypeClient temperature = client.OpenProperty(
    "https://example.com/submodels/process",
    "Temperature");

Opc.Ua.Aas.Client.AasValueReadResult current = await client.ReadValueAsync(temperature.ObjectId, ct);
Console.WriteLine($"{current.ValueType}: {current.LexicalValue}");

StatusCode writeStatus = await client.WriteLexicalValueAsync(
    temperature.ObjectId,
    "42.5",
    ct);
```

Operations keep the OPC UA Call status separate from the AAS success flag:

```csharp
Opc.Ua.Aas.V3.AASOperationTypeClient reset = client.OpenOperation(
    "https://example.com/submodels/process",
    "Reset");

Opc.Ua.Aas.Client.AasOperationInvokeResult result = await client.InvokeAsync(
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
    .AddAasV3Client(options =>
    {
        options.InstanceNamespaceUri = "urn:example:aas:instances";
    });

Func<CancellationToken, Task<Opc.Ua.Aas.Client.AasClient>> clientFactory =
    sp.GetRequiredService<Func<CancellationToken, Task<Opc.Ua.Aas.Client.AasClient>>>();
Opc.Ua.Aas.Client.AasClient aas = await clientFactory(ct);
```

---

## 6. V3 AAS registry

The V3 AAS registry is a concrete xRegistry specialization (clause 6.5). It exposes
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
var registry = new Opc.Ua.Aas.Server.Registry.AasRegistryService();
await registry.UpsertResourceAsync(new Opc.Ua.Aas.Server.Registry.AasUpsertResourceRequest
{
    GroupSourceIdentity = "https://example.com/aas/42",
    ResourceSourceIdentity = "https://example.com/submodels/nameplate",
    GroupKind = Opc.Ua.Aas.Server.Registry.AasRegistryEntityKind.Shell,
    ResourceKind = Opc.Ua.Aas.Server.Registry.AasRegistryEntityKind.Submodel,
    Content = ByteString.From(File.ReadAllBytes("nameplate.json")),
    ContentType = "application/aas+json",
    Format = "aas/3.0+json"
});

Opc.Ua.Aas.Server.Registry.AasGetSubmodelResult document = await registry.GetSubmodelAsync(
    "https://example.com/submodels/nameplate");
```

To expose the registry in a manually configured server, add the registry node
manager factory to the server's node manager factories:

```csharp
var registry = new Opc.Ua.Aas.Server.Registry.AasRegistryService();
server.NodeManagerFactories.Add(
    new Opc.Ua.Aas.Server.Registry.AasRegistryNodeManagerFactory(registry));
```

The V3 AAS registry projection uses the shared `XRegistryProjectionEngine` from
`Opc.Ua.XRegistry.Server`, so base xRegistry lifecycle, labels and file-transfer
semantics match the WoT registry implementation.

### Registry client

`AasRegistryClient` derives from `XRegistryClient`. It resolves the well-known
root, inherits the base xRegistry lifecycle helpers, and adds the AAS discovery
methods and typed resource clients:

```csharp
Opc.Ua.Aas.Client.Registry.AasRegistryClient registry =
    await Opc.Ua.Aas.Client.Registry.AasRegistryClient.ForServerAsync(
        session,
        telemetry,
        ct);

ArrayOf<NodeId> shells = await registry.LookupShellsByAssetLinkAsync(
    "serial",
    "42",
    ct);

Opc.Ua.Aas.Client.Registry.AasGetSubmodelDocumentResult submodel = await registry.GetSubmodelAsync(
    "https://example.com/submodels/nameplate",
    ct);

if (StatusCode.IsGood(submodel.StatusCode))
{
    File.WriteAllBytes("nameplate.json", submodel.Document.ToArray());
}
```

The DI shortcut is `AddAasV3RegistryClient`:

```csharp
services
    .AddOpcUa()
    .AddClient(options => { /* endpoint and application options */ })
    .AddAasV3RegistryClient();
```

---

## 7. V3 updateable registry and environment export

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

## 8. V3 federation, packages, DPP and WoT

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

## 10. V3 conformance matrix

The following table maps the sixteen V3 draft clause 10 conformance units to tests in
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
| `AAS-OperationInvoke` (partial, see section 11) | `Server.AasRuntimeInvocationTests.InvokeWithCorrectArityReturnsOperationOutputsAsync`; `Server.AasRuntimeInvocationTests.InvokeWithArityMismatchReturnsBadInvalidArgumentAsync`; `Server.AasRuntimeInvocationTests.InvokeOperationFailureReturnsGoodCallStatusAndFalseSuccessAsync`; `Client.AasClientTests.InvokeMarshalsArguments` |
| `AAS-Federation` | `Federation.AasFederationTests.ProxyIdentityRetainsRemoteAttributesAndIgnoresLocalEndpoint`; `Federation.AasFederationTests.EgressPolicyRejectsRestrictedAddressAndReturnsNoBytes`; `Federation.AasFederationTests.ConnectedAddressRevalidationBlocksDnsRebinding`; `Federation.AasFederationTests.OpcUaPeerIdentityMismatchTerminatesWithoutRemoteRead` |
| `AAS-DisclosureTiers` | `Registry.AasRegistryServiceTests.DisclosureTierAndAuthorizationAdvertiseConfigurationOnly`; `Registry.AasRegistryServiceTests.ControlledDisclosureTierRequiresAuthenticationButPublicDoesNot`; `Registry.AasRegistryGetSubmodelContractTests.TargetRolePermissionsDenialReturnsUserAccessDenied`; `Dpp.AasDppDisclosurePolicyTests.RegulatoryClassesMapToDisclosureTiers` |
| `AAS-UpdateableRegistry` | `Updateable.AasUpdateableRegistryMaterializationTests.ShadowGenerationStaysInvisibleUntilAtomicSwitch`; `Updateable.AasUpdateableRegistryMaterializationTests.GracefulRetirementServesExistingMonitoredItemUntilDrain`; `Updateable.AasUpdateableRegistryMaterializationTests.ValidationFailureKeepsPreviousGenerationAndDivergesVersions`; `Updateable.AasUpdateableRegistryMaterializationTests.ValueWriteBackBumpsVersionWithoutRedundantMaterialization` |
| `AAS-EnvironmentExport` | `Updateable.AasUpdateableRegistryMaterializationTests.EnvironmentExportIsFilteredPerSessionWithNoDigest`; `Serialization.AasxPackageTests.JsonPackageRoundTripsEnvironmentAndSupplementaryFiles`; `Serialization.AasxPackageTests.XmlPackageReadsEnvironment` |
| `AAS-Packages` | `Packaging.AasPackageIntegrityTests.PublishedVersionExposesImmutableDigestAndDigestAlg`; `Packaging.AasPackageIntegrityTests.MovingTagToNewManifestRetainsDistinctVersions`; `Packaging.AasPackageIntegrityTests.ReferrerDoesNotMutatePackageVersionCollectionOrLifecycle`; `Client.AasRegistryClientTests.PackageDownloadVerifiesDigestByDefault` |
| `AAS-PackageIntegrity` | `Packaging.AasPackageIntegrityTests.DigestAlgorithmsAcceptOnlyExactAasSpellings`; `Packaging.AasPackageIntegrityTests.MismatchedBlobIsRejectedBeforeItIsReadable`; `Packaging.AasPackageIntegrityTests.ConsumerSideVerificationCatchesTamperedBlob`; `Packaging.AasPackageIntegrityTests.OciBindingPublishesManifestDigestWithPrefixAndDigestWithoutPrefix`; `Packaging.AasPackageIntegrityTests.OciManifestRequiresExactlyOnePackageLayerDescriptor`; `Client.AasRegistryClientTests.PackageDownloadRejectsTamperedBlob` |

---

## 11. V3 limitations and migration notes

* Annex G / IDTA-01002 Part 2 HTTP APIs are not implemented by this branch.
* **Calling a V3 `Operation` with arguments is incomplete.** A materialized
  Operation carries its `Invoke` Method, and a Client resolves it by either its
  own NodeId or the `MethodDeclarationId` of `AASOperationType.Invoke`. The
  Method also carries `InputArguments` and `OutputArguments` under the standard
  BrowseNames. What does not yet work is the Call itself: `NodeState.AddChild`
  does not route through `MethodState.FindChild`, so a Method imported from a
  NodeSet leaves the typed `InputArguments` property null, and the Server
  answers `BadTooManyArguments`. Closing this needs a change to how the shared
  NodeSet import claims typed children, which affects every consumer of runtime
  NodeSets and is therefore out of scope here. `samples/Aas` exercises the path
  and reports the status rather than working around it. This limitation is V3-specific;
  the V2 `Operation` Method has no declared arguments and is called with an empty
  input list.
* The AAS registry server currently exposes direct construction through
  `AasRegistryService` and `AasRegistryNodeManagerFactory`; the client has a DI
  helper (`AddAasV3RegistryClient`), while the metamodel server has `AddAasV3Server`.
* This branch adds new AAS packages and the OPC UA `Decimal` DataType. No manual
  migration from 1.5.378 is required solely to consume the new AAS libraries;
  the general 2.0 migration guidance remains in [Migration Guide](MigrationGuide.md).


---

## 12. OPC 30270 / AAS V2 half

The OPC 30270 half implements the published "OPC UA for Asset Administration
Shell" companion specification for the AAS V2.0.1 metamodel. The static model
is generated into `Opc.Ua.Aas.V2`, its namespace URI is
`http://opcfoundation.org/UA/I4AAS/`, and predefined nodes are loaded with the
generated `AddOpcUaAasV2` extension. The server and client entry points are
`AddAasV2Server` and `AddAasV2Client`.

V2 is ingestion-only. `Opc.Ua.Aas.V2.AasJsonReader`,
`Opc.Ua.Aas.V2.AasXmlReader` and `Opc.Ua.Aas.V2.AasxPackageReader` parse AAS
V2.0.1 documents and AASX packages into the V2 object model; there is no
AddressSpace-to-document serializer and therefore no round-trip guarantee. The
materializer projects the document into an OPC UA NodeSet:

```csharp
await using FileStream input = File.OpenRead("environment.json");
Opc.Ua.Aas.V2.AasDocumentReadResult read = await new Opc.Ua.Aas.V2.AasJsonReader()
    .ReadAsync(input, ct);

if (!read.Succeeded)
{
    Console.WriteLine(read.Error);
    return;
}

Opc.Ua.Aas.AasMaterializationResult materialized = Opc.Ua.Aas.V2.AasEnvironmentMaterializer
    .Materialize(read.Environment!);
```

`AddAasV2Server` registers the V2 environment NodeManager, the document-backed
value provider and the shared operation handler contract:

```csharp
services
    .AddOpcUa()
    .AddServer(options =>
    {
        options.EndpointUrls.Add("opc.tcp://localhost:4840/AasV2Server");
    })
    .Services
    .AddOpcUa()
    .AddAasV2Server(options =>
    {
        options.ControlNamespaceUri = "urn:example:aas-v2:instances";
    })
    .AddEnvironmentProvider<MyAasV2EnvironmentProvider>()
    .AddOperationHandler<MyAasOperationHandler>();
```

The runtime surface is Read + Write + Call, matching the rest of this stack. A
V2 write reaches `IAasValueProvider.WriteValueAsync` and stops there: there is
no V2 document writer that could persist the change back to JSON, XML or AASX.
`Operation` is an `OptionalPlaceholder` Method named `Operation` with no
declared arguments, unlike the V3 mandatory `Invoke` Method, so a V2 Call is
zero-argument.

File and Blob content on V2 is served through an embedded standard OPC UA
`FileType` object. Clients call `Open`, `Read` and `Close` on that child rather
than reading a V3-style value projection:

```csharp
var client = new Opc.Ua.Aas.Client.V2.AasClient(session, aasNamespaceIndex, telemetry);
Opc.Ua.Aas.V2.AASFileTypeClient manual = client.OpenFile(
    "https://example.com/submodels/nameplate",
    "Manual");
ByteString content = await client.ReadFileContentAsync(manual.ObjectId, ct: ct);
```

OPC 30270 leaves instance NodeIds server-specific and defines no addressing
convention. The V2 materializer and client deliberately reuse the deterministic
`AasNodeIdEncoding` and `AasIdShortPath` rules used by the V3 draft, so one
addressing scheme serves both generations and neither client has to browse for
known identifiers:

```csharp
var client = new Opc.Ua.Aas.Client.V2.AasClient(session, aasNamespaceIndex, telemetry);
Opc.Ua.Aas.V2.AASPropertyTypeClient temperature = client.OpenProperty(
    "https://example.com/submodels/process",
    "Temperature");

Opc.Ua.Aas.Client.V2.AasValueReadResult current = await client.ReadValueAsync(
    temperature.ObjectId,
    ct);

StatusCode status = await client.WriteValueAsync(
    temperature.ObjectId,
    Opc.Ua.Aas.V2.AASValueTypeDataType.Double,
    new Variant(42.5),
    ct);
```

V2 has no registry, no packages, no federation and no DPP. Those are V3 draft
additions, and the V2 implementation intentionally stops at the OPC 30270
metamodel.

### OPC 30270 conformance units

The V2 server publishes the seventeen conformance units from OPC 30270 Table 83
through `Server/ServerCapabilities/ConformanceUnits` and publishes no
`ServerProfileArray` entry, because Table 84, which would assign profile URIs,
is empty. Two internal specification contradictions are resolved in
`src/Opc.Ua.Aas.Server/V2/AasV2ConformanceUnits.cs`: Table 83 misspells the
multi-language unit as `I4AAS MultiLangaugeProperty` while Table 85 spells it
`I4AAS MultiLanguageProperty`, and Table 85 lists an `I4AAS Security` unit that
Table 83 never defines. The implementation publishes the corrected
`I4AAS MultiLanguageProperty` spelling and does not publish `I4AAS Security`.

Each mapping below was checked against the cited test in
`tests/Opc.Ua.Aas.Tests/V2/`. No OPC 30270 Table 83 unit is currently unmapped.

| OPC 30270 Table 83 unit | Demonstrating tests |
|-------------------------|---------------------|
| `I4AAS AAS` | `Model.AasV2ObjectModelTests.ConstructionRetainsV2TopLevelIdentifiablesAndReferences`; `Serialization.AasV2SerializationTests.JsonReaderParsesV2EnvironmentAndEverySubmodelElementType`; `Server.AasV2ServerTests.ConformanceUnitsPublishOpc30270FacetWithoutProfileUris` |
| `I4AAS Asset` | `Model.AasV2ObjectModelTests.ConstructionRetainsV2TopLevelIdentifiablesAndReferences`; `Model.AasV2ObjectModelTests.V2OnlyConceptsRoundTripThroughModel`; `Client.AasClientTests.OpenMethodsResolveByIdentifierAndIdShortPath` |
| `I4AAS Submodel` | `Model.AasV2ObjectModelTests.ConstructionRetainsV2TopLevelIdentifiablesAndReferences`; `Materialization.AasEnvironmentMaterializerTests.EverySubmodelElementTypeMaterializesWithItsMembers`; `Client.AasClientTests.OpenMethodsResolveByIdentifierAndIdShortPath` |
| `I4AAS ConceptDescription` | `Model.AasV2ObjectModelTests.ConceptDescriptionFlavoursRetainIdentifierKinds`; `Serialization.AasV2SerializationTests.XmlReaderParsesV2EnvironmentAndEverySubmodelElementType` |
| `I4AAS View` | `Model.AasV2ObjectModelTests.V2OnlyConceptsRoundTripThroughModel`; `Materialization.AasEnvironmentMaterializerTests.ShellMaterializesAssetViewsInterfacesAndAasReferences` |
| `I4AAS RelationshipElement` | `Serialization.AasV2SerializationTests.JsonReaderParsesV2EnvironmentAndEverySubmodelElementType`; `Serialization.AasV2SerializationTests.XmlReaderParsesV2EnvironmentAndEverySubmodelElementType`; `Materialization.AasEnvironmentMaterializerTests.EverySubmodelElementTypeMaterializesWithItsMembers` |
| `I4AAS Property` | `Model.AasV2ObjectModelTests.SubmodelElementMembersRetainNodeSetTypes`; `Client.AasClientTests.ReadAndWriteValueUseDeclaredAasValueTypeAsync`; `Materialization.AasEnvironmentMaterializerTests.EverySubmodelElementTypeMaterializesWithItsMembers` |
| `I4AAS MultiLanguageProperty` | `Model.AasV2ObjectModelTests.SubmodelElementMembersRetainNodeSetTypes`; `Serialization.AasV2SerializationTests.JsonReaderParsesV2EnvironmentAndEverySubmodelElementType`; `Materialization.AasEnvironmentMaterializerTests.EverySubmodelElementTypeMaterializesWithItsMembers` |
| `I4AAS Range` | `Serialization.AasV2SerializationTests.JsonReaderParsesV2EnvironmentAndEverySubmodelElementType`; `Serialization.AasV2SerializationTests.XmlReaderParsesV2EnvironmentAndEverySubmodelElementType`; `Materialization.AasEnvironmentMaterializerTests.EverySubmodelElementTypeMaterializesWithItsMembers` |
| `I4AAS Blob` | `Serialization.AasV2SerializationTests.JsonReaderParsesV2EnvironmentAndEverySubmodelElementType`; `Materialization.AasEnvironmentMaterializerTests.EverySubmodelElementTypeMaterializesWithItsMembers`; `Client.AasClientTests.FileAndBlobContentAreReadThroughEmbeddedFileTypeAsync` |
| `I4AAS File` | `Serialization.AasV2SerializationTests.AasxReaderReadsXmlJsonAndSupplementaryFiles`; `Server.AasV2ServerTests.EmbeddedFileOpenReadAndCloseServeBlobContentAsync`; `Client.AasClientTests.FileAndBlobContentAreReadThroughEmbeddedFileTypeAsync` |
| `I4AAS ReferenceElement` | `Serialization.AasV2SerializationTests.JsonReaderParsesV2EnvironmentAndEverySubmodelElementType`; `Serialization.AasV2SerializationTests.XmlReaderParsesV2EnvironmentAndEverySubmodelElementType`; `Materialization.AasEnvironmentMaterializerTests.EverySubmodelElementTypeMaterializesWithItsMembers` |
| `I4AAS Capability` | `Serialization.AasV2SerializationTests.JsonReaderParsesV2EnvironmentAndEverySubmodelElementType`; `Serialization.AasV2SerializationTests.XmlReaderParsesV2EnvironmentAndEverySubmodelElementType`; `Materialization.AasEnvironmentMaterializerTests.EverySubmodelElementTypeMaterializesWithItsMembers` |
| `I4AAS SubmodelElementCollection` | `Model.AasV2ObjectModelTests.V2OnlyConceptsRoundTripThroughModel`; `Materialization.AasEnvironmentMaterializerTests.OrderedCollectionUsesHasOrderedComponentAndUnorderedCollectionUsesHasComponent`; `AasV2ModelTests.OrderedCollectionRedeclaresTheSubmodelElementPlaceholder` |
| `I4AAS Operation` | `Materialization.AasEnvironmentMaterializerTests.MaterializedNodeSetImportsIntoAnAddressSpace`; `Server.AasV2ServerTests.OperationMethodInvokesHandlerAndRejectsArgumentsAsync`; `Client.AasClientTests.InvokeCallsTheEmbeddedOperationMethodAsync` |
| `I4AAS Event` | `Serialization.AasV2SerializationTests.JsonReaderParsesV2EnvironmentAndEverySubmodelElementType`; `Serialization.AasV2SerializationTests.XmlReaderParsesV2EnvironmentAndEverySubmodelElementType`; `Materialization.AasEnvironmentMaterializerTests.EverySubmodelElementTypeMaterializesWithItsMembers` |
| `I4AAS Entity` | `Model.AasV2ObjectModelTests.SubmodelElementMembersRetainNodeSetTypes`; `Serialization.AasV2SerializationTests.JsonReaderParsesV2EnvironmentAndEverySubmodelElementType`; `Materialization.AasEnvironmentMaterializerTests.EverySubmodelElementTypeMaterializesWithItsMembers` |

---

## 13. References

* OPC 30270, "OPC UA for Asset Administration Shell", namespace `http://opcfoundation.org/UA/I4AAS/`.
* OPC UA for Asset Administration Shell V3 draft, release `3.00-draft3`, namespace `http://opcfoundation.org/UA/I4AAS/v3/`.
* [xRegistry — abstract registry base model](XRegistry.md).
* [OPC UA WoT Connectivity](WoTConnectivity.md).
* [Dependency Injection](DependencyInjection.md).
* [Runtime NodeSets](RuntimeNodeSets.md).

