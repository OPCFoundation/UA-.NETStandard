# OPCFoundation.NetStandard.Opc.Ua.MachineVision.Client

Typed OPC 40100-2 asset operations for a connected Machine Vision session.
Provides system-asset discovery, bounded inventory/health snapshots, generated
asset proxies, and paged optical, data, interface, and same-entity relationships.

The extension methods are in `Opc.Ua.Vision.Client`. Reference this package
alongside the base Vision client to use `EnumerateAssetsAsync`, `ReadAssetAsync`,
`Asset`, and `ReadRelationshipsAsync` on `MachineVisionClient`.

The Part 2 namespace can be used independently of Part 1 or the Perception model.
One `Opc.Ua.Mcp.Vision` package exposes tools for all three models.
This package follows the repository root version.

See the [Machine Vision guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/Vision.md).

## License

OPC Foundation MIT License 1.00 - <http://opcfoundation.org/License/MIT/1.00/>
