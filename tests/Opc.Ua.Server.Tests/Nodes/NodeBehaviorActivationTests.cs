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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.Nodes;

namespace Opc.Ua.Server.Tests.Nodes
{
    [TestFixture]
    [Category("NodeSource")]
    [Parallelizable]
    public sealed class NodeBehaviorActivationTests
    {
        [Test]
        public async Task ActivationIsChildFirstBaseToDerivedAndCleanupIsReverseAsync()
        {
            var recorder = new NodeBehaviorTestRecorder();
            var source = new NodeBehaviorTestSource(recorder);
            var timeProvider = new FakeTimeProvider();
            NodeSourceNodeManager manager = await CreateManagerAsync(
                source,
                serviceProvider: null,
                timeProvider).ConfigureAwait(false);
            try
            {
                await manager
                    .CreateAddressSpaceAsync(
                        new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false);

                Assert.That(
                    recorder.GetEvents(),
                    Is.EqualTo(s_activationEvents));

                NodeBehaviorContext[] contexts = recorder.GetContexts();
                Assert.Multiple(() =>
                {
                    Assert.That(contexts, Has.Length.EqualTo(4));
                    Assert.That(
                        contexts.Select(context => context.Node.NodeId),
                        Is.EqualTo(new[]
                        {
                            source.ChildId,
                            source.ChildId,
                            source.ParentId,
                            source.ParentId
                        }));
                    Assert.That(
                        contexts.All(context =>
                            ReferenceEquals(
                                context.AddressSpace.Find(source.SiblingId),
                                source.Sibling)),
                        Is.True);
                    Assert.That(
                        contexts.All(context =>
                            ReferenceEquals(
                                context.AddressSpace.Find(
                                    new ExpandedNodeId(
                                        "Sibling",
                                        NodeBehaviorTestSource.NamespaceUri)),
                                source.Sibling)),
                        Is.True);
                    Assert.That(
                        contexts.All(context => ReferenceEquals(context.Source, source)),
                        Is.True);
                    Assert.That(
                        contexts.All(context => context.Services is null),
                        Is.True);
                    Assert.That(
                        contexts.All(context =>
                            ReferenceEquals(context.TimeProvider, timeProvider)),
                        Is.True);
                    Assert.That(
                        contexts
                            .Select(context => context.Generation.SourceId)
                            .Distinct()
                            .Count(),
                        Is.EqualTo(1));
                    Assert.That(
                        contexts.All(context => context.Generation.Generation == 1),
                        Is.True);
                });

                using var canceled = new CancellationTokenSource();
                canceled.Cancel();
                await manager
                    .DeleteAddressSpaceAsync(canceled.Token)
                    .ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(
                        recorder.GetEvents(),
                        Is.EqualTo(s_cleanupEvents));
                    Assert.That(
                        recorder.GetLeases().All(lease =>
                            !lease.DeactivationTokenCanBeCanceled),
                        Is.True);
                    Assert.That(manager.Find(source.ParentId), Is.Null);
                });

                string[] eventsAfterCleanup = recorder.GetEvents();
                await manager.DeleteAddressSpaceAsync().ConfigureAwait(false);
                Assert.That(recorder.GetEvents(), Is.EqualTo(eventsAfterCleanup));
            }
            finally
            {
                await manager.DeleteAddressSpaceAsync().ConfigureAwait(false);
                ((IDisposable)manager).Dispose();
            }
        }

        [Test]
        public async Task CleanupProcessesEveryLeaseAndSurfacesFailuresInOrderAsync()
        {
            var derivedDeactivation = new InvalidOperationException(
                "derived deactivation failed");
            var derivedDisposal = new InvalidOperationException(
                "derived disposal failed");
            var baseDisposal = new InvalidOperationException(
                "base disposal failed");
            var recorder = new NodeBehaviorTestRecorder();
            var source = new NodeBehaviorTestSource(
                recorder,
                includeChild: false,
                includeSibling: false,
                leaseOptions: (node, factory) => factory switch
                {
                    "derived" => new NodeBehaviorTestLeaseOptions
                    {
                        DeactivationException = derivedDeactivation,
                        DisposalException = derivedDisposal
                    },
                    "base" => new NodeBehaviorTestLeaseOptions
                    {
                        DisposalException = baseDisposal
                    },
                    _ => null
                });
            NodeSourceNodeManager manager = await CreateManagerAsync(source)
                .ConfigureAwait(false);
            try
            {
                await manager
                    .CreateAddressSpaceAsync(
                        new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false);

                AggregateException exception = Assert.ThrowsAsync<AggregateException>(
                    async () => await manager
                        .DeleteAddressSpaceAsync()
                        .ConfigureAwait(false));

                Assert.Multiple(() =>
                {
                    Assert.That(
                        exception.InnerExceptions,
                        Is.EqualTo(new[]
                        {
                            derivedDeactivation,
                            derivedDisposal,
                            baseDisposal
                        }));
                    Assert.That(
                        recorder.GetEvents(),
                        Is.EqualTo(s_cleanupFailureEvents));
                    Assert.That(
                        recorder.GetLeases().All(lease =>
                            lease.ActivateCount == 1 &&
                            lease.DeactivateCount == 1 &&
                            lease.DisposeCount == 1),
                        Is.True);
                    Assert.That(manager.Find(source.ParentId), Is.Null);
                });

                string[] eventsAfterCleanup = recorder.GetEvents();
                await manager.DeleteAddressSpaceAsync().ConfigureAwait(false);
                Assert.That(recorder.GetEvents(), Is.EqualTo(eventsAfterCleanup));
            }
            finally
            {
                await manager.DeleteAddressSpaceAsync().ConfigureAwait(false);
                ((IDisposable)manager).Dispose();
            }
        }

        [Test]
        public async Task HostedSourceReceivesServiceProviderAndDirectSourceDoesNotAsync()
        {
            var recorder = new NodeBehaviorTestRecorder();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(recorder);
            services.AddOpcUa()
                .AddServer(options =>
                {
                    options.ApplicationName = "NodeBehaviorDi";
                    options.ApplicationUri = "urn:localhost:NodeBehaviorDi";
                    options.ProductUri = "urn:opcfoundation.org:NodeBehaviorDi";
                })
                .AddNodeSource<DiBehaviorSource>();
            services.AddSingleton(new DiBehaviorSource(recorder));

            using ServiceProvider provider = services.BuildServiceProvider();
            OpcUaServerNodeManagerRegistration registration = provider
                .GetServices<OpcUaServerNodeManagerRegistration>()
                .Single();
            IAsyncNodeManager hostedManager = await registration.AsyncFactory!
                .CreateAsync(BuildMockServer().Object, new ApplicationConfiguration())
                .ConfigureAwait(false);
            try
            {
                await hostedManager
                    .CreateAddressSpaceAsync(
                        new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false);
                Assert.That(
                    recorder.GetContexts().All(context =>
                        context.Services is not null &&
                        ReferenceEquals(
                            context.Services.GetService<NodeBehaviorTestRecorder>(),
                            recorder)),
                    Is.True);
            }
            finally
            {
                await hostedManager.DeleteAddressSpaceAsync().ConfigureAwait(false);
                ((IDisposable)hostedManager).Dispose();
            }

            var directRecorder = new NodeBehaviorTestRecorder();
            var directSource = new NodeBehaviorTestSource(
                directRecorder,
                includeChild: false,
                includeSibling: false);
            NodeSourceNodeManager directManager = await CreateManagerAsync(directSource)
                .ConfigureAwait(false);
            try
            {
                await directManager
                    .CreateAddressSpaceAsync(
                        new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false);
                Assert.That(
                    directRecorder.GetContexts().All(context =>
                        context.Services is null),
                    Is.True);
            }
            finally
            {
                await directManager.DeleteAddressSpaceAsync().ConfigureAwait(false);
                ((IDisposable)directManager).Dispose();
            }
        }

        [Test]
        public async Task HostedReloadPreservesServiceProviderAsync()
        {
            var recorder = new NodeBehaviorTestRecorder();
            var services = new ServiceCollection();
            services.AddSingleton(recorder);
            using ServiceProvider provider = services.BuildServiceProvider();
            var initial = new DiBehaviorSource(recorder);
            var initialFactory = new NodeSourceNodeManagerFactory(initial, provider);
            Mock<IServerInternal> server = BuildMockServer();
            var initialManager = (NodeSourceNodeManager)await initialFactory
                .CreateAsync(server.Object, new ApplicationConfiguration())
                .ConfigureAwait(false);
            try
            {
                await initialManager
                    .CreateAddressSpaceAsync(
                        new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false);

                var registration = new NodeManagerRegistration(
                    Guid.NewGuid(),
                    1,
                    initialManager);
                var replacement = new DiBehaviorSource(recorder);
                NodeSourceNodeManagerFactory replacementFactory =
                    NodeSourceNodeManagerFactory.CreateReplacement(
                        replacement,
                        registration);
                var replacementManager = (NodeSourceNodeManager)await replacementFactory
                    .CreateAsync(server.Object, new ApplicationConfiguration())
                    .ConfigureAwait(false);
                try
                {
                    await replacementManager
                        .CreateAddressSpaceAsync(
                            new Dictionary<NodeId, IList<IReference>>())
                        .ConfigureAwait(false);

                    NodeBehaviorContext[] replacementContexts = recorder
                        .GetContexts()
                        .Where(context => ReferenceEquals(context.Source, replacement))
                        .ToArray();
                    Assert.Multiple(() =>
                    {
                        Assert.That(replacementContexts, Is.Not.Empty);
                        Assert.That(
                            replacementContexts.All(context =>
                                ReferenceEquals(context.Services, provider)),
                            Is.True);
                        Assert.That(
                            replacementContexts.All(context =>
                                context.Generation.Generation == 2),
                            Is.True);
                    });
                }
                finally
                {
                    await replacementManager
                        .DeleteAddressSpaceAsync()
                        .ConfigureAwait(false);
                    replacementManager.Dispose();
                }
            }
            finally
            {
                await initialManager
                    .DeleteAddressSpaceAsync()
                    .ConfigureAwait(false);
                initialManager.Dispose();
            }
        }

        [Test]
        public void BehaviorModuleAddsNoPublicTypes()
        {
            Type[] behaviorTypes =
            [
                typeof(INodeBehaviorFactoryProvider),
                typeof(INodeBehaviorFactory),
                typeof(INodeBehaviorLease),
                typeof(NodeBehaviorAddressSpace),
                typeof(NodeBehaviorContext),
                typeof(NodeBehaviorGenerationIdentity),
                typeof(NodeBehaviorRegistry),
                typeof(NodeBehaviorActivation)
            ];

            Assert.Multiple(() =>
            {
                Assert.That(
                    behaviorTypes.All(type => !type.IsPublic && !type.IsNestedPublic),
                    Is.True);
                Assert.That(
                    typeof(INodeSource).Assembly
                        .GetExportedTypes()
                        .Any(type => type.Name.Contains(
                            "NodeBehavior",
                            StringComparison.Ordinal)),
                    Is.False);
            });
        }

        private static async ValueTask<NodeSourceNodeManager> CreateManagerAsync(
            INodeSource source,
            IServiceProvider serviceProvider = null,
            TimeProvider timeProvider = null)
        {
            Mock<IServerInternal> server = BuildMockServer(timeProvider);
            var factory = serviceProvider is null
                ? new NodeSourceNodeManagerFactory(source)
                : new NodeSourceNodeManagerFactory(source, serviceProvider);
            return (NodeSourceNodeManager)await factory
                .CreateAsync(server.Object, new ApplicationConfiguration())
                .ConfigureAwait(false);
        }

        private static Mock<IServerInternal> BuildMockServer(
            TimeProvider timeProvider = null)
        {
            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(NodeBehaviorTestSource.NamespaceUri);
            var typeTree = new TypeTable(namespaceTable);
            typeTree.AddSubtype(ObjectTypeIds.BaseObjectType, NodeId.Null);

            var telemetry = new Mock<ITelemetryContext>();
            telemetry
                .SetupGet(context => context.LoggerFactory)
                .Returns(NullLoggerFactory.Instance);

            var server = new Mock<IServerInternal>();
            if (timeProvider is not null)
            {
                server.As<ITimeProviderProvider>()
                    .SetupGet(value => value.TimeProvider)
                    .Returns(timeProvider);
            }
            var masterNodeManager = new Mock<IMasterNodeManager>();
            masterNodeManager
                .SetupGet(value => value.AsyncNodeManagers)
                .Returns(Array.Empty<IAsyncNodeManager>());
            server.SetupGet(value => value.Telemetry).Returns(telemetry.Object);
            server.SetupGet(value => value.NamespaceUris).Returns(namespaceTable);
            server.SetupGet(value => value.TypeTree).Returns(typeTree);
            server
                .SetupGet(value => value.NodeManager)
                .Returns(masterNodeManager.Object);
            server
                .SetupGet(value => value.DefaultSystemContext)
                .Returns(new ServerSystemContext(server.Object));
            return server;
        }

        private sealed class DiBehaviorSource :
            INodeSource,
            INodeBehaviorFactoryProvider
        {
            public DiBehaviorSource(NodeBehaviorTestRecorder recorder)
            {
                m_inner = new NodeBehaviorTestSource(
                    recorder,
                    includeChild: false,
                    includeSibling: false);
            }

            public ArrayOf<string> NamespaceUris => m_inner.NamespaceUris;

            public ValueTask BuildAsync(
                INodeGraphBuilder builder,
                CancellationToken cancellationToken = default)
            {
                return m_inner.BuildAsync(builder, cancellationToken);
            }

            public ArrayOf<INodeBehaviorFactory> GetNodeBehaviorFactories()
            {
                return m_inner.GetNodeBehaviorFactories();
            }

            private readonly NodeBehaviorTestSource m_inner;
        }

        private static readonly string[] s_activationEvents =
        [
            "create:Child:base",
            "create:Child:derived",
            "activate:Child:base",
            "activate:Child:derived",
            "create:Parent:base",
            "create:Parent:derived",
            "activate:Parent:base",
            "activate:Parent:derived"
        ];

        private static readonly string[] s_cleanupEvents =
        [
            "create:Child:base",
            "create:Child:derived",
            "activate:Child:base",
            "activate:Child:derived",
            "create:Parent:base",
            "create:Parent:derived",
            "activate:Parent:base",
            "activate:Parent:derived",
            "deactivate:Parent:derived",
            "deactivate:Parent:base",
            "deactivate:Child:derived",
            "deactivate:Child:base",
            "dispose:Parent:derived",
            "dispose:Parent:base",
            "dispose:Child:derived",
            "dispose:Child:base"
        ];

        private static readonly string[] s_cleanupFailureEvents =
        [
            "create:Parent:base",
            "create:Parent:derived",
            "activate:Parent:base",
            "activate:Parent:derived",
            "deactivate:Parent:derived",
            "deactivate:Parent:base",
            "dispose:Parent:derived",
            "dispose:Parent:base"
        ];
    }
}
