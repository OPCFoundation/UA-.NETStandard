// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

#if NET8_0_OR_GREATER && !NET_STANDARD_TESTS

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

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
                    It.IsAny<IReadOnlyList<NodeId>>(),
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
            IReadOnlyList<INode> result = await nodeCache.GetNodesAsync([id], default)
                .ConfigureAwait(false);

            // Assert
            Assert.AreEqual(1, result.Count);
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

            // Act
            BuiltInType result = await nodeCache.GetBuiltInTypeAsync(datatypeId, default)
                .ConfigureAwait(false);

            // Assert
            Assert.AreEqual(BuiltInType.Null, result);
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
            Assert.AreEqual(BuiltInType.Int32, result);
        }

        [Test]
        public async Task GetNodeAsyncShouldHandleEmptyListAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var context = new Mock<INodeCacheContext>();
            var nodeCache = new LruNodeCache(context.Object, telemetry);

            // Act
            IReadOnlyList<INode> result = await nodeCache.GetNodesAsync([], default)
                .ConfigureAwait(false);

            // Assert
            Assert.IsEmpty(result);
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
            Assert.AreEqual(expected, result);
            result = await nodeCache.GetNodeAsync(id, default).ConfigureAwait(false);
            Assert.AreEqual(expected, result);
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
                    => ValueTask.FromResult(expected))
                .Verifiable(Times.Once);
            var nodeCache = new LruNodeCache(context.Object, telemetry);

            INode result = await nodeCache.GetNodeAsync(id, default).ConfigureAwait(false);
            Assert.AreEqual(expected, result);
            result = await nodeCache.GetNodeAsync(id, default).ConfigureAwait(false);
            Assert.AreEqual(expected, result);
            result = await nodeCache.GetNodeAsync(id, default).ConfigureAwait(false);
            Assert.AreEqual(expected, result);
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
                    => ValueTask.FromException<Node>(new ServiceResultException()))
                .Verifiable(Times.Exactly(3));
            var nodeCache = new LruNodeCache(context.Object, telemetry);

            _ = NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                await nodeCache.GetNodeAsync(id, default).ConfigureAwait(false));
            _ = NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                await nodeCache.GetNodeAsync(id, default).ConfigureAwait(false));
            _ = NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                await nodeCache.GetNodeAsync(id, default).ConfigureAwait(false));
            context.Verify();
        }

        [Test]
        public async Task GetNodeWithBrowsePathAsyncShouldHandleInvalidBrowsePathAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var id = new NodeId("test", 0);
            var browsePath = new QualifiedNameCollection { new QualifiedName("invalid") };
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
            INode result = await nodeCache.GetNodeWithBrowsePathAsync(id, browsePath, default)
                .ConfigureAwait(false);

            // Assert
            Assert.Null(result);
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
                BrowseName = new QualifiedName("child"),
                NodeId = id,
                NodeClass = NodeClass.Variable
            };
            var browsePath = new QualifiedNameCollection { new QualifiedName("child") };
            var references = new List<ReferenceDescription>
            {
                new()
                {
                    NodeId = new ExpandedNodeId(id),
                    BrowseName = new QualifiedName("child"),
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
                    It.Is<IReadOnlyList<NodeId>>(n => n.Count == 1 && n[0] == id),
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
            INode result = await nodeCache.GetNodeWithBrowsePathAsync(id, browsePath, default)
                .ConfigureAwait(false);

            // Assert
            Assert.AreEqual(expected, result);

            // Act
            result = await nodeCache.GetNodeWithBrowsePathAsync(id, browsePath, default)
                .ConfigureAwait(false);

            // Assert
            Assert.AreEqual(expected, result);
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
            var browsePath = new QualifiedNameCollection
            {
                new QualifiedName("child"),
                new QualifiedName("grandChild")
            };

            var rootReferences = new List<ReferenceDescription>
            {
                new()
                {
                    NodeId = new ExpandedNodeId(childId),
                    BrowseName = new QualifiedName("child"),
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IsForward = true
                }
            };
            var childNode = new VariableNode
            {
                BrowseName = new QualifiedName("child"),
                NodeId = childId,
                NodeClass = NodeClass.Variable
            };
            var childReferences = new List<ReferenceDescription>
            {
                new()
                {
                    NodeId = new ExpandedNodeId(grandChildId),
                    BrowseName = new QualifiedName("grandChild"),
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IsForward = true
                }
            };
            var expected = new VariableNode
            {
                BrowseName = new QualifiedName("grandChild"),
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
                    It.Is<IReadOnlyList<NodeId>>(n => n.Count == 1 && n[0] == childId),
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
                    It.Is<IReadOnlyList<NodeId>>(n => n.Count == 1 && n[0] == grandChildId),
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
            INode result = await nodeCache
                .GetNodeWithBrowsePathAsync(rootId, browsePath, default)
                .ConfigureAwait(false);

            // Assert
            Assert.AreEqual(expected, result);

            // Act
            result = await nodeCache.GetNodeWithBrowsePathAsync(rootId, browsePath, default)
                .ConfigureAwait(false);

            // Assert
            Assert.AreEqual(expected, result);
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
            IReadOnlyList<INode> result = await nodeCache
                .GetReferencesAsync([], [], false, false, default)
                .ConfigureAwait(false);

            // Assert
            Assert.IsEmpty(result);
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
                    It.Is<IReadOnlyList<NodeId>>(n => n.Count == 1 && n[0] == targetNodeId),
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
            IReadOnlyList<INode> result1 = await nodeCache
                .GetReferencesAsync(id, referenceTypeId, true, false, default)
                .ConfigureAwait(false);
            IReadOnlyList<INode> result2 = await nodeCache
                .GetReferencesAsync(id, referenceTypeId, false, false, default)
                .ConfigureAwait(false);

            // Assert
            Assert.AreEqual(1, result1.Count);
            Assert.IsEmpty(result2);
            // Act
            result1 = await nodeCache
                .GetReferencesAsync(id, referenceTypeId, true, false, default)
                .ConfigureAwait(false);
            result2 = await nodeCache
                .GetReferencesAsync(id, referenceTypeId, false, false, default)
                .ConfigureAwait(false);

            // Assert
            Assert.AreEqual(1, result1.Count);
            Assert.IsEmpty(result2);
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
                            BrowseName = new QualifiedName("HasSubtype"),
                            NodeId = new ExpandedNodeId(referenceSubTypeId)
                        }
                    ])
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchNodesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<IReadOnlyList<NodeId>>(n => n.Count == 1 && n[0] == referenceSubTypeId),
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
                            BrowseName = new QualifiedName("HasSuperType"),
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
                    It.Is<IReadOnlyList<NodeId>>(n => n.Count == 1 && n[0] == targetNodeId),
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
            IReadOnlyList<INode> result1 = await nodeCache
                .GetReferencesAsync(id, referenceTypeId, true, true, default)
                .ConfigureAwait(false);
            IReadOnlyList<INode> result2 = await nodeCache
                .GetReferencesAsync(id, referenceTypeId, false, true, default)
                .ConfigureAwait(false);

            // Assert
            Assert.AreEqual(1, result1.Count);
            Assert.IsEmpty(result2);
            // Act
            result1 = await nodeCache
                .GetReferencesAsync(id, referenceTypeId, true, true, default)
                .ConfigureAwait(false);
            result2 = await nodeCache
                .GetReferencesAsync(id, referenceTypeId, false, true, default)
                .ConfigureAwait(false);

            // Assert
            Assert.AreEqual(1, result1.Count);
            Assert.IsEmpty(result2);
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
                    It.Is<IReadOnlyList<NodeId>>(n => n.Count == 1 && n[0] == targetNodeId),
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
            IReadOnlyList<INode> result1 = await nodeCache
                .GetReferencesAsync(id, referenceTypeId, true, true, default)
                .ConfigureAwait(false);
            IReadOnlyList<INode> result2 = await nodeCache
                .GetReferencesAsync(id, referenceTypeId, false, true, default)
                .ConfigureAwait(false);

            // Assert
            Assert.AreEqual(1, result1.Count);
            Assert.IsEmpty(result2);
            // Act
            result1 = await nodeCache
                .GetReferencesAsync(id, referenceTypeId, true, true, default)
                .ConfigureAwait(false);
            result2 = await nodeCache
                .GetReferencesAsync(id, referenceTypeId, false, true, default)
                .ConfigureAwait(false);

            // Assert
            Assert.AreEqual(1, result1.Count);
            Assert.IsEmpty(result2);
            context.Verify();
        }

        [Test]
        public async Task GetSuperTypeAsyncShouldHandleNoSupertypeAsync()
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
            NodeId result = await nodeCache.GetSuperTypeAsync(typeId, default)
                .ConfigureAwait(false);

            // Assert
            Assert.AreEqual(NodeId.Null, result);
            context.Verify();
        }

        [Test]
        public async Task GetSuperTypeAsyncShouldReturnSuperTypeAsync()
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
            NodeId result = await nodeCache.GetSuperTypeAsync(subTypeId, default)
                .ConfigureAwait(false);

            // Assert
            Assert.AreEqual(superTypeId, result);

            // Act
            result = await nodeCache.GetSuperTypeAsync(subTypeId, default).ConfigureAwait(false);
            // Assert
            Assert.AreEqual(superTypeId, result);

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
            _ = NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
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
            Assert.AreEqual(expected, result);
            result = await nodeCache.GetValueAsync(id, default).ConfigureAwait(false);
            Assert.AreEqual(expected, result);
            context.Verify();
        }

        [Test]
        public async Task GetValuesAsyncShouldHandleErrorsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var ids = new List<NodeId> { new("test1", 0), new("test2", 0) };
            var context = new Mock<INodeCacheContext>();

            context
                .Setup(c => c.FetchValuesAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<IReadOnlyList<NodeId>>(),
                        It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResultSet<DataValue>
                {
                    Results = [new(), new()],
                    Errors = [new ServiceResult(StatusCodes.BadUnexpectedError), ServiceResult.Good]
                })
                .Verifiable(Times.Once);
            var nodeCache = new LruNodeCache(context.Object, telemetry);

            // Act
            IReadOnlyList<DataValue> result = await nodeCache.GetValuesAsync(ids, default)
                .ConfigureAwait(false);

            // Assert
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(StatusCodes.BadUnexpectedError, (uint)result[0].StatusCode);
            Assert.AreEqual(StatusCodes.Good, (uint)result[1].StatusCode);
            context.Verify();
        }

        [Test]
        public async Task GetValuesAsyncShouldReturnValuesFromCacheAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var expected = new List<DataValue>
            {
                new(new Variant(123), StatusCodes.Good, DateTime.UtcNow),
                new(new Variant(456), StatusCodes.Good, DateTime.UtcNow)
            };
            var ids = new List<NodeId> { new("test1", 0), new("test2", 0) };
            var context = new Mock<INodeCacheContext>();

            context
                .Setup(c => c.FetchValuesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<IReadOnlyList<NodeId>>(i => i.ToHashSet().SetEquals(ids)),
                        It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResultSet<DataValue>
                {
                    Results = expected,
                    Errors = [ServiceResult.Good, ServiceResult.Good]
                })
                .Verifiable(Times.Once);
            var nodeCache = new LruNodeCache(context.Object, telemetry);

            // Act
            IReadOnlyList<DataValue> result = await nodeCache.GetValuesAsync(ids, default)
                .ConfigureAwait(false);

            // Assert
            Assert.AreEqual(expected, result);

            result = await nodeCache.GetValuesAsync(ids, default).ConfigureAwait(false);
            Assert.AreEqual(expected, result);
            context.Verify();
        }

        [Test]
        public async Task GetValuesAsyncShouldReturnValuesFromCacheButHonorStatusOfReadAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var expected = new List<DataValue>
            {
                new(new Variant(123), StatusCodes.Good, DateTime.UtcNow),
                new(new Variant(456), StatusCodes.Good, DateTime.UtcNow)
            };
            var ids = new List<NodeId> { new("test1", 0), new("test2", 0) };
            var context = new Mock<INodeCacheContext>();

            context
                .Setup(c => c.FetchValuesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<IReadOnlyList<NodeId>>(i => i.ToHashSet().SetEquals(ids)),
                        It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResultSet<DataValue>
                {
                    Results = expected,
                    Errors = [ServiceResult.Good, new ServiceResult(StatusCodes.Bad)]
                })
                .Verifiable(Times.Once);
            var nodeCache = new LruNodeCache(context.Object, telemetry);

            // Act
            IReadOnlyList<DataValue> result = await nodeCache.GetValuesAsync(ids, default)
                .ConfigureAwait(false);

            // Assert
            Assert.AreEqual(expected[0], result[0]);
            Assert.AreEqual(StatusCodes.Bad, (uint)result[1].StatusCode);
            Assert.AreEqual(expected[1].Value, result[1].Value);

            context
                .Setup(c => c.FetchValuesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<IReadOnlyList<NodeId>>(i => i.Count == 1 && i[0] == ids[1]),
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
        public void IsTypeOfShouldHandleNoReferences()
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
            var nodeCache = new LruNodeCache(context.Object, telemetry);

            // Act
            bool result = nodeCache.IsTypeOf(subTypeId, superTypeId);

            // Assert
            Assert.False(result);
            context.Verify();
        }

        [Test]
        public void IsTypeOfShouldReturnTrueForSuperType()
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
            bool result = nodeCache.IsTypeOf(subTypeId, superTypeId);

            // Assert
            Assert.True(result);

            // Act
            result = nodeCache.IsTypeOf(subTypeId, superTypeId);

            // Assert
            Assert.True(result);
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
            var references = new ReferenceDescriptionCollection
            {
                new()
                {
                    NodeId = new ExpandedNodeId(subTypeId),
                    ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                    IsForward = true
                }
            };

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
                    It.Is<IReadOnlyList<NodeId>>(n => n.Count == 1 && n[0] == subTypeId),
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
    }
}
#endif
