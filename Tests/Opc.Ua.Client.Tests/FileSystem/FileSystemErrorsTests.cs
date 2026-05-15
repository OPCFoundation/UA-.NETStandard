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
using Opc.Ua.Client.FileSystem;

namespace Opc.Ua.Client.Tests.FileSystem
{
    /// <summary>
    /// Unit tests for the internal <c>FileSystemErrors</c> mapper.
    /// </summary>
    [TestFixture]
    [Category("FileSystem")]
    [Parallelizable]
    public class FileSystemErrorsTests
    {
        [Test]
        public void BadNoMatchOnDirectoryReturnsDirectoryNotFoundException()
        {
            var ex = new ServiceResultException(StatusCodes.BadNoMatch);
            Exception mapped = FileSystemErrors.Translate(ex, "/foo/bar", targetIsDirectory: true);
            Assert.That(mapped, Is.InstanceOf<DirectoryNotFoundException>());
            Assert.That(mapped.Message, Does.Contain("/foo/bar"));
            Assert.That(mapped.InnerException, Is.SameAs(ex));
        }

        [Test]
        public void BadNoMatchOnFileReturnsFileNotFoundException()
        {
            var ex = new ServiceResultException(StatusCodes.BadNoMatch);
            Exception mapped = FileSystemErrors.Translate(ex, "/foo/bar.txt", targetIsDirectory: false);
            Assert.That(mapped, Is.InstanceOf<FileNotFoundException>());
            Assert.That(((FileNotFoundException)mapped).FileName, Is.EqualTo("/foo/bar.txt"));
        }

        [Test]
        public void BadNotFoundMapsToNotFound()
        {
            var ex = new ServiceResultException(StatusCodes.BadNotFound);
            Exception mapped = FileSystemErrors.Translate(ex, "/x", targetIsDirectory: false);
            Assert.That(mapped, Is.InstanceOf<FileNotFoundException>());
        }

        [Test]
        public void BadNodeIdUnknownMapsToNotFound()
        {
            var ex = new ServiceResultException(StatusCodes.BadNodeIdUnknown);
            Exception mapped = FileSystemErrors.Translate(ex, "/x", targetIsDirectory: true);
            Assert.That(mapped, Is.InstanceOf<DirectoryNotFoundException>());
        }

        [Test]
        public void BadBrowseNameDuplicatedMapsToIOException()
        {
            var ex = new ServiceResultException(StatusCodes.BadBrowseNameDuplicated);
            Exception mapped = FileSystemErrors.Translate(ex, "/foo", targetIsDirectory: false);
            Assert.That(mapped, Is.InstanceOf<IOException>());
            Assert.That(mapped.Message, Does.Contain("already exists"));
        }

        [Test]
        public void BadUserAccessDeniedMapsToUnauthorizedAccessException()
        {
            var ex = new ServiceResultException(StatusCodes.BadUserAccessDenied);
            Exception mapped = FileSystemErrors.Translate(ex, "/foo", targetIsDirectory: false);
            Assert.That(mapped, Is.InstanceOf<UnauthorizedAccessException>());
            Assert.That(mapped.Message, Does.Contain("access denied"));
        }

        [Test]
        public void BadNotWritableMapsToUnauthorizedAccessException()
        {
            var ex = new ServiceResultException(StatusCodes.BadNotWritable);
            Exception mapped = FileSystemErrors.Translate(ex, "/foo", targetIsDirectory: false);
            Assert.That(mapped, Is.InstanceOf<UnauthorizedAccessException>());
        }

        [Test]
        public void BadResourceUnavailableMapsToIOExceptionNotUnauthorized()
        {
            var ex = new ServiceResultException(StatusCodes.BadResourceUnavailable);
            Exception mapped = FileSystemErrors.Translate(ex, "/foo", targetIsDirectory: false);
            Assert.That(mapped, Is.InstanceOf<IOException>());
            Assert.That(mapped, Is.Not.InstanceOf<UnauthorizedAccessException>());
        }

        [Test]
        public void BadInvalidStateMapsToIOException()
        {
            var ex = new ServiceResultException(StatusCodes.BadInvalidState);
            Exception mapped = FileSystemErrors.Translate(ex, "/foo", targetIsDirectory: false);
            Assert.That(mapped, Is.InstanceOf<IOException>());
        }

        [Test]
        public void UnmappedStatusCodePassesThroughOriginalException()
        {
            var ex = new ServiceResultException(StatusCodes.BadInternalError);
            Exception mapped = FileSystemErrors.Translate(ex, "/foo", targetIsDirectory: false);
            Assert.That(mapped, Is.SameAs(ex));
        }

        [Test]
        public void NotFoundFactoryReturnsCorrectExceptionType()
        {
            Exception fileException = FileSystemErrors.NotFound("/x.txt", targetIsDirectory: false);
            Exception dirException = FileSystemErrors.NotFound("/x", targetIsDirectory: true);
            Assert.That(fileException, Is.InstanceOf<FileNotFoundException>());
            Assert.That(((FileNotFoundException)fileException).FileName, Is.EqualTo("/x.txt"));
            Assert.That(dirException, Is.InstanceOf<DirectoryNotFoundException>());
        }

        [Test]
        public void AmbiguousFactoryReturnsIOExceptionWithCount()
        {
            IOException ex = FileSystemErrors.Ambiguous("/foo", 3);
            Assert.That(ex.Message, Does.Contain("ambiguous"));
            Assert.That(ex.Message, Does.Contain("3"));
            Assert.That(ex.Message, Does.Contain("/foo"));
        }

        [Test]
        public void TranslateNullExceptionThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => FileSystemErrors.Translate(null!, "/x", false));
        }
    }
}
