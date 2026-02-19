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

using System.IO;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration.Templating.Tests
{
    /// <summary>
    /// Unit tests for the <see cref="TemplateContext"/> class.
    /// </summary>
    [TestFixture]
    [Category("Templating")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class TemplateContextTests
    {
        /// <summary>
        /// Tests that the constructor initializes all properties correctly with valid parameters.
        /// Input: Valid TemplateWriter, non-empty token string, and valid TemplateString.
        /// Expected: All properties are set correctly, Index is initialized to 0, Target and Template are null.
        /// </summary>
        [Test]
        public void Constructor_ValidParameters_InitializesAllPropertiesCorrectly()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            var writer = new TemplateWriter(stringWriter);
            const string token = "testToken";
            TemplateString templateString = "testTemplate";

            // Act
            var context = new TemplateContext(writer, token, templateString);

            // Assert
            Assert.That(context.Out, Is.SameAs(writer));
            Assert.That(context.Token, Is.EqualTo(token));
            Assert.That(context.TemplateString, Is.SameAs(templateString));
            Assert.That(context.Index, Is.EqualTo(0));
            Assert.That(context.Target, Is.Null);
            Assert.That(context.Template, Is.Null);
        }

        /// <summary>
        /// Tests that the constructor accepts null writer parameter.
        /// Input: Null TemplateWriter, valid token string, and valid TemplateString.
        /// Expected: Out property is set to null, other properties are set correctly.
        /// </summary>
        [Test]
        public void Constructor_NullWriter_SetsOutToNull()
        {
            // Arrange
            const string token = "testToken";
            TemplateString templateString = "testTemplate";

            // Act
            var context = new TemplateContext(null, token, templateString);

            // Assert
            Assert.That(context.Out, Is.Null);
            Assert.That(context.Token, Is.EqualTo(token));
            Assert.That(context.TemplateString, Is.SameAs(templateString));
            Assert.That(context.Index, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that the constructor accepts null token parameter.
        /// Input: Valid TemplateWriter, null token string, and valid TemplateString.
        /// Expected: Token property is set to null, other properties are set correctly.
        /// </summary>
        [Test]
        public void Constructor_NullToken_SetsTokenToNull()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            var writer = new TemplateWriter(stringWriter);
            TemplateString templateString = "testTemplate";

            // Act
            var context = new TemplateContext(writer, null, templateString);

            // Assert
            Assert.That(context.Out, Is.SameAs(writer));
            Assert.That(context.Token, Is.Null);
            Assert.That(context.TemplateString, Is.SameAs(templateString));
            Assert.That(context.Index, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that the constructor accepts empty token string.
        /// Input: Valid TemplateWriter, empty token string, and valid TemplateString.
        /// Expected: Token property is set to empty string, other properties are set correctly.
        /// </summary>
        [Test]
        public void Constructor_EmptyToken_SetsTokenToEmpty()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            var writer = new TemplateWriter(stringWriter);
            const string token = "";
            TemplateString templateString = "testTemplate";

            // Act
            var context = new TemplateContext(writer, token, templateString);

            // Assert
            Assert.That(context.Out, Is.SameAs(writer));
            Assert.That(context.Token, Is.Empty);
            Assert.That(context.TemplateString, Is.SameAs(templateString));
            Assert.That(context.Index, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that the constructor accepts whitespace-only token string.
        /// Input: Valid TemplateWriter, whitespace token string, and valid TemplateString.
        /// Expected: Token property is set to whitespace string, other properties are set correctly.
        /// </summary>
        [Test]
        public void Constructor_WhitespaceToken_SetsTokenToWhitespace()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            var writer = new TemplateWriter(stringWriter);
            const string token = "   \t\n";
            TemplateString templateString = "testTemplate";

            // Act
            var context = new TemplateContext(writer, token, templateString);

            // Assert
            Assert.That(context.Out, Is.SameAs(writer));
            Assert.That(context.Token, Is.EqualTo(token));
            Assert.That(context.TemplateString, Is.SameAs(templateString));
            Assert.That(context.Index, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that the constructor accepts null TemplateString parameter.
        /// Input: Valid TemplateWriter, valid token string, and null TemplateString.
        /// Expected: TemplateString property is set to null, other properties are set correctly.
        /// </summary>
        [Test]
        public void Constructor_NullTemplateString_SetsTemplateStringToNull()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            var writer = new TemplateWriter(stringWriter);
            const string token = "testToken";

            // Act
            var context = new TemplateContext(writer, token, null);

            // Assert
            Assert.That(context.Out, Is.SameAs(writer));
            Assert.That(context.Token, Is.EqualTo(token));
            Assert.That(context.TemplateString, Is.Null);
            Assert.That(context.Index, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that the constructor handles token with special characters correctly.
        /// Input: Valid TemplateWriter, token with special characters, and valid TemplateString.
        /// Expected: Token property preserves special characters, other properties are set correctly.
        /// </summary>
        [Test]
        public void Constructor_TokenWithSpecialCharacters_PreservesSpecialCharacters()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            var writer = new TemplateWriter(stringWriter);
            const string token = "!@#$%^&*()_+-={}[]|:;<>?,./~`";
            TemplateString templateString = "testTemplate";

            // Act
            var context = new TemplateContext(writer, token, templateString);

            // Assert
            Assert.That(context.Out, Is.SameAs(writer));
            Assert.That(context.Token, Is.EqualTo(token));
            Assert.That(context.TemplateString, Is.SameAs(templateString));
            Assert.That(context.Index, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that the constructor handles very long token string correctly.
        /// Input: Valid TemplateWriter, very long token string (1000+ characters), and valid TemplateString.
        /// Expected: Token property preserves the long string, other properties are set correctly.
        /// </summary>
        [Test]
        public void Constructor_VeryLongToken_PreservesLongString()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            var writer = new TemplateWriter(stringWriter);
            string token = new('a', 10000);
            TemplateString templateString = "testTemplate";

            // Act
            var context = new TemplateContext(writer, token, templateString);

            // Assert
            Assert.That(context.Out, Is.SameAs(writer));
            Assert.That(context.Token, Is.EqualTo(token));
            Assert.That(context.Token.Length, Is.EqualTo(10000));
            Assert.That(context.TemplateString, Is.SameAs(templateString));
            Assert.That(context.Index, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that the constructor handles all null parameters correctly.
        /// Input: Null values for all parameters.
        /// Expected: All properties are set to null except Index which is 0.
        /// </summary>
        [Test]
        public void Constructor_AllNullParameters_InitializesWithNulls()
        {
            // Act
            var context = new TemplateContext(null, null, null);

            // Assert
            Assert.That(context.Out, Is.Null);
            Assert.That(context.Token, Is.Null);
            Assert.That(context.TemplateString, Is.Null);
            Assert.That(context.Index, Is.EqualTo(0));
            Assert.That(context.Target, Is.Null);
            Assert.That(context.Template, Is.Null);
        }

        /// <summary>
        /// Tests that the constructor handles token with Unicode characters correctly.
        /// Input: Valid TemplateWriter, token with Unicode characters, and valid TemplateString.
        /// Expected: Token property preserves Unicode characters, other properties are set correctly.
        /// </summary>
        [Test]
        public void Constructor_TokenWithUnicodeCharacters_PreservesUnicodeCharacters()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            var writer = new TemplateWriter(stringWriter);
            const string token = "Hello世界🌍مرحبا";
            TemplateString templateString = "testTemplate";

            // Act
            var context = new TemplateContext(writer, token, templateString);

            // Assert
            Assert.That(context.Out, Is.SameAs(writer));
            Assert.That(context.Token, Is.EqualTo(token));
            Assert.That(context.TemplateString, Is.SameAs(templateString));
            Assert.That(context.Index, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that the constructor handles token with control characters correctly.
        /// Input: Valid TemplateWriter, token with control characters, and valid TemplateString.
        /// Expected: Token property preserves control characters, other properties are set correctly.
        /// </summary>
        [Test]
        public void Constructor_TokenWithControlCharacters_PreservesControlCharacters()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            var writer = new TemplateWriter(stringWriter);
            const string token = "test\0\a\b\f\r\n\ttoken";
            TemplateString templateString = "testTemplate";

            // Act
            var context = new TemplateContext(writer, token, templateString);

            // Assert
            Assert.That(context.Out, Is.SameAs(writer));
            Assert.That(context.Token, Is.EqualTo(token));
            Assert.That(context.TemplateString, Is.SameAs(templateString));
            Assert.That(context.Index, Is.EqualTo(0));
        }
    }
}
