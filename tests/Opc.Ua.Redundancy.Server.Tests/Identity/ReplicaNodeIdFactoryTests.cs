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

using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.Tests.Redundancy;
using Opc.Ua.Tests;

namespace Opc.Ua.Redundancy.Server.Tests.Identity
{
    /// <summary>
    /// Exercises the configured factory contract through independent replica contexts.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class ReplicaNodeIdFactoryTests
    {
        /// <summary>
        /// Keeps exact shared NodeIds despite reversed registration order and unrelated local counter allocations.
        /// </summary>
        [TestCase(NodeIdAssignmentMode.Numeric)]
        [TestCase(NodeIdAssignmentMode.String)]
        [TestCase(NodeIdAssignmentMode.Guid)]
        [TestCase(NodeIdAssignmentMode.Opaque)]
        public void IndependentFactoriesPreserveWireIdentity(NodeIdAssignmentMode mode)
        {
            var first = new ReplicaNodeIdFactory("test-set", [ModelUri, InstanceUri], mode);
            var second = new ReplicaNodeIdFactory("test-set", [ModelUri, InstanceUri], mode);
            SystemContext left = CreateContext("urn:replica:left");
            SystemContext right = CreateContext("urn:replica:right");
            first.PrepareNamespaces(left.NamespaceUris);
            second.PrepareNamespaces(right.NamespaceUris);
            left.NamespaceUris.GetIndexOrAppend(ModelUri);
            left.NamespaceUris.GetIndexOrAppend(InstanceUri);
            right.NamespaceUris.GetIndexOrAppend(InstanceUri);
            right.NamespaceUris.GetIndexOrAppend(ModelUri);
            IRebasableNodeIdFactory leftShared = first.WithDefaultNamespaceIndex(3);
            IRebasableNodeIdFactory rightShared = second.WithDefaultNamespaceIndex(3);
            IRebasableNodeIdFactory localCounters = second.WithDefaultNamespaceIndex(1)
                .WithMode(NodeIdAssignmentMode.Counter);
            _ = localCounters.NextCounterNodeId();
            _ = localCounters.NextCounterNodeId();

            NodeId leftPump = leftShared.New(left, Node("Pump"));
            NodeId leftValve = leftShared.New(left, Node("Valve"));
            NodeId rightValve = rightShared.New(right, Node("Valve"));
            NodeId rightPump = rightShared.New(right, Node("Pump"));

            Assert.That(rightPump, Is.EqualTo(leftPump));
            Assert.That(rightValve, Is.EqualTo(leftValve));
            Assert.That(rightPump.NamespaceIndex, Is.EqualTo(3));
            Assert.That(first.Descriptor, Is.EqualTo(second.Descriptor));
            Assert.That(right.NamespaceUris.GetString(1), Is.EqualTo("urn:replica:right"));
        }

        /// <summary>
        /// Rejects existing incompatible namespace indexes before appending any configured namespaces.
        /// </summary>
        [Test]
        public void NamespacePreparationRejectsIncompatibleExistingSlots()
        {
            var factory = new ReplicaNodeIdFactory("test-set", [ModelUri, InstanceUri]);
            SystemContext context = CreateContext("urn:replica");
            context.NamespaceUris.Append(InstanceUri);

            Assert.That(() => factory.PrepareNamespaces(context.NamespaceUris),
                Throws.TypeOf<ServiceResultException>());
            Assert.That(context.NamespaceUris.Count, Is.EqualTo(3));
            Assert.That(context.NamespaceUris.GetString(2), Is.EqualTo(InstanceUri));
        }

        /// <summary>
        /// Allows transient counter allocation but refuses independently minted counters in a shared registered graph.
        /// </summary>
        [Test]
        public void IndependentSharedRegistrationRejectsCounterFallback()
        {
            var root = new ReplicaNodeIdFactory("test-set", [ModelUri, InstanceUri]);
            SystemContext context = CreateContext("urn:replica");
            root.PrepareNamespaces(context.NamespaceUris);
            IRebasableNodeIdFactory factory = root.WithDefaultNamespaceIndex(3).WithMode(NodeIdAssignmentMode.Counter);
            BaseObjectState node = Node("Runtime");
            node.NodeId = factory.New(context, node);

            Assert.That(() => root.ValidateRegistration(context, node),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadConfigurationError));
        }

        /// <summary>
        /// Preserves supplied replica IDs instead of reminting them during validation or repeated assignment.
        /// </summary>
        [Test]
        public void SuppliedSharedNodeIdsRemainUnchanged()
        {
            var root = new ReplicaNodeIdFactory("test-set", [ModelUri, InstanceUri]);
            SystemContext context = CreateContext("urn:replica");
            root.PrepareNamespaces(context.NamespaceUris);
            IRebasableNodeIdFactory factory = root.WithDefaultNamespaceIndex(3);
            BaseObjectState node = Node("Imported");
            node.NodeId = new NodeId("stable-asset-key", 3);

            root.ValidateReplicatedTree(context, node);
            root.ValidateRegistration(context, node);

            Assert.That(factory.New(context, node), Is.EqualTo(new NodeId("stable-asset-key", 3)));
        }

        /// <summary>
        /// Prevents factory views and replacement from disabling collision protection or changing shared identity.
        /// </summary>
        [Test]
        public void FactoryViewsPreserveServerPolicy()
        {
            var root = new ReplicaNodeIdFactory("test-set", [ModelUri, InstanceUri]);
            IRebasableNodeIdFactory view = root.WithDefaultNamespaceIndex(3);

            Assert.That(view.WithCollisionDetection(false).DetectsCollisions, Is.True);
            Assert.That(() => view.WithMode(NodeIdAssignmentMode.Guid), Throws.TypeOf<ServiceResultException>());
            Assert.That(() => root.WithMode(NodeIdAssignmentMode.Guid).WithDefaultNamespaceIndex(3),
                Throws.TypeOf<ServiceResultException>());
            IRebasableNodeIdFactory replacement = root.Apply(new DefaultNodeIdFactory(
                NodeIdAssignmentMode.Numeric, 3, detectCollisions: false));
            Assert.That(replacement.DetectsCollisions, Is.True);
        }

        /// <summary>
        /// Rejects duplicate identities before registration and does not exempt shared nodes owned by the core manager.
        /// </summary>
        [Test]
        public void ExplicitCollisionsAndInfrastructureBypassesAreRejected()
        {
            var identity = new ReplicaNodeIdFactory("test-set", [ModelUri, InstanceUri]);
            SystemContext context = CreateContext("urn:replica");
            identity.PrepareNamespaces(context.NamespaceUris);
            BaseObjectState first = Node("First");
            first.NodeId = new NodeId(77, 3);
            identity.ValidateRegistration(context, first);
            BaseObjectState other = Node("Other");
            other.NodeId = first.NodeId;
            Assert.That(() => identity.ValidateRegistration(context, other), Throws.TypeOf<ServiceResultException>());

            BaseObjectState counter = Node("Counter");
            counter.NodeId = identity.WithDefaultNamespaceIndex(3).NextCounterNodeId();
            Assert.That(() => identity.ValidateRegistration(context, counter, isServerInfrastructure: true),
                Throws.TypeOf<ServiceResultException>());
            var local = new BaseObjectState(null) { NodeId = new NodeId("diagnostic", 4) };
            Assert.That(
                () => identity.ValidateRegistration(context, local, isServerInfrastructure: true),
                Throws.Nothing);
            Assert.That(() => identity.ValidateRegistration(context, local), Throws.TypeOf<ServiceResultException>());
        }

        /// <summary>
        /// Keeps transient event allocation local even when its owning manager serves a shared model namespace.
        /// </summary>
        [Test]
        public void TransientEventIdentifiersRemainLocal()
        {
            var identity = new ReplicaNodeIdFactory("test-set", [ModelUri, InstanceUri]);
            SystemContext context = CreateContext("urn:replica");
            identity.PrepareNamespaces(context.NamespaceUris);
            IRebasableNodeIdFactory shared = identity.WithDefaultNamespaceIndex(3);
            var eventState = new BaseEventState(null);
            eventState.NodeId = shared.New(context, eventState);
            var field = new PropertyState(eventState) { BrowseName = new QualifiedName(BrowseNames.SourceName) };

            Assert.That(eventState.NodeId.NamespaceIndex, Is.EqualTo(1));
            Assert.That(shared.New(context, field).NamespaceIndex, Is.EqualTo(1));
        }

        /// <summary>
        /// Distinguishes authenticated inbound object provenance from a transient allocation with the same numeric ID.
        /// </summary>
        [Test]
        public void IncomingIdentityIsNotMistakenForALocalCounterAllocation()
        {
            var identity = new ReplicaNodeIdFactory("test-set", [ModelUri, InstanceUri]);
            SystemContext context = CreateContext("urn:replica");
            identity.PrepareNamespaces(context.NamespaceUris);
            NodeId coincidentId = identity.WithDefaultNamespaceIndex(3).NextCounterNodeId();
            BaseObjectState incoming = Node("Incoming");
            incoming.NodeId = coincidentId;
            identity.AuthorizeReplicatedTree(context, incoming);

            Assert.That(() => identity.ValidateRegistration(context, incoming), Throws.Nothing);
            BaseObjectState local = Node("Local");
            local.NodeId = coincidentId;
            Assert.That(() => identity.ValidateRegistration(context, local), Throws.TypeOf<ServiceResultException>());
        }

        /// <summary>
        /// Reserves live root/child IDs and retained tombstones before the writer can allocate after restart.
        /// </summary>
        [Test]
        public async Task WriterAllocationSkipsRetainedIdsBeforeAuthoringAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messages = ServiceMessageContext.CreateEmpty(telemetry);
            messages.NamespaceUris.Append("urn:replica");
            using var store = new InMemorySharedKeyValueStore();
            await using var election = new StaticLeaderElection(true);
            var identity = new ReplicaNodeIdFactory(
                "test-set", [ModelUri, InstanceUri], writerElection: election, store: store);
            identity.PrepareNamespaces(messages.NamespaceUris);
            Mock<IServerInternal> server = CreateServer(identity, messages, telemetry);
            ServerSystemContext context = server.Object.DefaultSystemContext;
            IRebasableNodeIdFactory counter = identity.WithDefaultNamespaceIndex(3);
            Assert.That(counter.NextCounterNodeId().TryGetValue(out uint start), Is.True);
            var deletedId = new NodeId(start + 1, 3);
            BaseObjectState retained = Node("Retained");
            retained.NodeId = new NodeId(start + 2, 3);
            var child = new BaseObjectState(retained)
            {
                NodeId = new NodeId(start + 3, 3),
                BrowseName = new QualifiedName("Child", 3)
            };
            retained.AddChild(child);
            await identity.InitializeNewStoreAsync(store, NullRecordProtector.Instance).ConfigureAwait(false);
            using var nodes = new InMemoryNodeStateStore(store, messages);
            await nodes.DeleteNodeAsync(deletedId).ConfigureAwait(false);
            await nodes.UpsertNodeAsync(new StoredNode(
                retained.NodeId, NodeStateSerializer.Serialize(context, retained))).ConfigureAwait(false);

            await identity.OnServerStartingAsync(server.Object).ConfigureAwait(false);

            Assert.That(counter.NextCounterNodeId(), Is.EqualTo(new NodeId(start + 4, 3)));
            Assert.That((await nodes.TryGetNodeAsync(retained.NodeId).ConfigureAwait(false))!.NodeId,
                Is.EqualTo(retained.NodeId));
        }

        /// <summary>
        /// Keeps an invalidated peer admission at maintenance even if the ordinary provider reports healthy.
        /// </summary>
        [Test]
        public async Task RejectedPeerCannotRestoreHealthyServiceLevelAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messages = ServiceMessageContext.CreateEmpty(telemetry);
            messages.NamespaceUris.Append("urn:replica");
            var identity = new ReplicaNodeIdFactory("test-set", [ModelUri, InstanceUri]);
            Mock<IServerInternal> server = CreateServer(identity, messages, telemetry);
            var serverObject = new ServerObjectState(null);
            serverObject.ServiceLevel = PropertyState<byte>.With<VariantBuilder>(serverObject, byte.MaxValue);
            serverObject.ServiceLevel.NodeId = VariableIds.Server_ServiceLevel;
            server.Setup(s => s.ServerObject).Returns(serverObject);
            await identity.OnServerStartingAsync(server.Object).ConfigureAwait(false);
            var levels = new Mock<IServiceLevelProvider>();
            levels.Setup(s => s.GetServiceLevel()).Returns(byte.MaxValue);
            var startup = new ServiceLevelStartupTask(levels.Object);
            await startup.OnServerStartedAsync(server.Object).ConfigureAwait(false);

            identity.RejectPeer();
            levels.Raise(s => s.ServiceLevelChanged += null, (byte)250);

            Assert.That(serverObject.ServiceLevel.Value, Is.Zero);
        }

        /// <summary>
        /// Registers one shared factory and contributes the identity key to customized strong routing.
        /// </summary>
        [Test]
        public async Task FluentIdentityRegistrationPreservesSingletonAndStrongKeyAsync()
        {
            var builder = new DiTestServerBuilder();
            builder.UseReplicaNodeIdentity("test-set", [ModelUri, InstanceUri]);
            builder.UseDistributedAddressSpace();
            await using ServiceProvider services = builder.Services.BuildServiceProvider();
            ReplicaNodeIdFactory identity = services.GetRequiredService<ReplicaNodeIdFactory>();

            Assert.That(services.GetRequiredService<IRebasableNodeIdFactory>(), Is.SameAs(identity));
            Assert.That(
                services.GetServices<IStrongKeyspaceProvider>()
                    .SelectMany(provider => provider.GetStrongKeyPrefixes().ToArray()),
                Does.Contain(ReplicaIdentityStore.Key));
            Assert.That(services.GetServices<IServerPreStartupTask>().ToArray(),
                Has.Some.InstanceOf<DistributedAddressSpaceStartupTask>());
        }

        /// <summary>
        /// Rejects a multi-writer composition that attempts to authorize independent writer-assigned counters.
        /// </summary>
        [Test]
        public async Task MultiWriterCompositionCannotEnableWriterAssignedIdsAsync()
        {
            var builder = new DiTestServerBuilder();
            builder.UseReplicaNodeIdentity("test-set", [ModelUri, InstanceUri], writerAssignedIds: true);
            builder.UseReplicatedAddressSpace();
            await using ServiceProvider services = builder.Services.BuildServiceProvider();

            Assert.That(
                () => services.GetRequiredService<ReplicaNodeIdFactory>(),
                Throws.TypeOf<ServiceResultException>());
        }

        private static Mock<IServerInternal> CreateServer(
            ReplicaNodeIdFactory identity,
            ServiceMessageContext messages,
            ITelemetryContext telemetry)
        {
            var server = new Mock<IServerInternal>();
            server.As<INodeIdFactoryProvider>().Setup(s => s.NodeIdFactory).Returns(identity);
            server.Setup(s => s.Telemetry).Returns(telemetry);
            server.Setup(s => s.MessageContext).Returns(messages);
            server.Setup(s => s.NamespaceUris).Returns(messages.NamespaceUris);
            server.Setup(s => s.ServerUris).Returns(messages.ServerUris);
            server.Setup(s => s.Factory).Returns(messages.Factory);
            server.Setup(s => s.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            return server;
        }

        /// <summary>
        /// Checks namespace-bearing metadata and nested typed values, not just the root identifier and browse name.
        /// </summary>
        [TestCase("TypeDefinition")]
        [TestCase("DataType")]
        [TestCase("Reference")]
        [TestCase("Value")]
        [TestCase("Array")]
        [TestCase("Argument")]
        [TestCase("DataValue")]
        [TestCase("Permission")]
        public void SharedMetadataRejectsUndeclaredNamespace(string field)
        {
            var identity = new ReplicaNodeIdFactory("test-set", [ModelUri, InstanceUri]);
            SystemContext context = CreateContext("urn:replica");
            identity.PrepareNamespaces(context.NamespaceUris);
            context.NamespaceUris.Append("urn:undeclared");
            var foreign = new NodeId("foreign", 4);
            var node = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("variable", 3),
                BrowseName = new QualifiedName("Variable", 3),
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                DataType = DataTypeIds.NodeId
            };
            switch (field)
            {
                case "TypeDefinition":
                    node.TypeDefinitionId = foreign;
                    break;
                case "DataType":
                    node.DataType = foreign;
                    break;
                case "Reference":
                    node.AddReference(ReferenceTypeIds.HasComponent, false, foreign);
                    break;
                case "Value":
                    node.Value = Variant.From(foreign);
                    break;
                case "Array":
                    ArrayOf<NodeId> elements = [foreign];
                    node.Value = Variant.From(elements);
                    break;
                case "Argument":
                    node.Value = Variant.From(new ExtensionObject(new Argument
                    {
                        Name = "Input",
                        DataType = foreign,
                        ValueRank = ValueRanks.Scalar
                    }));
                    break;
                case "DataValue":
                    node.Value = Variant.From(new DataValue(Variant.From(foreign)));
                    break;
                case "Permission":
                    node.RolePermissions = [new RolePermissionType { RoleId = foreign }];
                    break;
                default:
                    Assert.Fail("Unknown metadata test case.");
                    break;
            }

            Assert.That(() => identity.ValidateRegistration(context, node), Throws.TypeOf<ServiceResultException>());
            Assert.That(() => identity.ValidateReplicatedTree(context, node), Throws.TypeOf<ServiceResultException>());
        }

        private static BaseObjectState Node(string name)
        {
            return new BaseObjectState(null) { BrowseName = new QualifiedName(name, 3) };
        }

        private static SystemContext CreateContext(string applicationUri)
        {
            var namespaces = new NamespaceTable();
            namespaces.Append(applicationUri);
            return new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = namespaces,
                ServerUris = new StringTable()
            };
        }

        private const string ModelUri = "urn:test:model";
        private const string InstanceUri = "urn:test:instances";
    }
}
