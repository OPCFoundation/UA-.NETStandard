/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Server.Historian;

namespace Opc.Ua.Server.Tests.Historian
{
    /// <summary>
    /// Unit tests for the composite historical key and the shipped
    /// StructuredHistoryData uniqueness-key selectors.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public class HistoricalValueKeyTests
    {
        [Test]
        public void FromTimestampCreatesKeyWithoutUniquenessKey()
        {
            var key = HistoricalValueKey.FromTimestamp(BaseTime);

            Assert.That(key.SourceTimestamp, Is.EqualTo((DateTimeUtc)BaseTime));
            Assert.That(key.UniquenessKey.IsEmpty, Is.True);
            Assert.That(key.IsStructured, Is.False);
        }

        [Test]
        public void StructuredKeyReportsUniquenessKey()
        {
            var key = new HistoricalValueKey(
                BaseTime,
                KeyValuePairStructuredDataKeySelector.Encode(new QualifiedName("A", 1)));

            Assert.That(key.IsStructured, Is.True);
        }

        [Test]
        public void KeysOrderByTimestampThenUniquenessKey()
        {
            var early = new HistoricalValueKey(
                BaseTime,
                KeyValuePairStructuredDataKeySelector.Encode(new QualifiedName("Zulu")));
            var lateSameTimestamp = new HistoricalValueKey(
                BaseTime,
                KeyValuePairStructuredDataKeySelector.Encode(new QualifiedName("Alpha")));
            var nextTimestamp = new HistoricalValueKey(
                BaseTime.AddTicks(1),
                ByteString.Empty);

            // "Alpha" sorts before "Zulu" at the same timestamp.
            Assert.That(lateSameTimestamp, Is.LessThan(early));
            Assert.That(early, Is.GreaterThan(lateSameTimestamp));
            Assert.That(early, Is.LessThan(nextTimestamp));
            Assert.That(nextTimestamp, Is.GreaterThanOrEqualTo(early));

            var sameAsEarly = new HistoricalValueKey(early.SourceTimestamp, early.UniquenessKey);
            Assert.That(early, Is.LessThanOrEqualTo(sameAsEarly));
            Assert.That(early, Is.GreaterThanOrEqualTo(sameAsEarly));
            Assert.That(early.CompareTo(sameAsEarly), Is.Zero);
        }

        [Test]
        public void ComparerTreatsNullAndEmptyUniquenessKeyAsEqual()
        {
            var withEmpty = new HistoricalValueKey(BaseTime, ByteString.Empty);
            var withNull = new HistoricalValueKey(BaseTime, default);

            Assert.That(HistoricalValueKeyComparer.Instance.Equals(withEmpty, withNull), Is.True);
            Assert.That(
                HistoricalValueKeyComparer.Instance.GetHashCode(withEmpty),
                Is.EqualTo(HistoricalValueKeyComparer.Instance.GetHashCode(withNull)));
            Assert.That(HistoricalValueKeyComparer.Instance.Compare(withEmpty, withNull), Is.Zero);
        }

        [Test]
        public void ComparerOrdersEntriesForSortedStorage()
        {
            var first = new HistoricalValueKey(BaseTime, ByteString.Empty);
            var second = new HistoricalValueKey(BaseTime.AddSeconds(1), ByteString.Empty);

            Assert.That(HistoricalValueKeyComparer.Instance.Compare(first, second), Is.LessThan(0));
            Assert.That(HistoricalValueKeyComparer.Instance.Compare(second, first), Is.GreaterThan(0));
        }

        [Test]
        public void TimestampSelectorReturnsEmptyKeyForEveryValue()
        {
            var value = new DataValue(
                new Variant(1.0),
                StatusCodes.Good,
                sourceTimestamp: BaseTime,
                serverTimestamp: BaseTime);

            Assert.That(
                TimestampStructuredDataKeySelector.Instance.TryGetUniquenessKey(
                    in value,
                    out ByteString key),
                Is.True);
            Assert.That(key.IsEmpty, Is.True);
            Assert.That(
                TimestampStructuredDataKeySelector.Instance.UniquenessFields.Count,
                Is.EqualTo(1));
            Assert.That(
                TimestampStructuredDataKeySelector.Instance.UniquenessFields[0].Name,
                Is.EqualTo(BrowseNames.SourceTimestamp));
        }

        [Test]
        public void KeyValuePairSelectorDescribesUniquenessFields()
        {
            ArrayOf<QualifiedName> fields =
                KeyValuePairStructuredDataKeySelector.Instance.UniquenessFields;

            Assert.That(fields.Count, Is.EqualTo(2));
            Assert.That(fields[0].Name, Is.EqualTo(BrowseNames.SourceTimestamp));
            Assert.That(fields[1].Name, Is.EqualTo("Key"));
        }

        [Test]
        public void KeyValuePairSelectorProducesStableKeyPerKey()
        {
            DataValue first = MakePair("Temperature", 1, 21.5);
            DataValue second = MakePair("Temperature", 1, 42.0);
            DataValue other = MakePair("Pressure", 1, 21.5);
            DataValue otherNamespace = MakePair("Temperature", 2, 21.5);

            Assert.That(
                KeyValuePairStructuredDataKeySelector.Instance.TryGetUniquenessKey(
                    in first,
                    out ByteString firstKey),
                Is.True);
            Assert.That(
                KeyValuePairStructuredDataKeySelector.Instance.TryGetUniquenessKey(
                    in second,
                    out ByteString secondKey),
                Is.True);
            Assert.That(
                KeyValuePairStructuredDataKeySelector.Instance.TryGetUniquenessKey(
                    in other,
                    out ByteString otherKey),
                Is.True);
            Assert.That(
                KeyValuePairStructuredDataKeySelector.Instance.TryGetUniquenessKey(
                    in otherNamespace,
                    out ByteString otherNamespaceKey),
                Is.True);

            // The value does not participate in the identity, the key does.
            Assert.That(firstKey, Is.EqualTo(secondKey));
            Assert.That(firstKey, Is.Not.EqualTo(otherKey));
            Assert.That(firstKey, Is.Not.EqualTo(otherNamespaceKey));
            Assert.That(firstKey.IsEmpty, Is.False);
        }

        [Test]
        public void KeyValuePairSelectorRejectsForeignStructures()
        {
            var value = new DataValue(
                new Variant(1.0),
                StatusCodes.Good,
                sourceTimestamp: BaseTime,
                serverTimestamp: BaseTime);

            Assert.That(
                KeyValuePairStructuredDataKeySelector.Instance.TryGetUniquenessKey(
                    in value,
                    out ByteString key),
                Is.False);
            Assert.That(key.IsEmpty, Is.True);
        }

        [Test]
        public void KeyValuePairSelectorAcceptsNullKey()
        {
            var pair = new KeyValuePair { Key = QualifiedName.Null, Value = new Variant(1.0) };
            var value = new DataValue(
                new Variant(new ExtensionObject(pair)),
                StatusCodes.Good,
                sourceTimestamp: BaseTime,
                serverTimestamp: BaseTime);

            Assert.That(
                KeyValuePairStructuredDataKeySelector.Instance.TryGetUniquenessKey(
                    in value,
                    out ByteString key),
                Is.True);
            Assert.That(key.Length, Is.EqualTo(3));
        }

        private static DataValue MakePair(string name, ushort namespaceIndex, double reading)
        {
            var pair = new KeyValuePair
            {
                Key = new QualifiedName(name, namespaceIndex),
                Value = new Variant(reading)
            };
            return new DataValue(
                new Variant(new ExtensionObject(pair)),
                StatusCodes.Good,
                sourceTimestamp: BaseTime,
                serverTimestamp: BaseTime);
        }

        private static readonly DateTime BaseTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}
