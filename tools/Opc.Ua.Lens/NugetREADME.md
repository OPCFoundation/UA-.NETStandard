# UaLens - OPC UA desktop engineering workspace

UaLens provides a desktop workspace for the OPC Foundation .NET stack:
connect securely, explore an address space, read and write values, call methods,
and monitor values, events, quality and timestamps.

Install the tool package with the version available from your configured feed:

```powershell
dotnet tool install --global OPCFoundation.NetStandard.Opc.Ua.Lens --prerelease
ualens
```

The stable File / View / Tools / Help menu, persistent connection bar and
searchable tool catalog keep common actions close. Advanced settings remain
available within the relevant document. Appearance follows the operating system
unless Light or Dark is chosen explicitly.

Included tools cover monitoring/charts, events, historical access, file operations,
local certificates, GDS discovery and certificate management, user/role administration,
write/call performance workloads, and a live-scaling Subscription Bench.

Workspaces store configuration and connection-policy intent, not passwords,
private keys, bearer tokens or active jobs. Certificate trust requires an explicit
decision; accepting one certificate once does not establish global trust.

See the [UaLens guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/UaLens.md)
for current workflows, server prerequisites and capability limits.
