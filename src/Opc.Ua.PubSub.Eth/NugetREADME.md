# OPCFoundation.NetStandard.Opc.Ua.PubSub.Eth

OPC UA PubSub **Ethernet (Layer 2)** transport for the OPC UA .NET Standard stack.

Implements the OPC UA Part 14 Ethernet mapping (`opc.eth://`, transport profile
`http://opcfoundation.org/UA-Profile/Transport/pubsub-eth-uadp`): raw Ethernet II frames with
EtherType `0xB62C` and optional IEEE 802.1Q VLAN tagging (VID/PCP). It reuses the UADP message
encoding and the message-level PubSub security of the core PubSub library.

## Frame backends

Raw Layer-2 frame I/O is platform-specific and privileged. The transport resolves the backend
through an injectable `IEthernetFrameChannelFactory` provider:

- **Native (default)** — Linux `AF_PACKET` and macOS BPF via libc P/Invoke (NativeAOT-compatible).
  Requires `CAP_NET_RAW` / root (Linux) or BPF access (macOS).
- **SharpPcap (`WithPcap()`)** — opt-in cross-platform / Windows backend over libpcap / Npcap.
  The SharpPcap members are annotated for trimming / NativeAOT so the rest of the assembly stays
  trim-clean.
- **In-memory loopback** — a deterministic, privilege-free backend for tests and local diagnostics.

## Usage

```csharp
services.AddOpcUaPubSub(pubsub => pubsub
    .AddEthTransport(options =>
    {
        options.PreferredNetworkInterface = "eth0";
        options.DefaultVlanId = 5;
        options.DefaultPriority = 6;
    }));

// Windows / cross-platform via libpcap / Npcap:
services.AddOpcUaPubSub(pubsub => pubsub.AddEthTransport().WithPcap());
```

Address connections with `opc.eth://<mac>[?vid=<0-4095>&pcp=<0-7>]`, for example
`opc.eth://01-00-5E-00-00-01?vid=5&pcp=6`.

See the project documentation (`docs/PubSubEth.md`) for details.
