# OPC UA .NET Standard — HTTPS REST binding (opt-in)

## Overview

`OPCFoundation.NetStandard.Opc.Ua.Bindings.WebApi` is the
ASP.NET Core MVC binding that implements the **OPC UA OpenAPI Mapping**
defined by [OPC UA Part 6 §G.3](https://reference.opcfoundation.org/Core/Part6/v105/docs/G.3) (v1.05.07).

Every OPC UA service is exposed as a `POST /<service>` route whose
request and response bodies are the corresponding `<Service>Request` /
`<Service>Response` types serialized with the OPC UA JSON encoding from
Part 6 §5.4. Both the **Compact** (default; mandatory per §5.4.9) and
**Verbose** flavours are supported and negotiated through the
`application/json; encoding=compact|verbose` media-type parameter on
`Accept` and `Content-Type`.

## Target frameworks

`net8.0`, `net9.0`, `net10.0`. The package is not provided on `net472`
/ `net48` / `netstandard2.x` because the ASP.NET Core MVC,
`Microsoft.AspNetCore.OpenApi`, and `Microsoft.AspNetCore.App`
framework-reference APIs used here are only available on net8+.
Consumers on older runtimes should use
`OPCFoundation.NetStandard.Opc.Ua.Bindings.Https` for the
`application/opcua+uajson` JSON sub-profile.

## Relationship to `Opc.Ua.Bindings.Https`

This package **adds** a REST surface; it does not replace the
HTTPS-binary or HTTPS-JSON bindings shipped by `Opc.Ua.Bindings.Https`.
In the default *shared* hosting mode the MVC controllers are mounted
into the same Kestrel pipeline as those bindings, so a single port
serves binary + `opcua+uajson` + REST at the same time. An opt-in
*own listener* mode runs the REST surface on its own Kestrel host.

## Getting started

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua;
using Opc.Ua.Bindings;

services
    .AddOpcUa()
    .AddHttpsTransport()        // existing binary + opcua+uajson bindings
    .AddWebApiTransport();     // adds the REST controllers + discovery URI
```

The binding emits a dedicated `TransportProfileUri`
(`http://opcfoundation.org/UA-Profile/Transport/https-webapi`) in
`GetEndpoints` so OPC UA clients can discover it. SecurityMode is
`None` only — transport security is provided exclusively by TLS at
the HTTPS layer.

## Additional documentation

See the [REST binding guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/WebApi.md)
and the [Transport Profiles guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/Transports.md)
in the main repository for the full mapping table, authentication
models, hosting modes, and the symmetric C# `IWebApiClient`.
