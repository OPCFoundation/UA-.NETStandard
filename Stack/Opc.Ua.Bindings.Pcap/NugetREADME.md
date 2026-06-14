# OPC UA .NET Standard — Pcap binding

`OPCFoundation.NetStandard.Opc.Ua.Bindings.Pcap` is an opt-in binding
that captures, dissects, and replays the wire-level traffic of every
OPC UA channel the host process opens. It composes with the existing
`opc.tcp` channel as a transparent decorator — no application changes
are needed beyond wiring the binding through DI.

## Overview

The package contains:

- `PcapTransportChannelBinding` — `ITransportChannelFactory` decorator
  installed via `AddOpcUaBindingsPcap()` that wraps every
  `TcpTransportChannel` with a capture-aware byte transport.
- `ChannelCaptureRegistry` + `CaptureSessionManager` — runtime control
  surface to start / stop a capture, manage rolling files, and emit
  pcap + keylog artifacts.
- `ReplaySessionManager` — offline pcap dissection and frame replay
  (optional, gated by `PcapOptions.AllowMockClientReplay`).
- Audit sinks (`LoggerPcapAuditSink`, `HashChainedAuditFileSink`) for
  tamper-evident capture provenance.

## Getting started

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua;

services
    .AddOpcUa()
    .AddOpcTcpTransport()
    .AddOpcUaBindingsPcap(opts =>
    {
        opts.BaseFolder = "/var/log/opcua-pcap";
        opts.MaxActiveSessions = 4;
    });
```

## Target frameworks

`net8.0`, `net9.0`, `net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the [Packet Capture guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/PacketCapture.md)
for the full design (capture-session lifecycle, key-escrow, audit
chaining, replay security model).
