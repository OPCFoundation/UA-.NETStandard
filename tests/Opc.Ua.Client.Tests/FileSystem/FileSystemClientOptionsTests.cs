// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
