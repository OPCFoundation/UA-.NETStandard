# OPC UA .NET Standard — Quickstart Servers

`OPCFoundation.NetStandard.Opc.Ua.Quickstarts.Servers` packages the
reference quickstart server `NodeManager`s shipped with the OPC UA
.NET Standard stack — the AggregationServer, the AlarmCondition
server, the DataAccess server, the EmptyServer, the
MemoryBufferServer, and the ReferenceServer sample. They are useful
as turn-key servers for tests and as copy-paste starting points for
custom servers.

## Overview

Reference this package from a test executable, a CI fixture, or a
demo application that needs a fully-functional OPC UA server without
authoring a `NodeManager` from scratch.

## Getting started

```csharp
using Opc.Ua.Configuration;
using Quickstarts.ReferenceServer;

var application = new ApplicationInstance(telemetry)
{
    ApplicationName = "ReferenceServer",
    ApplicationType = ApplicationType.Server,
};
await application.LoadApplicationConfigurationAsync(silent: false).ConfigureAwait(false);
await application.CheckApplicationInstanceCertificatesAsync(true).ConfigureAwait(false);
await application.StartAsync(new ReferenceServer(telemetry)).ConfigureAwait(false);
```

## Target frameworks

`net472`, `net48`, `netstandard2.0`, `netstandard2.1`, `net8.0`,
`net9.0`, `net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the
[Applications folder](https://github.com/OPCFoundation/UA-.NETStandard/tree/master/Applications)
for the full quickstart sources.
