// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.State
{
    /// <summary>
    /// <see cref="NodeState"/> publishes its children lock only when the first child is
    /// added. These tests cover the paths that see a node without that lock (a leaf) and
    /// the paths that publish it.
    /// </summary>
    [TestFixture]
    [Category("NodeState")]
    [Parallelizable]
    public class NodeStateLazyChildrenLockTests
    {
        private ITelemetryContext m_telemetry;
        private SystemContext m_context;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_context = new SystemContext(m_telemetry);
        }

        [Test]
        public void LeafNodeHasNoChildrenToFindListOrRemove()
        {
            BaseObjectState leaf = CreateObject("Leaf");

            var children = new List<BaseInstanceState>();
            leaf.GetChildren(m_context, children);

            Assert.That(children, Is.Empty);
            Assert.That(leaf.FindChild(m_context, QualifiedName.From("Missing")), Is.Null);
            Assert.DoesNotThrow(() => leaf.RemoveChild(CreateChild(null!, "Orphan")));
        }

        [Test]
        public void ReplaceChildOnLeafNodeAddsTheChild()
        {
            BaseObjectState leaf = CreateObject("Leaf");
            BaseDataVariableState replacement = CreateChild(null!, "Added");

            leaf.ReplaceChild(m_context, replacement);

            Assert.That(leaf.FindChild(m_context, QualifiedName.From("Added")), Is.SameAs(replacement));
            Assert.That(replacement.Parent, Is.SameAs(leaf));
        }

        [Test]
        public void CloneOfLeafNodeHasNoChildrenAndEqualsTheSource()
        {
            BaseObjectState leaf = CreateObject("Leaf");

            var clone = (BaseObjectState)leaf.Clone();

            var children = new List<BaseInstanceState>();
            clone.GetChildren(m_context, children);
            Assert.That(children, Is.Empty);
            Assert.That(leaf.DeepEquals(clone), Is.True);
            Assert.That(clone.DeepEquals(leaf), Is.True);
        }

        [Test]
        public void DeepEqualsComparesChildrenOfLeafAndParent()
        {
            BaseObjectState leaf = CreateObject("Node");
            BaseObjectState parent = CreateObject("Node");
            parent.AddChild(CreateChild(parent, "Child"));

            Assert.That(leaf.DeepEquals(parent), Is.False);
            Assert.That(parent.DeepEquals(leaf), Is.False);
        }

        [Test]
        public void CloneOfParentCopiesChildrenAndEqualsTheSource()
        {
            BaseObjectState parent = CreateObject("Parent");
            parent.AddChild(CreateChild(parent, "Child"));

            var clone = (BaseObjectState)parent.Clone();

            BaseInstanceState clonedChild = clone.FindChild(m_context, QualifiedName.From("Child"))!;
            Assert.That(clonedChild, Is.Not.Null);
            Assert.That(clonedChild, Is.Not.SameAs(parent.FindChild(m_context, QualifiedName.From("Child"))));
            Assert.That(clonedChild.Parent, Is.SameAs(clone));
        }

        [Test]
        public void CloneChildrenCopiesChildrenOfParentAndNoneOfLeaf()
        {
            var parent = new ChildrenCloningObjectState();
            parent.AddChild(CreateChild(parent, "Child"));

            var parentClone = new ChildrenCloningObjectState();
            parent.CloneChildrenInto(parentClone);

            var children = new List<BaseInstanceState>();
            parentClone.GetChildren(m_context, children);
            Assert.That(children, Has.Count.EqualTo(1));
            Assert.That(children[0].BrowseName, Is.EqualTo(QualifiedName.From("Child")));

            var leaf = new ChildrenCloningObjectState();
            var leafClone = new ChildrenCloningObjectState();
            leaf.CloneChildrenInto(leafClone);

            children.Clear();
            leafClone.GetChildren(m_context, children);
            Assert.That(children, Is.Empty);
            Assert.That(leafClone.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        [Test]
        public void RenameChildOfParentWithoutChildrenListKeepsLaterLookupsCorrect()
        {
            BaseObjectState parent = CreateObject("Parent");
            BaseDataVariableState child = CreateChild(parent, "Before");

            // the child names the parent before it is added: the rename publishes the lock.
            child.BrowseName = QualifiedName.From("After");
            parent.AddChild(child);

            Assert.That(parent.FindChild(m_context, QualifiedName.From("After")), Is.SameAs(child));
            Assert.That(parent.FindChild(m_context, QualifiedName.From("Before")), Is.Null);
        }

        [Test]
        public void ReparentFromLeafParentDoesNotThrow()
        {
            BaseObjectState first = CreateObject("First");
            BaseObjectState second = CreateObject("Second");
            BaseDataVariableState child = CreateChild(first, "Child");

            // the first parent never had a child added, so it has no children lock yet.
            second.AddChild(child);

            Assert.That(child.Parent, Is.SameAs(second));
            Assert.That(second.FindChild(m_context, QualifiedName.From("Child")), Is.SameAs(child));
            Assert.That(first.FindChild(m_context, QualifiedName.From("Child")), Is.Null);
        }

        [Test]
        public void ConcurrentFirstAddChildKeepsEveryChild()
        {
            const int threadCount = 8;
            const int rounds = 200;

            for (int round = 0; round < rounds; round++)
            {
                BaseObjectState parent = CreateObject("Parent");
                using var start = new Barrier(threadCount);
                var tasks = new Task[threadCount];
                for (int ii = 0; ii < threadCount; ii++)
                {
                    BaseDataVariableState child = CreateChild(parent, "Child" + ii);
                    tasks[ii] = Task.Factory.StartNew(
                        () =>
                        {
                            start.SignalAndWait();
                            parent.AddChild(child);
                        },
                        CancellationToken.None,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default);
                }

                Task.WaitAll(tasks);

                var children = new List<BaseInstanceState>();
                parent.GetChildren(m_context, children);
                Assert.That(children, Has.Count.EqualTo(threadCount), $"round {round}");
            }
        }

        private static BaseObjectState CreateObject(string name)
        {
            return new BaseObjectState(null)
            {
                NodeId = new NodeId(name, 1),
                BrowseName = QualifiedName.From(name)
            };
        }

        private static BaseDataVariableState CreateChild(NodeState parent, string name)
        {
            return new BaseDataVariableState(parent)
            {
                NodeId = new NodeId(name, 1),
                BrowseName = QualifiedName.From(name),
                ReferenceTypeId = ReferenceTypeIds.HasComponent
            };
        }

        private sealed class ChildrenCloningObjectState : BaseObjectState
        {
            public ChildrenCloningObjectState()
                : base(null)
            {
                NodeId = new NodeId("Cloning", 1);
                BrowseName = QualifiedName.From("Cloning");
            }

            public void CloneChildrenInto(NodeState clone)
            {
                CloneChildren(clone);
            }
        }
    }
}
