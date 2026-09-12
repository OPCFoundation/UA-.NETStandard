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

using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    [TestFixture]
    [Category("NodeManager")]
    public sealed class DynamicNodeCacheRegressionTests
    {
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

        private sealed class CacheHooks : CustomNodeManager2
        {
            public CacheHooks(IServerInternal server)
                : base(server, NullLogger.Instance, "urn:tests:dynamic-node-cache")
            {
            }

            public NodeState Find(NodeHandle handle, IDictionary<NodeId, NodeState> cache)
            {
                return FindNodeInCache(SystemContext, handle, cache);
            }

            public void AddShared(NodeHandle handle, NodeState node)
            {
                AddNodeToComponentCache(SystemContext, handle, node);
            }
        }
    }
}
