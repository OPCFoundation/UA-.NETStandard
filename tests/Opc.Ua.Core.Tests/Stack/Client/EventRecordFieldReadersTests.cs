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
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Stack.Client
{
    /// <summary>
    /// Tests for the structured and array event field readers.
    /// </summary>
    [TestFixture]
    [Category("Core")]
    [Category("EventRecord")]
    [Parallelizable]
    public sealed class EventRecordFieldReadersTests
    {
        [Test]
        public void StandardUriStringEventFieldsPreserveVariantAbi()
        {
            System.Reflection.PropertyInfo serverUri =
                typeof(AuditClientEventTypeRecord).GetProperty("ServerUri");

            Assert.That(serverUri, Is.Not.Null);
            Assert.That(serverUri!.PropertyType, Is.EqualTo(typeof(Variant)));
        }

        [Test]
        public void CompanionUriStringEventFieldsPreserveVariantAbi()
        {
            System.Reflection.PropertyInfo productInstanceUri =
                typeof(Onboarding.DeviceRegistrationAuditEventTypeRecord)
                    .GetProperty("ProductInstanceUri");

            Assert.That(productInstanceUri, Is.Not.Null);
            Assert.That(productInstanceUri!.PropertyType, Is.EqualTo(typeof(Variant)));
        }

        [Test]
        public void GetNodeIdArrayWhenFieldContainsNodeIdsReturnsValues()
        {
            NodeId[] expected = [new NodeId(1u), new NodeId("Second", 2)];
            Variant[] fields = [new Variant(expected.ToArrayOf())];

            NodeId[] actual = EventRecordFieldReaders.GetNodeIdArray(fields, 0);

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void GetNodeIdArrayWhenFieldIsUnavailableOrMismatchedReturnsNull()
        {
            Variant[] fields = [default, Variant.From(123)];

            Assert.Multiple(() =>
            {
                Assert.That(EventRecordFieldReaders.GetNodeIdArray(fields, fields.Length), Is.Null);
                Assert.That(EventRecordFieldReaders.GetNodeIdArray(fields, 0), Is.Null);
                Assert.That(EventRecordFieldReaders.GetNodeIdArray(fields, 1), Is.Null);
            });
        }

        [Test]
        public void GetNullableUInt32ReturnsValueOrNull()
        {
            Variant[] fields = [new Variant(42u), Variant.From("wrong")];

            Assert.Multiple(() =>
            {
                Assert.That(EventRecordFieldReaders.GetNullableUInt32(fields, 0), Is.EqualTo(42u));
                Assert.That(EventRecordFieldReaders.GetNullableUInt32(fields, 1), Is.Null);
                Assert.That(EventRecordFieldReaders.GetNullableUInt32(fields, fields.Length), Is.Null);
            });
        }

        [Test]
        public void GetStringArrayReturnsValuesOrNull()
        {
            string[] expected = ["a", "b"];
            Variant[] fields =
            [
                new Variant(new ArrayOf<string>(expected)),
                Variant.From("wrong")
            ];

            Assert.Multiple(() =>
            {
                Assert.That(EventRecordFieldReaders.GetStringArray(fields, 0), Is.EqualTo(expected));
                Assert.That(EventRecordFieldReaders.GetStringArray(fields, 1), Is.Null);
                Assert.That(EventRecordFieldReaders.GetStringArray(fields, fields.Length), Is.Null);
            });
        }

        [Test]
        public void GetEncodeableWhenFieldContainsRequestedTypeReturnsValue()
        {
            var expected = new Argument { Name = "Input" };
            Variant[] fields = [new Variant(new ExtensionObject(expected))];

            Argument actual = EventRecordFieldReaders.GetEncodeable<Argument>(fields, 0);

            Assert.That(actual, Is.SameAs(expected));
        }

        [Test]
        public void GetEncodeableWhenFieldIsUnavailableOrMismatchedReturnsNull()
        {
            Variant[] fields =
            [
                Variant.From(123),
                new Variant(new ExtensionObject(new ReadValueId()))
            ];

            Assert.Multiple(() =>
            {
                Assert.That(
                    EventRecordFieldReaders.GetEncodeable<Argument>(fields, fields.Length),
                    Is.Null);
                Assert.That(
                    EventRecordFieldReaders.GetEncodeable<Argument>(fields, 0),
                    Is.Null);
                Assert.That(
                    EventRecordFieldReaders.GetEncodeable<Argument>(fields, 1),
                    Is.Null);
            });
        }

        [Test]
        public void GetEncodeableArrayWhenAllFieldsContainRequestedTypeReturnsValues()
        {
            var first = new Argument { Name = "First" };
            var second = new Argument { Name = "Second" };
            ArrayOf<ExtensionObject> extensions = new ExtensionObject[]
            {
                new(first),
                new(second)
            }.ToArrayOf();
            Variant[] fields = [new Variant(extensions)];

            Argument[] actual =
                EventRecordFieldReaders.GetEncodeableArray<Argument>(fields, 0);

            Assert.That(actual, Has.Length.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(actual[0].Name, Is.EqualTo(first.Name));
                Assert.That(actual[1].Name, Is.EqualTo(second.Name));
            });
        }

        [Test]
        public void GetEncodeableArrayWhenFieldIsUnavailableOrMismatchedReturnsNull()
        {
            ArrayOf<ExtensionObject> mixedExtensions = new ExtensionObject[]
            {
                new(new Argument { Name = "Expected" }),
                new(new ReadValueId())
            }.ToArrayOf();
            Variant[] fields =
            [
                default,
                Variant.From(123),
                new Variant(mixedExtensions)
            ];

            Assert.Multiple(() =>
            {
                Assert.That(
                    EventRecordFieldReaders.GetEncodeableArray<Argument>(
                        fields,
                        fields.Length),
                    Is.Null);
                Assert.That(
                    EventRecordFieldReaders.GetEncodeableArray<Argument>(fields, 0),
                    Is.Null);
                Assert.That(
                    EventRecordFieldReaders.GetEncodeableArray<Argument>(fields, 1),
                    Is.Null);
                Assert.That(
                    EventRecordFieldReaders.GetEncodeableArray<Argument>(fields, 2),
                    Is.Null);
            });
        }

        /// <summary>
        /// Regression: the generator selected these fields but had no reader for
        /// their .NET type, so the generated decoder never populated them and
        /// every event read back null. Decodes a real generated record through
        /// its own StandardFields layout to prove the values arrive.
        /// </summary>
        [Test]
        public void GeneratedDecoderPopulatesPreviouslyUnmappedFieldTypes()
        {
            QualifiedName[][] layout = AlarmConditionTypeRecord.Decoder.StandardFields;

            int repeatCount = IndexOf(layout, BrowseNames.ReAlarmRepeatCount);
            Assert.That(
                repeatCount,
                Is.GreaterThanOrEqualTo(0),
                "ReAlarmRepeatCount must be part of the selected fields");

            var fields = new Variant[layout.Length];
            fields[repeatCount] = Variant.From((short)5);

            AlarmConditionTypeRecord record = AlarmConditionTypeRecord.Decoder.Decode(fields);

            Assert.That(record, Is.Not.Null);
            Assert.That(
                record.ReAlarmRepeatCount,
                Is.EqualTo((short)5),
                "an Int16 field used to have no reader and stayed null");
        }

        private static int IndexOf(QualifiedName[][] layout, string browseName)
        {
            for (int ii = 0; ii < layout.Length; ii++)
            {
                QualifiedName[] path = layout[ii];
                if (path is { Length: 1 } &&
                    string.Equals(path[0].Name, browseName, System.StringComparison.Ordinal))
                {
                    return ii;
                }
            }
            return -1;
        }

        /// <summary>
        /// Regression: the generator declared and selected fields of these types
        /// but had no reader to populate them, so e.g.
        /// <c>AlarmConditionTypeRecord.ReAlarmRepeatCount</c> (an Int16) read as
        /// null for every event.
        /// </summary>
        [Test]
        public void NullableScalarReadersReturnTheFieldValue()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    EventRecordFieldReaders.GetNullableSByte([Variant.From((sbyte)-3)], 0),
                    Is.EqualTo((sbyte)-3));
                Assert.That(
                    EventRecordFieldReaders.GetNullableByte([Variant.From((byte)3)], 0),
                    Is.EqualTo((byte)3));
                Assert.That(
                    EventRecordFieldReaders.GetNullableInt16([Variant.From((short)-7)], 0),
                    Is.EqualTo((short)-7));
                Assert.That(
                    EventRecordFieldReaders.GetNullableUInt16([Variant.From((ushort)7)], 0),
                    Is.EqualTo((ushort)7));
                Assert.That(
                    EventRecordFieldReaders.GetNullableInt32([Variant.From(-11)], 0),
                    Is.EqualTo(-11));
                Assert.That(
                    EventRecordFieldReaders.GetNullableInt64([Variant.From(-13L)], 0),
                    Is.EqualTo(-13L));
                Assert.That(
                    EventRecordFieldReaders.GetNullableUInt64([Variant.From(13UL)], 0),
                    Is.EqualTo(13UL));
                Assert.That(
                    EventRecordFieldReaders.GetNullableFloat([Variant.From(1.5f)], 0),
                    Is.EqualTo(1.5f));
            });
        }

        /// <summary>
        /// The same readers return null for an absent or mismatched field rather
        /// than a misleading zero.
        /// </summary>
        [Test]
        public void NullableScalarReadersReturnNullForAnAbsentField()
        {
            Variant[] fields = [default];

            Assert.Multiple(() =>
            {
                Assert.That(EventRecordFieldReaders.GetNullableInt16(fields, 0), Is.Null);
                Assert.That(EventRecordFieldReaders.GetNullableInt16(fields, 1), Is.Null);
                Assert.That(EventRecordFieldReaders.GetNullableInt32(fields, 0), Is.Null);
                Assert.That(EventRecordFieldReaders.GetNullableGuid(fields, 0), Is.Null);
            });
        }

        /// <summary>
        /// Regression: a field whose data type has no more specific projection
        /// is emitted as a Variant property. It had no reader at all, so it was
        /// never populated.
        /// </summary>
        [Test]
        public void GetVariantReturnsTheFieldVerbatim()
        {
            Variant[] fields = [Variant.From(42), default];

            Assert.Multiple(() =>
            {
                Assert.That(EventRecordFieldReaders.GetVariant(fields, 0), Is.EqualTo(fields[0]));
                Assert.That(EventRecordFieldReaders.GetVariant(fields, 1).IsNull, Is.True);
                Assert.That(
                    EventRecordFieldReaders.GetVariant(fields, fields.Length).IsNull,
                    Is.True);
            });
        }

        /// <summary>
        /// Regression: ExpandedNodeId and QualifiedName fields had no reader.
        /// </summary>
        [Test]
        public void IdentifierReadersReturnTheFieldValue()
        {
            var expandedNodeId = new ExpandedNodeId("Tag", 0, "urn:test", 0);
            var qualifiedName = new QualifiedName("Tag", 1);

            Assert.Multiple(() =>
            {
                Assert.That(
                    EventRecordFieldReaders.GetExpandedNodeId(
                        [Variant.From(expandedNodeId)], 0),
                    Is.EqualTo(expandedNodeId));
                Assert.That(
                    EventRecordFieldReaders.GetQualifiedName(
                        [Variant.From(qualifiedName)], 0),
                    Is.EqualTo(qualifiedName));
                Assert.That(
                    EventRecordFieldReaders.GetExpandedNodeId([default], 0),
                    Is.EqualTo(ExpandedNodeId.Null));
                Assert.That(
                    EventRecordFieldReaders.GetQualifiedName([default], 0),
                    Is.EqualTo(QualifiedName.Null));
            });
        }

        /// <summary>
        /// Regression: most array-valued event fields had no reader either.
        /// </summary>
        [Test]
        public void ArrayReadersReturnTheFieldValues()
        {
            int[] integers = [1, 2, 3];
            double[] doubles = [1.5, 2.5];
            bool[] booleans = [true, false];
            Variant[] absent = [default];

            Assert.Multiple(() =>
            {
                Assert.That(
                    EventRecordFieldReaders.GetInt32Array(
                        [new Variant(integers.ToArrayOf())], 0),
                    Is.EqualTo(integers));
                Assert.That(
                    EventRecordFieldReaders.GetDoubleArray(
                        [new Variant(doubles.ToArrayOf())], 0),
                    Is.EqualTo(doubles));
                Assert.That(
                    EventRecordFieldReaders.GetBoolArray(
                        [new Variant(booleans.ToArrayOf())], 0),
                    Is.EqualTo(booleans));
                Assert.That(EventRecordFieldReaders.GetInt32Array(absent, 0), Is.Null);
                Assert.That(EventRecordFieldReaders.GetInt32Array(absent, 1), Is.Null);
            });
        }
    }
}
