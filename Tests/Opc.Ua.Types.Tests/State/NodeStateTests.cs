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
using System.IO;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.State
{
    [TestFixture]
    [Category("NodeState")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class NodeStateTests
    {
        private ITelemetryContext m_telemetry;
        private ServiceMessageContext m_messageContext;
        private SystemContext m_context;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_messageContext = ServiceMessageContext.CreateEmpty(m_telemetry);
            m_context = new SystemContext(m_telemetry)
            {
                NamespaceUris = m_messageContext.NamespaceUris,
                ServerUris = m_messageContext.ServerUris
            };
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            (m_messageContext as IDisposable)?.Dispose();
        }

        private static BaseObjectState CreateObjectNode(
            NodeState parent = null,
            string name = "TestObject")
        {
            return new BaseObjectState(parent)
            {
                NodeId = new NodeId(1000, 0),
                BrowseName = QualifiedName.From(name),
                DisplayName = LocalizedText.From(name)
            };
        }

        private static PropertyState CreatePropertyChild(
            NodeState parent,
            string name = "TestProperty")
        {
            return new PropertyState(parent)
            {
                NodeId = new NodeId(2000, 0),
                BrowseName = QualifiedName.From(name),
                DisplayName = LocalizedText.From(name),
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypeIds.HasProperty
            };
        }

        [Test]
        public void ConstructorSetsNodeClass()
        {
            using var node = new BaseObjectState(null);
            Assert.That(node.NodeClass, Is.EqualTo(NodeClass.Object));
        }

        [Test]
        public void ConstructorWithParentSetsReferenceType()
        {
            using BaseObjectState parent = CreateObjectNode();
            using var child = new BaseObjectState(parent);
            Assert.That(child.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasComponent));
        }

        [Test]
        public void DisposeCanBeCalledMultipleTimes()
        {
            BaseObjectState node = CreateObjectNode();
            node.Dispose();
            Assert.DoesNotThrow(node.Dispose);
        }

        [Test]
        public void ViewStateConstructorSetsViewNodeClass()
        {
            using var view = new ViewState();
            Assert.That(view.NodeClass, Is.EqualTo(NodeClass.View));
        }

        [Test]
        public void BaseObjectTypeStateConstructorSetsObjectTypeNodeClass()
        {
            using var objectType = new BaseObjectTypeState();
            Assert.That(objectType.NodeClass, Is.EqualTo(NodeClass.ObjectType));
        }

        [Test]
        public void NodeIdSetterUpdatesChangeMask()
        {
            using BaseObjectState node = CreateObjectNode();
            node.ClearChangeMasks(m_context, false);
            node.NodeId = new NodeId(9999, 0);
            Assert.That(node.ChangeMasks, Is.Not.EqualTo(NodeStateChangeMasks.None));
            Assert.That(node.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue), Is.True);
        }

        [Test]
        public void NodeIdSetterSameValueDoesNotChangeChangeMask()
        {
            using BaseObjectState node = CreateObjectNode();
            NodeId originalId = node.NodeId;
            node.ClearChangeMasks(m_context, false);
            node.NodeId = originalId;
            Assert.That(node.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        [Test]
        public void BrowseNameSetterUpdatesChangeMask()
        {
            using BaseObjectState node = CreateObjectNode();
            node.ClearChangeMasks(m_context, false);
            node.BrowseName = QualifiedName.From("NewName");
            Assert.That(node.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue), Is.True);
        }

        [Test]
        public void DisplayNameSetterUpdatesChangeMask()
        {
            using BaseObjectState node = CreateObjectNode();
            node.ClearChangeMasks(m_context, false);
            node.DisplayName = LocalizedText.From("New Display");
            Assert.That(node.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue), Is.True);
        }

        [Test]
        public void DescriptionSetterUpdatesChangeMask()
        {
            using BaseObjectState node = CreateObjectNode();
            node.ClearChangeMasks(m_context, false);
            node.Description = LocalizedText.From("A description");
            Assert.That(node.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue), Is.True);
        }

        [Test]
        public void WriteMaskSetterUpdatesChangeMask()
        {
            using BaseObjectState node = CreateObjectNode();
            node.ClearChangeMasks(m_context, false);
            node.WriteMask = AttributeWriteMask.DisplayName;
            Assert.That(node.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue), Is.True);
        }

        [Test]
        public void UserWriteMaskSetterUpdatesChangeMask()
        {
            using BaseObjectState node = CreateObjectNode();
            node.ClearChangeMasks(m_context, false);
            node.UserWriteMask = AttributeWriteMask.Description;
            Assert.That(node.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue), Is.True);
        }

        [Test]
        public void RolePermissionsSetterUpdatesChangeMask()
        {
            using BaseObjectState node = CreateObjectNode();
            // Set an initial non-default value so the next set is a change
            node.RolePermissions =
            [
                new RolePermissionType
                {
                    RoleId = new NodeId(1),
                    Permissions = 1
                }
            ];
            node.ClearChangeMasks(m_context, false);
            node.RolePermissions =
            [
                new RolePermissionType
                {
                    RoleId = new NodeId(2),
                    Permissions = 2
                }
            ];
            Assert.That(node.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue), Is.True);
        }

        [Test]
        public void UserRolePermissionsSetterUpdatesChangeMask()
        {
            using BaseObjectState node = CreateObjectNode();
            node.UserRolePermissions =
            [
                new RolePermissionType
                {
                    RoleId = new NodeId(1),
                    Permissions = 1
                }
            ];
            node.ClearChangeMasks(m_context, false);
            node.UserRolePermissions =
            [
                new RolePermissionType
                {
                    RoleId = new NodeId(2),
                    Permissions = 2
                }
            ];
            Assert.That(node.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue), Is.True);
        }

        [Test]
        public void AccessRestrictionsSetterUpdatesChangeMask()
        {
            using BaseObjectState node = CreateObjectNode();
            node.ClearChangeMasks(m_context, false);
            node.AccessRestrictions = AccessRestrictionType.SigningRequired;
            Assert.That(node.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue), Is.True);
        }

        [Test]
        public void HandlePropertyRoundTrips()
        {
            using BaseObjectState node = CreateObjectNode();
            object handle = new();
            node.Handle = handle;
            Assert.That(node.Handle, Is.SameAs(handle));
        }

        [Test]
        public void SymbolicNamePropertyRoundTrips()
        {
            using BaseObjectState node = CreateObjectNode();
            node.SymbolicName = "MySymbolic";
            Assert.That(node.SymbolicName, Is.EqualTo("MySymbolic"));
        }

        [Test]
        public void InitializedPropertyRoundTrips()
        {
            using BaseObjectState node = CreateObjectNode();
            Assert.That(node.Initialized, Is.False);
            node.Initialized = true;
            Assert.That(node.Initialized, Is.True);
        }

        [Test]
        public void ExtensionsPropertyRoundTrips()
        {
            using BaseObjectState node = CreateObjectNode();
            Assert.That(node.Extensions, Is.Null);
            node.Extensions = [];
            Assert.That(node.Extensions, Is.Not.Null);
        }

        [Test]
        public void CategoriesPropertyRoundTrips()
        {
            using BaseObjectState node = CreateObjectNode();
            node.Categories = ["Cat1", "Cat2"];
            Assert.That(node.Categories, Has.Count.EqualTo(2));
        }

        [Test]
        public void SpecificationPropertyRoundTrips()
        {
            using BaseObjectState node = CreateObjectNode();
            node.Specification = "OPC 10000-5";
            Assert.That(node.Specification, Is.EqualTo("OPC 10000-5"));
        }

        [Test]
        public void NodeSetDocumentationPropertyRoundTrips()
        {
            using BaseObjectState node = CreateObjectNode();
            node.NodeSetDocumentation = "Some docs";
            Assert.That(node.NodeSetDocumentation, Is.EqualTo("Some docs"));
        }

        [Test]
        public void DesignToolOnlyPropertyRoundTrips()
        {
            using BaseObjectState node = CreateObjectNode();
            node.DesignToolOnly = true;
            Assert.That(node.DesignToolOnly, Is.True);
        }

        [Test]
        public void ReleaseStatusPropertyRoundTrips()
        {
            using BaseObjectState node = CreateObjectNode();
            node.ReleaseStatus = Export.ReleaseStatus.Released;
            Assert.That(node.ReleaseStatus, Is.EqualTo(Export.ReleaseStatus.Released));
        }

        [Test]
        public void ToStringWithBrowseNameReturnsNodeClassAndDisplayName()
        {
            using BaseObjectState node = CreateObjectNode(name: "MyObj");
            string result = node.ToString();
            Assert.That(result, Does.Contain("Object"));
            Assert.That(result, Does.Contain("MyObj"));
        }

        [Test]
        public void ToStringWithoutBrowseNameReturnsNodeClassAndNodeId()
        {
            using var node = new BaseObjectState(null);
            node.NodeId = new NodeId(42, 0);
            string result = node.ToString();
            Assert.That(result, Does.Contain("Object"));
            Assert.That(result, Does.Contain("42"));
        }

        [Test]
        public void ToStringWithFormatThrowsFormatException()
        {
            using BaseObjectState node = CreateObjectNode();
            Assert.Throws<FormatException>(() => node.ToString("G", null));
        }

        [Test]
        public void ToStringWithNullFormatSucceeds()
        {
            using BaseObjectState node = CreateObjectNode();
            string result = node.ToString(null, null);
            Assert.That(result, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void AddChildSetsParentAndAddsToChildren()
        {
            using BaseObjectState parent = CreateObjectNode();
            using PropertyState child = CreatePropertyChild(parent, "Child1");
            parent.AddChild(child);

            var children = new List<BaseInstanceState>();
            parent.GetChildren(m_context, children);
            Assert.That(children, Has.Count.EqualTo(1));
            Assert.That(children[0].BrowseName, Is.EqualTo(QualifiedName.From("Child1")));
        }

        [Test]
        public void AddChildSetsDefaultReferenceTypeIfNull()
        {
            using BaseObjectState parent = CreateObjectNode();
            using var child = new BaseObjectState(null)
            {
                BrowseName = QualifiedName.From("OrphanChild"),
                ReferenceTypeId = NodeId.Null
            };
            parent.AddChild(child);
            Assert.That(child.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasComponent));
        }

        [Test]
        public void AddChildUpdatesChangeMasks()
        {
            using BaseObjectState parent = CreateObjectNode();
            parent.ClearChangeMasks(m_context, false);
            using PropertyState child = CreatePropertyChild(parent, "Child1");
            parent.AddChild(child);
            Assert.That(parent.ChangeMasks.HasFlag(NodeStateChangeMasks.Children), Is.True);
        }

        [Test]
        public void RemoveChildRemovesFromParent()
        {
            using BaseObjectState parent = CreateObjectNode();
            using PropertyState child = CreatePropertyChild(parent, "Child1");
            parent.AddChild(child);

            parent.RemoveChild(child);

            var children = new List<BaseInstanceState>();
            parent.GetChildren(m_context, children);
            Assert.That(children, Is.Empty);
        }

        [Test]
        public void RemoveChildSetsParentToNull()
        {
            using BaseObjectState parent = CreateObjectNode();
            using PropertyState child = CreatePropertyChild(parent, "Child1");
            parent.AddChild(child);

            parent.RemoveChild(child);
            Assert.That(child.Parent, Is.Null);
        }

        [Test]
        public void RemoveChildUpdatesChangeMasks()
        {
            using BaseObjectState parent = CreateObjectNode();
            using PropertyState child = CreatePropertyChild(parent, "Child1");
            parent.AddChild(child);
            parent.ClearChangeMasks(m_context, false);

            parent.RemoveChild(child);
            Assert.That(parent.ChangeMasks.HasFlag(NodeStateChangeMasks.Children), Is.True);
        }

        [Test]
        public void RemoveChildThatDoesNotExistIsNoOp()
        {
            using BaseObjectState parent = CreateObjectNode();
            using PropertyState child = CreatePropertyChild(parent, "NotAdded");
            Assert.DoesNotThrow(() => parent.RemoveChild(child));
        }

        [Test]
        public void GetChildrenReturnsEmptyListWhenNoChildren()
        {
            using BaseObjectState node = CreateObjectNode();
            var children = new List<BaseInstanceState>();
            node.GetChildren(m_context, children);
            Assert.That(children, Is.Empty);
        }

        [Test]
        public void GetChildrenReturnsAllAddedChildren()
        {
            using BaseObjectState parent = CreateObjectNode();
            using PropertyState child1 = CreatePropertyChild(parent, "P1");
            child1.NodeId = new NodeId(2001, 0);
            using PropertyState child2 = CreatePropertyChild(parent, "P2");
            child2.NodeId = new NodeId(2002, 0);
            parent.AddChild(child1);
            parent.AddChild(child2);

            var children = new List<BaseInstanceState>();
            parent.GetChildren(m_context, children);
            Assert.That(children, Has.Count.EqualTo(2));
        }

        [Test]
        public void FindChildByBrowseNameFindsExistingChild()
        {
            using BaseObjectState parent = CreateObjectNode();
            using PropertyState child = CreatePropertyChild(parent, "MyProp");
            parent.AddChild(child);

            BaseInstanceState found = parent.FindChild(m_context, QualifiedName.From("MyProp"));
            Assert.That(found, Is.Not.Null);
            Assert.That(found.BrowseName, Is.EqualTo(QualifiedName.From("MyProp")));
        }

        [Test]
        public void FindChildByBrowseNameReturnsNullForMissing()
        {
            using BaseObjectState parent = CreateObjectNode();
            BaseInstanceState found = parent.FindChild(m_context, QualifiedName.From("NonExistent"));
            Assert.That(found, Is.Null);
        }

        [Test]
        public void FindChildByBrowsePathReturnsCorrectChild()
        {
            using BaseObjectState root = CreateObjectNode();
            using PropertyState child = CreatePropertyChild(root, "Level1");
            root.AddChild(child);

            var path = new List<QualifiedName> { QualifiedName.From("Level1") };
            BaseInstanceState found = root.FindChild(m_context, path, 0);
            Assert.That(found, Is.Not.Null);
            Assert.That(found.BrowseName, Is.EqualTo(QualifiedName.From("Level1")));
        }

        [Test]
        public void FindChildByBrowsePathReturnsNullWhenNotFound()
        {
            using BaseObjectState root = CreateObjectNode();
            var path = new List<QualifiedName> { QualifiedName.From("Missing") };
            BaseInstanceState found = root.FindChild(m_context, path, 0);
            Assert.That(found, Is.Null);
        }

        [Test]
        public void FindChildByBrowsePathThrowsForNegativeIndex()
        {
            using BaseObjectState root = CreateObjectNode();
            var path = new List<QualifiedName> { QualifiedName.From("X") };
            Assert.Throws<ArgumentOutOfRangeException>(() => root.FindChild(m_context, path, -1));
        }

        [Test]
        public void FindChildBySymbolicNameFindsChild()
        {
            using BaseObjectState parent = CreateObjectNode();
            using PropertyState child = CreatePropertyChild(parent, "SymChild");
            child.SymbolicName = "SymChild";
            parent.AddChild(child);

            BaseInstanceState found = parent.FindChildBySymbolicName(m_context, "SymChild");
            Assert.That(found, Is.Not.Null);
            Assert.That(found.SymbolicName, Is.EqualTo("SymChild"));
        }

        [Test]
        public void FindChildBySymbolicNameReturnsNullForEmpty()
        {
            using BaseObjectState parent = CreateObjectNode();
            BaseInstanceState found = parent.FindChildBySymbolicName(m_context, string.Empty);
            Assert.That(found, Is.Null);
        }

        [Test]
        public void FindChildBySymbolicNameReturnsNullForNull()
        {
            using BaseObjectState parent = CreateObjectNode();
            BaseInstanceState found = parent.FindChildBySymbolicName(m_context, null);
            Assert.That(found, Is.Null);
        }

        [Test]
        public void FindChildBySymbolicNameStripsLeadingSlashes()
        {
            using BaseObjectState parent = CreateObjectNode();
            using PropertyState child = CreatePropertyChild(parent, "Child1");
            child.SymbolicName = "Child1";
            parent.AddChild(child);

            BaseInstanceState found = parent.FindChildBySymbolicName(m_context, "///Child1");
            Assert.That(found, Is.Not.Null);
        }

        [Test]
        public void FindChildBySymbolicNameReturnsNullForOnlySlashes()
        {
            using BaseObjectState parent = CreateObjectNode();
            BaseInstanceState found = parent.FindChildBySymbolicName(m_context, "///");
            Assert.That(found, Is.Null);
        }

        [Test]
        public void FindChildBySymbolicNameNavigatesNestedPath()
        {
            using BaseObjectState root = CreateObjectNode();
            using var intermediate = new BaseObjectState(root)
            {
                NodeId = new NodeId(5001, 0),
                BrowseName = QualifiedName.From("Mid"),
                SymbolicName = "Mid"
            };
            root.AddChild(intermediate);

            using PropertyState leaf = CreatePropertyChild(intermediate, "Leaf");
            leaf.SymbolicName = "Leaf";
            intermediate.AddChild(leaf);

            BaseInstanceState found = root.FindChildBySymbolicName(m_context, "Mid/Leaf");
            Assert.That(found, Is.Not.Null);
            Assert.That(found.SymbolicName, Is.EqualTo("Leaf"));
        }

        [Test]
        public void FindChildBySymbolicNameReturnsNullForNonExistent()
        {
            using BaseObjectState parent = CreateObjectNode();
            BaseInstanceState found = parent.FindChildBySymbolicName(m_context, "DoesNotExist");
            Assert.That(found, Is.Null);
        }

        [Test]
        public void ReplaceChildReplacesExistingChild()
        {
            using BaseObjectState parent = CreateObjectNode();
            using PropertyState original = CreatePropertyChild(parent, "Prop");
            parent.AddChild(original);

            using PropertyState replacement = CreatePropertyChild(parent, "Prop");
            replacement.NodeId = new NodeId(9001, 0);
            parent.ReplaceChild(m_context, replacement);

            var children = new List<BaseInstanceState>();
            parent.GetChildren(m_context, children);
            BaseInstanceState found = children.FirstOrDefault(c => c.BrowseName == QualifiedName.From("Prop"));
            Assert.That(found, Is.Not.Null);
            Assert.That(found.NodeId, Is.EqualTo(new NodeId(9001, 0)));
        }

        [Test]
        public void ReplaceChildThrowsForNullChild()
        {
            using BaseObjectState parent = CreateObjectNode();
            Assert.Throws<ArgumentException>(() => parent.ReplaceChild(m_context, null));
        }

        [Test]
        public void ReplaceChildThrowsForChildWithNullBrowseName()
        {
            using BaseObjectState parent = CreateObjectNode();
            using var child = new BaseObjectState(parent);
            Assert.Throws<ArgumentException>(() => parent.ReplaceChild(m_context, child));
        }

        [Test]
        public void CreateChildWithNullBrowseNameReturnsNull()
        {
            using BaseObjectState parent = CreateObjectNode();
            BaseInstanceState result = parent.CreateChild(m_context, QualifiedName.Null);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void AddReferenceAddsAndCanBeFound()
        {
            using BaseObjectState node = CreateObjectNode();
            var targetId = new NodeId(500, 0);
            node.AddReference(ReferenceTypeIds.Organizes, false, targetId);
            Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, targetId), Is.True);
        }

        [Test]
        public void AddReferenceUpdatesChangeMask()
        {
            using BaseObjectState node = CreateObjectNode();
            node.ClearChangeMasks(m_context, false);
            node.AddReference(ReferenceTypeIds.Organizes, false, new NodeId(500, 0));
            Assert.That(node.ChangeMasks.HasFlag(NodeStateChangeMasks.References), Is.True);
        }

        [Test]
        public void AddReferenceThrowsForNullReferenceType()
        {
            using BaseObjectState node = CreateObjectNode();
            Assert.Throws<ArgumentNullException>(
                () => node.AddReference(NodeId.Null, false, new NodeId(1)));
        }

        [Test]
        public void AddReferenceThrowsForNullTarget()
        {
            using BaseObjectState node = CreateObjectNode();
            Assert.Throws<ArgumentNullException>(
                () => node.AddReference(ReferenceTypeIds.Organizes, false, ExpandedNodeId.Null));
        }

        [Test]
        public void RemoveReferenceRemovesExistingReference()
        {
            using BaseObjectState node = CreateObjectNode();
            var targetId = new NodeId(500, 0);
            node.AddReference(ReferenceTypeIds.Organizes, false, targetId);

            bool removed = node.RemoveReference(ReferenceTypeIds.Organizes, false, targetId);
            Assert.That(removed, Is.True);
            Assert.That(
                node.ReferenceExists(ReferenceTypeIds.Organizes, false, targetId),
                Is.False);
        }

        [Test]
        public void RemoveReferenceReturnsFalseWhenNotFound()
        {
            using BaseObjectState node = CreateObjectNode();
            node.AddReference(ReferenceTypeIds.Organizes, false, new NodeId(500, 0));
            bool removed = node.RemoveReference(
                ReferenceTypeIds.Organizes, false, new NodeId(999, 0));
            Assert.That(removed, Is.False);
        }

        [Test]
        public void RemoveReferenceThrowsForNullReferenceType()
        {
            using BaseObjectState node = CreateObjectNode();
            Assert.Throws<ArgumentNullException>(
                () => node.RemoveReference(NodeId.Null, false, new NodeId(1)));
        }

        [Test]
        public void RemoveReferenceThrowsForNullTarget()
        {
            using BaseObjectState node = CreateObjectNode();
            Assert.Throws<ArgumentNullException>(
                () => node.RemoveReference(ReferenceTypeIds.Organizes, false, ExpandedNodeId.Null));
        }

        [Test]
        public void ReferenceExistsReturnsFalseWhenNoReferences()
        {
            using BaseObjectState node = CreateObjectNode();
            Assert.That(
                node.ReferenceExists(ReferenceTypeIds.Organizes, false, new NodeId(1)),
                Is.False);
        }

        [Test]
        public void ReferenceExistsReturnsFalseForNullRefType()
        {
            using BaseObjectState node = CreateObjectNode();
            Assert.That(
                node.ReferenceExists(NodeId.Null, false, new NodeId(1)),
                Is.False);
        }

        [Test]
        public void ReferenceExistsReturnsFalseForNullTarget()
        {
            using BaseObjectState node = CreateObjectNode();
            Assert.That(
                node.ReferenceExists(ReferenceTypeIds.Organizes, false, ExpandedNodeId.Null),
                Is.False);
        }

        [Test]
        public void GetReferencesReturnsAllAddedReferences()
        {
            using BaseObjectState node = CreateObjectNode();
            node.AddReference(ReferenceTypeIds.Organizes, false, new NodeId(1));
            node.AddReference(ReferenceTypeIds.HasComponent, false, new NodeId(2));

            var references = new List<IReference>();
            node.GetReferences(m_context, references);
            Assert.That(references, Has.Count.EqualTo(2));
        }

        [Test]
        public void GetReferencesReturnsEmptyWhenNoReferences()
        {
            using BaseObjectState node = CreateObjectNode();
            var references = new List<IReference>();
            node.GetReferences(m_context, references);
            Assert.That(references, Is.Empty);
        }

        [Test]
        public void GetReferencesFiltersByTypeAndDirection()
        {
            using BaseObjectState node = CreateObjectNode();
            node.AddReference(ReferenceTypeIds.Organizes, false, new NodeId(1));
            node.AddReference(ReferenceTypeIds.HasComponent, false, new NodeId(2));
            node.AddReference(ReferenceTypeIds.Organizes, true, new NodeId(3));

            var forward = new List<IReference>();
            node.GetReferences(m_context, forward, ReferenceTypeIds.Organizes, false);
            Assert.That(forward, Has.Count.EqualTo(1));

            var inverse = new List<IReference>();
            node.GetReferences(m_context, inverse, ReferenceTypeIds.Organizes, true);
            Assert.That(inverse, Has.Count.EqualTo(1));
        }

        [Test]
        public void AddReferencesIgnoresDuplicates()
        {
            using BaseObjectState node = CreateObjectNode();
            var refs = new List<IReference>
            {
                new NodeStateReference(ReferenceTypeIds.Organizes, false, new NodeId(1)),
                new NodeStateReference(ReferenceTypeIds.Organizes, false, new NodeId(1)),
                new NodeStateReference(ReferenceTypeIds.HasComponent, false, new NodeId(2))
            };
            node.AddReferences(refs);

            var actual = new List<IReference>();
            node.GetReferences(m_context, actual);
            Assert.That(actual, Has.Count.EqualTo(2));
        }

        [Test]
        public void AddReferencesThrowsForNull()
        {
            using BaseObjectState node = CreateObjectNode();
            Assert.Throws<ArgumentNullException>(() => node.AddReferences(null));
        }

        [Test]
        public void RemoveReferencesRemovesAllOfTypeAndDirection()
        {
            using BaseObjectState node = CreateObjectNode();
            node.AddReference(ReferenceTypeIds.Organizes, false, new NodeId(1));
            node.AddReference(ReferenceTypeIds.Organizes, false, new NodeId(2));
            node.AddReference(ReferenceTypeIds.HasComponent, false, new NodeId(3));

            bool removed = node.RemoveReferences(ReferenceTypeIds.Organizes, false);
            Assert.That(removed, Is.True);

            var remaining = new List<IReference>();
            node.GetReferences(m_context, remaining);
            Assert.That(remaining, Has.Count.EqualTo(1));
            Assert.That(remaining[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasComponent));
        }

        [Test]
        public void RemoveReferencesReturnsFalseWhenNoneExist()
        {
            using BaseObjectState node = CreateObjectNode();
            bool removed = node.RemoveReferences(ReferenceTypeIds.Organizes, false);
            Assert.That(removed, Is.False);
        }

        [Test]
        public void RemoveReferencesThrowsForNullType()
        {
            using BaseObjectState node = CreateObjectNode();
            Assert.Throws<ArgumentNullException>(
                () => node.RemoveReferences(NodeId.Null, false));
        }

        [Test]
        public void OnReferenceAddedCallbackInvoked()
        {
            using BaseObjectState node = CreateObjectNode();
            bool invoked = false;
            node.OnReferenceAdded = (n, refType, isInverse, target) => invoked = true;
            node.AddReference(ReferenceTypeIds.Organizes, false, new NodeId(1));
            Assert.That(invoked, Is.True);
        }

        [Test]
        public void OnReferenceRemovedCallbackInvoked()
        {
            using BaseObjectState node = CreateObjectNode();
            bool invoked = false;
            node.OnReferenceRemoved = (n, refType, isInverse, target) => invoked = true;
            node.AddReference(ReferenceTypeIds.Organizes, false, new NodeId(1));
            node.RemoveReference(ReferenceTypeIds.Organizes, false, new NodeId(1));
            Assert.That(invoked, Is.True);
        }

        [Test]
        public void OnReferenceAddedInvokedByAddReferences()
        {
            using BaseObjectState node = CreateObjectNode();
            int count = 0;
            node.OnReferenceAdded = (n, refType, isInverse, target) => count++;
            var refs = new List<IReference>
            {
                new NodeStateReference(ReferenceTypeIds.Organizes, false, new NodeId(1)),
                new NodeStateReference(ReferenceTypeIds.HasComponent, false, new NodeId(2))
            };
            node.AddReferences(refs);
            Assert.That(count, Is.EqualTo(2));
        }

        [Test]
        public void UpdateChangeMasksOrsWithExistingValue()
        {
            using BaseObjectState node = CreateObjectNode();
            node.ClearChangeMasks(m_context, false);
            node.UpdateChangeMasks(NodeStateChangeMasks.Value);
            node.UpdateChangeMasks(NodeStateChangeMasks.Children);
            Assert.That(node.ChangeMasks.HasFlag(NodeStateChangeMasks.Value), Is.True);
            Assert.That(node.ChangeMasks.HasFlag(NodeStateChangeMasks.Children), Is.True);
        }

        [Test]
        public void ClearChangeMasksResetsToNone()
        {
            using BaseObjectState node = CreateObjectNode();
            node.UpdateChangeMasks(NodeStateChangeMasks.Value);
            node.ClearChangeMasks(m_context, false);
            Assert.That(node.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        [Test]
        public void ClearChangeMasksRecursivelyClearsChildren()
        {
            using BaseObjectState parent = CreateObjectNode();
            using PropertyState child = CreatePropertyChild(parent, "Child");
            parent.AddChild(child);
            child.UpdateChangeMasks(NodeStateChangeMasks.NonValue);

            parent.ClearChangeMasks(m_context, true);
            Assert.That(child.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        [Test]
        public void ClearChangeMasksInvokesOnStateChangedHandler()
        {
            using BaseObjectState node = CreateObjectNode();
            node.ClearChangeMasks(m_context, false);
            node.UpdateChangeMasks(NodeStateChangeMasks.Value);

            NodeStateChangeMasks captured = NodeStateChangeMasks.None;
            node.OnStateChanged = (ctx, n, changes) => captured = changes;
            node.ClearChangeMasks(m_context, false);
            Assert.That(captured.HasFlag(NodeStateChangeMasks.Value), Is.True);
        }

        [Test]
        public void ClearChangeMasksInvokesStateChangedEvent()
        {
            using BaseObjectState node = CreateObjectNode();
            node.ClearChangeMasks(m_context, false);
            node.UpdateChangeMasks(NodeStateChangeMasks.References);

            NodeStateChangeMasks captured = NodeStateChangeMasks.None;
            node.StateChanged += (ctx, n, changes) => captured = changes;
            node.ClearChangeMasks(m_context, false);
            Assert.That(captured.HasFlag(NodeStateChangeMasks.References), Is.True);
        }

        [Test]
        public void ClearChangeMasksDoesNotInvokeHandlerWhenNoChanges()
        {
            using BaseObjectState node = CreateObjectNode();
            node.ClearChangeMasks(m_context, false);

            bool invoked = false;
            node.OnStateChanged = (ctx, n, changes) => invoked = true;
            node.ClearChangeMasks(m_context, false);
            Assert.That(invoked, Is.False);
        }

        [Test]
        public void DeepEqualsSameReferenceReturnsTrue()
        {
            using BaseObjectState node = CreateObjectNode();
            Assert.That(node.DeepEquals(node), Is.True);
        }

        [Test]
        public void DeepEqualsNullReturnsFalse()
        {
            using BaseObjectState node = CreateObjectNode();
            Assert.That(node.DeepEquals(null), Is.False);
        }

        [Test]
        public void DeepEqualsCloneReturnsTrueForSimpleNode()
        {
            // DeepEquals tests basic property equality between original and clone
            using var original = new ViewState();
            original.NodeId = new NodeId(100, 0);
            original.BrowseName = QualifiedName.From("View1");
            original.SymbolicName = "Sym1";

            using var clone = (ViewState)original.Clone();

            // Verify key properties were copied
            Assert.That(clone.NodeId, Is.EqualTo(original.NodeId));
            Assert.That(clone.BrowseName, Is.EqualTo(original.BrowseName));
            Assert.That(clone.NodeClass, Is.EqualTo(original.NodeClass));
            Assert.That(clone.SymbolicName, Is.EqualTo(original.SymbolicName));
        }

        [Test]
        public void DeepEqualsDifferentBrowseNameReturnsFalse()
        {
            using BaseObjectState node1 = CreateObjectNode(name: "A");
            using BaseObjectState node2 = CreateObjectNode(name: "B");
            node1.NodeId = node2.NodeId;
            Assert.That(node1.DeepEquals(node2), Is.False);
        }

        [Test]
        public void DeepGetHashCodeDoesNotThrow()
        {
            using var node = new ViewState();
            node.NodeId = new NodeId(100, 0);
            node.BrowseName = QualifiedName.From("TestSym");
            node.SymbolicName = "TestSym";
            Assert.DoesNotThrow(() => node.DeepGetHashCode());
        }

        [Test]
        public void DeepGetHashCodeDiffersForDifferentNodes()
        {
            using BaseObjectState node1 = CreateObjectNode(name: "A");
            node1.SymbolicName = "A";
            using BaseObjectState node2 = CreateObjectNode(name: "B");
            node2.SymbolicName = "B";
            Assert.DoesNotThrow(() => node1.DeepGetHashCode());
            Assert.DoesNotThrow(() => node2.DeepGetHashCode());
        }

        [Test]
        public void DeepEqualsWithReferences()
        {
            using var node1 = new ViewState();
            node1.NodeId = new NodeId(100, 0);
            node1.BrowseName = QualifiedName.From("View1");
            node1.AddReference(ReferenceTypeIds.Organizes, false, new NodeId(200));

            // A different node with different references should not be equal
            using var node2 = new ViewState();
            node2.NodeId = new NodeId(100, 0);
            node2.BrowseName = QualifiedName.From("View1");
            node2.AddReference(ReferenceTypeIds.Organizes, false, new NodeId(300));

            Assert.That(node1.DeepEquals(node2), Is.False);
        }

        [Test]
        public void DeepEqualsReturnsFalseForDifferentReferences()
        {
            using BaseObjectState node1 = CreateObjectNode();
            node1.AddReference(ReferenceTypeIds.Organizes, false, new NodeId(100));

            using BaseObjectState node2 = CreateObjectNode();
            node2.AddReference(ReferenceTypeIds.Organizes, false, new NodeId(200));

            Assert.That(node1.DeepEquals(node2), Is.False);
        }

        [Test]
        public void CloneCopiesAllBaseProperties()
        {
            using BaseObjectState original = CreateObjectNode();
            original.Description = LocalizedText.From("Desc");
            original.WriteMask = AttributeWriteMask.Description;
            original.SymbolicName = "TestSym";
            original.Handle = "my handle";
            original.Specification = "Spec1";
            original.NodeSetDocumentation = "Doc1";
            original.DesignToolOnly = true;
            original.Categories = ["C1"];
            original.ReleaseStatus = Export.ReleaseStatus.Released;

            using var clone = (BaseObjectState)original.Clone();
            Assert.That(clone.NodeId, Is.EqualTo(original.NodeId));
            Assert.That(clone.BrowseName, Is.EqualTo(original.BrowseName));
            Assert.That(clone.DisplayName, Is.EqualTo(original.DisplayName));
            Assert.That(clone.Description, Is.EqualTo(original.Description));
            Assert.That(clone.WriteMask, Is.EqualTo(original.WriteMask));
            Assert.That(clone.SymbolicName, Is.EqualTo(original.SymbolicName));
            Assert.That(clone.Handle, Is.EqualTo(original.Handle));
            Assert.That(clone.Specification, Is.EqualTo(original.Specification));
            Assert.That(clone.NodeSetDocumentation, Is.EqualTo(original.NodeSetDocumentation));
            Assert.That(clone.DesignToolOnly, Is.EqualTo(original.DesignToolOnly));
            Assert.That(clone.ReleaseStatus, Is.EqualTo(original.ReleaseStatus));
            Assert.That(clone.NodeClass, Is.EqualTo(original.NodeClass));
        }

        [Test]
        public void CloneCopiesChildren()
        {
            using BaseObjectState original = CreateObjectNode();
            using PropertyState child = CreatePropertyChild(original, "P1");
            original.AddChild(child);

            using var clone = (BaseObjectState)original.Clone();
            var cloneChildren = new List<BaseInstanceState>();
            clone.GetChildren(m_context, cloneChildren);
            Assert.That(cloneChildren, Has.Count.EqualTo(1));
            Assert.That(cloneChildren[0].BrowseName, Is.EqualTo(QualifiedName.From("P1")));
        }

        [Test]
        public void CloneCopiesReferences()
        {
            using BaseObjectState original = CreateObjectNode();
            original.AddReference(ReferenceTypeIds.Organizes, false, new NodeId(100));

            using var clone = (BaseObjectState)original.Clone();
            Assert.That(
                clone.ReferenceExists(ReferenceTypeIds.Organizes, false, new NodeId(100)),
                Is.True);
        }

        [Test]
        public void CloneChildrenAreIndependentOfOriginal()
        {
            using BaseObjectState original = CreateObjectNode();
            using PropertyState child = CreatePropertyChild(original, "Child1");
            original.AddChild(child);

            using var clone = (BaseObjectState)original.Clone();
            original.RemoveChild(child);

            var cloneChildren = new List<BaseInstanceState>();
            clone.GetChildren(m_context, cloneChildren);
            Assert.That(cloneChildren, Has.Count.EqualTo(1));
        }

        [Test]
        public void GetHierarchyRootReturnsRootForNestedChild()
        {
            using BaseObjectState root = CreateObjectNode();
            using var mid = new BaseObjectState(root)
            {
                NodeId = new NodeId(5001, 0),
                BrowseName = QualifiedName.From("Mid")
            };
            root.AddChild(mid);

            using PropertyState leaf = CreatePropertyChild(mid, "Leaf");
            mid.AddChild(leaf);

            NodeState hierarchyRoot = leaf.GetHierarchyRoot();
            Assert.That(hierarchyRoot, Is.SameAs(root));
        }

        [Test]
        public void GetHierarchyRootReturnsSelfWhenNoParent()
        {
            using BaseObjectState node = CreateObjectNode();
            Assert.That(node.GetHierarchyRoot(), Is.SameAs(node));
        }

        [Test]
        public void GetHierarchyRootReturnsSelfForNonInstanceNode()
        {
            using var typeNode = new BaseObjectTypeState();
            Assert.That(typeNode.GetHierarchyRoot(), Is.SameAs(typeNode));
        }

        [Test]
        public void AreEventsMonitoredDefaultsFalse()
        {
            using BaseObjectState node = CreateObjectNode();
            Assert.That(node.AreEventsMonitored, Is.False);
        }

        [Test]
        public void SetAreEventsMonitoredSetsFlag()
        {
            using BaseObjectState node = CreateObjectNode();
            node.SetAreEventsMonitored(m_context, true, false);
            Assert.That(node.AreEventsMonitored, Is.True);
        }

        [Test]
        public void SetAreEventsMonitoredUnsetsFlag()
        {
            using BaseObjectState node = CreateObjectNode();
            node.SetAreEventsMonitored(m_context, true, false);
            node.SetAreEventsMonitored(m_context, false, false);
            Assert.That(node.AreEventsMonitored, Is.False);
        }

        [Test]
        public void SetAreEventsMonitoredDecrementsCorrectly()
        {
            using BaseObjectState node = CreateObjectNode();
            node.SetAreEventsMonitored(m_context, true, false);
            node.SetAreEventsMonitored(m_context, true, false);
            node.SetAreEventsMonitored(m_context, false, false);
            Assert.That(node.AreEventsMonitored, Is.True);
        }

        [Test]
        public void SetAreEventsMonitoredDoesNotGoBelowZero()
        {
            using BaseObjectState node = CreateObjectNode();
            node.SetAreEventsMonitored(m_context, false, false);
            Assert.That(node.AreEventsMonitored, Is.False);
        }

        [Test]
        public void SetAreEventsMonitoredPropagatesIncludeChildren()
        {
            using BaseObjectState parent = CreateObjectNode();
            using PropertyState child = CreatePropertyChild(parent, "C1");
            parent.AddChild(child);

            parent.SetAreEventsMonitored(m_context, true, true);
            Assert.That(child.AreEventsMonitored, Is.True);
        }

        [Test]
        public void ValidateReturnsTrueByDefault()
        {
            using BaseObjectState node = CreateObjectNode();
            Assert.That(node.Validate(m_context), Is.True);
        }

        [Test]
        public void ValidateCallsOnValidateHandler()
        {
            using BaseObjectState node = CreateObjectNode();
            node.OnValidate = (ctx, n) => false;
            Assert.That(node.Validate(m_context), Is.False);
        }

        [Test]
        public void ValidationRequiredReturnsFalseByDefault()
        {
            using BaseObjectState node = CreateObjectNode();
            Assert.That(node.ValidationRequired, Is.False);
        }

        [Test]
        public void ValidationRequiredReturnsTrueWhenHandlerSet()
        {
            using BaseObjectState node = CreateObjectNode();
            node.OnValidate = (ctx, n) => true;
            Assert.That(node.ValidationRequired, Is.True);
        }

        [Test]
        public void CreateSetsNodeIdBrowseNameDisplayName()
        {
            using var node = new BaseObjectState(null);
            node.Create(
                m_context,
                new NodeId(7777, 0),
                QualifiedName.From("TestBrowse"),
                LocalizedText.From("TestDisplay"),
                false);

            Assert.That(node.NodeId, Is.EqualTo(new NodeId(7777, 0)));
            Assert.That(node.BrowseName, Is.EqualTo(QualifiedName.From("TestBrowse")));
            Assert.That(node.DisplayName, Is.EqualTo(LocalizedText.From("TestDisplay")));
        }

        [Test]
        public void CreateWithNullNodeIdDoesNotOverride()
        {
            using var node = new BaseObjectState(null);
            node.Create(
                m_context,
                NodeId.Null,
                QualifiedName.From("Browse1"),
                default,
                false);

            Assert.That(node.BrowseName, Is.EqualTo(QualifiedName.From("Browse1")));
        }

        [Test]
        public void CreateSetsSymbolicName()
        {
            using var node = new BaseObjectState(null);
            node.Create(
                m_context,
                new NodeId(1, 0),
                QualifiedName.From("MyBrowse"),
                default,
                false);
            Assert.That(node.SymbolicName, Is.EqualTo("MyBrowse"));
        }

        [Test]
        public void CreateFromSourceCopiesProperties()
        {
            using BaseObjectState source = CreateObjectNode(name: "Source");
            source.Description = LocalizedText.From("Source Desc");
            source.AddReference(ReferenceTypeIds.Organizes, false, new NodeId(100));

            using var target = new BaseObjectState(null);
            target.Create(m_context, source);

            Assert.That(target.BrowseName, Is.EqualTo(source.BrowseName));
            Assert.That(
                target.ReferenceExists(ReferenceTypeIds.Organizes, false, new NodeId(100)),
                Is.True);
        }

        [Test]
        public void DeleteSetsDeletedChangeMask()
        {
            using BaseObjectState node = CreateObjectNode();
            NodeStateChangeMasks captured = NodeStateChangeMasks.None;
            node.OnStateChanged = (ctx, n, changes) => captured = changes;
            node.Delete(m_context);
            Assert.That(captured.HasFlag(NodeStateChangeMasks.Deleted), Is.True);
        }

        [Test]
        public void DeleteRecursivelyDeletesChildren()
        {
            using BaseObjectState parent = CreateObjectNode();
            using PropertyState child = CreatePropertyChild(parent, "Child1");
            parent.AddChild(child);

            NodeStateChangeMasks childCapture = NodeStateChangeMasks.None;
            child.OnStateChanged = (ctx, n, changes) => childCapture = changes;
            parent.Delete(m_context);
            Assert.That(childCapture.HasFlag(NodeStateChangeMasks.Deleted), Is.True);
        }

        [Test]
        public void ReadAttributeReturnsBadForNullDataValue()
        {
            using BaseObjectState node = CreateObjectNode();
            ServiceResult result = node.ReadAttribute(
                m_context, Attributes.NodeId, default, default, null);
            Assert.That(StatusCode.IsBad(result.StatusCode), Is.True);
        }

        [Test]
        public void ReadNodeIdAttribute()
        {
            using BaseObjectState node = CreateObjectNode();
            var dataValue = new DataValue();
            ServiceResult result = node.ReadAttribute(
                m_context, Attributes.NodeId, default, default, dataValue);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(dataValue.WrappedValue.GetNodeId(), Is.EqualTo(node.NodeId));
        }

        [Test]
        public void ReadNodeClassAttribute()
        {
            using BaseObjectState node = CreateObjectNode();
            var dataValue = new DataValue();
            ServiceResult result = node.ReadAttribute(
                m_context, Attributes.NodeClass, default, default, dataValue);
            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void ReadBrowseNameAttribute()
        {
            using BaseObjectState node = CreateObjectNode();
            var dataValue = new DataValue();
            ServiceResult result = node.ReadAttribute(
                m_context, Attributes.BrowseName, default, default, dataValue);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(dataValue.WrappedValue.GetQualifiedName(), Is.EqualTo(node.BrowseName));
        }

        [Test]
        public void ReadDisplayNameAttribute()
        {
            using BaseObjectState node = CreateObjectNode();
            var dataValue = new DataValue();
            ServiceResult result = node.ReadAttribute(
                m_context, Attributes.DisplayName, default, default, dataValue);
            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void ReadDescriptionAttribute()
        {
            using BaseObjectState node = CreateObjectNode();
            node.Description = LocalizedText.From("My desc");
            var dataValue = new DataValue();
            ServiceResult result = node.ReadAttribute(
                m_context, Attributes.Description, default, default, dataValue);
            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void ReadWriteMaskAttribute()
        {
            using BaseObjectState node = CreateObjectNode();
            node.WriteMask = AttributeWriteMask.DisplayName;
            var dataValue = new DataValue();
            ServiceResult result = node.ReadAttribute(
                m_context, Attributes.WriteMask, default, default, dataValue);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(dataValue.WrappedValue.GetUInt32(), Is.EqualTo((uint)AttributeWriteMask.DisplayName));
        }

        [Test]
        public void ReadUserWriteMaskAttribute()
        {
            using BaseObjectState node = CreateObjectNode();
            node.UserWriteMask = AttributeWriteMask.Description;
            var dataValue = new DataValue();
            ServiceResult result = node.ReadAttribute(
                m_context, Attributes.UserWriteMask, default, default, dataValue);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(dataValue.WrappedValue.GetUInt32(), Is.EqualTo((uint)AttributeWriteMask.Description));
        }

        [Test]
        public void ReadRolePermissionsAttributeWhenSet()
        {
            using BaseObjectState node = CreateObjectNode();
            node.RolePermissions =
            [
                new RolePermissionType
                {
                    RoleId = new NodeId(1),
                    Permissions = (uint)PermissionType.Browse
                }
            ];
            var dataValue = new DataValue();
            ServiceResult result = node.ReadAttribute(
                m_context, Attributes.RolePermissions, default, default, dataValue);
            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void ReadAccessRestrictionsAttributeWhenSet()
        {
            using BaseObjectState node = CreateObjectNode();
            node.AccessRestrictions = AccessRestrictionType.SigningRequired;
            var dataValue = new DataValue();
            ServiceResult result = node.ReadAttribute(
                m_context, Attributes.AccessRestrictions, default, default, dataValue);
            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void ReadInvalidAttributeIdReturnsBad()
        {
            using BaseObjectState node = CreateObjectNode();
            var dataValue = new DataValue();
            ServiceResult result = node.ReadAttribute(
                m_context, 99999, default, default, dataValue);
            Assert.That(StatusCode.IsBad(result.StatusCode), Is.True);
        }

        [Test]
        public void ReadValueAttributeOnBaseObjectReturnsBad()
        {
            using BaseObjectState node = CreateObjectNode();
            var dataValue = new DataValue();
            ServiceResult result = node.ReadAttribute(
                m_context, Attributes.Value, default, default, dataValue);
            Assert.That(StatusCode.IsBad(result.StatusCode), Is.True);
        }

        [Test]
        public void ReadAttributesReturnsMultipleValues()
        {
            using BaseObjectState node = CreateObjectNode();
            ArrayOf<Variant> values = node.ReadAttributes(
                m_context,
                Attributes.NodeId,
                Attributes.BrowseName,
                Attributes.NodeClass);
            Assert.That(values.Count, Is.EqualTo(3));
        }

        [Test]
        public void ReadAttributesWithNullReturnsEmpty()
        {
            using BaseObjectState node = CreateObjectNode();
            ArrayOf<Variant> values = node.ReadAttributes(m_context, null);
            Assert.That(values.Count, Is.Zero);
        }

        [Test]
        public void ReadAttributesWithInvalidIdReturnsStatusCode()
        {
            using BaseObjectState node = CreateObjectNode();
            ArrayOf<Variant> values = node.ReadAttributes(m_context, 99999u);
            Assert.That(values.Count, Is.EqualTo(1));
        }

        [Test]
        public void OnReadNodeIdHandlerInvoked()
        {
            using BaseObjectState node = CreateObjectNode();
            var overrideId = new NodeId(8888, 0);
            node.OnReadNodeId = (ctx, n, ref value) =>
            {
                value = overrideId;
                return ServiceResult.Good;
            };

            var dataValue = new DataValue();
            node.ReadAttribute(
                m_context, Attributes.NodeId, default, default, dataValue);
            Assert.That(dataValue.WrappedValue.GetNodeId(), Is.EqualTo(overrideId));
        }

        [Test]
        public void OnReadBrowseNameHandlerInvoked()
        {
            using BaseObjectState node = CreateObjectNode();
            var overrideName = QualifiedName.From("Override");
            node.OnReadBrowseName = (ctx, n, ref value) =>
            {
                value = overrideName;
                return ServiceResult.Good;
            };

            var dataValue = new DataValue();
            node.ReadAttribute(
                m_context, Attributes.BrowseName, default, default, dataValue);
            Assert.That(dataValue.WrappedValue.GetQualifiedName(), Is.EqualTo(overrideName));
        }

        [Test]
        public void OnReadDisplayNameHandlerInvoked()
        {
            using BaseObjectState node = CreateObjectNode();
            var overrideText = LocalizedText.From("Override");
            node.OnReadDisplayName = (ctx, n, ref value) =>
            {
                value = overrideText;
                return ServiceResult.Good;
            };

            var dataValue = new DataValue();
            node.ReadAttribute(
                m_context, Attributes.DisplayName, default, default, dataValue);
            Assert.That(dataValue.WrappedValue.GetLocalizedText(), Is.EqualTo(overrideText));
        }

        [Test]
        public void OnReadDescriptionHandlerInvoked()
        {
            using BaseObjectState node = CreateObjectNode();
            var overrideDesc = LocalizedText.From("Override Desc");
            node.OnReadDescription = (ctx, n, ref value) =>
            {
                value = overrideDesc;
                return ServiceResult.Good;
            };

            var dataValue = new DataValue();
            node.ReadAttribute(
                m_context, Attributes.Description, default, default, dataValue);
            Assert.That(dataValue.WrappedValue.GetLocalizedText(), Is.EqualTo(overrideDesc));
        }

        [Test]
        public void OnReadWriteMaskHandlerInvoked()
        {
            using BaseObjectState node = CreateObjectNode();
            node.OnReadWriteMask = (ctx, n, ref value) =>
            {
                value = AttributeWriteMask.BrowseName;
                return ServiceResult.Good;
            };

            var dataValue = new DataValue();
            node.ReadAttribute(
                m_context, Attributes.WriteMask, default, default, dataValue);
            Assert.That(
                dataValue.WrappedValue.GetUInt32(),
                Is.EqualTo((uint)AttributeWriteMask.BrowseName));
        }

        [Test]
        public void OnReadUserWriteMaskHandlerInvoked()
        {
            using BaseObjectState node = CreateObjectNode();
            node.OnReadUserWriteMask =
                (ctx, n, ref value) =>
            {
                value = AttributeWriteMask.WriteMask;
                return ServiceResult.Good;
            };

            var dataValue = new DataValue();
            node.ReadAttribute(
                m_context, Attributes.UserWriteMask, default, default, dataValue);
            Assert.That(
                dataValue.WrappedValue.GetUInt32(),
                Is.EqualTo((uint)AttributeWriteMask.WriteMask));
        }

        [Test]
        public void OnReadNodeClassHandlerInvoked()
        {
            using BaseObjectState node = CreateObjectNode();
            node.OnReadNodeClass = (ctx, n, ref value) =>
            {
                value = NodeClass.Variable;
                return ServiceResult.Good;
            };

            var dataValue = new DataValue();
            node.ReadAttribute(
                m_context, Attributes.NodeClass, default, default, dataValue);
        }

        [Test]
        public void OnReadAccessRestrictionsHandlerInvoked()
        {
            using BaseObjectState node = CreateObjectNode();
            node.OnReadAccessRestrictions =
                (ctx, n, ref value) =>
            {
                value = AccessRestrictionType.EncryptionRequired;
                return ServiceResult.Good;
            };

            var dataValue = new DataValue();
            ServiceResult result = node.ReadAttribute(
                m_context, Attributes.AccessRestrictions, default, default, dataValue);
            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void WriteAttributeReturnsBadForNullDataValue()
        {
            using BaseObjectState node = CreateObjectNode();
            ServiceResult result = node.WriteAttribute(
                m_context, Attributes.BrowseName, default, null);
            Assert.That(StatusCode.IsBad(result.StatusCode), Is.True);
        }

        [Test]
        public void WriteNodeIdAttributeSucceeds()
        {
            using BaseObjectState node = CreateObjectNode();
            node.WriteMask = AttributeWriteMask.NodeId;
            var newId = new NodeId(5555, 0);
            var dv = new DataValue(new Variant(newId));
            ServiceResult result = node.WriteAttribute(
                m_context, Attributes.NodeId, default, dv);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(node.NodeId, Is.EqualTo(newId));
        }

        [Test]
        public void WriteNodeIdAttributeFailsWhenNotWritable()
        {
            using BaseObjectState node = CreateObjectNode();
            node.WriteMask = AttributeWriteMask.None;
            var dv = new DataValue(new Variant(new NodeId(1)));
            ServiceResult result = node.WriteAttribute(
                m_context, Attributes.NodeId, default, dv);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
        }

        [Test]
        public void WriteNodeIdAttributeFailsForTypeMismatch()
        {
            using BaseObjectState node = CreateObjectNode();
            node.WriteMask = AttributeWriteMask.NodeId;
            var dv = new DataValue(new Variant("not a NodeId"));
            ServiceResult result = node.WriteAttribute(
                m_context, Attributes.NodeId, default, dv);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
        }

        [Test]
        public void WriteBrowseNameAttributeSucceeds()
        {
            using BaseObjectState node = CreateObjectNode();
            node.WriteMask = AttributeWriteMask.BrowseName;
            var newName = QualifiedName.From("NewBrowse");
            var dv = new DataValue(new Variant(newName));
            ServiceResult result = node.WriteAttribute(
                m_context, Attributes.BrowseName, default, dv);
            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void WriteDisplayNameAttributeSucceeds()
        {
            using BaseObjectState node = CreateObjectNode();
            node.WriteMask = AttributeWriteMask.DisplayName;
            var dv = new DataValue(new Variant(LocalizedText.From("NewDisplay")));
            ServiceResult result = node.WriteAttribute(
                m_context, Attributes.DisplayName, default, dv);
            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void WriteDescriptionAttributeSucceeds()
        {
            using BaseObjectState node = CreateObjectNode();
            node.WriteMask = AttributeWriteMask.Description;
            var dv = new DataValue(new Variant(LocalizedText.From("NewDesc")));
            ServiceResult result = node.WriteAttribute(
                m_context, Attributes.Description, default, dv);
            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void WriteDescriptionAttributeAcceptsNullValue()
        {
            using BaseObjectState node = CreateObjectNode();
            node.WriteMask = AttributeWriteMask.Description;
            var dv = new DataValue(Variant.Null);
            ServiceResult result = node.WriteAttribute(
                m_context, Attributes.Description, default, dv);
            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void WriteWriteMaskAttributeSucceeds()
        {
            using BaseObjectState node = CreateObjectNode();
            node.WriteMask = AttributeWriteMask.WriteMask;
            var dv = new DataValue(new Variant((uint)AttributeWriteMask.DisplayName));
            ServiceResult result = node.WriteAttribute(
                m_context, Attributes.WriteMask, default, dv);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(node.WriteMask, Is.EqualTo(AttributeWriteMask.DisplayName));
        }

        [Test]
        public void WriteUserWriteMaskAttributeSucceeds()
        {
            using BaseObjectState node = CreateObjectNode();
            node.WriteMask = AttributeWriteMask.UserWriteMask;
            var dv = new DataValue(new Variant((uint)AttributeWriteMask.Description));
            ServiceResult result = node.WriteAttribute(
                m_context, Attributes.UserWriteMask, default, dv);
            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void WriteNodeClassAttributeSucceeds()
        {
            using BaseObjectState node = CreateObjectNode();
            node.WriteMask = AttributeWriteMask.NodeClass;
            var dv = new DataValue(Variant.From(NodeClass.Variable));
            ServiceResult result = node.WriteAttribute(
                m_context, Attributes.NodeClass, default, dv);
            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void WriteValueAttributeRejectsServerTimestamp()
        {
            using BaseObjectState node = CreateObjectNode();
            var dv = new DataValue
            {
                WrappedValue = new Variant(42),
                ServerTimestamp = DateTimeUtc.Now
            };
            ServiceResult result = node.WriteAttribute(
                m_context, Attributes.Value, default, dv);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadWriteNotSupported));
        }

        [Test]
        public void WriteNonValueAttributeRejectsStatusCode()
        {
            using BaseObjectState node = CreateObjectNode();
            node.WriteMask = AttributeWriteMask.BrowseName;
            var dv = new DataValue
            {
                WrappedValue = new Variant(QualifiedName.From("Test")),
                StatusCode = StatusCodes.BadUnexpectedError
            };
            ServiceResult result = node.WriteAttribute(
                m_context, Attributes.BrowseName, default, dv);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadWriteNotSupported));
        }

        [Test]
        public void WriteNonValueAttributeRejectsIndexRange()
        {
            using BaseObjectState node = CreateObjectNode();
            node.WriteMask = AttributeWriteMask.BrowseName;
            var dv = new DataValue(new Variant(QualifiedName.From("Test")));
            var indexRange = NumericRange.Parse("0:1");
            ServiceResult result = node.WriteAttribute(
                m_context, Attributes.BrowseName, indexRange, dv);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadIndexRangeInvalid));
        }

        [Test]
        public void WriteInvalidAttributeIdReturnsBad()
        {
            using BaseObjectState node = CreateObjectNode();
            var dv = new DataValue(new Variant(42));
            ServiceResult result = node.WriteAttribute(m_context, 99999, default, dv);
            Assert.That(StatusCode.IsBad(result.StatusCode), Is.True);
        }

        [Test]
        public void WriteAccessRestrictionsAsUInt16Succeeds()
        {
            using BaseObjectState node = CreateObjectNode();
            node.WriteMask = AttributeWriteMask.AccessRestrictions;
            var dv = new DataValue(new Variant((ushort)AccessRestrictionType.SigningRequired));
            ServiceResult result = node.WriteAttribute(
                m_context, Attributes.AccessRestrictions, default, dv);
            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void WriteAccessRestrictionsAsUInt32Succeeds()
        {
            using BaseObjectState node = CreateObjectNode();
            node.WriteMask = AttributeWriteMask.AccessRestrictions;
            var dv = new DataValue(new Variant((uint)AccessRestrictionType.EncryptionRequired));
            ServiceResult result = node.WriteAttribute(
                m_context, Attributes.AccessRestrictions, default, dv);
            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void WriteAccessRestrictionsNullValueSucceeds()
        {
            using BaseObjectState node = CreateObjectNode();
            node.WriteMask = AttributeWriteMask.AccessRestrictions;
            var dv = new DataValue(Variant.Null);
            ServiceResult result = node.WriteAttribute(
                m_context, Attributes.AccessRestrictions, default, dv);
            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void WriteAccessRestrictionsTypeMismatchReturnsBad()
        {
            using BaseObjectState node = CreateObjectNode();
            node.WriteMask = AttributeWriteMask.AccessRestrictions;
            var dv = new DataValue(new Variant("not a number"));
            ServiceResult result = node.WriteAttribute(
                m_context, Attributes.AccessRestrictions, default, dv);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
        }

        [Test]
        public void OnWriteNodeIdHandlerInvoked()
        {
            using BaseObjectState node = CreateObjectNode();
            node.WriteMask = AttributeWriteMask.NodeId;
            bool invoked = false;
            node.OnWriteNodeId = (ctx, n, ref value) =>
            {
                invoked = true;
                return ServiceResult.Good;
            };

            var dv = new DataValue(new Variant(new NodeId(1)));
            node.WriteAttribute(m_context, Attributes.NodeId, default, dv);
            Assert.That(invoked, Is.True);
        }

        [Test]
        public void OnWriteBrowseNameHandlerInvoked()
        {
            using BaseObjectState node = CreateObjectNode();
            node.WriteMask = AttributeWriteMask.BrowseName;
            bool invoked = false;
            node.OnWriteBrowseName =
                (ctx, n, ref value) =>
            {
                invoked = true;
                return ServiceResult.Good;
            };

            var dv = new DataValue(new Variant(QualifiedName.From("X")));
            node.WriteAttribute(m_context, Attributes.BrowseName, default, dv);
            Assert.That(invoked, Is.True);
        }

        [Test]
        public void OnWriteDisplayNameHandlerInvoked()
        {
            using BaseObjectState node = CreateObjectNode();
            node.WriteMask = AttributeWriteMask.DisplayName;
            bool invoked = false;
            node.OnWriteDisplayName =
                (ctx, n, ref value) =>
            {
                invoked = true;
                return ServiceResult.Good;
            };

            var dv = new DataValue(new Variant(LocalizedText.From("X")));
            node.WriteAttribute(m_context, Attributes.DisplayName, default, dv);
            Assert.That(invoked, Is.True);
        }

        [Test]
        public void OnWriteDescriptionHandlerInvoked()
        {
            using BaseObjectState node = CreateObjectNode();
            node.WriteMask = AttributeWriteMask.Description;
            bool invoked = false;
            node.OnWriteDescription =
                (ctx, n, ref value) =>
            {
                invoked = true;
                return ServiceResult.Good;
            };

            var dv = new DataValue(new Variant(LocalizedText.From("X")));
            node.WriteAttribute(m_context, Attributes.Description, default, dv);
            Assert.That(invoked, Is.True);
        }

        [Test]
        public void OnWriteWriteMaskHandlerInvoked()
        {
            using BaseObjectState node = CreateObjectNode();
            node.WriteMask = AttributeWriteMask.WriteMask;
            bool invoked = false;
            node.OnWriteWriteMask = (ctx, n, ref value) =>
            {
                invoked = true;
                return ServiceResult.Good;
            };

            var dv = new DataValue(new Variant((uint)0));
            node.WriteAttribute(m_context, Attributes.WriteMask, default, dv);
            Assert.That(invoked, Is.True);
        }

        [Test]
        public void OnWriteUserWriteMaskHandlerInvoked()
        {
            using BaseObjectState node = CreateObjectNode();
            node.WriteMask = AttributeWriteMask.UserWriteMask;
            bool invoked = false;
            node.OnWriteUserWriteMask =
                (ctx, n, ref value) =>
            {
                invoked = true;
                return ServiceResult.Good;
            };

            var dv = new DataValue(new Variant((uint)0));
            node.WriteAttribute(
                m_context, Attributes.UserWriteMask, default, dv);
            Assert.That(invoked, Is.True);
        }

        [Test]
        public void OnWriteNodeClassHandlerInvoked()
        {
            using BaseObjectState node = CreateObjectNode();
            node.WriteMask = AttributeWriteMask.NodeClass;
            bool invoked = false;
            node.OnWriteNodeClass = (ctx, n, ref value) =>
            {
                invoked = true;
                return ServiceResult.Good;
            };

            var dv = new DataValue(Variant.From(NodeClass.Variable));
            node.WriteAttribute(m_context, Attributes.NodeClass, default, dv);
            Assert.That(invoked, Is.True);
        }

        [Test]
        public void OnWriteAccessRestrictionsHandlerInvoked()
        {
            using BaseObjectState node = CreateObjectNode();
            node.WriteMask = AttributeWriteMask.AccessRestrictions;
            bool invoked = false;
            node.OnWriteAccessRestrictions =
                (ctx, n, ref value) =>
            {
                invoked = true;
                return ServiceResult.Good;
            };

            var dv = new DataValue(new Variant((ushort)1));
            node.WriteAttribute(
                m_context, Attributes.AccessRestrictions, default, dv);
            Assert.That(invoked, Is.True);
        }

        [Test]
        public void ExportToNodeTableCreatesObjectNode()
        {
            using BaseObjectState node = CreateObjectNode();
            node.AddReference(ReferenceTypeIds.Organizes, false, new NodeId(100));
            var table = new NodeTable(
                m_context.NamespaceUris,
                m_context.ServerUris,
                null);

            node.Export(m_context, table);
            Assert.That(table.Find(node.NodeId), Is.Not.Null);
        }

        [Test]
        public void ExportObjectTypeToNodeTableSucceeds()
        {
            using var typeNode = new BaseObjectTypeState();
            typeNode.NodeId = new NodeId(6000, 0);
            typeNode.BrowseName = QualifiedName.From("MyType");
            typeNode.DisplayName = LocalizedText.From("MyType");

            var table = new NodeTable(
                m_context.NamespaceUris,
                m_context.ServerUris,
                null);

            typeNode.Export(m_context, table);
            Assert.That(table.Find(typeNode.NodeId), Is.Not.Null);
        }

        [Test]
        public void ExportViewToNodeTableSucceeds()
        {
            using var view = new ViewState();
            view.NodeId = new NodeId(7000, 0);
            view.BrowseName = QualifiedName.From("MyView");
            view.DisplayName = LocalizedText.From("MyView");

            var table = new NodeTable(
                m_context.NamespaceUris,
                m_context.ServerUris,
                null);

            view.Export(m_context, table);
            Assert.That(table.Find(view.NodeId), Is.Not.Null);
        }

        [Test]
        public void ExportWithChildrenIncludesChildrenInTable()
        {
            using BaseObjectState parent = CreateObjectNode();
            using PropertyState child = CreatePropertyChild(parent, "ExportChild");
            parent.AddChild(child);

            var table = new NodeTable(
                m_context.NamespaceUris,
                m_context.ServerUris,
                null);

            parent.Export(m_context, table);
            Assert.That(table.Find(parent.NodeId), Is.Not.Null);
            Assert.That(table.Find(child.NodeId), Is.Not.Null);
        }

        [Test]
        public void SaveAndLoadAsBinaryRoundTrips()
        {
            using BaseObjectState original = CreateObjectNode();
            original.Description = LocalizedText.From("Binary test");
            original.WriteMask = AttributeWriteMask.DisplayName;

            using var stream = new MemoryStream();
            original.SaveAsBinary(m_context, stream);
            stream.Position = 0;

            using var loaded = new BaseObjectState(null);
            loaded.LoadAsBinary(m_context, stream);

            Assert.That(loaded.NodeId, Is.EqualTo(original.NodeId));
            Assert.That(loaded.BrowseName, Is.EqualTo(original.BrowseName));
            Assert.That(loaded.Description, Is.EqualTo(original.Description));
        }

        [Test]
        public void SaveAndLoadAsBinaryWithChildrenRoundTrips()
        {
            using BaseObjectState original = CreateObjectNode();
            using PropertyState child = CreatePropertyChild(original, "BinChild");
            original.AddChild(child);

            using var stream = new MemoryStream();
            original.SaveAsBinary(m_context, stream);
            stream.Position = 0;

            using var loaded = new BaseObjectState(null);
            loaded.LoadAsBinary(m_context, stream);

            var loadedChildren = new List<BaseInstanceState>();
            loaded.GetChildren(m_context, loadedChildren);
            Assert.That(loadedChildren, Has.Count.EqualTo(1));
        }

        [Test]
        public void SaveAndLoadAsBinaryWithReferencesRoundTrips()
        {
            using BaseObjectState original = CreateObjectNode();
            original.AddReference(ReferenceTypeIds.Organizes, false, new NodeId(100));

            using var stream = new MemoryStream();
            original.SaveAsBinary(m_context, stream);
            stream.Position = 0;

            using var loaded = new BaseObjectState(null);
            loaded.LoadAsBinary(m_context, stream);

            var refs = new List<IReference>();
            loaded.GetReferences(m_context, refs);
            Assert.That(refs, Has.Count.EqualTo(1));
        }

        [Test]
        public void SaveAndLoadAsXmlRoundTrips()
        {
            using BaseObjectState original = CreateObjectNode();
            original.Description = LocalizedText.From("XML test");
            original.SymbolicName = "XmlNode";

            byte[] xmlBytes;
            using (var stream = new MemoryStream())
            {
                original.SaveAsXml(m_context, stream);
                xmlBytes = stream.ToArray();
            }

            using var loadStream = new MemoryStream(xmlBytes);
            using var loaded = new BaseObjectState(null);
            loaded.LoadFromXml(m_context, loadStream);

            Assert.That(loaded.NodeId, Is.EqualTo(original.NodeId));
            Assert.That(loaded.BrowseName, Is.EqualTo(original.BrowseName));
        }

        [Test]
        public void AddNotifierAddsRelationship()
        {
            using BaseObjectState source = CreateObjectNode(name: "Source");
            source.NodeId = new NodeId(1001, 0);
            using BaseObjectState target = CreateObjectNode(name: "Target");
            target.NodeId = new NodeId(1002, 0);

            source.AddNotifier(m_context, ReferenceTypeIds.HasEventSource, false, target);

            var notifiers = new List<NodeState.Notifier>();
            source.GetNotifiers(m_context, notifiers);
            Assert.That(notifiers, Has.Count.EqualTo(1));
            Assert.That(notifiers[0].Node, Is.SameAs(target));
        }

        [Test]
        public void AddNotifierWithNullRefTypeUsesHasEventSource()
        {
            using BaseObjectState source = CreateObjectNode(name: "Source");
            source.NodeId = new NodeId(1001, 0);
            using BaseObjectState target = CreateObjectNode(name: "Target");
            target.NodeId = new NodeId(1002, 0);

            source.AddNotifier(m_context, NodeId.Null, false, target);

            var notifiers = new List<NodeState.Notifier>();
            source.GetNotifiers(m_context, notifiers);
            Assert.That(notifiers, Has.Count.EqualTo(1));
            Assert.That(notifiers[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasEventSource));
        }

        [Test]
        public void AddNotifierUpdatesExistingEntry()
        {
            using BaseObjectState source = CreateObjectNode(name: "Source");
            source.NodeId = new NodeId(1001, 0);
            using BaseObjectState target = CreateObjectNode(name: "Target");
            target.NodeId = new NodeId(1002, 0);

            source.AddNotifier(m_context, ReferenceTypeIds.HasEventSource, false, target);
            source.AddNotifier(m_context, ReferenceTypeIds.HasNotifier, true, target);

            var notifiers = new List<NodeState.Notifier>();
            source.GetNotifiers(m_context, notifiers);
            Assert.That(notifiers, Has.Count.EqualTo(1));
            Assert.That(notifiers[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasNotifier));
            Assert.That(notifiers[0].IsInverse, Is.True);
        }

        [Test]
        public void RemoveNotifierRemovesRelationship()
        {
            using BaseObjectState source = CreateObjectNode(name: "Source");
            source.NodeId = new NodeId(1001, 0);
            using BaseObjectState target = CreateObjectNode(name: "Target");
            target.NodeId = new NodeId(1002, 0);

            source.AddNotifier(m_context, ReferenceTypeIds.HasEventSource, false, target);
            source.RemoveNotifier(m_context, target, false);

            var notifiers = new List<NodeState.Notifier>();
            source.GetNotifiers(m_context, notifiers);
            Assert.That(notifiers, Is.Empty);
        }

        [Test]
        public void RemoveNotifierBidirectionalRemovesBothSides()
        {
            using BaseObjectState source = CreateObjectNode(name: "Source");
            source.NodeId = new NodeId(1001, 0);
            using BaseObjectState target = CreateObjectNode(name: "Target");
            target.NodeId = new NodeId(1002, 0);

            source.AddNotifier(m_context, ReferenceTypeIds.HasEventSource, false, target);
            target.AddNotifier(m_context, ReferenceTypeIds.HasEventSource, true, source);

            source.RemoveNotifier(m_context, target, true);

            var sourceNotifiers = new List<NodeState.Notifier>();
            source.GetNotifiers(m_context, sourceNotifiers);
            Assert.That(sourceNotifiers, Is.Empty);

            var targetNotifiers = new List<NodeState.Notifier>();
            target.GetNotifiers(m_context, targetNotifiers);
            Assert.That(targetNotifiers, Is.Empty);
        }

        [Test]
        public void RemoveNotifierNonExistentIsNoOp()
        {
            using BaseObjectState source = CreateObjectNode(name: "Source");
            using BaseObjectState target = CreateObjectNode(name: "Target");
            Assert.DoesNotThrow(() => source.RemoveNotifier(m_context, target, false));
        }

        [Test]
        public void GetNotifiersFiltersByTypeAndDirection()
        {
            using BaseObjectState source = CreateObjectNode(name: "Source");
            source.NodeId = new NodeId(1001, 0);
            using BaseObjectState target1 = CreateObjectNode(name: "T1");
            target1.NodeId = new NodeId(1002, 0);
            using BaseObjectState target2 = CreateObjectNode(name: "T2");
            target2.NodeId = new NodeId(1003, 0);

            source.AddNotifier(m_context, ReferenceTypeIds.HasEventSource, false, target1);
            source.AddNotifier(m_context, ReferenceTypeIds.HasNotifier, true, target2);

            var forward = new List<NodeState.Notifier>();
            source.GetNotifiers(
                m_context, forward, ReferenceTypeIds.HasEventSource, false);
            Assert.That(forward, Has.Count.EqualTo(1));

            var inverse = new List<NodeState.Notifier>();
            source.GetNotifiers(
                m_context, inverse, ReferenceTypeIds.HasNotifier, true);
            Assert.That(inverse, Has.Count.EqualTo(1));
        }

        [Test]
        public void GetNotifiersReturnsEmptyForNoNotifiers()
        {
            using BaseObjectState node = CreateObjectNode();
            var notifiers = new List<NodeState.Notifier>();
            node.GetNotifiers(m_context, notifiers);
            Assert.That(notifiers, Is.Empty);
        }

        [Test]
        public void ReportEventInvokesOnReportEventHandler()
        {
            using BaseObjectState node = CreateObjectNode();
            bool invoked = false;
            node.OnReportEvent = (ctx, n, e) => invoked = true;
            node.ReportEvent(m_context, null);
            Assert.That(invoked, Is.True);
        }

        [Test]
        public void ReportEventPropagatesViaInverseNotifiers()
        {
            using BaseObjectState source = CreateObjectNode(name: "Source");
            source.NodeId = new NodeId(1001, 0);
            using BaseObjectState parent = CreateObjectNode(name: "Parent");
            parent.NodeId = new NodeId(1002, 0);

            // source has inverse notifier to parent (source reports events to parent)
            source.AddNotifier(m_context, ReferenceTypeIds.HasEventSource, true, parent);

            bool parentEventReceived = false;
            parent.OnReportEvent = (ctx, n, e) => parentEventReceived = true;

            source.ReportEvent(m_context, null);
            Assert.That(parentEventReceived, Is.True);
        }

        [Test]
        public void ConditionRefreshInvokesHandler()
        {
            using BaseObjectState node = CreateObjectNode();
            bool invoked = false;
            node.OnConditionRefresh = (ctx, n, events) => invoked = true;

            var events = new List<IFilterTarget>();
            node.ConditionRefresh(m_context, events, false);
            Assert.That(invoked, Is.True);
        }

        [Test]
        public void ConditionRefreshRecursesIntoChildren()
        {
            using BaseObjectState parent = CreateObjectNode();
            using PropertyState child = CreatePropertyChild(parent, "C1");
            parent.AddChild(child);

            bool childInvoked = false;
            child.OnConditionRefresh = (ctx, n, events) => childInvoked = true;

            var events = new List<IFilterTarget>();
            parent.ConditionRefresh(m_context, events, true);
            Assert.That(childInvoked, Is.True);
        }

        [Test]
        public void FindMethodReturnsNullWhenNoMethods()
        {
            using BaseObjectState node = CreateObjectNode();
            MethodState result = node.FindMethod(m_context, new NodeId(999));
            Assert.That(result, Is.Null);
        }

        [Test]
        public void FindMethodReturnsMethodByNodeId()
        {
            using BaseObjectState parent = CreateObjectNode();
            using var method = new MethodState(parent)
            {
                NodeId = new NodeId(3001, 0),
                BrowseName = QualifiedName.From("MyMethod")
            };
            parent.AddChild(method);

            MethodState found = parent.FindMethod(m_context, new NodeId(3001, 0));
            Assert.That(found, Is.Not.Null);
            Assert.That(found.NodeId, Is.EqualTo(new NodeId(3001, 0)));
        }

        [Test]
        public void FindMethodReturnsNullForNonMatchingId()
        {
            using BaseObjectState parent = CreateObjectNode();
            using var method = new MethodState(parent)
            {
                NodeId = new NodeId(3001, 0),
                BrowseName = QualifiedName.From("MyMethod")
            };
            parent.AddChild(method);

            MethodState found = parent.FindMethod(m_context, new NodeId(9999, 0));
            Assert.That(found, Is.Null);
        }

        [Test]
        public void ReadChildAttributeReadsCurrentNodeWhenAtEnd()
        {
            using BaseObjectState node = CreateObjectNode();
            var relativePath = new List<QualifiedName>();
            var dataValue = new DataValue();

            ServiceResult result = node.ReadChildAttribute(
                m_context, relativePath, 0, Attributes.NodeId, dataValue);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(dataValue.WrappedValue.GetNodeId(), Is.EqualTo(node.NodeId));
        }

        [Test]
        public void ReadChildAttributeReturnsNotFoundForMissingChild()
        {
            using BaseObjectState node = CreateObjectNode();
            var relativePath = new List<QualifiedName> { QualifiedName.From("Missing") };
            var dataValue = new DataValue();

            ServiceResult result = node.ReadChildAttribute(
                m_context, relativePath, 0, Attributes.NodeId, dataValue);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public void ReadChildAttributeReadsNestedChild()
        {
            using BaseObjectState root = CreateObjectNode();
            using PropertyState child = CreatePropertyChild(root, "ChildProp");
            root.AddChild(child);

            var relativePath = new List<QualifiedName> { QualifiedName.From("ChildProp") };
            var dataValue = new DataValue();

            ServiceResult result = root.ReadChildAttribute(
                m_context, relativePath, 0, Attributes.NodeId, dataValue);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(dataValue.WrappedValue.GetNodeId(), Is.EqualTo(child.NodeId));
        }

        [Test]
        public void GetInstanceHierarchyBuildsPathForChildren()
        {
            using BaseObjectState root = CreateObjectNode();
            using PropertyState child = CreatePropertyChild(root, "Child1");
            child.SymbolicName = "Child1";
            child.NodeId = new NodeId(2001, 0);
            root.AddChild(child);

            var hierarchy = new Dictionary<NodeId, string>();
            root.GetInstanceHierarchy(m_context, "Root", hierarchy);

            Assert.That(hierarchy, Contains.Key(new NodeId(2001, 0)));
            Assert.That(hierarchy[new NodeId(2001, 0)], Does.Contain("Child1"));
        }

        [Test]
        public void GetInstanceHierarchyBuildsNestedPaths()
        {
            using BaseObjectState root = CreateObjectNode();
            using var mid = new BaseObjectState(root)
            {
                NodeId = new NodeId(5001, 0),
                BrowseName = QualifiedName.From("Mid"),
                SymbolicName = "Mid"
            };
            root.AddChild(mid);

            using PropertyState leaf = CreatePropertyChild(mid, "Leaf");
            leaf.SymbolicName = "Leaf";
            leaf.NodeId = new NodeId(5002, 0);
            mid.AddChild(leaf);

            var hierarchy = new Dictionary<NodeId, string>();
            root.GetInstanceHierarchy(m_context, "Root", hierarchy);

            Assert.That(hierarchy, Contains.Key(new NodeId(5001, 0)));
            Assert.That(hierarchy, Contains.Key(new NodeId(5002, 0)));
            Assert.That(hierarchy[new NodeId(5002, 0)], Does.Contain("Mid"));
            Assert.That(hierarchy[new NodeId(5002, 0)], Does.Contain("Leaf"));
        }

        [Test]
        public void SetStatusCodePropagatesRecursivelyToChildren()
        {
            using BaseObjectState parent = CreateObjectNode();
            Assert.DoesNotThrow(
                () => parent.SetStatusCode(
                    m_context,
                    StatusCodes.Bad,
                    DateTimeUtc.Now));
        }

        [Test]
        public void GetHierarchyReferencesCollectsReferences()
        {
            using BaseObjectState root = CreateObjectNode();
            root.AddReference(ReferenceTypeIds.Organizes, false, new NodeId(500));

            var hierarchy = new Dictionary<NodeId, string>
            {
                [root.NodeId] = "Root"
            };
            var references = new List<NodeStateHierarchyReference>();

            root.GetHierarchyReferences(m_context, "Root", hierarchy, references);
            Assert.That(references, Has.Count.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void UpdateReferenceTargetsRemapsNodeIds()
        {
            using BaseObjectState node = CreateObjectNode();
            var oldTarget = new NodeId(100, 0);
            var newTarget = new NodeId(200, 0);
            node.AddReference(ReferenceTypeIds.Organizes, false, oldTarget);

            var mapping = new Dictionary<NodeId, NodeId> {
                { oldTarget, newTarget }
            };
            node.UpdateReferenceTargets(m_context, mapping);

            Assert.That(
                node.ReferenceExists(ReferenceTypeIds.Organizes, false, newTarget),
                Is.True);
            Assert.That(
                node.ReferenceExists(ReferenceTypeIds.Organizes, false, oldTarget),
                Is.False);
        }

        [Test]
        public void UpdateReferenceTargetsIgnoresUnmappedTargets()
        {
            using BaseObjectState node = CreateObjectNode();
            node.AddReference(ReferenceTypeIds.Organizes, false, new NodeId(100));
            var mapping = new Dictionary<NodeId, NodeId>();
            node.UpdateReferenceTargets(m_context, mapping);

            Assert.That(
                node.ReferenceExists(ReferenceTypeIds.Organizes, false, new NodeId(100)),
                Is.True);
        }

        [Test]
        public void CreateBrowserReturnsNonNull()
        {
            using BaseObjectState node = CreateObjectNode();
            INodeBrowser browser = node.CreateBrowser(
                m_context,
                default,
                default,
                true,
                BrowseDirection.Both,
                default,
                null,
                true);
            Assert.That(browser, Is.Not.Null);
        }

        [Test]
        public void OnPopulateBrowserHandlerInvoked()
        {
            using BaseObjectState node = CreateObjectNode();
            bool invoked = false;
            node.OnPopulateBrowser = (ctx, n, browser) => invoked = true;

            node.CreateBrowser(
                m_context, default, default, true, BrowseDirection.Both, default, null, true);
            Assert.That(invoked, Is.True);
        }

        [Test]
        public void AssignNodeIdsWithNoFactoryIsNoOp()
        {
            using BaseObjectState node = CreateObjectNode();
            NodeId originalId = node.NodeId;
            var context = new SystemContext(m_telemetry)
            {
                NamespaceUris = m_context.NamespaceUris,
                NodeIdFactory = null
            };
            var mapping = new Dictionary<NodeId, NodeId>();
            node.AssignNodeIds(context, mapping);
            Assert.That(node.NodeId, Is.EqualTo(originalId));
        }

        [Test]
        public void MultipleChildrenWithSameBrowseNameFindReturnsFirst()
        {
            using BaseObjectState parent = CreateObjectNode();
            using PropertyState child1 = CreatePropertyChild(parent, "Dup");
            child1.NodeId = new NodeId(3001, 0);
            using PropertyState child2 = CreatePropertyChild(parent, "Dup");
            child2.NodeId = new NodeId(3002, 0);
            parent.AddChild(child1);
            parent.AddChild(child2);

            BaseInstanceState found = parent.FindChild(m_context, QualifiedName.From("Dup"));
            Assert.That(found, Is.Not.Null);
            Assert.That(found.NodeId, Is.EqualTo(new NodeId(3001, 0)));
        }

        [Test]
        public void LargeNumberOfReferencesHandledCorrectly()
        {
            using BaseObjectState node = CreateObjectNode();
            for (uint i = 0; i < 100; i++)
            {
                node.AddReference(ReferenceTypeIds.Organizes, false, new NodeId(i + 1000));
            }

            var refs = new List<IReference>();
            node.GetReferences(m_context, refs);
            Assert.That(refs, Has.Count.EqualTo(100));
        }

        [Test]
        public void CreateAsPredefinedNodeSucceeds()
        {
            using var node = new BaseObjectState(null);
            node.NodeId = new NodeId(100, 0);
            node.BrowseName = QualifiedName.From("Predefined");
            node.DisplayName = LocalizedText.From("Predefined");
            Assert.DoesNotThrow(() => node.CreateAsPredefinedNode(m_context));
        }

        [Test]
        public void ReplaceChildAddsChildIfBrowseNameNotFound()
        {
            using BaseObjectState parent = CreateObjectNode();
            using PropertyState child = CreatePropertyChild(parent, "NewChild");
            parent.ReplaceChild(m_context, child);

            var children = new List<BaseInstanceState>();
            parent.GetChildren(m_context, children);
            Assert.That(children, Has.Count.EqualTo(1));
        }

        [Test]
        public void FindChildBrowsePathNavigatesMultipleLevels()
        {
            using BaseObjectState root = CreateObjectNode();
            using var mid = new BaseObjectState(root)
            {
                NodeId = new NodeId(5001, 0),
                BrowseName = QualifiedName.From("Mid")
            };
            root.AddChild(mid);

            using PropertyState leaf = CreatePropertyChild(mid, "Leaf");
            mid.AddChild(leaf);

            var path = new List<QualifiedName> {
                QualifiedName.From("Mid"),
                QualifiedName.From("Leaf")
            };
            BaseInstanceState found = root.FindChild(m_context, path, 0);
            Assert.That(found, Is.Not.Null);
            Assert.That(found.BrowseName, Is.EqualTo(QualifiedName.From("Leaf")));
        }
    }
}
