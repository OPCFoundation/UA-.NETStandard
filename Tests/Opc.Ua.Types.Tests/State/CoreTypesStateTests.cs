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

using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.State
{
    [TestFixture]
    [Category("NodeState")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class CoreTypesStateTests
    {
        private static readonly int[] s_temperatureValues = [10, 20, 30];
        private static readonly ArrayOf<int> s_indexedTemperatureValues = new([20, 30]);

        private SystemContext m_context;
        private Mock<IFilterContext> m_filterContext;
        private Mock<ITypeTable> m_typeTree;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetry);
            m_context = new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                EncodeableFactory = messageContext.Factory
            };
        }

        [SetUp]
        public void SetUp()
        {
            m_typeTree = new Mock<ITypeTable>(MockBehavior.Strict);
            m_filterContext = new Mock<IFilterContext>(MockBehavior.Strict);
            m_filterContext.SetupGet(context => context.TypeTree).Returns(m_typeTree.Object);
        }

        [Test]
        public void FolderStateConstructsWithFolderDefaultsAndClonePreservesParent()
        {
            var parent = new BaseObjectState(null);
            var folder = new TestFolderState(parent);
            folder.InitializeForTest(m_context);

            var clone = (FolderState)folder.Clone();

            Assert.That(folder.Parent, Is.SameAs(parent));
            Assert.That(folder.NodeClass, Is.EqualTo(NodeClass.Object));
            Assert.That(folder.NumericId, Is.EqualTo(61u));
            Assert.That(folder.TypeDefinitionId, Is.EqualTo(new NodeId(61)));
            Assert.That(folder.EventNotifier, Is.EqualTo(EventNotifiers.None));
            Assert.That(clone.Parent, Is.SameAs(parent));
            Assert.That(clone.NodeClass, Is.EqualTo(NodeClass.Object));
        }

        [Test]
        public void FolderTypeStateConstructsWithTypeDefaultsAndCloneCreatesFolderType()
        {
            var folderType = new TestFolderTypeState();
            folderType.InitializeForTest(m_context);

            var clone = (FolderTypeState)folderType.Clone();

            Assert.That(folderType.NodeClass, Is.EqualTo(NodeClass.ObjectType));
            Assert.That(folderType.NodeId, Is.EqualTo(new NodeId(61)));
            Assert.That(folderType.SuperTypeId, Is.EqualTo(ObjectTypeIds.BaseObjectType));
            Assert.That(folderType.BrowseName.Name, Is.EqualTo("FolderType"));
            Assert.That(folderType.DisplayName.Text, Is.EqualTo("FolderType"));
            Assert.That(folderType.IsAbstract, Is.False);
            Assert.That(clone, Is.Not.SameAs(folderType));
            Assert.That(clone.NodeClass, Is.EqualTo(NodeClass.ObjectType));
        }

        [Test]
        public void InstanceStateSnapshotCapturesObjectAndVariableAttributes()
        {
            BaseObjectState parent = CreateSnapshotSource();
            var snapshot = new InstanceStateSnapshot();

            snapshot.Initialize(m_context, parent);

            Assert.That(snapshot.Handle, Is.SameAs(parent));
            Assert.That(snapshot.IsTypeOf(m_filterContext.Object, NodeId.Null), Is.True);

            Variant parentNodeId = snapshot.GetAttributeValue(
                m_filterContext.Object,
                NodeId.Null,
                ArrayOf<QualifiedName>.Empty,
                Attributes.NodeId,
                NumericRange.Null);
            Assert.That(parentNodeId.TryGetValue(out NodeId nodeId), Is.True);
            Assert.That(nodeId, Is.EqualTo(parent.NodeId));

            Variant childValue = snapshot.GetAttributeValue(
                m_filterContext.Object,
                NodeId.Null,
                new[] { new QualifiedName("Temperature", 2) }.ToArrayOf(),
                Attributes.Value,
                NumericRange.Parse("1:2"));
            Assert.That(childValue.TryGetValue(out ArrayOf<int> values), Is.True);
            Assert.That(values, Is.EqualTo(s_indexedTemperatureValues));

            Variant childBrowseName = snapshot.GetAttributeValue(
                m_filterContext.Object,
                NodeId.Null,
                new[] { new QualifiedName("Temperature", 2) }.ToArrayOf(),
                Attributes.BrowseName,
                NumericRange.Null);
            Assert.That(childBrowseName.TryGetValue(out QualifiedName browseName), Is.True);
            Assert.That(browseName, Is.EqualTo(new QualifiedName("Temperature", 2)));

            Variant childNodeClass = snapshot.GetAttributeValue(
                m_filterContext.Object,
                NodeId.Null,
                new[] { new QualifiedName("Temperature", 2) }.ToArrayOf(),
                Attributes.NodeClass,
                NumericRange.Null);
            Assert.That(childNodeClass.TryGetValue(out NodeClass nodeClass), Is.True);
            Assert.That(nodeClass, Is.EqualTo(NodeClass.Variable));
        }

        [Test]
        public void InstanceStateSnapshotFiltersByTypeAndHandlesMissingPaths()
        {
            BaseObjectState parent = CreateSnapshotSource();
            var snapshot = new InstanceStateSnapshot();
            snapshot.Initialize(m_context, parent);

            m_typeTree
                .Setup(tree => tree.IsTypeOf(ObjectTypeIds.BaseObjectType, ObjectTypeIds.BaseEventType))
                .Returns(false);
            m_typeTree
                .Setup(tree => tree.IsTypeOf(ObjectTypeIds.BaseObjectType, ObjectTypeIds.BaseObjectType))
                .Returns(true);

            Assert.That(snapshot.IsTypeOf(m_filterContext.Object, ObjectTypeIds.BaseObjectType), Is.True);

            Variant wrongTypeValue = snapshot.GetAttributeValue(
                m_filterContext.Object,
                ObjectTypeIds.BaseEventType,
                ArrayOf<QualifiedName>.Empty,
                Attributes.NodeId,
                NumericRange.Null);
            Assert.That(wrongTypeValue.IsNull, Is.True);

            Variant missingPath = snapshot.GetAttributeValue(
                m_filterContext.Object,
                NodeId.Null,
                new[] { new QualifiedName("Missing", 2) }.ToArrayOf(),
                Attributes.Value,
                NumericRange.Null);
            Assert.That(missingPath.IsNull, Is.True);

            Variant badIndexRange = snapshot.GetAttributeValue(
                m_filterContext.Object,
                NodeId.Null,
                new[] { new QualifiedName("Temperature", 2) }.ToArrayOf(),
                Attributes.Value,
                NumericRange.Parse("20:30"));
            Assert.That(badIndexRange.IsNull, Is.True);
        }

        [Test]
        public void InstanceStateSnapshotSetChildValueAddsAndUpdatesSyntheticChild()
        {
            var snapshot = new InstanceStateSnapshot();
            snapshot.Initialize(m_context, CreateSnapshotSource());
            var dynamicName = new QualifiedName("Dynamic", 2);

            snapshot.SetChildValue(dynamicName, NodeClass.Variable, Variant.From(100));
            snapshot.SetChildValue(dynamicName, NodeClass.Variable, Variant.From(200));

            Variant value = snapshot.GetAttributeValue(
                m_filterContext.Object,
                NodeId.Null,
                new[] { dynamicName }.ToArrayOf(),
                Attributes.Value,
                NumericRange.Null);
            Assert.That(value.TryGetValue(out int updated), Is.True);
            Assert.That(updated, Is.EqualTo(200));
        }

        [Test]
        public void BaseInstanceStateUpdateCreatesChildrenFromEventFields()
        {
            var state = new BaseObjectState(null);
            var eventNodeId = new NodeId(7001, 2);
            var eventTypeId = ObjectTypeIds.BaseEventType;
            var messageName = new QualifiedName("Message", 0);
            var sourceName = new QualifiedName("SourceNode", 0);
            var sourceNodeId = new NodeId(7002, 2);
            var fields = new[]
            {
                new SimpleAttributeOperand { AttributeId = Attributes.NodeId },
                new SimpleAttributeOperand
                {
                    AttributeId = Attributes.Value,
                    BrowsePath = new[] { new QualifiedName("EventType") }.ToArrayOf()
                },
                new SimpleAttributeOperand
                {
                    AttributeId = Attributes.Value,
                    BrowsePath = new[] { messageName }.ToArrayOf()
                },
                new SimpleAttributeOperand
                {
                    AttributeId = Attributes.NodeId,
                    BrowsePath = new[] { sourceName }.ToArrayOf()
                },
                new SimpleAttributeOperand
                {
                    AttributeId = Attributes.Value,
                    BrowsePath = new[] { new QualifiedName("Ignored", 0) }.ToArrayOf()
                }
            }.ToArrayOf();
            var values = new EventFieldList
            {
                EventFields = new[]
                {
                    new Variant(eventNodeId),
                    new Variant(eventTypeId),
                    new Variant("raised"),
                    new Variant(sourceNodeId),
                    Variant.Null
                }.ToArrayOf()
            };

            state.Update(m_context, fields, values);

            Assert.That(state.NodeId, Is.EqualTo(eventNodeId));
            Assert.That(state.TypeDefinitionId, Is.EqualTo(eventTypeId));

            NodeState message = state.FindChild(m_context, new[] { messageName }.ToArrayOf(), 0);
            NodeState source = state.FindChild(m_context, new[] { sourceName }.ToArrayOf(), 0);

            Assert.That(message, Is.TypeOf<BaseDataVariableState>());
            Assert.That(((BaseDataVariableState)message).Value, Is.EqualTo("raised"));
            Assert.That(source, Is.TypeOf<BaseObjectState>());
            Assert.That(source.NodeId, Is.EqualTo(sourceNodeId));
            Assert.That(state.FindChild(
                m_context,
                new[] { new QualifiedName("Ignored", 0) }.ToArrayOf(),
                0), Is.Null);
        }


        private sealed class TestFolderState : FolderState
        {
            public TestFolderState(NodeState parent)
                : base(parent)
            {
            }

            public void InitializeForTest(ISystemContext context)
            {
                Initialize(context);
            }
        }

        private sealed class TestFolderTypeState : FolderTypeState
        {
            public void InitializeForTest(ISystemContext context)
            {
                Initialize(context);
            }
        }

        private static BaseObjectState CreateSnapshotSource()
        {
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId(6001, 2),
                BrowseName = new QualifiedName("Parent", 2),
                TypeDefinitionId = ObjectTypeIds.BaseObjectType
            };
            var temperature = new BaseDataVariableState(parent)
            {
                BrowseName = new QualifiedName("Temperature", 2),
                Value = Variant.From(s_temperatureValues),
                StatusCode = StatusCodes.Good
            };
            var component = new BaseObjectState(parent)
            {
                BrowseName = new QualifiedName("Component", 2),
                NodeId = new NodeId(6002, 2)
            };
            var suppressed = new BaseDataVariableState(parent)
            {
                BrowseName = new QualifiedName("Suppressed", 2),
                StatusCode = StatusCodes.Bad,
                Value = 500
            };
            var method = new MethodState(parent)
            {
                BrowseName = new QualifiedName("Method", 2)
            };

            parent.AddChild(temperature);
            parent.AddChild(component);
            parent.AddChild(suppressed);
            parent.AddChild(method);
            return parent;
        }
    }
}
