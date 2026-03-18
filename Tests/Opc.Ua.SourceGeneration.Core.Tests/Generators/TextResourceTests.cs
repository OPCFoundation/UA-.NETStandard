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
using System.Text;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Unit tests for the <see cref = "TextResource"/> class.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class TextResourceTests
    {
        /// <summary>
        /// Tests that GetLength returns the correct UTF-8 byte count for various text inputs.
        /// </summary>
        /// <param name = "text">The text to test.</param>
        /// <param name = "expectedByteCount">The expected UTF-8 byte count.</param>
        [TestCase("", 0, TestName = "GetLength_EmptyString_ReturnsZero")]
        [TestCase("Hello", 5, TestName = "GetLength_SimpleAsciiText_ReturnsCorrectByteCount")]
        [TestCase("Hello World!", 12, TestName = "GetLength_AsciiTextWithSpace_ReturnsCorrectByteCount")]
        [TestCase("café", 5, TestName = "GetLength_LatinExtendedCharacters_ReturnsCorrectByteCount")]
        [TestCase("你好", 6, TestName = "GetLength_ChineseCharacters_ReturnsCorrectByteCount")]
        [TestCase("こんにちは", 15, TestName = "GetLength_JapaneseCharacters_ReturnsCorrectByteCount")]
        [TestCase("🚀", 4, TestName = "GetLength_EmojiCharacter_ReturnsCorrectByteCount")]
        [TestCase("Hello 世界 🌍", 17, TestName = "GetLength_MixedAsciiUnicodeEmoji_ReturnsCorrectByteCount")]
        [TestCase("\n\r\t", 3, TestName = "GetLength_SpecialCharacters_ReturnsCorrectByteCount")]
        [TestCase("Line1\nLine2\r\nLine3", 18, TestName = "GetLength_TextWithNewlines_ReturnsCorrectByteCount")]
        public void GetLength_VariousTextInputs_ReturnsCorrectUtf8ByteCount(string text, long expectedByteCount)
        {
            // Arrange
            var resource = new TextResource("Test.Resource", text);
            var mockFileSystem = new Mock<IFileSystem>();
            // Act
            long actualByteCount = resource.GetLength(mockFileSystem.Object);
            // Assert
            Assert.That(actualByteCount, Is.EqualTo(expectedByteCount));
        }

        /// <summary>
        /// Tests that GetLength returns the correct UTF-8 byte count for a very long string.
        /// </summary>
        [Test]
        public void GetLength_VeryLongString_ReturnsCorrectByteCount()
        {
            // Arrange
            string longText = new('A', 100000);
            var resource = new TextResource("Test.Resource", longText);
            long expectedByteCount = Encoding.UTF8.GetByteCount(longText);
            var mockFileSystem = new Mock<IFileSystem>();
            // Act
            long actualByteCount = resource.GetLength(mockFileSystem.Object);
            // Assert
            Assert.That(actualByteCount, Is.EqualTo(expectedByteCount));
            Assert.That(actualByteCount, Is.EqualTo(100000));
        }

        /// <summary>
        /// Tests that GetLength throws ArgumentNullException when Text property is null.
        /// </summary>
        [Test]
        public void GetLength_NullText_ThrowsArgumentNullException()
        {
            // Arrange
            var resource = new TextResource("Test.Resource", null);
            var mockFileSystem = new Mock<IFileSystem>();
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => resource.GetLength(mockFileSystem.Object));
        }

        /// <summary>
        /// Tests that GetLength works correctly when fileSystem parameter is null,
        /// demonstrating that the parameter is not used by the method.
        /// </summary>
        [Test]
        public void GetLength_NullFileSystemParameter_ReturnsCorrectByteCount()
        {
            // Arrange
            const string text = "Test text";
            var resource = new TextResource("Test.Resource", text);
            long expectedByteCount = Encoding.UTF8.GetByteCount(text);
            // Act
            long actualByteCount = resource.GetLength(null);
            // Assert
            Assert.That(actualByteCount, Is.EqualTo(expectedByteCount));
            Assert.That(actualByteCount, Is.EqualTo(9));
        }

        /// <summary>
        /// Tests that GetLength returns correct byte count for text with various control characters.
        /// </summary>
        [Test]
        public void GetLength_TextWithControlCharacters_ReturnsCorrectByteCount()
        {
            // Arrange
            const string text = "Hello\0World\u0001\u0002";
            var resource = new TextResource("Test.Resource", text);
            long expectedByteCount = Encoding.UTF8.GetByteCount(text);
            var mockFileSystem = new Mock<IFileSystem>();
            // Act
            long actualByteCount = resource.GetLength(mockFileSystem.Object);
            // Assert
            Assert.That(actualByteCount, Is.EqualTo(expectedByteCount));
        }

        /// <summary>
        /// Tests that GetLength returns correct byte count for text with all valid UTF-8 ranges.
        /// </summary>
        [Test]
        public void GetLength_TextWithVariousUtf8Ranges_ReturnsCorrectByteCount()
        {
            // Arrange
            // 1-byte: ASCII
            // 2-byte: Latin Extended
            // 3-byte: CJK
            // 4-byte: Emoji
            const string text = "Aé中🎉";
            var resource = new TextResource("Test.Resource", text);
            const long expectedByteCount = 1 + 2 + 3 + 4; // Total: 10 bytes
            var mockFileSystem = new Mock<IFileSystem>();
            // Act
            long actualByteCount = resource.GetLength(mockFileSystem.Object);
            // Assert
            Assert.That(actualByteCount, Is.EqualTo(expectedByteCount));
            Assert.That(actualByteCount, Is.EqualTo(10));
        }

        /// <summary>
        /// Tests that GetLength returns correct byte count for whitespace-only text.
        /// </summary>
        [Test]
        public void GetLength_WhitespaceOnlyText_ReturnsCorrectByteCount()
        {
            // Arrange
            const string text = "   \t\t\n\r  ";
            var resource = new TextResource("Test.Resource", text);
            long expectedByteCount = Encoding.UTF8.GetByteCount(text);
            var mockFileSystem = new Mock<IFileSystem>();
            // Act
            long actualByteCount = resource.GetLength(mockFileSystem.Object);
            // Assert
            Assert.That(actualByteCount, Is.EqualTo(expectedByteCount));
        }
    }
}
