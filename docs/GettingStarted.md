# Getting started

This guide gets an OPC UA client and server talking on one computer. Choose one
of two entry routes:

- **[Route A: Run the repository samples](#route-a-run-the-repository-samples)**
  if you have cloned this repository, or want to explore it. You build and run
  the minimal server and client samples from source.
- **[Route B: Build applications from NuGet packages](#route-b-build-applications-from-nuget-packages)**
  if you are starting your own project. You create a small server and client
  from the published packages.

Both routes run on your computer without other servers, brokers, credentials,
or cloud accounts. They show slightly different parts of the first workflow:

| Outcome | Route A | Route B |
| --- | --- | --- |
| Encrypted connection (`SignAndEncrypt`, `Basic256Sha256`) | Yes | Yes |
| Certificate trust | The client trusts the server explicitly. The sample server accepts clients automatically. | Each application trusts the other explicitly. |
| Browse the `Objects` folder | Yes, including the sample's Boiler model | Yes, standard server nodes only |
| Read a value and check its status | Yes | Yes |
| Receive data-change notifications | One notification | Three notifications |

After either route, [what the code does](#what-the-code-does) explains each step.
The [concepts guide](Concepts.md) explains the underlying OPC UA terms.

## Contents

- [Before you begin](#before-you-begin)
- [Route A: Run the repository samples](#route-a-run-the-repository-samples)
  - [A1. Start the server](#a1-start-the-server)
  - [A2. Run the client and trust the server](#a2-run-the-client-and-trust-the-server)
  - [A3. Check the result](#a3-check-the-result)
  - [A4. Stop and clean up](#a4-stop-and-clean-up)
- [Route B: Build applications from NuGet packages](#route-b-build-applications-from-nuget-packages)
  - [B1. Create the server](#b1-create-the-server)
  - [B2. Create the client](#b2-create-the-client)
  - [B3. Trust each application](#b3-trust-each-application)
  - [B4. Check the result](#b4-check-the-result)
  - [B5. Stop and clean up](#b5-stop-and-clean-up)
  - [Connect to another server](#connect-to-another-server)
- [What the code does](#what-the-code-does)
- [Troubleshooting](#troubleshooting)
- [Next steps](#next-steps)

## Before you begin

You need two terminal windows. The commands are shown for PowerShell and for
bash where they differ.

| Requirement | Route A | Route B |
| --- | --- | --- |
| .NET SDK | The exact version in [`global.json`](../global.json). Roll-forward is disabled, so other SDK versions are refused. | .NET 10 SDK. |
| Other requirements | Git, and internet access to restore NuGet packages on the first build | Internet access to nuget.org |
| TCP port | 62541 | 62560 |

Route B was verified with package version `2.0.0`.

## Route A: Run the repository samples

Route A uses two samples from `samples/MinimalApi`:

- [`MinimalBoilerServer`](../samples/MinimalApi/MinimalBoilerServer) hosts a
  small Boiler model. Its node manager is generated from the model at build time.
- [`MinimalClient`](../samples/MinimalApi/MinimalClient/README.md) discovers the
  server's encrypted endpoint, creates a subscription, browses, reads, and
  waits for the subscription's first data-change notification.

### A1. Start the server

```bash
git clone https://github.com/OPCFoundation/UA-.NETStandard.git
cd UA-.NETStandard
dotnet run --project samples/MinimalApi/MinimalBoilerServer
```

The first build compiles the stack and can take a few minutes. The server is
ready when it logs:

```text
OPC UA server listening at opc.tcp://localhost:62541/MinimalBoilerServer.
```

Leave the server running.

> **Local sample only:** MinimalBoilerServer sets
> `AutoAcceptUntrustedCertificates = true`, so it accepts every client
> certificate and logs `Auto accepted certificate`. Never use this setting in
> production. Route B shows how a server trusts a client explicitly.

### A2. Run the client and trust the server

In the second terminal, at the repository root, run:

```bash
dotnet run --project samples/MinimalApi/MinimalClient
```

The client does not trust the new server certificate yet. It logs
`BadCertificateUntrusted`, saves the server certificate in its *rejected*
store, and keeps retrying. While it retries, move the certificate to the
client's *trusted* store. The client keeps its stores in the system temporary
directory.

PowerShell:

```powershell
$pki = Join-Path ([IO.Path]::GetTempPath()) 'OPC Foundation/MinimalClient/pki'
New-Item -ItemType Directory -Force "$pki/trusted/certs" | Out-Null
Move-Item "$pki/rejected/certs/*.der" "$pki/trusted/certs"
```

bash:

```bash
pki="${TMPDIR:-/tmp}/OPC Foundation/MinimalClient/pki"
mkdir -p "$pki/trusted/certs"
mv "$pki"/rejected/certs/*.der "$pki/trusted/certs/"
```

These commands move every certificate the client has rejected. In this
exercise, that is only the MinimalBoilerServer certificate. Outside a local
test, confirm a certificate's thumbprint with the server's administrator
before you trust it; the thumbprint is part of the file name.

The client retries with an increasing delay of up to 30 seconds, so it connects
within about half a minute after you move the certificate. You can also stop it
with Ctrl+C and run it again. If it cannot connect for five minutes, it gives up.

### A3. Check the result

After the rejection messages, the client prints output like this:

```text
Using endpoint: opc.tcp://localhost:62541/MinimalBoilerServer
Security mode: SignAndEncrypt
Security policy: http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256
Connected!

A&C client ready: AlarmClient
Subscription state: Opened

Browsing Objects folder...
Subscription state: Created
Found 4 references
  - Server (Object)
  - Aliases (Object)
  - Locations (Object)
  - Boilers (Object)

Reading ServerStatus.CurrentTime...
Server time: 28/09/2026 11:43:42

Waiting for a data change notification...
Subscription state: Modified
Subscription value: 28/09/2026 11:43:43

Disconnecting...
Subscription state: Deleted
Subscription state: Opened
Done
```

`Boilers` is the folder of the sample's generated model; the other three nodes
are standard server nodes. `Subscription value` is the data-change notification
for the monitored server time. The times and their format depend on your
computer.

### A4. Stop and clean up

Press Ctrl+C in the server terminal. The samples keep their certificates,
including private keys, in these folders of the system temporary directory:

- `OPC Foundation/MinimalBoilerServer/pki`
- `OPC Foundation/MinimalClient/pki`

Delete them to repeat the exercise with new certificates.

## Route B: Build applications from NuGet packages

In Route B, you create two console applications:

- A server that exposes only the standard OPC UA nodes.
- A client that browses and reads the server, then monitors a value.

Each application keeps its certificate stores in a `pki` folder in its project
folder. Run each application from its own project folder so that it finds that
`pki` folder.

### B1. Create the server

```bash
dotnet new console -n GettingStartedServer
cd GettingStartedServer
dotnet add package OPCFoundation.NetStandard.Opc.Ua.Server
dotnet add package Microsoft.Extensions.Hosting
```

Replace the contents of `Program.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddOpcUa()
    .AddServer(options =>
    {
        options.ApplicationName = "GettingStartedServer";
        options.ApplicationUri = "urn:localhost:GettingStartedServer";
        options.ProductUri = "urn:example.com:GettingStartedServer";
        options.EndpointUrls.Add("opc.tcp://localhost:62560/GettingStartedServer");
        options.PkiRoot = Path.Combine(builder.Environment.ContentRootPath, "pki");
    });

await builder.Build().RunAsync();
```

Start the server:

```bash
dotnet run
```

On first start, the server creates its application certificates in `pki`. It
is ready when it logs:

```text
OPC UA server listening at opc.tcp://localhost:62560/GettingStartedServer.
```

Leave the server running.

### B2. Create the client

In the second terminal, go to the folder that contains `GettingStartedServer`
and create the client next to it:

```bash
dotnet new console -n GettingStartedClient
cd GettingStartedClient
dotnet add package OPCFoundation.NetStandard.Opc.Ua.Client
dotnet add package Microsoft.Extensions.Hosting
```

Replace the contents of `Program.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services
    .AddOpcUa()
    .AddClient(options =>
    {
        options.ApplicationName = "GettingStartedClient";
        options.ApplicationUri = "urn:localhost:GettingStartedClient";
        options.ProductUri = "urn:example.com:GettingStartedClient";
        options.PkiRoot = Path.Combine(builder.Environment.ContentRootPath, "pki");
        // Report connection problems quickly while you set up trust. Long-running
        // clients usually keep the default policy, which retries for minutes.
        options.Session = new ManagedSessionOptions
        {
            ReconnectPolicy = new ReconnectPolicyOptions { MaxRetries = 2 },
        };
    })
    .AddDiscoveryAndConnect(options =>
    {
        options.DiscoveryUrl = "opc.tcp://localhost:62560/GettingStartedServer";
        options.SecurityMode = MessageSecurityMode.SignAndEncrypt;
        options.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
    });

using IHost host = builder.Build();
await host.StartAsync();

using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
CancellationToken ct = timeout.Token;

try
{
    var connect = host.Services
        .GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>();
    ManagedSession session = await connect(ct);
    await using (session)
    {
        Console.WriteLine($"Connected to {session.ConfiguredEndpoint?.EndpointUrl}");

        // Browse the standard Objects folder.
        var browser = new Browser(session)
        {
            BrowseDirection = BrowseDirection.Forward,
            ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
            IncludeSubtypes = true,
            NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable),
        };
        ArrayOf<ReferenceDescription> children =
            await browser.BrowseAsync(ObjectIds.ObjectsFolder, ct);
        Console.WriteLine("Objects folder:");
        foreach (ReferenceDescription child in children)
        {
            Console.WriteLine($"  {child.DisplayName} ({child.NodeClass})");
        }

        // Read the server clock. Check the status before using the value.
        DataValue value = await session.ReadValueAsync(
            VariableIds.Server_ServerStatus_CurrentTime,
            ct);
        PrintTime("Read", value);

        // Monitor the same variable and stop after three notifications.
        Console.WriteLine("Waiting for three data changes...");
        await foreach (DataValueChange change in session.DefaultStreaming
            .SubscribeDataChangesAsync(VariableIds.Server_ServerStatus_CurrentTime, ct: ct)
            .TakeAsync(3)
            .WithCancellation(ct))
        {
            PrintTime("Update", change.Value);
        }
    }
}
catch (ServiceResultException ex)
{
    Console.Error.WriteLine($"OPC UA error {ex.StatusCode}: {ex.Message}");
    Environment.ExitCode = 1;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("The client did not finish within one minute.");
    Environment.ExitCode = 1;
}
finally
{
    await host.StopAsync();
}

static void PrintTime(string label, DataValue value)
{
    if (StatusCode.IsBad(value.StatusCode))
    {
        Console.WriteLine($"{label}: failed with {value.StatusCode}");
    }
    else if (value.WrappedValue.TryGetValue(out DateTimeUtc time))
    {
        Console.WriteLine($"{label}: {time} (status {value.StatusCode})");
    }
    else
    {
        Console.WriteLine($"{label}: unexpected value {value.WrappedValue}");
    }
}
```

### B3. Trust each application

A new application trusts no other application. Each time a connection fails
because of an untrusted certificate, the application that refused it saves the
certificate in its `pki/rejected/certs` folder. You approve a certificate by
moving it to `pki/trusted/certs`. Both applications read their trusted store
again when it changes, so you do not need to restart the server.

1. From the `GettingStartedClient` folder, run `dotnet run`. After some log
   messages, the client stops with:

   ```text
   OPC UA error BadSecurityChecksFailed [0x80130000]: ...
   ```

   The server has refused the client certificate. In the
   `GettingStartedServer` folder, trust it:

   PowerShell:

   ```powershell
   New-Item -ItemType Directory -Force pki/trusted/certs | Out-Null
   Move-Item pki/rejected/certs/*.der pki/trusted/certs
   ```

   bash:

   ```bash
   mkdir -p pki/trusted/certs
   mv pki/rejected/certs/*.der pki/trusted/certs/
   ```

2. Run the client again. It now stops with:

   ```text
   OPC UA error BadCertificateUntrusted [0x801A0000]: ...
   ```

   This time, the client has refused the server certificate. Run the same trust
   commands in the `GettingStartedClient` folder.

3. Run the client a third time. It connects and completes.

### B4. Check the result

The client prints output like this and exits with exit code 0:

```text
Connected to opc.tcp://localhost:62560/GettingStartedServer
Objects folder:
  Server (Object)
  Aliases (Object)
  Locations (Object)
Read: 09/28/2026 09:28:36 (status Good [0x00000000])
Waiting for three data changes...
Update: 09/28/2026 09:28:36 (status Good [0x00000000])
Update: 09/28/2026 09:28:37 (status Good [0x00000000])
Update: 09/28/2026 09:28:38 (status Good [0x00000000])
```

The times depend on when you run the client. Before the output, the client logs
several `Application Certificate Validation suppressed BadCertificateUntrusted`
warnings. They come from the startup check of the client's *own* self-signed
certificates, not from the server certificate, and need no action.

### B5. Stop and clean up

Press Ctrl+C in the server terminal. Each `pki` folder contains private keys.
Do not commit them to source control. Delete both `pki` folders to repeat the
exercise with new certificates.

### Connect to another server

To use the client with another server, change `DiscoveryUrl` to that server's
URL. Set `SecurityMode` and `SecurityPolicyUri` to values that one of its
endpoints offers. Trust the server certificate as in [B3](#b3-trust-each-application),
and ask the server's administrator to trust your client certificate from
`GettingStartedClient/pki/certs`. The client connects as an anonymous user; if
the server requires user authentication, see
[Identity providers](IdentityProviders.md). The client only browses, reads, and
monitors; it does not change the server.

## What the code does

| Step | Code | Learn more |
| --- | --- | --- |
| Identify the application | `ApplicationName`, `ApplicationUri`, and `ProductUri` name the application. `PkiRoot` selects its certificate stores. The first start creates an RSA certificate and, where the platform supports them, ECC certificates. | [Concepts: applications](Concepts.md#applications), [Certificates](Certificates.md) |
| Host the server | `AddServer` runs the server as a hosted service. By default, it offers `SignAndEncrypt` endpoints with RSA security policies, does not offer the insecure `None` policy, and allows anonymous users. | [Dependency injection](DependencyInjection.md#server-feature) |
| Select an endpoint | `AddDiscoveryAndConnect` calls `GetEndpoints` on the URL and selects the endpoint with the configured security mode and policy. | [Concepts: endpoints](Concepts.md#endpoints-secure-channels-and-sessions) |
| Connect | The registered `Func<CancellationToken, Task<ManagedSession>>` connects a `ManagedSession`, which reconnects automatically after connection loss. | [Sessions](Sessions.md#3-managedsession--the-connection-state-machine-facade) |
| Browse | `Browser` follows hierarchical references from the `Objects` folder. | [Concepts: address space](Concepts.md#the-address-space) |
| Read | `ReadValueAsync` returns a `DataValue` whose status is Good or Uncertain, and throws a `ServiceResultException` when the status is Bad. | [Concepts: values](Concepts.md#values-status-codes-and-timestamps) |
| Monitor | `session.DefaultStreaming` is the session's shared streaming subscription. The first `SubscribeDataChangesAsync` call creates the OPC UA subscription and adds a monitored item. `TakeAsync(3)` ends the stream after three values and removes the monitored item. | [Streaming subscriptions](Subscriptions.md#streaming-subscriptions) |
| Clean up | `await using` closes the session and its subscription. The timeout bounds the whole run. | [Concepts: SDK organization](Concepts.md#how-the-sdk-is-organized) |

The client limits reconnect attempts to two so that trust problems appear
quickly. A long-running client usually keeps the default policy, which retries
for several minutes. Some settings suit local use only: automatic certificate
acceptance in the Route A server, and certificate stores in a temporary or
project folder. Production applications keep their certificate stores in a
persistent, access-controlled location; see the
[production readiness checklist](ProductionChecklist.md).

## Troubleshooting

| Symptom | Cause | What to do |
| --- | --- | --- |
| `BadSecurityChecksFailed` with `Could not verify security on OpenSecureChannel request` | The server does not trust the client certificate. | Move the client certificate from the server's rejected store to its trusted store. |
| `BadCertificateUntrusted` with `Could not verify security on OpenSecureChannel response` | The client does not trust the server certificate. | Move the server certificate from the client's rejected store to its trusted store. |
| `BadNotConnected` with `Could not connect to the remote endpoint` | No server is listening at the URL. | Start the server, then check the host, port, and path in the URL. |
| `No discovered endpoint matched the configured security policy and mode.` | The server does not offer the requested security mode and policy. | Choose a mode and policy that the server offers. See [Profiles](Profiles.md#security-profiles). |
| The server does not start, and the port is in use | Another application uses the port. | Stop that application, or change the port in both the server endpoint URL and the client discovery URL. |
| `A compatible .NET SDK was not found` (Route A) | The SDK version in `global.json` is not installed. | Install that SDK version. |

See [Certificates](Certificates.md) for certificate stores and chains, and
[Diagnostics](Diagnostics.md) for logging and packet capture.

## Next steps

- Learn the terms used throughout the documentation in
  [OPC UA concepts](Concepts.md).
- Add your own model to the Route B server, and read, write, and call it from
  the client, in [Your first information model](FirstModel.md).
- Continue on the path for your application in the
  [documentation index](README.md#2-build-your-application):
  a client, a server with your own model, or a PubSub application.
- Before you deploy, work through the
  [production readiness checklist](ProductionChecklist.md): certificate
  management, user authentication, resource limits, and diagnostics.
