/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Linq;
using NUnit.Framework;

#pragma warning disable IDE0301 // Simplify collection initialization

namespace Opc.Ua.Types.Buffers.Tests
{
    /// <summary>
    /// Tests for read only span extensions.
    /// </summary>
    [TestFixture]
    [Category("Buffers")]
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
            byte[] data = [.. Enumerable.Range(0, length).Select(r => (byte)r)];
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
            byte[] data = [.. Enumerable.Range(0, length).Select(r => (byte)r)];
            int hash = ReadOnlySpan.ComputeHash32(data, ReadOnlySpan.DefaultSeed, false);
            int expected = ReadOnlySpan.ComputeHash32(data, ReadOnlySpan.DefaultSeed, false);
            Assert.That(hash, Is.EqualTo(expected)); // Expected hash value
        }
    }
}
