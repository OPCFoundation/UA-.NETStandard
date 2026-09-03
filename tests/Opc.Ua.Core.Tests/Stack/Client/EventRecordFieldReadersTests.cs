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
    }
}
