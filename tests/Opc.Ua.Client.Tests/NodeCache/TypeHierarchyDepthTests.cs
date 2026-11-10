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
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Type hierarchy walks of the node cache and the session follow the
    /// HasSubtype references the server returns. They walk iteratively and
    /// stop at <see cref="NodeCache.MaxTypeHierarchyDepth"/>.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("NodeCache")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class TypeHierarchyDepthTests
    {
        [Test]
        public async Task LoadTypeHierarchyFollowsAChainWithinTheLimitAsync()
        {
            using SessionMock session = SessionMock.Create();
            var server = new SubtypeChainServer(
                session, ReferenceTypeIds.HierarchicalReferences, NodeClass.ReferenceType, 100);

            await session.NodeCache.LoadTypeHierarchyAsync(
                [ReferenceTypeIds.HierarchicalReferences], default).ConfigureAwait(false);

            Assert.That(server.MaxLevelBrowsed, Is.EqualTo(100));
        }

        [Test]
        public async Task LoadTypeHierarchyDeeperThanTheLimitFailsAsync()
        {
            using SessionMock session = SessionMock.Create();
            var server = new SubtypeChainServer(
                session,
                ReferenceTypeIds.HierarchicalReferences,
                NodeClass.ReferenceType,
                NodeCache.MaxTypeHierarchyDepth + 20);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await session.NodeCache.LoadTypeHierarchyAsync(
                    [ReferenceTypeIds.HierarchicalReferences], default).ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadQueryTooComplex));
            Assert.That(server.MaxLevelBrowsed, Is.LessThanOrEqualTo(NodeCache.MaxTypeHierarchyDepth + 1));
        }

        [Test]
        public async Task SubtypeQueryOverAHierarchyDeeperThanTheLimitFailsAsync()
        {
            using SessionMock session = SessionMock.Create();
            var server = new SubtypeChainServer(
                session,
                ReferenceTypeIds.HierarchicalReferences,
                NodeClass.ReferenceType,
                NodeCache.MaxTypeHierarchyDepth + 20);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await session.NodeCache.GetReferencesAsync(
                    new NodeId("T1", 2),
                    ReferenceTypeIds.HierarchicalReferences,
                    false,
                    true,
                    default).ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadQueryTooComplex));
            Assert.That(server.MaxLevelBrowsed, Is.LessThanOrEqualTo(NodeCache.MaxTypeHierarchyDepth + 1));
        }

        [Test]
        public async Task FetchTypeTreeFollowsAChainWithinTheLimitAsync()
        {
            using SessionMock session = SessionMock.Create();
            var server = new SubtypeChainServer(
                session, ObjectTypeIds.FileType, NodeClass.ObjectType, 100);

            await session.FetchTypeTreeAsync(ObjectTypeIds.FileType).ConfigureAwait(false);

            Assert.That(server.MaxLevelBrowsed, Is.GreaterThanOrEqualTo(99));
        }

        [Test]
        public async Task FetchTypeTreeDeeperThanTheLimitFailsAsync()
        {
            using SessionMock session = SessionMock.Create();
            var server = new SubtypeChainServer(
                session,
                ObjectTypeIds.FileType,
                NodeClass.ObjectType,
                NodeCache.MaxTypeHierarchyDepth + 20);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(
                () => session.FetchTypeTreeAsync(ObjectTypeIds.FileType)).ConfigureAwait(false);

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadQueryTooComplex));
            Assert.That(server.MaxLevelBrowsed, Is.LessThanOrEqualTo(NodeCache.MaxTypeHierarchyDepth + 2));
        }

        [Test]
        public async Task LoadTypeHierarchyAtTheLimitSucceedsAsync()
        {
            using SessionMock session = SessionMock.Create();
            var server = new SubtypeChainServer(
                session,
                ReferenceTypeIds.HierarchicalReferences,
                NodeClass.ReferenceType,
                NodeCache.MaxTypeHierarchyDepth);

            await session.NodeCache.LoadTypeHierarchyAsync(
                [ReferenceTypeIds.HierarchicalReferences], default).ConfigureAwait(false);

            Assert.That(server.MaxLevelBrowsed, Is.EqualTo(NodeCache.MaxTypeHierarchyDepth));
        }

        [Test]
        public async Task LoadTypeHierarchyOneLevelBeyondTheLimitFailsAsync()
        {
            using SessionMock session = SessionMock.Create();
            _ = new SubtypeChainServer(
                session,
                ReferenceTypeIds.HierarchicalReferences,
                NodeClass.ReferenceType,
                NodeCache.MaxTypeHierarchyDepth + 1);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await session.NodeCache.LoadTypeHierarchyAsync(
                    [ReferenceTypeIds.HierarchicalReferences], default).ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadQueryTooComplex));
        }

        [Test]
        public async Task FetchTypeTreeAtTheLimitSucceedsAsync([Values] bool arrayOverload)
        {
            using SessionMock session = SessionMock.Create();
            var server = new SubtypeChainServer(
                session, ObjectTypeIds.FileType, NodeClass.ObjectType, NodeCache.MaxTypeHierarchyDepth);

            await FetchFileTypeTreeAsync(session, arrayOverload).ConfigureAwait(false);

            Assert.That(server.MaxLevelBrowsed, Is.GreaterThanOrEqualTo(NodeCache.MaxTypeHierarchyDepth - 1));
        }

        [Test]
        public async Task FetchTypeTreeOneLevelBeyondTheLimitFailsAsync([Values] bool arrayOverload)
        {
            using SessionMock session = SessionMock.Create();
            _ = new SubtypeChainServer(
                session, ObjectTypeIds.FileType, NodeClass.ObjectType, NodeCache.MaxTypeHierarchyDepth + 1);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(
                () => FetchFileTypeTreeAsync(session, arrayOverload)).ConfigureAwait(false);

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadQueryTooComplex));
        }

        private static Task FetchFileTypeTreeAsync(SessionMock session, bool arrayOverload)
        {
            return arrayOverload
                ? session.FetchTypeTreeAsync(new ExpandedNodeId[] { ObjectTypeIds.FileType }.ToArrayOf())
                : session.FetchTypeTreeAsync(ObjectTypeIds.FileType);
        }

        [Test]
        public async Task FindReferenceTypeFindsANameInTheChainAsync()
        {
            using SessionMock session = SessionMock.Create();
            _ = new SubtypeChainServer(
                session, ReferenceTypeIds.References, NodeClass.ReferenceType, 50);

            NodeId found = await session.NodeCache.FindReferenceTypeAsync(
                new QualifiedName("T40", 2), default).ConfigureAwait(false);

            Assert.That(found, Is.EqualTo(new NodeId("T40", 2)));
        }

        [Test]
        public async Task FindReferenceTypeDoesNotSearchBelowTheLimitAsync()
        {
            using SessionMock session = SessionMock.Create();
            var server = new SubtypeChainServer(
                session,
                ReferenceTypeIds.References,
                NodeClass.ReferenceType,
                NodeCache.MaxTypeHierarchyDepth + 20);

            NodeId found = await session.NodeCache.FindReferenceTypeAsync(
                new QualifiedName("Missing", 2), default).ConfigureAwait(false);

            Assert.That(found.IsNull, Is.True);
            Assert.That(server.MaxLevelBrowsed, Is.LessThanOrEqualTo(NodeCache.MaxTypeHierarchyDepth));
        }

        [Test]
        public async Task FindReferenceTypeSearchesSubtypesDepthFirstInOrderAsync()
        {
            // Both branches below References hold a node named Twin: the one in
            // the first branch is found, as before the walk became iterative.
            using SessionMock session = SessionMock.Create();
            var tree = new Dictionary<NodeId, NodeId[]>
            {
                [ReferenceTypeIds.References] = [new NodeId("A", 2), new NodeId("B", 2)],
                [new NodeId("A", 2)] = [new NodeId("A1", 2)],
                [new NodeId("A1", 2)] = [new NodeId("Twin1", 2)],
                [new NodeId("B", 2)] = [new NodeId("Twin2", 2)]
            };
            var names = new Dictionary<NodeId, QualifiedName>
            {
                [new NodeId("Twin1", 2)] = new QualifiedName("Twin", 2),
                [new NodeId("Twin2", 2)] = new QualifiedName("Twin", 2)
            };
            _ = new SubtypeTreeServer(session, tree, names);

            NodeId found = await session.NodeCache.FindReferenceTypeAsync(
                new QualifiedName("Twin", 2), default).ConfigureAwait(false);

            Assert.That(found, Is.EqualTo(new NodeId("Twin1", 2)));
        }

        /// <summary>
        /// Serves Browse and Read for a linear chain root, T1, ..., Tn of type
        /// nodes connected by HasSubtype.
        /// </summary>
        private sealed class SubtypeChainServer : SubtypeTreeServer
        {
            public SubtypeChainServer(SessionMock session, NodeId root, NodeClass nodeClass, int depth)
                : base(session, BuildChain(root, depth), [], nodeClass)
            {
                m_root = root;
                for (int level = 1; level <= depth; level++)
                {
                    m_levels[new NodeId("T" + level, 2)] = level;
                }
            }

            public int MaxLevelBrowsed => Volatile.Read(ref m_maxLevel);

            protected override void OnBrowse(NodeId nodeId)
            {
                int level = nodeId == m_root ? 0 : m_levels.TryGetValue(nodeId, out int l) ? l : -1;
                int max = Volatile.Read(ref m_maxLevel);
                while (level > max && Interlocked.CompareExchange(ref m_maxLevel, level, max) != max)
                {
                    max = Volatile.Read(ref m_maxLevel);
                }
            }

            private static Dictionary<NodeId, NodeId[]> BuildChain(NodeId root, int depth)
            {
                var tree = new Dictionary<NodeId, NodeId[]>();
                NodeId parent = root;
                for (int level = 1; level <= depth; level++)
                {
                    var child = new NodeId("T" + level, 2);
                    tree[parent] = [child];
                    parent = child;
                }
                tree[parent] = [];
                return tree;
            }

            private readonly NodeId m_root;
            private readonly Dictionary<NodeId, int> m_levels = [];
            private int m_maxLevel;
        }

        /// <summary>
        /// Serves Browse and Read for a tree of type nodes connected by
        /// HasSubtype. Every response completes asynchronously.
        /// </summary>
        private class SubtypeTreeServer
        {
            public SubtypeTreeServer(
                SessionMock session,
                Dictionary<NodeId, NodeId[]> tree,
                Dictionary<NodeId, QualifiedName> names,
                NodeClass nodeClass = NodeClass.ReferenceType)
            {
                m_tree = tree;
                m_names = names;
                m_nodeClass = nodeClass;
                foreach (KeyValuePair<NodeId, NodeId[]> entry in tree)
                {
                    foreach (NodeId child in entry.Value)
                    {
                        m_parents[child] = entry.Key;
                    }
                }
                session.Channel
                    .Setup(c => c.SendRequestAsync(It.IsAny<BrowseRequest>(), It.IsAny<CancellationToken>()))
                    .Returns((IServiceRequest r, CancellationToken _) => RespondAsync(Browse((BrowseRequest)r)));
                session.Channel
                    .Setup(c => c.SendRequestAsync(It.IsAny<ReadRequest>(), It.IsAny<CancellationToken>()))
                    .Returns((IServiceRequest r, CancellationToken _) => RespondAsync(Read((ReadRequest)r)));
            }

            protected virtual void OnBrowse(NodeId nodeId)
            {
            }

            private static async ValueTask<IServiceResponse> RespondAsync(IServiceResponse response)
            {
                await Task.Yield();
                return response;
            }

            private bool IsKnown(NodeId nodeId)
            {
                return m_tree.ContainsKey(nodeId) || m_parents.ContainsKey(nodeId);
            }

            private QualifiedName NameOf(NodeId nodeId)
            {
                if (m_names.TryGetValue(nodeId, out QualifiedName name))
                {
                    return name;
                }
                return new QualifiedName(nodeId.TryGetValue(out string s) ? s : nodeId.ToString(), 2);
            }

            private BrowseResponse Browse(BrowseRequest request)
            {
                var results = new List<BrowseResult>();
                foreach (BrowseDescription description in request.NodesToBrowse)
                {
                    NodeId nodeId = description.NodeId;
                    var references = new List<ReferenceDescription>();
                    if (IsKnown(nodeId))
                    {
                        OnBrowse(nodeId);
                        if (description.BrowseDirection != BrowseDirection.Inverse &&
                            m_tree.TryGetValue(nodeId, out NodeId[]? children))
                        {
                            foreach (NodeId child in children)
                            {
                                references.Add(Reference(child, true));
                            }
                        }
                        if (description.BrowseDirection != BrowseDirection.Forward &&
                            m_parents.TryGetValue(nodeId, out NodeId parent))
                        {
                            references.Add(Reference(parent, false));
                        }
                    }
                    results.Add(new BrowseResult
                    {
                        StatusCode = IsKnown(nodeId) ? StatusCodes.Good : StatusCodes.BadNodeIdUnknown,
                        References = references.ToArrayOf()
                    });
                }
                return new BrowseResponse { Results = results.ToArrayOf(), DiagnosticInfos = [] };
            }

            private ReferenceDescription Reference(NodeId target, bool forward)
            {
                QualifiedName name = NameOf(target);
                return new ReferenceDescription
                {
                    ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                    IsForward = forward,
                    NodeId = target,
                    BrowseName = name,
                    DisplayName = LocalizedText.From(name.Name!),
                    NodeClass = m_nodeClass
                };
            }

            private ReadResponse Read(ReadRequest request)
            {
                var results = new List<DataValue>();
                foreach (ReadValueId valueId in request.NodesToRead)
                {
                    if (!IsKnown(valueId.NodeId))
                    {
                        results.Add(DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown));
                        continue;
                    }
                    Node node = CreateNode(valueId.NodeId);
                    var value = new DataValue();
                    ServiceResult result = node.Read(null!, valueId.AttributeId, ref value);
                    results.Add(ServiceResult.IsBad(result)
                        ? DataValue.FromStatusCode(StatusCodes.BadAttributeIdInvalid)
                        : value);
                }
                return new ReadResponse { Results = results.ToArrayOf(), DiagnosticInfos = [] };
            }

            private Node CreateNode(NodeId nodeId)
            {
                QualifiedName name = NameOf(nodeId);
                if (m_nodeClass == NodeClass.ReferenceType)
                {
                    return new ReferenceTypeNode
                    {
                        NodeId = nodeId,
                        NodeClass = NodeClass.ReferenceType,
                        BrowseName = name,
                        DisplayName = LocalizedText.From(name.Name!),
                        IsAbstract = false,
                        Symmetric = false,
                        InverseName = LocalizedText.From("Inverse" + name.Name)
                    };
                }
                return new ObjectTypeNode
                {
                    NodeId = nodeId,
                    NodeClass = NodeClass.ObjectType,
                    BrowseName = name,
                    DisplayName = LocalizedText.From(name.Name!),
                    IsAbstract = false
                };
            }

            private readonly Dictionary<NodeId, NodeId[]> m_tree;
            private readonly Dictionary<NodeId, NodeId> m_parents = [];
            private readonly Dictionary<NodeId, QualifiedName> m_names;
            private readonly NodeClass m_nodeClass;
        }
    }
}
