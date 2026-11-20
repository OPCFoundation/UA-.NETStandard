// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.IO;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Unit tests for the <see cref = "TextReaderResource"/> class.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class TextReaderResourceTests
    {
        /// <summary>
        /// Tests that GetLength returns 0 when called with a valid IFileSystem mock.
        /// This verifies that the method correctly indicates unknown length for a TextReader resource.
        /// </summary>
        [Test]
        public void GetLength_WithValidFileSystem_ReturnsZero()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            using var reader = new StringReader("Sample text content");
            var resource = new TextReaderResource("TestResource", reader);
            // Act
            long length = resource.GetLength(mockFileSystem.Object);
            // Assert
            Assert.That(length, Is.Zero);
        }

        /// <summary>
        /// Tests that GetLength returns 0 when called with a null IFileSystem parameter.
        /// This verifies that the method does not throw and correctly handles null input
        /// since the parameter is not actually used in the implementation.
        /// </summary>
        [Test]
        public void GetLength_WithNullFileSystem_ReturnsZero()
        {
            // Arrange
            using var reader = new StringReader("Sample text content");
            var resource = new TextReaderResource("TestResource", reader);
            // Act
            long length = resource.GetLength(null!);
            // Assert
            Assert.That(length, Is.Zero);
        }

        /// <summary>
        /// Tests that GetLength returns 0 consistently across multiple calls.
        /// This verifies that the method behavior is deterministic and stateless.
        /// </summary>
        [Test]
        public void GetLength_MultipleCallsWithSameInstance_ReturnsZeroConsistently()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            using var reader = new StringReader("Sample text content");
            var resource = new TextReaderResource("TestResource", reader);
            // Act
            long length1 = resource.GetLength(mockFileSystem.Object);
            long length2 = resource.GetLength(mockFileSystem.Object);
            long length3 = resource.GetLength(mockFileSystem.Object);
            // Assert
            Assert.That(length1, Is.Zero);
            Assert.That(length2, Is.Zero);
            Assert.That(length3, Is.Zero);
        }

        /// <summary>
        /// Tests that GetLength returns 0 with AsUtf16 parameter set to true.
        /// This verifies that the AsUtf16 encoding flag does not affect the returned length.
        /// </summary>
        [Test]
        public void GetLength_WithAsUtf16True_ReturnsZero()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            using var reader = new StringReader("Sample text content");
            var resource = new TextReaderResource("TestResource", reader, AsUtf16: true);
            // Act
            long length = resource.GetLength(mockFileSystem.Object);
            // Assert
            Assert.That(length, Is.Zero);
        }

        /// <summary>
        /// Tests that GetLength returns 0 with an empty TextReader.
        /// This verifies that the method returns 0 regardless of the TextReader content.
        /// </summary>
        [Test]
        public void GetLength_WithEmptyTextReader_ReturnsZero()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            using var reader = new StringReader(string.Empty);
            var resource = new TextReaderResource("TestResource", reader);
            // Act
            long length = resource.GetLength(mockFileSystem.Object);
            // Assert
            Assert.That(length, Is.Zero);
        }
    }
}
