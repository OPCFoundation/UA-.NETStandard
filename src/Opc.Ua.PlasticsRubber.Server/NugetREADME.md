# OPC UA plastics and rubber server capabilities

Composable OPC 40083 capability bindings, OPC 40082-6 granulate-drying devices,
and generated fluent accessors.
The `Opc.Ua.PlasticsRubber` model package owns the standard types; this package
does not duplicate their namespace or invent a universal machine root.

Use `AddPlasticsRubberToMachinery()` with an existing Machinery owner, or
`AddPlasticsRubber()` for a standalone owner. Supply an
`IPlasticsRubberAccessPolicy` and asynchronous device providers explicitly.
`ConfigurePlasticsRubberFor<TNodeManager>()` supports an existing custom owner.

The direct path binds already-created, generated capabilities:

```csharp
var runtime = new PlasticsRubberRuntime(owner, accessPolicy);
await runtime.BindStartDeviceAsync(startDevice, startProvider);
await runtime.BindMonitoredParameterAsync(parameter, parameterProvider);
```

Optional members are selected before binding. The provider remains authoritative
for committed device state, production counters, supervision and hardware
effects. Method acceptance does not imply that physical movement or tuning has
finished. Original caller context is preserved; the package installs neither
an allow-all policy nor an implicit simulated machine.

Common production datasets use typed storage options, metadata and bounded,
session-owned temporary files through the existing stack file infrastructure.
The package exposes injectable bindings and direct construction.

Granulate-drying behavior is included under the `Opc.Ua.GranulateDrying.Server`
CLR namespace. Use `AddGranulateDrying()` for standalone hosting or
`AddGranulateDryingToMachinery()` with an existing owner; supply an
`IGranulateDryingProvider` and `IGranulateDryingAuthorization`.
There is no separate granulate-drying package.

See the [common capability guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/PlasticsRubber.md)
and [production dataset guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/PlasticsRubber.md#production-dataset-protocol)
for requirements, examples and protocol details.

The included `licenses/CompanionModels.NOTICE.txt` describes model provenance.
The common model independently implements the 1.03 interface changes on the
MIT-licensed 1.02 basis; it is not a redistribution of the official 1.03 XML.
No formal OPC Foundation certification is claimed.
