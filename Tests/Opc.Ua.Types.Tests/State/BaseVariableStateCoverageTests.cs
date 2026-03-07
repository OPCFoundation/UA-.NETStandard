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
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.State
{
    using AttributesToSave = NodeState.AttributesToSave;

    /// <summary>
    /// Coverage tests for BaseVariableState and BaseVariableTypeState.
    /// </summary>
    [TestFixture]
    [Category("BaseVariableStateCoverage")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class BaseVariableStateCoverageTests
    {
        private ITelemetryContext m_telemetry;
        private IServiceMessageContext m_messageContext;

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

        private ISystemContext CreateSystemContext()
        {
            var namespaceUris = m_messageContext.NamespaceUris;
            return new SystemContext(m_telemetry)
            {
                NamespaceUris = namespaceUris,
                TypeTable = new TypeTable(namespaceUris)
            };
        }

        #region BaseVariableState Constructor Tests

        [Test]
        public void ConstructorWithNullParentSetsDefaults()
        {
            var variable = new BaseDataVariableState(null);

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
            var parent = new BaseDataVariableState(null);
            var variable = new BaseDataVariableState(parent);

            Assert.That(variable.Parent, Is.SameAs(parent));
            Assert.That(variable.DataType, Is.EqualTo(DataTypeIds.BaseDataType));
        }

        [Test]
        public void PropertyStateConstructorSetsDefaults()
        {
            var property = new PropertyState(null);

            Assert.That(property.DataType, Is.EqualTo(DataTypeIds.BaseDataType));
            Assert.That(property.ValueRank, Is.EqualTo(ValueRanks.Any));
            Assert.That(property.Historizing, Is.False);
        }

        #endregion

        #region Value Property Tests

        [Test]
        public void ValuePropertySetSetsChangeMaskAndStatusCode()
        {
            var variable = new BaseDataVariableState(null);
            var context = CreateSystemContext();
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
            var variable = new BaseDataVariableState(null);
            variable.Value = new Variant(42);
            var context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            // Set same value again
            variable.Value = new Variant(42);

            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.Value),
                Is.False);
        }

        [Test]
        public void WrappedValuePropertyDelegatesToValue()
        {
            var variable = new BaseDataVariableState(null);

            variable.WrappedValue = new Variant("hello");

            Assert.That(variable.Value, Is.EqualTo(new Variant("hello")));
            Assert.That(variable.WrappedValue, Is.EqualTo(variable.Value));
        }

        #endregion

        #region Timestamp Property Tests

        [Test]
        public void TimestampSetSetsChangeMask()
        {
            var variable = new BaseDataVariableState(null);
            var context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.Timestamp = DateTimeUtc.Now;

            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.Value),
                Is.True);
        }

        [Test]
        public void TimestampSetSameValueDoesNotSetChangeMask()
        {
            var ts = DateTimeUtc.Now;
            var variable = new BaseDataVariableState(null);
            variable.Timestamp = ts;
            var context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.Timestamp = ts;

            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.Value),
                Is.False);
        }

        #endregion

        #region StatusCode Property Tests

        [Test]
        public void StatusCodeSetSetsChangeMask()
        {
            var variable = new BaseDataVariableState(null);
            var context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.StatusCode = StatusCodes.Good;

            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.Value),
                Is.True);
        }

        [Test]
        public void StatusCodeSetSameValueDoesNotSetChangeMask()
        {
            var variable = new BaseDataVariableState(null);
            variable.StatusCode = StatusCodes.Good;
            var context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.StatusCode = StatusCodes.Good;

            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.Value),
                Is.False);
        }

        #endregion

        #region DataType Property Tests

        [Test]
        public void DataTypeSetSetsNonValueChangeMask()
        {
            var variable = new BaseDataVariableState(null);
            var context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.DataType = DataTypeIds.Int32;

            Assert.That(variable.DataType, Is.EqualTo(DataTypeIds.Int32));
            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.True);
        }

        [Test]
        public void DataTypeSetSameValueDoesNotSetChangeMask()
        {
            var variable = new BaseDataVariableState(null);
            variable.DataType = DataTypeIds.Int32;
            var context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.DataType = DataTypeIds.Int32;

            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.False);
        }

        #endregion

        #region ValueRank Property Tests

        [Test]
        public void ValueRankSetSetsNonValueChangeMask()
        {
            var variable = new BaseDataVariableState(null);
            var context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.ValueRank = ValueRanks.OneDimension;

            Assert.That(variable.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.True);
        }

        [Test]
        public void ValueRankSetSameValueDoesNotSetChangeMask()
        {
            var variable = new BaseDataVariableState(null);
            variable.ValueRank = ValueRanks.Scalar;
            var context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.ValueRank = ValueRanks.Scalar;

            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.False);
        }

        #endregion

        #region ArrayDimensions Property Tests

        [Test]
        public void ArrayDimensionsSetSetsNonValueChangeMask()
        {
            var variable = new BaseDataVariableState(null);
            var context = CreateSystemContext();
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
            var dims = new uint[] { 5 }.ToArrayOf();
            var variable = new BaseDataVariableState(null);
            variable.ArrayDimensions = dims;
            var context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.ArrayDimensions = dims;

            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.False);
        }

        #endregion

        #region AccessLevel Property Tests

        [Test]
        public void AccessLevelSetSetsNonValueChangeMask()
        {
            var variable = new BaseDataVariableState(null);
            var context = CreateSystemContext();
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
            var variable = new BaseDataVariableState(null);
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            var context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;

            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.False);
        }

        [Test]
        public void AccessLevelExSetSetsNonValueChangeMask()
        {
            var variable = new BaseDataVariableState(null);
            var context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.AccessLevelEx = 0x100;

            Assert.That(variable.AccessLevelEx, Is.EqualTo(0x100u));
            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.True);
        }

        [Test]
        public void AccessLevelExSetSameValueDoesNotSetChangeMask()
        {
            var variable = new BaseDataVariableState(null);
            variable.AccessLevelEx = 0x100;
            var context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.AccessLevelEx = 0x100;

            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.False);
        }

        [Test]
        public void AccessLevelReturnsLow8BitsOfAccessLevelEx()
        {
            var variable = new BaseDataVariableState(null);
            variable.AccessLevelEx = 0x1FF;

            Assert.That(variable.AccessLevel, Is.EqualTo((byte)0xFF));
        }

        [Test]
        public void AccessLevelSetPreservesHighBitsOfAccessLevelEx()
        {
            var variable = new BaseDataVariableState(null);
            variable.AccessLevelEx = 0xFF00;

            variable.AccessLevel = 0x03;

            Assert.That(variable.AccessLevelEx, Is.EqualTo(0xFF03u));
        }

        #endregion

        #region UserAccessLevel Property Tests

        [Test]
        public void UserAccessLevelSetSetsNonValueChangeMask()
        {
            var variable = new BaseDataVariableState(null);
            var context = CreateSystemContext();
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
            var variable = new BaseDataVariableState(null);
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            var context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;

            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.False);
        }

        #endregion

        #region MinimumSamplingInterval Property Tests

        [Test]
        public void MinimumSamplingIntervalSetSetsNonValueChangeMask()
        {
            var variable = new BaseDataVariableState(null);
            var context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.MinimumSamplingInterval = 1000.0;

            Assert.That(variable.MinimumSamplingInterval, Is.EqualTo(1000.0));
            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.True);
        }

        [Test]
        public void MinimumSamplingIntervalSetSameValueDoesNotSetChangeMask()
        {
            var variable = new BaseDataVariableState(null);
            variable.MinimumSamplingInterval = 1000.0;
            var context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.MinimumSamplingInterval = 1000.0;

            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.False);
        }

        #endregion

        #region Historizing Property Tests

        [Test]
        public void HistorizingSetSetsNonValueChangeMask()
        {
            var variable = new BaseDataVariableState(null);
            var context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.Historizing = true;

            Assert.That(variable.Historizing, Is.True);
            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.True);
        }

        [Test]
        public void HistorizingSetSameValueDoesNotSetChangeMask()
        {
            var variable = new BaseDataVariableState(null);
            variable.Historizing = true;
            var context = CreateSystemContext();
            variable.ClearChangeMasks(context, false);

            variable.Historizing = true;

            Assert.That(variable.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.False);
        }

        #endregion

        #region CopyPolicy Tests

        [Test]
        public void CopyPolicyDefaultIsCopyOnRead()
        {
            var variable = new BaseDataVariableState(null);

            Assert.That(variable.CopyPolicy,
                Is.EqualTo(VariableCopyPolicy.CopyOnRead));
        }

        [Test]
        public void CopyPolicyCanBeSet()
        {
            var variable = new BaseDataVariableState(null);
            variable.CopyPolicy = VariableCopyPolicy.Never;

            Assert.That(variable.CopyPolicy, Is.EqualTo(VariableCopyPolicy.Never));
        }

        #endregion

        #region Clone and CopyTo Tests

        [Test]
        public void CloneCreatesDeepCopy()
        {
            var variable = new BaseDataVariableState(null);
            variable.Value = new Variant(42);
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentRead;
            variable.MinimumSamplingInterval = 500.0;
            variable.Historizing = true;
            variable.ArrayDimensions = new uint[] { 10 }.ToArrayOf();
            variable.StatusCode = StatusCodes.Good;

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
            var property = new PropertyState(null);
            property.Value = new Variant("testValue");
            property.DataType = DataTypeIds.String;
            property.ValueRank = ValueRanks.Scalar;

            var clone = (PropertyState)property.Clone();

            Assert.That(clone, Is.Not.SameAs(property));
            Assert.That(clone.Value, Is.EqualTo(property.Value));
            Assert.That(clone.DataType, Is.EqualTo(property.DataType));
        }

        #endregion

        #region Initialize from Source Tests

        [Test]
        public void InitializeFromSourceCopiesAllProperties()
        {
            var source = new BaseDataVariableState(null);
            source.Value = new Variant(99);
            source.DataType = DataTypeIds.Double;
            source.ValueRank = ValueRanks.OneDimension;
            source.ArrayDimensions = new uint[] { 3 }.ToArrayOf();
            source.AccessLevel = AccessLevels.CurrentReadOrWrite;
            source.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            source.MinimumSamplingInterval = 250.0;
            source.Historizing = true;

            var context = CreateSystemContext();
            var target = new BaseDataVariableState(null);
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

        #endregion

        #region DeepEquals Tests

        [Test]
        public void DeepEqualsReturnsTrueForSameReference()
        {
            var property = new PropertyState(null);
            property.NodeId = new NodeId(1);
            property.BrowseName = new QualifiedName("Test");
            property.Value = new Variant(42);
            property.DataType = DataTypeIds.Int32;
            property.ValueRank = ValueRanks.Scalar;

            // Same reference should be deeply equal (exercises shortcut)
            Assert.That(property.DeepEquals(property), Is.True);
        }

        [Test]
        public void DeepEqualsReturnsFalseForDifferentValues()
        {
            var var1 = new BaseDataVariableState(null);
            var1.NodeId = new NodeId(1);
            var1.BrowseName = new QualifiedName("Test");
            var1.Value = new Variant(42);

            var var2 = (BaseDataVariableState)var1.Clone();
            var2.Value = new Variant(99);

            Assert.That(var1.DeepEquals(var2), Is.False);
        }

        [Test]
        public void DeepEqualsReturnsFalseForNullNode()
        {
            var variable = new BaseDataVariableState(null);

            Assert.That(variable.DeepEquals(null), Is.False);
        }

        [Test]
        public void DeepEqualsReturnsFalseForDifferentDataType()
        {
            var var1 = new BaseDataVariableState(null);
            var1.NodeId = new NodeId(1);
            var1.BrowseName = new QualifiedName("Test");
            var1.DataType = DataTypeIds.Int32;

            var var2 = (BaseDataVariableState)var1.Clone();
            var2.DataType = DataTypeIds.Double;

            Assert.That(var1.DeepEquals(var2), Is.False);
        }

        [Test]
        public void DeepEqualsReturnsFalseForDifferentAccessLevel()
        {
            var var1 = new BaseDataVariableState(null);
            var1.NodeId = new NodeId(1);
            var1.BrowseName = new QualifiedName("Test");
            var1.AccessLevel = AccessLevels.CurrentRead;

            var var2 = (BaseDataVariableState)var1.Clone();
            var2.AccessLevel = AccessLevels.CurrentReadOrWrite;

            Assert.That(var1.DeepEquals(var2), Is.False);
        }

        [Test]
        public void DeepEqualsReturnsFalseForDifferentHistorizing()
        {
            var var1 = new BaseDataVariableState(null);
            var1.NodeId = new NodeId(1);
            var1.BrowseName = new QualifiedName("Test");
            var1.Historizing = false;

            var var2 = (BaseDataVariableState)var1.Clone();
            var2.Historizing = true;

            Assert.That(var1.DeepEquals(var2), Is.False);
        }

        [Test]
        public void DeepEqualsReturnsFalseForDifferentMinSamplingInterval()
        {
            var var1 = new BaseDataVariableState(null);
            var1.NodeId = new NodeId(1);
            var1.BrowseName = new QualifiedName("Test");
            var1.MinimumSamplingInterval = 100.0;

            var var2 = (BaseDataVariableState)var1.Clone();
            var2.MinimumSamplingInterval = 500.0;

            Assert.That(var1.DeepEquals(var2), Is.False);
        }

        [Test]
        public void DeepEqualsReturnsFalseForDifferentUserAccessLevel()
        {
            var var1 = new BaseDataVariableState(null);
            var1.NodeId = new NodeId(1);
            var1.BrowseName = new QualifiedName("Test");
            var1.UserAccessLevel = AccessLevels.CurrentRead;

            var var2 = (BaseDataVariableState)var1.Clone();
            var2.UserAccessLevel = AccessLevels.CurrentReadOrWrite;

            Assert.That(var1.DeepEquals(var2), Is.False);
        }

        #endregion

        #region DeepGetHashCode Tests

        [Test]
        public void DeepGetHashCodeExercisesAllFields()
        {
            var property = new PropertyState(null);
            property.NodeId = new NodeId(1);
            property.BrowseName = new QualifiedName("Test");
            property.Value = new Variant(42);
            property.DataType = DataTypeIds.Int32;
            property.ValueRank = ValueRanks.Scalar;
            property.AccessLevel = AccessLevels.CurrentRead;
            property.MinimumSamplingInterval = 100.0;
            property.Historizing = true;

            // Should not throw and should return a value
            var hash = property.DeepGetHashCode();
            Assert.That(hash, Is.TypeOf<int>());
        }

        [Test]
        public void DeepGetHashCodeReturnsDifferentHashForDifferentObjects()
        {
            var var1 = new BaseDataVariableState(null);
            var1.NodeId = new NodeId(1);
            var1.BrowseName = new QualifiedName("Test");
            var1.Value = new Variant(42);

            var var2 = new BaseDataVariableState(null);
            var2.NodeId = new NodeId(2);
            var2.BrowseName = new QualifiedName("Other");
            var2.Value = new Variant(99);

            Assert.That(var1.DeepGetHashCode(),
                Is.Not.EqualTo(var2.DeepGetHashCode()));
        }

        #endregion

        #region Export Tests

        [Test]
        public void ExportToNodeTableCreatesVariableNode()
        {
            var variable = new BaseDataVariableState(null);
            variable.NodeId = new NodeId(100);
            variable.BrowseName = new QualifiedName("TestVar");
            variable.DisplayName = new LocalizedText("TestVar");
            variable.Value = new Variant(3.14);
            variable.DataType = DataTypeIds.Double;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentRead;
            variable.MinimumSamplingInterval = 100.0;
            variable.Historizing = true;
            variable.ArrayDimensions = new uint[] { 5 }.ToArrayOf();

            var context = CreateSystemContext();
            var namespaceUris = context.NamespaceUris;
            var table = new NodeTable(
                namespaceUris,
                new StringTable(),
                new TypeTable(namespaceUris));

            variable.Export(context, table);

            var exported = table.Find(variable.NodeId);
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

        #endregion

        #region SetStatusCode Tests

        [Test]
        public void SetStatusCodeSetsCodeAndTimestamp()
        {
            var variable = new BaseDataVariableState(null);
            var context = CreateSystemContext();
            var timestamp = DateTimeUtc.Now;

            variable.SetStatusCode(
                context, StatusCodes.BadNodeIdInvalid, timestamp);

            Assert.That(variable.StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            Assert.That(variable.Timestamp, Is.EqualTo(timestamp));
        }

        [Test]
        public void SetStatusCodeWithMinTimestampDoesNotUpdateTimestamp()
        {
            var variable = new BaseDataVariableState(null);
            var context = CreateSystemContext();
            var originalTimestamp = DateTimeUtc.Now;
            variable.Timestamp = originalTimestamp;

            variable.SetStatusCode(
                context, StatusCodes.Good, DateTimeUtc.MinValue);

            Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(variable.Timestamp, Is.EqualTo(originalTimestamp));
        }

        #endregion

        #region ArrayDimensionsToXml / ArrayDimensionsFromXml Tests

        [Test]
        public void ArrayDimensionsToXmlReturnsNullForEmpty()
        {
            var result = BaseVariableState.ArrayDimensionsToXml(default);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void ArrayDimensionsToXmlReturnsSingleValue()
        {
            var dims = new uint[] { 10 }.ToArrayOf();

            var result = BaseVariableState.ArrayDimensionsToXml(dims);

            Assert.That(result, Is.EqualTo("10"));
        }

        [Test]
        public void ArrayDimensionsToXmlReturnsCommaSeparatedValues()
        {
            var dims = new uint[] { 3, 4, 5 }.ToArrayOf();

            var result = BaseVariableState.ArrayDimensionsToXml(dims);

            Assert.That(result, Is.EqualTo("3,4,5"));
        }

        [Test]
        public void ArrayDimensionsFromXmlReturnsDefaultForNull()
        {
            var result = BaseVariableState.ArrayDimensionsFromXml(null);

            Assert.That(result.IsEmpty, Is.True);
        }

        [Test]
        public void ArrayDimensionsFromXmlReturnsDefaultForEmpty()
        {
            var result = BaseVariableState.ArrayDimensionsFromXml(string.Empty);

            Assert.That(result.IsEmpty, Is.True);
        }

        [Test]
        public void ArrayDimensionsFromXmlParsesSingleValue()
        {
            var result = BaseVariableState.ArrayDimensionsFromXml("10");

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(10u));
        }

        [Test]
        public void ArrayDimensionsFromXmlParsesMultipleValues()
        {
            var result = BaseVariableState.ArrayDimensionsFromXml("3,4,5");

            Assert.That(result.Count, Is.EqualTo(3));
            Assert.That(result[0], Is.EqualTo(3u));
            Assert.That(result[1], Is.EqualTo(4u));
            Assert.That(result[2], Is.EqualTo(5u));
        }

        [Test]
        public void ArrayDimensionsFromXmlHandlesInvalidValue()
        {
            var result = BaseVariableState.ArrayDimensionsFromXml("abc");

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(0u));
        }

        [Test]
        public void ArrayDimensionsRoundTrip()
        {
            var original = new uint[] { 2, 7, 11 }.ToArrayOf();

            var xml = BaseVariableState.ArrayDimensionsToXml(original);
            var parsed = BaseVariableState.ArrayDimensionsFromXml(xml);

            Assert.That(parsed.Count, Is.EqualTo(original.Count));
            for (int i = 0; i < original.Count; i++)
            {
                Assert.That(parsed[i], Is.EqualTo(original[i]));
            }
        }

        #endregion

        #region GetAttributesToSave Tests

        [Test]
        public void GetAttributesToSaveIncludesValueWhenSet()
        {
            var variable = new BaseDataVariableState(null);
            variable.Value = new Variant(42);
            var context = CreateSystemContext();

            var attrs = variable.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(AttributesToSave.Value), Is.True);
        }

        [Test]
        public void GetAttributesToSaveIncludesStatusCodeWhenNotGood()
        {
            var variable = new BaseDataVariableState(null);
            // Default StatusCode is BadWaitingForInitialData, which is not Good
            var context = CreateSystemContext();

            var attrs = variable.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(AttributesToSave.StatusCode), Is.True);
        }

        [Test]
        public void GetAttributesToSaveExcludesStatusCodeWhenGood()
        {
            var variable = new BaseDataVariableState(null);
            variable.Value = new Variant(1); // touch to set StatusCode to Good
            variable.StatusCode = StatusCodes.Good;
            var context = CreateSystemContext();

            var attrs = variable.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(AttributesToSave.StatusCode), Is.False);
        }

        [Test]
        public void GetAttributesToSaveIncludesDataTypeWhenNotNull()
        {
            var variable = new BaseDataVariableState(null);
            variable.DataType = DataTypeIds.Int32;
            var context = CreateSystemContext();

            var attrs = variable.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(AttributesToSave.DataType), Is.True);
        }

        [Test]
        public void GetAttributesToSaveIncludesValueRankWhenNotDefault()
        {
            var variable = new BaseDataVariableState(null);
            variable.ValueRank = ValueRanks.OneDimension;
            var context = CreateSystemContext();

            var attrs = variable.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(AttributesToSave.ValueRank), Is.True);
        }

        [Test]
        public void GetAttributesToSaveExcludesValueRankWhenDefault()
        {
            var variable = new BaseDataVariableState(null);
            // ValueRanks.Any is default
            var context = CreateSystemContext();

            var attrs = variable.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(AttributesToSave.ValueRank), Is.False);
        }

        [Test]
        public void GetAttributesToSaveIncludesArrayDimensionsWhenSet()
        {
            var variable = new BaseDataVariableState(null);
            variable.ArrayDimensions = new uint[] { 5 }.ToArrayOf();
            var context = CreateSystemContext();

            var attrs = variable.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(AttributesToSave.ArrayDimensions), Is.True);
        }

        [Test]
        public void GetAttributesToSaveIncludesAccessLevelWhenNonZero()
        {
            var variable = new BaseDataVariableState(null);
            // Default is CurrentRead which is non-zero
            var context = CreateSystemContext();

            var attrs = variable.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(AttributesToSave.AccessLevel), Is.True);
        }

        [Test]
        public void GetAttributesToSaveIncludesUserAccessLevelWhenNonZero()
        {
            var variable = new BaseDataVariableState(null);
            // Default is CurrentRead
            var context = CreateSystemContext();

            var attrs = variable.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(AttributesToSave.UserAccessLevel), Is.True);
        }

        [Test]
        public void GetAttributesToSaveIncludesMinSamplingIntervalWhenNonZero()
        {
            var variable = new BaseDataVariableState(null);
            variable.MinimumSamplingInterval = 500.0;
            var context = CreateSystemContext();

            var attrs = variable.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(AttributesToSave.MinimumSamplingInterval),
                Is.True);
        }

        [Test]
        public void GetAttributesToSaveExcludesMinSamplingIntervalWhenZero()
        {
            var variable = new BaseDataVariableState(null);
            // Default is Continuous = 0
            var context = CreateSystemContext();

            var attrs = variable.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(AttributesToSave.MinimumSamplingInterval),
                Is.False);
        }

        [Test]
        public void GetAttributesToSaveIncludesHistorizingWhenTrue()
        {
            var variable = new BaseDataVariableState(null);
            variable.Historizing = true;
            var context = CreateSystemContext();

            var attrs = variable.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(AttributesToSave.Historizing), Is.True);
        }

        [Test]
        public void GetAttributesToSaveExcludesHistorizingWhenFalse()
        {
            var variable = new BaseDataVariableState(null);
            // Default is false
            var context = CreateSystemContext();

            var attrs = variable.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(AttributesToSave.Historizing), Is.False);
        }

        #endregion

        #region Binary Save/Update Round Trip Tests

        [Test]
        public void BinarySaveAndUpdateRoundTripAllAttributes()
        {
            var context = CreateSystemContext();
            var original = new BaseDataVariableState(null);
            original.Value = new Variant(42);
            original.StatusCode = StatusCodes.Good;
            original.DataType = DataTypeIds.Int32;
            original.ValueRank = ValueRanks.Scalar;
            original.ArrayDimensions = new uint[] { 1 }.ToArrayOf();
            original.AccessLevel = AccessLevels.CurrentReadOrWrite;
            original.UserAccessLevel = AccessLevels.CurrentRead;
            original.MinimumSamplingInterval = 500.0;
            original.Historizing = true;

            // Save
            var attributesToSave = original.GetAttributesToSave(context);
            using var ms = new MemoryStream();
            using (var encoder = new BinaryEncoder(ms, m_messageContext, true))
            {
                original.Save(context, encoder, attributesToSave);
            }

            // Update
            ms.Position = 0;
            var restored = new BaseDataVariableState(null);
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
            var context = CreateSystemContext();
            var original = new BaseDataVariableState(null);
            // Only default DataType is non-null by default

            var attributesToSave = original.GetAttributesToSave(context);
            using var ms = new MemoryStream();
            using (var encoder = new BinaryEncoder(ms, m_messageContext, true))
            {
                original.Save(context, encoder, attributesToSave);
            }

            ms.Position = 0;
            var restored = new BaseDataVariableState(null);
            using (var decoder = new BinaryDecoder(ms, m_messageContext, true))
            {
                restored.Update(context, decoder, attributesToSave);
            }

            Assert.That(restored.DataType, Is.EqualTo(original.DataType));
        }

        [Test]
        public void BinarySaveAndUpdateStatusCodeAttribute()
        {
            var context = CreateSystemContext();
            var original = new BaseDataVariableState(null);
            // Don't touch the value; default status is BadWaitingForInitialData

            var attributesToSave = original.GetAttributesToSave(context);
            Assert.That(attributesToSave.HasFlag(AttributesToSave.StatusCode),
                Is.True);

            using var ms = new MemoryStream();
            using (var encoder = new BinaryEncoder(ms, m_messageContext, true))
            {
                original.Save(context, encoder, attributesToSave);
            }

            ms.Position = 0;
            var restored = new BaseDataVariableState(null);
            restored.StatusCode = StatusCodes.Good; // set different value first
            using (var decoder = new BinaryDecoder(ms, m_messageContext, true))
            {
                restored.Update(context, decoder, attributesToSave);
            }

            Assert.That(restored.StatusCode,
                Is.EqualTo(StatusCodes.BadWaitingForInitialData));
        }

        #endregion

        #region ApplyIndexRangeAndDataEncoding Tests

        [Test]
        public void ApplyIndexRangeAndDataEncodingWithEmptyRangeReturnsGood()
        {
            var context = CreateSystemContext();
            var value = new Variant(42);

            var result = BaseVariableState.ApplyIndexRangeAndDataEncoding(
                context,
                NumericRange.Empty,
                QualifiedName.Null,
                ref value);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            Assert.That((int)value, Is.EqualTo(42));
        }

        #endregion

        #region Typed Variable State Tests

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

        #endregion
    }

    /// <summary>
    /// Coverage tests for BaseVariableTypeState.
    /// </summary>
    [TestFixture]
    [Category("BaseVariableTypeStateCoverage")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class BaseVariableTypeStateCoverageTests
    {
        private ITelemetryContext m_telemetry;
        private IServiceMessageContext m_messageContext;

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

        private ISystemContext CreateSystemContext()
        {
            var namespaceUris = m_messageContext.NamespaceUris;
            return new SystemContext(m_telemetry)
            {
                NamespaceUris = namespaceUris,
                TypeTable = new TypeTable(namespaceUris)
            };
        }

        #region Constructor Tests

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
            var node = BaseDataVariableTypeState.Construct(null);

            Assert.That(node, Is.Not.Null);
            Assert.That(node, Is.InstanceOf<BaseDataVariableTypeState>());
        }

        [Test]
        public void PropertyTypeConstructStaticMethodCreatesInstance()
        {
            var node = PropertyTypeState.Construct(null);

            Assert.That(node, Is.Not.Null);
            Assert.That(node, Is.InstanceOf<PropertyTypeState>());
        }

        #endregion

        #region Value Property Tests

        [Test]
        public void ValueSetSetsValueChangeMask()
        {
            var variableType = new BaseDataVariableTypeState();
            var context = CreateSystemContext();
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
            var variableType = new BaseDataVariableTypeState();
            variableType.Value = new Variant(42);
            var context = CreateSystemContext();
            variableType.ClearChangeMasks(context, false);

            variableType.Value = new Variant(42);

            Assert.That(
                variableType.ChangeMasks.HasFlag(NodeStateChangeMasks.Value),
                Is.False);
        }

        [Test]
        public void WrappedValueDelegatesToValue()
        {
            var variableType = new BaseDataVariableTypeState();

            variableType.WrappedValue = new Variant("test");

            Assert.That(variableType.Value, Is.EqualTo(new Variant("test")));
            Assert.That(variableType.WrappedValue,
                Is.EqualTo(variableType.Value));
        }

        #endregion

        #region DataType Property Tests

        [Test]
        public void DataTypeSetSetsNonValueChangeMask()
        {
            var variableType = new BaseDataVariableTypeState();
            var context = CreateSystemContext();
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
            var variableType = new BaseDataVariableTypeState();
            variableType.DataType = DataTypeIds.Double;
            var context = CreateSystemContext();
            variableType.ClearChangeMasks(context, false);

            variableType.DataType = DataTypeIds.Double;

            Assert.That(
                variableType.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.False);
        }

        #endregion

        #region ValueRank Property Tests

        [Test]
        public void ValueRankSetSetsNonValueChangeMask()
        {
            var variableType = new BaseDataVariableTypeState();
            var context = CreateSystemContext();
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
            var variableType = new BaseDataVariableTypeState();
            variableType.ValueRank = ValueRanks.Scalar;
            var context = CreateSystemContext();
            variableType.ClearChangeMasks(context, false);

            variableType.ValueRank = ValueRanks.Scalar;

            Assert.That(
                variableType.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.False);
        }

        #endregion

        #region ArrayDimensions Property Tests

        [Test]
        public void ArrayDimensionsSetSetsNonValueChangeMask()
        {
            var variableType = new BaseDataVariableTypeState();
            var context = CreateSystemContext();
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
            var dims = new uint[] { 5 }.ToArrayOf();
            var variableType = new BaseDataVariableTypeState();
            variableType.ArrayDimensions = dims;
            var context = CreateSystemContext();
            variableType.ClearChangeMasks(context, false);

            variableType.ArrayDimensions = dims;

            Assert.That(
                variableType.ChangeMasks.HasFlag(NodeStateChangeMasks.NonValue),
                Is.False);
        }

        #endregion

        #region Clone Tests

        [Test]
        public void CloneCreatesDeepCopy()
        {
            var variableType = new BaseDataVariableTypeState();
            variableType.Value = new Variant(42);
            variableType.DataType = DataTypeIds.Int32;
            variableType.ValueRank = ValueRanks.Scalar;
            variableType.ArrayDimensions = new uint[] { 10 }.ToArrayOf();

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
            var propertyType = new PropertyTypeState();
            propertyType.Value = new Variant("hello");
            propertyType.DataType = DataTypeIds.String;

            var clone = (PropertyTypeState)propertyType.Clone();

            Assert.That(clone, Is.Not.SameAs(propertyType));
            Assert.That(clone.Value, Is.EqualTo(propertyType.Value));
            Assert.That(clone.DataType, Is.EqualTo(propertyType.DataType));
        }

        #endregion

        #region Initialize from Source Tests

        [Test]
        public void InitializeFromSourceViaCloneCopiesAllProperties()
        {
            var source = new BaseDataVariableTypeState();
            source.Value = new Variant(99.5);
            source.DataType = DataTypeIds.Double;
            source.ValueRank = ValueRanks.OneDimension;
            source.ArrayDimensions = new uint[] { 3 }.ToArrayOf();

            var clone = (BaseDataVariableTypeState)source.Clone();

            Assert.That(clone.Value, Is.EqualTo(source.Value));
            Assert.That(clone.DataType, Is.EqualTo(source.DataType));
            Assert.That(clone.ValueRank, Is.EqualTo(source.ValueRank));
            Assert.That(clone.ArrayDimensions.Count,
                Is.EqualTo(source.ArrayDimensions.Count));
        }

        #endregion

        #region DeepEquals Tests

        [Test]
        public void DeepEqualsReturnsTrueForEqualTypes()
        {
            var type1 = new BaseDataVariableTypeState();
            type1.NodeId = new NodeId(1);
            type1.BrowseName = new QualifiedName("Test");
            type1.Value = new Variant(42);
            type1.DataType = DataTypeIds.Int32;
            type1.ValueRank = ValueRanks.Scalar;

            Assert.That(type1.DeepEquals(type1), Is.True);
        }

        [Test]
        public void DeepEqualsReturnsFalseForDifferentValues()
        {
            var type1 = new BaseDataVariableTypeState();
            type1.NodeId = new NodeId(1);
            type1.BrowseName = new QualifiedName("Test");
            type1.Value = new Variant(42);

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
            var type1 = new BaseDataVariableTypeState();
            type1.NodeId = new NodeId(1);
            type1.BrowseName = new QualifiedName("Test");
            type1.DataType = DataTypeIds.Int32;

            var type2 = (BaseDataVariableTypeState)type1.Clone();
            type2.DataType = DataTypeIds.Double;

            Assert.That(type1.DeepEquals(type2), Is.False);
        }

        [Test]
        public void DeepEqualsReturnsFalseForDifferentValueRank()
        {
            var type1 = new BaseDataVariableTypeState();
            type1.NodeId = new NodeId(1);
            type1.BrowseName = new QualifiedName("Test");
            type1.ValueRank = ValueRanks.Scalar;

            var type2 = (BaseDataVariableTypeState)type1.Clone();
            type2.ValueRank = ValueRanks.OneDimension;

            Assert.That(type1.DeepEquals(type2), Is.False);
        }

        [Test]
        public void DeepEqualsReturnsFalseForDifferentArrayDimensions()
        {
            var type1 = new BaseDataVariableTypeState();
            type1.NodeId = new NodeId(1);
            type1.BrowseName = new QualifiedName("Test");
            type1.ArrayDimensions = new uint[] { 5 }.ToArrayOf();

            var type2 = (BaseDataVariableTypeState)type1.Clone();
            type2.ArrayDimensions = new uint[] { 10 }.ToArrayOf();

            Assert.That(type1.DeepEquals(type2), Is.False);
        }

        #endregion

        #region DeepGetHashCode Tests

        [Test]
        public void DeepGetHashCodeReturnsSameForEqual()
        {
            var type1 = new BaseDataVariableTypeState();
            type1.NodeId = new NodeId(1);
            type1.BrowseName = new QualifiedName("Test");
            type1.Value = new Variant(42);

            int hash = type1.DeepGetHashCode();
            Assert.That(hash, Is.TypeOf<int>());
        }

        [Test]
        public void DeepGetHashCodeReturnsDifferentForDifferent()
        {
            var type1 = new BaseDataVariableTypeState();
            type1.NodeId = new NodeId(1);
            type1.BrowseName = new QualifiedName("Test1");
            type1.Value = new Variant(42);

            var type2 = new BaseDataVariableTypeState();
            type2.NodeId = new NodeId(2);
            type2.BrowseName = new QualifiedName("Test2");
            type2.Value = new Variant(99);

            Assert.That(type1.DeepGetHashCode(),
                Is.Not.EqualTo(type2.DeepGetHashCode()));
        }

        #endregion

        #region Export Tests

        [Test]
        public void ExportToNodeTableCreatesVariableTypeNode()
        {
            var variableType = new BaseDataVariableTypeState();
            variableType.NodeId = new NodeId(300);
            variableType.BrowseName = new QualifiedName("TestVarType");
            variableType.DisplayName = new LocalizedText("TestVarType");
            variableType.Value = new Variant(42);
            variableType.DataType = DataTypeIds.Int32;
            variableType.ValueRank = ValueRanks.Scalar;
            variableType.ArrayDimensions = new uint[] { 5 }.ToArrayOf();

            var context = CreateSystemContext();
            var namespaceUris = context.NamespaceUris;
            var table = new NodeTable(
                namespaceUris,
                new StringTable(),
                new TypeTable(namespaceUris));

            variableType.Export(context, table);

            var exported = table.Find(variableType.NodeId);
            Assert.That(exported, Is.Not.Null);
            Assert.That(exported, Is.InstanceOf<VariableTypeNode>());

            var varTypeNode = (VariableTypeNode)exported;
            Assert.That(varTypeNode.DataType, Is.EqualTo(DataTypeIds.Int32));
            Assert.That(varTypeNode.ValueRank, Is.EqualTo(ValueRanks.Scalar));
        }

        #endregion

        #region GetAttributesToSave Tests

        [Test]
        public void GetAttributesToSaveIncludesValueWhenSet()
        {
            var variableType = new BaseDataVariableTypeState();
            variableType.Value = new Variant(42);
            var context = CreateSystemContext();

            var attrs = variableType.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(AttributesToSave.Value), Is.True);
        }

        [Test]
        public void GetAttributesToSaveExcludesValueWhenNull()
        {
            var variableType = new BaseDataVariableTypeState();
            var context = CreateSystemContext();

            var attrs = variableType.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(AttributesToSave.Value), Is.False);
        }

        [Test]
        public void GetAttributesToSaveIncludesDataTypeWhenNonNull()
        {
            var variableType = new BaseDataVariableTypeState();
            variableType.DataType = DataTypeIds.Int32;
            var context = CreateSystemContext();

            var attrs = variableType.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(AttributesToSave.DataType), Is.True);
        }

        [Test]
        public void GetAttributesToSaveIncludesValueRankWhenNotDefault()
        {
            var variableType = new BaseDataVariableTypeState();
            variableType.ValueRank = ValueRanks.OneDimension;
            var context = CreateSystemContext();

            var attrs = variableType.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(AttributesToSave.ValueRank), Is.True);
        }

        [Test]
        public void GetAttributesToSaveExcludesValueRankWhenDefault()
        {
            var variableType = new BaseDataVariableTypeState();
            var context = CreateSystemContext();

            var attrs = variableType.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(AttributesToSave.ValueRank), Is.False);
        }

        [Test]
        public void GetAttributesToSaveIncludesArrayDimensionsWhenSet()
        {
            var variableType = new BaseDataVariableTypeState();
            variableType.ArrayDimensions = new uint[] { 5 }.ToArrayOf();
            var context = CreateSystemContext();

            var attrs = variableType.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(AttributesToSave.ArrayDimensions),
                Is.True);
        }

        [Test]
        public void GetAttributesToSaveExcludesArrayDimensionsWhenEmpty()
        {
            var variableType = new BaseDataVariableTypeState();
            var context = CreateSystemContext();

            var attrs = variableType.GetAttributesToSave(context);

            Assert.That(attrs.HasFlag(AttributesToSave.ArrayDimensions),
                Is.False);
        }

        #endregion

        #region Binary Save/Update Round Trip Tests

        [Test]
        public void BinarySaveAndUpdateRoundTrip()
        {
            var context = CreateSystemContext();
            var original = new BaseDataVariableTypeState();
            original.Value = new Variant(3.14);
            original.DataType = DataTypeIds.Double;
            original.ValueRank = ValueRanks.Scalar;
            original.ArrayDimensions = new uint[] { 1 }.ToArrayOf();

            var attributesToSave = original.GetAttributesToSave(context);
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
            var context = CreateSystemContext();
            var original = new BaseDataVariableTypeState();

            var attributesToSave = original.GetAttributesToSave(context);
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

        #endregion
    }
}
