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
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.Utils
{
    /// <summary>
    /// Exercises the same framework convenience APIs on modern and polyfilled targets.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class FrameworkConvenienceApiTests
    {
        /// <summary>
        /// Distinguishes fully qualified paths from drive-relative and root-relative paths.
        /// </summary>
        [TestCase("", false, false)]
        [TestCase(".", false, false)]
        [TestCase("pki", false, false)]
        [TestCase(@"C:", false, false)]
        [TestCase(@"C:pki", false, false)]
        [TestCase(@"\pki", false, false)]
        [TestCase(@"C:\pki", true, false)]
        [TestCase(@"z:\pki", true, false)]
        [TestCase("C:/pki", true, false)]
        [TestCase(@"1:\pki", false, false)]
        [TestCase(@"\\server\share\pki", true, false)]
        [TestCase(@"\\?\C:\pki", true, false)]
        [TestCase(@"C:\pki\..\other", true, false)]
        [TestCase("/", false, true)]
        [TestCase("/pki", false, true)]
        [TestCase("//server/share/pki", true, true)]
        public void FullyQualifiedPathMatchesPlatformSemantics(
            string path,
            bool expectedWindows,
            bool expectedUnix)
        {
            bool expected = Path.DirectorySeparatorChar == '\\' ? expectedWindows : expectedUnix;

            Assert.That(Path.IsPathFullyQualified(path), Is.EqualTo(expected));
        }

        /// <summary>
        /// Rejects a null path with the same argument identity on every target.
        /// </summary>
        [Test]
        public void FullyQualifiedPathRejectsNull()
        {
            Assert.That(
                () => Path.IsPathFullyQualified(null!),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("path"));
        }

        /// <summary>
        /// Allocates exactly the requested number of random bytes, including an empty request.
        /// </summary>
        [TestCase(0)]
        [TestCase(4)]
        [TestCase(32)]
        public void RandomBytesHaveTheRequestedLength(int count)
        {
            Assert.That(RandomNumberGenerator.GetBytes(count), Has.Length.EqualTo(count));
        }

        /// <summary>
        /// Rejects a negative count before allocating.
        /// </summary>
        [Test]
        public void RandomBytesRejectNegativeCount()
        {
            Assert.That(
                () => RandomNumberGenerator.GetBytes(-1),
                Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("count"));
        }

        /// <summary>
        /// Keeps generated buffers independently owned by their callers.
        /// </summary>
        [Test]
        public void RandomByteBuffersDoNotShareMutableStorage()
        {
            byte[] first = RandomNumberGenerator.GetBytes(32);
            byte[] second = RandomNumberGenerator.GetBytes(32);
            byte[] snapshot = (byte[])second.Clone();
            first[0] ^= 1;

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.Not.SameAs(second));
                Assert.That(second, Is.EqualTo(snapshot));
            });
        }
    }
}
