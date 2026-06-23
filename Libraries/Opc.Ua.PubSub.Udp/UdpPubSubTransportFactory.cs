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
using System.Net.NetworkInformation;
using Microsoft.Extensions.Options;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.PubSub.Udp.Dtls;

namespace Opc.Ua.PubSub.Udp
{
    /// <summary>
    /// <see cref="IPubSubTransportFactory"/> for the
    /// <see cref="Profiles.PubSubUdpUadpTransport"/> profile. One
    /// instance is registered with the DI container; it
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
        private readonly DtlsTransportOptions m_dtlsOptions;
        private readonly IPubSubDiagnostics? m_diagnostics;
        private readonly DtlsProfileRegistry? m_dtlsProfileRegistry;
        private readonly IDtlsContextFactory? m_dtlsContextFactory;

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
        /// Optional shared diagnostics sink. The DI container wires the
        /// per-component diagnostics container; tests and direct
        /// callers may pass <see langword="null"/>.
        /// </param>
        /// <param name="dtlsOptions">Optional DTLS options for opc.dtls endpoints.</param>
        /// <param name="dtlsProfileRegistry">Optional DTLS profile registry.</param>
        /// <param name="dtlsContextFactory">Optional DTLS context factory.</param>
        public UdpPubSubTransportFactory(
            IOptions<UdpTransportOptions> options,
            IPubSubDiagnostics? diagnostics = null,
            IOptions<DtlsTransportOptions>? dtlsOptions = null,
            DtlsProfileRegistry? dtlsProfileRegistry = null,
            IDtlsContextFactory? dtlsContextFactory = null)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            m_defaultOptions = options.Value ?? new UdpTransportOptions();
            m_dtlsOptions = dtlsOptions?.Value ?? new DtlsTransportOptions();
            m_diagnostics = diagnostics;
            m_dtlsProfileRegistry = dtlsProfileRegistry;
            m_dtlsContextFactory = dtlsContextFactory;
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
            if (!endpoint.IsDtls)
            {
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

            return CreateDtlsTransport(
                connection,
                endpoint,
                direction,
                networkInterface,
                telemetry,
                timeProvider);
        }

        private DtlsDatagramTransport CreateDtlsTransport(
            PubSubConnectionDataType connection,
            UdpEndpoint endpoint,
            PubSubTransportDirection direction,
            NetworkInterface? networkInterface,
            ITelemetryContext telemetry,
            TimeProvider timeProvider)
        {
            if (endpoint.AddressType != UdpAddressType.Unicast)
            {
                throw new NotSupportedException(
                    "DTLS transport (opc.dtls://) is only supported for unicast PubSub endpoints per Part 14 §7.3.2.4.");
            }

            if (m_dtlsProfileRegistry is null || m_dtlsContextFactory is null)
            {
                throw new NotSupportedException(
                    "DTLS transport requires AddUdpTransport().WithDtls(...) registration or direct DTLS dependencies.");
            }

            m_dtlsProfileRegistry.EmitStartupDiagnostic(telemetry);
            DtlsProfile profile = SelectDtlsProfile(endpoint);
            return new DtlsDatagramTransport(
                connection,
                endpoint,
                direction,
                networkInterface,
                telemetry,
                timeProvider,
                m_defaultOptions,
                m_diagnostics,
                m_dtlsContextFactory,
                profile);
        }

        /// <summary>
        /// Selects the DTLS profile at runtime from the enabled and runtime-supported set. Cipher
        /// suites/profiles are not pinned by configuration; the endpoint and
        /// <see cref="DtlsTransportOptions.PreferredProfileName"/> only express a preference, while
        /// <see cref="DtlsTransportOptions.DisabledProfiles"/> removes profiles from the candidate set
        /// even when the runtime supports them. Fails closed when no candidate remains.
        /// </summary>
        // TODO: Full in-handshake cipher-suite negotiation (ClientHello offering multiple suites and
        // ServerHello selecting one) is a future enhancement. For now a single profile is selected here
        // at runtime and reused for the whole handshake.
        private DtlsProfile SelectDtlsProfile(UdpEndpoint endpoint)
        {
            DtlsProfileRegistry registry = m_dtlsProfileRegistry!;
            ISet<string> disabled = m_dtlsOptions.DisabledProfiles;

            if (!string.IsNullOrEmpty(endpoint.DtlsProfileName)
                && IsProfileEnabled(disabled, endpoint.DtlsProfileName!)
                && registry.TryResolve(endpoint.DtlsProfileName, out DtlsProfile? endpointProfile))
            {
                return endpointProfile!;
            }

            if (!string.IsNullOrEmpty(m_dtlsOptions.PreferredProfileName)
                && IsProfileEnabled(disabled, m_dtlsOptions.PreferredProfileName!)
                && registry.TryResolve(m_dtlsOptions.PreferredProfileName, out DtlsProfile? preferredProfile))
            {
                return preferredProfile!;
            }

            foreach (DtlsProfile candidate in registry.SupportedProfiles)
            {
                if (IsProfileEnabled(disabled, candidate.Name))
                {
                    return candidate;
                }
            }

            throw new NotSupportedException(
                "No OPC UA PubSub DTLS profile is available: every runtime-supported profile is disabled by " +
                "configuration (DtlsTransportOptions.DisabledProfiles) or no profile is supported by the current " +
                ".NET BCL/runtime. Enable a supported profile to use opc.dtls:// transport.");
        }

        private static bool IsProfileEnabled(ISet<string> disabledProfiles, string profileName)
        {
            return disabledProfiles is null || !disabledProfiles.Contains(profileName);
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


