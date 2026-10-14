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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// A request runs on the thread that received it only when every NodeManager
    /// it can reach supports that.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    [Parallelizable(ParallelScope.All)]
    public sealed class InlineExecutionRoutesTests
    {
        private const ushort kLegacyNamespace = 2;
        private const ushort kAsyncNamespace = 3;
        private const ushort kUnroutedNamespace = 5;
        private const string kLegacyNamespaceUri = "http://test.org/UA/InlineExecution/Legacy";

        [Test]
        public void EveryRequestIsAllowedWhenEveryNodeManagerSupportsInlineExecution()
        {
            NodeManagerRoutingTable table = CreateTable(
                (Manager(true), [0]),
                (Manager(true), [kAsyncNamespace]));

            Assert.That(table.InlineRoutes.All, Is.True);
            Assert.That(table.InlineRoutes.Allows(new CallRequest()), Is.True);
            Assert.That(table.InlineRoutes.Allows(Read(kUnroutedNamespace)), Is.True);
        }

        [Test]
        public void ReadAndWriteAreAllowedForTheNamespacesOfSupportingNodeManagers()
        {
            NodeManagerRoutingTable table = CreateTable(
                (Manager(true), [0]),
                (Manager(false), [kLegacyNamespace]),
                (Manager(true), [kAsyncNamespace]));
            InlineExecutionRoutes routes = table.InlineRoutes;

            Assert.That(routes.All, Is.False);
            Assert.That(routes.Allows(Read(kAsyncNamespace)), Is.True);
            Assert.That(routes.Allows(Read(0, kAsyncNamespace)), Is.True);
            Assert.That(routes.Allows(Write(kAsyncNamespace)), Is.True);
            Assert.That(routes.Allows(Read(kAsyncNamespace, kLegacyNamespace)), Is.False);
            Assert.That(routes.Allows(Write(kLegacyNamespace)), Is.False);
            Assert.That(
                routes.Allows(Read(kUnroutedNamespace)),
                Is.False,
                "a namespace without a route reaches every NodeManager");
        }

        [Test]
        public void ANamespaceSharedWithANodeManagerThatDoesNotSupportItIsNotAllowed()
        {
            NodeManagerRoutingTable table = CreateTable(
                (Manager(true), [kAsyncNamespace]),
                (Manager(false), [kAsyncNamespace]));

            Assert.That(table.InlineRoutes.Allows(Read(kAsyncNamespace)), Is.False);
        }

        [Test]
        public void ServicesThatReachEveryNodeManagerFollowTheLeastCapableOne()
        {
            InlineExecutionRoutes routes = CreateTable(
                (Manager(true), [kAsyncNamespace]),
                (Manager(false), [kLegacyNamespace])).InlineRoutes;

            Assert.That(routes.Allows(new CallRequest()), Is.False);
            Assert.That(routes.Allows(new BrowseRequest()), Is.False);
            Assert.That(routes.Allows(new TranslateBrowsePathsToNodeIdsRequest()), Is.False);
            Assert.That(routes.Allows(new CreateMonitoredItemsRequest()), Is.False);
            Assert.That(routes.Allows(new HistoryReadRequest()), Is.False);
            Assert.That(routes.Allows(new DeleteSubscriptionsRequest()), Is.False);

            Assert.That(routes.Allows(new CreateSubscriptionRequest()), Is.True);
            Assert.That(routes.Allows(new SetPublishingModeRequest()), Is.True);
            Assert.That(routes.Allows(new RepublishRequest()), Is.True);
            Assert.That(routes.Allows(new GetEndpointsRequest()), Is.True);
        }

        [Test]
        public void AHiddenNodeManagerCountsOnceItIsVisible()
        {
            NodeManagerRoutingTable table = CreateTable(
                (Manager(true), [0]),
                (Manager(true), [kAsyncNamespace]));
            IAsyncNodeManager legacy = Manager(false);

            table.Add(legacy, [kLegacyNamespace], visible: false);
            Assert.That(table.InlineRoutes.All, Is.True);

            table.SetVisible(legacy, true);
            Assert.That(table.InlineRoutes.All, Is.False);
            Assert.That(table.InlineRoutes.Allows(Read(kAsyncNamespace)), Is.True);
            Assert.That(table.InlineRoutes.Allows(Read(kLegacyNamespace)), Is.False);

            table.Remove(legacy);
            Assert.That(table.InlineRoutes.All, Is.True);
        }

        [Test]
        public void TheAdapterDeclaresWhatTheWrappedNodeManagerDeclares()
        {
            var plain = new Mock<INodeManager>();
            var supporting = new Mock<INodeManager>();
            supporting.As<INodeManagerInlineExecution>()
                .Setup(manager => manager.SupportsInlineExecution)
                .Returns(true);

            Assert.That(new AsyncNodeManagerAdapter(plain.Object).SupportsInlineExecution, Is.False);
            Assert.That(new AsyncNodeManagerAdapter(supporting.Object).SupportsInlineExecution, Is.True);
        }

        /// <summary>
        /// A <see cref="CustomNodeManager2"/> does not support inline execution unless
        /// it says so, so with the default configuration the server runs a Read of
        /// its nodes, and every request that reaches all NodeManagers, on a worker.
        /// </summary>
        [Test]
        [CancelAfter(120000)]
        public async Task TheServerRunsRequestsForALegacyNodeManagerOnAWorkerAsync(CancellationToken ct)
        {
            await WithServerAsync(
                legacySupportsInlineExecution: false,
                InlineRequestExecutionMode.Auto,
                server =>
                {
                    ushort legacy = server.LegacyNamespaceIndex;
                    Assert.That(server.CanRunInline(Read(0)), Is.True);
                    Assert.That(server.CanRunInline(Read(legacy)), Is.False);
                    Assert.That(server.CanRunInline(Read(0, legacy)), Is.False);
                    Assert.That(server.CanRunInline(new CallRequest()), Is.False);
                    Assert.That(server.CanRunInline(new CreateSubscriptionRequest()), Is.True);
                },
                ct).ConfigureAwait(false);
        }

        [Test]
        [CancelAfter(120000)]
        public async Task ALegacyNodeManagerCanDeclareThatItSupportsInlineExecutionAsync(CancellationToken ct)
        {
            await WithServerAsync(
                legacySupportsInlineExecution: true,
                InlineRequestExecutionMode.Auto,
                server =>
                {
                    Assert.That(server.CanRunInline(Read(server.LegacyNamespaceIndex)), Is.True);
                    Assert.That(server.CanRunInline(new CallRequest()), Is.True);
                },
                ct).ConfigureAwait(false);
        }

        [Test]
        [CancelAfter(120000)]
        public async Task EnabledRunsRequestsInlineWhateverTheNodeManagersDeclareAsync(CancellationToken ct)
        {
            await WithServerAsync(
                legacySupportsInlineExecution: false,
                InlineRequestExecutionMode.Enabled,
                server =>
                {
                    Assert.That(server.CanRunInline(Read(server.LegacyNamespaceIndex)), Is.True);
                    Assert.That(server.CanRunInline(new CallRequest()), Is.True);
                },
                ct).ConfigureAwait(false);
        }

        private static async Task WithServerAsync(
            bool legacySupportsInlineExecution,
            InlineRequestExecutionMode mode,
            System.Action<InlineTestServer> verify,
            CancellationToken ct)
        {
            var fixture = new ServerFixture<InlineTestServer>(
                telemetry => new InlineTestServer(telemetry, legacySupportsInlineExecution));
            try
            {
                await fixture.LoadConfigurationAsync().ConfigureAwait(false);
                fixture.Config.ServerConfiguration!.InlineRequestExecution = mode;
                InlineTestServer server = await fixture.StartAsync().ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                verify(server);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        private static IAsyncNodeManager Manager(bool supportsInlineExecution)
        {
            var manager = new Mock<IAsyncNodeManager>();
            if (supportsInlineExecution)
            {
                manager.As<INodeManagerInlineExecution>()
                    .Setup(inline => inline.SupportsInlineExecution)
                    .Returns(true);
            }
            return manager.Object;
        }

        private static NodeManagerRoutingTable CreateTable(
            params (IAsyncNodeManager Manager, int[] Namespaces)[] managers)
        {
            var table = new NodeManagerRoutingTable();
            var routes = new Dictionary<int, List<IAsyncNodeManager>>();
            foreach ((IAsyncNodeManager manager, int[] namespaces) in managers)
            {
                table.AddInitial(manager);
                foreach (int index in namespaces)
                {
                    if (!routes.TryGetValue(index, out List<IAsyncNodeManager>? route))
                    {
                        routes[index] = route = [];
                    }
                    route.Add(manager);
                }
            }
            table.Initialize(routes);
            return table;
        }

        private static ReadRequest Read(params ushort[] namespaces)
        {
            var nodes = new List<ReadValueId>();
            foreach (ushort index in namespaces)
            {
                nodes.Add(new ReadValueId
                {
                    NodeId = new NodeId(1000u, index),
                    AttributeId = Attributes.Value
                });
            }
            return new ReadRequest { NodesToRead = nodes.ToArrayOf() };
        }

        private static WriteRequest Write(ushort index)
        {
            return new WriteRequest
            {
                NodesToWrite =
                [
                    new WriteValue { NodeId = new NodeId(1000u, index), AttributeId = Attributes.Value }
                ]
            };
        }

        /// <summary>
        /// A reference server with one more NodeManager, derived from
        /// <see cref="CustomNodeManager2"/>.
        /// </summary>
        public sealed class InlineTestServer : ReferenceServer
        {
            public InlineTestServer(ITelemetryContext telemetry, bool legacySupportsInlineExecution)
                : base(telemetry)
            {
                AddNodeManager(new LegacyNodeManagerFactory(legacySupportsInlineExecution));
            }

            public ushort LegacyNamespaceIndex =>
                (ushort)CurrentInstance.NamespaceUris.GetIndex(kLegacyNamespaceUri);

            public bool CanRunInline(IServiceRequest request)
            {
                return CanRunRequestInline(new IncomingRequest(request));
            }
        }

        private sealed class LegacyNodeManagerFactory : INodeManagerFactory
        {
            public LegacyNodeManagerFactory(bool supportsInlineExecution)
            {
                m_supportsInlineExecution = supportsInlineExecution;
            }

            public ArrayOf<string> NamespacesUris => [kLegacyNamespaceUri];

            public INodeManager Create(IServerInternal server, ApplicationConfiguration configuration)
            {
                return new LegacyNodeManager(server, configuration, m_supportsInlineExecution);
            }

            private readonly bool m_supportsInlineExecution;
        }

        private sealed class LegacyNodeManager : CustomNodeManager2
        {
            public LegacyNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                bool supportsInlineExecution)
                : base(server, configuration, kLegacyNamespaceUri)
            {
                m_supportsInlineExecution = supportsInlineExecution;
            }

            public override bool SupportsInlineExecution => m_supportsInlineExecution;

            private readonly bool m_supportsInlineExecution;
        }

        private sealed class IncomingRequest : IEndpointIncomingRequest
        {
            public IncomingRequest(IServiceRequest request)
            {
                Request = request;
            }

            public IServiceRequest Request { get; }

            public SecureChannelContext SecureChannelContext { get; } =
                new("1", new EndpointDescription(), RequestEncoding.Binary);

            public ValueTask CallAsync(CancellationToken cancellationToken = default)
            {
                return default;
            }

            public void OperationCompleted(IServiceResponse? response, ServiceResult error)
            {
            }
        }
    }
}
