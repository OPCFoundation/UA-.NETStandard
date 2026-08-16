# Coverage NodeSet test harness (issue #4251)

This directory contains a purpose-authored **"kitchen-sink" NodeSet2**
(`Assets/Opc.Ua.CoverageTest.NodeSet2.xml`) plus a small dependent **secondary**
model (`Assets/Opc.Ua.CoverageTestSecondary.NodeSet2.xml`) that deliberately flip
a broad set of the knobs defined by `UANodeSet.xsd`, and a data-driven assertion
battery that runs the **same** checks against the model loaded through three
independent pipelines:

1. **Source-generation (hand-written manager)** — the XML is consumed at compile
   time via `<AdditionalFiles ModelSourceGeneratorPrefix="Opc.Ua.CoverageTest">`
   in `Opc.Ua.Server.Tests.csproj`. The generated `AddOpcUaCoverageTest(context)`
   extension composes the predefined nodes inside the hand-written
   `CoverageTestNodeManager : CustomNodeManager2` (`CoverageTestSourceGenServer`).
   Every authored node materialises as its generated `NodeState` subclass.
2. **Runtime import** — the identical XML is embedded as a resource and imported
   at startup through `RuntimeNodeSetNodeManagerFactory` / `AddRuntimeNodeSet`
   (`CoverageTestRuntimeServer`).
3. **Source-generation (generated fluent manager)** — a `[NodeManager]`-attributed
   partial (`CoverageTestFluentNodeManager`) makes the generator emit the entire
   `FluentNodeManagerBase` manager, factory and typed fluent-builder surface
   (`CoverageTestFluentGenServer`).

Each server is started for real (`ServerFixture<T>` + `ReferenceServer`), and the
assertion battery is executed once per server, then a cross-pipeline equivalence
fixture asserts the three address spaces agree.

## Files

| File | Purpose |
|------|---------|
| `Assets/Opc.Ua.CoverageTest.NodeSet2.xml` | The primary kitchen-sink model. |
| `Assets/Opc.Ua.CoverageTest.NodeSet2.csv` | Matching SymbolicName/Id/NodeClass table for byte-stable ids. |
| `Assets/Opc.Ua.CoverageTestSecondary.NodeSet2.xml` | A dependent second-namespace model (cross-namespace references). |
| `CoverageTestCatalogue.cs` | The expected node + reference catalogue (drives the data-driven tests). |
| `CoverageTestSourceGenServer.cs` | Source-generation host (`CoverageTestNodeManager`), owns both namespaces. |
| `CoverageTestRuntimeServer.cs` | Runtime-import host (two dependency-sorted sources). |
| `CoverageTestFluentNodeManager.cs` / `CoverageTestSecondaryFluentNodeManager.cs` | `[NodeManager]` partials for the generated fluent managers. |
| `CoverageTestFluentGenServer.cs` | Generated-fluent-manager host. |
| `CoverageNodeSetAssertionsBase.cs` | The shared, data-driven assertion battery (generic over the server type). |
| `CoverageTestAssertionsTests.cs` | Concrete fixtures binding the battery to each pipeline. |
| `CoverageTestEquivalenceTests.cs` | Cross-pipeline structural equivalence fixture (all three servers). |

## What is exercised

- Every node class (`UAReferenceType`, `UADataType`, `UAObjectType`,
  `UAVariableType`, `UAObject`, `UAVariable`, `UAMethod`, `UAView`).
- Every `Definition` shape (Enumeration, OptionSet, Structure, Structure with an
  optional field, Union, an abstract structure, non-default `Purpose`).
- Scalar, **array** and **matrix** variable value shapes.
- A deep, branching instance tree (depth 4, two branches) navigated child-by-child.
- **NamespaceMetadata publication** — the `NamespaceMetadata` Object exists for
  both namespaces (all pipelines) and, for the two source-generated pipelines,
  surfaces the model's `NamespaceVersion` (`1.2.3`) and `NamespacePublicationDate`
  (`2024-06-01`). The runtime-import pipeline carries no `ModelDependency` assembly
  attribute, so it only asserts the Object and its `NamespaceUri`.
- **Deterministic method call** — invoking the authored `AddNumbers` method with no
  bound handler returns `BadNotImplemented` and yields the identical status on every
  call.
- A **second (dependent) namespace** with cross-namespace `HasTypeDefinition`,
  `HasSubtype` and `HasComponent` references, hosted in all three pipelines.
- Custom reference types: hierarchical/asymmetric (with `InverseName`),
  symmetric, an abstract type with a concrete subtype, plus inverse authoring.
- Non-default common/instance/variable/method/view attributes, all five
  `HasModellingRule` targets, and a broad set of reference relations
  (`HasComponent`, `HasProperty`, `HasOrderedComponent`, `HasSubtype`,
  `HasTypeDefinition`, `HasEncoding`, `HasEventSource`, `HasCondition`,
  `HasInterface`, `GeneratesEvent`/`AlwaysGeneratesEvent`, `FromState`/`ToState`/
  `HasCause`/`HasEffect`, `HasTrueSubState`/`HasFalseSubState`, and the custom
  reference types).

## Known pipeline differences (intentionally not asserted for equivalence)

The reference catalogue deliberately excludes edges where the two pipelines
legitimately differ, so the equivalence fixture stays strict on everything else:

- **Type dictionaries / `DataTypeDescription` nodes** are not authored. The
  source generator regenerates the binary/XML schema from the
  `DataTypeDefinition`s rather than emitting `DataTypeDictionary` nodes into the
  address space, so hand-authored dictionary nodes would diverge. As a
  consequence the `HasDescription` and `HasDictionaryEntry` relations are not
  covered here.
- **Duplicate hierarchical references** to the same child (for example a
  `HasComponent` alongside a custom hierarchical reference, or `HasNotifier`
  alongside `HasEventSource`) and a `View`'s `Organizes` edges to already-parented
  nodes are represented differently by the two importers, so those specific
  edges are not asserted for equivalence.
- **Runtime structure stand-ins** — the runtime complex-type loader registers
  the enumeration and option-set stand-ins for this multi-type model but not the
  structure stand-ins; the structure *definitions* are validated directly via
  the `DataTypeDefinition` round-trip instead.

## Related generator and product fixes

Authoring this model surfaced two latent source-generator issues and one product
bug, all fixed:

- A top-level `<ServerUris>` table crashed `NodeSetToModelDesign`
  (`StringTable.Append(null)` for the reserved local-server slot). The guard is in
  `tools/Opc.Ua.SourceGeneration.Core/Schema/NodeSetToModelDesign.cs` and is
  regression-tested by `NodesetServerUrisTests` in
  `Opc.Ua.SourceGeneration.Tests`.
- A model with more than one structure (hence more than one `Default Binary`
  encoding) made the generated **typed fluent builder** emit colliding accessors,
  because `DataTypeEncoding` objects hang off their `DataType` and surfaced as
  parent-less "root" instances sharing the reserved `DefaultBinary`/`DefaultXml`/
  `DefaultJson` symbolic names. `FluentBuilderGenerator.GetTopLevelInstances` now
  skips `DataTypeEncoding` objects (they are encoding metadata, never fluent-wired).
- `NamespaceMetadataPublisher` never stamped a model's publication date because the
  "unset" guard compared against `DateTime.MinValue` (`0001-01-01`) while an unset
  `NamespacePublicationDate` defaults to the OPC UA epoch (`1601-01-01`); it now
  uses `DateTimeUtc.IsNull`. It also only discovered the `ModelDependency` attribute
  through a node manager's sync facade, which is a framework wrapper for
  async-native (fluent) managers; it now scans both the async manager's and its sync
  facade's assemblies so the fluent pipeline publishes version metadata too.
