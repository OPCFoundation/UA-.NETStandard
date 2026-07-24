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
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;
using Pumps;

namespace Opc.Ua.Di.Tests
{
    [TestFixture]
    [Category("Pumps")]
    [Category("Hosting")]
    [NonParallelizable]
    public sealed class PumpHostedReferenceTests
    {
        [Test]
        public async Task MasterBrowseOrganizesBothPumpsAsync()
        {
            CaptureServer.Reset();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa()
                .AddServer<CaptureServer>(ConfigureServer)
                .AddNodeManager<PumpNodeManagerFactory>()
                .ConfigureDevicesFor<PumpNodeManager>(async context =>
                {
                    var manager = (PumpNodeManager)context.Manager;
                    ushort pumpsNamespaceIndex = (ushort)manager.Server.NamespaceUris.GetIndex(
                        Opc.Ua.Pumps.Namespaces.Pumps);
                    await manager.CreatePumpAsync(
                        new QualifiedName("Pump #2", pumpsNamespaceIndex),
                        context.CancellationToken).ConfigureAwait(false);
                });

            await using ServiceProvider provider = services.BuildServiceProvider();
            IHostedService hostedService = provider.GetServices<IHostedService>().Single();
            await hostedService.StartAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                Assert.That(
                    await WaitForAsync(
                        () => CaptureServer.StartedInstance != null,
                        TimeSpan.FromSeconds(30)).ConfigureAwait(false),
                    Is.True);
                IServerInternal server = CaptureServer.StartedInstance!;
                ushort diNamespaceIndex = (ushort)server.NamespaceUris.GetIndex(
                    Opc.Ua.Di.Namespaces.OpcUaDi);
                var deviceSetId = new NodeId(Opc.Ua.Di.Objects.DeviceSet, diNamespaceIndex);
                ArrayOf<BrowseDescription> nodesToBrowse =
                [
                    new BrowseDescription
                    {
                        NodeId = deviceSetId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ];

                (ArrayOf<BrowseResult> results, _) = await server.NodeManager.BrowseAsync(
                    new OperationContext(
                        new RequestHeader(),
                        null,
                        RequestType.Browse,
                        RequestLifetime.None),
                    new ViewDescription(),
                    0,
                    nodesToBrowse,
                    CancellationToken.None).ConfigureAwait(false);

                var pumpReferences = new List<ReferenceDescription>();
                for (int ii = 0; ii < results[0].References.Count; ii++)
                {
                    ReferenceDescription reference = results[0].References[ii];
                    if (reference.BrowseName.Name is "Pump #1" or "Pump #2")
                    {
                        pumpReferences.Add(reference);
                    }
                }

                Assert.That(pumpReferences, Has.Count.EqualTo(2));
                Assert.That(
                    pumpReferences,
                    Has.All.Matches<ReferenceDescription>(reference =>
                        ExpandedNodeId.ToNodeId(
                            reference.ReferenceTypeId,
                            server.NamespaceUris) == Opc.Ua.Types.ReferenceTypeIds.Organizes));

                using var clientFixture = new ClientFixture(
                    NUnitTelemetryContext.Create());
                string clientPkiRoot = System.IO.Path.Combine(
                    TestContext.CurrentContext.WorkDirectory,
                    nameof(MasterBrowseOrganizesBothPumpsAsync),
                    "client-pki");
                await clientFixture.LoadClientConfigurationAsync(clientPkiRoot)
                    .ConfigureAwait(false);
                using Opc.Ua.Client.ISession session = await clientFixture.ConnectAsync(
                    new Uri(s_endpointUrl),
                    SecurityPolicies.None).ConfigureAwait(false);
                BrowseResponse clientBrowse = await session.BrowseAsync(
                    null,
                    null,
                    0,
                    nodesToBrowse,
                    CancellationToken.None).ConfigureAwait(false);
                var clientPumpReferences = new List<ReferenceDescription>();
                for (int ii = 0; ii < clientBrowse.Results[0].References.Count; ii++)
                {
                    ReferenceDescription reference =
                        clientBrowse.Results[0].References[ii];
                    if (reference.BrowseName.Name is "Pump #1" or "Pump #2")
                    {
                        clientPumpReferences.Add(reference);
                    }
                }
                Assert.That(clientPumpReferences, Has.Count.EqualTo(2));
                Assert.That(
                    clientPumpReferences,
                    Has.All.Matches<ReferenceDescription>(reference =>
                        ExpandedNodeId.ToNodeId(
                            reference.ReferenceTypeId,
                            session.NamespaceUris) == Opc.Ua.Types.ReferenceTypeIds.Organizes));
            }
            finally
            {
                await hostedService.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        private static void ConfigureServer(OpcUaServerOptions options)
        {
            string applicationName = nameof(PumpHostedReferenceTests);
            string testRoot = System.IO.Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                applicationName,
                Guid.NewGuid().ToString("N"));
            options.ApplicationName = applicationName;
            options.ApplicationUri = "urn:localhost:" + applicationName;
            options.ProductUri = "urn:localhost:" + applicationName + ":product";
            options.PkiRoot = System.IO.Path.Combine(testRoot, "pki");
            options.AutoAcceptUntrustedCertificates = true;
            options.IncludeUnsecurePolicyNone = true;
            options.EndpointUrls.Clear();
            s_endpointUrl =
                "opc.tcp://localhost:" +
                GetAvailablePort().ToString(CultureInfo.InvariantCulture) +
                "/" +
                applicationName;
            options.EndpointUrls.Add(s_endpointUrl);
        }

        private static int GetAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static async Task<bool> WaitForAsync(
            Func<bool> condition,
            TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return true;
                }
                await Task.Delay(100).ConfigureAwait(false);
            }
            return condition();
        }

        private static string s_endpointUrl = string.Empty;

        public sealed class CaptureServer : DependencyInjectionStandardServer
        {
            public CaptureServer(
                IServiceProvider services,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
                : base(services, telemetry, timeProvider)
            {
            }

            public static IServerInternal? StartedInstance =>
                Volatile.Read(ref s_startedInstance);

            public static void Reset()
            {
                Volatile.Write(ref s_startedInstance, null);
            }

            protected override void OnServerStarted(IServerInternal server)
            {
                Volatile.Write(ref s_startedInstance, server);
                base.OnServerStarted(server);
            }

            private static IServerInternal? s_startedInstance;
        }
    }
}
