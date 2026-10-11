# OPCFoundation.NetStandard.Opc.Ua.Vision.Machinery

OPC 40100-2 Machine Vision asset management and condition monitoring models:
generated nodes, structures, typed proxies, and a release-pinned NodeSet.
The model uses DI and Machinery; it does not require the Part 1 control model.

The wire namespace is `http://opcfoundation.org/UA/MachineVision/AMCM/`.
Generated types are in `Opc.Ua.Vision.MachineVision.AMCM`.

Use `Opc.Ua.Vision.Machinery.Server` for asset builders and hosting, and
`Opc.Ua.Vision.Machinery.Client` for discovery, snapshots, and relationships.
Part 1 and the Perception model are supplied by `Opc.Ua.Vision`. All three
can describe the same system through explicit same-entity relationships.

This package follows the repository root version. Only the optional
`Opc.Ua.Vision.OpenUsd` renderer is preview-only within the Vision family.

See the [Machine Vision guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/Vision.md).

## License

OPC Foundation MIT License 1.00 - <http://opcfoundation.org/License/MIT/1.00/>
