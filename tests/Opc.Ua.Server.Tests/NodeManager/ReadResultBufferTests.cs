// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies the pooled storage of a Read: the lists the node managers fill, and the
    /// response that returns its values to the pool once the transport has encoded it.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    [Parallelizable(ParallelScope.All)]
    public sealed class ReadResultBufferTests
    {
        /// <summary>
        /// The values of a Read response are encoded before the transport reuses the
        /// response; returning the buffer then clears it, and what later requests write
        /// into the same arrays does not change the encoded response.
        /// </summary>
        [Test]
        public void ValuesSurviveEncodingAfterTheBufferWasReturned()
        {
            // not retained, so no other test can rent it while it is inspected.
            ReadResultBuffer buffer = ReadResultBuffer.CreateUnpooled(3);
            DataValue[] values = buffer.Values;
            buffer.ValueList[0] = new DataValue(new Variant(42));
            buffer.ValueList[1] = new DataValue(new Variant("text"));
            buffer.ValueList[2] = DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown, DateTime.UtcNow);
            var response = new PooledReadResponse(buffer);

            byte[] encoded = Encode(response);
            ((IPooledEncodeable)response).Reuse();

            Assert.Multiple(() =>
            {
                Assert.That(buffer.Count, Is.Zero, "returned");
                Assert.That(values.Take(3).All(value => value.IsNull), Is.True, "cleared on return");
                Assert.That(response.Results.Count, Is.Zero, "a reused response shows no results");
            });

            // a later request fills the same arrays.
            values[0] = new DataValue(new Variant(-1));
            values[1] = new DataValue(new Variant("other"));

            ReadResponse decoded = Decode(encoded);
            Assert.Multiple(() =>
            {
                Assert.That(decoded.Results.Count, Is.EqualTo(3));
                Assert.That(decoded.Results[0].WrappedValue.GetInt32(), Is.EqualTo(42));
                Assert.That(decoded.Results[1].WrappedValue.GetString(), Is.EqualTo("text"));
                Assert.That(decoded.Results[2].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            });
        }

        /// <summary>
        /// A response reused twice returns its buffer once.
        /// </summary>
        [Test]
        public void ReuseReturnsTheBufferOnce()
        {
            ReadResultBuffer buffer = ReadResultBuffer.Rent(2);
            var response = new PooledReadResponse(buffer);

            ((IPooledEncodeable)response).Reuse();
            ReadResultBuffer next = ReadResultBuffer.Rent(2);
            next.ValueList[0] = new DataValue(new Variant(7));

            // a second reuse must not return the buffer another request may have rented.
            ((IPooledEncodeable)response).Reuse();

            Assert.Multiple(() =>
            {
                Assert.That(next.Count, Is.EqualTo(2));
                Assert.That(next.ValueList[0].WrappedValue.GetInt32(), Is.EqualTo(7));
            });
            next.Return();
        }

        /// <summary>
        /// A clone of a pooled response owns no buffer and keeps its values after the
        /// original was reused.
        /// </summary>
        [Test]
        public void CloneKeepsItsValuesAfterTheOriginalWasReused()
        {
            ReadResultBuffer buffer = ReadResultBuffer.CreateUnpooled(2);
            buffer.ValueList[0] = new DataValue(new Variant(1));
            buffer.ValueList[1] = new DataValue(new Variant(2));
            var response = new PooledReadResponse(buffer);

            var clone = (ReadResponse)response.Clone();
            ((IPooledEncodeable)response).Reuse();
            ((IPooledEncodeable)clone).Reuse();

            Assert.Multiple(() =>
            {
                Assert.That(clone.Results.Count, Is.EqualTo(2));
                Assert.That(clone.Results[0].WrappedValue.GetInt32(), Is.EqualTo(1));
                Assert.That(clone.Results[1].WrappedValue.GetInt32(), Is.EqualTo(2));
            });
        }

        /// <summary>
        /// The node managers see lists of exactly the request's length, although the
        /// pooled arrays are larger: like an array, they can be written by index but not
        /// resized.
        /// </summary>
        [Test]
        public void ListsHaveTheRequestLengthAndAFixedSize()
        {
            ReadResultBuffer buffer = ReadResultBuffer.Rent(3);
            try
            {
                IList<DataValue> values = buffer.ValueList;
                IList<ServiceResult> errors = buffer.ErrorList;
                values[2] = new DataValue(new Variant(5));
                errors[1] = new ServiceResult(StatusCodes.BadNotReadable);

                Assert.Multiple(() =>
                {
                    Assert.That(buffer.Values, Has.Length.GreaterThan(3), "pooled capacity");
                    Assert.That(values, Has.Count.EqualTo(3));
                    Assert.That(errors, Has.Count.EqualTo(3));
                    Assert.That(values.IsReadOnly, Is.True, "fixed size, like an array");
                    Assert.That(values.ToList(), Has.Count.EqualTo(3), "enumerates the request's entries");
                    Assert.That(values.IndexOf(values[2]), Is.EqualTo(2));
                    Assert.That(errors.IndexOf(errors[1]), Is.EqualTo(1));
                    Assert.That(buffer.Results.Count, Is.EqualTo(3));
                    Assert.That(buffer.Results[2].WrappedValue.GetInt32(), Is.EqualTo(5));
                    Assert.That(() => values[3], Throws.TypeOf<ArgumentOutOfRangeException>());
                    Assert.That(() => values[3] = default, Throws.TypeOf<ArgumentOutOfRangeException>());
                    Assert.That(() => values.Add(default), Throws.TypeOf<NotSupportedException>());
                    Assert.That(() => values.RemoveAt(0), Throws.TypeOf<NotSupportedException>());
                    Assert.That(() => errors.Clear(), Throws.TypeOf<NotSupportedException>());
                });

                var copy = new DataValue[3];
                values.CopyTo(copy, 0);
                Assert.That(copy[2].WrappedValue.GetInt32(), Is.EqualTo(5));
            }
            finally
            {
                buffer.Return();
            }
        }

        /// <summary>
        /// The operation results and the attribute cache are cleared once the dispatch is
        /// done, while the values stay for the response.
        /// </summary>
        [Test]
        public void ReleaseDispatchStateKeepsTheValues()
        {
            ReadResultBuffer buffer = ReadResultBuffer.CreateUnpooled(2);
            buffer.ValueList[0] = new DataValue(new Variant(3));
            buffer.ErrorList[0] = new ServiceResult(StatusCodes.BadNotReadable);
            buffer.AttributeCache[new NodeId(1)] = [new Variant(1)];

            buffer.ReleaseDispatchState();

            Assert.Multiple(() =>
            {
                Assert.That(buffer.ErrorList[0], Is.Null);
                Assert.That(buffer.AttributeCache, Is.Empty);
                Assert.That(buffer.ValueList[0].WrappedValue.GetInt32(), Is.EqualTo(3));
            });
        }

        /// <summary>
        /// A request larger than the largest pooled capacity gets a buffer of its exact
        /// size, which is not retained.
        /// </summary>
        [Test]
        public void OversizedRequestGetsAnExactBuffer()
        {
            int count = ReadResultBuffer.MaxPooledCapacity + 1;
            ReadResultBuffer buffer = ReadResultBuffer.Rent(count);
            buffer.ValueList[count - 1] = new DataValue(new Variant(9));
            DataValue[] values = buffer.Values;

            Assert.That(values, Has.Length.EqualTo(count));
            Assert.That(buffer.Results.Count, Is.EqualTo(count));

            buffer.Return();
            Assert.That(values[count - 1].IsNull, Is.True);
            Assert.That(() => ReadResultBuffer.Rent(0), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        private static byte[] Encode(ReadResponse response)
        {
            ServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            using var encoder = new BinaryEncoder(context);
            response.Encode(encoder);
            return encoder.CloseAndReturnBuffer()!;
        }

        private static ReadResponse Decode(byte[] encoded)
        {
            ServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            using var decoder = new BinaryDecoder(encoded, context);
            var decoded = new ReadResponse();
            decoded.Decode(decoder);
            return decoded;
        }
    }
}
