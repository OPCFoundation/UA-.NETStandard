// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies dynamic node resolution across request, shared-component, and already-validated cache entries.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    public sealed class DynamicNodeCacheRegressionTests
    {
        /// <summary>
        /// Verifies that all supported cache-hit paths return the original node while a genuine miss remains
        /// unresolved.
        /// </summary>
        [TestCase("direct")]
        [TestCase("root")]
        [TestCase("shared")]
        [TestCase("sharedComponent")]
        [TestCase("validated")]
        [TestCase("miss")]
        public void CacheLookupPreservesHitsAndFallsBackOnMiss(string source)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var manager = new CacheHooks(server.Object))
            {
                var root = new BaseObjectState(null) { NodeId = new NodeId("root", 1) };
                var child = new BaseDataVariableState(root)
                {
                    NodeId = new NodeId("root.child", 1),
                    SymbolicName = "Child",
                    BrowseName = new QualifiedName("Child", 1)
                };
                root.AddChild(child);
                var handle = new NodeHandle { NodeId = child.NodeId };
                var cache = new Dictionary<NodeId, NodeState>();
                switch (source)
                {
                    case "direct":
                        cache.Add(child.NodeId, child);
                        break;
                    case "root":
                        handle.RootId = root.NodeId;
                        handle.ComponentPath = "Child";
                        cache.Add(root.NodeId, root);
                        break;
                    case "shared":
                        manager.AddShared(handle, child);
                        break;
                    case "sharedComponent":
                        handle.RootId = root.NodeId;
                        handle.ComponentPath = "Child";
                        manager.AddShared(handle, child);
                        break;
                    case "validated":
                        handle.Validated = true;
                        handle.Node = child;
                        break;
                }

                NodeState result = manager.Find(handle, cache);
                if (source == "miss")
                {
                    Assert.That(result, Is.Null);
                }
                else
                {
                    Assert.That(result, Is.SameAs(child));
                    Assert.That(result.NodeId, Is.EqualTo(child.NodeId));
                }
            }
        }

        /// <summary>
        /// Exposes dynamic-node cache lookup and shared-component registration for focused cache tests.
        /// </summary>
        private sealed class CacheHooks : CustomNodeManager2
        {
            /// <summary>
            /// Creates a node manager whose dynamic caches can be populated and queried directly.
            /// </summary>
            public CacheHooks(IServerInternal server)
                : base(server, NullLogger.Instance, "urn:tests:dynamic-node-cache")
            {
            }

            /// <summary>
            /// Resolves a handle through the request cache and manager-owned component cache.
            /// </summary>
            public NodeState Find(NodeHandle handle, IDictionary<NodeId, NodeState> cache)
            {
                return FindNodeInCache(SystemContext, handle, cache)!;
            }

            /// <summary>
            /// Adds a resolved node to the manager's shared component cache.
            /// </summary>
            public void AddShared(NodeHandle handle, NodeState node)
            {
                AddNodeToComponentCache(SystemContext, handle, node);
            }
        }
    }
}
