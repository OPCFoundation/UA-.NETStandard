# Implementing a custom transport via `IUaSCByteTransport`

The OPC UA Secure Conversation (UASC) binary channel pipeline talks to the
wire through a narrow byte-level abstraction —
[`IUaSCByteTransport`](../Stack/Opc.Ua.Core/Stack/Tcp/IUaSCByteTransport.cs).
It is the public extension point for plugging in custom transports beyond
the built-in TCP and WebSocket implementations.

> **Stability.** The interface is stable for v2. The shape may evolve in
> v3 (see follow-up `fu-custom-transport-extensibility`); for now any
> additions will be done as new methods on a separate
> `IUaSCByteTransportV3` contract rather than breaking v2.

This doc is the canonical reference for implementing the contract. A
runnable worked example lives at
[`Tests/Opc.Ua.Core.Tests/Stack/Transport/InProcessTransportExample.cs`](../Tests/Opc.Ua.Core.Tests/Stack/Transport/InProcessTransportExample.cs).

## When you need this

Use cases that are good fits for a custom byte transport:

- **Named pipes / Unix domain sockets** for fast loopback IPC.
- **QUIC / HTTP/3** to replace the TCP transport's congestion behaviour.
- **In-process bridges** for unit tests or co-located server/client pairs
  that want to skip the network stack entirely.
- **Tunnels** (SSH, custom L4) that wrap the UASC chunks in another
  framing.

Use cases that should NOT use this:

- A new sub-protocol that changes UASC framing — that lives inside the
  channel implementation, not the transport.
- HTTPS-binary or HTTPS-JSON variants — those are handled in
  `Opc.Ua.Bindings.Https` outside the UASC pipeline.

## Contract summary

```csharp
public interface IUaSCByteTransport
{
    string Implementation { get; }                  // diagnostic id
    TransportChannelFeatures Features { get; }      // optional capabilities
    EndPoint? LocalEndpoint { get; }                // may be null
    EndPoint? RemoteEndpoint { get; }               // may be null

    ValueTask ConnectAsync(Uri url, CancellationToken ct);
    ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct);
    ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct);
    ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct);
    void Close();
}

public interface IUaSCByteTransportFactory
{
    string Implementation { get; }
    IUaSCByteTransport Create(
        BufferManager bufferManager,
        int receiveBufferSize,
        ITelemetryContext telemetry);
}
```

Each Send / Receive operates on **exactly one** complete UASC
`MessageChunk` (Part 6 §6.7.2). The channel pipeline above the transport
owns chunk-level framing — your job is to move chunk bytes across the
wire.

## Implementation checklist

- [ ] Buffer ownership. Buffers returned by `ReceiveChunkAsync` are
      rented from the supplied `BufferManager`; the caller (the UASC
      channel) returns them via `BufferManager.ReturnBuffer` once the
      chunk has been processed. Do **not** pool or reuse the same array
      across receives.
- [ ] Idempotent `Close`. The channel may dispose a transport from
      multiple paths (normal shutdown, fatal error, channel-state race);
      every implementation MUST treat `Close()` as a no-op when already
      closed.
- [ ] Cancellation. `ReceiveChunkAsync` should observe the cancellation
      token while awaiting peer data — long-lived sessions rely on this
      to tear down cleanly.
- [ ] Error mapping. Map transport-layer errors to
      [`ServiceResultException`](../Stack/Opc.Ua.Core/Types/Result/ServiceResultException.cs)
      with the matching `StatusCodes.BadXxx` (`BadConnectionClosed`,
      `BadTcpMessageTypeInvalid`, `BadTcpMessageTooLarge`, …) so the
      channel can route them through normal UA fault paths.
- [ ] Vectored Send. The `BufferCollection` overload is called by the
      channel for chunks that span multiple buffers (typical for the
      asymmetric handshake). If your transport does not support
      vectored writes, concatenate the segments into a single buffer
      before sending (this is what `WebSocketByteTransportBase` does).
- [ ] Client vs server. Client transports implement `ConnectAsync` to
      dial outbound; server transports built from an
      already-accepted connection should throw `NotSupportedException`
      from `ConnectAsync` (this is the contract enforced by
      `UaSCUaBinaryClientChannel`).

## Wiring the transport into the channel pipeline

### Client side

Implement [`IUaSCByteTransportFactory`](../Stack/Opc.Ua.Core/Stack/Tcp/IUaSCByteTransport.cs)
and hand the factory to a subclass of
`UaSCUaBinaryTransportChannel`:

```csharp
internal sealed class MyTransportFactory : IUaSCByteTransportFactory
{
    public string Implementation => "UA-MY";
    public IUaSCByteTransport Create(BufferManager bm, int rxSize, ITelemetryContext tel)
        => new MyByteTransport(bm, rxSize, tel);
}

public sealed class MyTransportChannel : UaSCUaBinaryTransportChannel
{
    public MyTransportChannel(ITelemetryContext telemetry)
        : base(new MyTransportFactory(), telemetry)
    {
    }
}

public sealed class MyTransportChannelFactory : ITransportChannelFactory
{
    public string UriScheme => "opc.my";
    public ITransportChannel Create(ITelemetryContext telemetry)
        => new MyTransportChannel(telemetry);
}
```

If your transport needs channel-level state (TLS validator, client cert,
custom credentials) plumbed in, override
`UaSCUaBinaryTransportChannel.OnSettingsSaved(TransportChannelSettings,
ChannelQuotas)` — that hook fires after the channel binds settings but
before it tries to connect. The WSS implementation uses this pattern;
see `WssTransportChannel.OnSettingsSaved` for a reference.

### Server side

Implement [`ITransportListener`](../Stack/Opc.Ua.Core/Stack/Transport/ITransportListener.cs)
plus an `ITransportListenerFactory`. For each accepted connection,
construct your transport directly (not through the
`IUaSCByteTransportFactory`, which is client-side only), then hand it to
a `TcpServerChannel` via `Attach(channelId, transport)`. The WSS server
path in `HttpsTransportListener.AcceptWebSocketAsync` is the canonical
template.

### Registering by URL scheme

Add an entry to
[`Utils.DefaultBindings`](../Stack/Opc.Ua.Core/Types/Utils/Utils.cs) so
the runtime auto-loads the assembly that hosts your factories when an
endpoint with your scheme is opened. The dictionary value is the
*assembly name* the runtime should load by name (e.g.
`"Opc.Ua.Bindings.Https"`).

## Worked example

[`InProcessTransportExample.cs`](../Tests/Opc.Ua.Core.Tests/Stack/Transport/InProcessTransportExample.cs)
implements an in-process loopback transport using
`System.Threading.Channels.Channel<byte[]>`. It does not use the
network stack at all — two `InProcessByteTransport` instances share a
pair of `Channel<byte[]>`s, one per direction. The test asserts:

1. A chunk sent on one peer is received intact on the other.
2. Closing one peer causes the receiver's `ReceiveChunkAsync` to throw
   `ServiceResultException(BadConnectionClosed)`.

The example is intentionally small (~200 lines) so it can serve as a
copy-paste starting point. Note that it does NOT implement the full
contract — `ConnectAsync` throws `NotSupportedException` because the
pair is created up-front via `CreatePair`. A real network transport
would implement `ConnectAsync` to open the wire.

## See also

- [`Transports.md`](Transports.md) — overall transport profile map.
- [`Profiles.md`](Profiles.md) — supported transport profile URIs.
- [`MigrationGuide.md`](MigrationGuide.md) — `IMessageSocket` →
  `IUaSCByteTransport` migration table for code coming from 1.5.378.
