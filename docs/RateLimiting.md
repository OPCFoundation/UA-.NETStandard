# Rate limiting and admission control

The server applies deterministic, configurable admission control so it sheds a connection or session-establishment storm with a fast, standard "busy" signal instead of collapsing (see [Server Session Scalability](ServerScalability.md) for the underlying analysis). Clients react to that signal by backing off adaptively rather than retrying blindly. The primitives are built on `System.Threading.RateLimiting`, so the algorithm is pluggable and configurable through dependency injection.

Rate limiting is **on by default with conservative limits** sized so normal and bulk-but-well-behaved load is unaffected; only a storm is shed. Tune or disable it per deployment, or replace the whole limiter provider via DI.

## Server side

### What is limited

- **Inbound connections** (`opc.tcp` listener): a per-second token bucket (with a burst) admits new connections; beyond the burst a connection is shed cheaply so the CPU-bound secure-channel handshake is protected. The listener socket backlog is configurable and defaults to 512 (raised from the previous hard-coded 10) so a burst of simultaneous connects is absorbed rather than dropped by the OS.
- **Session establishment** (`CreateSession` / `ActivateSession`): a concurrency limiter bounds the number of in-flight establishment operations so a connect storm cannot saturate every core and starve steady-state publish delivery. When at capacity the server returns `BadServerTooBusy` with a retry-after hint in the fault, before doing the expensive certificate validation / signing.
- **Incomplete messages** (`opc.tcp` and `opc.wss` secure channels): the chunks a channel keeps while it waits for the final chunk of a message draw on one memory budget shared by all channels and listeners of the server, so connections that never finish a message cannot exhaust the server's memory. See [Incomplete messages](#incomplete-messages).

### Status codes

| Situation | Status code |
| --- | --- |
| Session establishment at capacity | `BadServerTooBusy` (transient — the client should back off and retry) |
| Hard session cap reached (`MaxSessionCount`) | `BadTooManySessions` |
| Connection shed at the listener | the connection is dropped; the client sees a transport error and, together with any subsequent `BadServerTooBusy`, backs off |
| Chunks of incomplete messages exceed the chunk reassembly budget | the server sends an `Error` message with `BadTcpNotEnoughResources` and closes the channel; the client reconnects |

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

The `opc.tcp` listener consumes an `IConnectionRateLimiter` and a backlog value through `TransportListenerSettings`, injected by `StandardServer.ConfigureTransportListenerSettings`. A custom server can override that hook to supply its own limiter. The default `TokenBucketConnectionRateLimiter` wraps a `System.Threading.RateLimiting.TokenBucketRateLimiter`.

### Incomplete messages

A secure channel keeps the chunks of a message until its final chunk arrives (OPC 10000-6 §6.7.2). The `MaxMessageSize` and `MaxChunkCount` negotiated by the Hello/Acknowledge exchange bound what one channel keeps, but not what all channels keep together: a peer that opens many channels, sends intermediate chunks and never the final one, holds the per-channel maximum on every channel until the process runs out of memory. The server therefore charges every intermediate chunk it keeps against a `ChunkReassemblyBudget` (namespace `Opc.Ua.Bindings`) that all its channels and listeners share:

- A chunk is charged the length of the buffer it is received into, which is what it keeps alive. After the Hello the listener receives into buffers of the negotiated chunk size, so a client that negotiates small chunks is charged for what it sends, not for buffers of the listener's maximum size.
- The final chunk of a message is never charged: it is processed at once, so a request that fits in one chunk is served even while the budget is exhausted.
- Channels on which no session has been activated may fill only half the budget (`MaxBytesWithoutSession`), so peers that never authenticate cannot take the room the channels of sessions need.
- The reservation is released when the message completes, when a chunk of another request or an abort chunk discards it, and when the channel goes away.
- A chunk that does not fit discards the incomplete message; the server sends an `Error` message with `BadTcpNotEnoughResources` — the error OPC 10000-6 §7.1.5 defines for a server that runs out of resources — and closes the channel. Other channels are not affected.

By default a server sizes the budget from `TransportQuotas.MaxMessageSize`: room for sixteen messages of that size, but no less than 64 MiB and no more than 1 GiB, and always room for four of them. A configuration that does not limit the message size gets 1 GiB. With the default 2 MiB and the reference server's 4 MiB message size the budget is 64 MiB. `ChunkReassemblyBudget.GetDefaultMaxBytes(maxMessageSize)` returns the default for a given size.

Raise the budget when many clients send large requests at the same time; lower it on a device with little memory. Through dependency injection:

```csharp
services.AddOpcUa()
    .AddServer(options => options.ApplicationName = "MyServer")
    .WithChunkReassemblyBudget(256L * 1024 * 1024);
```

Without dependency injection, set the budget on the server before it starts. One instance can also be shared by several servers of a process:

```csharp
var server = new StandardServer(telemetry)
{
    ChunkReassemblyBudget = new ChunkReassemblyBudget(256L * 1024 * 1024)
};
```

The `ChunkReassemblyBudget(maxBytes, maxBytesWithoutSession)` constructor sets the share of channels without a session explicitly, for example the whole budget for a server that serves sessionless requests. A host that opens a transport listener itself passes the budget through `TransportListenerSettings.ChunkReassemblyBudget`; a listener given none creates its own, sized the same way from its endpoint configuration.

## HTTPS / Kestrel transport

The HTTPS/WSS transport is Kestrel-hosted and can use the ASP.NET Core rate limiter middleware. Because that listener builds its own isolated web host, attach the limiter through the stack's DI with `AddHttpsRateLimiter` (net8+):

```csharp
services.AddOpcUa()
    .AddHttpsTransport()
    .AddHttpsRateLimiter(); // default: global 100 req/s fixed window, rejects with HTTP 429
```

Pass an `Action<RateLimiterOptions>` to fully configure the limiter with any `System.Threading.RateLimiting` policy. This registers a contributor that calls `services.AddRateLimiter(...)` and `app.UseRateLimiter()` on the listener's isolated Kestrel host; rejected requests return HTTP 429.

## Client side

A client that gets a "server busy" signal must not hammer the server with retries — that is exactly what amplifies a connect storm. The default `ReconnectPolicy` (used by `ManagedSession`) is **server-signal-aware**: when the previous attempt failed with an overload signal (`BadServerTooBusy`, `BadTcpServerTooBusy`, `BadTooManySessions`, `BadTooManyOperations`, `BadTooManyPublishRequests`, or a transient timeout) it backs off more aggressively (4× the computed delay, capped at `MaxDelay`) and honors a server-provided retry-after hint as a lower bound.

This is exposed through `IReconnectPolicy.TryGetNextDelay`, which adapts the backoff to the previous attempt's status code and any server-provided retry-after hint. The connection state machine calls it automatically; a policy that returns `false` opts out of adaptive behavior and the state machine falls back to the basic attempt-based `GetNextDelay`, so a minimal custom policy only has to implement the plain delay. Because a failed initial connect funnels into the same reconnect loop, the adaptive backoff applies to both initial connects and reconnects.

`ReconnectPolicy.IsServerBusySignal(StatusCode)` classifies whether a status code is an overload signal, for use in custom policies.

To keep a bulk connect from bursting, a client-wide **connect admission gate** ramps concurrent initial connects: `ManagedSessionBuilder.WithConnectRateLimiter(maxConcurrency)` (or a shared `RateLimiter` / `IClientConnectGate`) bounds how many `ManagedSession.CreateAsync` calls establish at once; the excess wait in an unbounded queue rather than being rejected, so many sessions to one server ramp up smoothly instead of stampeding. It is off by default; through DI use the `ConnectRateLimiterMaxConcurrency` client option.

## Server backpressure signals

- **Structured retry-after**: the server carries a machine-readable retry-after to the client independently of diagnostics — as a `RetryAfterMs` value in `ResponseHeader.additionalHeader` on a `BadServerTooBusy` `ServiceFault`, and as the standard HTTP `Retry-After` header on the HTTPS 429. The client honors both via `IReconnectPolicy.TryGetNextDelay`. The UA-TCP client also honors a `RetryAfterMs=N` token in a transient server-busy `Error` message reason as a lower bound on channel-reconnect backoff. The legacy `RetryAfterMs=N` `AdditionalInfo` token remains a best-effort compatibility hint. See [Server retry-after backpressure](Sessions.md#server-retry-after-backpressure) for how the client honors these signals during reconnect.
- **Load-based `Server.ServiceLevel`**: the reference server computes `Server.ServiceLevel` from session-establishment headroom (255 at low load, scaling toward a floor as sessions approach `MaxSessionCount`, with hysteresis), so a client can read/subscribe to it as a proactive capacity signal.

## See also

- [Server Session Scalability](ServerScalability.md) — the analysis motivating this work.
- [Performance Benchmarks](Benchmarks.md#server-session-scalability) — the session-scalability load test.
- [Sessions, Reconnection, and Subscription Engines](Sessions.md) — `ManagedSession` and reconnect architecture.
