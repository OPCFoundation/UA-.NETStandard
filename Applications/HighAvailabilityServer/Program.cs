/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Net;
using Crdt;
using HighAvailability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server.Distributed;
using Opc.Ua.Server.Distributed.Crdt;
using Opc.Ua.Server.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

int port = int.TryParse(builder.Configuration["port"], out int p) ? p : 62543;
string nodeId = builder.Configuration["HA_NODE_ID"] ?? Guid.NewGuid().ToString("N");
string endpointUrl = $"opc.tcp://localhost:{port}/HighAvailabilityServer";
string applicationUri = $"urn:localhost:OPCFoundation:HighAvailabilityServer:{nodeId}";

builder.Services.AddSingleton(new HaSampleReplicaInfo(nodeId));

// Optional: a base64 32-byte master key shared by all replicas (provisioned
// from a Kubernetes Secret / KMS in production). When present, every record
// written to the shared store is encrypted + integrity-protected at rest.
string? recordKeyBase64 = builder.Configuration["HA_RECORD_KEY"];
byte[]? recordKey = string.IsNullOrWhiteSpace(recordKeyBase64)
    ? null
    : Convert.FromBase64String(recordKeyBase64);

// Opt into mirrored fast reconnect (default is the safe re-auth-on-failover).
bool enableFastReconnect =
    bool.TryParse(builder.Configuration["HA_FAST_RECONNECT"], out bool fr) && fr;

// Select the redundancy topology: "ap" (active/passive, default — a single
// elected writer) or "aa" (active/active — every replica writes and converges
// by CRDT gossip).
string haMode = (builder.Configuration["HA_MODE"] ?? "ap").Trim().ToLowerInvariant();
bool activeActive = haMode is "aa" or "activeactive" or "active-active";

if (activeActive && recordKey != null)
{
    // Encrypt mirrored session entries at rest (sessions are gossiped as a CRDT).
    builder.Services.AddSingleton<IRecordProtector>(_ => new AesCbcHmacRecordProtector(recordKey));
}

IOpcUaServerBuilder ua = builder.Services
    .AddOpcUa()
    .AddServer(o =>
    {
        o.ApplicationName = "HighAvailabilityServer";
        o.ApplicationUri = applicationUri;
        o.ProductUri = "uri:opcfoundation.org:HighAvailabilityServer";
        o.AutoAcceptUntrustedCertificates = true;
        o.EndpointUrls.Add(endpointUrl);
    })
    .AddNodeManager<HaSampleNodeManagerFactory>();

RedundancySupport redundancyMode;
if (activeActive)
{
    // The sample node manager gates writes on leadership; in active/active every
    // replica is a writer, so make every replica a leader.
    builder.Services.AddSingleton<ILeaderElection>(_ => new StaticLeaderElection(true));

    int gossipPort = int.TryParse(builder.Configuration["HA_GOSSIP_PORT"], out int gp) ? gp : 4840;
    var gossipPeers = new List<IPEndPoint>();
    foreach (string peer in ReadList(builder.Configuration, "HA_GOSSIP_PEERS"))
    {
        gossipPeers.Add(ParseEndpoint(peer));
    }
    ReplicaId replicaId = ReplicaIdFromNodeId(nodeId);

    ua.UseReplicatedAddressSpace(r =>
        {
            r.ReplicaId = replicaId;
            r.UseTcpGossip(IPAddress.Any, gossipPort);
            foreach (IPEndPoint peer in gossipPeers)
            {
                r.AddPeer(peer);
            }
        })
        .UseReplicatedSessions(s =>
        {
            // Mirrored session entries gossip on a second port; peers use the
            // address-space port + 1 by convention.
            s.ReplicaId = replicaId;
            s.UseTcpGossip(IPAddress.Any, gossipPort + 1);
            foreach (IPEndPoint peer in gossipPeers)
            {
                s.AddPeer(new IPEndPoint(peer.Address, peer.Port + 1));
            }
            s.Session.EnableFastReconnect = enableFastReconnect;
        });
    redundancyMode = RedundancySupport.HotAndMirrored;
}
else
{
    ua.UseDistributedAddressSpace(d =>
        {
            d.UseLeaderElection = true;
            d.NodeId = nodeId;
            if (recordKey != null)
            {
                d.RecordProtectorFactory = _ => new AesCbcHmacRecordProtector(recordKey);
            }
        })
        .UseDistributedSessions(s =>
        {
            // Mirror session state across replicas; the standby still runs the full
            // ActivateSession signature check on a token-reuse reconnect.
            s.EnableFastReconnect = enableFastReconnect;
        });
    redundancyMode = RedundancySupport.Hot;
}

ua.AddServerRedundancy(r =>
{
    r.Mode = redundancyMode;
    foreach (string peerServerUri in ReadList(builder.Configuration, "peerServerUris"))
    {
        r.PeerServerUris.Add(peerServerUri);
    }
});

await builder.Build().RunAsync().ConfigureAwait(false);

static IEnumerable<string> ReadList(IConfiguration configuration, string key)
{
    string? value = configuration[key];
    if (string.IsNullOrWhiteSpace(value))
    {
        yield break;
    }

    foreach (string item in value.Split(
        [',', ';'],
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        yield return item;
    }
}

static ReplicaId ReplicaIdFromNodeId(string nodeId)
{
    // Derive a stable replica identity from the node id so it survives restarts.
    byte[] hash = System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(nodeId));
    return new ReplicaId(new Guid(hash.AsSpan(0, 16).ToArray()));
}

static IPEndPoint ParseEndpoint(string hostPort)
{
    int separator = hostPort.LastIndexOf(':');
    if (separator <= 0 || separator == hostPort.Length - 1)
    {
        throw new FormatException($"Invalid gossip endpoint '{hostPort}'; expected host:port.");
    }

    string host = hostPort[..separator];
    int port = int.Parse(hostPort[(separator + 1)..], System.Globalization.CultureInfo.InvariantCulture);
    IPAddress address = IPAddress.TryParse(host, out IPAddress? ip)
        ? ip
        : Dns.GetHostAddresses(host)[0];
    return new IPEndPoint(address, port);
}
