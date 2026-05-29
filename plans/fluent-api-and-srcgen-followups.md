# Remaining fluent-API and source-generator gap follow-ups

> **Status:** Plan. Tracks open follow-ups that emerged after the
> original Pumps integration exercise (which exposed and closed
> gaps G1–G11; the historical writeup of those closed gaps was
> consolidated into the source-generated NodeManagers guide).

All 11 gaps identified by the Pumps companion-spec exercise are
closed. The fluent API surface now covers instance creation, alarms,
property initialization, simulation, multi-model composition,
FunctionalGroups, supervision flags, and engineering units;
source-generator blockers around cross-namespace BrowseNames,
OptionSet `Variant.From<T>` routing, and `OptionalPlaceholder`
initializer emission are fixed.

The items below are residual improvements found while landing the
Pumps + DI work that are out of scope for the original closure
but warrant follow-up.

## 1. Typed builders for source-generated TopologyElement subtypes

**Severity:** Medium

Today the Pump sample uses string browse paths
(`builder.Variable<double>("Pumps/Pump #1/Operational/...")`).
Source-generation already emits typed `*State` classes for every
TopologyElement; the fluent surface should expose a typed
`builder.Type<PumpType>("Pump #1")` overload that resolves children
through the generated property accessors rather than browse strings.
This eliminates a class of runtime `BadNodeIdUnknown` failures and
makes the sample compile-time-checked against its own model.

**Affected files:**

- `Libraries/Opc.Ua.Server/Fluent/InstanceCreationBuilderExtensions.cs`
- `Applications/MinimalPumpServer/PumpNodeManager.Configure.cs`
  (consumer — rewrite once the typed builder lands)

## 2. Snapshot import for transitive model dependencies in samples

**Severity:** Low

`Opc.Ua.Di` ships the merged `ModelDependencyAttribute` (with the
type-table payload), so consumer assemblies that source-generate
their own Machinery / Pumps models can resolve cross-namespace DI
references without needing to add the DI NodeSet2 XML again. The
remaining work is documentation: walk a reader through the snapshot
import flow inside the sample server's README so the pattern is
obvious to copy.

**Affected files:**

- `Applications/MinimalPumpServer/README.md` (link to the snapshot
  pattern walkthrough).
- `Docs/ModelDependencies.md` (already covers the wire format and
  unified attribute; add a small "consumer setup" section).

## 3. Promote source generation to the documented default

**Severity:** Low

`Docs/SourceGeneratedNodeManagers.md` should treat source generation
as the only recommended mode for application-owned models. The
narrow `ImportNodeSet(Stream)` overload remains for genuinely
runtime-only inputs (e.g. tenant uploads), but the doc should not
demonstrate runtime XML loading for first-party content. That edit
landed alongside this plan; this entry tracks the broader posture
change so future contributors keep the same default.

## 4. Test consolidation

**Severity:** Low

`Tests/Opc.Ua.Pumps.Tests/` was a transient project carved out for
the Pumps integration tests. Those tests have been folded back into
`Tests/Opc.Ua.Di.Tests/` because they exercise the DI hosting and
fluent surfaces, not Pumps-specific behaviour.

## Closed gaps (for historical context)

The 11 originally identified gaps closed by the Pumps integration:

| # | Area | Description | Implementation |
|---|------|-------------|----------------|
| G1 | Fluent API | Instance creation API | `InstanceCreationBuilderExtensions` |
| G2 | Fluent API | Alarm/condition setup | `AlarmBuilderExtensions` |
| G3 | Fluent API | Property initialization | `PropertyInitBuilderExtensions` |
| G4 | Fluent API | Simulation timer | `ISimulationBuilder`/`SimulationRegistry` |
| G5 | Fluent API | Multi-model composition | `IModelLoaderBuilder` |
| G6 | Fluent API | FunctionalGroup helpers | `ReferenceBuilderExtensions` |
| G7 | Fluent API | Supervision/health state | `SupervisionBuilderExtensions` |
| G8 | Fluent API | Engineering units | `EngineeringUnitsBuilderExtensions` |
| G9 | Source Gen | Cross-namespace BrowseName | `NodeStateTemplates.FindChildCase` |
| G10 | Source Gen | OptionSet `Variant.From<T>` misroute | `DataTypeGenerator`/`ModelDesignExtensions` |
| G11 | Source Gen | `OptionalPlaceholder` initializer | `NodeStateGenerator` |
