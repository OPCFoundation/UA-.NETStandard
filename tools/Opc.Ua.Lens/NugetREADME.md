# UaLens - OPC UA desktop engineering workspace

UaLens is an Avalonia desktop workspace for exploring and administering OPC UA
servers, monitoring data and inspecting offline information models.

See the [user guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/UaLens.md)
for workflows, connection and certificate trust, server prerequisites, saved
workspaces and capability limits.

## Install

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
RID-agnostic fallback. For rendering prerequisites and the scope of platform
checks, see
[artifact qualification](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/tools/Opc.Ua.Lens/README.md#publish-and-package).
