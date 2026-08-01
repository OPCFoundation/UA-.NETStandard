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

using NUnit.Framework;

namespace Opc.Ua.OpenUsd.Client.Tests
{
    /// <summary>
    /// Unit tests for <see cref="VariantConversions"/>, the widening accessors every
    /// OpenUSD binding conversion funnels through. They must accept each OPC UA numeric
    /// built-in without boxing and degrade to <c>false</c> — never throw — for a source
    /// whose built-in type does not widen.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class VariantConversionsTests
    {
        [Test]
        public void TryGetDoubleReadsADoubleSource()
        {
            Assert.That(VariantConversions.TryGetDouble(new Variant(1.5), out double result), Is.True);
            Assert.That(result, Is.EqualTo(1.5).Within(1e-12));
        }

        [Test]
        public void TryGetDoubleWidensAFloatSource()
        {
            Assert.That(VariantConversions.TryGetDouble(new Variant(2.5f), out double result), Is.True);
            Assert.That(result, Is.EqualTo(2.5).Within(1e-6));
        }

        [Test]
        public void TryGetDoubleWidensASignedIntegerSource()
        {
            Assert.That(VariantConversions.TryGetDouble(new Variant(-7), out double result), Is.True);
            Assert.That(result, Is.EqualTo(-7.0).Within(1e-12));
        }

        [Test]
        public void TryGetDoubleWidensAnUnsignedSixtyFourBitSource()
        {
            Assert.That(
                VariantConversions.TryGetDouble(new Variant(ulong.MaxValue), out double result),
                Is.True);
            Assert.That(result, Is.EqualTo((double)ulong.MaxValue).Within(1.0));
        }

        [Test]
        public void TryGetDoubleRejectsANonNumericSource()
        {
            Assert.That(VariantConversions.TryGetDouble(new Variant("41.0"), out double result), Is.False);
            Assert.That(result, Is.Zero);
        }

        [Test]
        public void TryGetDoubleRejectsANullSource()
        {
            Assert.That(VariantConversions.TryGetDouble(default, out double result), Is.False);
            Assert.That(result, Is.Zero);
        }

        [Test]
        public void TryGetInt64ReadsALongSource()
        {
            Assert.That(
                VariantConversions.TryGetInt64(new Variant(long.MinValue), out long result),
                Is.True);
            Assert.That(result, Is.EqualTo(long.MinValue));
        }

        [Test]
        public void TryGetInt64WidensAnIntSource()
        {
            Assert.That(VariantConversions.TryGetInt64(new Variant(-42), out long result), Is.True);
            Assert.That(result, Is.EqualTo(-42L));
        }

        [Test]
        public void TryGetInt64WidensAUIntSource()
        {
            Assert.That(
                VariantConversions.TryGetInt64(new Variant(uint.MaxValue), out long result),
                Is.True);
            Assert.That(result, Is.EqualTo(4294967295L));
        }

        [Test]
        public void TryGetInt64WidensAShortSource()
        {
            Assert.That(
                VariantConversions.TryGetInt64(new Variant((short)-300), out long result),
                Is.True);
            Assert.That(result, Is.EqualTo(-300L));
        }

        [Test]
        public void TryGetInt64WidensAUShortSource()
        {
            Assert.That(
                VariantConversions.TryGetInt64(new Variant((ushort)65535), out long result),
                Is.True);
            Assert.That(result, Is.EqualTo(65535L));
        }

        [Test]
        public void TryGetInt64WidensASByteSource()
        {
            Assert.That(
                VariantConversions.TryGetInt64(new Variant((sbyte)-128), out long result),
                Is.True);
            Assert.That(result, Is.EqualTo(-128L));
        }

        [Test]
        public void TryGetInt64WidensAByteSource()
        {
            Assert.That(
                VariantConversions.TryGetInt64(new Variant((byte)255), out long result),
                Is.True);
            Assert.That(result, Is.EqualTo(255L));
        }

        [TestCase(true, 1L)]
        [TestCase(false, 0L)]
        public void TryGetInt64MapsABooleanSourceToZeroOrOne(bool source, long expected)
        {
            Assert.That(VariantConversions.TryGetInt64(new Variant(source), out long result), Is.True);
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void TryGetInt64RejectsAUInt64SourceBecauseItDoesNotFit()
        {
            Assert.That(
                VariantConversions.TryGetInt64(new Variant(ulong.MaxValue), out long result),
                Is.False);
            Assert.That(result, Is.Zero);
        }

        [Test]
        public void TryGetInt64RejectsAFloatingPointSource()
        {
            Assert.That(VariantConversions.TryGetInt64(new Variant(1.5), out long result), Is.False);
            Assert.That(result, Is.Zero);
        }

        [Test]
        public void TryGetInt64RejectsANullSource()
        {
            Assert.That(VariantConversions.TryGetInt64(default, out long result), Is.False);
            Assert.That(result, Is.Zero);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void TryGetBooleanReadsABooleanSource(bool source)
        {
            Assert.That(VariantConversions.TryGetBoolean(new Variant(source), out bool result), Is.True);
            Assert.That(result, Is.EqualTo(source));
        }

        [Test]
        public void TryGetBooleanTreatsANonZeroNumericSourceAsTrue()
        {
            Assert.That(VariantConversions.TryGetBoolean(new Variant(-0.25), out bool result), Is.True);
            Assert.That(result, Is.True);
        }

        [Test]
        public void TryGetBooleanTreatsAZeroNumericSourceAsFalse()
        {
            Assert.That(VariantConversions.TryGetBoolean(new Variant(0), out bool result), Is.True);
            Assert.That(result, Is.False);
        }

        [Test]
        public void TryGetBooleanRejectsANonNumericSource()
        {
            Assert.That(
                VariantConversions.TryGetBoolean(new Variant("true"), out bool result),
                Is.False);
            Assert.That(result, Is.False);
        }

        [Test]
        public void TryGetBytesReadsAByteStringSource()
        {
            var source = new ByteString(new byte[] { 1, 2, 3 });

            Assert.That(
                VariantConversions.TryGetBytes(new Variant(source), out ByteString result),
                Is.True);
            Assert.That(result.Length, Is.EqualTo(3));
            Assert.That(result.Span[0], Is.EqualTo((byte)1));
            Assert.That(result.Span[2], Is.EqualTo((byte)3));
        }

        [Test]
        public void TryGetBytesCopiesAByteArraySource()
        {
            ArrayOf<byte> source = new byte[] { 9, 8, 7, 6 };

            Assert.That(
                VariantConversions.TryGetBytes(new Variant(source), out ByteString result),
                Is.True);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(result.Span[0], Is.EqualTo((byte)9));
            Assert.That(result.Span[3], Is.EqualTo((byte)6));
        }

        [Test]
        public void TryGetBytesRejectsANonBinarySource()
        {
            Assert.That(
                VariantConversions.TryGetBytes(new Variant(1.0), out ByteString result),
                Is.False);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void TryGetBytesRejectsANullSource()
        {
            Assert.That(VariantConversions.TryGetBytes(default, out ByteString result), Is.False);
            Assert.That(result.IsNull, Is.True);
        }
    }
}
