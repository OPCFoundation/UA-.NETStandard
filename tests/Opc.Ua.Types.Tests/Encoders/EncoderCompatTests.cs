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

namespace Opc.Ua.Types.Tests.Encoders
{
    /// <summary>
    /// Tests for the <see cref="EncoderCompat"/> bit-reinterpretation helpers used by the
    /// experimental Avro encoder across all target frameworks.
    /// </summary>
    /// <remarks>
    /// The reference values use <see cref="BitConverter.GetBytes(float)"/> /
    /// <see cref="BitConverter.ToInt32(byte[], int)"/> (available on every target framework)
    /// rather than the net5+ <c>BitConverter.SingleToInt32Bits</c> that <see cref="EncoderCompat"/>
    /// exists to replace on the legacy frameworks.
    /// </remarks>
    [TestFixture]
    [Category("Encoder")]
    [Parallelizable]
    public class EncoderCompatTests
    {
        private static readonly float[] s_floats =
        [
            0.0f,
            -0.0f,
            1.0f,
            -1.0f,
            float.MinValue,
            float.MaxValue,
            float.Epsilon,
            float.PositiveInfinity,
            float.NegativeInfinity,
            float.NaN,
            3.1415927f
        ];

        private static readonly double[] s_doubles =
        [
            0.0,
            -0.0,
            1.0,
            -1.0,
            double.MinValue,
            double.MaxValue,
            double.Epsilon,
            double.PositiveInfinity,
            double.NegativeInfinity,
            double.NaN,
            2.718281828459045
        ];

        private static int ReferenceSingleBits(float value)
        {
            return BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
        }

        [Test]
        public void SingleInt32BitsRoundTripsPreservingBits([ValueSource(nameof(s_floats))] float value)
        {
            int bits = EncoderCompat.SingleToInt32Bits(value);

            Assert.That(bits, Is.EqualTo(ReferenceSingleBits(value)));

            float roundTripped = EncoderCompat.Int32BitsToSingle(bits);
            Assert.That(ReferenceSingleBits(roundTripped), Is.EqualTo(ReferenceSingleBits(value)));
        }

        [Test]
        public void DoubleInt64BitsRoundTripsPreservingBits([ValueSource(nameof(s_doubles))] double value)
        {
            long bits = EncoderCompat.DoubleToInt64Bits(value);

            // BitConverter.DoubleToInt64Bits is available on every target framework.
            Assert.That(bits, Is.EqualTo(BitConverter.DoubleToInt64Bits(value)));

            double roundTripped = EncoderCompat.Int64BitsToDouble(bits);
            Assert.That(
                BitConverter.DoubleToInt64Bits(roundTripped),
                Is.EqualTo(BitConverter.DoubleToInt64Bits(value)));
        }

        [Test]
        public void Int32BitsToSingle_KnownBitPattern_ReturnsExpectedValue()
        {
            // 0x40490FDB is the IEEE-754 single-precision encoding of pi.
            float value = EncoderCompat.Int32BitsToSingle(0x40490FDB);

            Assert.That(value, Is.EqualTo(3.1415927f).Within(1e-6f));
        }

        [Test]
        public void Int64BitsToDouble_KnownBitPattern_ReturnsExpectedValue()
        {
            // 0x400921FB54442D18 is the IEEE-754 double-precision encoding of pi.
            double value = EncoderCompat.Int64BitsToDouble(0x400921FB54442D18L);

            Assert.That(value, Is.EqualTo(Math.PI).Within(1e-12));
        }
    }
}
