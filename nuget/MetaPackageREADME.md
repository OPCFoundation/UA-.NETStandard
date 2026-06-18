# OPC UA .NET Standard — meta-package

`OPCFoundation.NetStandard.Opc.Ua` is a convenience meta-package that
brings in the standard OPC UA .NET Standard stack components in one
reference. It does not contain any types of its own; install it when
you want the curated default set of packages an OPC UA application
typically needs.

## What you get

The meta-package depends on:

- `OPCFoundation.NetStandard.Opc.Ua.Types`
- `OPCFoundation.NetStandard.Opc.Ua.Core.Types`
- `OPCFoundation.NetStandard.Opc.Ua.Core`
- `OPCFoundation.NetStandard.Opc.Ua.Security.Certificates`
- `OPCFoundation.NetStandard.Opc.Ua.Configuration`
- `OPCFoundation.NetStandard.Opc.Ua.Client`
- `OPCFoundation.NetStandard.Opc.Ua.Server`
- `OPCFoundation.NetStandard.Opc.Ua.Gds.Common`
- `OPCFoundation.NetStandard.Opc.Ua.Gds.Client.Common`
- `OPCFoundation.NetStandard.Opc.Ua.Gds.Server.Common`
- `OPCFoundation.NetStandard.Opc.Ua.SourceGeneration`

Pick individual packages instead of the meta-package when you want a
tighter dependency surface (e.g. client-only or server-only
applications).

## Getting started

```csharp
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

// Open a session to an opc.tcp server.
var application = new ApplicationInstance(telemetry)
{
    ApplicationName = "MyClient",
    ApplicationType = ApplicationType.Client,
};
await application.LoadApplicationConfigurationAsync(silent: false).ConfigureAwait(false);
await application.CheckApplicationInstanceCertificatesAsync(true).ConfigureAwait(false);
```

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the [Docs folder](https://github.com/OPCFoundation/UA-.NETStandard/tree/master/Docs)
for the full design guides, transport profiles, certificate
management, identity providers, and the 1.5.378 → 2.0 migration
guide.
