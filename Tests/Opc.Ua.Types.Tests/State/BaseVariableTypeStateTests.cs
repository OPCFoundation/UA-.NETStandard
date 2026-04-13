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

namespace Opc.Ua.Types.Tests.State
{
    [TestFixture]
    [Category("NodeState")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class BaseVariableTypeStateTests
    {
        private ITelemetryContext m_telemetry;
        private ServiceMessageContext m_messageContext;

        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_messageContext = new ServiceMessageContext(m_telemetry);
        }

        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
            CoreUtils.SilentDispose(m_messageContext);
        }

        private SystemContext CreateSystemContext()
        {
            NamespaceTable namespaceUris = m_messageContext.NamespaceUris;
            return new SystemContext(m_telemetry)
            {
                NamespaceUris = namespaceUris,
                TypeTable = new TypeTable(namespaceUris)
            };
        }

        [Test]
        public void BaseDataVariableTypeStateConstructorSetsDefaults()
        {
            var variableType = new BaseDataVariableTypeState();

            Assert.That(variableType.ValueRank, Is.EqualTo(ValueRanks.Any));
            Assert.That(variableType.Value.IsNull, Is.True);
            Assert.That(variableType.ArrayDimensions.IsEmpty, Is.True);
        }

        [Test]
        public void PropertyTypeStateConstructorSetsDefaults()
        {
            var propertyType = new PropertyTypeState();

            Assert.That(propertyType.ValueRank, Is.EqualTo(ValueRanks.Any));
            Assert.That(propertyType.Value.IsNull, Is.True);
        }

        [Test]
        public void ConstructStaticMethodCreatesInstance()
        {
            NodeState node = BaseDataVariableTypeState.Construct(null);

            Assert.That(node, Is.Not.Null);
            Assert.That(node, Is.InstanceOf<BaseDataVariableTypeState>());
        }

        [Test]
        public void PropertyTypeConstructStaticMethodCreatesInstance()
        {
            NodeState node = PropertyTypeState.Construct(null);

            Assert.That(node, Is.Not.Null);
            Assert.That(node, Is.InstanceOf<PropertyTypeState>());
        }

        [Test]
        public void ValueSetSetsValueChangeMask()
        {
            var variableType = new BaseDataVariableTypeState();
            ISystemContext context = CreateSystemContext();
            variableType.ClearChangeMasks(context, false);

            variableType.Value = new Variant(42);

            Assert.That(variableType.Value, Is.EqualTo(new Variant(42)));
            Assert.That(
                variableType.ChangeMasks.HasFlag(NodeStateChangeMasks.Value),
                Is.True);
        }

        [Test]
        public void ValueSetSameValueDoesNotSetChangeMask()
        {
            var variableType = new BaseDataVariableTypeState
            {
                Value = new Variant(42)
            };
            ISystemContext context = CreateSystemContext();
            variableType.ClearChangeMasks(context, false);

            variableType.Value = new Variant(42);

            Assert.That(
                variableType.ChangeMasks.HasFlag(NodeStateChangeMasks.Value),
                Is.False);
        }

        [Test]
        public void WrappedValueDelegatesToValue()
        {
            var variableType = new BaseDataVariableTypeState
            {
                WrappedValue = new Variant("test")
            };

            Assert.That(variableType.Value, Is.EqualTo(new Variant("test")));
            Assert.That(variableType.WrappedValue,
                Is.EqualTo(variableType.Value));
        }

        [Test]
        public void DataTypeSetSetsNonValueChangeMask()
        {
            var variableType = new BaseDataVariableTypeState();
            ISystemContext context = CreateSystemContext();
            variableType.ClearChangeMasks(context, false);

            variableType.DataType = DataTypeIds.Double;

            Assert.That(variableType.DataType, Is.EqualTo(DataTypeIds.Double));
            Assert.That(
                variableType.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.True);
        }

        [Test]
        public void DataTypeSetSameValueDoesNotSetChangeMask()
        {
            var variableType = new BaseDataVariableTypeState
            {
                DataType = DataTypeIds.Double
            };
            ISystemContext context = CreateSystemContext();
            variableType.ClearChangeMasks(context, false);

            variableType.DataType = DataTypeIds.Double;

            Assert.That(
                variableType.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.False);
        }

        [Test]
        public void ValueRankSetSetsNonValueChangeMask()
        {
            var variableType = new BaseDataVariableTypeState();
            ISystemContext context = CreateSystemContext();
            variableType.ClearChangeMasks(context, false);

            variableType.ValueRank = ValueRanks.OneDimension;

            Assert.That(variableType.ValueRank,
                Is.EqualTo(ValueRanks.OneDimension));
            Assert.That(
                variableType.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.True);
        }

        [Test]
        public void ValueRankSetSameValueDoesNotSetChangeMask()
        {
            var variableType = new BaseDataVariableTypeState
            {
                ValueRank = ValueRanks.Scalar
            };
            ISystemContext context = CreateSystemContext();
            variableType.ClearChangeMasks(context, false);

            variableType.ValueRank = ValueRanks.Scalar;

            Assert.That(
                variableType.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.False);
        }

        [Test]
        public void ArrayDimensionsSetSetsNonValueChangeMask()
        {
            var variableType = new BaseDataVariableTypeState();
            ISystemContext context = CreateSystemContext();
            variableType.ClearChangeMasks(context, false);

            variableType.ArrayDimensions = new uint[] { 5 }.ToArrayOf();

            Assert.That(variableType.ArrayDimensions.Count, Is.EqualTo(1));
            Assert.That(
                variableType.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.True);
        }

        [Test]
        public void ArrayDimensionsSetSameValueDoesNotSetChangeMask()
        {
            ArrayOf<uint> dims = new uint[] { 5 }.ToArrayOf();
            var variableType = new BaseDataVariableTypeState
            {
                ArrayDimensions = dims
            };
            ISystemContext context = CreateSystemContext();
            variableType.ClearChangeMasks(context, false);

            variableType.ArrayDimensions = dims;

            Assert.That(
                variableType.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.False);
        }

        [Test]
        public void CloneCreatesDeepCopy()
        {
            var variableType = new BaseDataVariableTypeState
            {
                Value = new Variant(42),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar,
                ArrayDimensions = new uint[] { 10 }.ToArrayOf()
            };

            var clone = (BaseDataVariableTypeState)variableType.Clone();

            Assert.That(clone, Is.Not.SameAs(variableType));
            Assert.That(clone.Value, Is.EqualTo(variableType.Value));
            Assert.That(clone.DataType, Is.EqualTo(variableType.DataType));
            Assert.That(clone.ValueRank, Is.EqualTo(variableType.ValueRank));
            Assert.That(clone.ArrayDimensions.Count,
                Is.EqualTo(variableType.ArrayDimensions.Count));
        }

        [Test]
        public void ClonePropertyTypeStateCreatesDeepCopy()
        {
            var propertyType = new PropertyTypeState
            {
                Value = new Variant("hello"),
                DataType = DataTypeIds.String
            };

            var clone = (PropertyTypeState)propertyType.Clone();

            Assert.That(clone, Is.Not.SameAs(propertyType));
            Assert.That(clone.Value, Is.EqualTo(propertyType.Value));
            Assert.That(clone.DataType, Is.EqualTo(propertyType.DataType));
        }

        [Test]
        public void InitializeFromSourceViaCloneCopiesAllProperties()
        {
            var source = new BaseDataVariableTypeState
            {
                Value = new Variant(99.5),
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.OneDimension,
                ArrayDimensions = new uint[] { 3 }.ToArrayOf()
            };

            var clone = (BaseDataVariableTypeState)source.Clone();

            Assert.That(clone.Value, Is.EqualTo(source.Value));
            Assert.That(clone.DataType, Is.EqualTo(source.DataType));
            Assert.That(clone.ValueRank, Is.EqualTo(source.ValueRank));
            Assert.That(clone.ArrayDimensions.Count,
                Is.EqualTo(source.ArrayDimensions.Count));
        }

        [Test]
        public void DeepEqualsReturnsTrueForEqualTypes()
        {
            var type1 = new BaseDataVariableTypeState
            {
                NodeId = new NodeId(1),
                BrowseName = new QualifiedName("Test"),
                Value = new Variant(42),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar
            };

            Assert.That(type1.DeepEquals(type1), Is.True);
        }

        [Test]
        public void DeepEqualsReturnsFalseForDifferentValues()
        {
            var type1 = new BaseDataVariableTypeState
            {
                NodeId = new NodeId(1),
                BrowseName = new QualifiedName("Test"),
                Value = new Variant(42)
            };

            var type2 = (BaseDataVariableTypeState)type1.Clone();
            type2.Value = new Variant(99);

            Assert.That(type1.DeepEquals(type2), Is.False);
        }

        [Test]
        public void DeepEqualsReturnsFalseForNullNode()
        {
            var variableType = new BaseDataVariableTypeState();

            Assert.That(variableType.DeepEquals(null), Is.False);
        }

        [Test]
        public void DeepEqualsReturnsFalseForDifferentDataType()
        {
            var type1 = new BaseDataVariableTypeState
            {
                NodeId = new NodeId(1),
                BrowseName = new QualifiedName("Test"),
                DataType = DataTypeIds.Int32
            };

            var type2 = (BaseDataVariableTypeState)type1.Clone();
            type2.DataType = DataTypeIds.Double;

            Assert.That(type1.DeepEquals(type2), Is.False);
        }

        [Test]
        public void DeepEqualsReturnsFalseForDifferentValueRank()
        {
            var type1 = new BaseDataVariableTypeState
            {
                NodeId = new NodeId(1),
                BrowseName = new QualifiedName("Test"),
                ValueRank = ValueRanks.Scalar
            };

            var type2 = (BaseDataVariableTypeState)type1.Clone();
            type2.ValueRank = ValueRanks.OneDimension;

            Assert.That(type1.DeepEquals(type2), Is.False);
        }

        [Test]
        public void DeepEqualsReturnsFalseForDifferentArrayDimensions()
        {
            var type1 = new BaseDataVariableTypeState
            {
                NodeId = new NodeId(1),
                BrowseName = new QualifiedName("Test"),
                ArrayDimensions = new uint[] { 5 }.ToArrayOf()
            };

            var type2 = (BaseDataVariableTypeState)type1.Clone();
            type2.ArrayDimensions = new uint[] { 10 }.ToArrayOf();

            Assert.That(type1.DeepEquals(type2), Is.False);
        }

        [Test]
        public void DeepGetHashCodeReturnsSameForEqual()
        {
            var type1 = new BaseDataVariableTypeState
            {
                NodeId = new NodeId(1),
                BrowseName = new QualifiedName("Test"),
                Value = new Variant(42)
            };

            int hash = type1.DeepGetHashCode();
            Assert.That(hash, Is.TypeOf<int>());
        }

        [Test]
        public void DeepGetHashCodeReturnsDifferentForDifferent()
        {
            var type1 = new BaseDataVariableTypeState
            {
                NodeId = new NodeId(1),
                BrowseName = new QualifiedName("Test1"),
                Value = new Variant(42)
            };

            var type2 = new BaseDataVariableTypeState
            {
                NodeId = new NodeId(2),
                BrowseName = new QualifiedName("Test2"),
                Value = new Variant(99)
            };

            Assert.That(type1.DeepGetHashCode(),
                Is.Not.EqualTo(type2.DeepGetHashCode()));
        }

        [Test]
        public void ExportToNodeTableCreatesVariableTypeNode()
        {
            var variableType = new BaseDataVariableTypeState
            {
                NodeId = new NodeId(300),
                BrowseName = new QualifiedName("TestVarType"),
                DisplayName = new LocalizedText("TestVarType"),
                Value = new Variant(42),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar,
                ArrayDimensions = new uint[] { 5 }.ToArrayOf()
            };

            SystemContext context = CreateSystemContext();
            NamespaceTable namespaceUris = context.NamespaceUris;
            var table = new NodeTable(
                namespaceUris,
                new StringTable(),
                new TypeTable(namespaceUris));

            variableType.Export(context, table);

            INode exported = table.Find(variableType.NodeId);
            Assert.That(exported, Is.Not.Null);
            Assert.That(exported, Is.InstanceOf<VariableTypeNode>());

            var varTypeNode = (VariableTypeNode)exported;
            Assert.That(varTypeNode.DataType, Is.EqualTo(DataTypeIds.Int32));
            Assert.That(varTypeNode.ValueRank, Is.EqualTo(ValueRanks.Scalar));
        }

        [Test]
        public void GetAttributesToSaveIncludesValueWhenSet()
        {
            var variableType = new BaseDataVariableTypeState
            {
                Value = new Variant(42)
            };
            ISystemContext context = CreateSystemContext();

            NodeState.AttributesToSave attrs = variableType.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(NodeState.AttributesToSave.Value), Is.True);
        }

        [Test]
        public void GetAttributesToSaveExcludesValueWhenNull()
        {
            var variableType = new BaseDataVariableTypeState();
            ISystemContext context = CreateSystemContext();

            NodeState.AttributesToSave attrs = variableType.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(NodeState.AttributesToSave.Value), Is.False);
        }

        [Test]
        public void GetAttributesToSaveIncludesDataTypeWhenNonNull()
        {
            var variableType = new BaseDataVariableTypeState
            {
                DataType = DataTypeIds.Int32
            };
            ISystemContext context = CreateSystemContext();

            NodeState.AttributesToSave attrs = variableType.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(NodeState.AttributesToSave.DataType), Is.True);
        }

        [Test]
        public void GetAttributesToSaveIncludesValueRankWhenNotDefault()
        {
            var variableType = new BaseDataVariableTypeState
            {
                ValueRank = ValueRanks.OneDimension
            };
            ISystemContext context = CreateSystemContext();

            NodeState.AttributesToSave attrs = variableType.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(NodeState.AttributesToSave.ValueRank), Is.True);
        }

        [Test]
        public void GetAttributesToSaveExcludesValueRankWhenDefault()
        {
            var variableType = new BaseDataVariableTypeState();
            ISystemContext context = CreateSystemContext();

            NodeState.AttributesToSave attrs = variableType.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(NodeState.AttributesToSave.ValueRank), Is.False);
        }

        [Test]
        public void GetAttributesToSaveIncludesArrayDimensionsWhenSet()
        {
            var variableType = new BaseDataVariableTypeState
            {
                ArrayDimensions = new uint[] { 5 }.ToArrayOf()
            };
            ISystemContext context = CreateSystemContext();

            NodeState.AttributesToSave attrs = variableType.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(NodeState.AttributesToSave.ArrayDimensions),
                Is.True);
        }

        [Test]
        public void GetAttributesToSaveExcludesArrayDimensionsWhenEmpty()
        {
            var variableType = new BaseDataVariableTypeState();
            ISystemContext context = CreateSystemContext();

            NodeState.AttributesToSave attrs = variableType.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(NodeState.AttributesToSave.ArrayDimensions),
                Is.False);
        }

        [Test]
        public void BinarySaveAndUpdateRoundTrip()
        {
            ISystemContext context = CreateSystemContext();
            var original = new BaseDataVariableTypeState
            {
                Value = new Variant(3.14),
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar,
                ArrayDimensions = new uint[] { 1 }.ToArrayOf()
            };

            NodeState.AttributesToSave attributesToSave = original.GetAttributesToSave(context);
            using var ms = new MemoryStream();
            using (var encoder = new BinaryEncoder(ms, m_messageContext, true))
            {
                original.Save(context, encoder, attributesToSave);
            }

            ms.Position = 0;
            var restored = new BaseDataVariableTypeState();
            using (var decoder = new BinaryDecoder(ms, m_messageContext, true))
            {
                restored.Update(context, decoder, attributesToSave);
            }

            Assert.That((double)restored.Value, Is.EqualTo(3.14));
            Assert.That(restored.DataType, Is.EqualTo(DataTypeIds.Double));
            Assert.That(restored.ValueRank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(restored.ArrayDimensions.Count, Is.EqualTo(1));
        }

        [Test]
        public void BinarySaveAndUpdateWithDefaultValues()
        {
            ISystemContext context = CreateSystemContext();
            var original = new BaseDataVariableTypeState();

            NodeState.AttributesToSave attributesToSave = original.GetAttributesToSave(context);
            using var ms = new MemoryStream();
            using (var encoder = new BinaryEncoder(ms, m_messageContext, true))
            {
                original.Save(context, encoder, attributesToSave);
            }

            ms.Position = 0;
            var restored = new BaseDataVariableTypeState();
            using (var decoder = new BinaryDecoder(ms, m_messageContext, true))
            {
                restored.Update(context, decoder, attributesToSave);
            }

            Assert.That(restored.Value.IsNull, Is.True);
            Assert.That(restored.ValueRank, Is.EqualTo(ValueRanks.Any));
        }
    }
}
