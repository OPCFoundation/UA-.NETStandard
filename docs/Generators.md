# Generators (generating sets)

The draft **Generators companion specification**
(`http://opcfoundation.org/UA/Generators/`) realised end to end by
[`GeneratorDeviceIntegrationServer`](../samples/GeneratorDeviceIntegrationServer),
and composed with the pump sample by
[`SiteCompositionServer`](../samples/SiteCompositionServer).

## The model

A generating set is two things at once: an **asset** with a nameplate and a
health, and a **machine** whose core is an engine coupled to an alternator. The
specification layers on existing building blocks rather than reinventing them:

| Layer | Role |
|---|---|
| DI (OPC 10000-100) | `GeneratorSetType` derives from `DeviceType`, inheriting the nameplate and `DeviceHealth`; each subsystem derives from `ComponentType` |
| Machinery (OPC 40001-1) | `Identification` add-in and the `MachineryItemState` / `MachineryOperationMode` building blocks |
| Part 9 | `GeneratorProtectionAlarmType`, a subtype of `OffNormalAlarmType` |
| Part 16 | `GeneratorStateMachineType`, a twelve-state `FiniteStateMachineType` |

Composition over deep inheritance: subsystems are separate `ComponentType`
subtypes referenced by `HasComponent`, so a set is assembled from exactly the
components it has.

## Source generation

The sample generates the model locally from vendored NodeSet2 files rather than
referencing a library:

```xml
<AdditionalFiles Include="Model\Opc.Ua.Generators.NodeSet2.xml">
  <ModelSourceGeneratorPrefix>Opc.Ua.Generators</ModelSourceGeneratorPrefix>
</AdditionalFiles>
<AdditionalFiles Include="Model\Opc.Ua.Generators.NodeSet2.csv" />
```

The CSV alongside each XML is the **stable NodeId table**. Without it the
generator assigns synthetic NodeIds at compile time, which breaks byte-stable
wire formats for clients that cache NodeIds across builds.

### The Machinery dependency

The specification composes three Machinery types — `MachineryItemState_StateMachineType`,
`MachineryOperationModeStateMachineType` and `MachineIdentificationType` — that the
reduced Machinery nodeset carried by the pump sample does not define. The full
official nodeset defines them but **does not survive the model source generator**
(it fails with `MODELGEN003`), and it declares a dependency on the IA namespace
through a single optional `Stacklight` member that a generating set does not have.

`Model/prepare_machinery_nodeset.py` therefore derives a reduced-but-sufficient
set from the official nodeset by whitelist, strips the references left dangling
and drops the IA dependency. Deriving it mechanically keeps the provenance
checkable, and the whitelist is the only thing to edit when more types are needed.

Removing the IA URI is index-safe because it is the last entry in
`NamespaceUris`, so the Machinery (1) and DI (2) indices used throughout the file
do not move.

## Simulation design

The sample's organising idea is that **load fraction is the only independent
variable**. Everything else is a function of it:

```
V̇_f(x) = 3.67 + 100·x                      fuel rate       [L/h]
η(x)   = P(x) / (V̇_f(x) · ρ · LHV)         efficiency
S      = P / PF                            apparent power  [VA]
I      = S / (√3 · V_LL)                   line current    [A]
f      = N · p / 120                       frequency       [Hz]
```

This is not a stylistic choice. When each measurement is an independent
oscillator — as the pump sample's simulation once was — a server happily
publishes a duty point that no real machine could occupy, and no test can catch
it because there is nothing for the values to be inconsistent *with*. Deriving
them from one variable means `P = √3·V·I·PF` and `η = P/(V̇·ρ·LHV)` hold at every
tick by construction, and `GeneratorDatasheetConformanceTests` asserts exactly
that.

The published datasheet, the engineering ranges, the trip points and the
simulation all read the same constants, so they cannot drift apart.

## Cross-server composition

`SiteCompositionServer` demonstrates composing several servers at a supervisory
level. It owns no devices; it publishes a site stage and one **cross-server
component binding** per subordinate, carrying that server's `ComponentServerUri`
and `ComponentEndpointUrl`:

```
Opc.Ua.OpenUsd.Connector --server <site> --federate --view
```

With `--federate` the connector opens a session to each named server, discovers
its representations and drives its bindings into the same stage — one scene, live
machines, three servers.

Nothing is mirrored. The site server never proxies a subordinate's address space,
so there is no cache to invalidate and no second copy of the truth.

`--federate` is opt-in because the endpoint the connector dials comes from the
server being rendered rather than from the operator, which makes honouring it a
trust decision. Federation is also best-effort per component: a subordinate that
is down is logged and skipped, and the rest of the scene still renders.

## See also

- [`GeneratorDeviceIntegrationServer`](../samples/GeneratorDeviceIntegrationServer) and its [datasheet](../samples/GeneratorDeviceIntegrationServer/DATASHEET.md)
- [`SiteCompositionServer`](../samples/SiteCompositionServer)
- [OpenUSD](OpenUsd.md)
- [Device Integration](DeviceIntegration.md)
- [State machines](StateMachines.md)
