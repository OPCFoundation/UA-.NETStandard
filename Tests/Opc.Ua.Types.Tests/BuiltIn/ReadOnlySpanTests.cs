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

using System.Linq;
using System;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Tests for the BuiltIn Types.
    /// </summary>
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ReadOnlySpanTests
    {
        [Test]
        public void ComputeHash32WithDefaultSeedReturnsExpectedHash()
        {
            byte[] data = [1, 2, 3, 4, 5, 6, 7, 8];
            int hash = ReadOnlySpan.ComputeHash32(data);
            int expected = ReadOnlySpan.ComputeHash32(data, ReadOnlySpan.DefaultSeed);
            Assert.That(hash, Is.EqualTo(expected)); // Expected hash value
        }

        [Test]
        public void ComputeHash32WithCustomSeedReturnsExpectedHash()
        {
            byte[] data = [1, 2, 3, 4, 5, 6, 7, 8];
            const ulong seed = 1234567890UL;
            int hash = ReadOnlySpan.ComputeHash32(data, seed);
            Assert.That(hash, Is.EqualTo(692292723)); // Expected hash value
        }

        [Test]
        public void ComputeHash32SmallInputReturnsExpectedHash()
        {
            byte[] data = [1, 2, 3];
            int hash = ReadOnlySpan.ComputeHash32(data);
            int expected = ReadOnlySpan.ComputeHash32(data, ReadOnlySpan.DefaultSeed);
            Assert.That(hash, Is.EqualTo(expected)); // Expected hash value
        }

        [Test]
        public void ComputeHash32EmptyInputReturnsExpectedHash()
        {
            ReadOnlySpan<byte> data = ReadOnlySpan<byte>.Empty;
            int hash = ReadOnlySpan.ComputeHash32(data);
            int expected = ReadOnlySpan.ComputeHash32(data, ReadOnlySpan.DefaultSeed);
            Assert.That(hash, Is.EqualTo(expected)); // Expected hash value
        }

        [Theory]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(14)]
        [TestCase(32)]
        [TestCase(33)]
        [TestCase(34)]
        [TestCase(63)]
        [TestCase(64)]
        [TestCase(65)]
        [TestCase(66)]
        [TestCase(127)]
        [TestCase(257)]
        [TestCase(1023)]
        [TestCase(2050)]
        public void ComputeHash32WithVariousInputsLittleEndianReturnsExpectedHash(int length)
        {
            byte[] data = Enumerable.Range(0, length).Select(r => (byte)r).ToArray();
            int hash = ReadOnlySpan.ComputeHash32(data, ReadOnlySpan.DefaultSeed, true);
            int expected = ReadOnlySpan.ComputeHash32(data, ReadOnlySpan.DefaultSeed, true);
            Assert.That(hash, Is.EqualTo(expected)); // Expected hash value
        }

        [Theory]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(14)]
        [TestCase(32)]
        [TestCase(33)]
        [TestCase(34)]
        [TestCase(63)]
        [TestCase(64)]
        [TestCase(65)]
        [TestCase(66)]
        [TestCase(127)]
        [TestCase(257)]
        [TestCase(1023)]
        [TestCase(2050)]
        public void ComputeHash32WithVariousInputsBigEndianReturnsExpectedHash(int length)
        {
            byte[] data = Enumerable.Range(0, length).Select(r => (byte)r).ToArray();
            int hash = ReadOnlySpan.ComputeHash32(data, ReadOnlySpan.DefaultSeed, false);
            int expected = ReadOnlySpan.ComputeHash32(data, ReadOnlySpan.DefaultSeed, false);
            Assert.That(hash, Is.EqualTo(expected)); // Expected hash value
        }
    }
}
