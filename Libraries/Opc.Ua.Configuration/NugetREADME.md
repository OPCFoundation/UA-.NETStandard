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
- `ConfigureApplication(...)` — shared DI application options used by
  `AddClient(...)`, `AddServer(...)`, or a combined client/server host.
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

For dependency-injected applications:

```csharp
services.AddOpcUa()
    .ConfigureApplication(options =>
    {
        options.ApplicationName = "MyApplication";
        options.ApplicationUri = "urn:localhost:MyApplication";
        options.ProductUri = "uri:example.com:MyApplication";
    })
    .AddClient(options => options.Session = new ManagedSessionOptions());
```

## Target frameworks

`net472`, `net48`, `netstandard2.1`, `net8.0`, `net9.0`,
`net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the [Dependency Injection guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/DependencyInjection.md)
for the application builder surface, and the
[Docs folder](https://github.com/OPCFoundation/UA-.NETStandard/tree/master/Docs)
for the configuration file schema and certificate management.
