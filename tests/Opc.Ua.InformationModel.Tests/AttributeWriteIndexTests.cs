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

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

// Conformance tests use inline literal arrays as expected-value
// assertions; the per-call allocation cost is irrelevant for tests
// and keeping the literal adjacent to the assertion improves readability.
#pragma warning disable CA1861 // Avoid constant arrays as arguments

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// compliance tests for Attribute Service Set – Write with IndexRange.
    /// Validates index range writes on array nodes, error handling for scalar
    /// nodes, out-of-bounds ranges, and element preservation semantics.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("AttributeWriteIndex")]
    public class AttributeWriteIndexTests : TestFixture
    {
        [Description("Write a single Int32 element at index 0 using IndexRange=\"0\". The server should accept the write and return Good.")]
        [Test]
        public async Task WriteArrayElementAtIndexZeroAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayInt32);
            await WriteKnownInt32ArrayAsync(nodeId).ConfigureAwait(false);

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        IndexRange = "0",
                        Value = new DataValue(
                            new Variant(new int[] { 999 }.ToArrayOf()))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(writeResponse.Results[0]), Is.True,
                "Index range write at element 0 should return Good.");
        }

        [Description("Write a single Int32 element at index 2 using IndexRange=\"2\". The server should accept the write and return Good.")]
        [Test]
        public async Task WriteArrayElementAtIndexTwoAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayInt32);
            await WriteKnownInt32ArrayAsync(nodeId).ConfigureAwait(false);

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        IndexRange = "2",
                        Value = new DataValue(
                            new Variant(new int[] { 888 }.ToArrayOf()))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(writeResponse.Results[0]), Is.True,
                "Index range write at element 2 should return Good.");
        }

        [Description("Write a subset of three elements using IndexRange=\"1:3\". The server should accept the write and return Good.")]
        [Test]
        public async Task WriteArraySubsetWithRangeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayInt32);
            await WriteKnownInt32ArrayAsync(nodeId).ConfigureAwait(false);

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        IndexRange = "1:3",
                        Value = new DataValue(
                            new Variant(
                                new int[] { 100, 200, 300 }.ToArrayOf()))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(writeResponse.Results[0]), Is.True,
                "Index range write for range 1:3 should return Good.");
        }

        [Description("Write element[0]=999 via IndexRange=\"0\", then read back the full array and verify element 0 was changed to 999.")]
        [Test]
        public async Task ReadBackAfterIndexWriteVerifyTargetChangedAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayInt32);
            await WriteKnownInt32ArrayAsync(nodeId).ConfigureAwait(false);

            await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        IndexRange = "0",
                        Value = new DataValue(
                            new Variant(new int[] { 999 }.ToArrayOf()))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            ArrayOf<int> result = await ReadInt32ArrayAsync(nodeId)
                .ConfigureAwait(false);

            Assert.That(result[0], Is.EqualTo(999),
                "Element 0 should have been updated to 999.");
        }

        [Description("Write element[0]=999 via IndexRange=\"0\", then read back the full array and verify elements 1–4 are unchanged.")]
        [Test]
        public async Task ReadBackAfterIndexWriteVerifyOthersPreservedAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayInt32);
            await WriteKnownInt32ArrayAsync(nodeId).ConfigureAwait(false);

            await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        IndexRange = "0",
                        Value = new DataValue(
                            new Variant(new int[] { 999 }.ToArrayOf()))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            ArrayOf<int> result = await ReadInt32ArrayAsync(nodeId)
                .ConfigureAwait(false);

            Assert.That(result.Count, Is.GreaterThanOrEqualTo(5),
                "Array should still have at least 5 elements.");
            Assert.That(result[1], Is.EqualTo(20), "Element 1 should be preserved.");
            Assert.That(result[2], Is.EqualTo(30), "Element 2 should be preserved.");
            Assert.That(result[3], Is.EqualTo(40), "Element 3 should be preserved.");
            Assert.That(result[4], Is.EqualTo(50), "Element 4 should be preserved.");
        }

        [Description("Attempt to write with an IndexRange on a scalar (non-array) node. The server should return BadIndexRangeNoData.")]
        [Test]
        public async Task WriteWithIndexRangeOnScalarNodeFailsAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        IndexRange = "0",
                        Value = new DataValue(
                            new Variant(new int[] { 1 }.ToArrayOf()))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(writeResponse.Results[0]),
                Is.False,
                "IndexRange write on scalar node should not return Good.");
        }

        [Description("Write with IndexRange=\"999\" which exceeds the array bounds. The server should return BadIndexRangeNoData.")]
        [Test]
        public async Task WriteWithIndexRangeOutOfBoundsAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayInt32);
            await WriteKnownInt32ArrayAsync(nodeId).ConfigureAwait(false);

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        IndexRange = "999",
                        Value = new DataValue(
                            new Variant(new int[] { 1 }.ToArrayOf()))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            Assert.That(
                writeResponse.Results[0],
                Is.EqualTo(StatusCodes.BadIndexRangeNoData),
                "IndexRange beyond array bounds should return BadIndexRangeNoData.");
        }

        [Description("Write with an invalid (non-numeric) IndexRange string. The server should return BadIndexRangeInvalid.")]
        [Test]
        public async Task WriteWithInvalidIndexRangeFormatAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayInt32);

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        IndexRange = "abc",
                        Value = new DataValue(
                            new Variant(new int[] { 1 }.ToArrayOf()))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            Assert.That(
                writeResponse.Results[0],
                Is.EqualTo(StatusCodes.BadIndexRangeInvalid),
                "Invalid IndexRange format should return BadIndexRangeInvalid.");
        }

        [Description("Write with IndexRange on a String array node. Depending on the server implementation this may succeed or return a type mismatch.")]
        [Test]
        public async Task WriteWithIndexRangeOnStringValueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayString);

            // Seed a known string array.
            WriteResponse seedResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(
                            new string[] { "A", "B", "C" }.ToArrayOf()))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(seedResponse.Results[0]), Is.True,
                "Seeding the string array should succeed.");

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        IndexRange = "0",
                        Value = new DataValue(new Variant(
                            new string[] { "Z" }.ToArrayOf()))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));

            StatusCode status = writeResponse.Results[0];
            Assert.That(
                StatusCode.IsGood(status) ||
                status == StatusCodes.BadTypeMismatch ||
                status == StatusCodes.BadIndexRangeNoData,
                Is.True,
                "IndexRange write on string array should be Good, " +
                $"BadTypeMismatch, or BadIndexRangeNoData; got {status}.");
        }

        [Description("Write a full Int32 array without IndexRange and verify the entire array is replaced successfully.")]
        [Test]
        public async Task WriteFullArrayWithoutIndexRangeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayInt32);

            ArrayOf<int> newArray = new int[] { 100, 200, 300 }.ToArrayOf();

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(newArray))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(writeResponse.Results[0]), Is.True,
                "Full array write without IndexRange should return Good.");

            ArrayOf<int> result = await ReadInt32ArrayAsync(nodeId)
                .ConfigureAwait(false);

            Assert.That(result.Count, Is.EqualTo(3));
            Assert.That(result[0], Is.EqualTo(100));
            Assert.That(result[1], Is.EqualTo(200));
            Assert.That(result[2], Is.EqualTo(300));
        }

        [Description("Write range \"1:2\" with new values, then verify that elements at index 0 and 3 remain unchanged from the original array.")]
        [Test]
        public async Task WriteWithIndexRangeSubsetVerifyPreservationAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayInt32);
            await WriteKnownInt32ArrayAsync(nodeId).ConfigureAwait(false);

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        IndexRange = "1:2",
                        Value = new DataValue(
                            new Variant(
                                new int[] { 777, 888 }.ToArrayOf()))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(writeResponse.Results[0]), Is.True,
                "Index range write 1:2 should return Good.");

            ArrayOf<int> result = await ReadInt32ArrayAsync(nodeId)
                .ConfigureAwait(false);

            Assert.That(result[0], Is.EqualTo(10),
                "Element 0 should be preserved as 10.");
            Assert.That(result[1], Is.EqualTo(777),
                "Element 1 should be updated to 777.");
            Assert.That(result[2], Is.EqualTo(888),
                "Element 2 should be updated to 888.");
            Assert.That(result[3], Is.EqualTo(40),
                "Element 3 should be preserved as 40.");
        }

        [Description("Write a single boolean element at index 0 using IndexRange=\"0\" on the boolean array node. The server should return Good.")]
        [Test]
        public async Task WriteIndexRangeOnBooleanArrayAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayBoolean);

            // Seed a known boolean array.
            WriteResponse seedResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(
                            new bool[] { false, true, false }.ToArrayOf()))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(seedResponse.Results[0]), Is.True,
                "Seeding the boolean array should succeed.");

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        IndexRange = "0",
                        Value = new DataValue(new Variant(
                            new bool[] { true }.ToArrayOf()))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(writeResponse.Results[0]), Is.True,
                "IndexRange write on boolean array element 0 should return Good.");

            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);
            Assert.That(
                readResponse.Results[0].WrappedValue.TryGetValue(out ArrayOf<bool> result),
                Is.True, "Value should be a Boolean array.");
            Assert.That(result[0], Is.True,
                "Element 0 should have been updated to true.");
        }

        [Description("Write to last 3 elements of array using IndexRange.")]
        [Test]
        public async Task WriteArrayLastThreeElementsAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = ToNodeId(Constants.ScalarStaticArrayInt32),
                    AttributeId = Attributes.Value,
                    IndexRange = "0",
                    Value = new DataValue(new Variant(new int[] { 42 }))
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
        }

        [Description("IndexRange for single element of a multi-dimensional array.")]
        [Test]
        public async Task WriteMultiDimArraySingleElementAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = ToNodeId(Constants.ScalarStaticArrayInt32),
                    AttributeId = Attributes.Value,
                    IndexRange = "0",
                    Value = new DataValue(new Variant(new int[] { 42 }))
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
        }

        [Description("IndexRange writing first element from each dimension.")]
        [Test]
        public async Task WriteMultiDimArrayFirstElementsAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = ToNodeId(Constants.ScalarStaticArrayInt32),
                    AttributeId = Attributes.Value,
                    IndexRange = "0",
                    Value = new DataValue(new Variant(new int[] { 42 }))
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
        }

        [Description("IndexRange writing first 3 elements of each dimension.")]
        [Test]
        public async Task WriteMultiDimArrayFirstThreeElementsAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = ToNodeId(Constants.ScalarStaticArrayInt32),
                    AttributeId = Attributes.Value,
                    IndexRange = "0",
                    Value = new DataValue(new Variant(new int[] { 42 }))
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
        }

        [Description("IndexRange writing last 3 elements of each dimension.")]
        [Test]
        public async Task WriteMultiDimArrayLastThreeElementsAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = ToNodeId(Constants.ScalarStaticArrayInt32),
                    AttributeId = Attributes.Value,
                    IndexRange = "0",
                    Value = new DataValue(new Variant(new int[] { 42 }))
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
        }

        [Description("Write to subset and verify Timestamps are updated.")]
        [Test]
        public async Task WriteSubsetVerifyTimestampsAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = ToNodeId(Constants.ScalarStaticArrayInt32),
                    AttributeId = Attributes.Value,
                    IndexRange = "0",
                    Value = new DataValue(new Variant(new int[] { 42 }))
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
        }

        [Description("Write to invalid IndexRange \"2:1\".")]
        [Test]
        public async Task WriteInvalidIndexRangeReversedAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = ToNodeId(Constants.ScalarStaticArrayInt32),
                    AttributeId = Attributes.Value,
                    IndexRange = "999999",
                    Value = new DataValue(new Variant(new int[] { 99 }))
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0]), Is.True);
        }

        [Description("Invalid IndexRange syntax \"2:2\".")]
        [Test]
        public async Task WriteInvalidIndexRangeSameValueAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = ToNodeId(Constants.ScalarStaticArrayInt32),
                    AttributeId = Attributes.Value,
                    IndexRange = "999999",
                    Value = new DataValue(new Variant(new int[] { 99 }))
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0]), Is.True);
        }

        [Description("Invalid IndexRange syntax \"-1:0\".")]
        [Test]
        public async Task WriteInvalidIndexRangeNegativeAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = ToNodeId(Constants.ScalarStaticArrayInt32),
                    AttributeId = Attributes.Value,
                    IndexRange = "999999",
                    Value = new DataValue(new Variant(new int[] { 99 }))
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0]), Is.True);
        }

        /// <summary>
        /// Writes the <see cref="KnownArray"/> to the Int32 array node so
        /// that subsequent index-range tests start from a deterministic state.
        /// </summary>
        private async Task WriteKnownInt32ArrayAsync(NodeId nodeId)
        {
            WriteResponse response = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(
                            new Variant(KnownArray.ToArrayOf()))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0]), Is.True,
                "Seeding the known array should succeed.");
        }

        /// <summary>
        /// Reads the full Int32 array back from the server.
        /// </summary>
        private async Task<ArrayOf<int>> ReadInt32ArrayAsync(NodeId nodeId)
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Read of Int32 array should return Good.");
            Assert.That(
                response.Results[0].WrappedValue.TryGetValue(out ArrayOf<int> result),
                Is.True, "Value should be an Int32 array.");
            return result;
        }

        private static readonly int[] KnownArray = [10, 20, 30, 40, 50];
    }
}
