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
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// One host-authored endpoint, network and exact operation/resource grant.
    /// Wildcard hosts, implicit ports, credentials and empty network lists are not accepted.
    /// </summary>
    public sealed record McpNetworkRule
    {
        /// <summary>
        /// Gets the exact URI scheme of the permitted transport.
        /// </summary>
        public required string Scheme { get; init; }

        /// <summary>
        /// Gets the exact hostname or IP literal, normalized using IDNA.
        /// </summary>
        public required string Host { get; init; }

        /// <summary>
        /// Gets the explicit destination port.
        /// </summary>
        public required int Port { get; init; }

        /// <summary>
        /// Gets allowed CIDR networks. An exact loopback/private range must be explicitly granted.
        /// </summary>
        public ArrayOf<string> Networks { get; init; } = [];

        /// <summary>
        /// Gets exact protocol operation names, such as GET, readproperty, subscribe or publish.
        /// </summary>
        public ArrayOf<string> Operations { get; init; } = [];

        /// <summary>
        /// Gets exact paths, topics or protocol resource selectors. Empty grants only an empty selector.
        /// </summary>
        public ArrayOf<string> Resources { get; init; } = [];

        /// <summary>
        /// Gets the capability needed before the endpoint can be used.
        /// </summary>
        public required McpCapability Capability { get; init; }
    }

    /// <summary>
    /// Resolves addresses at the connection seam so policy checks and socket connection use the same result.
    /// </summary>
    public interface IMcpDnsResolver
    {
        /// <summary>
        /// Resolves one exact host without changing its TLS or protocol identity.
        /// </summary>
        ValueTask<ArrayOf<IPAddress>> ResolveAsync(string host, CancellationToken ct = default);
    }

    /// <summary>
    /// Opens a TCP stream to an already approved IP endpoint, without another DNS lookup.
    /// </summary>
    public interface IMcpTcpConnector
    {
        /// <summary>
        /// Connects to the supplied address; the caller owns the returned stream.
        /// </summary>
        ValueTask<Stream> ConnectAsync(IPEndPoint endpoint, CancellationToken ct = default);
    }

    /// <summary>
    /// Default-deny network authority that issues immutable caller-bound connection grants.
    /// A grant re-resolves and revalidates DNS at every connection, including reconnects.
    /// </summary>
    public sealed class McpNetworkPolicy
    {
        /// <summary>
        /// Snapshots explicit host rules and injectable DNS/socket adapters.
        /// </summary>
        public McpNetworkPolicy(
            IMcpOperationPolicy authority,
            ArrayOf<McpNetworkRule> rules,
            IMcpDnsResolver? resolver = null,
            IMcpTcpConnector? connector = null)
        {
            m_authority = authority ?? throw new ArgumentNullException(nameof(authority));
            m_resolver = resolver ?? new SystemDnsResolver();
            m_connector = connector ?? new SystemTcpConnector();
            var compiled = new List<Rule>();
            foreach (McpNetworkRule rule in rules)
            {
                ArgumentNullException.ThrowIfNull(rule);
                ArgumentException.ThrowIfNullOrWhiteSpace(rule.Scheme);
                ArgumentException.ThrowIfNullOrWhiteSpace(rule.Host);
                ArgumentOutOfRangeException.ThrowIfLessThan(rule.Port, 1);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(rule.Port, 65535);
                if (!Uri.CheckSchemeName(rule.Scheme) ||
                    rule.Host.Contains('*', StringComparison.Ordinal) ||
                    rule.Networks.Count == 0 ||
                    rule.Operations.Count == 0 ||
                    !Enum.IsDefined(rule.Capability))
                {
                    throw new ArgumentException("Network grants need an exact endpoint, networks and operations.");
                }
                if (compiled.Count >= 256)
                {
                    throw new ArgumentException("The network policy supports at most 256 explicit grants.");
                }
                compiled.Add(new Rule(rule));
            }
            m_rules = [.. compiled];
        }

        /// <summary>
        /// Authorizes a concrete endpoint and protocol operation before any DNS resolution or connection.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public McpNetworkGrant Authorize(
            Uri endpoint, string operation, string resource, McpCapability capability)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            ArgumentException.ThrowIfNullOrWhiteSpace(operation);
            ArgumentNullException.ThrowIfNull(resource);
            if (!endpoint.IsAbsoluteUri ||
                !string.IsNullOrEmpty(endpoint.UserInfo) ||
                !string.IsNullOrEmpty(endpoint.Fragment))
            {
                throw Refused();
            }
            McpCallerIdentity caller = m_authority.Require(capability);
            int port = EffectivePort(endpoint);
            string host = NormalizeHost(endpoint.IdnHost);
            foreach (Rule rule in m_rules)
            {
                if (rule.Capability == capability &&
                    rule.Port == port &&
                    rule.Scheme == endpoint.Scheme &&
                    rule.Host == host &&
                    rule.Operations.Contains(operation) &&
                    (rule.Resources.Contains(resource) || (resource.Length == 0 && rule.Resources.Count == 0)))
                {
                    return new McpNetworkGrant(
                        caller, endpoint, operation, resource, capability,
                        ct => ConnectAsync(rule, host, port, ct));
                }
            }
            throw Refused();
        }

        /// <summary>
        /// Resolves once, rejects every unapproved address, then connects without a DNS-to-connect race.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        /// <exception cref="IOException"></exception>
        private async ValueTask<Stream> ConnectAsync(Rule rule, string host, int port, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ArrayOf<IPAddress> addresses = IPAddress.TryParse(host, out IPAddress? literal)
                ? [literal] : await m_resolver.ResolveAsync(host, ct).ConfigureAwait(false);
            if (addresses.Count is 0 or > 32)
            {
                throw Refused();
            }
            var approved = new IPAddress[addresses.Count];
            for (int index = 0; index < addresses.Count; index++)
            {
                IPAddress address = NormalizeAddress(addresses[index]);
                if (!IsUnicast(address) || !rule.Networks.Exists(network => network.Contains(address)))
                {
                    throw Refused();
                }
                approved[index] = new IPAddress(address.GetAddressBytes());
            }
            SocketException? lastError = null;
            for (int index = 0; index < approved.Length; index++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    return await m_connector.ConnectAsync(
                        new IPEndPoint(approved[index], port), ct).ConfigureAwait(false);
                }
                catch (SocketException exception)
                {
                    lastError = exception;
                }
            }
            ct.ThrowIfCancellationRequested();
            throw new IOException("No policy-approved endpoint address accepted the TCP connection.", lastError);
        }

        /// <summary>
        /// Normalizes DNS names and IP literals without allowing wildcard or scoped-address aliases.
        /// </summary>
        private static string NormalizeHost(string host)
        {
            if (IPAddress.TryParse(host.Trim('[', ']'), out IPAddress? literal))
            {
                return NormalizeAddress(literal).ToString();
            }
            return new IdnMapping().GetAscii(host.TrimEnd('.')).ToLowerInvariant();
        }

        /// <summary>
        /// Resolves protocol defaults explicitly, including schemes the general URI parser does not know.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static int EffectivePort(Uri endpoint)
        {
            if (endpoint.Port == 0)
            {
                throw Refused();
            }
            if (endpoint.Port > 0)
            {
                return endpoint.Port;
            }
            return endpoint.Scheme switch
            {
                "http" => 80,
                "https" => 443,
                "mqtt" => 1883,
                "mqtts" => 8883,
                "modbus" or "modbus+tcp" => 502,
                "opc.tcp" => 4840,
                "kafka" or "kafkas" => 9092,
                _ => throw Refused()
            };
        }

        /// <summary>
        /// Makes mapped IPv4 subject to the same CIDR and reserved-address policy as native IPv4.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static IPAddress NormalizeAddress(IPAddress address)
        {
            ArgumentNullException.ThrowIfNull(address);
            if (address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId != 0)
            {
                throw Refused();
            }
            return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        }

        /// <summary>
        /// Disallows unspecified, multicast and reserved destinations even when a broad network is configured.
        /// Loopback and private unicast are permitted only by an explicit matching host rule.
        /// </summary>
        private static bool IsUnicast(IPAddress address)
        {
            byte[] bytes = address.GetAddressBytes();
            return bytes.Length == 4
                ? bytes[0] != 0 && bytes[0] < 224 && !address.Equals(IPAddress.Broadcast)
                : !address.Equals(IPAddress.IPv6Any) &&
                    !address.IsIPv6Multicast &&
                    !address.IsIPv6LinkLocal &&
                    address.ScopeId == 0;
        }

        /// <summary>
        /// Returns a secret-free policy refusal without echoing the supplied URL.
        /// </summary>
        private static ServiceResultException Refused()
        {
            return new ServiceResultException(StatusCodes.BadUserAccessDenied,
                "The endpoint, operation, resource or resolved address is not allowed by host network policy.");
        }

        private readonly IMcpOperationPolicy m_authority;
        private readonly IMcpDnsResolver m_resolver;
        private readonly IMcpTcpConnector m_connector;
        private readonly Rule[] m_rules;

        /// <summary>
        /// Immutable validated form of one host rule.
        /// </summary>
        private sealed class Rule
        {
            public Rule(McpNetworkRule rule)
            {
                Scheme = rule.Scheme.ToLowerInvariant();
                Host = NormalizeHost(rule.Host);
                Port = rule.Port;
                Capability = rule.Capability;
                Operations = new HashSet<string>(StringComparer.Ordinal);
                Resources = new HashSet<string>(StringComparer.Ordinal);
                foreach (string operation in rule.Operations)
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(operation);
                    Operations.Add(operation);
                }
                foreach (string resource in rule.Resources)
                {
                    ArgumentNullException.ThrowIfNull(resource);
                    Resources.Add(resource);
                }
                Networks = [];
                foreach (string range in rule.Networks)
                {
                    Networks.Add(IPNetwork.Parse(range));
                }
            }

            public string Scheme { get; }
            public string Host { get; }
            public int Port { get; }
            public McpCapability Capability { get; }
            public HashSet<string> Operations { get; }
            public HashSet<string> Resources { get; }
            public List<IPNetwork> Networks { get; }
        }

        /// <summary>
        /// Platform DNS adapter; callers can inject deterministic resolution for tests.
        /// </summary>
        private sealed class SystemDnsResolver : IMcpDnsResolver
        {
            public async ValueTask<ArrayOf<IPAddress>> ResolveAsync(string host, CancellationToken ct = default)
            {
                return (await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false)).ToArrayOf();
            }
        }

        /// <summary>
        /// Direct IP socket adapter with explicit ownership transfer to the returned stream.
        /// </summary>
        private sealed class SystemTcpConnector : IMcpTcpConnector
        {
            public async ValueTask<Stream> ConnectAsync(IPEndPoint endpoint, CancellationToken ct = default)
            {
                Socket? socket = new(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(endpoint, ct).ConfigureAwait(false);
                    var stream = new NetworkStream(socket, ownsSocket: true);
                    socket = null;
                    return stream;
                }
                finally
                {
                    socket?.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Authorization captured for one caller and endpoint. It may be used by an owned runtime's transport
    /// background work without accepting a new caller-controlled endpoint during reconnect.
    /// </summary>
    public sealed class McpNetworkGrant
    {
        /// <summary>
        /// Creates an already-authorized exact connection grant.
        /// </summary>
        internal McpNetworkGrant(
            McpCallerIdentity owner, Uri endpoint, string operation, string resource, McpCapability capability,
            Func<CancellationToken, ValueTask<Stream>> connect)
        {
            Owner = owner;
            Endpoint = endpoint;
            Operation = operation;
            Resource = resource;
            Capability = capability;
            m_connect = connect;
        }

        /// <summary>
        /// Gets the trusted owner captured before activation.
        /// </summary>
        public McpCallerIdentity Owner { get; }

        /// <summary>
        /// Gets the exact endpoint identity, retained for TLS hostname validation.
        /// </summary>
        public Uri Endpoint { get; }

        /// <summary>
        /// Gets the authorized protocol operation.
        /// </summary>
        public string Operation { get; }

        /// <summary>
        /// Gets the authorized path, topic or resource selector.
        /// </summary>
        public string Resource { get; }

        /// <summary>
        /// Gets the capability used when the grant was issued.
        /// </summary>
        public McpCapability Capability { get; }

        /// <summary>
        /// Revalidates DNS and opens a stream only to approved addresses; caller retains stream ownership.
        /// </summary>
        public ValueTask<Stream> ConnectTcpAsync(CancellationToken ct = default)
        {
            return m_connect(ct);
        }

        private readonly Func<CancellationToken, ValueTask<Stream>> m_connect;
    }
}
