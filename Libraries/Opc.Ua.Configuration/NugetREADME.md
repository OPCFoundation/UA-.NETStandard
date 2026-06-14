# OPC UA .NET Standard — Configuration

`OPCFoundation.NetStandard.Opc.Ua.Configuration` is the application
bootstrap library. It contains the `ApplicationInstance` /
`IApplicationInstance` surface that loads `ApplicationConfiguration`
from disk (or fluent builder), provisions the application instance
certificate, and starts a server via
`ApplicationInstance.StartAsync(IServerBase, …)`.

## Overview

Reference this package from every server, GDS, or LDS executable. The
package provides:

- `ApplicationInstance` — load configuration, ensure application
  certificate, start the server.
- `IApplicationConfigurationBuilder` — fluent configuration builder
  (replaces XML-only configuration).
- `CertificatePasswordProvider` integration for encrypted application
  certificates.
- `ConfigurationWatcher` — hot-reload of `Application.xml`.

## Getting started

```csharp
using Opc.Ua;
using Opc.Ua.Configuration;

var application = new ApplicationInstance(telemetry)
{
    ApplicationName = "MyServer",
    ApplicationType = ApplicationType.Server,
};

await application.Build("urn:localhost:MyServer", "uri:example.com:MyServer")
    .AsServer(new[] { "opc.tcp://localhost:62541/MyServer" })
    .Create()
    .ConfigureAwait(false);

await application.CheckApplicationInstanceCertificatesAsync(true).ConfigureAwait(false);
await application.StartAsync(new MyServer(telemetry)).ConfigureAwait(false);
```

## Target frameworks

`net472`, `net48`, `netstandard2.0`, `netstandard2.1`, `net8.0`,
`net9.0`, `net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the [Configuration guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/Configuration.md)
for the full builder surface and configuration file schema.
