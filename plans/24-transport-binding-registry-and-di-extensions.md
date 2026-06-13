# Plan — Transport binding registry + DI extensions

## Status

**Proposed.** Surfaced from PR feedback on
[#3880](https://github.com/OPCFoundation/UA-.NETStandard/pull/3880)
(`Docs/MigrationGuide.md` review comment: *"Consider a binding registry
and add DI extensions."*).

## Problem

Transport listener / channel bindings are registered today via static
mutators on `TransportBindings`:

```csharp
TransportBindings.Listeners.SetBinding(new KestrelTcpTransportListenerFactory());
TransportBindings.Channels.SetBinding(new MyChannelFactory());
```

This works but has several rough edges:

1. **Mutable global state.** Two parallel test fixtures racing on
   `TransportBindings.Listeners.SetBinding` corrupt each other's state.
   Restoring the original binding in `OneTimeTearDown` is mandatory
   boilerplate that every test author currently repeats by hand
   (see `KestrelTcpReverseConnectIntegrationTests`,
   `ClientTestKestrelTcp`, etc.).
2. **No DI story.** Consumers using `Microsoft.Extensions.DependencyInjection`
   cannot fluently register transport bindings into a service collection
   alongside their other services. They have to plumb the static setter
   from a startup hook.
3. **No scoped binding.** A library wanting to use a custom transport
   for just one channel cannot do so without mutating global state for
   the whole process.
4. **Discovery via reflection.** `Utils.DefaultBindings` is a hard-coded
   dictionary mapping `opc.https` / `opc.wss` to assembly names that
   the runtime loads by name on first use. This works but is opaque
   and ignores DI.

## Goals

- Make the binding registry **scoped** (per `IHost` / per
  `IServiceProvider`) while preserving the existing static API for
  back-compat.
- Expose **`AddOpcUaTransport(...)` DI extensions** so a host can
  register transports at composition root, e.g.

  ```csharp
  builder.Services
      .AddOpcUaServer(...)
      .AddOpcTcpTransport()                          // raw-socket listener
      .AddKestrelOpcTcpTransport()                   // opt-in alternative
      .AddHttpsTransport(o => o.MutualTls = true)    // also serves opc.wss
      .AddCustomTransport<MyTransportListenerFactory, MyChannelFactory>("opc.my");
  ```

- The DI registrations should compose with each other — e.g. swapping
  raw-socket TCP for Kestrel-TCP should be a one-line override (call
  `AddKestrelOpcTcpTransport` after `AddOpcTcpTransport`), not a
  mutation of a global registry.
- Provide a `ITransportBindingRegistry` interface that the existing
  static `TransportBindings` resolves through (so it stays
  source-compatible).

## Non-goals

- Removing or breaking the existing static `TransportBindings` API
  (would be source-breaking for every consumer).
- Replacing the `Utils.DefaultBindings` reflection-based auto-load —
  the DI extensions are opt-in *in addition* to that path.

## Sketch

```csharp
public interface ITransportBindingRegistry
{
    void RegisterListenerFactory(ITransportListenerFactory factory);
    void RegisterChannelFactory(ITransportChannelFactory factory);
    ITransportListenerFactory? GetListenerFactory(string uriScheme);
    ITransportChannelFactory? GetChannelFactory(string uriScheme);
}

public static class TransportRegistrationExtensions
{
    public static IServiceCollection AddOpcTcpTransport(this IServiceCollection services);
    public static IServiceCollection AddKestrelOpcTcpTransport(this IServiceCollection services);
    public static IServiceCollection AddHttpsTransport(
        this IServiceCollection services,
        Action<HttpsTransportOptions>? configure = null);
    public static IServiceCollection AddWssTransport(this IServiceCollection services);
    public static IServiceCollection AddCustomTransport<TListenerFactory, TChannelFactory>(
        this IServiceCollection services, string uriScheme)
        where TListenerFactory : class, ITransportListenerFactory
        where TChannelFactory : class, ITransportChannelFactory;
}
```

Backing implementation lives in a new
`Libraries/Opc.Ua.DependencyInjection` (or extends the existing DI
project — see `Docs/DependencyInjection.md`). The static
`TransportBindings.Channels` / `TransportBindings.Listeners` keep
working — they resolve through a process-wide
`ITransportBindingRegistry` instance.

## Acceptance

- `services.AddOpcUaServer().AddKestrelOpcTcpTransport()` produces a
  server that serves `opc.tcp://` through the Kestrel listener without
  touching `TransportBindings.Listeners`.
- Two test fixtures running in parallel can each register their own
  bindings into their own `ServiceProvider`s without interfering.
- The existing static `TransportBindings` API continues to work for
  back-compat (no source breaks for 1.5.378 consumers).
- New tests under `Tests/Opc.Ua.DependencyInjection.Tests` cover the
  scoped registry semantics.

## Out of scope (file separately if needed)

- `IConnectionListenerFactory`-shaped Kestrel transport hosting for
  non-OPC scenarios.
- Replacing the static `TransportBindings` API entirely (would be
  source-breaking).
- Adding the Kestrel-TCP integration on net472 / netstandard2.x (see
  the Kestrel-TCP package TFM rationale in
  `Stack/Opc.Ua.Bindings.Kestrel.Tcp/Opc.Ua.Bindings.Kestrel.Tcp.csproj`).
