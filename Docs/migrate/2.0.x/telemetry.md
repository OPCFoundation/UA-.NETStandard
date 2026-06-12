# Telemetry and Logging

> **When to read this:** Read this when migrating logger use (`Utils.LogX`, `Utils.Trace`, static log helpers) to the new `ITelemetryContext` model that resolves `ILogger` via `telemetry.CreateLogger<T>()`.

Observability in 2.0 is plumbed through `ITelemetryContext`. Loggers are resolved from the telemetry context via `telemetry.CreateLogger<T>()` rather than from `Utils.Trace` / `Utils.LogX`. The static logging helpers remain compilable but are `[Obsolete]`; consumers should resolve `ILogger` from `ITelemetryContext` instead.

Constructor injection across the public API is not uniform - the parameter is required positionally on most types, optional on `ApplicationInstance`, and absent on `Session` / `CustomNodeManager2`. The table below summarises the precise shape per type:

| Type | Telemetry parameter | Notes |
|---|---|---|
| `ApplicationInstance(ITelemetryContext? telemetry)` | Nullable | Also `ApplicationInstance(ApplicationConfiguration, ITelemetryContext?)`. Passing `null` falls back to a default telemetry context. |
| `ServerBase(ITelemetryContext telemetry)` | Required positional | The only public ctor. |
| `CertificateManagerFactory.Create(SecurityConfiguration, ITelemetryContext, Action<CertificateManagerOptions>?)` | Required positional (2nd parameter) | Factory entry point for `CertificateManager`. |
| `DefaultSessionFactory()` / `DefaultSessionFactory(ITelemetryContext telemetry)` | Both ctors exist | The parameterless ctor is `[Obsolete]`; use the telemetry-aware overload. |
| `ManagedSessionFactory(ITelemetryContext telemetry)` | Required positional | The only public ctor. |
| `Session` ctors | **None** | Telemetry flows in via `ApplicationConfiguration` or `ISubscriptionEngineFactory`. Do not look for a `Session(... ITelemetryContext)` overload - none exists. |
| `CustomNodeManager2(IServerInternal, ApplicationConfiguration?, bool, ILogger, params string[])` | **None directly** | Obtain a logger via `server.Telemetry.CreateLogger<T>()` and pass it to the ctor. |

```csharp
// Server side - log via the server's telemetry context
public sealed class MyNodeManager : CustomNodeManager2
{
    public MyNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        : base(server, configuration, useSamplingGroups: false,
               server.Telemetry.CreateLogger<MyNodeManager>(),
               "urn:example:my-namespace")
    {
    }
}

// Client side - construct the factory with telemetry
var factory = new ManagedSessionFactory(telemetry);
ISession session = await factory.CreateAsync(/* ... */);
```

---

**See also**

- Related: [packages.md](packages.md), [configuration.md](configuration.md).
- [2.0 migration index](README.md) — analyzer quick-start + symptom → sub-doc table.
- [Migration Guide](../../MigrationGuide.md) — landing page across versions.

