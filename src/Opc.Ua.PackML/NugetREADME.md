# Opc.Ua.PackML

Source-generated **OPC 30050 PackML** information model, the OPC UA mapping of
ISA-TR88.00.02.

The package exposes the generated ObjectTypes (`PackMLMachineStateMachineType`,
`PackMLBaseStateMachineType`, `PackMLExecuteStateMachineType`, the status,
admin and command objects), enums, typed node states and client proxies, plus
the `AddOpcUaPackML` model loader. The generated `ObjectTypeIds` /
`VariableTypeIds` classes and `Opc.Ua.PackML.Namespaces` are the source of
truth for the model.

`PackMLStateMachineController` drives a base state machine instance together
with its nested machine and execute state machines: it activates a sub-state
machine when its parent enters the hosting state and deactivates it on the way
out, binds the PackML methods (Abort, Clear, Stop, Reset, Start, Hold, Unhold,
Suspend, Unsuspend, ToComplete) with an `Executable` attribute that follows the
current state, and leaves the acting states either automatically or once the
equipment calls `CompleteActingState`. `PackMLStateNumbers` carries the
ISA-TR88.00.02 state numbers.

The NodeSet is the unmodified OPC Foundation publication from
`UA-Nodeset/latest/PackML` (model version 1.01, 2020-10-08). It depends on the
base namespace only.

PackML is composed by other companion specifications, notably OPC 40200
Weighing Technology (`Opc.Ua.Scales`), whose `ScaleDeviceType` carries an
optional PackML `State` machine.

## Target frameworks

net472, net48, netstandard2.1, net8.0, net9.0, net10.0

## Additional documentation

See the [Scales developer guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/Scales.md).
