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
or perform calibration. `Opc.Ua.IA` remains the single generated-model package.

See the [Industrial Automation guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/IndustrialAutomation.md)
and [public-API sample](https://github.com/OPCFoundation/UA-.NETStandard/tree/master/samples/IA).
