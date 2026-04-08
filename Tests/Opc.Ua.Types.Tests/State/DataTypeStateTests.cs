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
    public class DataTypeStateTests
    {
        private const string ApplicationUri = "uri:localhost:opcfoundation.org:NodeStates";
        private SystemContext m_context;
        private ServiceMessageContext m_messageContext;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            m_messageContext = ServiceMessageContext.CreateEmpty(telemetry);
            m_messageContext.NamespaceUris.GetIndexOrAppend(ApplicationUri);
            m_context = new SystemContext(telemetry)
            {
                NamespaceUris = m_messageContext.NamespaceUris,
                ServerUris = m_messageContext.ServerUris,
                EncodeableFactory = m_messageContext.Factory
            };
        }

        [Test]
        public void DefaultConstructorSetsDefaults()
        {
            var dt = new DataTypeState();
            Assert.That(dt, Is.Not.Null);
            Assert.That(dt.NodeClass, Is.EqualTo(NodeClass.DataType));
            Assert.That(dt.IsAbstract, Is.False);
            Assert.That(dt.SuperTypeId, Is.EqualTo(NodeId.Null));
            Assert.That(dt.DataTypeDefinition, Is.EqualTo(ExtensionObject.Null));
            dt.Dispose();
        }

        [Test]
        public void ConstructStaticFactory()
        {
            NodeState node = DataTypeState.Construct(null);
            Assert.That(node, Is.InstanceOf<DataTypeState>());
            node.Dispose();
        }

        [Test]
        public void DataTypeDefinitionPropertySetterTriggersChangeMask()
        {
            var dt = new DataTypeState();
            dt.ClearChangeMasks(null, false);

            var definition = new ExtensionObject(new StructureDefinition());
            dt.DataTypeDefinition = definition;
            Assert.That(dt.DataTypeDefinition, Is.EqualTo(definition));
            Assert.That(dt.ChangeMasks & NodeStateChangeMasks.NonValue,
                Is.EqualTo(NodeStateChangeMasks.NonValue));
            dt.Dispose();
        }

        [Test]
        public void DataTypeDefinitionSetSameValueDoesNotSetChangeMask()
        {
            var definition = new ExtensionObject(new StructureDefinition());
            var dt = new DataTypeState
            {
                DataTypeDefinition = definition
            };
            dt.ClearChangeMasks(null, false);

            dt.DataTypeDefinition = definition;
            Assert.That(dt.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            dt.Dispose();
        }

        [Test]
        public void PurposePropertyCanBeSetAndRead()
        {
            var dt = new DataTypeState
            {
                Purpose = Export.DataTypePurpose.Normal
            };

            Assert.That(dt.Purpose, Is.EqualTo(Export.DataTypePurpose.Normal));

            dt.Purpose = Export.DataTypePurpose.ServicesOnly;
            Assert.That(dt.Purpose, Is.EqualTo(Export.DataTypePurpose.ServicesOnly));
            dt.Dispose();
        }

        [Test]
        public void CloneCreatesDeepCopy()
        {
            var definition = new ExtensionObject(new StructureDefinition());
            var dt = new DataTypeState
            {
                NodeId = new NodeId(6000),
                BrowseName = new QualifiedName("MyDataType"),
                DisplayName = new LocalizedText("My Data Type"),
                SuperTypeId = new NodeId(100),
                IsAbstract = true,
                DataTypeDefinition = definition,
                Purpose = Export.DataTypePurpose.ServicesOnly
            };

            var clone = (DataTypeState)dt.Clone();
            Assert.That(clone, Is.Not.SameAs(dt));
            Assert.That(clone.SuperTypeId, Is.EqualTo(dt.SuperTypeId));
            Assert.That(clone.IsAbstract, Is.EqualTo(dt.IsAbstract));
            Assert.That(clone.DataTypeDefinition.IsNull, Is.False);
            Assert.That(clone.Purpose, Is.EqualTo(dt.Purpose));
            clone.Dispose();
            dt.Dispose();
        }

        [Test]
        public void DeepEqualsReturnsTrueForEqualInstances()
        {
            var definition = new ExtensionObject(new StructureDefinition());
            var dt1 = new DataTypeState
            {
                NodeId = new NodeId(6001),
                BrowseName = new QualifiedName("Type"),
                DataTypeDefinition = definition,
                Purpose = Export.DataTypePurpose.Normal
            };

            var dt2 = (DataTypeState)dt1.Clone();
            Assert.That(dt1.DeepEquals(dt2), Is.True);
            dt1.Dispose();
            dt2.Dispose();
        }

        [Test]
        public void DeepEqualsReturnsFalseForDifferentNodeType()
        {
            var dt = new DataTypeState();
            var view = new ViewState();
            Assert.That(dt.DeepEquals(view), Is.False);
            dt.Dispose();
            view.Dispose();
        }

        [Test]
        public void DeepEqualsReturnsFalseForDifferentDataTypeDefinition()
        {
            var dt1 = new DataTypeState
            {
                NodeId = new NodeId(6010),
                BrowseName = new QualifiedName("Type"),
                DataTypeDefinition = new ExtensionObject(new StructureDefinition())
            };

            var dt2 = (DataTypeState)dt1.Clone();
            dt2.DataTypeDefinition = new ExtensionObject(new EnumDefinition());

            Assert.That(dt1.DeepEquals(dt2), Is.False);
            dt1.Dispose();
            dt2.Dispose();
        }

        [Test]
        public void DeepEqualsReturnsFalseForDifferentPurpose()
        {
            var dt1 = new DataTypeState
            {
                NodeId = new NodeId(6011),
                BrowseName = new QualifiedName("Type"),
                Purpose = Export.DataTypePurpose.Normal
            };

            var dt2 = (DataTypeState)dt1.Clone();
            dt2.Purpose = Export.DataTypePurpose.ServicesOnly;

            Assert.That(dt1.DeepEquals(dt2), Is.False);
            dt1.Dispose();
            dt2.Dispose();
        }

        [Test]
        public void DeepGetHashCodeIsDeterministic()
        {
            var dt = new DataTypeState
            {
                NodeId = new NodeId(6020),
                BrowseName = new QualifiedName("Type"),
                DataTypeDefinition = new ExtensionObject(new StructureDefinition()),
                Purpose = Export.DataTypePurpose.Normal
            };
            int hash1 = dt.DeepGetHashCode();
            int hash2 = dt.DeepGetHashCode();
            Assert.That(hash1, Is.EqualTo(hash2));
            dt.Dispose();
        }

        [Test]
        public void DeepGetHashCodeReturnsDifferentForDifferentProperties()
        {
            var dt1 = new DataTypeState
            {
                NodeId = new NodeId(6021),
                BrowseName = new QualifiedName("Type1"),
                Purpose = Export.DataTypePurpose.Normal
            };

            var dt2 = new DataTypeState
            {
                NodeId = new NodeId(6022),
                BrowseName = new QualifiedName("Type2"),
                Purpose = Export.DataTypePurpose.ServicesOnly
            };

            Assert.That(dt1.DeepGetHashCode(), Is.Not.EqualTo(dt2.DeepGetHashCode()));
            dt1.Dispose();
            dt2.Dispose();
        }

        [Test]
        public void GetAttributesToSaveIncludesDataTypeDefinitionWhenSet()
        {
            var dt = new DataTypeState
            {
                DataTypeDefinition = new ExtensionObject(new StructureDefinition())
            };
            AttributesToSave attrs = dt.GetAttributesToSave(m_context);
            Assert.That(attrs & AttributesToSave.DataTypeDefinition, Is.Not.EqualTo(AttributesToSave.None));
            dt.Dispose();
        }

        [Test]
        public void GetAttributesToSaveExcludesDataTypeDefinitionWhenNull()
        {
            var dt = new DataTypeState();
            AttributesToSave attrs = dt.GetAttributesToSave(m_context);
            Assert.That(attrs & AttributesToSave.DataTypeDefinition, Is.EqualTo(AttributesToSave.None));
            dt.Dispose();
        }

        [Test]
        public void SaveAndUpdateBinaryRoundTripWithAllProperties()
        {
            var original = new DataTypeState
            {
                NodeId = new NodeId(6030),
                BrowseName = new QualifiedName("BinDataType"),
                DisplayName = new LocalizedText("Binary Data Type"),
                SuperTypeId = new NodeId(300),
                IsAbstract = true,
                DataTypeDefinition = new ExtensionObject(new StructureDefinition())
            };

            AttributesToSave attributesToSave = original.GetAttributesToSave(m_context);
            using var ms = new MemoryStream();
            using (var encoder = new BinaryEncoder(ms, m_messageContext, true))
            {
                original.Save(m_context, encoder, attributesToSave);
            }

            ms.Position = 0;
            var restored = new DataTypeState();
            using (var decoder = new BinaryDecoder(ms, m_messageContext, true))
            {
                restored.Update(m_context, decoder, attributesToSave);
            }

            Assert.That(restored.SuperTypeId, Is.EqualTo(original.SuperTypeId));
            Assert.That(restored.IsAbstract, Is.EqualTo(original.IsAbstract));
            Assert.That(restored.DataTypeDefinition.IsNull, Is.False);
            restored.Dispose();
            original.Dispose();
        }

        [Test]
        public void SaveAndUpdateBinaryRoundTripWithDefaultValues()
        {
            var original = new DataTypeState
            {
                NodeId = new NodeId(6031),
                BrowseName = new QualifiedName("DefaultDataType"),
                DisplayName = new LocalizedText("Default Data Type")
            };

            AttributesToSave attributesToSave = original.GetAttributesToSave(m_context);
            using var ms = new MemoryStream();
            using (var encoder = new BinaryEncoder(ms, m_messageContext, true))
            {
                original.Save(m_context, encoder, attributesToSave);
            }

            ms.Position = 0;
            var restored = new DataTypeState();
            using (var decoder = new BinaryDecoder(ms, m_messageContext, true))
            {
                restored.Update(m_context, decoder, attributesToSave);
            }

            Assert.That(restored.IsAbstract, Is.EqualTo(original.IsAbstract));
            Assert.That(restored.DataTypeDefinition.IsNull, Is.True);
            restored.Dispose();
            original.Dispose();
        }

        [Test]
        public void BinarySaveAndLoadRoundTrip()
        {
            var dt = new DataTypeState
            {
                NodeId = new NodeId(6040),
                BrowseName = new QualifiedName("LoadSaveDataType"),
                DisplayName = new LocalizedText("Load Save Data Type"),
                SuperTypeId = new NodeId(400),
                IsAbstract = true,
                DataTypeDefinition = new ExtensionObject(new StructureDefinition())
            };

            using var stream = new MemoryStream();
            dt.SaveAsBinary(m_context, stream);
            stream.Position = 0;

            var restored = new DataTypeState();
            restored.LoadAsBinary(m_context, stream);

            Assert.That(restored.SuperTypeId, Is.EqualTo(dt.SuperTypeId));
            Assert.That(restored.IsAbstract, Is.EqualTo(dt.IsAbstract));
            Assert.That(restored.DataTypeDefinition.IsNull, Is.False);
            restored.Dispose();
            dt.Dispose();
        }

        [Test]
        public void ExportToNodeTable()
        {
            var dt = new DataTypeState
            {
                NodeId = new NodeId(6050),
                BrowseName = new QualifiedName("ExportDataType"),
                DisplayName = new LocalizedText("Export Data Type"),
                SuperTypeId = new NodeId(100),
                IsAbstract = false
            };

            var table = new NodeTable(m_context.NamespaceUris, m_context.ServerUris, null);
            dt.Export(m_context, table);
            Assert.That(table, Is.Not.Empty);
            dt.Dispose();
        }
    }
}
