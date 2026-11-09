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

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Host-authored PubSub ceilings and non-TCP target rules. Empty target lists deny all activation.
    /// Shared runtime admission and lease ceilings remain owned by McpExecutionOptions.
    /// </summary>
    public sealed record McpPubSubOptions
    {
        /// <summary>
        /// Gets the maximum fields per DataSet.
        /// </summary>
        public int MaxFields { get; init; } = 64;

        /// <summary>
        /// Gets the maximum scalar elements in an array field.
        /// </summary>
        public int MaxArrayElements { get; init; } = 1024;

        /// <summary>
        /// Gets the maximum encoded or decoded message budget.
        /// </summary>
        public int MaxMessageBytes { get; init; } = 65536;

        /// <summary>
        /// Gets the maximum buffered DataSets per runtime.
        /// </summary>
        public int MaxBufferedMessages { get; init; } = 64;

        /// <summary>
        /// Gets the aggregate decoded receive buffer budget.
        /// </summary>
        public int MaxBufferedBytes { get; init; } = 1_048_576;

        /// <summary>
        /// Gets the maximum frames accepted or sent in one monotonic one-second window.
        /// </summary>
        public int MaxMessagesPerSecond { get; init; } = 100;

        /// <summary>
        /// Gets the minimum publishing interval, preventing callers from raising background traffic rates.
        /// </summary>
        public int MinimumPublishingIntervalMilliseconds { get; init; } = 100;

        /// <summary>
        /// Gets the configuration artifact ceiling before decoding or serializing.
        /// </summary>
        public int MaxConfigurationBytes { get; init; } = 1_048_576;

        /// <summary>
        /// Gets the maximum connections/components accepted in one remote configuration.
        /// </summary>
        public int MaxConfigurationComponents { get; init; } = 128;

        /// <summary>
        /// Gets the explicit UDP/DTLS rules.
        /// </summary>
        public ArrayOf<McpPubSubDatagramRule> DatagramRules { get; init; } = [];

        /// <summary>
        /// Gets the explicit Ethernet rules.
        /// </summary>
        public ArrayOf<McpPubSubEthernetRule> EthernetRules { get; init; } = [];

        /// <summary>
        /// Gets the exact named-session, node and operation rules for client adapters.
        /// </summary>
        public ArrayOf<McpPubSubAdapterRule> AdapterRules { get; init; } = [];

        /// <summary>
        /// Gets exact remote model targets and named operations. Session grants alone do not authorize a target.
        /// </summary>
        public ArrayOf<McpPubSubModelRule> ModelRules { get; init; } = [];

        /// <summary>
        /// Gets whether declarative bridges may lower message-layer security.
        /// The caller must also explicitly request the downgrade.
        /// </summary>
        public bool AllowSecurityDowngrade { get; init; }

        /// <summary>
        /// Checks host limits without performing I/O.
        /// </summary>
        public void Validate()
        {
            Limit(MaxFields, 1, 500);
            Limit(MaxArrayElements, 1, 65536);
            Limit(MaxMessageBytes, 256, 1_048_576);
            Limit(MaxBufferedMessages, 1, 500);
            Limit(MaxBufferedBytes, MaxMessageBytes, 16 * 1_048_576);
            Limit(MaxMessagesPerSecond, 1, 10000);
            Limit(MinimumPublishingIntervalMilliseconds, 10, 60000);
            Limit(MaxConfigurationBytes, 1024, 16 * 1_048_576);
            Limit(MaxConfigurationComponents, 1, 500);
            Limit(DatagramRules.Count, 0, 256);
            Limit(EthernetRules.Count, 0, 256);
            Limit(AdapterRules.Count, 0, 256);
            Limit(ModelRules.Count, 0, 256);
        }

        /// <summary>
        /// Checks inclusive bounds without silently changing host intent.
        /// </summary>
        private static void Limit(int value, int minimum, int maximum)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, minimum);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, maximum);
        }
    }

    /// <summary>
    /// A concrete UDP/DTLS endpoint and interface grant, not a TCP/DNS approval.
    /// Receive peers and reply port ranges must be explicitly listed, including loopback.
    /// </summary>
    public sealed record McpPubSubDatagramRule
    {
        /// <summary>
        /// Gets the exact opc.udp or opc.dtls URI with an IP literal and explicit port.
        /// </summary>
        public required string Endpoint { get; init; }

        /// <summary>
        /// Gets the exact NIC name or ID selected by the host.
        /// </summary>
        public required string NetworkInterface { get; init; }

        /// <summary>
        /// Gets the local unicast address belonging to that interface.
        /// </summary>
        public required string LocalAddress { get; init; }

        /// <summary>
        /// Gets exact operations: publish, subscribe, action, discovery.
        /// </summary>
        public ArrayOf<string> Operations { get; init; } = [];

        /// <summary>
        /// Gets allowed source addresses. No wildcard host or implicit DNS resolution is supported.
        /// </summary>
        public ArrayOf<McpPubSubPeerRule> Peers { get; init; } = [];

        /// <summary>
        /// Gets whether the standard 224.0.2.14:4840 discovery group may be joined and used.
        /// </summary>
        public bool AllowStandardDiscovery { get; init; }

        /// <summary>
        /// Gets whether replies to accepted source ports are allowed, including DTLS handshake responses.
        /// </summary>
        public bool AllowReplies { get; init; }

        /// <summary>
        /// Gets the maximum UDP payload, including DTLS control records.
        /// </summary>
        public int MaxFrameBytes { get; init; } = 65507;

        /// <summary>
        /// Gets the maximum IP TTL/hop limit. A value of one confines multicast to the local subnet.
        /// </summary>
        public int Ttl { get; init; } = 1;

        /// <summary>
        /// Gets whether multicast loopback is enabled for this explicit host rule.
        /// </summary>
        public bool MulticastLoopback { get; init; }
    }

    /// <summary>
    /// One IP-literal source/reply range. Ephemeral ports are never implicitly trusted.
    /// </summary>
    public sealed record McpPubSubPeerRule
    {
        /// <summary>
        /// Gets the exact peer address.
        /// </summary>
        public required string Address { get; init; }

        /// <summary>
        /// Gets the first permitted port.
        /// </summary>
        public required int FirstPort { get; init; }

        /// <summary>
        /// Gets the last permitted port.
        /// </summary>
        public required int LastPort { get; init; }
    }

    /// <summary>
    /// Exact Layer-2 scope. VLAN/priority are taken from the URI and matched without fallback.
    /// No rule grants OS privileges or installs a driver.
    /// </summary>
    public sealed record McpPubSubEthernetRule
    {
        /// <summary>
        /// Gets the exact opc.eth destination MAC/VLAN/priority URI.
        /// </summary>
        public required string Endpoint { get; init; }

        /// <summary>
        /// Gets the host-selected interface name or ID.
        /// </summary>
        public required string NetworkInterface { get; init; }

        /// <summary>
        /// Gets the exact local MAC address of the selected channel.
        /// </summary>
        public required string LocalMac { get; init; }

        /// <summary>
        /// Gets exact publish/subscribe/bridge operations.
        /// </summary>
        public ArrayOf<string> Operations { get; init; } = [];

        /// <summary>
        /// Gets permitted source MAC addresses, independently of the multicast destination.
        /// </summary>
        public ArrayOf<string> SourceMacs { get; init; } = [];

        /// <summary>
        /// Gets the full Ethernet frame ceiling, including MAC and VLAN headers.
        /// </summary>
        public int MaxFrameBytes { get; init; } = 1522;
    }

    /// <summary>
    /// Exact adapter permission on an existing named session. Node identities use expanded namespace URIs.
    /// </summary>
    public sealed record McpPubSubAdapterRule
    {
        /// <summary>
        /// Gets the actual host session name.
        /// </summary>
        public required string SessionName { get; init; }

        /// <summary>
        /// Gets the exact endpoint URL of the existing session.
        /// </summary>
        public required string Endpoint { get; init; }

        /// <summary>
        /// Gets read, write or call.
        /// </summary>
        public required string Operation { get; init; }

        /// <summary>
        /// Gets the namespace-qualified variable or method object node.
        /// </summary>
        public required string NodeId { get; init; }

        /// <summary>
        /// Gets the namespace-qualified method node for call rules only.
        /// </summary>
        public string? MethodId { get; init; }
    }

    /// <summary>
    /// Exact remote model authority, separate from local network activation and caller identity.
    /// </summary>
    public sealed record McpPubSubModelRule
    {
        /// <summary>
        /// Gets the actual registered UA session name.
        /// </summary>
        public required string SessionName { get; init; }

        /// <summary>
        /// Gets the namespace-qualified object identity.
        /// </summary>
        public required string NodeId { get; init; }

        /// <summary>
        /// Gets exact MCP operation names permitted on this object.
        /// </summary>
        public ArrayOf<string> Operations { get; init; } = [];
    }
}
