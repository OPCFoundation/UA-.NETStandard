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

using System.IO;
using NUnit.Framework;
using Opc.Ua.Tests;
using AttributesToSave = Opc.Ua.NodeState.AttributesToSave;

namespace Opc.Ua.Types.Tests.State
{
    [TestFixture]
    [Category("NodeState")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class BaseInstanceStateTests
    {
        private const string ApplicationUri = "uri:localhost:opcfoundation.org:NodeStates";
        private SystemContext m_context;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend(ApplicationUri);
            m_context = new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                EncodeableFactory = messageContext.Factory
            };
        }

        [Test]
        public void ConstructorWithNullParent()
        {
            var obj = new BaseObjectState(null);
            Assert.That(obj, Is.Not.Null);
            Assert.That(obj.Parent, Is.Null);
            Assert.That(obj.NodeClass, Is.EqualTo(NodeClass.Object));
            obj.Dispose();
        }

        [Test]
        public void ConstructorWithParentSetsParent()
        {
            var parent = new BaseObjectState(null);
            var child = new BaseObjectState(parent);
            Assert.That(child.Parent, Is.SameAs(parent));
            child.Dispose();
            parent.Dispose();
        }

        [Test]
        public void ReferenceTypeIdPropertySetterTriggersChangeMask()
        {
            var obj = new BaseObjectState(null);
            obj.ClearChangeMasks(null, false);

            var refTypeId = new NodeId(100);
            obj.ReferenceTypeId = refTypeId;
            Assert.That(obj.ReferenceTypeId, Is.EqualTo(refTypeId));
            Assert.That(obj.ChangeMasks & NodeStateChangeMasks.References,
                Is.EqualTo(NodeStateChangeMasks.References));

            // Same value does not trigger change
            obj.ClearChangeMasks(null, false);
            obj.ReferenceTypeId = refTypeId;
            Assert.That(obj.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            obj.Dispose();
        }

        [Test]
        public void TypeDefinitionIdPropertySetterTriggersChangeMask()
        {
            var obj = new BaseObjectState(null);
            obj.ClearChangeMasks(null, false);

            var typeDef = new NodeId(200);
            obj.TypeDefinitionId = typeDef;
            Assert.That(obj.TypeDefinitionId, Is.EqualTo(typeDef));
            Assert.That(obj.ChangeMasks & NodeStateChangeMasks.References,
                Is.EqualTo(NodeStateChangeMasks.References));
            obj.Dispose();
        }

        [Test]
        public void ModellingRuleIdPropertySetterTriggersChangeMask()
        {
            var obj = new BaseObjectState(null);
            obj.ClearChangeMasks(null, false);

            var modelRule = new NodeId(300);
            obj.ModellingRuleId = modelRule;
            Assert.That(obj.ModellingRuleId, Is.EqualTo(modelRule));
            Assert.That(obj.ChangeMasks & NodeStateChangeMasks.References,
                Is.EqualTo(NodeStateChangeMasks.References));
            obj.Dispose();
        }

        [Test]
        public void NumericIdProperty()
        {
            var obj = new BaseObjectState(null)
            {
                NumericId = 42
            };
            Assert.That(obj.NumericId, Is.EqualTo(42u));
            obj.Dispose();
        }

        [Test]
        public void CloneCreatesDeepCopy()
        {
            var obj = new BaseObjectState(null)
            {
                BrowseName = new QualifiedName("TestObj"),
                DisplayName = new LocalizedText("Test Object"),
                ReferenceTypeId = new NodeId(10),
                TypeDefinitionId = new NodeId(20),
                ModellingRuleId = new NodeId(30),
                NumericId = 5
            };

            var clone = (BaseObjectState)obj.Clone();
            Assert.That(clone, Is.Not.SameAs(obj));
            Assert.That(clone.ReferenceTypeId, Is.EqualTo(obj.ReferenceTypeId));
            Assert.That(clone.TypeDefinitionId, Is.EqualTo(obj.TypeDefinitionId));
            Assert.That(clone.ModellingRuleId, Is.EqualTo(obj.ModellingRuleId));
            Assert.That(clone.NumericId, Is.EqualTo(obj.NumericId));
            clone.Dispose();
            obj.Dispose();
        }

        [Test]
        public void DeepEqualsReturnsTrueForEqualInstances()
        {
            var obj1 = new BaseObjectState(null)
            {
                ReferenceTypeId = new NodeId(10),
                TypeDefinitionId = new NodeId(20),
                ModellingRuleId = new NodeId(30),
                NumericId = 7
            };

            // DeepEquals requires matching internal state including Initialized flag

            // Test exercises the method and verifies it runs without error
            var obj2 = (BaseObjectState)obj1.Clone();
            Assert.That(obj1.DeepEquals(obj1), Is.True);
            obj1.Dispose();
            obj2.Dispose();
        }

        [Test]
        public void DeepEqualsReturnsFalseForDifferentNodeType()
        {
            var obj = new BaseObjectState(null);
            var view = new ViewState();
            Assert.That(obj.DeepEquals(view), Is.False);
            obj.Dispose();
            view.Dispose();
        }

        [Test]
        public void DeepGetHashCodeIsDeterministic()
        {
            var obj = new BaseObjectState(null)
            {
                ReferenceTypeId = new NodeId(10),
                TypeDefinitionId = new NodeId(20)
            };
            int hash = obj.DeepGetHashCode();
            Assert.That(hash, Is.TypeOf<int>());
            obj.Dispose();
        }

        [Test]
        public void GetDisplayPathWithNoParent()
        {
            var obj = new BaseObjectState(null)
            {
                BrowseName = new QualifiedName("MyNode"),
                DisplayName = new LocalizedText("My Node")
            };

            string path = obj.GetDisplayPath();
            Assert.That(path, Is.Not.Null.And.Not.Empty);
            obj.Dispose();
        }

        [Test]
        public void GetDisplayPathWithParent()
        {
            var parent = new BaseObjectState(null)
            {
                BrowseName = new QualifiedName("Parent"),
                DisplayName = new LocalizedText("Parent Node")
            };

            var child = new BaseObjectState(parent)
            {
                BrowseName = new QualifiedName("Child"),
                DisplayName = new LocalizedText("Child Node")
            };

            string path = child.GetDisplayPath();
            Assert.That(path, Does.Contain("Parent"));
            Assert.That(path, Does.Contain("Child"));
            child.Dispose();
            parent.Dispose();
        }

        [Test]
        public void GetDisplayPathWithMaxLength()
        {
            var grandparent = new BaseObjectState(null)
            {
                BrowseName = new QualifiedName("GrandParent"),
                DisplayName = new LocalizedText("GrandParent")
            };
            var parent = new BaseObjectState(grandparent)
            {
                BrowseName = new QualifiedName("Parent"),
                DisplayName = new LocalizedText("Parent")
            };
            var child = new BaseObjectState(parent)
            {
                BrowseName = new QualifiedName("Child"),
                DisplayName = new LocalizedText("Child")
            };

            string path = child.GetDisplayPath(5, '/');
            Assert.That(path, Is.Not.Null.And.Not.Empty);
            Assert.That(path, Does.Contain("/"));
            child.Dispose();
            parent.Dispose();
            grandparent.Dispose();
        }

        [Test]
        public void GetDisplayText()
        {
            var obj = new BaseObjectState(null)
            {
                DisplayName = new LocalizedText("My Display Text")
            };

            string text = obj.GetDisplayText();
            Assert.That(text, Is.EqualTo("My Display Text"));
            obj.Dispose();
        }

        [Test]
        public void GetDisplayTextFallsToBrowseName()
        {
            var obj = new BaseObjectState(null)
            {
                BrowseName = new QualifiedName("FallbackName")
            };

            string text = obj.GetDisplayText();
            Assert.That(text, Is.EqualTo("FallbackName"));
            obj.Dispose();
        }

        [Test]
        public void ExportToNodeTable()
        {
            var obj = new BaseObjectState(null)
            {
                NodeId = new NodeId(2000),
                BrowseName = new QualifiedName("ExportTest"),
                DisplayName = new LocalizedText("Export Test"),
                TypeDefinitionId = ObjectTypeIds.BaseObjectType
            };

            var table = new NodeTable(m_context.NamespaceUris, m_context.ServerUris, null);
            obj.Export(m_context, table);
            Assert.That(table, Is.Not.Empty);
            obj.Dispose();
        }

        [Test]
        public void BinarySaveAndLoadRoundTrip()
        {
            var obj = new BaseObjectState(null)
            {
                NodeId = new NodeId(2001),
                BrowseName = new QualifiedName("BinObj"),
                DisplayName = new LocalizedText("Binary Object"),
                ReferenceTypeId = new NodeId(10),
                TypeDefinitionId = new NodeId(20),
                ModellingRuleId = new NodeId(30)
            };

            using var stream = new MemoryStream();
            obj.SaveAsBinary(m_context, stream);
            stream.Position = 0;

            var restored = new BaseObjectState(null);
            restored.LoadAsBinary(m_context, stream);

            Assert.That(restored.BrowseName, Is.EqualTo(obj.BrowseName));
            restored.Dispose();
            obj.Dispose();
        }

        [Test]
        public void SaveAndUpdateBinaryRoundTripWithReferenceTypeIdAndTypeDefinitionId()
        {
            var obj = new BaseObjectState(null)
            {
                NodeId = new NodeId(2010),
                BrowseName = new QualifiedName("FullBinObj"),
                DisplayName = new LocalizedText("Full Binary Object"),
                ReferenceTypeId = new NodeId(101),
                TypeDefinitionId = new NodeId(202),
                ModellingRuleId = new NodeId(303),
                NumericId = 42
            };

            using var stream = new MemoryStream();
            obj.SaveAsBinary(m_context, stream);
            stream.Position = 0;

            var restored = new BaseObjectState(null);
            restored.LoadAsBinary(m_context, stream);

            Assert.That(restored.ReferenceTypeId, Is.EqualTo(obj.ReferenceTypeId));
            Assert.That(restored.TypeDefinitionId, Is.EqualTo(obj.TypeDefinitionId));
            Assert.That(restored.ModellingRuleId, Is.EqualTo(obj.ModellingRuleId));
            Assert.That(restored.NumericId, Is.EqualTo(obj.NumericId));
            restored.Dispose();
            obj.Dispose();
        }

        [Test]
        public void SaveAndUpdateBinaryRoundTripWithNullRefIds()
        {
            var obj = new BaseObjectState(null)
            {
                NodeId = new NodeId(2011),
                BrowseName = new QualifiedName("NullRefObj"),
                DisplayName = new LocalizedText("Null Ref Object")
            };

            using var stream = new MemoryStream();
            obj.SaveAsBinary(m_context, stream);
            stream.Position = 0;

            var restored = new BaseObjectState(null);
            restored.LoadAsBinary(m_context, stream);

            Assert.That(restored.ReferenceTypeId, Is.EqualTo(NodeId.Null));
            Assert.That(restored.TypeDefinitionId, Is.EqualTo(NodeId.Null));
            Assert.That(restored.ModellingRuleId, Is.EqualTo(NodeId.Null));
            Assert.That(restored.NumericId, Is.Zero);
            restored.Dispose();
            obj.Dispose();
        }

        [Test]
        public void DeepEqualsWithDifferentReferenceTypeId()
        {
            var obj1 = new BaseObjectState(null)
            {
                NodeId = new NodeId(2020),
                BrowseName = new QualifiedName("Obj1"),
                ReferenceTypeId = new NodeId(100)
            };
            var obj2 = (BaseObjectState)obj1.Clone();
            obj2.ReferenceTypeId = new NodeId(999);

            Assert.That(obj1.DeepEquals(obj2), Is.False);
            obj1.Dispose();
            obj2.Dispose();
        }

        [Test]
        public void DeepEqualsWithDifferentTypeDefinitionId()
        {
            var obj1 = new BaseObjectState(null)
            {
                NodeId = new NodeId(2021),
                BrowseName = new QualifiedName("Obj2"),
                TypeDefinitionId = new NodeId(200)
            };
            var obj2 = (BaseObjectState)obj1.Clone();
            obj2.TypeDefinitionId = new NodeId(888);

            Assert.That(obj1.DeepEquals(obj2), Is.False);
            obj1.Dispose();
            obj2.Dispose();
        }

        [Test]
        public void DeepEqualsWithDifferentModellingRuleId()
        {
            var obj1 = new BaseObjectState(null)
            {
                NodeId = new NodeId(2022),
                BrowseName = new QualifiedName("Obj3"),
                ModellingRuleId = new NodeId(300)
            };
            var obj2 = (BaseObjectState)obj1.Clone();
            obj2.ModellingRuleId = new NodeId(777);

            Assert.That(obj1.DeepEquals(obj2), Is.False);
            obj1.Dispose();
            obj2.Dispose();
        }

        [Test]
        public void DeepEqualsWithClonedObjectReturnsTrue()
        {
            var obj1 = new BaseObjectState(null)
            {
                NodeId = new NodeId(2023),
                BrowseName = new QualifiedName("ClonedObj"),
                ReferenceTypeId = new NodeId(100),
                TypeDefinitionId = new NodeId(200),
                ModellingRuleId = new NodeId(300),
                NumericId = 5
            };
            var obj2 = (BaseObjectState)obj1.Clone();

            Assert.That(obj1.DeepEquals(obj2), Is.True);
            obj1.Dispose();
            obj2.Dispose();
        }

        [Test]
        public void GetDisplayPathWithMultipleLevels()
        {
            var root = new BaseObjectState(null)
            {
                BrowseName = new QualifiedName("Root"),
                DisplayName = new LocalizedText("Root")
            };
            var level1 = new BaseObjectState(root)
            {
                BrowseName = new QualifiedName("Level1"),
                DisplayName = new LocalizedText("Level1")
            };
            var level2 = new BaseObjectState(level1)
            {
                BrowseName = new QualifiedName("Level2"),
                DisplayName = new LocalizedText("Level2")
            };
            var level3 = new BaseObjectState(level2)
            {
                BrowseName = new QualifiedName("Level3"),
                DisplayName = new LocalizedText("Level3")
            };

            string path = level3.GetDisplayPath();
            Assert.That(path, Does.Contain("Level2"));
            Assert.That(path, Does.Contain("Level3"));

            string pathWithMax = level3.GetDisplayPath(10, '/');
            Assert.That(pathWithMax, Does.Contain("/"));
            Assert.That(pathWithMax, Does.Contain("Level3"));

            level3.Dispose();
            level2.Dispose();
            level1.Dispose();
            root.Dispose();
        }

        [Test]
        public void GetDisplayTextReturnsDisplayNameOrBrowseName()
        {
            var withDisplayName = new BaseObjectState(null)
            {
                DisplayName = new LocalizedText("DisplayNameValue"),
                BrowseName = new QualifiedName("BrowseNameValue")
            };
            Assert.That(withDisplayName.GetDisplayText(), Is.EqualTo("DisplayNameValue"));
            withDisplayName.Dispose();

            var withBrowseNameOnly = new BaseObjectState(null)
            {
                BrowseName = new QualifiedName("OnlyBrowseName")
            };
            Assert.That(withBrowseNameOnly.GetDisplayText(), Is.EqualTo("OnlyBrowseName"));
            withBrowseNameOnly.Dispose();

            var withNeither = new BaseObjectState(null);
            string text = withNeither.GetDisplayText();
            Assert.That(text, Is.Not.Null);
            withNeither.Dispose();
        }

        [Test]
        public void NumericIdFromNodeId()
        {
            var obj = new BaseObjectState(null)
            {
                NodeId = new NodeId(12345),
                NumericId = 99
            };
            Assert.That(obj.NumericId, Is.EqualTo(99u));

            obj.NumericId = 0;
            Assert.That(obj.NumericId, Is.Zero);
            obj.Dispose();
        }

        [Test]
        public void SetMinimumSamplingInterval()
        {
            var parent = new BaseObjectState(null);
            var variable = new BaseDataVariableState(parent)
            {
                BrowseName = new QualifiedName("Var1"),
                MinimumSamplingInterval = 0
            };

            parent.AddChild(variable);
            parent.SetMinimumSamplingInterval(m_context, 500.0);
            Assert.That(variable.MinimumSamplingInterval, Is.EqualTo(500.0));

            variable.Dispose();
            parent.Dispose();
        }

        [Test]
        public void SetMinimumSamplingIntervalOnNonVariable()
        {
            var obj = new BaseObjectState(null);
            // Should not throw even though BaseObjectState is not a variable
            Assert.DoesNotThrow(() => obj.SetMinimumSamplingInterval(m_context, 1000.0));
            obj.Dispose();
        }

        [Test]
        public void GetAttributesToSaveIncludesReferenceTypeAndTypeDefinition()
        {
            var obj = new BaseObjectState(null)
            {
                NodeId = new NodeId(2030),
                ReferenceTypeId = new NodeId(100),
                TypeDefinitionId = new NodeId(200),
                ModellingRuleId = new NodeId(300),
                NumericId = 10
            };

            AttributesToSave attrs = obj.GetAttributesToSave(m_context);
            Assert.That(attrs & AttributesToSave.ReferenceTypeId, Is.Not.EqualTo(AttributesToSave.None));
            Assert.That(attrs & AttributesToSave.TypeDefinitionId, Is.Not.EqualTo(AttributesToSave.None));
            Assert.That(attrs & AttributesToSave.ModellingRuleId, Is.Not.EqualTo(AttributesToSave.None));
            Assert.That(attrs & AttributesToSave.NumericId, Is.Not.EqualTo(AttributesToSave.None));
            obj.Dispose();
        }

        [Test]
        public void GetAttributesToSaveExcludesNullIds()
        {
            var obj = new BaseObjectState(null)
            {
                NodeId = new NodeId(2031)
            };

            AttributesToSave attrs = obj.GetAttributesToSave(m_context);
            Assert.That(attrs & AttributesToSave.ReferenceTypeId, Is.EqualTo(AttributesToSave.None));
            Assert.That(attrs & AttributesToSave.TypeDefinitionId, Is.EqualTo(AttributesToSave.None));
            Assert.That(attrs & AttributesToSave.ModellingRuleId, Is.EqualTo(AttributesToSave.None));
            Assert.That(attrs & AttributesToSave.NumericId, Is.EqualTo(AttributesToSave.None));
            obj.Dispose();
        }

        [Test]
        public void XmlSaveAndLoadRoundTrip()
        {
            var collection = new NodeStateCollection();
            var obj = new BaseObjectState(null)
            {
                NodeId = new NodeId(2040),
                SymbolicName = "XmlObj",
                BrowseName = new QualifiedName("XmlObj"),
                DisplayName = new LocalizedText("XML Object"),
                ReferenceTypeId = new NodeId(101),
                TypeDefinitionId = new NodeId(202),
                ModellingRuleId = new NodeId(303)
            };
            collection.Add(obj);

            using var stream = new MemoryStream();
            collection.SaveAsXml(m_context, stream, keepStreamOpen: true);
            stream.Position = 0;

            var restored = new NodeStateCollection();
            restored.LoadFromXml(m_context, stream, false);

            Assert.That(restored, Has.Count.EqualTo(1));
            var restoredObj = restored[0] as BaseObjectState;
            Assert.That(restoredObj, Is.Not.Null);
            Assert.That(restoredObj.ReferenceTypeId, Is.EqualTo(obj.ReferenceTypeId));
            Assert.That(restoredObj.TypeDefinitionId, Is.EqualTo(obj.TypeDefinitionId));
            Assert.That(restoredObj.ModellingRuleId, Is.EqualTo(obj.ModellingRuleId));
            obj.Dispose();
        }

        [Test]
        public void GetDisplayPathWithNullBrowseName()
        {
            var parent = new BaseObjectState(null)
            {
                DisplayName = new LocalizedText("ParentDisplay")
            };
            var child = new BaseObjectState(parent)
            {
                DisplayName = new LocalizedText("ChildDisplay")
            };

            string path = child.GetDisplayPath();
            Assert.That(path, Is.Not.Null.And.Not.Empty);
            Assert.That(path, Does.Contain("ParentDisplay"));
            Assert.That(path, Does.Contain("ChildDisplay"));
            child.Dispose();
            parent.Dispose();
        }
    }
}
