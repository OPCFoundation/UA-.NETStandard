# OPC UA .NET Standard — PubSub legacy compatibility

`OPCFoundation.NetStandard.Opc.Ua.PubSub.Legacy` ships the `[Obsolete]`
1.04-era PubSub shim types (for example `UaPubSubApplication`,
`UaPubSubConnection`, and the Newtonsoft-based JSON encoder) that were
split out of `OPCFoundation.NetStandard.Opc.Ua.PubSub` during the 2.0
modernization.

Add this package **only** if you still consume the obsolete PubSub API and
cannot yet migrate to the modern fluent / DI surface. New code should
depend on `OPCFoundation.NetStandard.Opc.Ua.PubSub` directly.

## Target frameworks

`net472`, `net48`, `netstandard2.1`, `net8.0`, `net9.0`, `net10.0`.

## Additional documentation

See the [PubSub migration guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/migrate/2.0.x/pubsub.md)
for how to move off the legacy API.
