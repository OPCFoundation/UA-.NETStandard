# Rate limiting and admission control

The server applies deterministic, configurable admission control so it sheds a connection or session-establishment storm with a fast, standard "busy" signal instead of collapsing (see [Server Session Scalability](ServerScalability.md) for the underlying analysis). Clients react to that signal by backing off adaptively rather than retrying blindly. The primitives are built on `System.Threading.RateLimiting`, so the algorithm is pluggable and configurable through dependency injection.

Rate limiting is **on by default with conservative limits** sized so normal and bulk-but-well-behaved load is unaffected; only a storm is shed. Tune or disable it per deployment, or replace the whole limiter provider via DI.

## Server side

### What is limited

- **Inbound connections** (`opc.tcp` listener): a per-second token bucket (with a burst) admits new connections; beyond the burst a connection is shed cheaply so the CPU-bound secure-channel handshake is protected. The listener socket backlog is configurable and defaults to 512 (raised from the previous hard-coded 10) so a burst of simultaneous connects is absorbed rather than dropped by the OS.
- **Session establishment** (`CreateSession` / `ActivateSession`): a concurrency limiter bounds the number of in-flight establishment operations so a connect storm cannot saturate every core and starve steady-state publish delivery. When at capacity the server returns `BadServerTooBusy` with a retry-after hint in the fault, before doing the expensive certificate validation / signing.

### Status codes

| Situation | Status code |
| --- | --- |
| Session establishment at capacity | `BadServerTooBusy` (transient — the client should back off and retry) |
| Hard session cap reached (`MaxSessionCount`) | `BadTooManySessions` |
| Connection shed at the listener | the connection is dropped; the client sees a transport error and, together with any subsequent `BadServerTooBusy`, backs off |

### Configuration

The deterministic limits live in `ServerRateLimitOptions`:

| Option | Default | Meaning |
| --- | --- | --- |
| `Enabled` | `true` | Master switch for all rate limiting. |
| `ListenBacklog` | 512 | Listener socket pending-connection backlog. |
| `ConnectionRateLimitEnabled` | `true` | Whether inbound connections are rate limited. |
| `ConnectionsPerSecond` | 500 | Sustained connection admission rate. |
| `ConnectionBurst` | 1000 | Connection burst capacity (token-bucket size). |
| `SessionRateLimitEnabled` | `true` | Whether session establishment is limited. |
| `MaxConcurrentSessionEstablishment` | `max(ProcessorCount * 8, 128)` | Concurrent in-flight `CreateSession`/`ActivateSession`. |
| `SessionEstablishmentQueueLimit` | 0 | Waiters before rejecting with `BadServerTooBusy` (0 = reject immediately). |

Configure it through the DI hosting options:

```csharp
services.AddOpcUa()
    .AddServer(options =>
    {
        options.ApplicationName = "MyServer";
        options.ConfigureRateLimits = limits =>
        {
            limits.ConnectionsPerSecond = 200;
            limits.MaxConcurrentSessionEstablishment = 64;
        };
    });
```

To plug in a completely different algorithm, register a custom `IServerRateLimiterProvider` in DI (it takes precedence over `ConfigureRateLimits`):

```csharp
services.AddSingleton<IServerRateLimiterProvider>(sp =>
    new MyCustomRateLimiterProvider(...));
```

Without dependency injection, set the options or provider directly on the server before it starts:

```csharp
var server = new StandardServer(telemetry);
server.RateLimitOptions = new ServerRateLimitOptions { ConnectionsPerSecond = 200 };
// or: server.RateLimiterProvider = new DefaultServerRateLimiterProvider(options);
```

### Extending the transport

The `opc.tcp` listener consumes an `IConnectionRateLimiter` and a backlog value through `TransportListenerSettings`, injected by `StandardServer.ConfigureTransportListenerSettings`. A custom server can override that hook to supply its own limiter. The default `TokenBucketConnectionRateLimiter` wraps a `System.Threading.RateLimiting.TokenBucketRateLimiter`.

## Client side

A client that gets a "server busy" signal must not hammer the server with retries — that is exactly what amplifies a connect storm. The default `ReconnectPolicy` (used by `ManagedSession`) is **server-signal-aware**: when the previous attempt failed with an overload signal (`BadServerTooBusy`, `BadTcpServerTooBusy`, `BadTooManySessions`, `BadTooManyOperations`, `BadTooManyPublishRequests`, or a transient timeout) it backs off more aggressively (4× the computed delay, capped at `MaxDelay`) and honors a server-provided retry-after hint as a lower bound.

This is exposed through `IAdaptiveReconnectPolicy` (a non-breaking companion of `IReconnectPolicy`). The connection state machine uses the adaptive overload automatically when the configured policy implements it, and falls back to the basic attempt-based delay otherwise, so existing custom policies keep working unchanged. Because a failed initial connect funnels into the same reconnect loop, the adaptive backoff applies to both initial connects and reconnects.

`ReconnectPolicy.IsServerBusySignal(StatusCode)` classifies whether a status code is an overload signal, for use in custom policies.

## Planned follow-ups

- **HTTPS / Kestrel**: expose the ASP.NET Core `RateLimiter` middleware through the stack's DI so it can be attached to the Kestrel-hosted HTTPS transport.
- **Client-wide connect admission**: a shared limiter so many concurrent `ManagedSession.CreateAsync` calls to one server ramp their initial connects instead of bursting.
- **Structured retry-after**: carry the server's retry-after hint in the response header so a cooperating client can honor it precisely (today it is human-readable in the fault message).
- **CreateSession crypto out of the establishment lock** (scalability item B4).

## See also

- [Server Session Scalability](ServerScalability.md) — the analysis motivating this work.
- [Performance Benchmarks](Benchmarks.md#server-session-scalability) — the session-scalability load test.
- [Sessions, Reconnection, and Subscription Engines](Sessions.md) — `ManagedSession` and reconnect architecture.
