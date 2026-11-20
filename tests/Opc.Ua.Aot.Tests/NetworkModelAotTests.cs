// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Client.Network;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.Network;
using TUnit.Core.Interfaces;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// NativeAOT reachability tests for Part 22 provider registration and typed proxies.
    /// </summary>
    [ClassDataSource<AotTestFixture>(Shared = SharedType.PerTestSession)]
    public sealed class NetworkModelAotTests(AotTestFixture fixture)
    {
        /// <summary>
        /// Exercises provider registration and generated Part 22 client proxies.
        /// </summary>
        [Test]
        public async Task NetworkModelProvidersRegistrationsAndProxiesAreReachable()
        {
            ITelemetryContext telemetry = fixture.Telemetry;
            ReferenceNetworkModelProvider networkProvider = new NetworkModelBuilder()
                .AddInterface(new NetworkInterfaceSnapshot("physical", "eth0")
                {
                    AdminStatus = InterfaceAdminStatus.Up,
                    OperStatus = InterfaceOperStatus.Up,
                    Speed = 1_000_000_000
                })
                .Build(DateTime.UtcNow, StatusCodes.Good);

            var serverServices = new ServiceCollection();
            serverServices.AddOpcUa()
                .AddServer(options => options.ApplicationName = "NetworkModelAotServer")
                .AddNetworkModel(networkProvider);
            using ServiceProvider serverProvider = serverServices.BuildServiceProvider();
            OpcUaServerNodeManagerRegistration registration = serverProvider
                .GetServices<OpcUaServerNodeManagerRegistration>()
                .Single();
            NetworkModelNodeManagerFactory registeredFactory = registration.AsyncFactory
                as NetworkModelNodeManagerFactory
                ?? throw new InvalidOperationException("The network model registration has no async factory.");
            await Assert.That(registeredFactory.NamespacesUris[0])
                .IsEqualTo(NetworkModelNodeManager.NamespaceUri);

            var clientServices = new ServiceCollection();
            clientServices.AddSingleton(telemetry);
            clientServices.AddOpcUa().AddNetworkModelClient();
            using ServiceProvider clientProvider = clientServices.BuildServiceProvider();
            NetworkModelClientFactory clientFactory =
                clientProvider.GetRequiredService<NetworkModelClientFactory>();
            NetworkModelClient client = clientFactory.Create(fixture.Session);
            NodeId interfaceId = new("physical", 2);
            await Assert.That(client.GetInterface(interfaceId).ObjectId).IsEqualTo(interfaceId);
            await Assert.That(client.GetMappingTable(new NodeId("mapping", 2)).ObjectId)
                .IsEqualTo(new NodeId("mapping", 2));
            await Assert.That(client.GetLldp(ObjectIds.LLDP).ObjectId).IsEqualTo(ObjectIds.LLDP);
        }
    }
}
