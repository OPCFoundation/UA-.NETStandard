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
using NUnit.Framework;
using Opc.Ua.Client.FileSystem;

namespace Opc.Ua.Client.Tests.FileSystem
{
    /// <summary>
    /// Unit tests for <see cref="FileSystemClientOptions"/>.
    /// </summary>
    [TestFixture]
    [Category("FileSystem")]
    [Parallelizable]
    public class FileSystemClientOptionsTests
    {
        [Test]
        public void DefaultsAreSensible()
        {
            var options = new FileSystemClientOptions();
            Assert.That(options.ChunkSize, Is.EqualTo(64 * 1024));
            Assert.That(options.MaxBufferedReadSize, Is.EqualTo(16L * 1024 * 1024));
            Assert.That(options.PathCacheSize, Is.EqualTo(1024));
            Assert.That(options.IncludeFileTypeSubtypes, Is.True);
            Assert.That(options.IncludeFileDirectoryTypeSubtypes, Is.True);
        }

        [Test]
        public void CloneProducesIndependentInstance()
        {
            var original = new FileSystemClientOptions
            {
                ChunkSize = 1234,
                MaxBufferedReadSize = 5678,
                PathCacheSize = 7,
                IncludeFileTypeSubtypes = false,
                IncludeFileDirectoryTypeSubtypes = false
            };

            FileSystemClientOptions clone = original.Clone();
            clone.ChunkSize = 9999;

            Assert.That(original.ChunkSize, Is.EqualTo(1234));
            Assert.That(clone.MaxBufferedReadSize, Is.EqualTo(5678));
            Assert.That(clone.PathCacheSize, Is.EqualTo(7));
            Assert.That(clone.IncludeFileTypeSubtypes, Is.False);
            Assert.That(clone.IncludeFileDirectoryTypeSubtypes, Is.False);
        }

        [Test]
        public void ValidateRejectsZeroChunkSize()
        {
            var options = new FileSystemClientOptions { ChunkSize = 0 };
            Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
        }

        [Test]
        public void ValidateRejectsNegativePathCacheSize()
        {
            var options = new FileSystemClientOptions { PathCacheSize = -1 };
            Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
        }

        [Test]
        public void ValidateRejectsZeroMaxBufferedReadSize()
        {
            var options = new FileSystemClientOptions { MaxBufferedReadSize = 0 };
            Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
        }

        [Test]
        public void ValidatePassesForDefaults()
        {
            new FileSystemClientOptions().Validate();
        }
    }
}
