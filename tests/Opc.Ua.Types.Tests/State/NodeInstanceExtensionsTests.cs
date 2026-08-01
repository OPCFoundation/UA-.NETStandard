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
using System.Linq;
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

        /// <summary>
        /// Records every identifier it hands out. A copy must not consume any,
        /// because each child is initialized from its source right afterwards
        /// and the assigned NodeId is discarded.
        /// </summary>
        private sealed class CountingNodeIdFactory : INodeIdFactory
        {
            public int Handouts { get; private set; }

            public NodeId New(ISystemContext context, NodeState node)
            {
                Handouts++;
                return new NodeId(++m_nextId, 3);
            }

            private uint m_nextId;
        }

        /// <summary>
        /// A hand written type that declares a child through the four argument
        /// <c>FindChild</c> only, standing in for a custom node manager written
        /// before the assignment control overload existed.
        /// </summary>
        private sealed class LegacyOwnerState : BaseObjectState
        {
            public LegacyOwnerState(NodeState parent)
                : base(parent)
            {
            }

            public PropertyState Detail { get; private set; }

            /// <summary>
            /// Whether a NodeIdFactory was visible the last time a child was
            /// created. A copy hides it from this type, because there is no way
            /// to tell a four argument override not to assign.
            /// </summary>
            public bool SawNodeIdFactory { get; private set; }

            public override void GetChildren(
                ISystemContext context,
                IList<BaseInstanceState> children)
            {
                if (Detail != null)
                {
                    children.Add(Detail);
                }
                base.GetChildren(context, children);
            }

            protected override BaseInstanceState FindChild(
                ISystemContext context,
                QualifiedName browseName,
                bool createOrReplace,
                BaseInstanceState replacement)
            {
                if (browseName.Name == "Detail")
                {
                    if (!createOrReplace)
                    {
                        return Detail;
                    }
                    SawNodeIdFactory = context.NodeIdFactory != null;
                    Detail ??= new PropertyState(this)
                    {
                        SymbolicName = "Detail",
                        BrowseName = new QualifiedName("Detail", 3),
                        ReferenceTypeId = ReferenceTypeIds.HasProperty
                    };
                    if (context.NodeIdFactory != null && Detail.NodeId.IsNull)
                    {
                        Detail.NodeId = context.NodeIdFactory.New(context, Detail);
                    }
                    return Detail;
                }
                return base.FindChild(context, browseName, createOrReplace, replacement);
            }
        }

        /// <summary>
        /// A hand written type deriving from a type that already opts into
        /// assignment control, overriding only the four argument
        /// <c>FindChild</c>. It inherits the capability as <c>true</c>, so the
        /// copy does not wrap the context up front - the factory must still be
        /// hidden by the time control reaches this override.
        /// </summary>
        private sealed class DerivedMethodState : MethodState
        {
            public DerivedMethodState(NodeState parent)
                : base(parent)
            {
            }

            public PropertyState Extra { get; private set; }

            public override void GetChildren(
                ISystemContext context,
                IList<BaseInstanceState> children)
            {
                if (Extra != null)
                {
                    children.Add(Extra);
                }
                base.GetChildren(context, children);
            }

            protected override BaseInstanceState FindChild(
                ISystemContext context,
                QualifiedName browseName,
                bool createOrReplace,
                BaseInstanceState replacement)
            {
                if (browseName.Name == "Extra")
                {
                    if (!createOrReplace)
                    {
                        return Extra;
                    }
                    Extra ??= new PropertyState(this)
                    {
                        SymbolicName = "Extra",
                        BrowseName = new QualifiedName("Extra", 3),
                        ReferenceTypeId = ReferenceTypeIds.HasProperty
                    };
                    if (context.NodeIdFactory != null && Extra.NodeId.IsNull)
                    {
                        Extra.NodeId = context.NodeIdFactory.New(context, Extra);
                    }
                    return Extra;
                }
                return base.FindChild(context, browseName, createOrReplace, replacement);
            }
        }

        /// <summary>
        /// A hand written type that intercepts child creation by overriding the
        /// public <c>CreateChild</c> rather than <c>FindChild</c>.
        /// </summary>
        private sealed class CreateChildOverrideState : BaseObjectState
        {
            public CreateChildOverrideState(NodeState parent)
                : base(parent)
            {
            }

            public int CreateChildCalls { get; private set; }

            public override BaseInstanceState CreateChild(
                ISystemContext context,
                QualifiedName browseName)
            {
                CreateChildCalls++;
                return base.CreateChild(context, browseName);
            }
        }

        /// <summary>
        /// A hand written type that opts into assignment control, standing in
        /// for what the source generator now emits.
        /// </summary>
        private sealed class ModernOwnerState : BaseObjectState
        {
            public ModernOwnerState(NodeState parent)
                : base(parent)
            {
            }

            public PropertyState Detail { get; private set; }

            /// <summary>
            /// Whether a NodeIdFactory was visible the last time a child was
            /// created. This type is asked not to assign, so it keeps seeing
            /// the real context.
            /// </summary>
            public bool SawNodeIdFactory { get; private set; }

            protected override bool SupportsInstanceNodeIdAssignmentControl => true;

            public override void GetChildren(
                ISystemContext context,
                IList<BaseInstanceState> children)
            {
                if (Detail != null)
                {
                    children.Add(Detail);
                }
                base.GetChildren(context, children);
            }

            protected override BaseInstanceState FindChild(
                ISystemContext context,
                QualifiedName browseName,
                bool createOrReplace,
                BaseInstanceState replacement)
            {
                // Never forward to the five argument overload: its unmatched
                // path returns here through the base and would recurse forever.
                return FindDeclaredChild(context, browseName, createOrReplace, true)
                    ?? base.FindChild(context, browseName, createOrReplace, replacement);
            }

            protected override BaseInstanceState FindChild(
                ISystemContext context,
                QualifiedName browseName,
                bool createOrReplace,
                BaseInstanceState replacement,
                bool assignInstanceNodeIds)
            {
                return FindDeclaredChild(
                        context, browseName, createOrReplace, assignInstanceNodeIds)
                    ?? base.FindChild(
                        context, browseName, createOrReplace, replacement,
                        assignInstanceNodeIds);
            }

            private PropertyState FindDeclaredChild(
                ISystemContext context,
                QualifiedName browseName,
                bool createOrReplace,
                bool assignInstanceNodeIds)
            {
                if (browseName.Name != "Detail")
                {
                    return null;
                }
                if (!createOrReplace)
                {
                    return Detail;
                }
                SawNodeIdFactory = context.NodeIdFactory != null;
                Detail ??= new PropertyState(this)
                {
                    SymbolicName = "Detail",
                    BrowseName = new QualifiedName("Detail", 3),
                    ReferenceTypeId = ReferenceTypeIds.HasProperty
                };
                if (assignInstanceNodeIds &&
                    context.NodeIdFactory != null &&
                    Detail.NodeId.IsNull)
                {
                    Detail.NodeId = context.NodeIdFactory.New(context, Detail);
                }
                return Detail;
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
        public void GeneratedCertificateAlarmAdderRebasesAllDescendants()
        {
            SystemContext context = CreateContext(new NullOnlyNodeIdFactory());
            var group = new CertificateGroupState(null)
            {
                NodeId = new NodeId("CertificateGroup", 3),
                SymbolicName = "CertificateGroup",
                BrowseName = new QualifiedName("CertificateGroup", 3)
            };

            group.AddCertificateExpired(context);

            CertificateExpirationAlarmState alarm = group.CertificateExpired;
            Assert.That(alarm, Is.Not.Null);
            var descendants = new List<BaseInstanceState>();
            CollectDescendants(context, alarm, descendants);

            Assert.That(descendants, Is.Not.Empty);
            Assert.That(
                descendants.Select(node => node.NodeId.NamespaceIndex),
                Is.All.EqualTo(3),
                "Runtime alarm descendants must not retain standard declaration NodeIds.");
            Assert.That(
                descendants.Select(node => node.NodeId).Distinct().Count(),
                Is.EqualTo(descendants.Count),
                "Every runtime alarm descendant must receive a unique NodeId.");
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

        /// <summary>
        /// CreateOrReplace helpers materialise a child onto an already
        /// instantiated tree, so the new child must receive a per-instance
        /// NodeId - otherwise two instances of the same type collide.
        /// </summary>
        [Test]
        public void CreateOrReplaceArgumentsAssignsInstanceNodeIds()
        {
            SystemContext context = CreateContext(new ChildIdFactory());
            MethodState first = CreateMethod("Start", 1);
            MethodState second = CreateMethod("Start", 2);

            PropertyState<ArrayOf<Argument>> firstArguments =
                first.CreateOrReplaceInputArguments(context, null);
            PropertyState<ArrayOf<Argument>> secondArguments =
                second.CreateOrReplaceInputArguments(context, null);

            Assert.That(firstArguments.NodeId.IsNull, Is.False);
            Assert.That(secondArguments.NodeId.IsNull, Is.False);
            Assert.That(firstArguments.NodeId, Is.Not.EqualTo(secondArguments.NodeId),
                "Arguments of distinct method instances must not share a NodeId.");
        }

        [Test]
        public void CreateOrReplaceArgumentsKeepsCallerAssignedNodeId()
        {
            SystemContext context = CreateContext(new ChildIdFactory());
            MethodState method = CreateMethod("Start", 1);
            var replacement = PropertyState<ArrayOf<Argument>>
                .With<StructureBuilder<Argument>>(method);
            var callerNodeId = new NodeId("CallerAssigned", 3);
            replacement.NodeId = callerNodeId;
            replacement.SymbolicName = BrowseNames.InputArguments;
            replacement.BrowseName = new QualifiedName(BrowseNames.InputArguments, 3);

            PropertyState<ArrayOf<Argument>> arguments =
                method.CreateOrReplaceInputArguments(context, replacement);

            Assert.That(arguments.NodeId, Is.EqualTo(callerNodeId));
        }

        [Test]
        public void CreateOrReplaceArgumentsHonoursTheAssignmentOptOut()
        {
            SystemContext context = CreateContext(new ChildIdFactory());
            MethodState method = CreateMethod("Start", 1);

            PropertyState<ArrayOf<Argument>> arguments =
                method.CreateOrReplaceInputArguments(context, null, assignInstanceNodeIds: false);

            Assert.That(arguments.NodeId.IsNull, Is.True,
                "Callers building declaration subtrees must keep control of the NodeIds.");
        }

        /// <summary>
        /// A copy initializes every child from its source right after creating
        /// it, which overwrites any NodeId handed out along the way. Consuming
        /// identifiers for them therefore only burns - and for factories that
        /// track outstanding allocations, leaks - them.
        /// </summary>
        [Test]
        public void CopyOfTypeWithAssignmentControlConsumesNoNodeIds()
        {
            var factory = new CountingNodeIdFactory();
            SystemContext context = CreateContext(factory);

            MethodState source = CreateMethod("Start", 1);
            source.CreateOrReplaceInputArguments(context, null);
            source.CreateOrReplaceOutputArguments(context, null);
            int handoutsAfterSource = factory.Handouts;

            var copy = new MethodState(null);
            copy.Create(context, source);

            Assert.That(factory.Handouts, Is.EqualTo(handoutsAfterSource),
                "Copying must not consume identifiers for children whose NodeId " +
                "is overwritten from the source immediately afterwards.");
        }

        /// <summary>
        /// The copy must still reproduce the source subtree while consuming
        /// nothing, otherwise the optimisation would have changed behaviour.
        /// </summary>
        [Test]
        public void CopyOfTypeWithAssignmentControlReproducesTheSource()
        {
            var factory = new CountingNodeIdFactory();
            SystemContext context = CreateContext(factory);

            MethodState source = CreateMethod("Start", 1);
            PropertyState<ArrayOf<Argument>> sourceArguments =
                source.CreateOrReplaceInputArguments(context, null);

            var copy = new MethodState(null);
            copy.Create(context, source);

            Assert.That(copy.NodeId, Is.EqualTo(source.NodeId));
            Assert.That(copy.InputArguments, Is.Not.Null);
            Assert.That(copy.InputArguments.NodeId, Is.EqualTo(sourceArguments.NodeId),
                "A copied child must carry the source NodeId, not a freshly minted one.");
        }

        /// <summary>
        /// A type that predates the assignment control overload still reaches
        /// its own four argument FindChild during a copy, and the factory is
        /// hidden from it so it cannot consume identifiers either.
        /// </summary>
        [Test]
        public void CopyOfLegacyTypeStillConsumesNoNodeIds()
        {
            var factory = new CountingNodeIdFactory();
            SystemContext context = CreateContext(factory);

            var source = new LegacyOwnerState(null)
            {
                NodeId = new NodeId("Owner", 3),
                SymbolicName = "Owner",
                BrowseName = new QualifiedName("Owner", 3)
            };
            source.CreateChild(context, new QualifiedName("Detail", 3));
            Assert.That(source.Detail, Is.Not.Null);
            int handoutsAfterSource = factory.Handouts;

            var copy = new LegacyOwnerState(null);
            copy.Create(context, source);

            Assert.That(copy.Detail, Is.Not.Null,
                "The legacy four argument override must still be reached by a copy.");
            Assert.That(factory.Handouts, Is.EqualTo(handoutsAfterSource),
                "The factory stays hidden from types that cannot be told not to assign.");
        }

        /// <summary>
        /// The fallback works by hiding the factory, which is why it is only
        /// used for types that cannot be told not to assign.
        /// </summary>
        [Test]
        public void CopyOfLegacyTypeHidesTheNodeIdFactory()
        {
            var factory = new CountingNodeIdFactory();
            SystemContext context = CreateContext(factory);

            var source = new LegacyOwnerState(null)
            {
                NodeId = new NodeId("Owner", 3),
                SymbolicName = "Owner",
                BrowseName = new QualifiedName("Owner", 3)
            };
            source.CreateChild(context, new QualifiedName("Detail", 3));

            var copy = new LegacyOwnerState(null);
            copy.Create(context, source);

            Assert.That(copy.SawNodeIdFactory, Is.False,
                "A legacy override can only be stopped by hiding the factory from it.");
        }

        /// <summary>
        /// A type that opts into assignment control is simply asked not to
        /// assign, so it keeps seeing the real context. This is the point of
        /// the overload: no wrapper misreports the factory as absent.
        /// </summary>
        [Test]
        public void CopyOfTypeWithAssignmentControlSeesTheRealContext()
        {
            var factory = new CountingNodeIdFactory();
            SystemContext context = CreateContext(factory);

            var source = new ModernOwnerState(null)
            {
                NodeId = new NodeId("Owner", 3),
                SymbolicName = "Owner",
                BrowseName = new QualifiedName("Owner", 3)
            };
            source.CreateChild(context, new QualifiedName("Detail", 3));
            int handoutsAfterSource = factory.Handouts;

            var copy = new ModernOwnerState(null);
            copy.Create(context, source);

            Assert.That(copy.Detail, Is.Not.Null);
            Assert.That(copy.SawNodeIdFactory, Is.True,
                "The context must not misreport the factory to a type that " +
                "understands the assignment request.");
            Assert.That(factory.Handouts, Is.EqualTo(handoutsAfterSource),
                "It was asked not to assign, so no identifier may be consumed.");
        }

        /// <summary>
        /// A type deriving from one that opts into assignment control inherits
        /// the capability, so the copy shows it the real context. Its four
        /// argument override cannot be told to decline, so the factory must be
        /// hidden by the time control reaches it - otherwise the inherited
        /// <c>true</c> would quietly reintroduce the leak.
        /// </summary>
        [Test]
        public void CopyOfDerivedLegacyOverrideConsumesNoNodeIds()
        {
            var factory = new CountingNodeIdFactory();
            SystemContext context = CreateContext(factory);

            var source = new DerivedMethodState(null)
            {
                NodeId = new NodeId("Start", 3),
                SymbolicName = "Start",
                BrowseName = new QualifiedName("Start", 3)
            };
            source.CreateChild(context, new QualifiedName("Extra", 3));
            source.CreateOrReplaceInputArguments(context, null);
            Assert.That(source.Extra, Is.Not.Null);
            int handoutsAfterSource = factory.Handouts;

            var copy = new DerivedMethodState(null);
            copy.Create(context, source);

            Assert.That(copy.Extra, Is.Not.Null,
                "A four argument override on a derived type must still be reached.");
            Assert.That(factory.Handouts, Is.EqualTo(handoutsAfterSource),
                "An inherited assignment control capability must not let a four " +
                "argument override consume identifiers.");
        }

        /// <summary>
        /// A type that intercepts by overriding the public CreateChild rather
        /// than FindChild must still be dispatched through by a copy.
        /// </summary>
        [Test]
        public void CopyStillDispatchesThroughCreateChildOverride()
        {
            SystemContext context = CreateContext(new CountingNodeIdFactory());

            var source = new CreateChildOverrideState(null)
            {
                NodeId = new NodeId("Owner", 3),
                SymbolicName = "Owner",
                BrowseName = new QualifiedName("Owner", 3)
            };
            var detail = new PropertyState(source)
            {
                NodeId = new NodeId("Owner_Detail", 3),
                SymbolicName = "Detail",
                BrowseName = new QualifiedName("Detail", 3),
                ReferenceTypeId = ReferenceTypeIds.HasProperty
            };
            source.AddChild(detail);

            var copy = new CreateChildOverrideState(null);
            copy.Create(context, source);

            Assert.That(copy.CreateChildCalls, Is.GreaterThan(0),
                "Overriding CreateChild must keep working across a node copy.");
        }

        /// <summary>
        /// Resolving a browse name no type in the hierarchy declares walks the
        /// whole override chain. It must terminate.
        /// </summary>
        [Test]
        public void FindingAnUndeclaredChildDoesNotRecurse()
        {
            SystemContext context = CreateContext(new CountingNodeIdFactory());
            var owner = new ModernOwnerState(null)
            {
                NodeId = new NodeId("Owner", 3),
                SymbolicName = "Owner",
                BrowseName = new QualifiedName("Owner", 3)
            };

            BaseInstanceState found = null;
            Assert.DoesNotThrow(
                () => found = owner.CreateChild(
                    context, new QualifiedName("NoSuchChild", 3), false));
            Assert.That(found, Is.Null);
        }

        /// <summary>
        /// Reading InputArguments must not return OutputArguments.
        /// </summary>
        [Test]
        public void FindChildReturnsTheRequestedArgumentsProperty()
        {
            SystemContext context = CreateContext(new ChildIdFactory());
            MethodState method = CreateMethod("Start", 1);
            PropertyState<ArrayOf<Argument>> inputs =
                method.CreateOrReplaceInputArguments(context, null);
            PropertyState<ArrayOf<Argument>> outputs =
                method.CreateOrReplaceOutputArguments(context, null);
            Assert.That(inputs.NodeId, Is.Not.EqualTo(outputs.NodeId));

            BaseInstanceState found = method.FindChild(
                context, new QualifiedName(BrowseNames.InputArguments, 3));

            Assert.That(found, Is.SameAs(inputs),
                "InputArguments must resolve to the input arguments property.");
        }

        [Test]
        public void CreateOrReplaceEnumStringsAssignsInstanceNodeIds()
        {
            SystemContext context = CreateContext(new ChildIdFactory());
            var parent = new BaseObjectState(null) { NodeId = new NodeId("Owner", 3) };
            var variable = new BaseDataVariableState(parent)
            {
                NodeId = new NodeId("Owner_Enum", 3),
                SymbolicName = "Enum",
                BrowseName = new QualifiedName("Enum", 3)
            };

            PropertyState<ArrayOf<LocalizedText>> enumStrings =
                variable.CreateOrReplaceEnumStrings(context, null);

            Assert.That(enumStrings.NodeId, Is.EqualTo(new NodeId("Owner_Enum_EnumStrings", 3)));
        }

        private static MethodState CreateMethod(string name, uint instance)
        {
            var owner = new BaseObjectState(null)
            {
                NodeId = new NodeId($"Owner{instance}", 3),
                SymbolicName = $"Owner{instance}",
                BrowseName = new QualifiedName($"Owner{instance}", 3)
            };
            var method = new MethodState(owner)
            {
                NodeId = new NodeId($"Owner{instance}_{name}", 3),
                SymbolicName = name,
                BrowseName = new QualifiedName(name, 3)
            };
            owner.AddChild(method);
            return method;
        }

        private static void CollectDescendants(
            ISystemContext context,
            NodeState node,
            List<BaseInstanceState> descendants)
        {
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                descendants.Add(child);
                CollectDescendants(context, child, descendants);
            }
        }
    }
}
