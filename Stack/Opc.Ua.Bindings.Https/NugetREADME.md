# OPC UA .NET Standard — HTTPS binding

`OPCFoundation.NetStandard.Opc.Ua.Bindings.Https` adds the
`https://` / `opc.https://` (HTTPS-binary) server listener on top of
`OPCFoundation.NetStandard.Opc.Ua.Core`. The listener is hosted by
Kestrel.

## Overview

The package contains:

- `HttpsTransportListener` — a Kestrel-hosted listener that serves the
  `https-uabinary` transport profile for an OPC UA server.
- `HttpsServiceHost` — publishes the HTTPS endpoint descriptions during
  discovery.

Reference the package from an OPC UA **server** that should expose an
HTTPS endpoint. The matching client-side `HttpsTransportChannel` ships
in `OPCFoundation.NetStandard.Opc.Ua.Core`, so a pure client does not
need this package.

## Getting started

The binding is discovered automatically once the assembly is
referenced. Add an `https://` (or `opc.https://`) base address to the
server's endpoint configuration to activate it:

```xml
<BaseAddresses>
  <ua:String>https://localhost:62541/MyServer</ua:String>
</BaseAddresses>
```

## Target frameworks

`net472`, `net48`, `netstandard2.1`, `net8.0`, `net9.0`, `net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the [Transport Profiles overview](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/Profiles.md)
for the supported URI schemes and security profiles, and the
[Reverse Connect guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/ReverseConnect.md).
