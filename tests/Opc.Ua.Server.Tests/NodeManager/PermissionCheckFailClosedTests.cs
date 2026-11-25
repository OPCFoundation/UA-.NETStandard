// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies that the master node manager's permission check fails closed for client
    /// requests (Part 3 §8.55, §8.56): a node without metadata is refused with
    /// Bad_NodeIdUnknown before its node manager sees the operation, while internal calls
    /// without a session, identity or channel are not checked.
    /// </summary>
    [TestFixture]
    [Category("MasterNodeManager")]
    [Parallelizable]
    public class PermissionCheckFailClosedTests
    {
        private const string kNamespaceUri = "urn:opcfoundation:server:tests:fail-closed";
        private static readonly NodeId s_roleId = ObjectIds.WellKnownRole_Operator;

        [Test]
        public async Task ClientReadOfNodeWithoutMetadataIsRefusedBeforeDispatchAsync()
        {
            using var harness = new Harness();

            DataValue value = await harness.ReadAsync(harness.HiddenNodeId, clientRequest: true)
                .ConfigureAwait(false);

            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(harness.ReadDispatchCount, Is.Zero, "The node manager must not see the read.");
        }

        [Test]
        public async Task ClientReadOfNodeWithMetadataIsDispatchedAsync()
        {
            using var harness = new Harness();

            DataValue value = await harness.ReadAsync(harness.VisibleNodeId, clientRequest: true)
                .ConfigureAwait(false);

            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.ReadDispatchCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ClientReadOfUnknownNodeReturnsBadNodeIdUnknownAsync()
        {
            using var harness = new Harness();

            DataValue value = await harness.ReadAsync(
                new NodeId("Missing", harness.NamespaceIndex),
                clientRequest: true).ConfigureAwait(false);

            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(harness.ReadDispatchCount, Is.Zero);
        }

        [Test]
        public async Task InternalReadOfNodeWithoutMetadataIsNotCheckedAsync()
        {
            using var harness = new Harness();

            DataValue value = await harness.ReadAsync(harness.HiddenNodeId, clientRequest: false)
                .ConfigureAwait(false);

            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.ReadDispatchCount, Is.EqualTo(1));
        }

        [Test]
        public async Task TranslateBrowsePathDropsTargetWithoutMetadataAsync()
        {
            using var harness = new Harness();

            BrowsePathResult result = await harness.TranslateAsync("Hidden").ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNoMatch));
            Assert.That(result.Targets.Count, Is.Zero);
        }

        [Test]
        public async Task TranslateBrowsePathKeepsTargetWithMetadataAsync()
        {
            using var harness = new Harness();

            BrowsePathResult result = await harness.TranslateAsync("Visible").ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.Targets.Count, Is.EqualTo(1));
            Assert.That(result.Targets[0].TargetId, Is.EqualTo(new ExpandedNodeId(harness.VisibleNodeId)));
        }

        /// <summary>
        /// One node manager owning Start, Visible and Hidden. Hidden resolves to a handle
        /// but the node manager returns no metadata for it.
        /// </summary>
        private sealed class Harness : IDisposable
        {
            public Harness()
            {
                var namespaceTable = new NamespaceTable();
                NamespaceIndex = (ushort)namespaceTable.Append(kNamespaceUri);

                var typeTree = new TypeTable(namespaceTable);
                typeTree.AddSubtype(ReferenceTypeIds.References, NodeId.Null);
                typeTree.AddSubtype(ReferenceTypeIds.HierarchicalReferences, ReferenceTypeIds.References);
                typeTree.AddSubtype(ReferenceTypeIds.Organizes, ReferenceTypeIds.HierarchicalReferences);

                m_startNodeId = new NodeId("Start", NamespaceIndex);
                VisibleNodeId = new NodeId("Visible", NamespaceIndex);
                HiddenNodeId = new NodeId("Hidden", NamespaceIndex);
                var metadata = new Dictionary<NodeId, NodeMetadata?>
                {
                    [m_startNodeId] = CreateMetadata(m_startNodeId),
                    [VisibleNodeId] = CreateMetadata(VisibleNodeId),
                    [HiddenNodeId] = null
                };

                var manager = new Mock<IAsyncNodeManager>();
                manager.Setup(m => m.NamespaceUris).Returns([kNamespaceUri]);
                manager
                    .Setup(m => m.GetManagerHandleAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                    .Returns((NodeId nodeId, CancellationToken _) =>
                        new ValueTask<object?>(metadata.ContainsKey(nodeId) ? nodeId : null));
                manager
                    .Setup(m => m.GetPermissionMetadataAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<object>(),
                        It.IsAny<BrowseResultMask>(),
                        It.IsAny<Dictionary<NodeId, Variant[]>>(),
                        It.IsAny<bool>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((
                        OperationContext _,
                        object handle,
                        BrowseResultMask _,
                        Dictionary<NodeId, Variant[]> _,
                        bool _,
                        CancellationToken _) => new ValueTask<NodeMetadata?>(metadata[(NodeId)handle]));
                manager
                    .Setup(m => m.GetNodeMetadataAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<object>(),
                        It.IsAny<BrowseResultMask>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((OperationContext _, object handle, BrowseResultMask _, CancellationToken _) =>
                        new ValueTask<NodeMetadata?>(metadata[(NodeId)handle]));
                manager
                    .Setup(m => m.ReadAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<double>(),
                        It.IsAny<ArrayOf<ReadValueId>>(),
                        It.IsAny<IList<DataValue>>(),
                        It.IsAny<IList<ServiceResult>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((
                        OperationContext _,
                        double _,
                        ArrayOf<ReadValueId> nodesToRead,
                        IList<DataValue> values,
                        IList<ServiceResult> errors,
                        CancellationToken _) =>
                    {
                        for (int ii = 0; ii < nodesToRead.Count; ii++)
                        {
                            if (nodesToRead[ii].Processed ||
                                !metadata.ContainsKey(nodesToRead[ii].NodeId))
                            {
                                continue;
                            }
                            nodesToRead[ii].Processed = true;
                            Interlocked.Increment(ref m_readDispatchCount);
                            values[ii] = new DataValue(new Variant(42));
                            errors[ii] = ServiceResult.Good;
                        }
                        return default;
                    });
                manager
                    .Setup(m => m.TranslateBrowsePathAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<object>(),
                        It.IsAny<RelativePathElement>(),
                        It.IsAny<IList<ExpandedNodeId>>(),
                        It.IsAny<IList<NodeId>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((
                        OperationContext _,
                        object handle,
                        RelativePathElement element,
                        IList<ExpandedNodeId> targetIds,
                        IList<NodeId> _,
                        CancellationToken _) =>
                    {
                        if ((NodeId)handle == m_startNodeId)
                        {
                            targetIds.Add(new NodeId(element.TargetName.Name!, NamespaceIndex));
                        }
                        return default;
                    });

                var configurationManager = new Mock<IConfigurationNodeManager>();
                var coreNodeManager = new Mock<ICoreNodeManager>();
                var factory = new Mock<IMainNodeManagerFactory>();
                factory.Setup(f => f.CreateConfigurationNodeManager()).Returns(configurationManager.Object);
                factory.Setup(f => f.CreateCoreNodeManager(It.IsAny<ushort>())).Returns(coreNodeManager.Object);

                var server = new Mock<IServerInternal>();
                server.Setup(s => s.Telemetry).Returns(NUnitTelemetryContext.Create());
                server.Setup(s => s.NamespaceUris).Returns(namespaceTable);
                server.Setup(s => s.ServerUris).Returns(new StringTable());
                server.Setup(s => s.TypeTree).Returns(typeTree);
                server.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
                server.Setup(s => s.MainNodeManagerFactory).Returns(factory.Object);
                server.Setup(s => s.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));

                var identity = new Mock<IUserIdentity>();
                identity.Setup(i => i.GrantedRoleIds).Returns(new NodeId[] { s_roleId }.ToArrayOf());
                var session = new Mock<ISession>();
                session.Setup(s => s.Id).Returns(new NodeId("FailClosedSession", 0));
                session.Setup(s => s.EffectiveIdentity).Returns(identity.Object);
                session.Setup(s => s.PreferredLocales).Returns([]);
                m_session = session;

                Sut = new MasterNodeManager(
                    server.Object,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    null,
                    [manager.Object]);
            }

            public MasterNodeManager Sut { get; }

            public ushort NamespaceIndex { get; }

            public NodeId VisibleNodeId { get; }

            public NodeId HiddenNodeId { get; }

            public int ReadDispatchCount => Volatile.Read(ref m_readDispatchCount);

            public async Task<DataValue> ReadAsync(NodeId nodeId, bool clientRequest)
            {
                using OperationContext context = clientRequest
                    ? CreateClientContext(RequestType.Read)
                    : new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
                (ArrayOf<DataValue> values, _) = await Sut.ReadAsync(
                    context,
                    0,
                    TimestampsToReturn.Neither,
                    new[] { new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value } }.ToArrayOf())
                    .ConfigureAwait(false);
                return values[0];
            }

            public async Task<BrowsePathResult> TranslateAsync(string targetName)
            {
                using OperationContext context = CreateClientContext(RequestType.TranslateBrowsePathsToNodeIds);
                var browsePath = new BrowsePath
                {
                    StartingNode = m_startNodeId,
                    RelativePath = new RelativePath
                    {
                        Elements =
                        [
                            new RelativePathElement
                            {
                                ReferenceTypeId = ReferenceTypeIds.Organizes,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName(targetName, NamespaceIndex)
                            }
                        ]
                    }
                };

                (ArrayOf<BrowsePathResult> results, _) = await Sut.TranslateBrowsePathsToNodeIdsAsync(
                    context,
                    new BrowsePath[] { browsePath }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
                return results[0];
            }

            public void Dispose()
            {
                Sut.Dispose();
            }

            private OperationContext CreateClientContext(RequestType requestType)
            {
                var endpoint = new EndpointDescription
                {
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                    TransportProfileUri = Profiles.UaTcpTransport
                };
                return new OperationContext(
                    new RequestHeader(),
                    new SecureChannelContext("FailClosedChannel", endpoint, RequestEncoding.Binary),
                    requestType,
                    RequestLifetime.None,
                    m_session.Object);
            }

            private static NodeMetadata CreateMetadata(NodeId nodeId)
            {
                return new NodeMetadata(nodeId, nodeId)
                {
                    NodeClass = NodeClass.Variable,
                    RolePermissions =
                    [
                        new RolePermissionType
                        {
                            RoleId = s_roleId,
                            Permissions = (uint)(PermissionType.Browse | PermissionType.Read)
                        }
                    ]
                };
            }

            private readonly NodeId m_startNodeId;
            private readonly Mock<ISession> m_session;
            private int m_readDispatchCount;
        }
    }
}
