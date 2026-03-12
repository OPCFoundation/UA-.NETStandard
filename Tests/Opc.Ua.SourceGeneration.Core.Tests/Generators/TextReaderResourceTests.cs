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
using System.IO;

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
            Assert.That(length, Is.EqualTo(0));
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
            long length = resource.GetLength(null);
            // Assert
            Assert.That(length, Is.EqualTo(0));
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
            Assert.That(length1, Is.EqualTo(0));
            Assert.That(length2, Is.EqualTo(0));
            Assert.That(length3, Is.EqualTo(0));
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
            Assert.That(length, Is.EqualTo(0));
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
            Assert.That(length, Is.EqualTo(0));
        }
    }
}
