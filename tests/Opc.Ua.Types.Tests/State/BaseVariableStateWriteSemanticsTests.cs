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
using System.Threading.Tasks;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.State
{
    /// <summary>
    /// Tests for the Write service semantics of the Value attribute of
    /// <see cref="BaseVariableState"/>: DataType checks on the handler path, IndexRange
    /// merging, the StatusWrite and TimestampWrite AccessLevel bits (Part 3 8.57) and the
    /// Enumeration range check (Part 4 Table 55).
    /// </summary>
    [TestFixture]
    [Category("NodeState")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class BaseVariableStateWriteSemanticsTests
    {
        private static readonly int[] s_initialArray = [1, 2, 3, 4];
        private static readonly int[] s_slice = [20, 30];
        private static readonly int[] s_mergedArray = [1, 20, 30, 4];
        private static readonly int[] s_definedEnumValues = [0, 1, 5];
        private static readonly int[] s_partlyUndefinedEnumValues = [0, 3];
        private static readonly NodeId s_enumTypeId = new(5001, 1);
        private static readonly NodeId s_unknownEnumTypeId = new(5002, 1);
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
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append("urn:test:enums");

            var typeTable = new TypeTable(namespaceUris);
            typeTable.AddSubtype(DataTypeIds.BaseDataType, NodeId.Null);
            typeTable.AddSubtype(DataTypeIds.Enumeration, DataTypeIds.BaseDataType);
            typeTable.AddSubtype(s_enumTypeId, DataTypeIds.Enumeration);
            typeTable.AddSubtype(s_unknownEnumTypeId, DataTypeIds.Enumeration);

            var factory = new EncodeableFactory();
            factory.Builder.AddEnumeratedType(
                NodeId.ToExpandedNodeId(s_enumTypeId, namespaceUris),
                new Ua.Encoders.Enumeration(
                    new XmlQualifiedName("TestEnum", "urn:test:enums"),
                    new EnumDefinition
                    {
                        Fields = new EnumField[]
                        {
                            new() { Name = "Zero", Value = 0 },
                            new() { Name = "One", Value = 1 },
                            new() { Name = "Five", Value = 5 }
                        }
                    }))
                .Commit();

            return new SystemContext(m_telemetry)
            {
                NamespaceUris = namespaceUris,
                TypeTable = typeTable,
                EncodeableFactory = factory
            };
        }

        private static BaseDataVariableState CreateVariable(
            NodeId dataType,
            int valueRank,
            byte accessLevel = AccessLevels.CurrentReadOrWrite)
        {
            return new BaseDataVariableState(null)
            {
                NodeId = new NodeId("Var", 0),
                BrowseName = new QualifiedName("Var", 0),
                DisplayName = new LocalizedText("Var"),
                DataType = dataType,
                ValueRank = valueRank,
                AccessLevel = accessLevel,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite
            };
        }

        [Test]
        public void WriteWithOnWriteValueHandlerRejectsTypeMismatch()
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState variable = CreateVariable(DataTypeIds.String, ValueRanks.Scalar);
            variable.Value = "1";
            bool called = false;
            variable.OnWriteValue = (
                ISystemContext _,
                NodeState _,
                NumericRange _,
                QualifiedName _,
                ref Variant _,
                ref StatusCode _,
                ref DateTimeUtc _) =>
            {
                called = true;
                return ServiceResult.Good;
            };

            ServiceResult result = variable.WriteAttribute(
                context,
                Attributes.Value,
                NumericRange.Null,
                new DataValue(new Variant(42)));

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(called, Is.False, "The handler must not see a value of the wrong type.");
            Assert.That(variable.Value.GetString(), Is.EqualTo("1"));
        }

        [Test]
        public void WriteWithOnWriteValueHandlerMergesIndexRangeSlice()
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState variable = CreateVariable(DataTypeIds.Int32, ValueRanks.OneDimension);
            variable.Value = Variant.From(s_initialArray);
            NumericRange observedRange = NumericRange.Null;
            variable.OnWriteValue = (
                ISystemContext _,
                NodeState _,
                NumericRange range,
                QualifiedName _,
                ref Variant _,
                ref StatusCode _,
                ref DateTimeUtc _) =>
            {
                observedRange = range;
                return ServiceResult.Good;
            };

            ServiceResult result = variable.WriteAttribute(
                context,
                Attributes.Value,
                NumericRange.Parse("1:2"),
                new DataValue(Variant.From(s_slice)));

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(observedRange.IsNull, Is.False);
            Assert.That(variable.Value.GetInt32Array(), Is.EqualTo(s_mergedArray.ToArrayOf()),
                "An index-range write must only replace the addressed elements of the cached value.");
        }

        [Test]
        public void WriteRejectsStatusCodeWithoutStatusWrite()
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState variable = CreateVariable(DataTypeIds.Double, ValueRanks.Scalar);
            variable.Value = 1.0;

            ServiceResult result = variable.WriteAttribute(
                context,
                Attributes.Value,
                NumericRange.Null,
                new DataValue(new Variant(2.0), StatusCodes.Uncertain));

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadWriteNotSupported));
            Assert.That(variable.Value.GetDouble(), Is.EqualTo(1.0));
        }

        [Test]
        public void WriteRejectsSourceTimestampWithoutTimestampWrite()
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState variable = CreateVariable(DataTypeIds.Double, ValueRanks.Scalar);
            variable.Value = 1.0;

            ServiceResult result = variable.WriteAttribute(
                context,
                Attributes.Value,
                NumericRange.Null,
                new DataValue(new Variant(2.0), StatusCodes.Good, new DateTimeUtc(2024, 1, 2, 3, 4, 5)));

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadWriteNotSupported));
            Assert.That(variable.Value.GetDouble(), Is.EqualTo(1.0));
        }

        [Test]
        public void WriteAcceptsGoodStatusAndNullTimestampWithoutAccessBits()
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState variable = CreateVariable(DataTypeIds.Double, ValueRanks.Scalar);

            ServiceResult result = variable.WriteAttribute(
                context,
                Attributes.Value,
                NumericRange.Null,
                new DataValue(new Variant(2.0), StatusCodes.Good));

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(variable.Value.GetDouble(), Is.EqualTo(2.0));
        }

        [Test]
        public void WriteStoresStatusCodeAndSourceTimestampWithAccessBits()
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState variable = CreateVariable(
                DataTypeIds.Double,
                ValueRanks.Scalar,
                (byte)(AccessLevels.CurrentReadOrWrite |
                    AccessLevels.StatusWrite |
                    AccessLevels.TimestampWrite));
            var sourceTimestamp = new DateTimeUtc(2024, 1, 2, 3, 4, 5);

            ServiceResult result = variable.WriteAttribute(
                context,
                Attributes.Value,
                NumericRange.Null,
                new DataValue(new Variant(2.0), StatusCodes.Uncertain, sourceTimestamp));

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(variable.Value.GetDouble(), Is.EqualTo(2.0));
            Assert.That(variable.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Uncertain));
            Assert.That(variable.Timestamp, Is.EqualTo(sourceTimestamp));
        }

        [Test]
        public void WriteWithStatusWriteOnlyStillRejectsSourceTimestamp()
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState variable = CreateVariable(
                DataTypeIds.Double,
                ValueRanks.Scalar,
                (byte)(AccessLevels.CurrentReadOrWrite | AccessLevels.StatusWrite));

            ServiceResult result = variable.WriteAttribute(
                context,
                Attributes.Value,
                NumericRange.Null,
                new DataValue(new Variant(2.0), StatusCodes.Uncertain, DateTimeUtc.Now));

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadWriteNotSupported));
        }

        [Test]
        public async Task WriteAttributeAsyncRejectsSourceTimestampWithoutTimestampWriteAsync()
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState variable = CreateVariable(DataTypeIds.Double, ValueRanks.Scalar);
            bool called = false;
            variable.OnWriteValueAsync = (c, n, range, value, ct) =>
            {
                called = true;
                return new ValueTask<AttributeWriteResult>(
                    new AttributeWriteResult(ServiceResult.Good));
            };

            ServiceResult result = await variable.WriteAttributeAsync(
                context,
                Attributes.Value,
                NumericRange.Null,
                new DataValue(new Variant(2.0), StatusCodes.Good, DateTimeUtc.Now)).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadWriteNotSupported));
            Assert.That(called, Is.False);
        }

        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(5, true)]
        [TestCase(2, false)]
        [TestCase(-1, false)]
        public void WriteChecksEnumerationValueIsDefined(int value, bool defined)
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState variable = CreateVariable(s_enumTypeId, ValueRanks.Scalar);
            variable.Value = 0;

            ServiceResult result = variable.WriteAttribute(
                context,
                Attributes.Value,
                NumericRange.Null,
                new DataValue(new Variant(value)));

            if (defined)
            {
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(variable.Value.GetInt32(), Is.EqualTo(value));
            }
            else
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                Assert.That(variable.Value.GetInt32(), Is.Zero);
            }
        }

        [Test]
        public void WriteRejectsEnumerationArrayWithUndefinedElement()
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState variable = CreateVariable(s_enumTypeId, ValueRanks.OneDimension);

            ServiceResult good = variable.WriteAttribute(
                context,
                Attributes.Value,
                NumericRange.Null,
                new DataValue(Variant.From(s_definedEnumValues)));
            ServiceResult bad = variable.WriteAttribute(
                context,
                Attributes.Value,
                NumericRange.Null,
                new DataValue(Variant.From(s_partlyUndefinedEnumValues)));

            Assert.That(ServiceResult.IsGood(good), Is.True);
            Assert.That(bad.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
        }

        [Test]
        public void WriteAcceptsAnyValueOfEnumerationWithUnknownDefinition()
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState variable = CreateVariable(s_unknownEnumTypeId, ValueRanks.Scalar);

            ServiceResult result = variable.WriteAttribute(
                context,
                Attributes.Value,
                NumericRange.Null,
                new DataValue(new Variant(1234)));

            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void WriteWithOnWriteValueHandlerRejectsUndefinedEnumerationValue()
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState variable = CreateVariable(s_enumTypeId, ValueRanks.Scalar);
            bool called = false;
            variable.OnWriteValue = (
                ISystemContext _,
                NodeState _,
                NumericRange _,
                QualifiedName _,
                ref Variant _,
                ref StatusCode _,
                ref DateTimeUtc _) =>
            {
                called = true;
                return ServiceResult.Good;
            };

            ServiceResult result = variable.WriteAttribute(
                context,
                Attributes.Value,
                NumericRange.Null,
                new DataValue(new Variant(3)));

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
            Assert.That(called, Is.False);
        }
    }
}
