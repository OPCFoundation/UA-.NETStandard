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
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.State
{
    /// <summary>
    /// <see cref="NodeState.FindChild(ISystemContext, QualifiedName)"/> searches large
    /// children lists through a browse name index. These tests keep the results equal to
    /// a linear search of the children list while the list and its children change.
    /// </summary>
    [TestFixture]
    [Category("NodeState")]
    [Parallelizable]
    public class NodeStateChildNameIndexTests
    {
        private const int kLargeChildCount = 200;

        private ITelemetryContext m_telemetry;
        private SystemContext m_context;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_context = new SystemContext(m_telemetry);
        }

        [Test]
        public void FindChildInLargeListFindsEveryChildAndMissesUnknownNames()
        {
            BaseObjectState parent = CreateParentWithChildren(kLargeChildCount);

            for (int ii = 0; ii < kLargeChildCount; ii++)
            {
                BaseInstanceState found = parent.FindChild(m_context, Name(ii));
                Assert.That(found, Is.Not.Null, $"child {ii}");
                Assert.That(found.BrowseName, Is.EqualTo(Name(ii)));
            }

            Assert.That(parent.FindChild(m_context, QualifiedName.From("Unknown")), Is.Null);
            Assert.That(parent.FindChild(m_context, new QualifiedName("Child5", 1)), Is.Null);
        }

        [Test]
        public void FindChildInLargeListFindsChildAddedAfterLookup()
        {
            BaseObjectState parent = CreateParentWithChildren(kLargeChildCount);
            Assert.That(parent.FindChild(m_context, Name(1)), Is.Not.Null);

            BaseDataVariableState added = CreateChild(parent, "Added");
            parent.AddChild(added);

            Assert.That(parent.FindChild(m_context, QualifiedName.From("Added")), Is.SameAs(added));
        }

        [Test]
        public void FindChildInLargeListReturnsFirstChildWithDuplicateBrowseName()
        {
            BaseObjectState parent = CreateParentWithChildren(kLargeChildCount);
            BaseDataVariableState first = CreateChild(parent, "Duplicate");
            BaseDataVariableState second = CreateChild(parent, "Duplicate");
            parent.AddChild(first);
            parent.AddChild(second);

            Assert.That(parent.FindChild(m_context, QualifiedName.From("Duplicate")), Is.SameAs(first));

            parent.RemoveChild(first);

            Assert.That(parent.FindChild(m_context, QualifiedName.From("Duplicate")), Is.SameAs(second));
        }

        [Test]
        public void FindChildInLargeListDoesNotReturnRemovedChild()
        {
            BaseObjectState parent = CreateParentWithChildren(kLargeChildCount);
            BaseInstanceState child = parent.FindChild(m_context, Name(7));
            Assert.That(child, Is.Not.Null);

            parent.RemoveChild(child);

            Assert.That(parent.FindChild(m_context, Name(7)), Is.Null);
            Assert.That(parent.FindChild(m_context, Name(8)), Is.Not.Null);
        }

        [Test]
        public void FindChildInLargeListFindsRenamedChildUnderNewNameOnly()
        {
            BaseObjectState parent = CreateParentWithChildren(kLargeChildCount);
            BaseInstanceState child = parent.FindChild(m_context, Name(3));
            Assert.That(child, Is.Not.Null);

            child.BrowseName = QualifiedName.From("Renamed");

            Assert.That(parent.FindChild(m_context, QualifiedName.From("Renamed")), Is.SameAs(child));
            Assert.That(parent.FindChild(m_context, Name(3)), Is.Null);
        }

        [Test]
        public void FindChildInLargeListReturnsReplacement()
        {
            BaseObjectState parent = CreateParentWithChildren(kLargeChildCount);
            Assert.That(parent.FindChild(m_context, Name(9)), Is.Not.Null);

            BaseDataVariableState replacement = CreateChild(null, "Child9");
            parent.ReplaceChild(m_context, replacement);

            Assert.That(parent.FindChild(m_context, Name(9)), Is.SameAs(replacement));
            Assert.That(replacement.Parent, Is.SameAs(parent));
        }

        [Test]
        public void FindChildInLargeListFindsChildThatWasAddedToAnotherParent()
        {
            BaseObjectState parent = CreateParentWithChildren(kLargeChildCount);
            BaseInstanceState child = parent.FindChild(m_context, Name(11));
            Assert.That(child, Is.Not.Null);

            // AddChild moves the Parent but leaves the child in the first list.
            BaseObjectState other = CreateParentWithChildren(0);
            other.AddChild(child);
            child.BrowseName = QualifiedName.From("RenamedAfterMove");

            Assert.That(parent.FindChild(m_context, QualifiedName.From("RenamedAfterMove")), Is.SameAs(child));
            Assert.That(parent.FindChild(m_context, Name(11)), Is.Null);
            Assert.That(other.FindChild(m_context, QualifiedName.From("RenamedAfterMove")), Is.SameAs(child));
        }

        [Test]
        public void FindChildInLargeListOfClonedParentFindsRenamedChild()
        {
            BaseObjectState parent = CreateParentWithChildren(kLargeChildCount);
            Assert.That(parent.FindChild(m_context, Name(2)), Is.Not.Null);

            var clone = (BaseObjectState)parent.Clone();
            BaseInstanceState clonedChild = clone.FindChild(m_context, Name(2));
            Assert.That(clonedChild, Is.Not.Null);
            Assert.That(clonedChild, Is.Not.SameAs(parent.FindChild(m_context, Name(2))));

            clonedChild.BrowseName = QualifiedName.From("RenamedClone");

            Assert.That(clone.FindChild(m_context, QualifiedName.From("RenamedClone")), Is.SameAs(clonedChild));
            Assert.That(clone.FindChild(m_context, Name(2)), Is.Null);
            Assert.That(parent.FindChild(m_context, Name(2)), Is.Not.Null);
        }

        [Test]
        public void FindChildFindsRenamedChildAfterListShrinksAndGrowsAgain()
        {
            BaseObjectState parent = CreateParentWithChildren(kLargeChildCount);
            Assert.That(parent.FindChild(m_context, Name(0)), Is.Not.Null);

            for (int ii = kLargeChildCount - 1; ii >= 10; ii--)
            {
                parent.RemoveChild(parent.FindChild(m_context, Name(ii)));
            }

            BaseInstanceState child = parent.FindChild(m_context, Name(4));
            child.BrowseName = QualifiedName.From("RenamedWhileSmall");

            for (int ii = 10; ii < kLargeChildCount; ii++)
            {
                parent.AddChild(CreateChild(parent, Name(ii).Name));
            }

            Assert.That(parent.FindChild(m_context, QualifiedName.From("RenamedWhileSmall")), Is.SameAs(child));
            Assert.That(parent.FindChild(m_context, Name(4)), Is.Null);
            Assert.That(parent.FindChild(m_context, Name(kLargeChildCount - 1)), Is.Not.Null);
        }

        [Test]
        public void FindChildInLargeListMatchesLinearSearchAfterRandomChanges()
        {
            var random = new UnsecureRandom(4711);
            BaseObjectState parent = CreateParentWithChildren(kLargeChildCount);
            var children = new System.Collections.Generic.List<BaseInstanceState>();
            parent.GetChildren(m_context, children);

            for (int step = 0; step < 2000; step++)
            {
                switch (random.Next(4))
                {
                    case 0:
                        BaseDataVariableState added = CreateChild(parent, "Child" + random.Next(300));
                        parent.AddChild(added);
                        children.Add(added);
                        break;
                    case 1 when children.Count > 0:
                        BaseInstanceState removed = children[random.Next(children.Count)];
                        parent.RemoveChild(removed);
                        children.Remove(removed);
                        break;
                    case 2 when children.Count > 0:
                        children[random.Next(children.Count)].BrowseName =
                            QualifiedName.From("Child" + random.Next(300));
                        break;
                    default:
                        QualifiedName name = QualifiedName.From("Child" + random.Next(300));
                        BaseInstanceState expected = children.Find(c => c.BrowseName == name);
                        Assert.That(parent.FindChild(m_context, name), Is.SameAs(expected), $"step {step}");
                        break;
                }
            }
        }

        [Test]
        public void FindChildInLargeListDoesNotScanTheList()
        {
            // AddNodes looks up every new BrowseName below the parent (duplicate check and
            // NodeVersion), so a linear search made adding nodes below one parent quadratic.
            // 5000 misses against 50000 children are 250 million comparisons linearly.
            const int childCount = 50000;
            BaseObjectState parent = CreateParentWithChildren(childCount);

            int found = 0;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            for (int ii = 0; ii < 5000; ii++)
            {
                if (parent.FindChild(m_context, QualifiedName.From("Missing" + ii)) != null)
                {
                    found++;
                }
            }
            stopwatch.Stop();

            Assert.That(found, Is.Zero);
            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(500));
        }

        [Test]
        public void FindChildWithQualifiedNameComparesTheNamespaceOfGeneratedChildSlots()
        {
            // MethodState resolves InputArguments by Name only; the AddNodes duplicate check
            // must not treat a child in another namespace as a duplicate of that slot.
            var method = new MethodState(null)
            {
                NodeId = new NodeId("Method", 1),
                BrowseName = QualifiedName.From("Method")
            };
            method.CreateOrReplaceInputArguments(m_context, null);
            var otherNamespace = new QualifiedName(BrowseNames.InputArguments, 2);

            Assert.That(method.FindChild(m_context, otherNamespace), Is.SameAs(method.InputArguments));
            Assert.That(method.FindChildWithQualifiedName(m_context, otherNamespace), Is.Null);
            Assert.That(
                method.FindChildWithQualifiedName(m_context, QualifiedName.From(BrowseNames.InputArguments)),
                Is.SameAs(method.InputArguments));

            // a child list entry with the full browse name is found although the slot matched first.
            BaseDataVariableState listed = CreateChild(method, BrowseNames.InputArguments);
            listed.BrowseName = otherNamespace;
            method.AddChild(listed);

            Assert.That(method.FindChildWithQualifiedName(m_context, otherNamespace), Is.SameAs(listed));
        }

        [Test]
        public void FindChildWithQualifiedNameSearchesTheChildListWhenTheSlotIsEmpty()
        {
            var variable = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("Variable", 1),
                BrowseName = QualifiedName.From("Variable")
            };
            BaseDataVariableState listed = CreateChild(variable, BrowseNames.EnumStrings);
            variable.AddChild(listed);

            Assert.That(variable.FindChild(m_context, QualifiedName.From(BrowseNames.EnumStrings)), Is.Null);
            Assert.That(
                variable.FindChildWithQualifiedName(m_context, QualifiedName.From(BrowseNames.EnumStrings)),
                Is.SameAs(listed));
        }

        private static QualifiedName Name(int index)
        {
            return QualifiedName.From("Child" + index);
        }

        private static BaseObjectState CreateParentWithChildren(int count)
        {
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("Parent", 1),
                BrowseName = QualifiedName.From("Parent")
            };

            for (int ii = 0; ii < count; ii++)
            {
                parent.AddChild(CreateChild(parent, Name(ii).Name));
            }

            return parent;
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
    }
}
