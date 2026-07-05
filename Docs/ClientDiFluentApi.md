# Client DI and fluent API shortcuts

The client DI surface starts with `services.AddOpcUa().AddClient(...)`. The returned
`IOpcUaClientBuilder` can compose client features without returning to the root builder:

```csharp
services.AddOpcUa()
    .AddClient(options =>
    {
        options.Configuration = configuration;
        options.Session = options.Session with { Endpoint = endpoint };
    })
    .AddSubscriptions()
    .AddManagedClientPool()
    .AddWotConClient()
    .AddGdsClient()
    .AddCertificateManagement();
```

Reverse-connect one-shot sessions can be built through DI:

```csharp
services.AddOpcUa()
    .AddReverseConnectClient(options =>
    {
        options.Configuration = configuration;
        options.Session = options.Session with { Endpoint = endpoint };
    }, new Uri("urn:server"));
```

Discovery-and-connect resolves `IOpcUaDiscoveryService`, selects an endpoint by security policy and mode,
then connects through `IManagedSessionFactory`:

```csharp
services.AddOpcUa()
    .AddClient(options => options.Configuration = configuration)
    .AddDiscoveryAndConnect(options =>
    {
        options.DiscoveryUrl = "opc.tcp://localhost:4840";
        options.SecurityMode = MessageSecurityMode.SignAndEncrypt;
        options.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
    });
```
