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
