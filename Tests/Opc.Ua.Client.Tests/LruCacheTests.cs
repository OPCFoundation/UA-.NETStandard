// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

#if NET6_0_OR_GREATER

using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using NUnit.Framework;

namespace Opc.Ua.Client.Tests
{
    [TestFixture, Category("Client")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    public sealed class LruCacheTests
    {
        [Test]
        public async Task FetchRemainingNodesAsyncShouldHandleErrorsAsync()
        {
            // Arrange
            var id = new NodeId("test", 0);
            var context = new Mock<ISession>();

            context
                .Setup(c => c.ReadNodesAsync(
                    It.IsAny<IList<NodeId>>(),
                    NodeClass.Unspecified,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((new List<Node> { new() }, new[] { new ServiceResult(StatusCodes.BadUnexpectedError) }))
                .Verifiable(Times.Once);
            var nodeCache = new LruNodeCache(context.Object);

            // Act
            IReadOnlyList<INode> result = await nodeCache.GetNodesAsync([id], default);

            // Assert
            Assert.AreEqual(1, result.Count);
            context.Verify();
        }

        [Test]
        public async Task GetBuiltInTypeAsyncShouldHandleUnknownTypeAsync()
        {
            // Arrange
            var datatypeId = new NodeId("unknownType", 0);
            var context = new Mock<ISession>();
            var nodeCache = new LruNodeCache(context.Object);

            context
                .Setup(c => c.FetchReferencesAsync(
                    It.Is<NodeId>(i => i == datatypeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Once);

            // Act
            BuiltInType result = await nodeCache.GetBuiltInTypeAsync(datatypeId, default);

            // Assert
            Assert.AreEqual(BuiltInType.Null, result);
            context.Verify();
        }

        [Test]
        public async Task GetBuiltInTypeAsyncShouldReturnBuiltInTypeAsync()
        {
            // Arrange
            var datatypeId = new NodeId((uint)BuiltInType.Int32, 0);
            var context = new Mock<ISession>();
            var nodeCache = new LruNodeCache(context.Object);

            // Act
            BuiltInType result = await nodeCache.GetBuiltInTypeAsync(datatypeId, default);

            // Assert
            Assert.AreEqual(BuiltInType.Int32, result);
        }

        [Test]
        public async Task GetNodeAsyncShouldHandleEmptyListAsync()
        {
            // Arrange
            var context = new Mock<ISession>();
            var nodeCache = new LruNodeCache(context.Object);

            // Act
            IReadOnlyList<INode> result = await nodeCache.GetNodesAsync([], default);

            // Assert
            Assert.IsEmpty(result);
        }

        [Test]
        public async Task GetNodeAsyncShouldReturnNodeFromCacheAsync()
        {
            // Arrange
            var expected = new Node();
            var id = new NodeId("test", 0);
            var context = new Mock<ISession>();

            context
                .Setup(c => c.ReadNodeAsync(
                    It.Is<NodeId>(i => i == id),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(expected)
                .Verifiable(Times.Once);
            var nodeCache = new LruNodeCache(context.Object);

            // Act
            INode result = await nodeCache.GetNodeAsync(id, default);

            // Assert
            Assert.AreEqual(expected, result);
            result = await nodeCache.GetNodeAsync(id, default);
            Assert.AreEqual(expected, result);
            context.Verify();
        }

        [Test]
        public async Task GetNodeTestAsync()
        {
            var expected = new Node();
            var id = new NodeId("test", 0);
            var context = new Mock<ISession>();

            context
                .Setup(c => c.ReadNodeAsync(
                    It.Is<NodeId>(i => i == id),
                    It.IsAny<CancellationToken>()))
                .Returns<NodeId, CancellationToken>((nodeId, ct)
                    => Task.FromResult(expected))
                .Verifiable(Times.Once);
            var nodeCache = new LruNodeCache(context.Object);

            INode result = await nodeCache.GetNodeAsync(id, default);
            Assert.AreEqual(expected, result);
            result = await nodeCache.GetNodeAsync(id, default);
            Assert.AreEqual(expected, result);
            result = await nodeCache.GetNodeAsync(id, default);
            Assert.AreEqual(expected, result);
            context.Verify();
        }

        [Test]
        public void GetNodeThrowsTest()
        {
            var expected = new Node();
            var id = new NodeId("test", 0);
            var context = new Mock<ISession>();

            context
                .Setup(c => c.ReadNodeAsync(
                    It.Is<NodeId>(i => i == id),
                    It.IsAny<CancellationToken>()))
                .Returns<NodeId, CancellationToken>((nodeId, ct)
                    => Task.FromException<Node>(new ServiceResultException()))
                .Verifiable(Times.Exactly(3));
            var nodeCache = new LruNodeCache(context.Object);

            _ = Assert.ThrowsAsync<ServiceResultException>(
                async () => await nodeCache.GetNodeAsync(id, default));
            _ = Assert.ThrowsAsync<ServiceResultException>(
                async () => await nodeCache.GetNodeAsync(id, default));
            _ = Assert.ThrowsAsync<ServiceResultException>(
                async () => await nodeCache.GetNodeAsync(id, default));
            context.Verify();
        }

        [Test]
        public async Task GetNodeWithBrowsePathAsyncShouldHandleInvalidBrowsePathAsync()
        {
            // Arrange
            var id = new NodeId("test", 0);
            var browsePath = new QualifiedNameCollection { new QualifiedName("invalid") };
            var context = new Mock<ISession>();

            context
                .Setup(c => c.FetchReferencesAsync(
                    It.Is<NodeId>(i => i == ReferenceTypeIds.HierarchicalReferences),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.Is<NodeId>(i => i == id),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Once);
            var nodeCache = new LruNodeCache(context.Object);

            // Act
            INode result = await nodeCache.GetNodeWithBrowsePathAsync(id, browsePath, default);

            // Assert
            Assert.Null(result);
            context.Verify();
        }

        [Test]
        public async Task GetNodeWithBrowsePathAsyncShouldReturnNodeAsync()
        {
            // Arrange
            var id = new NodeId("test", 0);
            var expected = new VariableNode {
                BrowseName = new QualifiedName("child"),
                NodeId = id,
                NodeClass = NodeClass.Variable
            };
            var browsePath = new QualifiedNameCollection { new QualifiedName("child") };
            var references = new List<ReferenceDescription>
            {
                new ()
                {
                    NodeId = new ExpandedNodeId(id),
                    BrowseName = new QualifiedName("child"),
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IsForward = true
                }
            };
            var context = new Mock<ISession>();
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.Is<NodeId>(i => i == ReferenceTypeIds.HierarchicalReferences),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.Is<NodeId>(i => i == id),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([.. references])
                .Verifiable(Times.Once);
            context
                .Setup(c => c.ReadNodesAsync(
                    It.Is<IList<NodeId>>(n => n.Count == 1 && n[0] == id),
                    NodeClass.Unspecified,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((new List<Node> { expected }, new[] { ServiceResult.Good }))
                .Verifiable(Times.Once);

            var nodeCache = new LruNodeCache(context.Object);

            // Act
            INode result = await nodeCache.GetNodeWithBrowsePathAsync(id,
                browsePath, default);

            // Assert
            Assert.AreEqual(expected, result);

            // Act
            result = await nodeCache.GetNodeWithBrowsePathAsync(id,
                browsePath, default);

            // Assert
            Assert.AreEqual(expected, result);
            context.Verify();
        }

        [Test]
        public async Task GetNodeWithBrowsePathAsyncShouldReturnNodeWithMultipleElementsAsync()
        {
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
                new  ()
                {
                    NodeId = new ExpandedNodeId(childId),
                    BrowseName = new QualifiedName("child"),
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IsForward = true
                }
            };
            var childNode = new VariableNode {
                BrowseName = new QualifiedName("child"),
                NodeId = childId,
                NodeClass = NodeClass.Variable
            };
            var childReferences = new List<ReferenceDescription>
            {
                new ()
                {
                    NodeId = new ExpandedNodeId(grandChildId),
                    BrowseName = new QualifiedName("grandChild"),
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IsForward = true
                }
            };
            var expected = new VariableNode {
                BrowseName = new QualifiedName("grandChild"),
                NodeId = grandChildId,
                NodeClass = NodeClass.Variable
            };

            var context = new Mock<ISession>();

            context
                .Setup(c => c.FetchReferencesAsync(
                    It.Is<NodeId>(i => i == ReferenceTypeIds.HierarchicalReferences),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.Is<NodeId>(i => i == rootId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([.. rootReferences])
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.Is<NodeId>(i => i == childId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([.. childReferences])
                .Verifiable(Times.Once);
            context
                .Setup(c => c.ReadNodesAsync(
                    It.Is<IList<NodeId>>(n => n.Count == 1 && n[0] == childId),
                    NodeClass.Unspecified,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((new List<Node> { childNode }, new[] { ServiceResult.Good }))
                .Verifiable(Times.Once);
            context
                .Setup(c => c.ReadNodesAsync(
                    It.Is<IList<NodeId>>(n => n.Count == 1 && n[0] == grandChildId),
                    NodeClass.Unspecified,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((new List<Node> { expected }, new[] { ServiceResult.Good }))
                .Verifiable(Times.Once);

            var nodeCache = new LruNodeCache(context.Object);

            // Act
            INode result = await nodeCache.GetNodeWithBrowsePathAsync(rootId, browsePath, default);

            // Assert
            Assert.AreEqual(expected, result);

            // Act
            result = await nodeCache.GetNodeWithBrowsePathAsync(rootId, browsePath, default);

            // Assert
            Assert.AreEqual(expected, result);
            context.Verify();
        }

        [Test]
        public async Task GetReferencesAsyncShouldHandleEmptyListOfNodeIdsAsync()
        {
            // Arrange
            var context = new Mock<ISession>();
            var nodeCache = new LruNodeCache(context.Object);

            // Act
            IReadOnlyList<INode> result = await nodeCache.GetReferencesAsync([], [], false, false, default);

            // Assert
            Assert.IsEmpty(result);
        }

        [Test]
        public async Task GetReferencesAsyncShouldReturnReferencesFromCacheAsync()
        {
            // Arrange
            var referenceTypeId = new NodeId("referenceType", 0);
            var targetExpandedNodeId = new ExpandedNodeId("target", 0);
            var targetNodeId = new NodeId("target", 0);
            var expected = new List<ReferenceDescription>
            {
                new ()
                {
                    NodeId = targetExpandedNodeId,
                    ReferenceTypeId = referenceTypeId,
                    IsForward = false
                }
            };
            var id = new NodeId("test", 0);
            var context = new Mock<ISession>();

            context
                .Setup(c => c.FetchReferencesAsync(
                    It.Is<NodeId>(i => i == id),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([.. expected])
                .Verifiable(Times.Once);
            context
                .Setup(c => c.ReadNodesAsync(
                    It.Is<IList<NodeId>>(n => n.Count == 1 && n[0] == targetNodeId),
                    NodeClass.Unspecified,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((new List<Node>
                    {
                        new VariableNode
                        {
                            NodeId = targetNodeId,
                            NodeClass = NodeClass.Variable
                        }
                    },
                    new[] { ServiceResult.Good }))
                .Verifiable(Times.Once);

            var nodeCache = new LruNodeCache(context.Object);

            // Act
            IReadOnlyList<INode> result1 = await nodeCache.GetReferencesAsync(id,
                referenceTypeId, true, false, default);
            IReadOnlyList<INode> result2 = await nodeCache.GetReferencesAsync(id,
                referenceTypeId, false, false, default);

            // Assert
            Assert.AreEqual(1, result1.Count);
            Assert.IsEmpty(result2);
            // Act
            result1 = await nodeCache.GetReferencesAsync(id,
                referenceTypeId, true, false, default);
            result2 = await nodeCache.GetReferencesAsync(id,
                referenceTypeId, false, false, default);

            // Assert
            Assert.AreEqual(1, result1.Count);
            Assert.IsEmpty(result2);
            context.Verify();
        }

        [Test]
        public async Task GetReferencesAsyncWithMoreThanOneSubtypeShouldReturnReferencesFromCacheAsync()
        {
            // Arrange
            var referenceTypeId = new NodeId("referenceType", 0);
            var referenceSubTypeId = new NodeId("referenceSubType", 0);
            var targetExpandedNodeId = new ExpandedNodeId("target", 0);
            var targetNodeId = new NodeId("target", 0);
            var expected = new List<ReferenceDescription>
            {
                new ()
                {
                    NodeId = targetExpandedNodeId,
                    ReferenceTypeId = referenceSubTypeId,
                    IsForward = false
                }
            };
            var id = new NodeId("test", 0);
            var context = new Mock<ISession>();

            context
                .Setup(c => c.FetchReferencesAsync(
                    It.Is<NodeId>(i => i == referenceTypeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new ()
                    {
                        ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                        BrowseName = new QualifiedName("HasSubtype"),
                        NodeId = new ExpandedNodeId(referenceSubTypeId)
                    }
                ])
                .Verifiable(Times.Once);
            context
                .Setup(c => c.ReadNodesAsync(
                    It.Is<IList<NodeId>>(n => n.Count == 1 && n[0] == referenceSubTypeId),
                    NodeClass.Unspecified,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((new List<Node>
                    {
                        new ReferenceTypeNode
                        {
                            NodeId = referenceSubTypeId,
                            NodeClass = NodeClass.ReferenceType
                        }
                    },
                    new[] { ServiceResult.Good }))
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.Is<NodeId>(i => i == referenceSubTypeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new ()
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
                    It.Is<NodeId>(i => i == id),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([.. expected])
                .Verifiable(Times.Once);
            context
                .Setup(c => c.ReadNodesAsync(
                    It.Is<IList<NodeId>>(n => n.Count == 1 && n[0] == targetNodeId),
                    NodeClass.Unspecified,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((new List<Node>
                    {
                        new VariableNode
                        {
                            NodeId = targetNodeId,
                            NodeClass = NodeClass.Variable
                        }
                    },
                    new[] { ServiceResult.Good }))
                .Verifiable(Times.Once);

            var nodeCache = new LruNodeCache(context.Object);

            // Act
            IReadOnlyList<INode> result1 = await nodeCache.GetReferencesAsync(id,
                referenceTypeId, true, true, default);
            IReadOnlyList<INode> result2 = await nodeCache.GetReferencesAsync(id,
                referenceTypeId, false, true, default);

            // Assert
            Assert.AreEqual(1, result1.Count);
            Assert.IsEmpty(result2);
            // Act
            result1 = await nodeCache.GetReferencesAsync(id,
                referenceTypeId, true, true, default);
            result2 = await nodeCache.GetReferencesAsync(id,
                referenceTypeId, false, true, default);

            // Assert
            Assert.AreEqual(1, result1.Count);
            Assert.IsEmpty(result2);
            context.Verify();
        }

        [Test]
        public async Task GetReferencesAsyncWithSubtypesShouldReturnReferencesFromCacheAsync()
        {
            // Arrange
            var referenceTypeId = new NodeId("referenceType", 0);
            var targetExpandedNodeId = new ExpandedNodeId("target", 0);
            var targetNodeId = new NodeId("target", 0);
            var expected = new List<ReferenceDescription>
            {
                new ()
                {
                    NodeId = targetExpandedNodeId,
                    ReferenceTypeId = referenceTypeId,
                    IsForward = false
                }
            };
            var id = new NodeId("test", 0);
            var context = new Mock<ISession>();

            context
                .Setup(c => c.FetchReferencesAsync(
                    It.Is<NodeId>(i => i == referenceTypeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.Is<NodeId>(i => i == id),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([.. expected])
                .Verifiable(Times.Once);
            context
                .Setup(c => c.ReadNodesAsync(
                    It.Is<IList<NodeId>>(n => n.Count == 1 && n[0] == targetNodeId),
                    NodeClass.Unspecified,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((new List<Node>
                    {
                        new VariableNode
                        {
                            NodeId = targetNodeId,
                            NodeClass = NodeClass.Variable
                        }
                    },
                    new[] { ServiceResult.Good }))
                .Verifiable(Times.Once);

            var nodeCache = new LruNodeCache(context.Object);

            // Act
            IReadOnlyList<INode> result1 = await nodeCache.GetReferencesAsync(id,
                referenceTypeId, true, true, default);
            IReadOnlyList<INode> result2 = await nodeCache.GetReferencesAsync(id,
                referenceTypeId, false, true, default);

            // Assert
            Assert.AreEqual(1, result1.Count);
            Assert.IsEmpty(result2);
            // Act
            result1 = await nodeCache.GetReferencesAsync(id,
                referenceTypeId, true, true, default);
            result2 = await nodeCache.GetReferencesAsync(id,
                referenceTypeId, false, true, default);

            // Assert
            Assert.AreEqual(1, result1.Count);
            Assert.IsEmpty(result2);
            context.Verify();
        }

        [Test]
        public async Task GetSuperTypeAsyncShouldHandleNoSupertypeAsync()
        {
            // Arrange
            var typeId = new NodeId("type", 0);
            var context = new Mock<ISession>();

            context
                .Setup(c => c.FetchReferencesAsync(
                    It.Is<NodeId>(i => i == typeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Once);
            var nodeCache = new LruNodeCache(context.Object);

            // Act
            NodeId result = await nodeCache.GetSuperTypeAsync(typeId, default);

            // Assert
            Assert.AreEqual(NodeId.Null, result);
            context.Verify();
        }

        [Test]
        public async Task GetSuperTypeAsyncShouldReturnSuperTypeAsync()
        {
            // Arrange
            var superTypeId = new NodeId("superType", 0);
            var subTypeId = new NodeId("subType", 0);
            var references = new List<ReferenceDescription>
            {
                new ()
                {
                    NodeId = new ExpandedNodeId(superTypeId),
                    ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                    IsForward = false
                }
            };
            var context = new Mock<ISession>();

            context
                .Setup(c => c.FetchReferencesAsync(
                    It.Is<NodeId>(i => i == subTypeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([.. references])
                .Verifiable(Times.Once);
            var nodeCache = new LruNodeCache(context.Object);

            // Act
            NodeId result = await nodeCache.GetSuperTypeAsync(subTypeId, default);

            // Assert
            Assert.AreEqual(superTypeId, result);

            // Act
            result = await nodeCache.GetSuperTypeAsync(subTypeId, default);
            // Assert
            Assert.AreEqual(superTypeId, result);

            context.Verify();
        }

        [Test]
        public void GetValueAsyncShouldHandleErrors()
        {
            // Arrange
            var id = new NodeId("test", 0);
            var context = new Mock<ISession>();

            context
                .Setup(c => c.ReadValueAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadUnexpectedError))
                .Verifiable(Times.Once);
            var nodeCache = new LruNodeCache(context.Object);

            // Act && Assert
            _ = Assert.ThrowsAsync<ServiceResultException>(async () => await nodeCache.GetValueAsync(id, default));
            context.Verify();
        }

        [Test]
        public async Task GetValueAsyncShouldReturnValueFromCacheAsync()
        {
            // Arrange
            var expected = new DataValue(new Variant(123), StatusCodes.Good, DateTime.UtcNow);
            var id = new NodeId("test", 0);
            var context = new Mock<ISession>();

            context
                .Setup(c => c.ReadValueAsync(
                    It.Is<NodeId>(i => i == id),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(expected)
                .Verifiable(Times.Once);
            var nodeCache = new LruNodeCache(context.Object);

            // Act
            DataValue result = await nodeCache.GetValueAsync(id, default);

            // Assert
            Assert.AreEqual(expected, result);
            result = await nodeCache.GetValueAsync(id, default);
            Assert.AreEqual(expected, result);
            context.Verify();
        }
        [Test]
        public async Task GetValuesAsyncShouldHandleErrorsAsync()
        {
            // Arrange
            var ids = new List<NodeId> { new("test1", 0), new("test2", 0) };
            var context = new Mock<ISession>();

            context
                .Setup(c => c.ReadValuesAsync(
                    It.IsAny<IList<NodeId>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((new DataValueCollection { new(), new() }, new[] { new ServiceResult(StatusCodes.BadUnexpectedError), ServiceResult.Good }))
                .Verifiable(Times.Once);
            var nodeCache = new LruNodeCache(context.Object);

            // Act
            IReadOnlyList<DataValue> result = await nodeCache.GetValuesAsync(ids, default);

            // Assert
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(StatusCodes.BadUnexpectedError, (uint)result[0].StatusCode);
            Assert.AreEqual(StatusCodes.Good, (uint)result[1].StatusCode);
            context.Verify();
        }

        [Test]
        public async Task GetValuesAsyncShouldReturnValuesFromCacheAsync()
        {
            // Arrange
            var expected = new List<DataValue>
            {
                new (new Variant(123), StatusCodes.Good, DateTime.UtcNow),
                new (new Variant(456), StatusCodes.Good, DateTime.UtcNow)
            };
            var ids = new List<NodeId> { new("test1", 0), new("test2", 0) };
            var context = new Mock<ISession>();

            context
                .Setup(c => c.ReadValuesAsync(
                    It.Is<IList<NodeId>>(i => i.ToHashSet().SetEquals(ids)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((new DataValueCollection(expected), new[] { ServiceResult.Good, ServiceResult.Good }))
                .Verifiable(Times.Once);
            var nodeCache = new LruNodeCache(context.Object);

            // Act
            IReadOnlyList<DataValue> result = await nodeCache.GetValuesAsync(ids, default);

            // Assert
            Assert.AreEqual(expected, result);

            result = await nodeCache.GetValuesAsync(ids, default);
            Assert.AreEqual(expected, result);
            context.Verify();
        }

        [Test]
        public async Task GetValuesAsyncShouldReturnValuesFromCacheButHonorStatusOfReadAsync()
        {
            // Arrange
            var expected = new List<DataValue>
            {
                new (new Variant(123), StatusCodes.Good, DateTime.UtcNow),
                new (new Variant(456), StatusCodes.Good, DateTime.UtcNow)
            };
            var ids = new List<NodeId> { new("test1", 0), new("test2", 0) };
            var context = new Mock<ISession>();

            context
                .Setup(c => c.ReadValuesAsync(
                    It.Is<IList<NodeId>>(i => i.ToHashSet().SetEquals(ids)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((new DataValueCollection(expected), new[] { ServiceResult.Good, new ServiceResult(StatusCodes.Bad) }))
                .Verifiable(Times.Once);
            var nodeCache = new LruNodeCache(context.Object);

            // Act
            IReadOnlyList<DataValue> result = await nodeCache.GetValuesAsync(ids, default);

            // Assert
            Assert.AreEqual(expected[0], result[0]);
            Assert.AreEqual(StatusCodes.Bad, (uint)result[1].StatusCode);
            Assert.AreEqual(expected[1].Value, result[1].Value);

            context
                .Setup(c => c.ReadValuesAsync(
                    It.Is<IList<NodeId>>(i => i.Count == 1 && i[0] == ids[1]),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((new DataValueCollection { expected[1] }, new[] { ServiceResult.Good }))
                .Verifiable(Times.Once);
            result = await nodeCache.GetValuesAsync(ids, default);
            context.Verify();
        }

        [Test]
        public void IsTypeOfShouldHandleNoReferences()
        {
            // Arrange
            var superTypeId = new NodeId("superType", 0);
            var subTypeId = new NodeId("subType", 0);
            var context = new Mock<ISession>();

            context
                .Setup(c => c.FetchReferencesAsync(
                    It.Is<NodeId>(i => i == subTypeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Once);
            var nodeCache = new LruNodeCache(context.Object);

            // Act
            bool result = nodeCache.IsTypeOf(subTypeId, superTypeId);

            // Assert
            Assert.False(result);
            context.Verify();
        }

        [Test]
        public void IsTypeOfShouldReturnTrueForSuperType()
        {
            // Arrange
            var superTypeId = new NodeId("superType", 0);
            var subTypeId = new NodeId("subType", 0);
            var references = new List<ReferenceDescription>
            {
                new ()
                {
                    NodeId = new ExpandedNodeId(superTypeId),
                    ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                    IsForward = false
                }
            };
            var context = new Mock<ISession>();

            context
                .Setup(c => c.FetchReferencesAsync(
                    It.Is<NodeId>(i => i == subTypeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([.. references])
                .Verifiable(Times.Once);
            var nodeCache = new LruNodeCache(context.Object);

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
            // Arrange
            var typeId = new NodeId("type", 0);
            var context = new Mock<ISession>();
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.Is<NodeId>(i => i == typeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Once);
            var nodeCache = new LruNodeCache(context.Object);

            // Act
            await nodeCache.LoadTypeHierarchyAsync([typeId], default);

            // Assert
            context.Verify();
        }

        [Test]
        public async Task LoadTypeHierarchyAsyncShouldLoadTypeHierarchyAsync()
        {
            // Arrange
            var typeId = new NodeId("type", 0);
            var subTypeId = new NodeId("subType", 0);
            var references = new ReferenceDescriptionCollection
            {
                new ()
                {
                    NodeId = new ExpandedNodeId(subTypeId),
                    ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                    IsForward = true
                }
            };
            var context = new Mock<ISession>();
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.Is<NodeId>(i => i == typeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(references)
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.Is<NodeId>(i => i == subTypeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Once);
            context
                .Setup(c => c.ReadNodesAsync(
                    It.Is<IList<NodeId>>(n => n.Count == 1 && n[0] == subTypeId),
                    NodeClass.Unspecified,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((new List<Node>
                    {
                        new DataTypeNode
                        {
                            NodeId = subTypeId,
                            NodeClass = NodeClass.DataType
                        }
                    }, new[] { ServiceResult.Good }))
                .Verifiable(Times.Once);
            var nodeCache = new LruNodeCache(context.Object);

            // Act
            await nodeCache.LoadTypeHierarchyAsync([typeId], default);
            await nodeCache.LoadTypeHierarchyAsync([typeId], default);
            await nodeCache.LoadTypeHierarchyAsync([typeId], default);

            // Assert
            context.Verify();
        }
    }
}
#endif

