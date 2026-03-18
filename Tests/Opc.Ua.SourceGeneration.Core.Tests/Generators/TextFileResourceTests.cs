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

using Moq;
using NUnit.Framework;
using System;
using System.IO;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Unit tests for the TextFileResource class.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class TextFileResourceTests
    {
        /// <summary>
        /// Tests that GetLength returns the expected file length from the file system.
        /// </summary>
        /// <param name = "fileName">The file name to test with.</param>
        /// <param name = "expectedLength">The expected length value that the file system should return.</param>
        [TestCase("test.txt", 100L)]
        [TestCase("file.xml", 0L)]
        [TestCase("large.bin", long.MaxValue)]
        [TestCase("C:\\path\\to\\file.txt", 12345L)]
        [TestCase("../relative/path.txt", 999L)]
        [TestCase("file-with-special-chars_@#$.txt", 42L)]
        public void GetLength_ValidFileSystemAndFileName_ReturnsExpectedLength(string fileName, long expectedLength)
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.GetLength(fileName)).Returns(expectedLength);
            var resource = new TextFileResource("TestResource", fileName);
            // Act
            long actualLength = resource.GetLength(mockFileSystem.Object);
            // Assert
            Assert.That(actualLength, Is.EqualTo(expectedLength));
            mockFileSystem.Verify(fs => fs.GetLength(fileName), Times.Once);
        }

        /// <summary>
        /// Tests that GetLength throws NullReferenceException when fileSystem parameter is null.
        /// </summary>
        [Test]
        public void GetLength_NullFileSystem_ThrowsNullReferenceException()
        {
            // Arrange
            var resource = new TextFileResource("TestResource", "test.txt");
            // Act & Assert
            Assert.Throws<NullReferenceException>(() => resource.GetLength(null));
        }

        /// <summary>
        /// Tests that GetLength handles empty file name string.
        /// </summary>
        [Test]
        public void GetLength_EmptyFileName_CallsFileSystemWithEmptyString()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.GetLength(string.Empty)).Returns(0L);
            var resource = new TextFileResource("TestResource", string.Empty);
            // Act
            long actualLength = resource.GetLength(mockFileSystem.Object);
            // Assert
            Assert.That(actualLength, Is.EqualTo(0L));
            mockFileSystem.Verify(fs => fs.GetLength(string.Empty), Times.Once);
        }

        /// <summary>
        /// Tests that GetLength returns zero for zero-length files.
        /// </summary>
        [Test]
        public void GetLength_ZeroLengthFile_ReturnsZero()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.GetLength("empty.txt")).Returns(0L);
            var resource = new TextFileResource("TestResource", "empty.txt");
            // Act
            long actualLength = resource.GetLength(mockFileSystem.Object);
            // Assert
            Assert.That(actualLength, Is.EqualTo(0L));
        }

        /// <summary>
        /// Tests that GetLength correctly handles maximum long value for very large files.
        /// </summary>
        [Test]
        public void GetLength_MaximumFileSize_ReturnsMaxValue()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.GetLength("huge.bin")).Returns(long.MaxValue);
            var resource = new TextFileResource("TestResource", "huge.bin");
            // Act
            long actualLength = resource.GetLength(mockFileSystem.Object);
            // Assert
            Assert.That(actualLength, Is.EqualTo(long.MaxValue));
        }

        /// <summary>
        /// Tests that GetLength handles negative return values from the file system (edge case).
        /// </summary>
        [Test]
        public void GetLength_NegativeLength_ReturnsNegativeValue()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.GetLength("invalid.txt")).Returns(-1L);
            var resource = new TextFileResource("TestResource", "invalid.txt");
            // Act
            long actualLength = resource.GetLength(mockFileSystem.Object);
            // Assert
            Assert.That(actualLength, Is.EqualTo(-1L));
        }

        /// <summary>
        /// Tests that GetLength correctly passes the FileName property to the file system.
        /// </summary>
        [Test]
        public void GetLength_DifferentFileNames_PassesCorrectFileNameToFileSystem()
        {
            // Arrange
            const string expectedFileName = "specific-file.txt";
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.GetLength(expectedFileName)).Returns(500L);
            var resource = new TextFileResource("TestResource", expectedFileName);
            // Act
            resource.GetLength(mockFileSystem.Object);
            // Assert
            mockFileSystem.Verify(fs => fs.GetLength(expectedFileName), Times.Once);
        }

        /// <summary>
        /// Tests that GetLength returns the correct file length from the file system
        /// when provided with a valid file name and file system.
        /// </summary>
        [TestCase(0L)]
        [TestCase(1L)]
        [TestCase(100L)]
        [TestCase(1024L)]
        [TestCase(1048576L)]
        [TestCase(long.MaxValue)]
        public void GetLength_ValidFileSystem_ReturnsExpectedLength(long expectedLength)
        {
            // Arrange
            const string resourceName = "Test.Resource";
            const string fileName = "test.txt";
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.GetLength(fileName)).Returns(expectedLength);
            var resource = new TextFileResource(resourceName, fileName);
            // Act
            long actualLength = resource.GetLength(mockFileSystem.Object);
            // Assert
            Assert.That(actualLength, Is.EqualTo(expectedLength));
            mockFileSystem.Verify(fs => fs.GetLength(fileName), Times.Once);
        }

        /// <summary>
        /// Tests that GetLength passes the correct FileName to the file system's GetLength method.
        /// </summary>
        [TestCase("simple.txt")]
        [TestCase("path/to/file.txt")]
        [TestCase("C:\\absolute\\path\\file.txt")]
        [TestCase("")]
        [TestCase("file with spaces.txt")]
        [TestCase("file_with_special_chars!@#.txt")]
        public void GetLength_VariousFileNames_PassesCorrectFileNameToFileSystem(string fileName)
        {
            // Arrange
            const string resourceName = "Test.Resource";
            const long expectedLength = 42L;
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.GetLength(fileName)).Returns(expectedLength);
            var resource = new TextFileResource(resourceName, fileName);
            // Act
            long actualLength = resource.GetLength(mockFileSystem.Object);
            // Assert
            Assert.That(actualLength, Is.EqualTo(expectedLength));
            mockFileSystem.Verify(fs => fs.GetLength(fileName), Times.Once);
        }

        /// <summary>
        /// Tests that GetLength propagates exceptions thrown by the file system's GetLength method.
        /// </summary>
        [Test]
        public void GetLength_FileSystemThrowsIOException_PropagatesException()
        {
            // Arrange
            const string resourceName = "Test.Resource";
            const string fileName = "nonexistent.txt";
            var mockFileSystem = new Mock<IFileSystem>();
            var expectedException = new IOException("File not found");
            mockFileSystem.Setup(fs => fs.GetLength(fileName)).Throws(expectedException);
            var resource = new TextFileResource(resourceName, fileName);
            // Act & Assert
            IOException thrownException = Assert.Throws<IOException>(() => resource.GetLength(mockFileSystem.Object));
            Assert.That(thrownException.Message, Is.EqualTo("File not found"));
        }

        /// <summary>
        /// Tests that GetLength propagates ArgumentException when the file system throws it.
        /// </summary>
        [Test]
        public void GetLength_FileSystemThrowsArgumentException_PropagatesException()
        {
            // Arrange
            const string resourceName = "Test.Resource";
            const string fileName = "invalid:filename.txt";
            var mockFileSystem = new Mock<IFileSystem>();
            var expectedException = new ArgumentException("Invalid file name");
            mockFileSystem.Setup(fs => fs.GetLength(fileName)).Throws(expectedException);
            var resource = new TextFileResource(resourceName, fileName);
            // Act & Assert
            ArgumentException thrownException = Assert.Throws<ArgumentException>(() => resource.GetLength(mockFileSystem.Object));
            Assert.That(thrownException.Message, Is.EqualTo("Invalid file name"));
        }

        /// <summary>
        /// Tests that GetLength returns zero when the file system reports a zero-length file.
        /// </summary>
        [Test]
        public void GetLength_EmptyFile_ReturnsZero()
        {
            // Arrange
            const string resourceName = "Test.Resource";
            const string fileName = "empty.txt";
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.GetLength(fileName)).Returns(0L);
            var resource = new TextFileResource(resourceName, fileName);
            // Act
            long actualLength = resource.GetLength(mockFileSystem.Object);
            // Assert
            Assert.That(actualLength, Is.EqualTo(0L));
        }
    }
}
