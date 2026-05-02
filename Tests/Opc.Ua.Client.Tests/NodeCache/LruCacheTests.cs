// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    [TestFixture]
    [Category("Client")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class LruCacheTests
    {
        [Test]
        public async Task FetchRemainingNodesAsyncShouldHandleErrorsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var id = new NodeId("test", 0);
            var context = new Mock<INodeCacheContext>();

            context
                .Setup(c => c.FetchNodesAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<NodeId>>(),
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResultSet<Node>
                {
                    Results = [new()],
                    Errors = [new ServiceResult(StatusCodes.BadUnexpectedError)]
                })
                .Verifiable(Times.Once);
            var nodeCache = new LruNodeCache(context.Object, telemetry);

            // Act
            ArrayOf<INode> result = await nodeCache.GetNodesAsync([id], default)
                .ConfigureAwait(false);

            // Assert
            Assert.That(result.Count, Is.EqualTo(1));
            context.Verify();
        }

        [Test]
        public async Task GetBuiltInTypeAsyncShouldHandleUnknownTypeAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var datatypeId = new NodeId("unknownType", 0);
            var context = new Mock<INodeCacheContext>();
            var nodeCache = new LruNodeCache(context.Object, telemetry);

            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == datatypeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i != datatypeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Never);

            // Act
            BuiltInType result = await nodeCache.GetBuiltInTypeAsync(datatypeId, default)
                .ConfigureAwait(false);

            // Assert
            Assert.That(result, Is.EqualTo(BuiltInType.Null));
            context.Verify();
        }

        [Test]
        public async Task GetBuiltInTypeAsyncShouldReturnBuiltInTypeAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var datatypeId = new NodeId((uint)BuiltInType.Int32, 0);
            var context = new Mock<INodeCacheContext>();
            var nodeCache = new LruNodeCache(context.Object, telemetry);

            // Act
            BuiltInType result = await nodeCache.GetBuiltInTypeAsync(datatypeId, default)
                .ConfigureAwait(false);

            // Assert
            Assert.That(result, Is.EqualTo(BuiltInType.Int32));
        }

        [Test]
        public async Task GetNodeAsyncShouldHandleEmptyListAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var context = new Mock<INodeCacheContext>();
            var nodeCache = new LruNodeCache(context.Object, telemetry);

            // Act
            ArrayOf<INode> result = await nodeCache.GetNodesAsync([], default)
                .ConfigureAwait(false);

            // Assert
            Assert.That(result.IsEmpty, Is.True);
        }

        [Test]
        public async Task GetNodeAsyncShouldReturnNodeFromCacheAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var expected = new Node();
            var id = new NodeId("test", 0);
            var context = new Mock<INodeCacheContext>();

            context
                .Setup(c => c.FetchNodeAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == id),
                    NodeClass.Unspecified,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(expected)
                .Verifiable(Times.Once);
            var nodeCache = new LruNodeCache(context.Object, telemetry);

            // Act
            INode result = await nodeCache.GetNodeAsync(id, default).ConfigureAwait(false);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
            result = await nodeCache.GetNodeAsync(id, default).ConfigureAwait(false);
            Assert.That(result, Is.EqualTo(expected));
            context.Verify();
        }

        [Test]
        public async Task GetNodeTestAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var expected = new Node();
            var id = new NodeId("test", 0);

            var context = new Mock<INodeCacheContext>();
            var nsTable = new NamespaceTable();
            context.Setup(c => c.NamespaceUris).Returns(nsTable);

            context
                .Setup(c => c.FetchNodeAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == id),
                    NodeClass.Unspecified,
                    false,
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, NodeId, NodeClass, bool, CancellationToken>((_, nodeId, _, _, ct)
                    => new ValueTask<Node>(expected))
                .Verifiable(Times.Once);
            var nodeCache = new LruNodeCache(context.Object, telemetry);

            INode result = await nodeCache.GetNodeAsync(id, default).ConfigureAwait(false);
            Assert.That(result, Is.EqualTo(expected));
            result = await nodeCache.GetNodeAsync(id, default).ConfigureAwait(false);
            Assert.That(result, Is.EqualTo(expected));
            result = await nodeCache.GetNodeAsync(id, default).ConfigureAwait(false);
            Assert.That(result, Is.EqualTo(expected));
            context.Verify();
        }

        [Test]
        public void GetNodeThrowsTest()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var expected = new Node();
            var id = new NodeId("test", 0);
            var context = new Mock<INodeCacheContext>();

            context
                .Setup(c => c.FetchNodeAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == id),
                    NodeClass.Unspecified,
                    false,
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, NodeId, NodeClass, bool, CancellationToken>((_, nodeId, _, _, ct)
                    => new ValueTask<Node>(Task.FromException<Node>(new ServiceResultException())))
                .Verifiable(Times.Exactly(3));
            var nodeCache = new LruNodeCache(context.Object, telemetry);

            _ = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await nodeCache.GetNodeAsync(id, default).ConfigureAwait(false));
            _ = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await nodeCache.GetNodeAsync(id, default).ConfigureAwait(false));
            _ = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await nodeCache.GetNodeAsync(id, default).ConfigureAwait(false));
            context.Verify();
        }

        [Test]
        public async Task GetNodeWithBrowsePathAsyncShouldHandleInvalidBrowsePathAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var id = new NodeId("test", 0);
            ArrayOf<QualifiedName> browsePath = [QualifiedName.From("invalid")];
            var context = new Mock<INodeCacheContext>();

            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == ReferenceTypeIds.HierarchicalReferences),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == id),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Once);
            var nodeCache = new LruNodeCache(context.Object, telemetry);

            // Act
            INode? result = await nodeCache.GetNodeWithBrowsePathAsync(id, browsePath, default)
                .ConfigureAwait(false);

            // Assert
            Assert.That(result, Is.Null);
            context.Verify();
        }

        [Test]
        public async Task GetNodeWithBrowsePathAsyncShouldReturnNodeAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var id = new NodeId("test", 0);
            var expected = new VariableNode
            {
                BrowseName = QualifiedName.From("child"),
                NodeId = id,
                NodeClass = NodeClass.Variable
            };
            ArrayOf<QualifiedName> browsePath = [QualifiedName.From("child")];
            var references = new List<ReferenceDescription>
            {
                new()
                {
                    NodeId = new ExpandedNodeId(id),
                    BrowseName = QualifiedName.From("child"),
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IsForward = true
                }
            };

            var context = new Mock<INodeCacheContext>();
            var nsTable = new NamespaceTable();
            context.Setup(c => c.NamespaceUris).Returns(nsTable);
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == ReferenceTypeIds.HierarchicalReferences),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == id),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([.. references])
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchNodesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<NodeId>>(n => n.Count == 1 && n[0] == id),
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResultSet<Node>
                {
                    Results = [expected],
                    Errors = [ServiceResult.Good]
                })
                .Verifiable(Times.Once);

            var nodeCache = new LruNodeCache(context.Object, telemetry);

            // Act
            INode? result = await nodeCache.GetNodeWithBrowsePathAsync(id, browsePath, default)
                .ConfigureAwait(false);

            // Assert
            Assert.That(result, Is.EqualTo(expected));

            // Act
            result = await nodeCache.GetNodeWithBrowsePathAsync(id, browsePath, default)
                .ConfigureAwait(false);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
            context.Verify();
        }

        [Test]
        public async Task GetNodeWithBrowsePathAsyncShouldReturnNodeWithMultipleElementsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var rootId = new NodeId("root", 0);
            var childId = new NodeId("child", 0);
            var grandChildId = new NodeId("grandChild", 0);
            ArrayOf<QualifiedName> browsePath =
            [
                new QualifiedName("child"),
                new QualifiedName("grandChild")
            ];

            var rootReferences = new List<ReferenceDescription>
            {
                new()
                {
                    NodeId = new ExpandedNodeId(childId),
                    BrowseName = QualifiedName.From("child"),
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IsForward = true
                }
            };
            var childNode = new VariableNode
            {
                BrowseName = QualifiedName.From("child"),
                NodeId = childId,
                NodeClass = NodeClass.Variable
            };
            var childReferences = new List<ReferenceDescription>
            {
                new()
                {
                    NodeId = new ExpandedNodeId(grandChildId),
                    BrowseName = QualifiedName.From("grandChild"),
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IsForward = true
                }
            };
            var expected = new VariableNode
            {
                BrowseName = QualifiedName.From("grandChild"),
                NodeId = grandChildId,
                NodeClass = NodeClass.Variable
            };

            var context = new Mock<INodeCacheContext>();
            var nsTable = new NamespaceTable();
            context.Setup(c => c.NamespaceUris).Returns(nsTable);

            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == ReferenceTypeIds.HierarchicalReferences),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == rootId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([.. rootReferences])
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == childId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([.. childReferences])
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchNodesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<NodeId>>(n => n.Count == 1 && n[0] == childId),
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResultSet<Node>
                {
                    Results = [childNode],
                    Errors = [ServiceResult.Good]
                })
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchNodesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<NodeId>>(n => n.Count == 1 && n[0] == grandChildId),
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResultSet<Node>
                {
                    Results = [expected],
                    Errors = [ServiceResult.Good]
                })
                .Verifiable(Times.Once);

            var nodeCache = new LruNodeCache(context.Object, telemetry);

            // Act
            INode? result = await nodeCache
                .GetNodeWithBrowsePathAsync(rootId, browsePath, default)
                .ConfigureAwait(false);

            // Assert
            Assert.That(result, Is.EqualTo(expected));

            // Act
            result = await nodeCache.GetNodeWithBrowsePathAsync(rootId, browsePath, default)
                .ConfigureAwait(false);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
            context.Verify();
        }

        [Test]
        public async Task GetReferencesAsyncShouldHandleEmptyListOfNodeIdsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var context = new Mock<INodeCacheContext>();
            var nodeCache = new LruNodeCache(context.Object, telemetry);

            // Act
            ArrayOf<INode> result = await nodeCache
                .GetReferencesAsync([], [], false, false, default)
                .ConfigureAwait(false);

            // Assert
            Assert.That(result.IsEmpty, Is.True);
        }

        [Test]
        public async Task GetReferencesAsyncShouldReturnReferencesFromCacheAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var referenceTypeId = new NodeId("referenceType", 0);
            var targetExpandedNodeId = new ExpandedNodeId("target", 0);
            var targetNodeId = new NodeId("target", 0);
            var expected = new List<ReferenceDescription>
            {
                new()
                {
                    NodeId = targetExpandedNodeId,
                    ReferenceTypeId = referenceTypeId,
                    IsForward = false
                }
            };
            var id = new NodeId("test", 0);

            var context = new Mock<INodeCacheContext>();
            var nsTable = new NamespaceTable();
            context.Setup(c => c.NamespaceUris).Returns(nsTable);

            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == id),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([.. expected])
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchNodesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<NodeId>>(n => n.Count == 1 && n[0] == targetNodeId),
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResultSet<Node>
                {
                    Results =
                    [
                        new VariableNode
                        {
                            NodeId = targetNodeId,
                            NodeClass = NodeClass.Variable
                        }
                    ],
                    Errors = [ServiceResult.Good]
                })
                .Verifiable(Times.Once);

            var nodeCache = new LruNodeCache(context.Object, telemetry);

            // Act
            ArrayOf<INode> result1 = await nodeCache
                .GetReferencesAsync(id, referenceTypeId, true, false, default)
                .ConfigureAwait(false);
            ArrayOf<INode> result2 = await nodeCache
                .GetReferencesAsync(id, referenceTypeId, false, false, default)
                .ConfigureAwait(false);

            // Assert
            Assert.That(result1.Count, Is.EqualTo(1));
            Assert.That(result2.IsEmpty, Is.True);
            // Act
            result1 = await nodeCache
                .GetReferencesAsync(id, referenceTypeId, true, false, default)
                .ConfigureAwait(false);
            result2 = await nodeCache
                .GetReferencesAsync(id, referenceTypeId, false, false, default)
                .ConfigureAwait(false);

            // Assert
            Assert.That(result1.Count, Is.EqualTo(1));
            Assert.That(result2.IsEmpty, Is.True);
            context.Verify();
        }

        [Test]
        public async Task GetReferencesAsyncWithMoreThanOneSubtypeShouldReturnReferencesFromCacheAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var referenceTypeId = new NodeId("referenceType", 0);
            var referenceSubTypeId = new NodeId("referenceSubType", 0);
            var targetExpandedNodeId = new ExpandedNodeId("target", 0);
            var targetNodeId = new NodeId("target", 0);
            var expected = new List<ReferenceDescription>
            {
                new()
                {
                    NodeId = targetExpandedNodeId,
                    ReferenceTypeId = referenceSubTypeId,
                    IsForward = false
                }
            };
            var id = new NodeId("test", 0);

            var context = new Mock<INodeCacheContext>();
            var nsTable = new NamespaceTable();
            context.Setup(c => c.NamespaceUris).Returns(nsTable);

            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                        It.Is<NodeId>(i => i == referenceTypeId),
                        It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                    [
                        new()
                        {
                            ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                            BrowseName = QualifiedName.From("HasSubtype"),
                            NodeId = new ExpandedNodeId(referenceSubTypeId)
                        }
                    ])
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchNodesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<NodeId>>(n => n.Count == 1 && n[0] == referenceSubTypeId),
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResultSet<Node>
                {
                    Results =
                    [
                        new ReferenceTypeNode
                        {
                            NodeId = referenceSubTypeId,
                            NodeClass = NodeClass.ReferenceType
                        }
                    ],
                    Errors = [ServiceResult.Good]
                })
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                        It.Is<NodeId>(i => i == referenceSubTypeId),
                        It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                    [
                        new()
                        {
                            ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                            IsForward = false,
                            BrowseName = QualifiedName.From("HasSuperType"),
                            NodeId = new ExpandedNodeId(referenceTypeId)
                        }
                    ])
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == id),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([.. expected])
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchNodesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<NodeId>>(n => n.Count == 1 && n[0] == targetNodeId),
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResultSet<Node>
                {
                    Results =
                    [
                        new VariableNode
                        {
                            NodeId = targetNodeId,
                            NodeClass = NodeClass.Variable
                        }
                    ],
                    Errors = [ServiceResult.Good]
                })
                .Verifiable(Times.Once);

            var nodeCache = new LruNodeCache(context.Object, telemetry);

            // Act
            ArrayOf<INode> result1 = await nodeCache
                .GetReferencesAsync(id, referenceTypeId, true, true, default)
                .ConfigureAwait(false);
            ArrayOf<INode> result2 = await nodeCache
                .GetReferencesAsync(id, referenceTypeId, false, true, default)
                .ConfigureAwait(false);

            // Assert
            Assert.That(result1.Count, Is.EqualTo(1));
            Assert.That(result2.IsEmpty, Is.True);
            // Act
            result1 = await nodeCache
                .GetReferencesAsync(id, referenceTypeId, true, true, default)
                .ConfigureAwait(false);
            result2 = await nodeCache
                .GetReferencesAsync(id, referenceTypeId, false, true, default)
                .ConfigureAwait(false);

            // Assert
            Assert.That(result1.Count, Is.EqualTo(1));
            Assert.That(result2.IsEmpty, Is.True);
            context.Verify();
        }

        [Test]
        public async Task GetReferencesAsyncWithSubtypesShouldReturnReferencesFromCacheAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var referenceTypeId = new NodeId("referenceType", 0);
            var targetExpandedNodeId = new ExpandedNodeId("target", 0);
            var targetNodeId = new NodeId("target", 0);
            var expected = new List<ReferenceDescription>
            {
                new()
                {
                    NodeId = targetExpandedNodeId,
                    ReferenceTypeId = referenceTypeId,
                    IsForward = false
                }
            };
            var id = new NodeId("test", 0);

            var context = new Mock<INodeCacheContext>();
            var nsTable = new NamespaceTable();
            context.Setup(c => c.NamespaceUris).Returns(nsTable);

            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                        It.Is<NodeId>(i => i == referenceTypeId),
                        It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == id),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([.. expected])
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchNodesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<NodeId>>(n => n.Count == 1 && n[0] == targetNodeId),
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResultSet<Node>
                {
                    Results =
                    [
                        new VariableNode
                        {
                            NodeId = targetNodeId,
                            NodeClass = NodeClass.Variable
                        }
                    ],
                    Errors = [ServiceResult.Good]
                })
                .Verifiable(Times.Once);

            var nodeCache = new LruNodeCache(context.Object, telemetry);

            // Act
            ArrayOf<INode> result1 = await nodeCache
                .GetReferencesAsync(id, referenceTypeId, true, true, default)
                .ConfigureAwait(false);
            ArrayOf<INode> result2 = await nodeCache
                .GetReferencesAsync(id, referenceTypeId, false, true, default)
                .ConfigureAwait(false);

            // Assert
            Assert.That(result1.Count, Is.EqualTo(1));
            Assert.That(result2.IsEmpty, Is.True);
            // Act
            result1 = await nodeCache
                .GetReferencesAsync(id, referenceTypeId, true, true, default)
                .ConfigureAwait(false);
            result2 = await nodeCache
                .GetReferencesAsync(id, referenceTypeId, false, true, default)
                .ConfigureAwait(false);

            // Assert
            Assert.That(result1.Count, Is.EqualTo(1));
            Assert.That(result2.IsEmpty, Is.True);
            context.Verify();
        }

        [Test]
        public async Task FindSuperTypeAsyncShouldHandleNoSupertypeAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var typeId = new NodeId("type", 0);
            var context = new Mock<INodeCacheContext>();

            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == typeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Once);
            var nodeCache = new LruNodeCache(context.Object, telemetry);

            // Act
            NodeId result = await nodeCache.FindSuperTypeAsync(typeId, default)
                .ConfigureAwait(false);

            // Assert
            Assert.That(result, Is.EqualTo(NodeId.Null));
            context.Verify();
        }

        [Test]
        public async Task FindSuperTypeAsyncShouldReturnSuperTypeAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var superTypeId = new NodeId("superType", 0);
            var subTypeId = new NodeId("subType", 0);
            var references = new List<ReferenceDescription>
            {
                new()
                {
                    NodeId = new ExpandedNodeId(superTypeId),
                    ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                    IsForward = false
                }
            };

            var context = new Mock<INodeCacheContext>();
            var nsTable = new NamespaceTable();
            context.Setup(c => c.NamespaceUris).Returns(nsTable);

            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == subTypeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([.. references])
                .Verifiable(Times.Once);
            var nodeCache = new LruNodeCache(context.Object, telemetry);

            // Act
            NodeId result = await nodeCache.FindSuperTypeAsync(subTypeId, default)
                .ConfigureAwait(false);

            // Assert
            Assert.That(result, Is.EqualTo(superTypeId));

            // Act
            result = await nodeCache.FindSuperTypeAsync(subTypeId, default).ConfigureAwait(false);
            // Assert
            Assert.That(result, Is.EqualTo(superTypeId));

            context.Verify();
        }

        [Test]
        public void GetValueAsyncShouldHandleErrors()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var id = new NodeId("test", 0);
            var context = new Mock<INodeCacheContext>();

            context
                .Setup(c => c.FetchValueAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadUnexpectedError))
                .Verifiable(Times.Once);
            var nodeCache = new LruNodeCache(context.Object, telemetry);

            // Act && Assert
            _ = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await nodeCache.GetValueAsync(id, default).ConfigureAwait(false));
            context.Verify();
        }

        [Test]
        public async Task GetValueAsyncShouldReturnValueFromCacheAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var expected = new DataValue(new Variant(123), StatusCodes.Good, DateTime.UtcNow);
            var id = new NodeId("test", 0);
            var context = new Mock<INodeCacheContext>();

            context
                .Setup(c => c.FetchValueAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == id),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(expected)
                .Verifiable(Times.Once);
            var nodeCache = new LruNodeCache(context.Object, telemetry);

            // Act
            DataValue result = await nodeCache.GetValueAsync(id, default).ConfigureAwait(false);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
            result = await nodeCache.GetValueAsync(id, default).ConfigureAwait(false);
            Assert.That(result, Is.EqualTo(expected));
            context.Verify();
        }

        [Test]
        public async Task GetValuesAsyncShouldHandleErrorsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            ArrayOf<NodeId> ids = [new("test1", 0), new("test2", 0)];
            var context = new Mock<INodeCacheContext>();

            context
                .Setup(c => c.FetchValuesAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<NodeId>>(),
                        It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResultSet<DataValue>
                {
                    Results = [new(), new()],
                    Errors = [new ServiceResult(StatusCodes.BadUnexpectedError), ServiceResult.Good]
                })
                .Verifiable(Times.Once);
            var nodeCache = new LruNodeCache(context.Object, telemetry);

            // Act
            ArrayOf<DataValue> result = await nodeCache.GetValuesAsync(ids, default)
                .ConfigureAwait(false);

            // Assert
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That((uint)result[0].StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That((uint)result[1].StatusCode, Is.EqualTo(StatusCodes.Good));
            context.Verify();
        }

        [Test]
        public async Task GetValuesAsyncShouldReturnValuesFromCacheAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            ArrayOf<DataValue> expected =
            [
                new(new Variant(123), StatusCodes.Good, DateTime.UtcNow),
                new(new Variant(456), StatusCodes.Good, DateTime.UtcNow)
            ];
            ArrayOf<NodeId> ids = [new("test1", 0), new("test2", 0)];
            var context = new Mock<INodeCacheContext>();

            context
                .Setup(c => c.FetchValuesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<NodeId>>(i => i.ToList().ToHashSet().SetEquals(ids.ToList().ToHashSet())),
                        It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResultSet<DataValue>
                {
                    Results = expected,
                    Errors = [ServiceResult.Good, ServiceResult.Good]
                })
                .Verifiable(Times.Once);
            var nodeCache = new LruNodeCache(context.Object, telemetry);

            // Act
            ArrayOf<DataValue> result = await nodeCache.GetValuesAsync(ids, default)
                .ConfigureAwait(false);

            // Assert
            Assert.That(result, Is.EqualTo(expected));

            result = await nodeCache.GetValuesAsync(ids, default).ConfigureAwait(false);
            Assert.That(result, Is.EqualTo(expected));
            context.Verify();
        }

        [Test]
        public async Task GetValuesAsyncShouldReturnValuesFromCacheButHonorStatusOfReadAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            ArrayOf<DataValue> expected =
            [
                new(new Variant(123), StatusCodes.Good, DateTime.UtcNow),
                new(new Variant(456), StatusCodes.Good, DateTime.UtcNow)
            ];
            ArrayOf<NodeId> ids = [new("test1", 0), new("test2", 0)];
            var context = new Mock<INodeCacheContext>();

            context
                .Setup(c => c.FetchValuesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<NodeId>>(i => i.ToList().ToHashSet().SetEquals(ids.ToList().ToHashSet())),
                        It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResultSet<DataValue>
                {
                    Results = expected,
                    Errors = [ServiceResult.Good, new ServiceResult(StatusCodes.Bad)]
                })
                .Verifiable(Times.Once);
            var nodeCache = new LruNodeCache(context.Object, telemetry);

            // Act
            ArrayOf<DataValue> result = await nodeCache.GetValuesAsync(ids, default)
                .ConfigureAwait(false);

            // Assert
            Assert.That(result[0], Is.EqualTo(expected[0]));
            Assert.That((uint)result[1].StatusCode, Is.EqualTo(StatusCodes.Bad));
            Assert.That(result[1].WrappedValue, Is.EqualTo(expected[1].WrappedValue));

            context
                .Setup(c => c.FetchValuesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<NodeId>>(i => i.Count == 1 && i[0] == ids[1]),
                        It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResultSet<DataValue>
                {
                    Results = [expected[1]],
                    Errors = [ServiceResult.Good]
                })
                .Verifiable(Times.Once);
            result = await nodeCache.GetValuesAsync(ids, default).ConfigureAwait(false);
            context.Verify();
        }

        [Test]
        public async Task IsTypeOfShouldHandleNoReferencesAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var superTypeId = new NodeId("superType", 0);
            var subTypeId = new NodeId("subType", 0);
            var context = new Mock<INodeCacheContext>();

            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == subTypeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i != subTypeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Never);
            var nodeCache = new LruNodeCache(context.Object, telemetry);

            // Act
            bool result = await nodeCache.IsTypeOfAsync(subTypeId, superTypeId, default)
                .ConfigureAwait(false);

            // Assert
            Assert.That(result, Is.False);
            context.Verify();
        }

        [Test]
        public async Task IsTypeOfShouldReturnTrueForSuperTypeAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var superTypeId = new NodeId("superType", 0);
            var subTypeId = new NodeId("subType", 0);
            var references = new List<ReferenceDescription>
            {
                new()
                {
                    NodeId = new ExpandedNodeId(superTypeId),
                    ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                    IsForward = false
                }
            };

            var context = new Mock<INodeCacheContext>();
            var nsTable = new NamespaceTable();
            context.Setup(c => c.NamespaceUris).Returns(nsTable);

            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == subTypeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([.. references])
                .Verifiable(Times.Once);
            var nodeCache = new LruNodeCache(context.Object, telemetry);

            // Act
            bool result = await nodeCache.IsTypeOfAsync(subTypeId, superTypeId, default)
                .ConfigureAwait(false);

            // Assert
            Assert.That(result, Is.True);

            // Act
            result = await nodeCache.IsTypeOfAsync(subTypeId, superTypeId, default)
                .ConfigureAwait(false);

            // Assert
            Assert.That(result, Is.True);
            context.Verify();
        }

        [Test]
        public async Task LoadTypeHierarchyAsyncShouldHandleNoSubtypesAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var typeId = new NodeId("type", 0);
            var context = new Mock<INodeCacheContext>();
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == typeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Once);
            var nodeCache = new LruNodeCache(context.Object, telemetry);

            // Act
            await nodeCache.LoadTypeHierarchyAsync([typeId], default).ConfigureAwait(false);

            // Assert
            context.Verify();
        }

        [Test]
        public async Task LoadTypeHierarchyAsyncShouldLoadTypeHierarchyAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var typeId = new NodeId("type", 0);
            var subTypeId = new NodeId("subType", 0);
            ArrayOf<ReferenceDescription> references =
            [
                new()
                {
                    NodeId = new ExpandedNodeId(subTypeId),
                    ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                    IsForward = true
                }
            ];

            var context = new Mock<INodeCacheContext>();
            var nsTable = new NamespaceTable();
            context.Setup(c => c.NamespaceUris).Returns(nsTable);
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == typeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(references)
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == subTypeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchNodesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<NodeId>>(n => n.Count == 1 && n[0] == subTypeId),
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResultSet<Node>
                {
                    Results =
                    [
                        new DataTypeNode
                        {
                            NodeId = subTypeId,
                            NodeClass = NodeClass.DataType
                        }
                    ],
                    Errors = [ServiceResult.Good]
                })
                .Verifiable(Times.Once);
            var nodeCache = new LruNodeCache(context.Object, telemetry);

            // Act
            await nodeCache.LoadTypeHierarchyAsync([typeId], default).ConfigureAwait(false);
            await nodeCache.LoadTypeHierarchyAsync([typeId], default).ConfigureAwait(false);
            await nodeCache.LoadTypeHierarchyAsync([typeId], default).ConfigureAwait(false);

            // Assert
            context.Verify();
        }

        [Test]
        public async Task MetricsAreEmittedViaTelemetryContextAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange — capture instruments published on the OPC UA client meter
            var capturedHits = new Dictionary<string, long>();
            var capturedMisses = new Dictionary<string, long>();
            var capturedSize = new Dictionary<string, long>();

            using var listener = new MeterListener
            {
                InstrumentPublished = (instrument, l) =>
                {
                    if (instrument.Name.StartsWith("opcua.client.nodecache.", StringComparison.Ordinal))
                    {
                        l.EnableMeasurementEvents(instrument);
                    }
                }
            };
            listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
            {
                string cacheTag = string.Empty;
                foreach (KeyValuePair<string, object?> kv in tags)
                {
                    if (kv.Key == "cache" && kv.Value is string s)
                    {
                        cacheTag = s;
                        break;
                    }
                }
                switch (instrument.Name)
                {
                    case "opcua.client.nodecache.hits":
                        capturedHits[cacheTag] = value;
                        break;
                    case "opcua.client.nodecache.misses":
                        capturedMisses[cacheTag] = value;
                        break;
                    case "opcua.client.nodecache.size":
                        capturedSize[cacheTag] = value;
                        break;
                }
            });
            listener.Start();

            var id = new NodeId("metricsNode", 0);
            var context = new Mock<INodeCacheContext>();
            context
                .Setup(c => c.FetchNodeAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == id),
                    NodeClass.Unspecified,
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Node { NodeId = id });

            using var nodeCache = new LruNodeCache(context.Object, telemetry);

            // Act — first call is a miss (fetches), second call is a hit
            _ = await nodeCache.GetNodeAsync(id, default).ConfigureAwait(false);
            _ = await nodeCache.GetNodeAsync(id, default).ConfigureAwait(false);

            listener.RecordObservableInstruments();

            // Assert — at least one miss and one hit on the nodes cache, size == 1
            Assert.That(capturedMisses.ContainsKey("nodes"), Is.True);
            Assert.That(capturedHits.ContainsKey("nodes"), Is.True);
            Assert.That(capturedSize.ContainsKey("nodes"), Is.True);
            Assert.That(capturedMisses["nodes"], Is.GreaterThanOrEqualTo(1));
            Assert.That(capturedHits["nodes"], Is.GreaterThanOrEqualTo(1));
            Assert.That(capturedSize["nodes"], Is.EqualTo(1));
        }
    }
}
