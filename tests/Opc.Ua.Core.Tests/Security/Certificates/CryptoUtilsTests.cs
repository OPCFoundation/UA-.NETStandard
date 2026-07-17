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
