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
 * copies of the Software, and to permit persons to whom the Software
 * is furnished to do so, subject to the following conditions:
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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Network;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Samples.Network;
using Opc.Ua.Server.Network;

int port = args.Length == 0 ? 62588 : int.Parse(args[0], CultureInfo.InvariantCulture);
if (port is < 1 or > 65535 || args.Length > 1)
{
    throw new ArgumentException("Usage: NetworkModelSample [port], where port is 1 through 65535.");
}
string endpoint = $"opc.tcp://localhost:{port.ToString(CultureInfo.InvariantCulture)}/NetworkModelSample";
string pkiRoot = Path.Combine(Path.GetTempPath(), "OpcUaNetworkModelSample", "pki");
Console.WriteLine("SIMULATED NETWORK: no host interface, switch or TSN reservation is changed.");
Console.WriteLine("Sample-only trust: local application certificates are accepted automatically.");

ReferenceNetworkModelProvider provider = NetworkScenario.Create();
int permitMappingEdits = 0;
HostApplicationBuilder serverBuilder = Host.CreateApplicationBuilder();
serverBuilder.Logging.ClearProviders();
serverBuilder.Logging.EnableOpcUaRedaction();
serverBuilder.Logging.AddConsole().SetMinimumLevel(LogLevel.Warning);
serverBuilder.Services.AddSingleton<INetworkModelProvider>(provider);
serverBuilder.Services.AddOpcUa().AddServer(options =>
{
    options.ApplicationName = "NetworkModelSampleServer";
    options.ApplicationUri = "urn:localhost:OPCFoundation:NetworkModelSampleServer";
    options.ProductUri = "urn:opcfoundation.org:NetworkModelSample";
    options.PkiRoot = Path.Combine(pkiRoot, "server");
    options.AutoAcceptUntrustedCertificates = true;
    options.EndpointUrls.Add(endpoint);
}).AddNetworkModel(
    services => services.GetRequiredService<INetworkModelProvider>(),
    _ => Volatile.Read(ref permitMappingEdits) != 0);

using IHost serverHost = serverBuilder.Build();
await serverHost.StartAsync().ConfigureAwait(false);
try
{
    HostApplicationBuilder clientBuilder = Host.CreateApplicationBuilder();
    clientBuilder.Logging.ClearProviders();
    clientBuilder.Logging.EnableOpcUaRedaction();
    clientBuilder.Logging.AddConsole().SetMinimumLevel(LogLevel.Warning);
    clientBuilder.Services.AddOpcUa().AddClient(options =>
    {
        options.ApplicationName = "NetworkModelSampleClient";
        options.ApplicationUri = "urn:localhost:OPCFoundation:NetworkModelSampleClient";
        options.ProductUri = "urn:opcfoundation.org:NetworkModelSample";
        options.PkiRoot = Path.Combine(pkiRoot, "client");
        options.AutoAcceptUntrustedCertificates = true;
    }).AddDiscoveryAndConnect(options =>
    {
        options.DiscoveryUrl = endpoint;
        options.SecurityMode = MessageSecurityMode.SignAndEncrypt;
        options.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
    }).AddSubscriptions().AddNetworkModelClient();
    using IHost clientHost = clientBuilder.Build();
    await clientHost.StartAsync().ConfigureAwait(false);
    try
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        CancellationToken ct = lifetime.Token;
        Func<CancellationToken, Task<ManagedSession>> connect =
            clientHost.Services.GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>();
        ManagedSession session = await connect(ct).ConfigureAwait(false);
        NetworkModelClient client = clientHost.Services.GetRequiredService<NetworkModelClientFactory>()
            .Create(session);
        LldpLocalSystemTypeClient localSystem = await client.GetLldp(ObjectIds.LLDP)
            .GetLocalSystemDataAsync(client.Telemetry, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The standard LLDP local-system object is missing.");
        Console.WriteLine($"Generated LLDP proxy: {localSystem.ObjectId}");

        ArrayOf<NodeId> interfaces = await client.DiscoverInterfacesAsync(ct).ConfigureAwait(false);
        ArrayOf<NodeId> tables = await client.DiscoverMappingTablesAsync(ct).ConfigureAwait(false);
        if (interfaces.Count != 2 || tables.Count != 1)
        {
            throw new InvalidOperationException("The scenario requires two interfaces and one mapping table.");
        }
        NodeId table = tables[0];
        foreach (NodeId id in interfaces.ToList())
        {
            NetworkInterfaceValues values = await client.ReadInterfaceAsync(id, ct).ConfigureAwait(false);
            ArrayOf<NodeId> mappings = await client.GetEffectiveMappingTablesAsync(id, ct).ConfigureAwait(false);
            if (values.Speed.DataValue.StatusCode != StatusCodes.Good ||
                values.Speed.DataValue.WrappedValue != new Variant(1_000_000_000UL) ||
                mappings.Count != 1 || mappings[0] != table)
            {
                throw new InvalidOperationException("An interface did not expose the expected speed and mapping.");
            }
            Console.WriteLine($"{id}: speed={values.Speed.DataValue}; inherited/assigned tables={mappings.Count}");
        }
        CallMethodResult denied = await client.AddPriorityMappingEntryAsync(
            table, "urn:opcfoundation:network-sample:priority", "Diagnostic", 0, 0, ct).ConfigureAwait(false);
        Require(denied, StatusCodes.BadUserAccessDenied);
        Volatile.Write(ref permitMappingEdits, 1);
        Require(await client.AddPriorityMappingEntryAsync(
            table, "urn:opcfoundation:network-sample:priority", "Diagnostic", 0, 0, ct).ConfigureAwait(false),
            StatusCodes.Good);
        Require(await client.AddPriorityMappingEntryAsync(
            table, "urn:opcfoundation:network-sample:priority", "Invalid", 8, 64, ct).ConfigureAwait(false),
            StatusCodes.BadInvalidArgument);
        Volatile.Write(ref permitMappingEdits, 0);

        ArrayOf<NodeId> talkers = await client.DiscoverStreamsAsync(true, ct).ConfigureAwait(false);
        ArrayOf<NodeId> listeners = await client.DiscoverStreamsAsync(false, ct).ConfigureAwait(false);
        if (talkers.Count != 1 || listeners.Count != 1)
        {
            throw new InvalidOperationException("The scenario requires exactly one talker and one listener.");
        }
        NetworkStreamValues stream = await client.ReadStreamAsync(talkers[0], ct).ConfigureAwait(false);
        IAsyncEnumerator<DataValueChange> changes = client.ObserveAsync(
            stream.State.NodeId, session.DefaultStreaming, cancellationToken: ct).GetAsyncEnumerator(ct);
        await using var changesLifetime = changes.ConfigureAwait(false);
        if (!await changes.MoveNextAsync().ConfigureAwait(false))
        {
            throw new InvalidOperationException("The state subscription ended before its initial observation.");
        }
        for (int step = 0; step < 4; step++)
        {
            NetworkModelSnapshot snapshot = NetworkScenario.Advance(
                await provider.ReadAsync(ct).ConfigureAwait(false), step);
            provider.Publish(snapshot);
            bool observed = false;
            while (!observed && await changes.MoveNextAsync().ConfigureAwait(false))
            {
                DataValueChange change = changes.Current;
                Console.WriteLine($"Stream change: {change}");
                observed = change.Value.StatusCode == StatusCodes.Good &&
                    change.Value.WrappedValue == new Variant((int)snapshot.Streams[0].State) &&
                    change.Value.SourceTimestamp == (DateTimeUtc)snapshot.Timestamp;
            }
            if (!observed)
            {
                throw new InvalidOperationException("The subscription ended before the simulated transition.");
            }
            NetworkStreamValues listener = await client.ReadStreamAsync(listeners[0], ct).ConfigureAwait(false);
            while (listener.State.DataValue.SourceTimestamp != (DateTimeUtc)snapshot.Timestamp)
            {
                await Task.Delay(20, ct).ConfigureAwait(false);
                listener = await client.ReadStreamAsync(listeners[0], ct).ConfigureAwait(false);
            }
            if (listener.State.DataValue.StatusCode != StatusCodes.Good ||
                listener.State.DataValue.WrappedValue != Variant.From(snapshot.Streams[1].State))
            {
                throw new InvalidOperationException("The listener did not publish the simulated lifecycle state.");
            }
            Console.WriteLine($"Listener state: {listener.State.DataValue}");
            ArrayOf<NodeId> ports = await client.DiscoverLldpPortsAsync(ct).ConfigureAwait(false);
            if (ports.Count != 1)
            {
                throw new InvalidOperationException("The scenario requires exactly one LLDP port.");
            }
            ArrayOf<NodeId> neighbors = await client.DiscoverNeighborsAsync(ports[0], ct).ConfigureAwait(false);
            int expectedNeighbors = step is 1 or 3 ? 1 : 0;
            while (neighbors.Count != expectedNeighbors)
            {
                await Task.Delay(20, ct).ConfigureAwait(false);
                neighbors = await client.DiscoverNeighborsAsync(ports[0], ct).ConfigureAwait(false);
            }
            Console.WriteLine($"Simulated LLDP neighbors: {neighbors.Count}");
        }
    }
    finally
    {
        await clientHost.StopAsync().ConfigureAwait(false);
    }
}
finally
{
    await serverHost.StopAsync().ConfigureAwait(false);
}

static void Require(CallMethodResult result, StatusCode expected)
{
    Console.WriteLine($"Mapping operation: {result.StatusCode}");
    if (result.StatusCode != expected)
    {
        throw new ServiceResultException(result.StatusCode,
            $"Expected mapping result {expected}.");
    }
}
