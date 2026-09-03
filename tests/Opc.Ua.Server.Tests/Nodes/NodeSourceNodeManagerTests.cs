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
                    Assert.That(
                        source.DeepGeneratedReferenceTarget,
                        Is.EqualTo(source.DeepGeneratedChild.NodeId));
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

        [Test]
        public void PublicFactoryCreatesClassicHostingAdapter()
        {
            IAsyncNodeManagerFactory factory = NodeSourceFactory.Create(
                new EmptySource());

            Assert.Multiple(() =>
            {
                Assert.That(factory, Is.TypeOf<NodeSourceNodeManagerFactory>());
                Assert.That(factory.NamespacesUris, Has.Count.EqualTo(1));
                Assert.That(factory.NamespacesUris[0], Is.EqualTo(kNamespaceUri));
                Assert.That(
                    () => NodeSourceFactory.Create(null!),
                    Throws.ArgumentNullException);
            });
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
        public async Task CrossNamespaceChildrenIncludeParentIdentityAsync()
        {
            var source = new CrossNamespaceChildrenSource();
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
                    Assert.That(source.FirstChildId, Is.Not.EqualTo(source.SecondChildId));
                    Assert.That(source.FirstChildId.NamespaceIndex, Is.EqualTo((ushort)1));
                    Assert.That(source.SecondChildId.NamespaceIndex, Is.EqualTo((ushort)1));
                    Assert.That(
                        ((NodeSourceNodeManager)manager).Find(source.FirstChildId),
                        Is.Not.Null);
                    Assert.That(
                        ((NodeSourceNodeManager)manager).Find(source.SecondChildId),
                        Is.Not.Null);
                });
            }
            finally
            {
                ((IDisposable)manager).Dispose();
            }
        }

        [Test]
        public async Task SameNamedExternalChildrenIncludeExternalParentIdentityAsync()
        {
            var source = new ExternalParentChildrenSource();
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
                    Assert.That(source.FirstId, Is.Not.EqualTo(source.SecondId));
                    Assert.That(
                        ((NodeSourceNodeManager)manager).Find(source.FirstId),
                        Is.Not.Null);
                    Assert.That(
                        ((NodeSourceNodeManager)manager).Find(source.SecondId),
                        Is.Not.Null);
                });
            }
            finally
            {
                ((IDisposable)manager).Dispose();
            }
        }

        [Test]
        public async Task ChildNodeIdsEncodeParentTypeAndSegmentBoundariesAsync()
        {
            var source = new IdentifierCollisionSource();
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
                    Assert.That(
                        source.NumericParentChildId,
                        Is.Not.EqualTo(source.StringParentChildId));
                    Assert.That(
                        source.DelimitedParentChildId,
                        Is.Not.EqualTo(source.DelimitedBrowseNameChildId));
                    Assert.That(
                        source.NumericParentChildId.IdentifierAsString,
                        Does.StartWith("v1:"));
                    Assert.That(
                        ((NodeSourceNodeManager)manager).Find(
                            source.NumericParentChildId),
                        Is.Not.Null);
                    Assert.That(
                        ((NodeSourceNodeManager)manager).Find(
                            source.StringParentChildId),
                        Is.Not.Null);
                });
            }
            finally
            {
                ((IDisposable)manager).Dispose();
            }
        }

        [Test]
        public async Task ChildNodeIdentifiersDoNotDependOnNamespaceTableOrderAsync()
        {
            var firstSource = new IdentifierCollisionSource();
            var secondSource = new IdentifierCollisionSource();
            IAsyncNodeManager firstManager = await CreateManagerAsync(
                firstSource,
                IdentifierCollisionSource.ExternalNamespaceUri,
                kNamespaceUri)
                .ConfigureAwait(false);
            IAsyncNodeManager secondManager = await CreateManagerAsync(
                secondSource,
                "urn:opcfoundation.org:Tests:Padding",
                IdentifierCollisionSource.ExternalNamespaceUri,
                kNamespaceUri).ConfigureAwait(false);
            try
            {
                await firstManager
                    .CreateAddressSpaceAsync(
                        new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false);
                await secondManager
                    .CreateAddressSpaceAsync(
                        new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(
                        firstSource.ExternalParentChildId.NamespaceIndex,
                        Is.Not.EqualTo(
                            secondSource.ExternalParentChildId.NamespaceIndex));
                    Assert.That(
                        firstSource.ExternalParentChildId.IdentifierAsString,
                        Is.EqualTo(
                            secondSource.ExternalParentChildId.IdentifierAsString));
                });
            }
            finally
            {
                ((IDisposable)firstManager).Dispose();
                ((IDisposable)secondManager).Dispose();
            }
        }

        [Test]
        public void ChildNodeIdsSeparateNamespaceUriAndIdentifierBoundaries()
        {
            var namespaceUris = new NamespaceTable();
            ushort childNamespaceIndex = namespaceUris.GetIndexOrAppend(
                "urn:opcfoundation.org:Tests:Child");
            ushort firstParentNamespaceIndex = namespaceUris.GetIndexOrAppend(
                "urn:test");
            ushort secondParentNamespaceIndex = namespaceUris.GetIndexOrAppend(
                "urn:test;s=a");
            var browseName = new QualifiedName("Value", childNamespaceIndex);

            NodeId first = NodeSourceNodeIdFactory.CreateChildNodeId(
                new NodeId("a;s=b", firstParentNamespaceIndex),
                browseName,
                childNamespaceIndex,
                namespaceUris);
            NodeId second = NodeSourceNodeIdFactory.CreateChildNodeId(
                new NodeId("b", secondParentNamespaceIndex),
                browseName,
                childNamespaceIndex,
                namespaceUris);
            NodeId namespaceZeroBrowseName =
                NodeSourceNodeIdFactory.CreateChildNodeId(
                    new NodeId("Parent", childNamespaceIndex),
                    new QualifiedName("Value"),
                    childNamespaceIndex,
                    namespaceUris);
            NodeId childNamespaceBrowseName =
                NodeSourceNodeIdFactory.CreateChildNodeId(
                    new NodeId("Parent", childNamespaceIndex),
                    browseName,
                    childNamespaceIndex,
                    namespaceUris);

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.Not.EqualTo(second));
                Assert.That(
                    namespaceZeroBrowseName,
                    Is.Not.EqualTo(childNamespaceBrowseName));
            });
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
        public async Task BrowseNameOverloadsUseExplicitNamespaceSemanticsAsync()
        {
            var source = new BrowseNameSource();
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
                    Assert.That(
                        source.DefaultBrowseName,
                        Is.EqualTo(new QualifiedName("Default", source.DefaultNamespaceIndex)));
                    Assert.That(
                        source.ExplicitBrowseName,
                        Is.EqualTo(new QualifiedName("Explicit", source.SecondaryNamespaceIndex)));
                });
            }
            finally
            {
                ((IDisposable)manager).Dispose();
            }
        }

        [Test]
        public async Task QualifiedBrowseNameRequiresNonzeroNamespaceIndexAsync()
        {
            IAsyncNodeManager manager = await CreateManagerAsync(
                new AmbiguousBrowseNameSource()).ConfigureAwait(false);
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
                    Is.EqualTo((uint)StatusCodes.BadBrowseNameInvalid));
            }
            finally
            {
                ((IDisposable)manager).Dispose();
            }
        }

        [Test]
        public async Task GeneratedHelperRequiresNonzeroQualifiedBrowseNameNamespaceAsync()
        {
            IAsyncNodeManager manager = await CreateManagerAsync(
                new AmbiguousGeneratedBrowseNameSource()).ConfigureAwait(false);
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
                    Is.EqualTo((uint)StatusCodes.BadBrowseNameInvalid));
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
                    () => source.Builder!.AddFolder("Late"));
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
            INodeSource source,
            params string[] initialNamespaceUris)
        {
            Mock<IServerInternal> server = BuildMockServer(initialNamespaceUris);
            var factory = new NodeSourceNodeManagerFactory(source);
            return factory.CreateAsync(server.Object, new ApplicationConfiguration());
        }

        private static Mock<IServerInternal> BuildMockServer(
            params string[] initialNamespaceUris)
        {
            var namespaceTable = new NamespaceTable();
            if (initialNamespaceUris.Length == 0)
            {
                namespaceTable.Append(kNamespaceUri);
            }
            else
            {
                for (int i = 0; i < initialNamespaceUris.Length; i++)
                {
                    namespaceTable.Append(initialNamespaceUris[i]);
                }
            }

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
                    NodeId = new NodeId(123u),
                    BrowseName = new QualifiedName("DeepGeneratedChild", 1),
                    DataType = DataTypeIds.Int32
                };
                AssignedChild.AddChild(DeepGeneratedChild);
                AssignedChild.AddReference(
                    ReferenceTypeIds.HasComponent,
                    false,
                    DeepGeneratedChild.NodeId);
            }

            public ArrayOf<string> NamespaceUris => [kNamespaceUri];

            public NodeId AssignedNodeId { get; }

            public NodeId AssignedChildNodeId { get; }

            public BaseDataVariableState AssignedChild { get; }

            public BaseDataVariableState DeepGeneratedChild { get; }

            public TypedObjectState State { get; }

            public TypedObjectState ReturnedState { get; private set; }

            public NodeId DeepGeneratedReferenceTarget
            {
                get
                {
                    var references = new List<IReference>();
                    AssignedChild.GetReferences(null!, references);
                    return (NodeId)references.Single(reference =>
                        reference.ReferenceTypeId == ReferenceTypeIds.HasComponent &&
                        !reference.IsInverse).TargetId;
                }
            }

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
                    builder.AddFolder("Configured");
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
                builder.AddFolder("Initial");
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

        private sealed class EmptySource : INodeSource
        {
            public ArrayOf<string> NamespaceUris => [kNamespaceUri];

            public ValueTask BuildAsync(
                INodeGraphBuilder builder,
                CancellationToken cancellationToken = default)
            {
                return default;
            }
        }

        private sealed class BrowseNameSource : INodeSource
        {
            private const string kSecondaryNamespaceUri =
                "urn:opcfoundation.org:Tests:NodeSource:Secondary";

            public ArrayOf<string> NamespaceUris =>
                [kNamespaceUri, kSecondaryNamespaceUri];

            public ushort DefaultNamespaceIndex { get; private set; }

            public ushort SecondaryNamespaceIndex { get; private set; }

            public QualifiedName DefaultBrowseName { get; private set; } = QualifiedName.Null;

            public QualifiedName ExplicitBrowseName { get; private set; } = QualifiedName.Null;

            public ValueTask BuildAsync(
                INodeGraphBuilder builder,
                CancellationToken cancellationToken = default)
            {
                DefaultNamespaceIndex =
                    builder.Context.NamespaceUris.GetIndexOrAppend(kNamespaceUri);
                SecondaryNamespaceIndex =
                    builder.Context.NamespaceUris.GetIndexOrAppend(kSecondaryNamespaceUri);
                INodeBuilder<FolderState> folder = builder.AddFolder("Default");
                DefaultBrowseName = folder.Node.BrowseName;
                ExplicitBrowseName = builder.AddObject(
                    new QualifiedName("Explicit", SecondaryNamespaceIndex),
                    folder.Node.NodeId).Node.BrowseName;
                return default;
            }
        }

        private sealed class AmbiguousBrowseNameSource : INodeSource
        {
            public ArrayOf<string> NamespaceUris => [kNamespaceUri];

            public ValueTask BuildAsync(
                INodeGraphBuilder builder,
                CancellationToken cancellationToken = default)
            {
                builder.AddFolder(new QualifiedName("Ambiguous"));
                return default;
            }
        }

        private sealed class AmbiguousGeneratedBrowseNameSource : INodeSource
        {
            public ArrayOf<string> NamespaceUris =>
                [GeneratedNodeSourceModel.Namespaces.GeneratedNodeSourceModel];

            public ValueTask BuildAsync(
                INodeGraphBuilder builder,
                CancellationToken cancellationToken = default)
            {
                GeneratedNodeSourceModel.
                    GeneratedNodeSourceModelNodeGraphBuilderExtensions.
                    AddDeviceType(
                        builder,
                        new QualifiedName("Ambiguous"));
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
                            "Child",
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

        private sealed class CrossNamespaceChildrenSource : INodeSource
        {
            private const string kInstanceNamespaceUri =
                "urn:opcfoundation.org:Tests:NodeSource:Instances";

            public ArrayOf<string> NamespaceUris =>
                [kNamespaceUri, kInstanceNamespaceUri];

            public NodeId FirstChildId { get; private set; }

            public NodeId SecondChildId { get; private set; }

            public ValueTask BuildAsync(
                INodeGraphBuilder builder,
                CancellationToken cancellationToken = default)
            {
                ushort modelNamespaceIndex =
                    builder.Context.NamespaceUris.GetIndexOrAppend(kNamespaceUri);
                ushort instanceNamespaceIndex =
                    builder.Context.NamespaceUris.GetIndexOrAppend(kInstanceNamespaceUri);
                INodeBuilder<BaseObjectState> firstParent = builder.Add(
                    new BaseObjectState(null)
                    {
                        BrowseName = new QualifiedName("First", instanceNamespaceIndex)
                    });
                INodeBuilder<BaseObjectState> secondParent = builder.Add(
                    new BaseObjectState(null)
                    {
                        BrowseName = new QualifiedName("Second", instanceNamespaceIndex)
                    });
                FirstChildId = builder.Add(
                    new BaseDataVariableState(firstParent.Node)
                    {
                        BrowseName = new QualifiedName("Value", modelNamespaceIndex),
                        DataType = DataTypeIds.Int32
                    }).Node.NodeId;
                SecondChildId = builder.Add(
                    new BaseDataVariableState(secondParent.Node)
                    {
                        BrowseName = new QualifiedName("Value", modelNamespaceIndex),
                        DataType = DataTypeIds.Int32
                    }).Node.NodeId;
                return default;
            }
        }

        private sealed class ExternalParentChildrenSource : INodeSource
        {
            public ArrayOf<string> NamespaceUris => [kNamespaceUri];

            public NodeId FirstId { get; private set; }

            public NodeId SecondId { get; private set; }

            public ValueTask BuildAsync(
                INodeGraphBuilder builder,
                CancellationToken cancellationToken = default)
            {
                FirstId = builder.AddObject(
                    "Shared",
                    ObjectIds.Server).Node.NodeId;
                SecondId = builder.AddObject(
                    "Shared",
                    ObjectIds.Server_ServerCapabilities).Node.NodeId;
                return default;
            }
        }

        private sealed class IdentifierCollisionSource : INodeSource
        {
            public const string ExternalNamespaceUri =
                "urn:opcfoundation.org:Tests:NodeSource:External";

            public ArrayOf<string> NamespaceUris => [kNamespaceUri];

            public NodeId NumericParentChildId { get; private set; }

            public NodeId StringParentChildId { get; private set; }

            public NodeId DelimitedParentChildId { get; private set; }

            public NodeId DelimitedBrowseNameChildId { get; private set; }

            public NodeId ExternalParentChildId { get; private set; }

            public ValueTask BuildAsync(
                INodeGraphBuilder builder,
                CancellationToken cancellationToken = default)
            {
                ushort namespaceIndex =
                    builder.Context.NamespaceUris.GetIndexOrAppend(kNamespaceUri);
                INodeBuilder<BaseObjectState> numericParent = builder.Add(
                    CreateParent(
                        new NodeId(1u, namespaceIndex),
                        new QualifiedName("NumericParent", namespaceIndex)));
                INodeBuilder<BaseObjectState> stringParent = builder.Add(
                    CreateParent(
                        new NodeId("1", namespaceIndex),
                        new QualifiedName("StringParent", namespaceIndex)));
                INodeBuilder<BaseObjectState> delimitedParent = builder.Add(
                    CreateParent(
                        new NodeId("A_B", namespaceIndex),
                        new QualifiedName("DelimitedParent", namespaceIndex)));
                INodeBuilder<BaseObjectState> delimitedBrowseNameParent = builder.Add(
                    CreateParent(
                        new NodeId("A", namespaceIndex),
                        new QualifiedName("DelimitedBrowseNameParent", namespaceIndex)));

                NumericParentChildId = builder
                    .AddObject("Value", numericParent.Node.NodeId).Node.NodeId;
                StringParentChildId = builder
                    .AddObject("Value", stringParent.Node.NodeId).Node.NodeId;
                DelimitedParentChildId = builder
                    .AddObject("C", delimitedParent.Node.NodeId).Node.NodeId;
                DelimitedBrowseNameChildId = builder
                    .AddObject(
                        "B_C",
                        delimitedBrowseNameParent.Node.NodeId).Node.NodeId;
                int externalNamespaceIndex = builder.Context.NamespaceUris.GetIndex(
                    ExternalNamespaceUri);
                if (externalNamespaceIndex >= 0)
                {
                    ExternalParentChildId = builder.AddObject(
                        "ExternalValue",
                        new NodeId(
                            "ExternalParent",
                            (ushort)externalNamespaceIndex)).Node.NodeId;
                }
                return default;
            }

            private static BaseObjectState CreateParent(
                NodeId nodeId,
                QualifiedName browseName)
            {
                return new BaseObjectState(null)
                {
                    NodeId = nodeId,
                    BrowseName = browseName,
                    DisplayName = new LocalizedText(browseName.Name)
                };
            }
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
