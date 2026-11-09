# Fluent API samples

| Folder | Contents |
| --- | --- |
| [BasicFluentApiServer](BasicFluentApiServer/) | A Generic Host server that registers a node manager through the fluent configuration API. Its node manager and factory are in `BasicFluentNodeManager.cs` and `BasicFluentNodeManagerFactory.cs`. |

See [Dependency injection](../../docs/DependencyInjection.md) and
[Node managers](../../docs/NodeManagers.md) for the hosting and address-space APIs.

## Run

From the repository root:

```powershell
dotnet run --project samples\FluentApi\BasicFluentApiServer\BasicFluentApiServer.csproj -f net10.0
```

The default endpoint is `opc.tcp://localhost:62542/BasicFluentApiServer`;
`--port` changes the port. The sample automatically accepts untrusted application
certificates for local testing. Replace that setting with explicit trust
configuration before adapting it for a deployment.
