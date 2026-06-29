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
using System.Net.NetworkInformation;
using Microsoft.Extensions.Options;
using Opc.Ua.PubSub.Eth.Channels;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.PubSub.Eth
{
    /// <summary>
    /// <see cref="IPubSubTransportFactory"/> for the
    /// <see cref="EthProfiles.PubSubEthUadpTransport"/> profile. One instance
    /// is registered with the DI container; it turns each
    /// <see cref="PubSubConnectionDataType"/> with an <c>opc.eth://</c>
    /// address into an <see cref="EthernetDatagramTransport"/> backed by
    /// the registered <see cref="IEthernetFrameChannelFactory"/>.
    /// </summary>
    /// <remarks>
    /// Implements the factory side of the OPC UA Part 14 Ethernet
    /// mapping. It honours <see cref="PubSubConnectionDataType.WriterGroups"/>
    /// / <see cref="PubSubConnectionDataType.ReaderGroups"/> to pick the
    /// transport direction and resolves the network interface from the
    /// standard <c>NetworkAddressUrlDataType.NetworkInterface</c> field,
    /// the <c>NetworkInterface</c> connection property, or
    /// <see cref="EthTransportOptions.PreferredNetworkInterface"/>.
    /// </remarks>
    public sealed class EthPubSubTransportFactory : IPubSubTransportFactory
    {
        /// <summary>
        /// Property key under <c>ConnectionProperties</c> that names the
        /// preferred network interface.
        /// </summary>
        public const string NetworkInterfacePropertyKey = "NetworkInterface";

        private readonly EthTransportOptions m_options;
        private readonly IEthernetFrameChannelFactory m_channelFactory;

        /// <summary>
        /// Initializes a new <see cref="EthPubSubTransportFactory"/>.
        /// </summary>
        /// <param name="options">Default transport tunables.</param>
        /// <param name="channelFactory">
        /// The frame channel factory used to materialise the platform
        /// backend (native, SharpPcap, or in-memory).
        /// </param>
        public EthPubSubTransportFactory(
            IOptions<EthTransportOptions> options,
            IEthernetFrameChannelFactory channelFactory)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            m_options = options.Value ?? new EthTransportOptions();
            m_channelFactory = channelFactory
                ?? throw new ArgumentNullException(nameof(channelFactory));
        }

        /// <inheritdoc/>
        public string TransportProfileUri => EthProfiles.PubSubEthUadpTransport;

        /// <inheritdoc/>
        public IPubSubTransport Create(
            PubSubConnectionDataType connection,
            ITelemetryContext telemetry,
            TimeProvider timeProvider)
        {
            if (connection is null)
            {
                throw new ArgumentNullException(nameof(connection));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (timeProvider is null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }
            if (connection.Address.IsNull)
            {
                throw new NotSupportedException(
                    "PubSubConnection.Address is required for Ethernet transport.");
            }
            if (!connection.Address.TryGetValue(out NetworkAddressUrlDataType? networkAddress)
                || networkAddress is null)
            {
                throw new NotSupportedException(
                    "Ethernet transport requires a NetworkAddressUrlDataType address payload.");
            }
            string? url = networkAddress.Url;
            if (string.IsNullOrEmpty(url))
            {
                throw new NotSupportedException(
                    "NetworkAddressUrlDataType.Url is required for Ethernet transport.");
            }

            EthEndpoint endpoint = EthEndpointParser.Parse(url!);
            string? preferredInterface = ResolveNetworkInterfaceName(
                networkAddress.NetworkInterface,
                connection.ConnectionProperties,
                m_options.PreferredNetworkInterface);
            NetworkInterface? networkInterface = EthNetworkInterfaceResolver.Resolve(preferredInterface);
            PubSubTransportDirection direction = DetermineDirection(connection);

            PhysicalAddress? multicastGroup = endpoint.AddressType is EthAddressType.Unicast
                ? null
                : endpoint.Address;

            var parameters = new EthChannelParameters
            {
                InterfaceName = preferredInterface ?? networkInterface?.Name,
                NetworkInterface = networkInterface,
                InterfaceAddress = networkInterface?.GetPhysicalAddress(),
                EtherType = EthernetFrameCodec.OpcUaEtherType,
                MulticastGroup = multicastGroup,
                Promiscuous = m_options.Promiscuous,
                ReceiveQueueCapacity = m_options.ReceiveQueueCapacity,
                MaxFrameSize = m_options.MaxFrameSize
            };

            IEthernetFrameChannel channel = m_channelFactory.Create(parameters, telemetry, timeProvider);
            return new EthernetDatagramTransport(
                connection,
                endpoint,
                direction,
                channel,
                m_options,
                telemetry,
                timeProvider);
        }

        private static PubSubTransportDirection DetermineDirection(
            PubSubConnectionDataType connection)
        {
            PubSubTransportDirection direction = PubSubTransportDirection.None;
            if (!connection.WriterGroups.IsNull && connection.WriterGroups.Count > 0)
            {
                direction |= PubSubTransportDirection.Send;
            }
            if (!connection.ReaderGroups.IsNull && connection.ReaderGroups.Count > 0)
            {
                direction |= PubSubTransportDirection.Receive;
            }
            if (direction == PubSubTransportDirection.None)
            {
                direction = PubSubTransportDirection.SendReceive;
            }
            return direction;
        }

        private static string? ResolveNetworkInterfaceName(
            string? standardField,
            ArrayOf<KeyValuePair> connectionProperties,
            string? fallback)
        {
            if (!string.IsNullOrEmpty(standardField))
            {
                return standardField;
            }
            if (!connectionProperties.IsNull)
            {
                foreach (KeyValuePair entry in connectionProperties)
                {
                    if (entry.Key.IsNull)
                    {
                        continue;
                    }
                    if (!string.Equals(
                            entry.Key.Name,
                            NetworkInterfacePropertyKey,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (entry.Value.TryGetValue(out string? text)
                        && !string.IsNullOrEmpty(text))
                    {
                        return text;
                    }
                }
            }
            return fallback;
        }
    }
}
