# Opc.Ua.PackML.Server

Provider-backed standalone OPC 30050 PackML 1.01 units with status,
administration and the nested Base/Machine/Execute state machines.
Use `AddPackML` on the fluent server builder, compose `PackMLRuntime` inside
an existing asynchronous node manager, or create an unregistered unit with
`PackMLUnit.CreateAsync`.

The equipment provider authorizes and accepts requests. Equipment completion
is reported separately with the accepted request identity and expected acting
state. The runtime does not implement physical safety logic or replay
uncertain commands.

See the [PackML guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/PackML.md)
for modes, optional capabilities, persistence, lifecycle and the simulated sample.
