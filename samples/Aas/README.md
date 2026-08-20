# AAS V3 sample

This sample starts one local OPC UA server that exposes both AAS V3 companion-spec halves:

* the AAS registry (`AASRegistry`) with a shell group, submodel resources, a concept dictionary, and a package resource;
* the AAS metamodel address space materialized from an `AasEnvironment`.

The same process then starts a small OPC UA client. The client resolves the registry, calls
`LookupShellsByAssetLink` and `GetSubmodel`, reads the live typed carbon-footprint value from the materialized
metamodel nodes, calls the live `Invoke` Method on the materialized AAS Operation, and verifies the published package
digest.

Run from the repository root:

```powershell
dotnet run --project samples\Aas\AasSample.csproj -f net10.0
```
