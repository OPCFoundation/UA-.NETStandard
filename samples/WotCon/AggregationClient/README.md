# AggregationClient

This one-shot client orders and uploads the checked-in WoT documents, substitutes the two source endpoints, calls registry `Refresh`, browses the materialized Pump, reads values from both sources, and prints the result.

See the [WoT aggregation sample guide](../README.md) for complete commands and expected behavior.

`BuildHost` and the executable default to trusted certificates and
`SignAndEncrypt` / `Basic256Sha256`. Supply `AggregationClientOptions.IdentityProvider`
from your secret registry when using authenticated registry management; the
stock CLI uses anonymous identity and is therefore denied by the aggregate's
default management policy. See the guide's programmatic authenticated example.

`--auto-accept` relaxes only unknown-server certificate trust, while
`--security-none` independently selects an unsigned, unencrypted endpoint.
Both are explicit, default-false lab opt-ins and accept `=false`. Neither grants
registry-management privileges. The optional `--exerciseControls true` workflow
also connects to the sources directly; provision those trust pairs as well.

```powershell
# Isolated lab only; the aggregate must explicitly allow anonymous management.
dotnet run --project samples\WotCon\AggregationClient -f net10.0 -- --security-none
```
