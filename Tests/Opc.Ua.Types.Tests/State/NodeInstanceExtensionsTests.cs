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

using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.State
{
    /// <summary>
    /// Tests for <see cref="NodeInstanceExtensions.AssignInstanceChildNodeIds"/>,
    /// the helper the source-generated <c>CreateInstanceOf&lt;Type&gt;</c>
    /// factories use to rebase a materialised subtree onto per-instance NodeIds.
    /// </summary>
    [TestFixture]
    [Category("NodeState")]
    [Parallelizable]
    public class NodeInstanceExtensionsTests
    {
        /// <summary>
        /// NodeIdFactory that mirrors the convention used by managers such as
        /// DiNodeManager: {parentIdentifier}_{symbolicName} in the parent's
        /// namespace.
        /// </summary>
        private sealed class ChildIdFactory : INodeIdFactory
        {
            public NodeId New(ISystemContext context, NodeState node)
            {
                if (node is BaseInstanceState instance && instance.Parent != null)
                {
                    return new NodeId(
                        $"{instance.Parent.NodeId.IdentifierAsString}_{instance.SymbolicName}",
                        instance.Parent.NodeId.NamespaceIndex);
                }
                return node.NodeId;
            }
        }

        private sealed class NullOnlyNodeIdFactory : INodeIdFactory
        {
            public NodeId New(ISystemContext context, NodeState node)
            {
                return node.NodeId.IsNull
                    ? new NodeId(++m_nextId, 3)
                    : node.NodeId;
            }

            private uint m_nextId;
        }

        private sealed class PreserveNodeIdFactory : INodeIdFactory
        {
            public NodeId New(ISystemContext context, NodeState node)
            {
                return node.NodeId;
            }
        }

        private static SystemContext CreateContext(INodeIdFactory factory)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            return new SystemContext(telemetry)
            {
                NamespaceUris = new NamespaceTable(),
                NodeIdFactory = factory
            };
        }

        private static (BaseObjectState Root, BaseObjectState Child, PropertyState Leaf)
            BuildSubtreeWithTypeIds()
        {
            var root = new BaseObjectState(null)
            {
                NodeId = new NodeId("Device", 3),
                SymbolicName = "Device",
                BrowseName = new QualifiedName("Device", 3)
            };
            var child = new BaseObjectState(root)
            {
                NodeId = new NodeId(100, 3), // TYPE NodeId
                SymbolicName = "SoftwareUpdate",
                BrowseName = new QualifiedName("SoftwareUpdate", 3),
                ReferenceTypeId = ReferenceTypeIds.HasComponent
            };
            root.AddChild(child);
            var leaf = new PropertyState(child)
            {
                NodeId = new NodeId(101, 3), // TYPE NodeId
                SymbolicName = "CurrentVersion",
                BrowseName = new QualifiedName("CurrentVersion", 3),
                ReferenceTypeId = ReferenceTypeIds.HasProperty
            };
            child.AddChild(leaf);
            return (root, child, leaf);
        }

        [Test]
        public void AssignInstanceChildNodeIdsRebasesSubtreeTopDown()
        {
            SystemContext context = CreateContext(new ChildIdFactory());
            (BaseObjectState root, BaseObjectState child, PropertyState leaf) =
                BuildSubtreeWithTypeIds();

            context.AssignInstanceChildNodeIds(root);

            Assert.That(child.NodeId, Is.EqualTo(new NodeId("Device_SoftwareUpdate", 3)));
            Assert.That(leaf.NodeId,
                Is.EqualTo(new NodeId("Device_SoftwareUpdate_CurrentVersion", 3)),
                "Grandchild id must derive from the already-rebased child (top-down).");
            Assert.That(root.NodeId, Is.EqualTo(new NodeId("Device", 3)),
                "The root itself is left untouched; only descendants are rebased.");
        }

        [Test]
        public void AssignInstanceChildNodeIdsAvoidsCollisionAcrossInstances()
        {
            SystemContext context = CreateContext(new ChildIdFactory());

            (BaseObjectState rootA, BaseObjectState childA, _) = BuildSubtreeWithTypeIds();
            rootA.NodeId = new NodeId("DeviceA", 3);
            (BaseObjectState rootB, BaseObjectState childB, _) = BuildSubtreeWithTypeIds();
            rootB.NodeId = new NodeId("DeviceB", 3);

            context.AssignInstanceChildNodeIds(rootA);
            context.AssignInstanceChildNodeIds(rootB);

            Assert.That(childA.NodeId, Is.Not.EqualTo(childB.NodeId));
        }

        [Test]
        public void AssignInstanceChildNodeIdsAllocatesWhenFactoryRequiresNullNodeIds()
        {
            SystemContext context = CreateContext(new NullOnlyNodeIdFactory());
            (BaseObjectState root, BaseObjectState child, PropertyState leaf) =
                BuildSubtreeWithTypeIds();
            root.AddReference(ReferenceTypeIds.Organizes, false, leaf.NodeId);

            context.AssignInstanceChildNodeIds(root);

            Assert.That(child.NodeId, Is.Not.EqualTo(new NodeId(100, 3)));
            Assert.That(leaf.NodeId, Is.Not.EqualTo(new NodeId(101, 3)));
            Assert.That(child.NodeId, Is.Not.EqualTo(leaf.NodeId));

            var references = new List<IReference>();
            root.GetReferences(context, references);
            NodeId targetId = NodeId.Null;
            foreach (IReference reference in references)
            {
                if (reference.ReferenceTypeId == ReferenceTypeIds.Organizes &&
                    !reference.IsInverse)
                {
                    targetId = ExpandedNodeId.ToNodeId(reference.TargetId, context.NamespaceUris);
                    break;
                }
            }

            Assert.That(targetId, Is.EqualTo(leaf.NodeId));
        }

        [Test]
        public void AssignInstanceChildNodeIdsPreservesIdsWhenFactoryCannotAllocate()
        {
            SystemContext context = CreateContext(new PreserveNodeIdFactory());
            (BaseObjectState root, BaseObjectState child, PropertyState leaf) =
                BuildSubtreeWithTypeIds();

            context.AssignInstanceChildNodeIds(root);

            Assert.That(child.NodeId, Is.EqualTo(new NodeId(100, 3)));
            Assert.That(leaf.NodeId, Is.EqualTo(new NodeId(101, 3)));
        }

        [Test]
        public void AssignInstanceChildNodeIdsUpdatesReferencesFromOwningRoot()
        {
            SystemContext context = CreateContext(new NullOnlyNodeIdFactory());
            (BaseObjectState root, BaseObjectState child, _) = BuildSubtreeWithTypeIds();
            var sibling = new BaseObjectState(root)
            {
                NodeId = new NodeId(102, 3),
                SymbolicName = "Sibling",
                BrowseName = new QualifiedName("Sibling", 3)
            };
            root.AddChild(sibling);
            sibling.AddReference(ReferenceTypeIds.Organizes, false, child.NodeId);

            NodeId previousNodeId = context.AssignInstanceNodeId(child);
            context.AssignInstanceChildNodeIds(child, previousNodeId, root);

            var references = new List<IReference>();
            sibling.GetReferences(context, references);
            NodeId targetId = NodeId.Null;
            foreach (IReference reference in references)
            {
                if (reference.ReferenceTypeId == ReferenceTypeIds.Organizes &&
                    !reference.IsInverse)
                {
                    targetId = ExpandedNodeId.ToNodeId(reference.TargetId, context.NamespaceUris);
                    break;
                }
            }

            Assert.That(targetId, Is.EqualTo(child.NodeId));
        }

        [Test]
        public void AssignInstanceNodeIdRetriesDeclarationIdCollision()
        {
            SystemContext context = CreateContext(new NullOnlyNodeIdFactory());
            var node = new BaseObjectState(null)
            {
                NodeId = new NodeId(1, 3),
                SymbolicName = "Dynamic",
                BrowseName = new QualifiedName("Dynamic", 3)
            };

            NodeId previousNodeId = context.AssignInstanceNodeId(node);

            Assert.That(previousNodeId, Is.EqualTo(new NodeId(1, 3)));
            Assert.That(node.NodeId, Is.EqualTo(new NodeId(2, 3)));
        }

        [Test]
        public void AssignInstanceChildNodeIdsIsNoOpWithoutNodeIdFactory()
        {
            SystemContext context = CreateContext(null);
            (BaseObjectState root, BaseObjectState child, _) = BuildSubtreeWithTypeIds();

            context.AssignInstanceChildNodeIds(root);

            Assert.That(child.NodeId, Is.EqualTo(new NodeId(100, 3)),
                "Without a NodeIdFactory the type NodeIds must be left unchanged.");
        }

        [Test]
        public void AssignInstanceChildNodeIdsIsNoOpForNullNode()
        {
            SystemContext context = CreateContext(new ChildIdFactory());
            Assert.DoesNotThrow(() => context.AssignInstanceChildNodeIds(null));
        }
    }
}
