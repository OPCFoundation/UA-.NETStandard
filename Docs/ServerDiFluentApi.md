# Server DI and fluent hosting shortcuts

The server hosting surface exposes granular extension points and one-shot presets:

```csharp
services.AddOpcUa()
    .AddSecureServer(options =>
    {
        options.ApplicationName = "PlantServer";
        options.ApplicationUri = "urn:localhost:PlantServer";
        options.ProductUri = "urn:example:PlantServer";
        options.EndpointUrls.Add("opc.tcp://localhost:4840/PlantServer");
    })
    .AddOpcTcpTransport()
    .AddReverseConnect(options =>
    {
        options.Clients.Add(new ServerReverseConnectClientOptions
        {
            EndpointUrl = "opc.tcp://client.example.com:4841"
        });
    })
    .ConfigureOperationLimits(options => options.MaxNodesPerRead = 1000)
    .ConfigureRoles(options => options.Roles.Add(new RoleDefinitionOptions
    {
        Name = BrowseNames.WellKnownRole_Observer,
        Identities =
        {
            new RoleIdentityMappingOptions
            {
                CriteriaType = IdentityCriteriaType.UserName,
                Criteria = "operator"
            }
        }
    }));
```

Use `AddRoleManager(IRoleManager)` or `AddRoleManager<T>()` to replace the default role manager.
Custom server types that need session, subscription, or durable-subscription DI hooks must derive from
`DependencyInjectionStandardServer`; otherwise startup fails fast instead of ignoring those hooks.

Fluent node managers can be registered without a factory class:

```csharp
services.AddOpcUa()
    .AddReferenceServer()
    .AddNodeManager("urn:example:line", nodes =>
    {
        nodes.Node("ReferenceServer");
    });
```

`AddHistorianFileStore(provider, path)` combines the historian provider registration with a Part 20
file-system mount for demo and lab servers.
