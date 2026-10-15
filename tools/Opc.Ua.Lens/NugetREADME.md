# UaLens - OPC UA desktop engineering workspace

UaLens provides a desktop workspace for the OPC Foundation .NET stack:
connect securely, explore an address space, read and write values, call methods,
and monitor values, events, quality and timestamps.

Open one or more NodeSet2 XML files in the same explorer for read-only offline
model inspection. The core OPC UA model is bundled. Missing dependencies can
be selected locally or downloaded with permission from OPCFoundation/UA-Nodeset.

Install the tool package with the version available from your configured feed:

```powershell
dotnet tool install --global OPCFoundation.NetStandard.Opc.Ua.Lens --prerelease
ualens
```

Debug builds use the package ID `OPCFoundation.NetStandard.Opc.Ua.Lens.Debug`
so their artifacts remain distinct from Release packages. Both expose `ualens`;
install only one configuration in a given tool scope.

Use the .NET 10 SDK to install the root package. It selects the matching
`win-x64`, `linux-x64` or `osx-arm64` payload automatically. The selector and
all three platform packages must be available at the same version in the feed;
the suffixed packages are not installed directly. Other RIDs have no
RID-agnostic fallback. Platform-specific rendering prerequisites and
qualification limits still apply.

The File / View / Tools / Help menu, persistent connection bar and searchable
tool catalog provide access to common actions. Document actions and document or
connection settings let you perform advanced operations and control their
configuration. Appearance follows the operating system unless Light or Dark is
chosen explicitly.

Included tools cover monitoring/charts, events, historical access, file operations,
local certificates, GDS discovery and certificate management, user/role administration,
write/call performance workloads, and a live-scaling Subscription Bench. Alarms,
Models, Continuity Lab, PubSub and Companion Tasks provide condition-aware operation,
structured-value inspection, recovery evidence, dataset observation and typed
companion workflows.

Dedicated Asset Management, Machinery, Pumps, Weighing, PackML and Positioning
documents reuse the same primary connection and prepared-operation controls.
Published Robotics and draft Robot Intent remain distinct; only explicitly
authorized owned simulations can perform supported tasks.

OpenUSD representations can be explicitly opened in a bounded, in-process native
viewport with read-only live telemetry, selection-only picking and local simulated
Vision frames. The retained stage and native resources are drained on Stop or close.
Loading a workspace never starts rendering. Supported scene profiles, asset/frame
bounds and platform-specific qualification limits are documented in the guide.

The diagnostics panel also offers explicit, bounded UA-TCP and UDP/UADP packet
capture and local PCAP replay. Raw packet export is separate from redacted
diagnostic metadata. Decryption keys are not collected, captured traffic is never
sent by replay, and private recordings are removed on Clear or application shutdown.

Workspaces store configuration and connection-policy intent, not passwords,
private keys, bearer tokens or active jobs. Certificate trust requires an explicit
decision; accepting one certificate once does not establish global trust.
X.509, issued-token, hardware-key and reverse-connect workflows require explicit
provider and server prerequisites. PubSub traffic and lab experiments are opt-in
and never restart merely because a workspace was loaded.

See the [UaLens guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/UaLens.md)
for current workflows, server prerequisites and capability limits.
