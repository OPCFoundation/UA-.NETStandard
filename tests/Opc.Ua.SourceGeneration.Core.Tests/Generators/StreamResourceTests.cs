/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.IO;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Unit tests for <see cref = "StreamResource"/>.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class StreamResourceTests
    {
        /// <summary>
        /// Tests that GetLength returns the correct length for a stream with data.
        /// </summary>
        [TestCase(0L)]
        [TestCase(1L)]
        [TestCase(100L)]
        [TestCase(1024L)]
        [TestCase(int.MaxValue)]
        public void GetLength_StreamWithKnownLength_ReturnsStreamLength(long expectedLength)
        {
            // Arrange
            var mockStream = new Mock<Stream>();
            mockStream.Setup(s => s.Length).Returns(expectedLength);
            var resource = new StreamResource("TestResource", mockStream.Object, false);
            var mockFileSystem = new Mock<IFileSystem>();
            // Act
            long actualLength = resource.GetLength(mockFileSystem.Object);
            // Assert
            Assert.That(actualLength, Is.EqualTo(expectedLength));
        }

        /// <summary>
        /// Tests that GetLength returns the correct length when fileSystem parameter is null.
        /// The fileSystem parameter is not used in the implementation, so null should work.
        /// </summary>
        [Test]
        public void GetLength_FileSystemIsNull_ReturnsStreamLength()
        {
            // Arrange
            var mockStream = new Mock<Stream>();
            mockStream.Setup(s => s.Length).Returns(42L);
            var resource = new StreamResource("TestResource", mockStream.Object, false);
            // Act
            long actualLength = resource.GetLength(null!);
            // Assert
            Assert.That(actualLength, Is.EqualTo(42L));
        }

        /// <summary>
        /// Tests that GetLength throws ObjectDisposedException when the stream is disposed.
        /// </summary>
        [Test]
        public void GetLength_DisposedStream_ThrowsObjectDisposedException()
        {
            // Arrange
            var stream = new MemoryStream();
            stream.Dispose();
            var resource = new StreamResource("TestResource", stream, false);
            var mockFileSystem = new Mock<IFileSystem>();
            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => resource.GetLength(mockFileSystem.Object));
        }

        /// <summary>
        /// Tests that GetLength throws NotSupportedException when the stream does not support seeking.
        /// Non-seekable streams throw NotSupportedException when accessing the Length property.
        /// </summary>
        [Test]
        public void GetLength_NonSeekableStream_ThrowsNotSupportedException()
        {
            // Arrange
            var mockStream = new Mock<Stream>();
            mockStream.Setup(s => s.Length).Throws<NotSupportedException>();
            var resource = new StreamResource("TestResource", mockStream.Object, false);
            var mockFileSystem = new Mock<IFileSystem>();
            // Act & Assert
            Assert.Throws<NotSupportedException>(() => resource.GetLength(mockFileSystem.Object));
        }

        /// <summary>
        /// Tests that GetLength throws NullReferenceException when the Stream property is null.
        /// This tests an edge case where the record is constructed with a null stream.
        /// </summary>
        [Test]
        public void GetLength_NullStream_ThrowsNullReferenceException()
        {
            // Arrange
            var resource = new StreamResource("TestResource", null!, false);
            var mockFileSystem = new Mock<IFileSystem>();
            // Act & Assert
            Assert.Throws<NullReferenceException>(() => resource.GetLength(mockFileSystem.Object));
        }

        /// <summary>
        /// Tests that GetLength returns zero for an empty stream.
        /// </summary>
        [Test]
        public void GetLength_EmptyStream_ReturnsZero()
        {
            // Arrange
            using var stream = new MemoryStream();
            var resource = new StreamResource("TestResource", stream, false);
            var mockFileSystem = new Mock<IFileSystem>();
            // Act
            long actualLength = resource.GetLength(mockFileSystem.Object);
            // Assert
            Assert.That(actualLength, Is.Zero);
        }

        /// <summary>
        /// Tests that GetLength returns the correct length regardless of stream position.
        /// The Length property should return the total length, not the remaining length.
        /// </summary>
        [Test]
        public void GetLength_StreamPositionNotAtStart_ReturnsFullLength()
        {
            // Arrange
            using var stream = new MemoryStream(new byte[100]);
            stream.Position = 50;
            var resource = new StreamResource("TestResource", stream, false);
            var mockFileSystem = new Mock<IFileSystem>();
            // Act
            long actualLength = resource.GetLength(mockFileSystem.Object);
            // Assert
            Assert.That(actualLength, Is.EqualTo(100L));
        }

        /// <summary>
        /// Tests that GetLength works correctly when IsText is true.
        /// </summary>
        [Test]
        public void GetLength_IsTextTrue_ReturnsStreamLength()
        {
            // Arrange
            var mockStream = new Mock<Stream>();
            mockStream.Setup(s => s.Length).Returns(256L);
            var resource = new StreamResource("TestResource", mockStream.Object, true);
            var mockFileSystem = new Mock<IFileSystem>();
            // Act
            long actualLength = resource.GetLength(mockFileSystem.Object);
            // Assert
            Assert.That(actualLength, Is.EqualTo(256L));
        }

        /// <summary>
        /// Tests that GetLength returns the correct length for a stream with maximum long value.
        /// This tests boundary condition for very large streams.
        /// </summary>
        [Test]
        public void GetLength_StreamWithMaxLength_ReturnsMaxLongValue()
        {
            // Arrange
            var mockStream = new Mock<Stream>();
            mockStream.Setup(s => s.Length).Returns(long.MaxValue);
            var resource = new StreamResource("TestResource", mockStream.Object, false);
            var mockFileSystem = new Mock<IFileSystem>();
            // Act
            long actualLength = resource.GetLength(mockFileSystem.Object);
            // Assert
            Assert.That(actualLength, Is.EqualTo(long.MaxValue));
        }
    }
}
