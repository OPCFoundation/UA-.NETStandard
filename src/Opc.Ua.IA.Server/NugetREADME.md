# Opc.Ua.IA.Server

Provider bindings and composition builders for OPC 10000-200 Industrial
Automation 1.01.4: stacklight observations, application-owned aggregate/rolling
statistics with authorized reset, and calibration-target information.

Register `.AddIndustrialAutomation()` on an existing DI or companion server.
Use `.ConfigureIndustrialAutomationFor<TNodeManager>()` to configure the
existing owner after its models have loaded. Direct constructors accept that
owner's system context and generated IA nodes.

Applications own acquisition, calculation, storage, and reset policy.
The bindings preserve source quality and timestamps. They do not operate
physical lights, calculate unspecified analytics, retrieve certificate URIs,
or perform calibration. `Opc.Ua.IA` remains the single producer of the IA model.

Ordinary decoded statistics accept exact `EnumValueType` and `Argument` bodies
through field-wise copies. Registering an `IEncodeableType` does not establish
ownership; custom and derived bodies are rejected with `BadNotSupported`.
Deferred localized text retains its templates and detached arguments.

`WithCombinedPublication` stages statistics and companion readings before any
notification. This opt-in path additionally copies exact decoded ECM
`AcPeDataType` and `AcPpDataType` values through generated primitive-field
assignments; it does not invoke their virtual codecs or clone methods.
`Opc.Ua.IA.Server` therefore depends on the `Opc.Ua.ECM` model package, which
depends on the IA and DI models, not on IA.Server or ECM.Server. Both model and
server libraries use the same net48/net8.0/net9.0/net10.0 target selection.
No application copier or pre-owned-value registration is exposed.
IA support remains Partial.

See the [Industrial Automation guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/IndustrialAutomation.md)
and [public-API sample](https://github.com/OPCFoundation/UA-.NETStandard/tree/master/samples/IA).
