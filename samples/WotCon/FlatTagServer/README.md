# FlatTagServer

This sample runs one deterministic flat OPC UA source for the WoT aggregation scenario. Start it twice with the Source A and Source B namespace/endpoint options from the [WoT aggregation sample guide](../README.md).

The server intentionally exposes flat variables rather than a Pumps companion-model hierarchy; the generic aggregation server creates that hierarchy from the checked-in WoT documents.

The CLI and `FlatTagServerHost.Build(new FlatTagServerOptions())` require trusted
client certificates and do not offer None by default. Provision a stable,
per-instance `--pkiRoot` and mutually trust the aggregation host. `--auto-accept`
only relaxes unknown-certificate trust; `--security-none` independently offers
an unsigned, unencrypted endpoint. Both default to false and print warnings
when enabled. `--help` has no PKI or network side effects.

An isolated unsecured Source A example is:

```powershell
dotnet run --project samples\WotCon\FlatTagServer -f net10.0 -- --port 62551 --security-none
```

Omit `--security-none` for provisioned secure use. Use the
[secure topology instructions](../README.md#secure-provisioning-and-authenticated-management)
for all required trust pairs and distinct Source A/Source B identities.
