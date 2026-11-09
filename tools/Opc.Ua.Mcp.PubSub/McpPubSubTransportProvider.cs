/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using MQTTnet.Channel;
using Opc.Ua.PubSub.Eth;
using Opc.Ua.PubSub.Eth.Channels;
using Opc.Ua.PubSub.Mqtt;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.PubSub.Udp;
using Opc.Ua.PubSub.Udp.Dtls;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Injectable, policy-enforcing transport composition for named and legacy MCP runtimes.
    /// A host replacement is trusted infrastructure, never a caller-supplied factory.
    /// </summary>
    public interface IMcpPubSubTransportProvider
    {
        /// <summary>
        /// Reports backend requirements without activating any network resource.
        /// </summary>
        JsonArray Capabilities();

        /// <summary>
        /// Captures authority and composes an existing factory without opening a transport.
        /// </summary>
        IPubSubTransportFactory CreateFactory(PubSubApplicationInput input, ISecretRegistry? credentials = null);

        /// <summary>
        /// Captures explicit authority for the legacy UDP endpoint without changing its until-stop lifetime.
        /// </summary>
        IPubSubTransportFactory CreateLegacyFactory(string endpoint, bool publisher);
    }

    /// <summary>
    /// Composes shipped UDP, DTLS, MQTT and Ethernet backends behind their actual connection/frame seams.
    /// Kafka fails explicitly because the pinned managed backend cannot authorize metadata-discovered brokers.
    /// </summary>
    public sealed class McpPubSubTransportProvider : IMcpPubSubTransportProvider
    {
        /// <summary>
        /// Creates transport composition with explicit host policy and optional DTLS/Ethernet infrastructure.
        /// </summary>
        public McpPubSubTransportProvider(
            IMcpOperationPolicy policy,
            McpNetworkPolicy network,
            McpPubSubOptions options,
            TimeProvider time,
            IOptions<DtlsTransportOptions>? dtlsOptions = null,
            DtlsProfileRegistry? dtlsProfiles = null,
            IDtlsContextFactory? dtlsFactory = null,
            IEthernetFrameChannelFactory? ethernetFactory = null,
            ApplicationConfiguration? applicationConfiguration = null)
        {
            m_policy = policy ?? throw new ArgumentNullException(nameof(policy));
            m_network = network ?? throw new ArgumentNullException(nameof(network));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_time = time ?? throw new ArgumentNullException(nameof(time));
            options.Validate();
            m_dtlsOptions = dtlsOptions;
            m_dtlsProfiles = dtlsProfiles;
            m_dtlsFactory = dtlsFactory;
            m_ethernetFactory = ethernetFactory;
            m_configuration = applicationConfiguration;
        }

        /// <inheritdoc/>
        public JsonArray Capabilities()
        {
            return new JsonArray(
                Capability("Udp", true, "IP-literal endpoint, local interface/address and peer rules required"),
                Capability("Dtls", m_dtlsFactory is not null && m_dtlsProfiles is not null,
                    "Host DTLS context, certificate manager and explicit datagram rules required"),
                Capability("Mqtt", true, "MQTTnet; TCP stream grant checked on every reconnect; exact topics"),
                Capability("Kafka", false, KafkaLimitation),
                Capability("Ethernet", m_ethernetFactory is not null || !OperatingSystem.IsWindows(),
                    m_ethernetFactory is not null
                        ? "Host-injected frame backend; actual open verifies capability; not proof of hardware interop"
                        : "Native AF_PACKET/BPF requires existing OS permission; Windows requires an injected driver"));
        }

        /// <inheritdoc/>
        public IPubSubTransportFactory CreateFactory(
            PubSubApplicationInput input, ISecretRegistry? credentials = null)
        {
            PubSubInputValidation.Validate(input, m_options);
            _ = m_policy.Require(McpCapability.PubSubRuntime);
            PubSubConnectionInput connection = input.Connection;
            string operation = input.Mode == "Publisher" ? "publish" : "subscribe";
            if (connection.Transport == PubSubTransportKind.Kafka)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, KafkaLimitation);
            }
            if (connection.Transport == PubSubTransportKind.Mqtt)
            {
                return MqttFactory(input, operation, credentials);
            }
            if (connection.Transport == PubSubTransportKind.Ethernet)
            {
                McpPubSubEthernetRule rule = m_options.EthernetRules.Find(rule =>
                    rule.Endpoint == connection.Endpoint &&
                    rule.NetworkInterface == connection.NetworkInterface &&
                    rule.Operations.Contains(operation)) ??
                    throw Refused();
                ArgumentOutOfRangeException.ThrowIfLessThan(rule.MaxFrameBytes, 64);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(rule.MaxFrameBytes, 65536);
                return new EthPubSubTransportFactory(Options.Create(new EthTransportOptions
                {
                    PreferredNetworkInterface = rule.NetworkInterface,
                    MaxFrameSize = Math.Min(rule.MaxFrameBytes, m_options.MaxMessageBytes),
                    ReceiveQueueCapacity = m_options.MaxBufferedMessages,
                    DiscoveryAnnounceRate = 0
                }), new AuthorizedEthernetFactory(
                    rule, m_ethernetFactory ?? new DefaultEthernetFrameChannelFactory(), m_options, m_time));
            }
            McpPubSubDatagramRule datagram = DatagramRule(connection, operation);
            if (connection.Transport == PubSubTransportKind.Dtls &&
                (m_dtlsFactory is null || m_dtlsProfiles is null))
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "DTLS requires host-registered DTLS profiles, certificate authority and context factory.");
            }
            return new UdpPubSubTransportFactory(
                Options.Create(UdpOptions(datagram)), dtlsOptions: m_dtlsOptions,
                dtlsProfileRegistry: m_dtlsProfiles, dtlsContextFactory: m_dtlsFactory);
        }

        /// <summary>
        /// Captures exact rules for a legacy UDP runtime without changing its lifetime or wire defaults.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public IPubSubTransportFactory CreateLegacyFactory(string endpoint, bool publisher)
        {
            _ = m_policy.Require(McpCapability.PubSubRuntime);
            string operation = publisher ? "publish" : "subscribe";
            McpPubSubDatagramRule rule = m_options.DatagramRules.Find(rule =>
                rule.Endpoint == endpoint && rule.Operations.Contains(operation)) ??
                throw Refused();
            return new UdpPubSubTransportFactory(Options.Create(UdpOptions(rule)));
        }

        /// <summary>
        /// Captures every requested broker topic before a transport factory can resolve credentials or connect.
        /// </summary>
        private MqttPubSubTransportFactory MqttFactory(
            PubSubApplicationInput input, string operation, ISecretRegistry? credentials)
        {
            PubSubConnectionInput connection = input.Connection;
            var endpoint = new Uri(connection.Endpoint, UriKind.Absolute);
            var topics = new HashSet<string>(StringComparer.Ordinal)
            {
                connection.Topic!, connection.MetadataTopic!
            };
            topics.UnionWith(connection.AuxiliaryTopics.ToList());
            McpNetworkGrant connectionGrant = m_network.Authorize(
                endpoint, operation, connection.Topic!, McpCapability.PubSubRuntime);
            foreach (string topic in topics)
            {
                _ = m_network.Authorize(endpoint, operation, topic, McpCapability.PubSubRuntime);
            }
            HashSet<string> publicationTopics = operation == "publish"
                ? new HashSet<string>(topics, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            // MQTTnet configures a last will for both connection directions.
            // The standard status topic is an independent publish grant, not implied by subscribe.
            string statusTopic = MqttTopicBuilder.BuildPublisherTopic("opcua",
                connection.Encoding == PubSubMessageEncoding.Json ? MqttEncoding.Json : MqttEncoding.Uadp,
                "status", PubSubInputValidation.Publisher(input.PublisherId, connection.Encoding).ToVariant());
            _ = m_network.Authorize(endpoint, "publish", statusTopic, McpCapability.PubSubRuntime);
            publicationTopics.Add(statusTopic);
            return MqttPubSubTransportFactory.CreateDefault(
                PubSubInputValidation.Profile(connection),
                Options.Create(new MqttConnectionOptions
                {
                    Endpoint = connection.Endpoint,
                    IncludeDiscoverySubscriptions = false,
                    MaxConcurrentSubscriptions = 32,
                    MaxNetworkMessageSize = m_options.MaxMessageBytes,
                    ConnectTimeout = TimeSpan.FromSeconds(10),
                    UserName = connection.UserName,
                    PasswordSecretId = connection.CredentialReference is null ? null : "McpRuntime:password"
                }
                .WithStreamProvider(new AuthorizedMqttStream(connectionGrant))
                .WithTopicPolicy(new AuthorizedMqttTopics(
                    publicationTopics, operation == "subscribe" ? topics : [],
                    m_options.MaxMessageBytes, m_options.MaxMessagesPerSecond, m_time))),
                credentials, m_configuration);
        }

        /// <summary>
        /// Selects only a matching host-authored datagram rule.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private McpPubSubDatagramRule DatagramRule(PubSubConnectionInput connection, string operation)
        {
            return m_options.DatagramRules.Find(rule =>
                rule.Endpoint == connection.Endpoint &&
                rule.NetworkInterface == connection.NetworkInterface &&
                rule.Operations.Contains(operation)) ??
                throw Refused();
        }

        /// <summary>
        /// Applies real interface, peer and per-datagram limits, including DTLS control traffic.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private UdpTransportOptions UdpOptions(McpPubSubDatagramRule rule)
        {
            var endpoint = new Uri(rule.Endpoint, UriKind.Absolute);
            if (endpoint.Scheme is not ("opc.udp" or "opc.dtls") ||
                endpoint.UserInfo.Length != 0 ||
                endpoint.Query.Length != 0 ||
                endpoint.Fragment.Length != 0 ||
                endpoint.AbsolutePath is not ("" or "/") ||
                endpoint.Port is < 1 or > 65535 ||
                !IPAddress.TryParse(endpoint.Host.Trim('[', ']'), out IPAddress? destination) ||
                destination.Equals(IPAddress.Any) ||
                destination.Equals(IPAddress.IPv6Any))
            {
                throw new ArgumentException("Host datagram rules require a literal address and explicit port.");
            }
            PubSubInputValidation.Text(rule.NetworkInterface, 256);
            ArgumentOutOfRangeException.ThrowIfLessThan(rule.MaxFrameBytes, 256);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(rule.MaxFrameBytes, 65507);
            ArgumentOutOfRangeException.ThrowIfLessThan(rule.Ttl, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(rule.Ttl, 255);
            return new UdpTransportOptions
            {
                PreferredNetworkInterface = rule.NetworkInterface,
                MaxFrameSize = Math.Min(rule.MaxFrameBytes, m_options.MaxMessageBytes),
                ReceiveQueueCapacity = m_options.MaxBufferedMessages,
                ReceiveBufferSize = m_options.MaxBufferedBytes,
                Ttl = rule.Ttl,
                MulticastLoopback = rule.MulticastLoopback
            }
            .WithLocalAddress(IPAddress.Parse(rule.LocalAddress))
            .WithDatagramPolicy(new AuthorizedDatagrams(rule, m_options, m_time));
        }

        /// <summary>
        /// Projects backend presence separately from host permission and actual driver activation.
        /// </summary>
        private JsonObject Capability(string name, bool backend, string detail)
        {
            return new JsonObject
            {
                ["transport"] = name,
                ["backendAvailable"] = backend,
                ["hostEnabled"] = m_policy.IsEnabled(McpCapability.PubSubRuntime),
                ["detail"] = detail,
                ["activated"] = false
            };
        }

        /// <summary>
        /// Creates a stable refusal without echoing sensitive provider exceptions.
        /// </summary>
        internal static ServiceResultException Refused()
        {
            return new ServiceResultException(StatusCodes.BadUserAccessDenied,
                "The PubSub target, operation, interface or peer is not explicitly authorized by the host.");
        }

        /// <summary>The pinned default Kafka backend's policy limitation.</summary>
        internal const string KafkaLimitation =
            "Missing policy-capable Kafka backend: managed Dekaf 1.21.1 has no supported public per-broker " +
            "connection/DNS seam. Bootstrap allowlisting cannot constrain metadata-discovered broker egress. " +
            "Configuration/schema are supported; built-in live Kafka is blocked, not activated.";

        private readonly IMcpOperationPolicy m_policy;
        private readonly McpNetworkPolicy m_network;
        private readonly McpPubSubOptions m_options;
        private readonly TimeProvider m_time;
        private readonly IOptions<DtlsTransportOptions>? m_dtlsOptions;
        private readonly DtlsProfileRegistry? m_dtlsProfiles;
        private readonly IDtlsContextFactory? m_dtlsFactory;
        private readonly IEthernetFrameChannelFactory? m_ethernetFactory;
        private readonly ApplicationConfiguration? m_configuration;

        /// <summary>
        /// Connects through the immutable grant without using a request AsyncLocal on background reconnect.
        /// </summary>
        private sealed class AuthorizedMqttStream(McpNetworkGrant grant) : IMqttClientStreamProvider
        {
            /// <inheritdoc/>
            public Task<Stream> ConnectAsync(EndPoint brokerEndPoint, CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(brokerEndPoint);
                return grant.ConnectTcpAsync(cancellationToken).AsTask();
            }
        }

        /// <summary>
        /// Captured topic authority at the real MQTT protocol boundary.
        /// </summary>
        private sealed class AuthorizedMqttTopics(
            HashSet<string> publications, HashSet<string> subscriptions,
            int maximum, int rate, TimeProvider time) : IMqttTopicPolicy
        {
            /// <inheritdoc/>
            public void RequireSubscribe(string topic)
            {
                if (!subscriptions.Contains(topic))
                {
                    throw Refused();
                }
            }

            /// <inheritdoc/>
            public void RequirePublish(string topic, int payloadLength)
            {
                if (!publications.Contains(topic))
                {
                    throw Refused();
                }
                if (payloadLength > maximum || !m_send.TryTake())
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                        "The PubSub frame size or publication rate exceeds the host ceiling.");
                }
            }

            /// <inheritdoc/>
            public bool AcceptReceive(string topic, int payloadLength)
            {
                return payloadLength <= maximum && subscriptions.Contains(topic) && m_receive.TryTake();
            }

            private readonly McpPubSubRateBudget m_send = new(rate, time);
            private readonly McpPubSubRateBudget m_receive = new(rate, time);
        }

        /// <summary>
        /// Captured literal-address authority applied to every real UDP datagram and interface.
        /// </summary>
        private sealed class AuthorizedDatagrams : IUdpDatagramPolicy
        {
            /// <summary>
            /// Validates host data without invoking DNS.
            /// </summary>
            internal AuthorizedDatagrams(McpPubSubDatagramRule rule, McpPubSubOptions options, TimeProvider time)
            {
                var endpoint = new Uri(rule.Endpoint, UriKind.Absolute);
                m_endpoint = new IPEndPoint(IPAddress.Parse(endpoint.Host.Trim('[', ']')), endpoint.Port);
                m_rule = rule;
                m_local = IPAddress.Parse(rule.LocalAddress);
                m_maximum = Math.Min(rule.MaxFrameBytes, options.MaxMessageBytes);
                m_peers = [.. rule.Peers.ToList().Select(peer =>
                {
                    ArgumentOutOfRangeException.ThrowIfLessThan(peer.FirstPort, 1);
                    ArgumentOutOfRangeException.ThrowIfGreaterThan(peer.LastPort, 65535);
                    ArgumentOutOfRangeException.ThrowIfLessThan(peer.LastPort, peer.FirstPort);
                    return (IPAddress.Parse(peer.Address), peer.FirstPort, peer.LastPort);
                })];
                m_send = new McpPubSubRateBudget(options.MaxMessagesPerSecond, time);
                m_receive = new McpPubSubRateBudget(options.MaxMessagesPerSecond, time);
            }

            /// <inheritdoc/>
            public bool AllowStandardDiscovery =>
                m_rule.AllowStandardDiscovery && m_rule.Operations.Contains("discovery");

            /// <inheritdoc/>
            public void RequireOpen(
                UdpEndpoint endpoint, PubSubTransportDirection direction, NetworkInterface? networkInterface)
            {
                if (!endpoint.Address.Equals(m_endpoint.Address) ||
                    endpoint.Port != m_endpoint.Port ||
                    networkInterface is null ||
                    (networkInterface.Name != m_rule.NetworkInterface &&
                        networkInterface.Id != m_rule.NetworkInterface) ||
                    !networkInterface.GetIPProperties().UnicastAddresses.Any(value => value.Address.Equals(m_local)) ||
                    ((direction & PubSubTransportDirection.Send) != 0 &&
                        !m_rule.Operations.Contains("publish") &&
                        !m_rule.Operations.Contains("action")) ||
                    ((direction & PubSubTransportDirection.Receive) != 0 &&
                        !m_rule.Operations.Contains("subscribe") &&
                        !m_rule.AllowReplies))
                {
                    throw Refused();
                }
                m_interfaceIndex = m_local.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                    ? networkInterface.GetIPProperties().GetIPv4Properties().Index
                    : networkInterface.GetIPProperties().GetIPv6Properties().Index;
            }

            /// <inheritdoc/>
            public void RequireSend(IPEndPoint destination, int payloadLength)
            {
                bool data = destination.Equals(m_endpoint) &&
                    (m_rule.Operations.Contains("publish") || m_rule.Operations.Contains("action"));
                bool discovery = AllowStandardDiscovery &&
                    destination.Equals(UdpDatagramTransport.StandardDiscoveryEndpoint);
                bool reply = m_rule.AllowReplies && Peer(destination);
                if (!data && !discovery && !reply)
                {
                    throw Refused();
                }
                if (payloadLength > m_maximum || !m_send.TryTake())
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                        "The datagram frame size or rate exceeds the host ceiling.");
                }
            }

            /// <inheritdoc/>
            public bool AcceptReceive(IPEndPoint source, int interfaceIndex, int payloadLength)
            {
                return interfaceIndex == m_interfaceIndex &&
                    payloadLength <= m_maximum &&
                    Peer(source) &&
                    m_receive.TryTake();
            }

            /// <summary>
            /// Matches exact peer addresses and explicitly authorized port intervals.
            /// </summary>
            private bool Peer(IPEndPoint peer)
            {
                return m_peers.Any(rule => rule.Address.Equals(peer.Address) &&
                    peer.Port >= rule.FirstPort &&
                    peer.Port <= rule.LastPort);
            }

            private readonly McpPubSubDatagramRule m_rule;
            private readonly IPEndPoint m_endpoint;
            private readonly IPAddress m_local;
            private readonly int m_maximum;
            private readonly (IPAddress Address, int FirstPort, int LastPort)[] m_peers;
            private readonly McpPubSubRateBudget m_send;
            private readonly McpPubSubRateBudget m_receive;
            private int m_interfaceIndex;
        }

        /// <summary>
        /// Restricts the real Layer-2 channel and full frames rather than treating a TCP rule as Ethernet authority.
        /// </summary>
        private sealed class AuthorizedEthernetFactory(
            McpPubSubEthernetRule rule, IEthernetFrameChannelFactory inner,
            McpPubSubOptions options, TimeProvider clock)
            : IEthernetFrameChannelFactory
        {
            /// <inheritdoc/>
            public IEthernetFrameChannel Create(
                EthChannelParameters parameters, ITelemetryContext telemetry, TimeProvider timeProvider)
            {
                var local = PhysicalAddress.Parse(rule.LocalMac);
                if (parameters.InterfaceName != rule.NetworkInterface ||
                    parameters.Promiscuous ||
                    (parameters.InterfaceAddress is not null && !parameters.InterfaceAddress.Equals(local)))
                {
                    throw Refused();
                }
                var approved = new EthChannelParameters
                {
                    InterfaceName = parameters.InterfaceName,
                    NetworkInterface = parameters.NetworkInterface,
                    InterfaceAddress = local,
                    EtherType = EthernetFrameCodec.OpcUaEtherType,
                    MulticastGroup = parameters.MulticastGroup,
                    MaxFrameSize = Math.Min(rule.MaxFrameBytes, options.MaxMessageBytes),
                    ReceiveQueueCapacity = options.MaxBufferedMessages
                };
                return new AuthorizedEthernetChannel(
                    inner.Create(approved, telemetry, timeProvider), rule, options, clock);
            }
        }

        /// <summary>
        /// Checks source/destination MAC, VLAN and priority on full inbound and outbound Ethernet frames.
        /// </summary>
        private sealed class AuthorizedEthernetChannel : IEthernetFrameChannel
        {
            /// <summary>
            /// Captures a complete immutable frame policy.
            /// </summary>
            internal AuthorizedEthernetChannel(
                IEthernetFrameChannel inner, McpPubSubEthernetRule rule, McpPubSubOptions options, TimeProvider clock)
            {
                m_inner = inner;
                m_rule = rule;
                m_endpoint = EthEndpointParser.Parse(rule.Endpoint);
                m_local = PhysicalAddress.Parse(rule.LocalMac);
                m_sources = [.. rule.SourceMacs.ToList().Select(PhysicalAddress.Parse)];
                m_send = new McpPubSubRateBudget(options.MaxMessagesPerSecond, clock);
                m_receive = new McpPubSubRateBudget(options.MaxMessagesPerSecond, clock);
            }

            /// <inheritdoc/>
            public PhysicalAddress InterfaceAddress => m_inner.InterfaceAddress;

            /// <inheritdoc/>
            public bool IsOpen => m_inner.IsOpen;

            /// <inheritdoc/>
            public async ValueTask OpenAsync(CancellationToken cancellationToken = default)
            {
                await m_inner.OpenAsync(cancellationToken).ConfigureAwait(false);
                if (!m_inner.InterfaceAddress.Equals(m_local))
                {
                    await m_inner.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    throw Refused();
                }
            }

            /// <inheritdoc/>
            public ValueTask CloseAsync(CancellationToken cancellationToken = default)
            {
                return m_inner.CloseAsync(cancellationToken);
            }

            /// <inheritdoc/>
            public ValueTask SendFrameAsync(
                ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default)
            {
                if (!m_rule.Operations.Contains("publish") ||
                    !Accept(frame.Span, sending: true) ||
                    !m_send.TryTake())
                {
                    throw Refused();
                }
                return m_inner.SendFrameAsync(frame, cancellationToken);
            }

            /// <inheritdoc/>
            public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReceiveFramesAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                await foreach (ReadOnlyMemory<byte> frame in m_inner.ReceiveFramesAsync(cancellationToken)
                    .ConfigureAwait(false))
                {
                    if (m_rule.Operations.Contains("subscribe") &&
                        Accept(frame.Span, sending: false) &&
                        m_receive.TryTake())
                    {
                        yield return frame;
                    }
                }
            }

            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                return m_inner.DisposeAsync();
            }

            /// <summary>
            /// Validates the wire addresses and tagging, not merely the configuration text.
            /// </summary>
            private bool Accept(ReadOnlySpan<byte> frame, bool sending)
            {
                if (frame.Length > m_rule.MaxFrameBytes ||
                    !EthernetFrameCodec.TryParse(frame, out _, out ushort? vlan, out byte? priority) ||
                    vlan != m_endpoint.VlanId ||
                    priority != m_endpoint.Priority ||
                    !frame[..6].SequenceEqual(m_endpoint.Address.GetAddressBytes()))
                {
                    return false;
                }
                var source = new PhysicalAddress(frame.Slice(6, 6).ToArray());
                return sending ? source.Equals(m_local) : m_sources.Contains(source);
            }

            private readonly IEthernetFrameChannel m_inner;
            private readonly McpPubSubEthernetRule m_rule;
            private readonly EthEndpoint m_endpoint;
            private readonly PhysicalAddress m_local;
            private readonly PhysicalAddress[] m_sources;
            private readonly McpPubSubRateBudget m_send;
            private readonly McpPubSubRateBudget m_receive;
        }
    }
}
