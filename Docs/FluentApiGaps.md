# Fluent API Gap Analysis — OPC 40223 Pumps Implementation (Historical)

> **STATUS:** Historical record. **All 11 gaps closed.** This document
> is preserved for design rationale and to document the migration path
> for any caller who used the pre-fix workarounds.
>
> For current usage documentation see:
>
> - [Source-generated NodeManagers — Building richer node managers](SourceGeneratedNodeManagers.md#building-richer-node-managers--the-fluent-extension-surface) — developer-facing how-to for each fluent extension.
> - [Companion specification libraries](CompanionSpecLibraries.md) — packaging a companion spec as a model + server + client library trio (worked example: DI + Pumps).
> - `Applications/MinimalPumpServer/` — the running consumer that exercises every extension end-to-end.

Findings from implementing the OPC UA Pumps companion specification
(OPC 40223) with the server fluent API + source generator.

## Summary

Implementing a full Pumps server exposed **11 gaps** in the current fluent API
and source generator. The gaps range from source-generator bugs that block
compilation to missing convenience APIs that require verbose workarounds.
**All 11 gaps have now been closed.**

| # | Area | Severity | Description | Status | Implementation |
|---|------|----------|-------------|--------|----------------|
| G1 | Fluent API | High | No instance creation API | ✅ Fixed | `InstanceCreationBuilderExtensions.cs` |
| G2 | Fluent API | High | No alarm/condition setup helpers | ✅ Fixed | `AlarmBuilderExtensions.cs` |
| G3 | Fluent API | Medium | No property initialization helpers | ✅ Fixed | `PropertyInitBuilderExtensions.cs` |
| G4 | Fluent API | Medium | No simulation timer integration | ✅ Fixed | `ISimulationBuilder.cs` + `SimulationRegistry.cs` |
| G5 | Fluent API | Medium | No multi-model composition API | ✅ Fixed | `IModelLoaderBuilder.cs` |
| G6 | Fluent API | Medium | No FunctionalGroup helpers | ✅ Fixed | `ReferenceBuilderExtensions.cs` |
| G7 | Fluent API | Low | No supervision/health state helpers | ✅ Fixed | `SupervisionBuilderExtensions.cs` |
| G8 | Fluent API | Low | No engineering units builder | ✅ Fixed | `EngineeringUnitsBuilderExtensions.cs` |
| G9 | Source Gen | **Blocker** | Cross-namespace BrowseName references | ✅ Fixed | `NodeStateTemplates.cs` FindChildCase template |
| G10 | Source Gen | **Blocker** | OptionSet types misrouted to `Variant.From<T>` (struct constraint) | ✅ Fixed | `DataTypeGenerator.cs` + `ModelDesignExtensions.cs` |
| G11 | Source Gen | High | Missing CreateOrReplace*_Placeholder for OptionalPlaceholder slots | ✅ Fixed | `NodeStateGenerator.cs` initializer logic |

All fluent-API additions live under `Libraries/Opc.Ua.Server/Fluent/`
and are exercised by **77 new tests** under
`Tests/Opc.Ua.Server.Tests/Fluent/`. All 180 Fluent-category tests pass
(77 new + 103 pre-existing).

The MinimalPumpServer pump simulation has been rewritten end-to-end on
top of the new fluent APIs (see `Applications/MinimalPumpServer/`).

---

## G1 — No Instance Creation API

**Severity:** High  
**Area:** Fluent API (`INodeManagerBuilder`)

The fluent API only wires callbacks onto *predefined* nodes from the NodeSet2 XML.
There is no way to fluently create new instances of a type.

### Current workaround
```csharp
// Must use direct NodeState manipulation outside the fluent API:
var pump2 = new BaseObjectState(parent);
pump2.BrowseName = new QualifiedName("Pump #2", nsIndex);
pump2.TypeDefinitionId = pumpTypeNodeId;
// ... manually create every child, set every property ...
AddPredefinedNode(SystemContext, pump2);
```

### Proposed API
```csharp
builder.CreateInstance<PumpType>("Pump #2", parentNodeId)
    .WithIdentification(id => {
        id.Manufacturer = "SimPump Corp";
        id.SerialNumber = "SN-002";
    })
    .WireCallbacks(node => {
        node.Variable<double>("Operational/Measurements/FluidTemperature")
            .OnRead(SimulateTemperature);
    });
```

---

## G2 — No Alarm/Condition Setup Helpers

**Severity:** High  
**Area:** Fluent API

The Pumps specification uses Boolean supervision flags that trigger NAMUR NE 107
alarms (FailureAlarm, MaintenanceRequiredAlarm, CheckFunctionAlarm, OffSpecAlarm).
There is no fluent surface for creating alarm conditions, setting limits, or wiring
acknowledge/confirm handlers.

### Current workaround
Requires direct `AlarmConditionState` instantiation with manual parent/child wiring,
limit configuration, and event callback setup — ~50 lines per alarm.

### Proposed API
```csharp
builder.Node("Events/Supervision")
    .CreateAlarm<NonExclusiveLimitAlarmState>("OverTemperatureAlarm")
    .WithLimits(highHigh: 380.0, high: 370.0, low: 273.0, lowLow: 263.0)
    .MonitorVariable(tempVariableNodeId)
    .OnAcknowledge(HandleAcknowledge)
    .OnConfirm(HandleConfirm);
```

---

## G3 — No Property Initialization Helpers

**Severity:** Medium  
**Area:** Fluent API

Setting identification properties (Manufacturer, SerialNumber, etc.) on a
`PumpIdentificationType` instance requires accessing each child property's
`NodeState.Value` individually. The fluent API has no bulk-initialization
surface.

### Current workaround
```csharp
var idNode = builder.Node("Pumps/Pump #1/Identification").Node;
// Must traverse children manually and set Value on each
```

---

## G4 — No Simulation Timer Integration

**Severity:** Medium  
**Area:** Fluent API

The fluent API has no built-in simulation loop. The MinimalPumpServer uses
per-read value generation (each `OnRead` call advances the simulation wave),
which works but doesn't support time-based events (e.g., fault injection
every 30 seconds) without a separate timer.

### Current workaround
Per-read simulation with `Interlocked.Increment(ref ticks)` on each read.
Time-based events require a manual `System.Threading.Timer`.

### Proposed API
```csharp
builder.Simulation(interval: TimeSpan.FromSeconds(1))
    .OnTick((context, dt) => {
        UpdateMeasurements(dt);
        CheckSupervisionFlags();
    });
```

---

## G5 — No Multi-Model Composition API

**Severity:** Medium  
**Area:** Fluent API / Node Manager

Loading multiple companion spec models (DI + Machinery + Pumps) into a single
node manager requires manual orchestration of `AddDI(context)`,
`AddMachinery(context)`, and `LoadFromXml()` calls. There is no fluent
`.AddModel()` chain.

Additionally, constructing a `NodeManagerBuilder` for a hand-written manager
requires 6 constructor parameters (context, manager, namespace index, and
three resolver delegates). The generated managers get this for free.

### Current workaround
```csharp
// Must manually compose:
NodeStateCollection nodes = new NodeStateCollection()
    .AddDI(context)
    .AddMachinery(context);
nodes.LoadFromXml(context, pumpsStream, true);

// Must manually construct builder with resolver delegates:
NodeManagerBuilder builder = new NodeManagerBuilder(
    SystemContext, this, nsIndex,
    browseName => FindRootByBrowseName(browseName)!,
    nodeId => FindNodeById(nodeId)!,
    typeDefId => FindNodesByTypeId(typeDefId));
```

### Proposed API
```csharp
// Simple builder construction for hand-written managers:
NodeManagerBuilder builder = NodeManagerBuilder.For(this);
```

---

## G6 — No FunctionalGroup Helpers

**Severity:** Medium  
**Area:** Fluent API

The DI `FunctionalGroupType` pattern (grouping children with Organizes
references) is central to the Pumps specification but has no fluent support.

---

## G7 — No Supervision/Health State Helpers

**Severity:** Low  
**Area:** Fluent API

The NAMUR NE 107 pattern (Boolean flag → alarm generation) is a common
pattern in DI/Machinery/Pumps but is unsupported by the fluent API.

---

## G8 — No Engineering Units Builder

**Severity:** Low  
**Area:** Fluent API

Setting `EngineeringUnits` and `EURange` on `BaseAnalogType` variables is
tedious. The Pumps spec has ~40 measurement variables, each with SI units
(Pa, K, m³/s, m, W, %).

---

## G9 — Source Gen: Cross-Namespace BrowseName References (Fixed)

**Severity:** Blocker  
**Area:** Source Generator (`NodeStateTemplates.cs`)  
**Status:** ✅ **Fixed** in commit (see template change below)

When a NodeSet2 references a child whose BrowseName is in a foreign namespace
(e.g. Pumps' `Configuration` child has `BrowseName="3:Configuration"` —
namespace 3 = DI), the generator emitted a `case` clause referencing
`DI.BrowseNames.Configuration` — but DI's source-generated `BrowseNames`
class only contains names declared by DI's own nodes, so `Configuration`
was missing, producing **CS0117** errors.

**Fix:** Changed the `FindChildCase` template to use a string literal in the
`switch` expression (matched against `browseName.Name`) instead of the
namespace's `BrowseNames.X` constant. Cross-namespace references now compile
without depending on the foreign namespace's constant table.

```diff
- case {{Tokens.BrowseNameNamespacePrefix}}.BrowseNames.{{Tokens.ChildName}}:
+ case "{{Tokens.ChildName}}":
```

(Earlier note about duplicate FluentBuilder members for DataType encodings
turned out to be a downstream symptom of G10 — once G10 was fixed, the
duplicates disappeared too.)

---

## G10 — Source Gen: OptionSet `Variant.From<T>` Misroute (Fixed)

**Severity:** Blocker  
**Area:** Source Generator (`DataTypeGenerator.cs`, `ModelDesignExtensions.cs`)  
**Status:** ✅ **Fixed** in commit (see two-part fix below)

OptionSet types (e.g., `DeclarationOfConformityOptionSet`) inherit from
`Opc.Ua.OptionSet` (a class), so `Variant.From<T> where T : struct, Enum`
does not apply (**CS0453**). Two emission paths needed fixing:

**Part 1 — Activator emission** (`DataTypeGenerator.cs`):
```diff
  if (datatype.BasicDataType == BasicDataType.Enumeration &&
-     datatype.IsEnumeration)
+     datatype.IsEnumeration &&
+     !datatype.IsOptionSet)
  {
      return DataTypeTemplates.EnumerationActivatorClass;
  }
```
(Applied to both `LoadTemplate_ListOfActivatorClasses` and
`LoadTemplate_ListOfActivatorRegistrations`.)

**Part 2 — Default value emission** (`ModelDesignExtensions.cs`):
Route OptionSet defaults through `Variant.FromStructure<T>` (which requires
`T : IEncodeable` — satisfied by `Opc.Ua.OptionSet`'s generated partial).
```diff
  case BasicDataType.Enumeration:
      if (dataType.BaseTypeNode?.SymbolicId ==
          new XmlQualifiedName("OptionSet", Namespaces.OpcUa))
      {
-         return MakeReturnType(CoreUtils.Format("new {0}()",
-             dataType.SymbolicName.AsFullyQualifiedTypeSymbol(namespaces)));
+         return MakeReturnType(
+             CoreUtils.Format("new {0}()",
+                 dataType.SymbolicName.AsFullyQualifiedTypeSymbol(namespaces)),
+             "Structure");
      }
```

---

## G11 — Source Gen: Missing CreateOrReplace*_Placeholder (Fixed)

**Severity:** High  
**Area:** Source Generator (`NodeStateGenerator.cs`)  
**Status:** ✅ **Fixed** in commit (see initializer code below)

When a type's child slot uses `OptionalPlaceholder`/`MandatoryPlaceholder`
(cardinality 0..N), the type's NodeState class gets `Add*_Placeholder`
methods (taking a `QualifiedName` to disambiguate concrete instances),
**not** `CreateOrReplace*_Placeholder` methods (which only exist for
fixed Mandatory/Optional slots).

The initializer generator was emitting `state.CreateOrReplace*_Placeholder`
unconditionally whenever a child's `ModellingRule` was Optional/Mandatory,
including for instances of placeholder slots that inherited an Optional
rule on the parent. This produced **CS1061** ("method does not exist")
errors.

**Fix:** When the child's `SymbolicName.Name` ends in `_Placeholder`, fall
through to the `state.AddChild(Create...)` path instead of emitting the
non-existent `CreateOrReplace*_Placeholder` call.

```csharp
bool isPlaceholderSlot = instance.SymbolicName.Name
    .EndsWith("_Placeholder", StringComparison.Ordinal);
if (!isPlaceholderSlot)
{
    switch (instance.ModellingRule)
    {
        case ModellingRule.Mandatory:
        case ModellingRule.Optional:
            // emit state.CreateOrReplace*
        ...
    }
}
```
