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
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.Nodes;

namespace Opc.Ua.Server.Tests.Nodes
{
    [TestFixture]
    [Category("NodeSource")]
    [Parallelizable]
    public sealed class NodeSourceNodeManagerTests
    {
        private const string kNamespaceUri =
            "urn:opcfoundation.org:Tests:NodeSource";

        [Test]
        public async Task AddPreservesConcreteStateAndCallerNodeIdAsync()
        {
            var source = new TypedStateSource();
            IAsyncNodeManager manager = await CreateManagerAsync(source)
                .ConfigureAwait(false);
            try
            {
                var externalReferences =
                    new Dictionary<NodeId, IList<IReference>>();
                await manager
                    .CreateAddressSpaceAsync(externalReferences)
                    .ConfigureAwait(false);

                var adapter = (NodeSourceNodeManager)manager;
                NodeState registered = adapter.Find(source.State.NodeId);

                Assert.Multiple(() =>
                {
                    Assert.That(source.ReturnedState, Is.SameAs(source.State));
                    Assert.That(registered, Is.SameAs(source.State));
                    Assert.That(registered, Is.TypeOf<TypedObjectState>());
                    Assert.That(source.State.NodeId, Is.EqualTo(source.AssignedNodeId));
                    Assert.That(
                        source.AssignedChild.NodeId,
                        Is.EqualTo(source.AssignedChildNodeId));
                    Assert.That(source.DeepGeneratedChild.NodeId.IsNull, Is.False);
                    Assert.That(source.State.FindChild(
                        adapter.SystemContext,
                        source.AssignedChild.BrowseName), Is.SameAs(source.AssignedChild));
                });
            }
            finally
            {
                ((IDisposable)manager).Dispose();
            }
        }

        [Test]
        public void SourceRejectsTheOpcUaBaseNamespace()
        {
            Assert.That(
                () => new NodeSourceNodeManagerFactory(new BaseNamespaceSource()),
                Throws.InstanceOf<InvalidOperationException>());
        }

        [TestCase(InvalidGraphKind.UnknownOwnedParent)]
        [TestCase(InvalidGraphKind.NamespaceZeroNodeId)]
        [TestCase(InvalidGraphKind.ForeignNamespaceNodeId)]
        public async Task InvalidAuthoredGraphFailsBeforeRegistrationAsync(
            InvalidGraphKind kind)
        {
            IAsyncNodeManager manager = await CreateManagerAsync(
                new InvalidGraphSource(kind)).ConfigureAwait(false);
            try
            {
                ServiceResultException exception =
                    Assert.ThrowsAsync<ServiceResultException>(
                        async () => await manager
                            .CreateAddressSpaceAsync(
                                new Dictionary<NodeId, IList<IReference>>())
                            .ConfigureAwait(false));

                Assert.That(
                    exception.StatusCode,
                    kind == InvalidGraphKind.UnknownOwnedParent
                        ? Is.EqualTo((uint)StatusCodes.BadNodeIdUnknown)
                        : Is.EqualTo((uint)StatusCodes.BadNodeIdInvalid));
            }
            finally
            {
                ((IDisposable)manager).Dispose();
            }
        }

        [Test]
        public async Task NodeIdsAreFinalBeforeReturnedBuilderIsConfiguredAsync()
        {
            var source = new NodeIdCaptureSource();
            IAsyncNodeManager manager = await CreateManagerAsync(source)
                .ConfigureAwait(false);
            try
            {
                await manager
                    .CreateAddressSpaceAsync(
                        new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(source.NodeIdAtConfiguration.IsNull, Is.False);
                    Assert.That(source.NodeIdAtNodeAdded, Is.EqualTo(source.NodeIdAtConfiguration));
                    Assert.That(source.NodeAddedCount, Is.EqualTo(1));
                    Assert.That(
                        ((NodeSourceNodeManager)manager).Find(source.NodeIdAtConfiguration),
                        Is.Not.Null);
                });
            }
            finally
            {
                ((IDisposable)manager).Dispose();
            }
        }

        [Test]
        public async Task BuildRunsOnceAndSealedBuilderRejectsLateAuthoringAsync()
        {
            var source = new RetainedBuilderSource();
            IAsyncNodeManager manager = await CreateManagerAsync(source)
                .ConfigureAwait(false);
            try
            {
                await manager
                    .CreateAddressSpaceAsync(
                        new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false);

                ServiceResultException lateAuthoring = Assert.Throws<ServiceResultException>(
                    () => source.Builder!.AddFolder(new QualifiedName("Late")));
                Assert.That(
                    lateAuthoring.StatusCode,
                    Is.EqualTo((uint)StatusCodes.BadInvalidState));

                ServiceResultException secondBuild = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await manager
                        .CreateAddressSpaceAsync(
                            new Dictionary<NodeId, IList<IReference>>())
                        .ConfigureAwait(false));
                Assert.Multiple(() =>
                {
                    Assert.That(
                        secondBuild.StatusCode,
                        Is.EqualTo((uint)StatusCodes.BadInvalidState));
                    Assert.That(source.BuildCount, Is.EqualTo(1));
                });
            }
            finally
            {
                ((IDisposable)manager).Dispose();
            }
        }

        [Test]
        public void AddNodeSourceUsesDependencyInjectionForSourceConstruction()
        {
            var dependency = new SourceDependency();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(dependency);
            services.AddOpcUa()
                .AddServer(options =>
                {
                    options.ApplicationName = "NodeSourceDi";
                    options.ApplicationUri = "urn:localhost:NodeSourceDi";
                    options.ProductUri = "urn:opcfoundation.org:NodeSourceDi";
                })
                .AddNodeSource<InjectedSource>();

            using ServiceProvider provider = services.BuildServiceProvider();
            InjectedSource source = provider.GetRequiredService<InjectedSource>();
            OpcUaServerNodeManagerRegistration registration = provider
                .GetServices<OpcUaServerNodeManagerRegistration>()
                .Single();

            Assert.Multiple(() =>
            {
                Assert.That(source.Dependency, Is.SameAs(dependency));
                Assert.That(
                    registration.AsyncFactory,
                    Is.TypeOf<NodeSourceNodeManagerFactory>());
                Assert.That(
                    registration.AsyncFactory!.NamespacesUris[0],
                    Is.EqualTo(kNamespaceUri));
            });
        }

        private static ValueTask<IAsyncNodeManager> CreateManagerAsync(
            INodeSource source)
        {
            Mock<IServerInternal> server = BuildMockServer();
            var factory = new NodeSourceNodeManagerFactory(source);
            return factory.CreateAsync(server.Object, new ApplicationConfiguration());
        }

        private static Mock<IServerInternal> BuildMockServer()
        {
            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(kNamespaceUri);

            var telemetry = new Mock<ITelemetryContext>();
            telemetry
                .SetupGet(context => context.LoggerFactory)
                .Returns(NullLoggerFactory.Instance);

            var server = new Mock<IServerInternal>();
            server.SetupGet(value => value.Telemetry).Returns(telemetry.Object);
            server.SetupGet(value => value.NamespaceUris).Returns(namespaceTable);
            server
                .SetupGet(value => value.DefaultSystemContext)
                .Returns(new ServerSystemContext(server.Object));
            return server;
        }

        private sealed class TypedStateSource : INodeSource
        {
            public TypedStateSource()
            {
                AssignedNodeId = new NodeId("CallerAssigned", 1);
                State = new TypedObjectState(null)
                {
                    NodeId = AssignedNodeId,
                    BrowseName = new QualifiedName("Typed", 1),
                    DisplayName = new LocalizedText("Typed")
                };
                AssignedChildNodeId = new NodeId("CallerAssignedChild", 1);
                AssignedChild = new BaseDataVariableState(State)
                {
                    NodeId = AssignedChildNodeId,
                    BrowseName = new QualifiedName("AssignedChild", 1),
                    DataType = DataTypeIds.Int32
                };
                DeepGeneratedChild = new BaseDataVariableState(AssignedChild)
                {
                    BrowseName = new QualifiedName("DeepGeneratedChild", 1),
                    DataType = DataTypeIds.Int32
                };
                AssignedChild.AddChild(DeepGeneratedChild);
            }

            public ArrayOf<string> NamespaceUris => [kNamespaceUri];

            public NodeId AssignedNodeId { get; }

            public NodeId AssignedChildNodeId { get; }

            public BaseDataVariableState AssignedChild { get; }

            public BaseDataVariableState DeepGeneratedChild { get; }

            public TypedObjectState State { get; }

            public TypedObjectState ReturnedState { get; private set; }

            public ValueTask BuildAsync(
                INodeGraphBuilder builder,
                CancellationToken cancellationToken = default)
            {
                ReturnedState = builder.Add(State).Node;
                builder.Add(AssignedChild);
                return default;
            }
        }

        private sealed class NodeIdCaptureSource : INodeSource
        {
            public ArrayOf<string> NamespaceUris => [kNamespaceUri];

            public NodeId NodeIdAtConfiguration { get; private set; }

            public NodeId NodeIdAtNodeAdded { get; private set; }

            public int NodeAddedCount { get; private set; }

            public ValueTask BuildAsync(
                INodeGraphBuilder builder,
                CancellationToken cancellationToken = default)
            {
                INodeBuilder<FolderState> folder =
                    builder.AddFolder(new QualifiedName("Configured"));
                Configure(folder);
                return default;
            }

            private void Configure(INodeBuilder<FolderState> folder)
            {
                NodeIdAtConfiguration = folder.Node.NodeId;
                folder.OnNodeAdded((_, node) =>
                {
                    NodeAddedCount++;
                    NodeIdAtNodeAdded = node.NodeId;
                });
            }
        }

        private sealed class RetainedBuilderSource : INodeSource
        {
            public ArrayOf<string> NamespaceUris => [kNamespaceUri];

            public INodeGraphBuilder Builder { get; private set; }

            public int BuildCount { get; private set; }

            public ValueTask BuildAsync(
                INodeGraphBuilder builder,
                CancellationToken cancellationToken = default)
            {
                BuildCount++;
                Builder = builder;
                builder.AddFolder(new QualifiedName("Initial"));
                return default;
            }
        }

        public sealed class InjectedSource : INodeSource
        {
            public InjectedSource(SourceDependency dependency)
            {
                Dependency = dependency;
            }

            public ArrayOf<string> NamespaceUris => [kNamespaceUri];

            public SourceDependency Dependency { get; }

            public ValueTask BuildAsync(
                INodeGraphBuilder builder,
                CancellationToken cancellationToken = default)
            {
                return default;
            }
        }

        public sealed class SourceDependency;

        private sealed class BaseNamespaceSource : INodeSource
        {
            public ArrayOf<string> NamespaceUris => [Opc.Ua.Types.Namespaces.OpcUa];

            public ValueTask BuildAsync(
                INodeGraphBuilder builder,
                CancellationToken cancellationToken = default)
            {
                return default;
            }
        }

        private sealed class InvalidGraphSource : INodeSource
        {
            public InvalidGraphSource(InvalidGraphKind kind)
            {
                m_kind = kind;
            }

            public ArrayOf<string> NamespaceUris => [kNamespaceUri];

            public ValueTask BuildAsync(
                INodeGraphBuilder builder,
                CancellationToken cancellationToken = default)
            {
                switch (m_kind)
                {
                    case InvalidGraphKind.UnknownOwnedParent:
                        builder.AddObject(
                            new QualifiedName("Child"),
                            new NodeId("Missing", 1));
                        break;
                    case InvalidGraphKind.NamespaceZeroNodeId:
                        builder.Add(new BaseObjectState(null)
                        {
                            NodeId = new NodeId("NamespaceZero", 0),
                            BrowseName = new QualifiedName("NamespaceZero", 1)
                        });
                        break;
                    case InvalidGraphKind.ForeignNamespaceNodeId:
                        builder.Add(new BaseObjectState(null)
                        {
                            NodeId = new NodeId("Foreign", 99),
                            BrowseName = new QualifiedName("Foreign", 1)
                        });
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Unexpected invalid graph kind '{m_kind}'.");
                }
                return default;
            }

            private readonly InvalidGraphKind m_kind;
        }

        public enum InvalidGraphKind
        {
            UnknownOwnedParent,
            NamespaceZeroNodeId,
            ForeignNamespaceNodeId
        }

        private sealed class TypedObjectState : BaseObjectState
        {
            public TypedObjectState(NodeState parent)
                : base(parent)
            {
            }
        }
    }
}
