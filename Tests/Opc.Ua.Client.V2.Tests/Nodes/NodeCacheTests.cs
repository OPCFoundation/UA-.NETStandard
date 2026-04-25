// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using Moq;
using Opc.Ua.Client.Sessions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Client.Nodes
{
    [TestFixture]
    public sealed class NodeCacheTests
    {
        [Test]
        public async Task FetchRemainingNodesAsyncShouldHandleErrorsAsync()
        {
            // Arrange
            var id = new NodeId("test", 0);
            var context = new Mock<INodeCacheContext>();

            context
                .Setup(c => c.FetchNodesAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Sessions.ResultSet<Node>>(new Sessions.ResultSet<Node>
                {
                    Results = [new()],
                    Errors = [new ServiceResult(StatusCodes.BadUnexpectedError)]
                }))
                .Verifiable(Times.Once);
            var nodeCache = new NodeCache(context.Object);

            // Act
            var result = await nodeCache.GetNodesAsync([id], default);

            // Assert
            Assert.That(result, Has.Exactly(1).Items);
            context.Verify();
        }

        [Test]
        public async Task GetBuiltInTypeAsyncShouldHandleUnknownTypeAsync()
        {
            // Arrange
            var datatypeId = new NodeId("unknownType", 0);
            var context = new Mock<INodeCacheContext>();
            var nodeCache = new NodeCache(context.Object);

            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == datatypeId),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ReferenceDescription>>(default(ArrayOf<ReferenceDescription>)))
                .Verifiable(Times.Once);

            // Act
            var result = await nodeCache.GetBuiltInTypeAsync(datatypeId, default);

            // Assert
            Assert.That(result, Is.EqualTo(BuiltInType.Null));
            context.Verify();
        }

        [Test]
        public async Task GetBuiltInTypeAsyncShouldReturnBuiltInTypeAsync()
        {
            // Arrange
            var datatypeId = new NodeId((uint)BuiltInType.Int32, 0);
            var context = new Mock<INodeCacheContext>();
            var nodeCache = new NodeCache(context.Object);

            // Act
            var result = await nodeCache.GetBuiltInTypeAsync(datatypeId, default);

            // Assert
            Assert.That(result, Is.EqualTo(BuiltInType.Int32));
        }

        [Test]
        public async Task GetNodeAsyncShouldHandleEmptyListAsync()
        {
            // Arrange
            var context = new Mock<INodeCacheContext>();
            var nodeCache = new NodeCache(context.Object);

            // Act
            var result = await nodeCache.GetNodesAsync([], default);

            // Assert
            Assert.That(result, Is.Empty);
        }

        [Test]
        public async Task GetNodeAsyncShouldReturnNodeFromCacheAsync()
        {
            // Arrange
            var expected = new Node();
            var id = new NodeId("test", 0);
            var context = new Mock<INodeCacheContext>();

            context
                .Setup(c => c.FetchNodeAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == id),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Node>(expected))
                .Verifiable(Times.Once);
            var nodeCache = new NodeCache(context.Object);

            // Act
            var result = await nodeCache.GetNodeAsync(id, default);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
            result = await nodeCache.GetNodeAsync(id, default);
            Assert.That(result, Is.EqualTo(expected));
            context.Verify();
        }
        [Test]
        public async Task GetNodeTestAsync()
        {
            var expected = new Node();
            var id = new NodeId("test", 0);
            var context = new Mock<INodeCacheContext>();

            context
                .Setup(c => c.FetchNodeAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == id),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, NodeId, CancellationToken>((_, nodeId, ct)
                    => ValueTask.FromResult(expected))
                .Verifiable(Times.Once);
            var nodeCache = new NodeCache(context.Object);

            var result = await nodeCache.GetNodeAsync(id, default);
            Assert.That(result, Is.EqualTo(expected));
            result = await nodeCache.GetNodeAsync(id, default);
            Assert.That(result, Is.EqualTo(expected));
            result = await nodeCache.GetNodeAsync(id, default);
            Assert.That(result, Is.EqualTo(expected));
            context.Verify();
        }

        [Test]
        public async Task GetNodeThrowsTestAsync()
        {
            var expected = new Node();
            var id = new NodeId("test", 0);
            var context = new Mock<INodeCacheContext>();

            context
                .Setup(c => c.FetchNodeAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == id),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, NodeId, CancellationToken>((_, nodeId, ct)
                    => ValueTask.FromException<Node>(new ServiceResultException()))
                .Verifiable(Times.Exactly(3));
            var nodeCache = new NodeCache(context.Object);

            Assert.ThrowsAsync<ServiceResultException>(
                () => nodeCache.GetNodeAsync(id, default).AsTask());
            Assert.ThrowsAsync<ServiceResultException>(
                () => nodeCache.GetNodeAsync(id, default).AsTask());
            Assert.ThrowsAsync<ServiceResultException>(
                () => nodeCache.GetNodeAsync(id, default).AsTask());
            context.Verify();
        }

        [Test]
        public async Task GetNodeWithBrowsePathAsyncShouldHandleInvalidBrowsePathAsync()
        {
            // Arrange
            var id = new NodeId("test", 0);
            var browsePath = (ArrayOf<QualifiedName>)[new QualifiedName("invalid")];
            var context = new Mock<INodeCacheContext>();

            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == ReferenceTypeIds.HierarchicalReferences),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ReferenceDescription>>(default(ArrayOf<ReferenceDescription>)))
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == id),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ReferenceDescription>>(default(ArrayOf<ReferenceDescription>)))
                .Verifiable(Times.Once);
            var nodeCache = new NodeCache(context.Object);

            // Act
            var result = await nodeCache.GetNodeWithBrowsePathAsync(id, browsePath, default);

            // Assert
            Assert.That(result, Is.Null);
            context.Verify();
        }

        [Test]
        public async Task GetNodeWithBrowsePathAsyncShouldReturnNodeAsync()
        {
            // Arrange
            var id = new NodeId("test", 0);
            var expected = new VariableNode
            {
                BrowseName = new QualifiedName("child"),
                NodeId = id,
                NodeClass = NodeClass.Variable
            };
            var browsePath = (ArrayOf<QualifiedName>)[new QualifiedName("child")];
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
            var context = new Mock<INodeCacheContext>();
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == ReferenceTypeIds.HierarchicalReferences),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ReferenceDescription>>(default(ArrayOf<ReferenceDescription>)))
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == id),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ReferenceDescription>>(references.ToArrayOf()))
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchNodesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<IReadOnlyList<NodeId>>(n => n.Count == 1 && n[0] == id),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Sessions.ResultSet<Node>>(new Sessions.ResultSet<Node>
                {
                    Results = [expected],
                    Errors = [ServiceResult.Good]
                }))
                .Verifiable(Times.Once);

            var nodeCache = new NodeCache(context.Object);

            // Act
            var result = await nodeCache.GetNodeWithBrowsePathAsync(id,
                browsePath, default);

            // Assert
            Assert.That(result, Is.EqualTo(expected));

            // Act
            result = await nodeCache.GetNodeWithBrowsePathAsync(id,
                browsePath, default);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
            context.Verify();
        }

        [Test]
        public async Task GetNodeWithBrowsePathAsyncShouldReturnNodeWithMultipleElementsAsync()
        {
            // Arrange
            var rootId = new NodeId("root", 0);
            var childId = new NodeId("child", 0);
            var grandChildId = new NodeId("grandChild", 0);
            var browsePath = (ArrayOf<QualifiedName>)[new QualifiedName("child"), new QualifiedName("grandChild")];

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
            var childNode = new VariableNode
            {
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
            var expected = new VariableNode
            {
                BrowseName = new QualifiedName("grandChild"),
                NodeId = grandChildId,
                NodeClass = NodeClass.Variable
            };

            var context = new Mock<INodeCacheContext>();

            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == ReferenceTypeIds.HierarchicalReferences),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ReferenceDescription>>(default(ArrayOf<ReferenceDescription>)))
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == rootId),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ReferenceDescription>>(rootReferences.ToArrayOf()))
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == childId),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ReferenceDescription>>(childReferences.ToArrayOf()))
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchNodesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<IReadOnlyList<NodeId>>(n => n.Count == 1 && n[0] == childId),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Sessions.ResultSet<Node>>(new Sessions.ResultSet<Node>
                {
                    Results = [childNode],
                    Errors = [ServiceResult.Good]
                }))
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchNodesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<IReadOnlyList<NodeId>>(n => n.Count == 1 && n[0] == grandChildId),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Sessions.ResultSet<Node>>(new Sessions.ResultSet<Node>
                {
                    Results = [expected],
                    Errors = [ServiceResult.Good]
                }))
                .Verifiable(Times.Once);

            var nodeCache = new NodeCache(context.Object);

            // Act
            var result = await nodeCache.GetNodeWithBrowsePathAsync(rootId, browsePath, default);

            // Assert
            Assert.That(result, Is.EqualTo(expected));

            // Act
            result = await nodeCache.GetNodeWithBrowsePathAsync(rootId, browsePath, default);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
            context.Verify();
        }

        [Test]
        public async Task GetReferencesAsyncShouldHandleEmptyListOfNodeIdsAsync()
        {
            // Arrange
            var context = new Mock<INodeCacheContext>();
            var nodeCache = new NodeCache(context.Object);

            // Act
            var result = await nodeCache.GetReferencesAsync([], [], false, false, default);

            // Assert
            Assert.That(result, Is.Empty);
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
            var context = new Mock<INodeCacheContext>();

            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == id),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ReferenceDescription>>(expected.ToArrayOf()))
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchNodesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<IReadOnlyList<NodeId>>(n => n.Count == 1 && n[0] == targetNodeId),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Sessions.ResultSet<Node>>(new Sessions.ResultSet<Node>
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
                }))
                .Verifiable(Times.Once);

            var nodeCache = new NodeCache(context.Object);

            // Act
            var result1 = await nodeCache.GetReferencesAsync(id,
                referenceTypeId, true, false, default);
            var result2 = await nodeCache.GetReferencesAsync(id,
                referenceTypeId, false, false, default);

            // Assert
            Assert.That(result1, Has.Exactly(1).Items);
            Assert.That(result2, Is.Empty);
            // Act
            result1 = await nodeCache.GetReferencesAsync(id,
                referenceTypeId, true, false, default);
            result2 = await nodeCache.GetReferencesAsync(id,
                referenceTypeId, false, false, default);

            // Assert
            Assert.That(result1, Has.Exactly(1).Items);
            Assert.That(result2, Is.Empty);
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
            var context = new Mock<INodeCacheContext>();

            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == referenceTypeId),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ReferenceDescription>>((ArrayOf<ReferenceDescription>)
                [
                    new ()
                    {
                        ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                        BrowseName = new QualifiedName("HasSubtype"),
                        NodeId = new ExpandedNodeId(referenceSubTypeId)
                    }
                ]))
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchNodesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<IReadOnlyList<NodeId>>(n => n.Count == 1 && n[0] == referenceSubTypeId),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Sessions.ResultSet<Node>>(new Sessions.ResultSet<Node>
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
                }))
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == referenceSubTypeId),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ReferenceDescription>>((ArrayOf<ReferenceDescription>)
                [
                    new ()
                    {
                        ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                        IsForward = false,
                        BrowseName = new QualifiedName("HasSuperType"),
                        NodeId = new ExpandedNodeId(referenceTypeId)
                    }
                ]))
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == id),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ReferenceDescription>>(expected.ToArrayOf()))
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchNodesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<IReadOnlyList<NodeId>>(n => n.Count == 1 && n[0] == targetNodeId),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Sessions.ResultSet<Node>>(new Sessions.ResultSet<Node>
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
                }))
                .Verifiable(Times.Once);

            var nodeCache = new NodeCache(context.Object);

            // Act
            var result1 = await nodeCache.GetReferencesAsync(id,
                referenceTypeId, true, true, default);
            var result2 = await nodeCache.GetReferencesAsync(id,
                referenceTypeId, false, true, default);

            // Assert
            Assert.That(result1, Has.Exactly(1).Items);
            Assert.That(result2, Is.Empty);
            // Act
            result1 = await nodeCache.GetReferencesAsync(id,
                referenceTypeId, true, true, default);
            result2 = await nodeCache.GetReferencesAsync(id,
                referenceTypeId, false, true, default);

            // Assert
            Assert.That(result1, Has.Exactly(1).Items);
            Assert.That(result2, Is.Empty);
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
            var context = new Mock<INodeCacheContext>();

            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == referenceTypeId),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ReferenceDescription>>(default(ArrayOf<ReferenceDescription>)))
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == id),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ReferenceDescription>>(expected.ToArrayOf()))
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchNodesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<IReadOnlyList<NodeId>>(n => n.Count == 1 && n[0] == targetNodeId),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Sessions.ResultSet<Node>>(new Sessions.ResultSet<Node>
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
                }))
                .Verifiable(Times.Once);

            var nodeCache = new NodeCache(context.Object);

            // Act
            var result1 = await nodeCache.GetReferencesAsync(id,
                referenceTypeId, true, true, default);
            var result2 = await nodeCache.GetReferencesAsync(id,
                referenceTypeId, false, true, default);

            // Assert
            Assert.That(result1, Has.Exactly(1).Items);
            Assert.That(result2, Is.Empty);
            // Act
            result1 = await nodeCache.GetReferencesAsync(id,
                referenceTypeId, true, true, default);
            result2 = await nodeCache.GetReferencesAsync(id,
                referenceTypeId, false, true, default);

            // Assert
            Assert.That(result1, Has.Exactly(1).Items);
            Assert.That(result2, Is.Empty);
            context.Verify();
        }

        [Test]
        public async Task GetSuperTypeAsyncShouldHandleNoSupertypeAsync()
        {
            // Arrange
            var typeId = new NodeId("type", 0);
            var context = new Mock<INodeCacheContext>();

            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == typeId),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ReferenceDescription>>(default(ArrayOf<ReferenceDescription>)))
                .Verifiable(Times.Once);
            var nodeCache = new NodeCache(context.Object);

            // Act
            var result = await nodeCache.GetSuperTypeAsync(typeId, default);

            // Assert
            Assert.That(result, Is.EqualTo(NodeId.Null));
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
            var context = new Mock<INodeCacheContext>();

            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == subTypeId),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ReferenceDescription>>(references.ToArrayOf()))
                .Verifiable(Times.Once);
            var nodeCache = new NodeCache(context.Object);

            // Act
            var result = await nodeCache.GetSuperTypeAsync(subTypeId, default);

            // Assert
            Assert.That(result, Is.EqualTo(superTypeId));

            // Act
            result = await nodeCache.GetSuperTypeAsync(subTypeId, default);
            // Assert
            Assert.That(result, Is.EqualTo(superTypeId));

            context.Verify();
        }

        [Test]
        public async Task GetValueAsyncShouldHandleErrorsAsync()
        {
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
            var nodeCache = new NodeCache(context.Object);

            // Act
            Func<Task> act = async () => await nodeCache.GetValueAsync(id, default);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            context.Verify();
        }

        [Test]
        public async Task GetValueAsyncShouldReturnValueFromCacheAsync()
        {
            // Arrange
            var expected = new DataValue(new Variant(123), StatusCodes.Good, DateTimeUtc.Now);
            var id = new NodeId("test", 0);
            var context = new Mock<INodeCacheContext>();

            context
                .Setup(c => c.FetchValueAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == id),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<DataValue>(expected))
                .Verifiable(Times.Once);
            var nodeCache = new NodeCache(context.Object);

            // Act
            var result = await nodeCache.GetValueAsync(id, default);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
            result = await nodeCache.GetValueAsync(id, default);
            Assert.That(result, Is.EqualTo(expected));
            context.Verify();
        }
        [Test]
        public async Task GetValuesAsyncShouldHandleErrorsAsync()
        {
            // Arrange
            var ids = new List<NodeId> { new("test1", 0), new("test2", 0) };
            var context = new Mock<INodeCacheContext>();

            context
                .Setup(c => c.FetchValuesAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Sessions.ResultSet<DataValue>>(new Sessions.ResultSet<DataValue>
                {
                    Results = [new(), new()],
                    Errors = [new ServiceResult(StatusCodes.BadUnexpectedError), ServiceResult.Good]
                }))
                .Verifiable(Times.Once);
            var nodeCache = new NodeCache(context.Object);

            // Act
            var result = await nodeCache.GetValuesAsync(ids, default);

            // Assert
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[0].StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(result[1].StatusCode, Is.EqualTo(StatusCodes.Good));
            context.Verify();
        }

        [Test]
        public async Task GetValuesAsyncShouldReturnValuesFromCacheAsync()
        {
            // Arrange
            var expected = new List<DataValue>
            {
                new (new Variant(123), StatusCodes.Good, DateTimeUtc.Now),
                new (new Variant(456), StatusCodes.Good, DateTimeUtc.Now)
            };
            var ids = new List<NodeId> { new("test1", 0), new("test2", 0) };
            var context = new Mock<INodeCacheContext>();

            context
                .Setup(c => c.FetchValuesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<IReadOnlyList<NodeId>>(i => i.ToHashSet().SetEquals(ids)),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Sessions.ResultSet<DataValue>>(new Sessions.ResultSet<DataValue>
                {
                    Results = expected,
                    Errors = [ServiceResult.Good, ServiceResult.Good]
                }))
                .Verifiable(Times.Once);
            var nodeCache = new NodeCache(context.Object);

            // Act
            var result = await nodeCache.GetValuesAsync(ids, default);

            // Assert
            Assert.That(result, Is.EqualTo(expected));

            result = await nodeCache.GetValuesAsync(ids, default);
            Assert.That(result, Is.EqualTo(expected));
            context.Verify();
        }

        [Test]
        public async Task GetValuesAsyncShouldReturnValuesFromCacheButHonorStatusOfReadAsync()
        {
            // Arrange
            var expected = new List<DataValue>
            {
                new (new Variant(123), StatusCodes.Good, DateTimeUtc.Now),
                new (new Variant(456), StatusCodes.Good, DateTimeUtc.Now)
            };
            var ids = new List<NodeId> { new("test1", 0), new("test2", 0) };
            var context = new Mock<INodeCacheContext>();

            context
                .Setup(c => c.FetchValuesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<IReadOnlyList<NodeId>>(i => i.ToHashSet().SetEquals(ids)),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Sessions.ResultSet<DataValue>>(new Sessions.ResultSet<DataValue>
                {
                    Results = expected,
                    Errors = [ServiceResult.Good, new ServiceResult(StatusCodes.Bad)]
                }))
                .Verifiable(Times.Once);
            var nodeCache = new NodeCache(context.Object);

            // Act
            var result = await nodeCache.GetValuesAsync(ids, default);

            // Assert
            Assert.That(result[0], Is.EqualTo(expected[0]));
            Assert.That(result[1].StatusCode, Is.EqualTo(StatusCodes.Bad));
            Assert.That(result[1].WrappedValue, Is.EqualTo(expected[1].WrappedValue));

            context
                .Setup(c => c.FetchValuesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<IReadOnlyList<NodeId>>(i => i.Count == 1 && i[0] == ids[1]),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Sessions.ResultSet<DataValue>>(new Sessions.ResultSet<DataValue>
                {
                    Results = [expected[1]],
                    Errors = [ServiceResult.Good]
                }))
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
            var context = new Mock<INodeCacheContext>();

            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == subTypeId),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ReferenceDescription>>(default(ArrayOf<ReferenceDescription>)))
                .Verifiable(Times.Once);
            var nodeCache = new NodeCache(context.Object);

            // Act
            var result = nodeCache.IsTypeOf(subTypeId, superTypeId);

            // Assert
            Assert.That(result, Is.False);
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
            var context = new Mock<INodeCacheContext>();

            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == subTypeId),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ReferenceDescription>>(references.ToArrayOf()))
                .Verifiable(Times.Once);
            var nodeCache = new NodeCache(context.Object);

            // Act
            var result = nodeCache.IsTypeOf(subTypeId, superTypeId);

            // Assert
            Assert.That(result, Is.True);

            // Act
            result = nodeCache.IsTypeOf(subTypeId, superTypeId);

            // Assert
            Assert.That(result, Is.True);
            context.Verify();
        }

        [Test]
        public async Task LoadTypeHierarchyAyncShouldHandleNoSubtypesAsync()
        {
            // Arrange
            var typeId = new NodeId("type", 0);
            var context = new Mock<INodeCacheContext>();
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == typeId),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ReferenceDescription>>(default(ArrayOf<ReferenceDescription>)))
                .Verifiable(Times.Once);
            var nodeCache = new NodeCache(context.Object);

            // Act
            await nodeCache.LoadTypeHierarchyAync([typeId], default);

            // Assert
            context.Verify();
        }

        [Test]
        public async Task LoadTypeHierarchyAyncShouldLoadTypeHierarchyAsync()
        {
            // Arrange
            var typeId = new NodeId("type", 0);
            var subTypeId = new NodeId("subType", 0);
            var references = new List<ReferenceDescription>
            {
                new ()
                {
                    NodeId = new ExpandedNodeId(subTypeId),
                    ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                    IsForward = true
                }
            };
            var context = new Mock<INodeCacheContext>();
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == typeId),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ReferenceDescription>>(references.ToArrayOf()))
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<NodeId>(i => i == subTypeId),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ReferenceDescription>>(default(ArrayOf<ReferenceDescription>)))
                .Verifiable(Times.Once);
            context
                .Setup(c => c.FetchNodesAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<IReadOnlyList<NodeId>>(n => n.Count == 1 && n[0] == subTypeId),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Sessions.ResultSet<Node>>(new Sessions.ResultSet<Node>
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
                }))
                .Verifiable(Times.Once);
            var nodeCache = new NodeCache(context.Object);

            // Act
            await nodeCache.LoadTypeHierarchyAync([typeId], default);
            await nodeCache.LoadTypeHierarchyAync([typeId], default);
            await nodeCache.LoadTypeHierarchyAync([typeId], default);

            // Assert
            context.Verify();
        }
    }
}
