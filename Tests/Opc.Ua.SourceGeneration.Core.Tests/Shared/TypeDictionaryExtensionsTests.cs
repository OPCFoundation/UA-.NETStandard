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

using NUnit.Framework;
using Opc.Ua.Schema.Types;

namespace Opc.Ua.SourceGeneration.Shared.Tests
{
    /// <summary>
    /// Unit tests for TypeDictionaryExtensions class.
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class TypeDictionaryExtensionsTests
    {
        /// <summary>
        /// Tests that GetDescription returns null when documentation parameter is null.
        /// </summary>
        [Test]
        public void GetDescription_NullDocumentation_ReturnsNull()
        {
            // Arrange
            Documentation documentation = null;

            // Act
            string result = documentation.GetDescription();

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that GetDescription returns null when documentation.Text property is null.
        /// </summary>
        [Test]
        public void GetDescription_NullTextProperty_ReturnsNull()
        {
            // Arrange
            var documentation = new Documentation
            {
                Text = null
            };

            // Act
            string result = documentation.GetDescription();

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that GetDescription returns empty string when documentation.Text is an empty array.
        /// </summary>
        [Test]
        public void GetDescription_EmptyTextArray_ReturnsEmptyString()
        {
            // Arrange
            var documentation = new Documentation
            {
                Text = []
            };

            // Act
            string result = documentation.GetDescription();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that GetDescription returns the single element when documentation.Text contains one element.
        /// </summary>
        [Test]
        public void GetDescription_SingleTextElement_ReturnsElement()
        {
            // Arrange
            var documentation = new Documentation
            {
                Text = ["Hello World"]
            };

            // Act
            string result = documentation.GetDescription();

            // Assert
            Assert.That(result, Is.EqualTo("Hello World"));
        }

        /// <summary>
        /// Tests that GetDescription concatenates multiple text elements with spaces.
        /// </summary>
        [Test]
        public void GetDescription_MultipleTextElements_ReturnsConcatenatedWithSpaces()
        {
            // Arrange
            var documentation = new Documentation
            {
                Text = ["Hello", "World", "OPC", "UA"]
            };

            // Act
            string result = documentation.GetDescription();

            // Assert
            Assert.That(result, Is.EqualTo("Hello World OPC UA"));
        }

        /// <summary>
        /// Tests that GetDescription handles text array with two elements correctly.
        /// </summary>
        [Test]
        public void GetDescription_TwoTextElements_ReturnsConcatenatedWithSpace()
        {
            // Arrange
            var documentation = new Documentation
            {
                Text = ["First", "Second"]
            };

            // Act
            string result = documentation.GetDescription();

            // Assert
            Assert.That(result, Is.EqualTo("First Second"));
        }

        /// <summary>
        /// Tests that GetDescription handles text array containing null elements.
        /// </summary>
        [Test]
        public void GetDescription_TextWithNullElements_ConcatenatesNonNullElements()
        {
            // Arrange
            var documentation = new Documentation
            {
                Text = ["Hello", null, "World"]
            };

            // Act
            string result = documentation.GetDescription();

            // Assert
            Assert.That(result, Is.EqualTo("Hello  World"));
        }

        /// <summary>
        /// Tests that GetDescription handles text array containing empty strings.
        /// </summary>
        [Test]
        public void GetDescription_TextWithEmptyStrings_ConcatenatesEmptyStrings()
        {
            // Arrange
            var documentation = new Documentation
            {
                Text = ["Hello", string.Empty, "World"]
            };

            // Act
            string result = documentation.GetDescription();

            // Assert
            Assert.That(result, Is.EqualTo("Hello  World"));
        }

        /// <summary>
        /// Tests that GetDescription handles text array containing whitespace strings.
        /// </summary>
        [Test]
        public void GetDescription_TextWithWhitespaceStrings_ConcatenatesWhitespaceStrings()
        {
            // Arrange
            var documentation = new Documentation
            {
                Text = ["Hello", "   ", "World"]
            };

            // Act
            string result = documentation.GetDescription();

            // Assert
            Assert.That(result, Is.EqualTo("Hello     World"));
        }

        /// <summary>
        /// Tests that GetDescription handles text array with special characters.
        /// </summary>
        [Test]
        public void GetDescription_TextWithSpecialCharacters_ConcatenatesCorrectly()
        {
            // Arrange
            var documentation = new Documentation
            {
                Text = ["Line1\n", "Tab\t", "Quote\""]
            };

            // Act
            string result = documentation.GetDescription();

            // Assert
            Assert.That(result, Is.EqualTo("Line1\n Tab\t Quote\""));
        }

        /// <summary>
        /// Tests that GetDescription handles text array with very long strings.
        /// </summary>
        [Test]
        public void GetDescription_TextWithVeryLongStrings_ConcatenatesCorrectly()
        {
            // Arrange
            string longString = new('A', 10000);
            var documentation = new Documentation
            {
                Text = [longString, "Short", longString]
            };

            // Act
            string result = documentation.GetDescription();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(longString.Length + 1 + 5 + 1 + longString.Length));
            Assert.That(result, Does.StartWith(longString));
            Assert.That(result, Does.EndWith(longString));
        }

        /// <summary>
        /// Tests that GetDescription handles text array with many elements.
        /// </summary>
        [Test]
        public void GetDescription_TextWithManyElements_ConcatenatesAllElements()
        {
            // Arrange
            string[] textArray = new string[100];
            for (int i = 0; i < textArray.Length; i++)
            {
                textArray[i] = $"Element{i}";
            }
            var documentation = new Documentation
            {
                Text = textArray
            };

            // Act
            string result = documentation.GetDescription();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Does.StartWith("Element0"));
            Assert.That(result, Does.EndWith("Element99"));
            Assert.That(result, Does.Contain("Element50"));
        }

        /// <summary>
        /// Tests that GetDescription handles single element with empty string.
        /// </summary>
        [Test]
        public void GetDescription_SingleEmptyString_ReturnsEmptyString()
        {
            // Arrange
            var documentation = new Documentation
            {
                Text = [string.Empty]
            };

            // Act
            string result = documentation.GetDescription();

            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that GetDescription handles single element with null string.
        /// </summary>
        [Test]
        public void GetDescription_SingleNullString_ReturnsNull()
        {
            // Arrange
            var documentation = new Documentation
            {
                Text = [null]
            };

            // Act
            string result = documentation.GetDescription();

            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that GetDescription handles text array with all null elements.
        /// </summary>
        [Test]
        public void GetDescription_AllNullElements_ReturnsEmptyString()
        {
            // Arrange
            var documentation = new Documentation
            {
                Text = [null, null, null]
            };

            // Act
            string result = documentation.GetDescription();

            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that GetDescription handles text array with Unicode characters.
        /// </summary>
        [Test]
        public void GetDescription_TextWithUnicodeCharacters_ConcatenatesCorrectly()
        {
            // Arrange
            var documentation = new Documentation
            {
                Text = ["Hello", "世界", "🌍"]
            };

            // Act
            string result = documentation.GetDescription();

            // Assert
            Assert.That(result, Is.EqualTo("Hello 世界 🌍"));
        }
    }
}
