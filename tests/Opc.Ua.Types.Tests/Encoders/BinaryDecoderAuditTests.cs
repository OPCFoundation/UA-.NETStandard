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

namespace Opc.Ua.Types.Tests.Encoders
{
    /// <summary>
    /// Regression tests for the OPC UA Binary decoding defects reported by
    /// the codec audit (B1, T1-1, E1-8).
    /// </summary>
    [TestFixture]
    [Category("Encoders")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class BinaryDecoderAuditTests
    {
        private static readonly int[] s_oneElement = [1];
        private static readonly bool[] s_streamModes = [false, true];
        private static readonly double[] s_twoDoubles = [1.5, 2.5];

        private static ServiceMessageContext CreateContext()
        {
            return ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
        }

        private static byte[] Build(Action<BinaryWriter> write)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream))
            {
                write(writer);
            }
            return stream.ToArray();
        }

        private static void AssertStatus(Action code, StatusCode statusCode)
        {
            ServiceResultException ex = Assert.Throws<ServiceResultException>(code);
            Assert.That(ex.StatusCode, Is.EqualTo(statusCode));
        }

        private static BinaryDecoder CreateDecoder(
            byte[] bytes,
            IServiceMessageContext context,
            bool useStream)
        {
            return useStream
                ? new BinaryDecoder(new MemoryStream(bytes, false), context)
                : new BinaryDecoder(bytes, context);
        }

        [Test]
        [TestCase(0x98, false)]
        [TestCase(0x98, true)]
        [TestCase(0x97, false)]
        [TestCase(0x97, true)]
        [TestCase(0x96, false)]
        [TestCase(0x8C, true)]
        [TestCase(0x91, false)]
        [TestCase(0x8B, false)]
        [TestCase(0x8B, true)]
        [TestCase(0x81, true)]
        public void ReadArrayRejectsLengthBeyondRemainingBytes(int encodingByte, bool useStream)
        {
            // A Variant array of 65535 (MaxArrayLength) elements followed by
            // nothing: the length was only checked against MaxArrayLength and
            // the element storage allocated before a single element was read.
            byte[] bytes = Build(w =>
            {
                w.Write((byte)encodingByte);
                w.Write(65535);
            });

            using BinaryDecoder decoder = CreateDecoder(bytes, CreateContext(), useStream);
            AssertStatus(() => decoder.ReadVariant(null), StatusCodes.BadDecodingError);
        }

        [Test]
        public void ReadArrayAcceptsLengthThatExactlyFitsTheRemainingBytes()
        {
            byte[] bytes = Build(w =>
            {
                w.Write((byte)0x8B);
                w.Write(2);
                w.Write(1.5);
                w.Write(2.5);
            });

            using var decoder = new BinaryDecoder(bytes, CreateContext());
            Variant value = decoder.ReadVariant(null);
            Assert.That(value.GetDoubleArray().ToArray(), Is.EqualTo(s_twoDoubles));
        }

        [Test]
        public void NestedDataValueArraysAreRejectedBeforeAllocating()
        {
            // 200 levels of Variant(DataValue[65535]) whose first element is
            // the next level: ~1.2 KB that used to allocate ~700 MB.
            byte[] bytes = Build(w =>
            {
                for (int ii = 0; ii < 200; ii++)
                {
                    w.Write((byte)0x97);
                    w.Write(65535);
                    w.Write((byte)0x01);
                }
            });

            using var decoder = new BinaryDecoder(
                new MemoryStream(bytes, false),
                CreateContext());
            AssertStatus(() => decoder.ReadVariant(null), StatusCodes.BadDecodingError);
        }

#if NET
        [Test]
        public void NestedDataValueArraysWithPaddingAllocateProportionalToDecodedElements()
        {
            // With 1 MB of padding every level passes the remaining bytes
            // check, the allocation must still not scale with the lengths.
            const int levels = 100;
            byte[] bytes = Build(w =>
            {
                for (int ii = 0; ii < levels; ii++)
                {
                    w.Write((byte)0x97);
                    w.Write(65535);
                    w.Write((byte)0x01);
                }
                // The innermost value, then padding that the second element
                // of the innermost array fails on (invalid Variant type).
                w.Write((byte)0x01);
                w.Write((byte)0x01);
                byte[] padding = new byte[1024 * 1024];
                padding.AsSpan().Fill(0xFF);
                w.Write(padding);
            });

            using var decoder = new BinaryDecoder(bytes, CreateContext());
            long before = GC.GetAllocatedBytesForCurrentThread();
            Assert.Throws<ServiceResultException>(() => decoder.ReadVariant(null));
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            // 100 levels x 65535 DataValues used to be several hundred MB.
            Assert.That(allocated, Is.LessThan(32L * 1024 * 1024));
        }
#endif

        [Test]
        public void LargeArraysRoundTripThroughGrowingStorage()
        {
            IServiceMessageContext context = CreateContext();
            var variants = new Variant[5000];
            var values = new DataValue[5000];
            string[] strings = new string[5000];
            for (int ii = 0; ii < variants.Length; ii++)
            {
                variants[ii] = Variant.From(ii);
                values[ii] = new DataValue(Variant.From((double)ii));
                strings[ii] = ii.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            byte[] bytes;
            using (var encoder = new BinaryEncoder(context))
            {
                encoder.WriteVariantArray(null, variants);
                encoder.WriteDataValueArray(null, values);
                encoder.WriteStringArray(null, strings);
                bytes = encoder.CloseAndReturnBuffer()!;
            }

            foreach (bool useStream in s_streamModes)
            {
                using BinaryDecoder decoder = CreateDecoder(bytes, context, useStream);
                Assert.That(decoder.ReadVariantArray(null).ToArray(), Is.EqualTo(variants));
                Assert.That(decoder.ReadDataValueArray(null).ToArray(), Is.EqualTo(values));
                Assert.That(decoder.ReadStringArray(null).ToArray(), Is.EqualTo(strings));
            }
        }

        [Test]
        [TestCase(false)]
        [TestCase(true)]
        public void ByteStringLengthBeyondRemainingBytesIsRejectedWithoutLimits(bool useStream)
        {
            // With MaxByteStringLength = 0 (unlimited) a 2 GB length from 4
            // bytes was allocated by BinaryReader.ReadBytes on the stream path.
            ServiceMessageContext context = CreateContext();
            context.MaxByteStringLength = 0;
            byte[] bytes = Build(w => w.Write(int.MaxValue));

            using BinaryDecoder decoder = CreateDecoder(bytes, context, useStream);
            AssertStatus(() => decoder.ReadByteString(null), StatusCodes.BadDecodingError);
        }

        [Test]
        [TestCase(false)]
        [TestCase(true)]
        public void StringLengthBeyondRemainingBytesIsRejectedWithoutLimits(bool useStream)
        {
            ServiceMessageContext context = CreateContext();
            context.MaxStringLength = 0;
            byte[] bytes = Build(w => w.Write(int.MaxValue));

            using BinaryDecoder decoder = CreateDecoder(bytes, context, useStream);
            AssertStatus(() => decoder.ReadString(null), StatusCodes.BadDecodingError);
        }

        [Test]
        public void VariantMatrixWithMoreThan32DimensionsIsRejected()
        {
            // Int32 | Array | ArrayDimensions, one value, 33 dimensions of 1:
            // the product matches the element count, but no .NET array (and
            // hence no consumer) can have a rank above 32.
            byte[] bytes = Build(w =>
            {
                w.Write((byte)0xC6);
                w.Write(1);
                w.Write(0);
                w.Write(33);
                for (int ii = 0; ii < 33; ii++)
                {
                    w.Write(1);
                }
            });

            using var decoder = new BinaryDecoder(bytes, CreateContext());
            AssertStatus(() => decoder.ReadVariant(null), StatusCodes.BadDecodingError);
        }

        [Test]
        public void VariantMatrixWithRankAboveShortMaxValueIsRejectedAsDecodingError()
        {
            // A rank above short.MaxValue made TypeInfo throw an
            // ArgumentOutOfRangeException out of the decoder.
            const int rank = 40000;
            byte[] bytes = Build(w =>
            {
                w.Write((byte)0xC1);
                w.Write(1);
                w.Write((byte)1);
                w.Write(rank);
                for (int ii = 0; ii < rank; ii++)
                {
                    w.Write(1);
                }
            });

            using var decoder = new BinaryDecoder(bytes, CreateContext());
            AssertStatus(() => decoder.ReadVariant(null), StatusCodes.BadDecodingError);
        }

        [Test]
        public void VariantMatrixWith32DimensionsIsAccepted()
        {
            byte[] bytes = Build(w =>
            {
                w.Write((byte)0xC6);
                w.Write(1);
                w.Write(7);
                w.Write(32);
                for (int ii = 0; ii < 32; ii++)
                {
                    w.Write(1);
                }
            });

            using var decoder = new BinaryDecoder(bytes, CreateContext());
            Variant value = decoder.ReadVariant(null);
            Assert.That(value.TypeInfo.ValueRank, Is.EqualTo(32));
        }

        [Test]
        public void EmptyInlineMatrixWithMoreThan32DimensionsIsRejected()
        {
            // An empty inline matrix (a zero dimension, no values) was
            // accepted with any rank.
            byte[] bytes = Build(w =>
            {
                w.Write(33);
                for (int ii = 0; ii < 33; ii++)
                {
                    w.Write(0);
                }
            });

            using var decoder = new BinaryDecoder(bytes, CreateContext());
            AssertStatus(
                () => decoder.ReadVariantValue(
                    null,
                    TypeInfo.Create(BuiltInType.Int32, ValueRanks.TwoDimensions)),
                StatusCodes.BadDecodingError);
        }

        [Test]
        public void MatrixValidatorsRejectMoreThan32Dimensions()
        {
            int[] dimensions = new int[33];
            dimensions.AsSpan().Fill(1);

            Assert.Multiple(() =>
            {
                Assert.That(MatrixOf.IsValidMatrix(dimensions, 1), Is.False);
                Assert.That(
                    MatrixOf.TryGetInlineMatrixElementCount(dimensions, out _, out _),
                    Is.False);
                Assert.That(
                    MatrixOf.IsValidMatrix(dimensions.AsSpan(0, 32), 1),
                    Is.True);
                Assert.Throws<ArgumentException>(
                    () => _ = new MatrixOf<int>(s_oneElement, dimensions));
            });
        }
    }
}
