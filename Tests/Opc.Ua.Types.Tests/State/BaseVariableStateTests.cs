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
using System.IO;
using NUnit.Framework;
using Opc.Ua.Tests;
using AttributesToSave = Opc.Ua.NodeState.AttributesToSave;

namespace Opc.Ua.Types.Tests.State
{
    /// <summary>
    /// Tests for BaseVariableState.
    /// </summary>
    [TestFixture]
    [Category("NodeState")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class BaseVariableStateTests
    {
        private ITelemetryContext m_telemetry;
        private ServiceMessageContext m_messageContext;

        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_messageContext = ServiceMessageContext.CreateEmpty(m_telemetry);
        }

        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
            (m_messageContext as IDisposable)?.Dispose();
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
        public void ConstructorWithNullParentSetsDefaults()
        {
            using var variable = new BaseDataVariableState(null);

            Assert.That(variable.DataType, Is.EqualTo(DataTypeIds.BaseDataType));
            Assert.That(variable.ValueRank, Is.EqualTo(ValueRanks.Any));
            Assert.That(variable.ArrayDimensions.IsEmpty, Is.True);
            Assert.That(variable.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
            Assert.That(variable.UserAccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
            Assert.That(variable.MinimumSamplingInterval,
                Is.EqualTo(MinimumSamplingIntervals.Continuous));
            Assert.That(variable.Historizing, Is.False);
            Assert.That(variable.StatusCode,
                Is.EqualTo(StatusCodes.BadWaitingForInitialData));
        }

        [Test]
        public void ConstructorWithParentSetsDefaults()
        {
            using var parent = new BaseDataVariableState(null);
            using var variable = new BaseDataVariableState(parent);

            Assert.That(variable.Parent, Is.SameAs(parent));
            Assert.That(variable.DataType, Is.EqualTo(DataTypeIds.BaseDataType));
        }

        [Test]
        public void PropertyStateConstructorSetsDefaults()
        {
            using var property = new PropertyState(null);

            Assert.That(property.DataType, Is.EqualTo(DataTypeIds.BaseDataType));
            Assert.That(property.ValueRank, Is.EqualTo(ValueRanks.Any));
            Assert.That(property.Historizing, Is.False);
        }

        /// <summary>
        /// Test that setting a value wrapped in an ExtensionObject extracts the body correctly.
        /// </summary>
        [Test]
        public void PropertyStateExtractsValueFromExtensionObject()
        {
            // Create a PropertyState for Argument type (IEncodeable)
            var propertyState = PropertyState<Argument>.With<StructureBuilder<Argument>>(null);

            // Create an Argument (IEncodeable type that can be in ExtensionObject)
            var testArg = new Argument("arg1", DataTypeIds.String, -1, "test description");

            // Wrap in ExtensionObject and set the value using the base Value property (object type)
            // This should trigger ExtractValueFromVariant to unwrap the ExtensionObject
            ((BaseVariableState)propertyState).Value = new ExtensionObject(testArg);

            // The value should be extracted from the ExtensionObject
            Assert.That(propertyState.Value, Is.Not.Null);
            Assert.That(propertyState.Value.Name, Is.EqualTo("arg1"));
            Assert.That(propertyState.Value.Description.Text, Is.EqualTo("test description"));
        }

        /// <summary>
        /// Test that setting a value wrapped in an ExtensionObject for a complex type
        /// extracts correctly.
        /// </summary>
        [Test]
        public void PropertyStateExtractsComplexTypeFromExtensionObject()
        {
            // Create a PropertyState for RelativePath type (IEncodeable)
            var propertyState = PropertyState<RelativePath>.With<StructureBuilder<RelativePath>>(null);

            // Create a RelativePath (IEncodeable type)
            var testValue = new RelativePath
            {
                Elements =
                [
                    new RelativePathElement
                    {
                        TargetName = QualifiedName.From("TestName"),
                        IsInverse = false
                    }
                ]
            };

            // Set the value
            ((BaseVariableState)propertyState).Value = new ExtensionObject(testValue);

            // The value should be extracted
            Assert.That(propertyState.Value, Is.Not.Null);
            Assert.That(propertyState.Value.Elements.IsNull, Is.False);
            Assert.That(propertyState.Value.Elements.Count, Is.EqualTo(1));
            Assert.That(propertyState.Value.Elements[0].TargetName.Name, Is.EqualTo("TestName"));
        }

        /// <summary>
        /// Test that setting a direct value (not wrapped) still works correctly.
        /// </summary>
        [Test]
        public void PropertyStateAcceptsDirectValue()
        {
            var propertyState = PropertyState<string>.With<VariantBuilder>(null);
            const string testString = "DirectValue";

            // Set value directly (not in ExtensionObject)
            ((BaseVariableState)propertyState).Value = testString;

            Assert.That(propertyState.Value, Is.EqualTo(testString));
        }

        /// <summary>
        /// Test that setting null value works correctly.
        /// </summary>
        [Test]
        public void PropertyStateAcceptsNullValue()
        {
            var propertyState = PropertyState<string>.With<VariantBuilder>(null);

            // Set null value
            ((BaseVariableState)propertyState).Value = default;

            Assert.That(propertyState.Value, Is.Null);
        }

        /// <summary>
        /// Test with BaseDataVariableState to ensure the fix works for all variable types.
        /// </summary>
        [Test]
        public void BaseDataVariableStateExtractsValueFromExtensionObject()
        {
            var variableState = BaseDataVariableState<Argument>.With<StructureBuilder<Argument>>(null);

            // Create an Argument (IEncodeable type)
            var testArg = new Argument("testArg", DataTypeIds.Int32, -1, "test description");
            ((BaseVariableState)variableState).Value = new ExtensionObject(testArg);

            Assert.That(variableState.Value, Is.Not.Null);
            Assert.That(variableState.Value.Name, Is.EqualTo("testArg"));
        }

        /// <summary>
        /// Test that Variant values are properly unwrapped.
        /// </summary>
        [Test]
        public void PropertyStateExtractsValueFromVariant()
        {
            var propertyState = PropertyState<string>.With<VariantBuilder>(null);
            const string testString = "VariantValue";

            // Use WrappedValue property which calls ExtractValueFromVariant
            propertyState.WrappedValue = new Variant(testString);

            Assert.That(propertyState.Value, Is.EqualTo(testString));
        }

        /// <summary>
        /// Test that Variant with ExtensionObject is properly unwrapped.
        /// </summary>
        [Test]
        public void PropertyStateExtractsValueFromVariantWithExtensionObject()
        {
            var propertyState = PropertyState<Argument>.With<StructureBuilder<Argument>>(null);
            var testArg = new Argument("variantArg", DataTypeIds.Double, -1, "test description");
            var extensionObject = new ExtensionObject(testArg);

            // Use WrappedValue property
            propertyState.WrappedValue = new Variant(extensionObject);

            Assert.That(propertyState.Value, Is.Not.Null);
            Assert.That(propertyState.Value.Name, Is.EqualTo("variantArg"));
        }

        [Test]
        public void ValuePropertySetSetsChangeMaskAndStatusCode()
        {
            using var variable = new BaseDataVariableState(null);
            ISystemContext context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.Value = new Variant(42);

            Assert.That(variable.Value, Is.EqualTo(new Variant(42)));
            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.Value),
                Is.True);
            // First time touched => StatusCode set to Good
            Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ValuePropertySetSameValueDoesNotSetChangeMask()
        {
            using var variable = new BaseDataVariableState(null)
            {
                Value = new Variant(42)
            };
            ISystemContext context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            // Set same value again
            variable.Value = new Variant(42);

            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.Value),
                Is.False);
        }

        [Test]
        public void WrappedValuePropertyDelegatesToValue()
        {
            using var variable = new BaseDataVariableState(null)
            {
                WrappedValue = new Variant("hello")
            };

            Assert.That(variable.Value, Is.EqualTo(new Variant("hello")));
            Assert.That(variable.WrappedValue, Is.EqualTo(variable.Value));
        }

        [Test]
        public void TimestampSetSetsChangeMask()
        {
            using var variable = new BaseDataVariableState(null);
            ISystemContext context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.Timestamp = DateTimeUtc.Now;

            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.Value),
                Is.True);
        }

        [Test]
        public void TimestampSetSameValueDoesNotSetChangeMask()
        {
            DateTimeUtc ts = DateTimeUtc.Now;
            using var variable = new BaseDataVariableState(null)
            {
                Timestamp = ts
            };
            ISystemContext context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.Timestamp = ts;

            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.Value),
                Is.False);
        }

        [Test]
        public void StatusCodeSetSetsChangeMask()
        {
            using var variable = new BaseDataVariableState(null);
            ISystemContext context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.StatusCode = StatusCodes.Good;

            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.Value),
                Is.True);
        }

        [Test]
        public void StatusCodeSetSameValueDoesNotSetChangeMask()
        {
            using var variable = new BaseDataVariableState(null)
            {
                StatusCode = StatusCodes.Good
            };
            ISystemContext context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.StatusCode = StatusCodes.Good;

            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.Value),
                Is.False);
        }

        [Test]
        public void DataTypeSetSetsNonValueChangeMask()
        {
            using var variable = new BaseDataVariableState(null);
            ISystemContext context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.DataType = DataTypeIds.Int32;

            Assert.That(variable.DataType, Is.EqualTo(DataTypeIds.Int32));
            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.True);
        }

        [Test]
        public void DataTypeSetSameValueDoesNotSetChangeMask()
        {
            using var variable = new BaseDataVariableState(null)
            {
                DataType = DataTypeIds.Int32
            };
            ISystemContext context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.DataType = DataTypeIds.Int32;

            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.False);
        }

        [Test]
        public void ValueRankSetSetsNonValueChangeMask()
        {
            using var variable = new BaseDataVariableState(null);
            ISystemContext context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.ValueRank = ValueRanks.OneDimension;

            Assert.That(variable.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.True);
        }

        [Test]
        public void ValueRankSetSameValueDoesNotSetChangeMask()
        {
            using var variable = new BaseDataVariableState(null)
            {
                ValueRank = ValueRanks.Scalar
            };
            ISystemContext context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.ValueRank = ValueRanks.Scalar;

            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.False);
        }

        [Test]
        public void ArrayDimensionsSetSetsNonValueChangeMask()
        {
            using var variable = new BaseDataVariableState(null);
            ISystemContext context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.ArrayDimensions = new uint[] { 5 }.ToArrayOf();

            Assert.That(variable.ArrayDimensions.Count, Is.EqualTo(1));
            Assert.That(variable.ArrayDimensions[0], Is.EqualTo(5u));
            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.True);
        }

        [Test]
        public void ArrayDimensionsSetSameValueDoesNotSetChangeMask()
        {
            ArrayOf<uint> dims = new uint[] { 5 }.ToArrayOf();
            using var variable = new BaseDataVariableState(null)
            {
                ArrayDimensions = dims
            };
            ISystemContext context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.ArrayDimensions = dims;

            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.False);
        }

        [Test]
        public void AccessLevelSetSetsNonValueChangeMask()
        {
            using var variable = new BaseDataVariableState(null);
            ISystemContext context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;

            Assert.That(variable.AccessLevel,
                Is.EqualTo(AccessLevels.CurrentReadOrWrite));
            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.True);
        }

        [Test]
        public void AccessLevelSetSameValueDoesNotSetChangeMask()
        {
            using var variable = new BaseDataVariableState(null)
            {
                AccessLevel = AccessLevels.CurrentReadOrWrite
            };
            ISystemContext context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;

            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.False);
        }

        [Test]
        public void AccessLevelExSetSetsNonValueChangeMask()
        {
            using var variable = new BaseDataVariableState(null);
            ISystemContext context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.AccessLevelEx = 0x100;

            Assert.That(variable.AccessLevelEx, Is.EqualTo(0x100u));
            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.True);
        }

        [Test]
        public void AccessLevelExSetSameValueDoesNotSetChangeMask()
        {
            using var variable = new BaseDataVariableState(null)
            {
                AccessLevelEx = 0x100
            };
            ISystemContext context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.AccessLevelEx = 0x100;

            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.False);
        }

        [Test]
        public void AccessLevelReturnsLow8BitsOfAccessLevelEx()
        {
            using var variable = new BaseDataVariableState(null)
            {
                AccessLevelEx = 0x1FF
            };

            Assert.That(variable.AccessLevel, Is.EqualTo((byte)0xFF));
        }

        [Test]
        public void AccessLevelSetPreservesHighBitsOfAccessLevelEx()
        {
            using var variable = new BaseDataVariableState(null)
            {
                AccessLevelEx = 0xFF00,

                AccessLevel = 0x03
            };

            Assert.That(variable.AccessLevelEx, Is.EqualTo(0xFF03u));
        }

        [Test]
        public void UserAccessLevelSetSetsNonValueChangeMask()
        {
            using var variable = new BaseDataVariableState(null);
            ISystemContext context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;

            Assert.That(variable.UserAccessLevel,
                Is.EqualTo(AccessLevels.CurrentReadOrWrite));
            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.True);
        }

        [Test]
        public void UserAccessLevelSetSameValueDoesNotSetChangeMask()
        {
            using var variable = new BaseDataVariableState(null)
            {
                UserAccessLevel = AccessLevels.CurrentReadOrWrite
            };
            ISystemContext context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;

            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.False);
        }

        [Test]
        public void MinimumSamplingIntervalSetSetsNonValueChangeMask()
        {
            using var variable = new BaseDataVariableState(null);
            ISystemContext context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.MinimumSamplingInterval = 1000.0;

            Assert.That(variable.MinimumSamplingInterval, Is.EqualTo(1000.0));
            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.True);
        }

        [Test]
        public void MinimumSamplingIntervalSetSameValueDoesNotSetChangeMask()
        {
            using var variable = new BaseDataVariableState(null)
            {
                MinimumSamplingInterval = 1000.0
            };
            ISystemContext context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.MinimumSamplingInterval = 1000.0;

            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.False);
        }

        [Test]
        public void HistorizingSetSetsNonValueChangeMask()
        {
            using var variable = new BaseDataVariableState(null);
            ISystemContext context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.Historizing = true;

            Assert.That(variable.Historizing, Is.True);
            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.True);
        }

        [Test]
        public void HistorizingSetSameValueDoesNotSetChangeMask()
        {
            using var variable = new BaseDataVariableState(null)
            {
                Historizing = true
            };
            ISystemContext context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.Historizing = true;

            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.False);
        }

        [Test]
        public void CopyPolicyDefaultIsCopyOnRead()
        {
            using var variable = new BaseDataVariableState(null);

            Assert.That(variable.CopyPolicy,
                Is.EqualTo(VariableCopyPolicy.CopyOnRead));
        }

        [Test]
        public void CopyPolicyCanBeSet()
        {
            using var variable = new BaseDataVariableState(null)
            {
                CopyPolicy = VariableCopyPolicy.Never
            };

            Assert.That(variable.CopyPolicy, Is.EqualTo(VariableCopyPolicy.Never));
        }

        [Test]
        public void CloneCreatesDeepCopy()
        {
            using var variable = new BaseDataVariableState(null)
            {
                Value = new Variant(42),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentRead,
                MinimumSamplingInterval = 500.0,
                Historizing = true,
                ArrayDimensions = new uint[] { 10 }.ToArrayOf(),
                StatusCode = StatusCodes.Good
            };

            var clone = (BaseDataVariableState)variable.Clone();

            Assert.That(clone, Is.Not.SameAs(variable));
            Assert.That(clone.Value, Is.EqualTo(variable.Value));
            Assert.That(clone.DataType, Is.EqualTo(variable.DataType));
            Assert.That(clone.ValueRank, Is.EqualTo(variable.ValueRank));
            Assert.That(clone.AccessLevel, Is.EqualTo(variable.AccessLevel));
            Assert.That(clone.UserAccessLevel,
                Is.EqualTo(variable.UserAccessLevel));
            Assert.That(clone.MinimumSamplingInterval,
                Is.EqualTo(variable.MinimumSamplingInterval));
            Assert.That(clone.Historizing, Is.EqualTo(variable.Historizing));
            Assert.That(clone.ArrayDimensions.Count,
                Is.EqualTo(variable.ArrayDimensions.Count));
            Assert.That(clone.StatusCode, Is.EqualTo(variable.StatusCode));
        }

        [Test]
        public void ClonePropertyStateCreatesDeepCopy()
        {
            using var property = new PropertyState(null)
            {
                Value = new Variant("testValue"),
                DataType = DataTypeIds.String,
                ValueRank = ValueRanks.Scalar
            };

            var clone = (PropertyState)property.Clone();

            Assert.That(clone, Is.Not.SameAs(property));
            Assert.That(clone.Value, Is.EqualTo(property.Value));
            Assert.That(clone.DataType, Is.EqualTo(property.DataType));
        }

        [Test]
        public void InitializeFromSourceCopiesAllProperties()
        {
            using var source = new BaseDataVariableState(null)
            {
                Value = new Variant(99),
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.OneDimension,
                ArrayDimensions = new uint[] { 3 }.ToArrayOf(),
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                MinimumSamplingInterval = 250.0,
                Historizing = true
            };

            ISystemContext context = CreateSystemContext();
            using var target = new BaseDataVariableState(null);
            target.Create(context, source);

            Assert.That(target.Value, Is.EqualTo(source.Value));
            Assert.That(target.DataType, Is.EqualTo(source.DataType));
            Assert.That(target.ValueRank, Is.EqualTo(source.ValueRank));
            Assert.That(target.ArrayDimensions.Count,
                Is.EqualTo(source.ArrayDimensions.Count));
            Assert.That(target.AccessLevel, Is.EqualTo(source.AccessLevel));
            Assert.That(target.UserAccessLevel,
                Is.EqualTo(source.UserAccessLevel));
            Assert.That(target.MinimumSamplingInterval,
                Is.EqualTo(source.MinimumSamplingInterval));
            Assert.That(target.Historizing, Is.EqualTo(source.Historizing));
        }

        [Test]
        public void DeepEqualsReturnsTrueForSameReference()
        {
            using var property = new PropertyState(null)
            {
                NodeId = new NodeId(1),
                BrowseName = new QualifiedName("Test"),
                Value = new Variant(42),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar
            };

            // Same reference should be deeply equal (exercises shortcut)
            Assert.That(property.DeepEquals(property), Is.True);
        }

        [Test]
        public void DeepEqualsReturnsFalseForDifferentValues()
        {
            using var var1 = new BaseDataVariableState(null)
            {
                NodeId = new NodeId(1),
                BrowseName = new QualifiedName("Test"),
                Value = new Variant(42)
            };

            var var2 = (BaseDataVariableState)var1.Clone();
            var2.Value = new Variant(99);

            Assert.That(var1.DeepEquals(var2), Is.False);
        }

        [Test]
        public void DeepEqualsReturnsFalseForNullNode()
        {
            using var variable = new BaseDataVariableState(null);

            Assert.That(variable.DeepEquals(null), Is.False);
        }

        [Test]
        public void DeepEqualsReturnsFalseForDifferentDataType()
        {
            using var var1 = new BaseDataVariableState(null)
            {
                NodeId = new NodeId(1),
                BrowseName = new QualifiedName("Test"),
                DataType = DataTypeIds.Int32
            };

            var var2 = (BaseDataVariableState)var1.Clone();
            var2.DataType = DataTypeIds.Double;

            Assert.That(var1.DeepEquals(var2), Is.False);
        }

        [Test]
        public void DeepEqualsReturnsFalseForDifferentAccessLevel()
        {
            using var var1 = new BaseDataVariableState(null)
            {
                NodeId = new NodeId(1),
                BrowseName = new QualifiedName("Test"),
                AccessLevel = AccessLevels.CurrentRead
            };

            var var2 = (BaseDataVariableState)var1.Clone();
            var2.AccessLevel = AccessLevels.CurrentReadOrWrite;

            Assert.That(var1.DeepEquals(var2), Is.False);
        }

        [Test]
        public void DeepEqualsReturnsFalseForDifferentHistorizing()
        {
            using var var1 = new BaseDataVariableState(null)
            {
                NodeId = new NodeId(1),
                BrowseName = new QualifiedName("Test"),
                Historizing = false
            };

            var var2 = (BaseDataVariableState)var1.Clone();
            var2.Historizing = true;

            Assert.That(var1.DeepEquals(var2), Is.False);
        }

        [Test]
        public void DeepEqualsReturnsFalseForDifferentMinSamplingInterval()
        {
            using var var1 = new BaseDataVariableState(null)
            {
                NodeId = new NodeId(1),
                BrowseName = new QualifiedName("Test"),
                MinimumSamplingInterval = 100.0
            };

            var var2 = (BaseDataVariableState)var1.Clone();
            var2.MinimumSamplingInterval = 500.0;

            Assert.That(var1.DeepEquals(var2), Is.False);
        }

        [Test]
        public void DeepEqualsReturnsFalseForDifferentUserAccessLevel()
        {
            using var var1 = new BaseDataVariableState(null)
            {
                NodeId = new NodeId(1),
                BrowseName = new QualifiedName("Test"),
                UserAccessLevel = AccessLevels.CurrentRead
            };

            var var2 = (BaseDataVariableState)var1.Clone();
            var2.UserAccessLevel = AccessLevels.CurrentReadOrWrite;

            Assert.That(var1.DeepEquals(var2), Is.False);
        }

        [Test]
        public void DeepGetHashCodeExercisesAllFields()
        {
            using var property = new PropertyState(null)
            {
                NodeId = new NodeId(1),
                BrowseName = new QualifiedName("Test"),
                Value = new Variant(42),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                MinimumSamplingInterval = 100.0,
                Historizing = true
            };

            // Should not throw and should return a value
            int hash = property.DeepGetHashCode();
            Assert.That(hash, Is.TypeOf<int>());
        }

        [Test]
        public void DeepGetHashCodeReturnsDifferentHashForDifferentObjects()
        {
            using var var1 = new BaseDataVariableState(null)
            {
                NodeId = new NodeId(1),
                BrowseName = new QualifiedName("Test"),
                Value = new Variant(42)
            };

            using var var2 = new BaseDataVariableState(null)
            {
                NodeId = new NodeId(2),
                BrowseName = new QualifiedName("Other"),
                Value = new Variant(99)
            };

            Assert.That(var1.DeepGetHashCode(),
                Is.Not.EqualTo(var2.DeepGetHashCode()));
        }

        [Test]
        public void ExportToNodeTableCreatesVariableNode()
        {
            using var variable = new BaseDataVariableState(null)
            {
                NodeId = new NodeId(100),
                BrowseName = new QualifiedName("TestVar"),
                DisplayName = new LocalizedText("TestVar"),
                Value = new Variant(3.14),
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentRead,
                MinimumSamplingInterval = 100.0,
                Historizing = true,
                ArrayDimensions = new uint[] { 5 }.ToArrayOf()
            };

            SystemContext context = CreateSystemContext();
            NamespaceTable namespaceUris = context.NamespaceUris;
            var table = new NodeTable(
                namespaceUris,
                new StringTable(),
                new TypeTable(namespaceUris));

            variable.Export(context, table);

            INode exported = table.Find(variable.NodeId);
            Assert.That(exported, Is.Not.Null);
            Assert.That(exported, Is.InstanceOf<VariableNode>());

            var variableNode = (VariableNode)exported;
            Assert.That(variableNode.DataType, Is.EqualTo(DataTypeIds.Double));
            Assert.That(variableNode.ValueRank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(variableNode.AccessLevel,
                Is.EqualTo(AccessLevels.CurrentReadOrWrite));
            Assert.That(variableNode.UserAccessLevel,
                Is.EqualTo(AccessLevels.CurrentRead));
            Assert.That(variableNode.MinimumSamplingInterval, Is.EqualTo(100.0));
            Assert.That(variableNode.Historizing, Is.True);
        }

        [Test]
        public void SetStatusCodeSetsCodeAndTimestamp()
        {
            using var variable = new BaseDataVariableState(null);
            ISystemContext context = CreateSystemContext();
            DateTimeUtc timestamp = DateTimeUtc.Now;

            variable.SetStatusCode(
                context, StatusCodes.BadNodeIdInvalid, timestamp);

            Assert.That(variable.StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            Assert.That(variable.Timestamp, Is.EqualTo(timestamp));
        }

        [Test]
        public void SetStatusCodeWithMinTimestampDoesNotUpdateTimestamp()
        {
            using var variable = new BaseDataVariableState(null);
            ISystemContext context = CreateSystemContext();
            DateTimeUtc originalTimestamp = DateTimeUtc.Now;
            variable.Timestamp = originalTimestamp;

            variable.SetStatusCode(
                context, StatusCodes.Good, DateTimeUtc.MinValue);

            Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(variable.Timestamp, Is.EqualTo(originalTimestamp));
        }

        [Test]
        public void ArrayDimensionsToXmlReturnsNullForEmpty()
        {
            string result = BaseVariableState.ArrayDimensionsToXml(default);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void ArrayDimensionsToXmlReturnsSingleValue()
        {
            ArrayOf<uint> dims = new uint[] { 10 }.ToArrayOf();

            string result = BaseVariableState.ArrayDimensionsToXml(dims);

            Assert.That(result, Is.EqualTo("10"));
        }

        [Test]
        public void ArrayDimensionsToXmlReturnsCommaSeparatedValues()
        {
            ArrayOf<uint> dims = new uint[] { 3, 4, 5 }.ToArrayOf();

            string result = BaseVariableState.ArrayDimensionsToXml(dims);

            Assert.That(result, Is.EqualTo("3,4,5"));
        }

        [Test]
        public void ArrayDimensionsFromXmlReturnsDefaultForNull()
        {
            ArrayOf<uint> result = BaseVariableState.ArrayDimensionsFromXml(null);

            Assert.That(result.IsEmpty, Is.True);
        }

        [Test]
        public void ArrayDimensionsFromXmlReturnsDefaultForEmpty()
        {
            ArrayOf<uint> result = BaseVariableState.ArrayDimensionsFromXml(string.Empty);

            Assert.That(result.IsEmpty, Is.True);
        }

        [Test]
        public void ArrayDimensionsFromXmlParsesSingleValue()
        {
            ArrayOf<uint> result = BaseVariableState.ArrayDimensionsFromXml("10");

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(10u));
        }

        [Test]
        public void ArrayDimensionsFromXmlParsesMultipleValues()
        {
            ArrayOf<uint> result = BaseVariableState.ArrayDimensionsFromXml("3,4,5");

            Assert.That(result.Count, Is.EqualTo(3));
            Assert.That(result[0], Is.EqualTo(3u));
            Assert.That(result[1], Is.EqualTo(4u));
            Assert.That(result[2], Is.EqualTo(5u));
        }

        [Test]
        public void ArrayDimensionsFromXmlHandlesInvalidValue()
        {
            ArrayOf<uint> result = BaseVariableState.ArrayDimensionsFromXml("abc");

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0], Is.Zero);
        }

        [Test]
        public void ArrayDimensionsRoundTrip()
        {
            ArrayOf<uint> original = new uint[] { 2, 7, 11 }.ToArrayOf();

            string xml = BaseVariableState.ArrayDimensionsToXml(original);
            ArrayOf<uint> parsed = BaseVariableState.ArrayDimensionsFromXml(xml);

            Assert.That(parsed.Count, Is.EqualTo(original.Count));
            for (int i = 0; i < original.Count; i++)
            {
                Assert.That(parsed[i], Is.EqualTo(original[i]));
            }
        }

        [Test]
        public void GetAttributesToSaveIncludesValueWhenSet()
        {
            using var variable = new BaseDataVariableState(null)
            {
                Value = new Variant(42)
            };
            ISystemContext context = CreateSystemContext();

            AttributesToSave attrs = variable.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(AttributesToSave.Value), Is.True);
        }

        [Test]
        public void GetAttributesToSaveIncludesStatusCodeWhenNotGood()
        {
            using var variable = new BaseDataVariableState(null);
            // Default StatusCode is BadWaitingForInitialData, which is not Good
            ISystemContext context = CreateSystemContext();

            AttributesToSave attrs = variable.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(AttributesToSave.StatusCode), Is.True);
        }

        [Test]
        public void GetAttributesToSaveExcludesStatusCodeWhenGood()
        {
            using var variable = new BaseDataVariableState(null)
            {
                Value = new Variant(1), // touch to set StatusCode to Good
                StatusCode = StatusCodes.Good
            };
            ISystemContext context = CreateSystemContext();

            AttributesToSave attrs = variable.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(AttributesToSave.StatusCode), Is.False);
        }

        [Test]
        public void GetAttributesToSaveIncludesDataTypeWhenNotNull()
        {
            using var variable = new BaseDataVariableState(null)
            {
                DataType = DataTypeIds.Int32
            };
            ISystemContext context = CreateSystemContext();

            AttributesToSave attrs = variable.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(AttributesToSave.DataType), Is.True);
        }

        [Test]
        public void GetAttributesToSaveIncludesValueRankWhenNotDefault()
        {
            using var variable = new BaseDataVariableState(null)
            {
                ValueRank = ValueRanks.OneDimension
            };
            ISystemContext context = CreateSystemContext();

            AttributesToSave attrs = variable.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(AttributesToSave.ValueRank), Is.True);
        }

        [Test]
        public void GetAttributesToSaveExcludesValueRankWhenDefault()
        {
            using var variable = new BaseDataVariableState(null);
            // ValueRanks.Any is default
            ISystemContext context = CreateSystemContext();

            AttributesToSave attrs = variable.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(AttributesToSave.ValueRank), Is.False);
        }

        [Test]
        public void GetAttributesToSaveIncludesArrayDimensionsWhenSet()
        {
            using var variable = new BaseDataVariableState(null)
            {
                ArrayDimensions = new uint[] { 5 }.ToArrayOf()
            };
            ISystemContext context = CreateSystemContext();

            AttributesToSave attrs = variable.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(AttributesToSave.ArrayDimensions), Is.True);
        }

        [Test]
        public void GetAttributesToSaveIncludesAccessLevelWhenNonZero()
        {
            using var variable = new BaseDataVariableState(null);
            // Default is CurrentRead which is non-zero
            ISystemContext context = CreateSystemContext();

            AttributesToSave attrs = variable.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(AttributesToSave.AccessLevel), Is.True);
        }

        [Test]
        public void GetAttributesToSaveIncludesUserAccessLevelWhenNonZero()
        {
            using var variable = new BaseDataVariableState(null);
            // Default is CurrentRead
            ISystemContext context = CreateSystemContext();

            AttributesToSave attrs = variable.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(AttributesToSave.UserAccessLevel), Is.True);
        }

        [Test]
        public void GetAttributesToSaveIncludesMinSamplingIntervalWhenNonZero()
        {
            using var variable = new BaseDataVariableState(null)
            {
                MinimumSamplingInterval = 500.0
            };
            ISystemContext context = CreateSystemContext();

            AttributesToSave attrs = variable.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(AttributesToSave.MinimumSamplingInterval),
                Is.True);
        }

        [Test]
        public void GetAttributesToSaveExcludesMinSamplingIntervalWhenZero()
        {
            using var variable = new BaseDataVariableState(null);
            // Default is Continuous = 0
            ISystemContext context = CreateSystemContext();

            AttributesToSave attrs = variable.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(AttributesToSave.MinimumSamplingInterval),
                Is.False);
        }

        [Test]
        public void GetAttributesToSaveIncludesHistorizingWhenTrue()
        {
            using var variable = new BaseDataVariableState(null)
            {
                Historizing = true
            };
            ISystemContext context = CreateSystemContext();

            AttributesToSave attrs = variable.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(AttributesToSave.Historizing), Is.True);
        }

        [Test]
        public void GetAttributesToSaveExcludesHistorizingWhenFalse()
        {
            using var variable = new BaseDataVariableState(null);
            // Default is false
            ISystemContext context = CreateSystemContext();

            AttributesToSave attrs = variable.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(AttributesToSave.Historizing), Is.False);
        }

        [Test]
        public void BinarySaveAndUpdateRoundTripAllAttributes()
        {
            ISystemContext context = CreateSystemContext();
            using var original = new BaseDataVariableState(null)
            {
                Value = new Variant(42),
                StatusCode = StatusCodes.Good,
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar,
                ArrayDimensions = new uint[] { 1 }.ToArrayOf(),
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentRead,
                MinimumSamplingInterval = 500.0,
                Historizing = true
            };

            // Save
            AttributesToSave attributesToSave = original.GetAttributesToSave(context);
            using var ms = new MemoryStream();
            using (var encoder = new BinaryEncoder(ms, m_messageContext, true))
            {
                original.Save(context, encoder, attributesToSave);
            }

            // Update
            ms.Position = 0;
            using var restored = new BaseDataVariableState(null);
            using (var decoder = new BinaryDecoder(ms, m_messageContext, true))
            {
                restored.Update(context, decoder, attributesToSave);
            }

            Assert.That((int)restored.Value, Is.EqualTo(42));
            Assert.That(restored.DataType, Is.EqualTo(DataTypeIds.Int32));
            Assert.That(restored.ValueRank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(restored.ArrayDimensions.Count, Is.EqualTo(1));
            Assert.That(restored.AccessLevel,
                Is.EqualTo(AccessLevels.CurrentReadOrWrite));
            Assert.That(restored.UserAccessLevel,
                Is.EqualTo(AccessLevels.CurrentRead));
            Assert.That(restored.MinimumSamplingInterval, Is.EqualTo(500.0));
            Assert.That(restored.Historizing, Is.True);
        }

        [Test]
        public void BinarySaveAndUpdateWithMinimalAttributes()
        {
            ISystemContext context = CreateSystemContext();
            using var original = new BaseDataVariableState(null);
            // Only default DataType is non-null by default

            AttributesToSave attributesToSave = original.GetAttributesToSave(context);
            using var ms = new MemoryStream();
            using (var encoder = new BinaryEncoder(ms, m_messageContext, true))
            {
                original.Save(context, encoder, attributesToSave);
            }

            ms.Position = 0;
            using var restored = new BaseDataVariableState(null);
            using (var decoder = new BinaryDecoder(ms, m_messageContext, true))
            {
                restored.Update(context, decoder, attributesToSave);
            }

            Assert.That(restored.DataType, Is.EqualTo(original.DataType));
        }

        [Test]
        public void BinarySaveAndUpdateStatusCodeAttribute()
        {
            ISystemContext context = CreateSystemContext();
            using var original = new BaseDataVariableState(null);
            // Don't touch the value; default status is BadWaitingForInitialData

            AttributesToSave attributesToSave = original.GetAttributesToSave(context);
            Assert.That(attributesToSave.HasFlag(AttributesToSave.StatusCode),
                Is.True);

            using var ms = new MemoryStream();
            using (var encoder = new BinaryEncoder(ms, m_messageContext, true))
            {
                original.Save(context, encoder, attributesToSave);
            }

            ms.Position = 0;
            using var restored = new BaseDataVariableState(null)
            {
                StatusCode = StatusCodes.Good // set different value first
            };
            using (var decoder = new BinaryDecoder(ms, m_messageContext, true))
            {
                restored.Update(context, decoder, attributesToSave);
            }

            Assert.That(restored.StatusCode,
                Is.EqualTo(StatusCodes.BadWaitingForInitialData));
        }

        [Test]
        public void ApplyIndexRangeAndDataEncodingWithEmptyRangeReturnsGood()
        {
            ISystemContext context = CreateSystemContext();
            var value = new Variant(42);

            ServiceResult result = BaseVariableState.ApplyIndexRangeAndDataEncoding(
                context,
                default,
                default,
                ref value);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            Assert.That((int)value, Is.EqualTo(42));
        }

        [Test]
        public void GenericPropertyStateValueSetAndGet()
        {
            var property = PropertyState<int>.With<VariantBuilder>(null);

            property.Value = 42;

            Assert.That(property.Value, Is.EqualTo(42));
        }

        [Test]
        public void GenericBaseDataVariableStateValueSetAndGet()
        {
            var variable =
                BaseDataVariableState<string>.With<VariantBuilder>(null);

            variable.Value = "hello";

            Assert.That(variable.Value, Is.EqualTo("hello"));
        }
    }
}
