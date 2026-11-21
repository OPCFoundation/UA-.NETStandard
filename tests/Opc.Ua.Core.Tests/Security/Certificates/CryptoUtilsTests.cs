// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Tests for the constant-time and zeroization helpers on <see cref="CryptoUtils"/>.
    /// </summary>
    [TestFixture]
    [Category("CryptoUtils")]
    [Parallelizable]
    [SetCulture("en-us")]
    public class CryptoUtilsTests
    {
        /// <summary>
        /// Verifies that ZeroMemory overwrites every byte of the buffer with zero.
        /// </summary>
        [Test]
        public void ZeroMemoryClearsBuffer()
        {
            byte[] buffer = [1, 2, 3, 4, 5, 0xff, 0x80];

            CryptoUtils.ZeroMemory(buffer);

            Assert.That(buffer, Is.All.EqualTo(0));
        }

        /// <summary>
        /// Verifies that ZeroMemory tolerates an empty buffer without throwing.
        /// </summary>
        [Test]
        public void ZeroMemoryEmptyBufferDoesNotThrow()
        {
            Assert.That(() => CryptoUtils.ZeroMemory([]), Throws.Nothing);
        }

        /// <summary>
        /// Verifies that equal-content, equal-length buffers compare as equal.
        /// </summary>
        [Test]
        public void FixedTimeEqualsReturnsTrueForEqualBuffers()
        {
            byte[] left = [1, 2, 3, 4, 5];
            byte[] right = [1, 2, 3, 4, 5];

            Assert.That(CryptoUtils.FixedTimeEquals(left, right), Is.True);
        }

        /// <summary>
        /// Verifies that same-length buffers with differing content compare as unequal.
        /// </summary>
        [Test]
        public void FixedTimeEqualsReturnsFalseForDifferentContent()
        {
            byte[] left = [1, 2, 3, 4, 5];
            byte[] right = [1, 2, 3, 4, 6];

            Assert.That(CryptoUtils.FixedTimeEquals(left, right), Is.False);
        }

        /// <summary>
        /// Verifies that buffers of different lengths compare as unequal.
        /// </summary>
        [Test]
        public void FixedTimeEqualsReturnsFalseForDifferentLength()
        {
            byte[] left = [1, 2, 3, 4, 5];
            byte[] right = [1, 2, 3, 4];

            Assert.That(CryptoUtils.FixedTimeEquals(left, right), Is.False);
        }

        /// <summary>
        /// Verifies that two empty buffers compare as equal.
        /// </summary>
        [Test]
        public void FixedTimeEqualsReturnsTrueForEmptyBuffers()
        {
            Assert.That(CryptoUtils.FixedTimeEquals([], []), Is.True);
        }
    }
}
