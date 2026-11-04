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
 *
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

using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Client.Network;
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

            var nodeManagerFactory = new NetworkModelNodeManagerFactory(
                networkProvider,
                static _ => false);
            await Assert.That(nodeManagerFactory.NamespacesUris[0])
                .IsEqualTo(NetworkModelNodeManager.NamespaceUri);

            var serverServices = new ServiceCollection();
            serverServices.AddOpcUa()
                .AddServer(options => options.ApplicationName = "NetworkModelAotServer")
                .AddNetworkModel(networkProvider);
            using ServiceProvider serverProvider = serverServices.BuildServiceProvider();
            await Assert.That(serverProvider).IsNotNull();

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
