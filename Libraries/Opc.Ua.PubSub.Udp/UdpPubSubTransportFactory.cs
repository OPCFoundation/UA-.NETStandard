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
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.PubSub.Udp
{
    /// <summary>
    /// <see cref="IPubSubTransportFactory"/> for the
    /// <see cref="Profiles.PubSubUdpUadpTransport"/> profile. One
    /// instance is registered with the DI container in Phase 9; it
    /// turns each <see cref="PubSubConnectionDataType"/> with an
    /// <c>opc.udp://</c> address into a
    /// <see cref="UdpDatagramTransport"/>.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.2">
    /// Part 14 §7.3.2 UDP datagram transport</see> from the factory
    /// side. The factory honours
    /// <see cref="PubSubConnectionDataType.WriterGroups"/> /
    /// <see cref="PubSubConnectionDataType.ReaderGroups"/> to pick the
    /// transport direction; it consults
    /// <see cref="PubSubConnectionDataType.ConnectionProperties"/> for
    /// a <c>NetworkInterface</c> key (falling back to
    /// <see cref="UdpTransportOptions.PreferredNetworkInterface"/>).
    /// </remarks>
    public sealed class UdpPubSubTransportFactory : IPubSubTransportFactory
    {
        /// <summary>
        /// Property key under <c>ConnectionProperties</c> that names
        /// the preferred network interface. Matches the v1.05.06
        /// Part 14 informative usage of
        /// <c>NetworkAddressUrlDataType.NetworkInterface</c>; this
        /// override lets operators specify a different NIC without
        /// editing the standard address payload.
        /// </summary>
        public const string NetworkInterfacePropertyKey = "NetworkInterface";

        private readonly UdpTransportOptions m_defaultOptions;
        private readonly IPubSubDiagnostics? m_diagnostics;

        /// <summary>
        /// Initializes a new <see cref="UdpPubSubTransportFactory"/>.
        /// </summary>
        /// <param name="options">
        /// Default transport tunables. Per-connection overrides come
        /// from <see cref="PubSubConnectionDataType.ConnectionProperties"/>
        /// and the standard <c>NetworkAddressUrlDataType.NetworkInterface</c>
        /// field. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="diagnostics">
        /// Optional shared diagnostics sink. Phase 9 wires the
        /// per-component diagnostics container; tests and direct
        /// callers may pass <see langword="null"/>.
        /// </param>
        public UdpPubSubTransportFactory(
            IOptions<UdpTransportOptions> options,
            IPubSubDiagnostics? diagnostics = null)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            m_defaultOptions = options.Value ?? new UdpTransportOptions();
            m_diagnostics = diagnostics;
        }

        /// <inheritdoc/>
        public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

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
                    "PubSubConnection.Address is required for UDP transport.");
            }
            if (!connection.Address.TryGetValue(out NetworkAddressUrlDataType? networkAddress)
                || networkAddress is null)
            {
                throw new NotSupportedException(
                    "UDP transport requires a NetworkAddressUrlDataType address payload.");
            }
            string? url = networkAddress.Url;
            if (string.IsNullOrEmpty(url))
            {
                throw new NotSupportedException(
                    "NetworkAddressUrlDataType.Url is required for UDP transport.");
            }
            UdpEndpoint endpoint = UdpEndpointParser.Parse(url);
            string? preferredInterface = ResolveNetworkInterfaceName(
                networkAddress.NetworkInterface,
                connection.ConnectionProperties,
                m_defaultOptions.PreferredNetworkInterface);
            NetworkInterface? networkInterface = UdpNetworkInterfaceResolver.Resolve(
                preferredInterface,
                endpoint.Address.AddressFamily);
            PubSubTransportDirection direction = DetermineDirection(connection);
            return new UdpDatagramTransport(
                connection,
                endpoint,
                direction,
                networkInterface,
                telemetry,
                timeProvider,
                m_defaultOptions,
                m_diagnostics);
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
