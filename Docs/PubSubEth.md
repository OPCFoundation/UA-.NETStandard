# OPC UA PubSub — Ethernet (Layer 2) transport

`Opc.Ua.PubSub.Eth` implements the OPC UA Part 14 **Ethernet** transport mapping: OPC UA PubSub NetworkMessages are carried directly inside raw Ethernet II frames (no IP, no UDP), identified by the OPC Foundation EtherType `0xB62C`. It reuses the existing UADP message encoding and the message-level PubSub security of `Opc.Ua.PubSub`, so only the transport binding is new.

- Transport profile: `PubSub Ethernet UADP` — `http://opcfoundation.org/UA-Profile/Transport/pubsub-eth-uadp` (`Profiles.PubSubEthUadpTransport`).
- Address scheme: `opc.eth://`.
- EtherType: `0xB62C`.
- Optional IEEE 802.1Q VLAN tagging (VID + PCP) for TSN / prioritized traffic.

## Addressing

The connection address is a `NetworkAddressUrlDataType.Url` of the form:

```
opc.eth://<mac>[?vid=<0-4095>&pcp=<0-7>]
```

The destination MAC accepts the hyphen form (`01-00-5E-7F-00-01`), the colon form (`01:00:5E:7F:00:01`), and the bare twelve hex digit form (`01005E7F0001`). VLAN parameters are supplied through the query string; the legacy `opc.eth://<mac>:<vid>.<pcp>` suffix is also accepted for backward compatibility.

Examples:

```
opc.eth://01-00-5E-7F-00-01                 # multicast, untagged
opc.eth://01-00-5E-7F-00-01?vid=5&pcp=6     # multicast, VLAN 5, priority 6
opc.eth://FF-FF-FF-FF-FF-FF                  # broadcast
opc.eth://00-11-22-33-44-55                 # unicast
```

The destination MAC is classified as unicast, multicast (I/G bit set), or broadcast (all ones); multicast / broadcast addresses cause the receive backend to join the corresponding group.

## Frame backends (provider model)

Raw Layer-2 frame I/O is platform-specific and privileged — there is no cross-platform BCL raw-Ethernet support. The transport never touches a socket directly; it resolves the backend through an injectable `IEthernetFrameChannelFactory` provider, and the transport owns the Ethernet/VLAN framing.

| Backend | Platforms | Notes |
| ------- | --------- | ----- |
| Native (default) | Linux (`AF_PACKET`), macOS (BPF) | libc P/Invoke, no managed dependency, NativeAOT-compatible. Requires `CAP_NET_RAW` / root (Linux) or BPF device access (macOS). |
| SharpPcap (`WithPcap()`) | Linux, macOS, **Windows** | libpcap / Npcap via SharpPcap. Opt-in. Requires the native capture library to be installed. |
| In-memory loopback | any | Deterministic, privilege-free; used by the unit / integration tests and for local diagnostics. |

On Windows the default native factory throws `PlatformNotSupportedException` (there is no raw L2 socket); register the SharpPcap backend with `WithPcap()` or inject a custom `IEthernetFrameChannelFactory`.

### NativeAOT

The default native and in-memory backends are NativeAOT / trim clean. The SharpPcap backend lives in the same package but its SharpPcap-touching members are isolated with `[UnconditionalSuppressMessage(...)]`; the SharpPcap reference and that code are compiled only for `net8.0+`. The `Opc.Ua.Aot.Tests` suite contains an AOT smoke test that exercises the SharpPcap path in the NativeAOT-published binary to verify it runs under AOT.

## Registration

```csharp
using Microsoft.Extensions.DependencyInjection;

services.AddOpcUa().AddPubSub(pubsub => pubsub
    .AddPublisher()
    .AddEthTransport(options =>
    {
        options.PreferredNetworkInterface = "eth0";
        options.DefaultVlanId = 5;
        options.DefaultPriority = 6;
    }));
```

To use the SharpPcap backend (for example on Windows with Npcap installed):

```csharp
services.AddOpcUa().AddPubSub(pubsub => pubsub
    .AddSubscriber()
    .AddEthTransport()
    .WithPcap());
```

`AddEthTransport` also accepts an `IConfiguration` / `IConfigurationSection` (default section `OpcUa:PubSub:Eth`) for options binding. A custom backend can be registered by adding your own `IEthernetFrameChannelFactory` to the service collection before `AddEthTransport`, or by constructing `EthPubSubTransportFactory` directly with an `IEthernetFrameChannelFactory` of your choice.

## Options

`EthTransportOptions` (bindable from `OpcUa:PubSub:Eth`):

| Option | Default | Meaning |
| ------ | ------- | ------- |
| `ReceiveQueueCapacity` | 1024 | Bounded receive queue depth (frames). |
| `MaxFrameSize` | 1522 | Maximum accepted frame size (standard Ethernet + 802.1Q tag); raise for jumbo frames. |
| `PreferredNetworkInterface` | `null` | NIC name fallback when the address does not name an interface. |
| `DefaultVlanId` | `null` | VLAN id applied when the address URL omits one. |
| `DefaultPriority` | `null` | 802.1Q priority applied when the address URL omits one. |
| `Promiscuous` | `false` | Place the interface in promiscuous mode (multicast is received via group membership without it). |
| `DiscoveryAnnounceRate` | 0 | Cyclic discovery announcement rate (ms); 0 disables. |
| `DiscoveryMulticastAddress` | `null` | Destination MAC for discovery announcements; defaults to the data destination MAC. |

## Discovery

`EthernetDatagramTransport` implements `IPubSubDiscoveryAnnouncementTransport`. When `DiscoveryAnnounceRate` is non-zero, discovery announcements are sent to `DiscoveryMulticastAddress` (or, when unset, the configured data destination MAC).

## Notes and limitations

- Only UADP message encoding is defined for the Ethernet mapping; JSON is not used over `opc.eth://`.
- There is no transport-level security for Ethernet (unlike `opc.dtls://`); use the message-level PubSub security (`SecurityMode` / SecurityGroups), which is configured the same way as for UDP.
- Frames exceeding `MaxFrameSize` (the link MTU) cannot be sent; enable UADP chunking or raise the MTU.
- The native AF_PACKET / BPF backends are exercised by opt-in / manual tests only, because they require privileges and real hardware; CI uses the in-memory loopback backend.

## References

- OPC UA Part 14 (PubSub) Ethernet transport mapping.
- [PubSub overview](PubSub.md) · [Profiles](Profiles.md#pubsub-transports)
