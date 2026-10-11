# OPCFoundation.NetStandard.Opc.Ua.MachineVision.Server

OPC 40100-2 asset builders, identification, maintenance/health information,
condition-monitoring updates, and optical/data relationships. Builders live
in `Opc.Ua.Vision.Server` and reuse its shared context and resource ownership.

For a standalone Vision manager, call `AddVision(...).AddVisionMachinery()`.
For an existing Machinery owner, call `AddVisionToMachinery()` and configure
instances through `ConfigureVisionFor<MachineryNodeManager>`.
Add multiple uniquely named systems/assets within that owner rather than
registering another manager for the same DI/Machinery namespace.

Part 1 controls and the Perception model can be combined with Part 2 assets.
Selecting assets without control omits the Part 1 namespace; the standalone
Vision host also supplies its Perception root. The package follows
the repository root version; the optional Vision.OpenUsd renderer remains preview-only.

See the [Machine Vision guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/Vision.md).

## License

OPC Foundation MIT License 1.00 - <http://opcfoundation.org/License/MIT/1.00/>
