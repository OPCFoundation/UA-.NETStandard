/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies that TranslateBrowsePathsToNodeIds only follows the references of nodes
    /// the user is allowed to browse (Part 3 8.55 PermissionType Browse).
    /// </summary>
    [TestFixture]
    [Category("MasterNodeManager")]
    [Parallelizable]
    public class TranslateBrowsePathPermissionTests
    {
        private const string kNamespaceUri = "urn:opcfoundation:server:tests:translate-permissions";
        private static readonly NodeId s_roleId = ObjectIds.WellKnownRole_Operator;

        [Test]
        public async Task TranslateBrowsePathDoesNotWalkThroughIntermediateNodeWithoutBrowseAsync()
        {
            using var harness = new Harness(intermediateNodePermissions: PermissionType.Read);

            BrowsePathResult result = await harness.TranslateAsync().ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNoMatch));
            Assert.That(result.Targets.Count, Is.Zero);
        }

        [Test]
        public async Task TranslateBrowsePathWalksThroughIntermediateNodeWithBrowseAsync()
        {
            using var harness = new Harness(
                intermediateNodePermissions: PermissionType.Browse | PermissionType.Read);

            BrowsePathResult result = await harness.TranslateAsync().ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.Targets.Count, Is.EqualTo(1));
            Assert.That(result.Targets[0].TargetId, Is.EqualTo(new ExpandedNodeId(harness.TargetNodeId)));
        }

        /// <summary>
        /// A single node manager owning Start/Intermediate/Target, reachable by the path
        /// "Intermediate/Target" from Start.
        /// </summary>
        private sealed class Harness : IDisposable
        {
            public Harness(PermissionType intermediateNodePermissions)
            {
                var namespaceTable = new NamespaceTable();
                ushort namespaceIndex = (ushort)namespaceTable.Append(kNamespaceUri);

                var typeTree = new TypeTable(namespaceTable);
                typeTree.AddSubtype(ReferenceTypeIds.References, NodeId.Null);
                typeTree.AddSubtype(ReferenceTypeIds.HierarchicalReferences, ReferenceTypeIds.References);
                typeTree.AddSubtype(ReferenceTypeIds.Organizes, ReferenceTypeIds.HierarchicalReferences);

                StartNodeId = new NodeId("Start", namespaceIndex);
                m_intermediateNodeId = new NodeId("Intermediate", namespaceIndex);
                TargetNodeId = new NodeId("Target", namespaceIndex);
                var metadata = new Dictionary<NodeId, NodeMetadata>
                {
                    [StartNodeId] = CreateMetadata(StartNodeId, PermissionType.Browse),
                    [m_intermediateNodeId] = CreateMetadata(m_intermediateNodeId, intermediateNodePermissions),
                    [TargetNodeId] = CreateMetadata(TargetNodeId, PermissionType.Browse)
                };
                var children = new Dictionary<NodeId, (string Name, NodeId Child)>
                {
                    [StartNodeId] = ("Intermediate", m_intermediateNodeId),
                    [m_intermediateNodeId] = ("Target", TargetNodeId)
                };

                var manager = new Mock<IAsyncNodeManager>();
                manager.Setup(m => m.NamespaceUris).Returns([kNamespaceUri]);
                manager
                    .Setup(m => m.GetManagerHandleAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                    .Returns((NodeId nodeId, CancellationToken _) =>
                        new ValueTask<object>(metadata.ContainsKey(nodeId) ? nodeId : null!));
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
                        CancellationToken _) => new ValueTask<NodeMetadata>(metadata[(NodeId)handle]));
                manager
                    .Setup(m => m.GetNodeMetadataAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<object>(),
                        It.IsAny<BrowseResultMask>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((OperationContext _, object handle, BrowseResultMask _, CancellationToken _) =>
                        new ValueTask<NodeMetadata>(metadata[(NodeId)handle]));
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
                        if (children.TryGetValue((NodeId)handle, out (string Name, NodeId Child) child) &&
                            element.TargetName.Name == child.Name)
                        {
                            targetIds.Add(child.Child);
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
                session.Setup(s => s.Id).Returns(new NodeId("TranslateSession", 0));
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

            public NodeId StartNodeId { get; }

            public NodeId TargetNodeId { get; }

            public async Task<BrowsePathResult> TranslateAsync()
            {
                var endpoint = new EndpointDescription
                {
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                    TransportProfileUri = Profiles.UaTcpTransport
                };
                var context = new OperationContext(
                    new RequestHeader(),
                    new SecureChannelContext("TranslateChannel", endpoint, RequestEncoding.Binary),
                    RequestType.TranslateBrowsePathsToNodeIds,
                    RequestLifetime.None,
                    m_session.Object);
                var browsePath = new BrowsePath
                {
                    StartingNode = StartNodeId,
                    RelativePath = new RelativePath
                    {
                        Elements =
                        [
                            new RelativePathElement
                            {
                                ReferenceTypeId = ReferenceTypeIds.Organizes,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName("Intermediate", StartNodeId.NamespaceIndex)
                            },
                            new RelativePathElement
                            {
                                ReferenceTypeId = ReferenceTypeIds.Organizes,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName("Target", StartNodeId.NamespaceIndex)
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

            private static NodeMetadata CreateMetadata(NodeId nodeId, PermissionType permissions)
            {
                return new NodeMetadata(nodeId, nodeId)
                {
                    NodeClass = NodeClass.Object,
                    RolePermissions =
                    [
                        new RolePermissionType
                        {
                            RoleId = s_roleId,
                            Permissions = (uint)permissions
                        }
                    ]
                };
            }

            private readonly NodeId m_intermediateNodeId;
            private readonly Mock<ISession> m_session;
        }
    }
}
