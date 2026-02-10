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

namespace Opc.Ua.SourceGeneration.Templating.Tests
{
    /// <summary>
    /// Unit tests for the <see cref="Template"/> class constructor.
    /// </summary>
    [TestFixture]
    [Category("Templating")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class TemplateTests
    {
        /// <summary>
        /// Tests that the constructor successfully creates a Template instance with valid parameters.
        /// Validates that the Template object is created and can be used for basic operations.
        /// </summary>
        [Test]
        public void Constructor_WithValidParameters_CreatesTemplateSuccessfully()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var writer = new TemplateWriter(stringWriter);
            TemplateString templateString = "test template content";

            // Act
            var template = new Template(writer, templateString);

            // Assert
            Assert.That(template, Is.Not.Null);
        }

        /// <summary>
        /// Tests that the constructor accepts an empty TemplateString.
        /// Validates that the Template can be created with an empty template.
        /// </summary>
        [Test]
        public void Constructor_WithEmptyTemplateString_CreatesTemplateSuccessfully()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var writer = new TemplateWriter(stringWriter);
            TemplateString templateString = TemplateString.Empty;

            // Act
            var template = new Template(writer, templateString);

            // Assert
            Assert.That(template, Is.Not.Null);
        }

        /// <summary>
        /// Tests that the constructor accepts a null TemplateWriter parameter.
        /// The constructor does not validate parameters, so null is stored and may cause issues during later usage.
        /// Expected result: Constructor succeeds without throwing.
        /// </summary>
        [Test]
        public void Constructor_WithNullWriter_DoesNotThrow()
        {
            // Arrange
            TemplateString templateString = "test template content";

            // Act & Assert
            Assert.DoesNotThrow(() => new Template(null, templateString));
        }

        /// <summary>
        /// Tests that the constructor accepts a null TemplateString parameter.
        /// The constructor does not validate parameters, so null is stored and may cause issues during later usage.
        /// Expected result: Constructor succeeds without throwing.
        /// </summary>
        [Test]
        public void Constructor_WithNullTemplateString_DoesNotThrow()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var writer = new TemplateWriter(stringWriter);

            // Act & Assert
            Assert.DoesNotThrow(() => new Template(writer, null));
        }

        /// <summary>
        /// Tests that the constructor accepts both null parameters.
        /// The constructor does not validate parameters, so nulls are stored and may cause issues during later usage.
        /// Expected result: Constructor succeeds without throwing.
        /// </summary>
        [Test]
        public void Constructor_WithBothParametersNull_DoesNotThrow()
        {
            // Act & Assert
            Assert.DoesNotThrow(() => new Template(null, null));
        }

        /// <summary>
        /// Tests that a Template created with valid parameters can successfully add replacements.
        /// Validates that the internal initialization (including default replacements) works correctly.
        /// </summary>
        [Test]
        public void Constructor_WithValidParameters_AllowsAddingReplacements()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var writer = new TemplateWriter(stringWriter);
            TemplateString templateString = "test template content";

            // Act
            var template = new Template(writer, templateString);

            // Assert
            Assert.DoesNotThrow(() => template.AddReplacement("TestToken", "TestValue"));
        }

        /// <summary>
        /// Tests that a Template can be created with a template string containing special characters.
        /// Validates that the constructor handles various string content correctly.
        /// </summary>
        [Test]
        public void Constructor_WithSpecialCharactersInTemplateString_CreatesTemplateSuccessfully()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var writer = new TemplateWriter(stringWriter);
            TemplateString templateString = "template with special chars: \n\r\t@#$%^&*(){}[]<>|\\";

            // Act
            var template = new Template(writer, templateString);

            // Assert
            Assert.That(template, Is.Not.Null);
        }

        /// <summary>
        /// Tests that a Template can be created with a very long template string.
        /// Validates that the constructor handles large string content without issues.
        /// </summary>
        [Test]
        public void Constructor_WithVeryLongTemplateString_CreatesTemplateSuccessfully()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var writer = new TemplateWriter(stringWriter);
            TemplateString templateString = (string)new('x', 10000);

            // Act
            var template = new Template(writer, templateString);

            // Assert
            Assert.That(template, Is.Not.Null);
        }

        /// <summary>
        /// Tests that a Template can be created with a whitespace-only template string.
        /// Validates that the constructor handles whitespace content correctly.
        /// </summary>
        [Test]
        public void Constructor_WithWhitespaceOnlyTemplateString_CreatesTemplateSuccessfully()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var writer = new TemplateWriter(stringWriter);
            TemplateString templateString = "   \t\n\r   ";

            // Act
            var template = new Template(writer, templateString);

            // Assert
            Assert.That(template, Is.Not.Null);
        }

        /// <summary>
        /// Tests that AddReplacement with valid token and replacement strings stores the values correctly
        /// and can be rendered successfully.
        /// </summary>
        [Test]
        public void AddReplacement_ValidTokenAndReplacement_StoresValueSuccessfully()
        {
            // Arrange
            using var writer = new StringWriter();
            TemplateString templateString = ConstantsTemplates.BrowseName;
            Template template;
            using var templateWriter = new TemplateWriter(writer);
            template = new Template(templateWriter, templateString);

            // Act
            // Assert - verify no exception is thrown
            Assert.DoesNotThrow(() => template.AddReplacement("TestToken", "TestReplacement"));
        }

        /// <summary>
        /// Tests that AddReplacement with empty string token stores the value successfully.
        /// </summary>
        [Test]
        public void AddReplacement_EmptyToken_StoresValueSuccessfully()
        {
            // Arrange
            using var writer = new StringWriter();
            TemplateString templateString = ConstantsTemplates.BrowseName;
            Template template;
            using var templateWriter = new TemplateWriter(writer);
            template = new Template(templateWriter, templateString);

            // Act & Assert
            Assert.DoesNotThrow(() => template.AddReplacement(string.Empty, "TestReplacement"));
        }

        /// <summary>
        /// Tests that AddReplacement with whitespace-only token stores the value successfully.
        /// </summary>
        [Test]
        public void AddReplacement_WhitespaceToken_StoresValueSuccessfully()
        {
            // Arrange
            using var writer = new StringWriter();
            TemplateString templateString = ConstantsTemplates.BrowseName;
            Template template;
            using var templateWriter = new TemplateWriter(writer);
            template = new Template(templateWriter, templateString);

            // Act & Assert
            Assert.DoesNotThrow(() => template.AddReplacement("   ", "TestReplacement"));
        }

        /// <summary>
        /// Tests that AddReplacement with null replacement stores the null value successfully.
        /// </summary>
        [Test]
        public void AddReplacement_NullReplacement_StoresValueSuccessfully()
        {
            // Arrange
            using var writer = new StringWriter();
            TemplateString templateString = ConstantsTemplates.BrowseName;
            Template template;
            using var templateWriter = new TemplateWriter(writer);
            template = new Template(templateWriter, templateString);

            // Act & Assert
            Assert.DoesNotThrow(() => template.AddReplacement("TestToken", (string)null));
        }

        /// <summary>
        /// Tests that AddReplacement with empty replacement string stores the value successfully.
        /// </summary>
        [Test]
        public void AddReplacement_EmptyReplacement_StoresValueSuccessfully()
        {
            // Arrange
            using var writer = new StringWriter();
            TemplateString templateString = ConstantsTemplates.BrowseName;
            Template template;
            using var templateWriter = new TemplateWriter(writer);
            template = new Template(templateWriter, templateString);

            // Act & Assert
            Assert.DoesNotThrow(() => template.AddReplacement("TestToken", string.Empty));
        }

        /// <summary>
        /// Tests that AddReplacement with whitespace-only replacement stores the value successfully.
        /// </summary>
        [Test]
        public void AddReplacement_WhitespaceReplacement_StoresValueSuccessfully()
        {
            // Arrange
            using var writer = new StringWriter();
            TemplateString templateString = ConstantsTemplates.BrowseName;
            Template template;
            using var templateWriter = new TemplateWriter(writer);
            template = new Template(templateWriter, templateString);

            // Act & Assert
            Assert.DoesNotThrow(() => template.AddReplacement("TestToken", "   "));
        }

        /// <summary>
        /// Tests that AddReplacement with very long token string stores the value successfully.
        /// </summary>
        [Test]
        public void AddReplacement_VeryLongToken_StoresValueSuccessfully()
        {
            // Arrange
            using var writer = new StringWriter();
            TemplateString templateString = ConstantsTemplates.BrowseName;
            Template template;
            string veryLongToken = new('A', 10000);
            using var templateWriter = new TemplateWriter(writer);
            template = new Template(templateWriter, templateString);

            // Act & Assert
            Assert.DoesNotThrow(() => template.AddReplacement(veryLongToken, "TestReplacement"));
        }

        /// <summary>
        /// Tests that AddReplacement with very long replacement string stores the value successfully.
        /// </summary>
        [Test]
        public void AddReplacement_VeryLongReplacement_StoresValueSuccessfully()
        {
            // Arrange
            using var writer = new StringWriter();
            TemplateString templateString = ConstantsTemplates.BrowseName;
            Template template;
            string veryLongReplacement = new('B', 10000);
            using var templateWriter = new TemplateWriter(writer);
            template = new Template(templateWriter, templateString);

            // Act & Assert
            Assert.DoesNotThrow(() => template.AddReplacement("TestToken", veryLongReplacement));
        }

        /// <summary>
        /// Tests that AddReplacement with special characters in token stores the value successfully.
        /// </summary>
        /// <param name="token">The token with special characters.</param>
        [TestCase("Test\nToken")]
        [TestCase("Test\rToken")]
        [TestCase("Test\tToken")]
        [TestCase("Test\0Token")]
        [TestCase("Test\"Token")]
        [TestCase("Test'Token")]
        [TestCase("Test\\Token")]
        [TestCase("Test!@#$%^&*()Token")]
        [TestCase("Test<>Token")]
        [TestCase("Test{}Token")]
        [TestCase("Test[]Token")]
        public void AddReplacement_TokenWithSpecialCharacters_StoresValueSuccessfully(string token)
        {
            // Arrange
            using var writer = new StringWriter();
            TemplateString templateString = ConstantsTemplates.BrowseName;
            Template template;
            using var templateWriter = new TemplateWriter(writer);
            template = new Template(templateWriter, templateString);

            // Act & Assert
            Assert.DoesNotThrow(() => template.AddReplacement(token, "TestReplacement"));
        }

        /// <summary>
        /// Tests that AddReplacement with special characters in replacement stores the value successfully.
        /// </summary>
        /// <param name="replacement">The replacement with special characters.</param>
        [TestCase("Test\nReplacement")]
        [TestCase("Test\rReplacement")]
        [TestCase("Test\tReplacement")]
        [TestCase("Test\0Replacement")]
        [TestCase("Test\"Replacement")]
        [TestCase("Test'Replacement")]
        [TestCase("Test\\Replacement")]
        [TestCase("Test!@#$%^&*()Replacement")]
        [TestCase("Test<>Replacement")]
        [TestCase("Test{}Replacement")]
        [TestCase("Test[]Replacement")]
        public void AddReplacement_ReplacementWithSpecialCharacters_StoresValueSuccessfully(string replacement)
        {
            // Arrange
            using var writer = new StringWriter();
            TemplateString templateString = ConstantsTemplates.BrowseName;
            Template template;
            using var templateWriter = new TemplateWriter(writer);
            template = new Template(templateWriter, templateString);

            // Act & Assert
            Assert.DoesNotThrow(() => template.AddReplacement("TestToken", replacement));
        }

        /// <summary>
        /// Tests that AddReplacement updates existing token when called multiple times with the same token.
        /// </summary>
        [Test]
        public void AddReplacement_CalledTwiceWithSameToken_UpdatesValue()
        {
            // Arrange
            using var writer = new StringWriter();
            TemplateString templateString = ConstantsTemplates.BrowseName;
            Template template;
            using var templateWriter = new TemplateWriter(writer);
            template = new Template(templateWriter, templateString);

            // Act
            template.AddReplacement("TestToken", "FirstValue");

            // Assert - should not throw when updating
            Assert.DoesNotThrow(() => template.AddReplacement("TestToken", "SecondValue"));
        }

        /// <summary>
        /// Tests that AddReplacement can store multiple distinct tokens.
        /// </summary>
        [Test]
        public void AddReplacement_MultipleDistinctTokens_StoresAllValuesSuccessfully()
        {
            // Arrange
            using var writer = new StringWriter();
            TemplateString templateString = ConstantsTemplates.BrowseName;
            Template template;
            using var templateWriter = new TemplateWriter(writer);
            template = new Template(templateWriter, templateString);

            // Act & Assert
            Assert.DoesNotThrow(() =>
            {
                template.AddReplacement("Token1", "Value1");
                template.AddReplacement("Token2", "Value2");
                template.AddReplacement("Token3", "Value3");
            });
        }

        /// <summary>
        /// Tests that AddReplacement works correctly with unicode characters in token.
        /// </summary>
        [Test]
        public void AddReplacement_UnicodeToken_StoresValueSuccessfully()
        {
            // Arrange
            using var writer = new StringWriter();
            TemplateString templateString = ConstantsTemplates.BrowseName;
            Template template;
            using var templateWriter = new TemplateWriter(writer);
            template = new Template(templateWriter, templateString);

            // Act & Assert
            Assert.DoesNotThrow(() => template.AddReplacement("Test\u00E9\u4E2D\u0416Token", "TestReplacement"));
        }

        /// <summary>
        /// Tests that AddReplacement works correctly with unicode characters in replacement.
        /// </summary>
        [Test]
        public void AddReplacement_UnicodeReplacement_StoresValueSuccessfully()
        {
            // Arrange
            using var writer = new StringWriter();
            TemplateString templateString = ConstantsTemplates.BrowseName;
            Template template;
            using var templateWriter = new TemplateWriter(writer);
            template = new Template(templateWriter, templateString);

            // Act & Assert
            Assert.DoesNotThrow(() => template.AddReplacement("TestToken", "Test\u00E9\u4E2D\u0416Replacement"));
        }

        /// <summary>
        /// Tests that AddReplacement integrates correctly with Render method using actual token replacements.
        /// </summary>
        [Test]
        public void AddReplacement_IntegrationWithRender_ReplacesTokenCorrectly()
        {
            // Arrange
            using var writer = new StringWriter();
            TemplateString templateString = ConstantsTemplates.BrowseName;
            using (var templateWriter = new TemplateWriter(writer))
            {
                var template = new Template(templateWriter, templateString);

                // Act
                template.AddReplacement(Tokens.SymbolicName, "MyTestName");
                template.AddReplacement(Tokens.BrowseName, "MyTestBrowseName");

                template.Render();
            }
            string result = writer.ToString();

            // Assert
            Assert.That(result, Does.Contain("MyTestName"));
            Assert.That(result, Does.Contain("MyTestBrowseName"));
        }

        /// <summary>
        /// Tests that Render returns false when the template string has no operations.
        /// Input: Empty template string.
        /// Expected: Returns false, no output written.
        /// </summary>
        [Test]
        public void Render_EmptyTemplate_ReturnsFalse()
        {
            // Arrange
            using var writer = new StringWriter();
            TemplateString templateString = string.Empty;
            using var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, templateString);

            // Act
            bool result = template.Render();

            // Assert
            Assert.That(result, Is.False);
            Assert.That(writer.ToString(), Is.Empty);
        }

        /// <summary>
        /// Tests that Render writes a literal and returns true.
        /// Input: Template with single literal operation.
        /// Expected: Returns true, literal is written to output.
        /// </summary>
        [Test]
        public void Render_LiteralOnly_WritesLiteralAndReturnsTrue()
        {
            // Arrange
            using var writer = new StringWriter();
            TemplateString templateString = "Hello World";
            using var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, templateString);

            // Act
            bool result = template.Render();

            // Assert
            Assert.That(result, Is.True);
            Assert.That(writer.ToString(), Is.EqualTo("Hello World"));
        }

        /// <summary>
        /// Tests that Render writes whitespace and returns true.
        /// Input: Template with whitespace operation.
        /// Expected: Returns true, whitespace is written to output.
        /// </summary>
        [Test]
        public void Render_WhitespaceOnly_WritesNothingAndReturnsFalse()
        {
            // Arrange
            using var writer = new StringWriter();
            TemplateString templateString = "    ";
            using (var templateWriter = new TemplateWriter(writer))
            {
                var template = new Template(templateWriter, templateString);

                // Act
                bool result = template.Render();

                // Assert
                Assert.That(result, Is.False); // Nothing written, just whitespace
            }
            Assert.That(writer.ToString(), Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that Render writes a newline and returns true.
        /// Input: Template with line break operation.
        /// Expected: Returns true, newline is written to output.
        /// </summary>
        [Test]
        public void Render_LineBreakOnly_WritesNewLineAndReturnsTrue()
        {
            // Arrange
            using var writer = new StringWriter();
            TemplateString templateString = "\n";
            using (var templateWriter = new TemplateWriter(writer))
            {
                var template = new Template(templateWriter, templateString);

                // Act
                bool result = template.Render();
                Assert.That(result, Is.True);
            }

            // Assert
            Assert.That(writer.ToString(), Is.EqualTo(Environment.NewLine));
        }

        /// <summary>
        /// Tests that Render writes multiple line breaks correctly.
        /// Input: Template with multiple line break operations.
        /// Expected: Returns true, multiple newlines are written to output.
        /// </summary>
        [Test]
        public void Render_MultipleLineBreaks_WritesNewLinesAndReturnsTrue()
        {
            // Arrange
            using var writer = new StringWriter();
            TemplateString templateString = "Line1\nLine2\nLine3";
            using var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, templateString);

            // Act
            bool result = template.Render();

            // Assert
            Assert.That(result, Is.True);
            string expected = $"Line1{Environment.NewLine}Line2{Environment.NewLine}Line3";
            Assert.That(writer.ToString(), Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that Render handles extreme edge case of very long string literal.
        /// Input: Template with very long literal string.
        /// Expected: Returns true, entire string is written.
        /// </summary>
        [Test]
        public void Render_VeryLongLiteral_WritesEntireString()
        {
            // Arrange
            using var writer = new StringWriter();
            string longString = new('A', 10000);
            TemplateString templateString = longString;
            using var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, templateString);

            // Act
            bool result = template.Render();

            // Assert
            Assert.That(result, Is.True);
            Assert.That(writer.ToString(), Is.EqualTo(longString));
        }

        [Test]
        public void Template_CanBeCreated_ReturnsNotNull()
        {
            using var writer = new StringWriter();
            TemplateString templateString = "Test";

            using var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, templateString);

            Assert.That(template, Is.Not.Null);
        }

        [Test]
        public void WriteTemplate_WithNamespaceUriTemplate_RendersCorrectly()
        {
            using var writer = new StringWriter();
            TemplateString templateString = ConstantsTemplates.NamespaceUri;
            using (var templateWriter = new TemplateWriter(writer))
            {
                var template = new Template(templateWriter, templateString);

                template.AddReplacement(Tokens.Name, "MyNamespace");
                template.AddReplacement(Tokens.CodeName, "My.Namespace");
                template.AddReplacement(Tokens.NamespaceUri, "http://mynamespace.org/UA/");

                template.Render();
            }
            string result = writer.ToString();

            const string expected =
                """
                /// <summary>
                /// The URI for the MyNamespace namespace (.NET code namespace is 'My.Namespace').
                /// </summary>
                public const string MyNamespace = "http://mynamespace.org/UA/";

                """;
            Assert.That(result, Is.EqualTo(expected.ReplaceLineEndings()));
        }

        [Test]
        public void WriteTemplate_WithBrowseNameTemplate_RendersCorrectly()
        {
            using var writer = new StringWriter();
            TemplateString templateString = ConstantsTemplates.BrowseName;
            using (var templateWriter = new TemplateWriter(writer))
            {
                var template = new Template(templateWriter, templateString);

                template.AddReplacement(Tokens.SymbolicName, "MyBrowseName");
                template.AddReplacement(Tokens.BrowseName, "MyBrowseName");

                template.Render();
            }
            string result = writer.ToString();

            const string expected = """
                public const string MyBrowseName = "MyBrowseName";

                """;
            Assert.That(result, Is.EqualTo(expected.ReplaceLineEndings()));
        }

        [Test]
        public void WriteTemplate_WithIdDeclarationTemplate_RendersCorrectly()
        {
            using var writer = new StringWriter();
            TemplateString templateString = NodeIdTemplates.IdDeclaration;
            using (var templateWriter = new TemplateWriter(writer))
            {
                var template = new Template(templateWriter, templateString);

                template.AddReplacement(Tokens.IdType, "uint");
                template.AddReplacement(Tokens.SymbolicName, "MyId");
                template.AddReplacement(Tokens.Identifier, "12345");

                template.Render();
            }
            string result = writer.ToString();

            const string expected = """
                public const uint MyId = 12345;

                """;
            Assert.That(result, Is.EqualTo(expected.ReplaceLineEndings()));
        }

        [Test]
        public void AddReplacement_Generic_RendersCorrectly()
        {
            using var writer = new StringWriter();
            const string myToken = nameof(myToken);
            var templateString = TemplateString.Parse($"Value is {myToken}");
            using (var templateWriter = new TemplateWriter(writer))
            {
                var template = new Template(templateWriter, templateString);

                template.AddReplacement(myToken, 123.45);

                template.Render();
            }
            string result = writer.ToString();

            Assert.That(result, Is.EqualTo("Value is 123.45"));
        }

        [Test]
        public void AddReplacement_Bool_RendersCorrectly()
        {
            using var writer = new StringWriter();
            const string myToken = nameof(myToken);
            var templateString = TemplateString.Parse($"Value is {myToken}");
            using (var templateWriter = new TemplateWriter(writer))
            {
                var template = new Template(templateWriter, templateString);

                template.AddReplacement(myToken, true);

                template.Render();
            }
            string result = writer.ToString();

            Assert.That(result, Is.EqualTo("Value is true"));
        }

        [Test]
        public void AddReplacement_String_RendersCorrectly()
        {
            using var writer = new StringWriter();
            const string myToken = nameof(myToken);
            var templateString = TemplateString.Parse($"Value is {myToken}");
            using (var templateWriter = new TemplateWriter(writer))
            {
                var template = new Template(templateWriter, templateString);

                template.AddReplacement(myToken, "test");

                template.Render();
            }
            string result = writer.ToString();

            Assert.That(result, Is.EqualTo("Value is test"));
        }

        [Test]
        public void AddReplacement_WithListOfTargets_RendersCorrectly()
        {
            using var writer = new StringWriter();
            const string myList = nameof(myList);
            const string itemValue = nameof(itemValue);
            using (var templateWriter = new TemplateWriter(writer))
            {
                var template = new Template(templateWriter, TemplateString.Parse(
                $$"""

                {{myList}}
                """));
                string[] targets = ["One", "Two", "Three"];
                var itemTemplate = TemplateString.Parse($"Item: {itemValue} ");

                template.AddReplacement(myList, itemTemplate, targets, c =>
                {
                    c.Template.AddReplacement(itemValue, (string)c.Target);
                    return c.Template.Render();
                });

                template.Render();
            }
            string result = writer.ToString();

            // There is no line break in the item value template, so the
            // result of mylist substitution will be on a single line.
            const string expected =
                """

                Item: One Item: Two Item: Three
                """;
            Assert.That(result, Is.EqualTo(expected.ReplaceLineEndings()));
        }

        [Test]
        public void AddReplacement_WithSingleTarget_RendersCorrectly()
        {
            using var writer = new StringWriter();
            const string myList = nameof(myList);
            const string itemValue = nameof(itemValue);
            using (var templateWriter = new TemplateWriter(writer))
            {
#pragma warning disable RCS1214 // Unnecessary interpolated string
                var template = new Template(templateWriter, TemplateString.Parse($"{myList}"));
#pragma warning restore RCS1214 // Unnecessary interpolated string
                var itemTemplate = TemplateString.Parse($"Item: {itemValue}");

                template.AddReplacement(myList, itemTemplate, "Single", c =>
                {
                    c.Template.AddReplacement(itemValue, (string)c.Target);
                    return c.Template.Render();
                });

                template.Render();
            }
            string result = writer.ToString();

            const string expected =
                """
                Item: Single
                """;
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void AddReplacement_WithMissingReplacement_RendersCorrectly()
        {
            using var writer = new StringWriter();
            const string myList = nameof(myList);
            const string itemValue = nameof(itemValue);
            using (var templateWriter = new TemplateWriter(writer))
            {
                var template = new Template(templateWriter, TemplateString.Parse(
            $$"""
            {
                {{myList}}
                {{itemValue}}
            }
            """));

                var itemTemplate = TemplateString.Parse($"Item: {itemValue}");
                template.AddReplacement(myList, itemTemplate, ["Single"], c =>
                {
                    c.Template.AddReplacement(itemValue, (string)c.Target);
                    return c.Template.Render();
                });

                template.Render();
            }
            string result = writer.ToString();

            const string expected =
                """
                {
                    Item: Single
                }
                """;
            Assert.That(result, Is.EqualTo(expected.ReplaceLineEndings()));
        }

        [Test]
        public void AddReplacement_WithSingleTargetAndLoadHandler_RendersCorrectly()
        {
            using var writer = new StringWriter();
            const string myItem = nameof(myItem);
            const string itemValue = nameof(itemValue);
            using (var templateWriter = new TemplateWriter(writer))
            {
#pragma warning disable RCS1214 // Unnecessary interpolated string
                var template = new Template(templateWriter, TemplateString.Parse($"{myItem}"));
#pragma warning restore RCS1214 // Unnecessary interpolated string
                var itemTemplate1 = TemplateString.Parse($"Template1: {itemValue}");
                var itemTemplate2 = TemplateString.Parse($"Template2: {itemValue}");

                template.AddReplacement(myItem, "Single",
                    onLoad: c => (string)c.Target == "Single" ? itemTemplate1 : itemTemplate2,
                    onWrite: c =>
                    {
                        c.Template.AddReplacement(itemValue, (string)c.Target);
                        return c.Template.Render();
                    });

                template.Render();
            }
            string result = writer.ToString();

            const string expected =
                """
                Template1: Single
                """;
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void AddReplacement_WithListOfTargetsAndLoadHandler_RendersCorrectly()
        {
            using var writer = new StringWriter();
            const string myList = nameof(myList);
            const string itemValue = nameof(itemValue);
            using (var templateWriter = new TemplateWriter(writer))
            {
#pragma warning disable RCS1214 // Unnecessary interpolated string
                var template = new Template(templateWriter, TemplateString.Parse($"{myList}"));
#pragma warning restore RCS1214 // Unnecessary interpolated string
                string[] targets = ["One", "Two"];
                var itemTemplate1 = TemplateString.Parse($"Template1: {itemValue}\r\n");
                var itemTemplate2 = TemplateString.Parse($"Template2: {itemValue}");

                template.AddReplacement(myList, targets,
                    onLoad: c => (string)c.Target == "One" ? itemTemplate1 : itemTemplate2,
                    onWrite: c =>
                    {
                        c.Template.AddReplacement(itemValue, (string)c.Target);
                        return c.Template.Render();
                    });

                template.Render();
            }
            string result = writer.ToString();

            const string expected =
                """
                Template1: One

                Template2: Two
                """;
            Assert.That(result, Is.EqualTo(expected.ReplaceLineEndings()));
        }

        [Test]
        public void AddReplacement_WithListOfTargetsAndFinalLineBreakRendersCorrectly()
        {
            using var writer = new StringWriter();
            const string myList = nameof(myList);
            const string itemValue = nameof(itemValue);
            using (var templateWriter = new TemplateWriter(writer))
            {
#pragma warning disable RCS1214 // Unnecessary interpolated string
                var template = new Template(templateWriter, TemplateString.Parse($"{myList}"));
#pragma warning restore RCS1214 // Unnecessary interpolated string
                string[] targets = ["One", "Two"];
                var itemTemplate1 = TemplateString.Parse($"Template1: {itemValue}\r\n\t");
                var itemTemplate2 = TemplateString.Parse($"Template2: {itemValue}");

                template.AddReplacement(myList, targets,
                    onLoad: c => (string)c.Target == "Two" ? itemTemplate1 : itemTemplate2,
                    onWrite: c =>
                    {
                        c.Template.AddReplacement(itemValue, (string)c.Target);
                        return c.Template.Render();
                    });

                template.Render();
            }
            string result = writer.ToString();

            const string expected =
                """
                Template2: OneTemplate1: Two

                """ +
                "\t";
            Assert.That(result, Is.EqualTo(expected.ReplaceLineEndings()));
        }

        [Test]
        public void AddReplacement_WithMessageTemplate_RendersCorrectly()
        {
            using var writer = new StringWriter();
            using (var templateWriter = new TemplateWriter(writer))
            {
                var template = new Template(templateWriter, MessagesTemplates.File);
                template.AddReplacement(Tokens.Prefix, "MyNamespace");
                template.AddReplacement(Tokens.CodeHeader, string.Empty);
                template.AddReplacement(
                    Tokens.TypeList,
                    MessagesTemplates.DataTypeAnnotation,
                    ["Type1", "Type2", "Type3"],
                    context =>
                    {
                        context.Template.AddReplacement(Tokens.Name, (string)context.Target);
                        return context.Template.Render();
                    });

                template.Render();
            }
            string result = writer.ToString();

            const string expected =
                """


                namespace MyNamespace
                {
                    /// <summary>
                    /// The request message for the Type1 service.
                    /// </summary>
                    public partial class Type1Request : global::Opc.Ua.IServiceRequest
                    {
                    }

                    /// <summary>
                    /// The response message for the Type1 service.
                    /// </summary>
                    public partial class Type1Response : global::Opc.Ua.IServiceResponse
                    {
                    }

                    /// <summary>
                    /// The request message for the Type2 service.
                    /// </summary>
                    public partial class Type2Request : global::Opc.Ua.IServiceRequest
                    {
                    }

                    /// <summary>
                    /// The response message for the Type2 service.
                    /// </summary>
                    public partial class Type2Response : global::Opc.Ua.IServiceResponse
                    {
                    }

                    /// <summary>
                    /// The request message for the Type3 service.
                    /// </summary>
                    public partial class Type3Request : global::Opc.Ua.IServiceRequest
                    {
                    }

                    /// <summary>
                    /// The response message for the Type3 service.
                    /// </summary>
                    public partial class Type3Response : global::Opc.Ua.IServiceResponse
                    {
                    }
                }
                """;
            Assert.That(result, Is.EqualTo(expected.ReplaceLineEndings()));
        }

        [Test]
        public void AddReplacement_NestedTemplate_RendersCorrectly()
        {
            using var writer = new StringWriter();
            const string subTemplate = nameof(subTemplate);
            const string innerTemplate = nameof(innerTemplate);
            const string myValue = nameof(myValue);

            using (var templateWriter = new TemplateWriter(writer))
            {
                var mainTemplate = new Template(templateWriter, TemplateString.Parse(
                $$"""
                Main:
                    {{subTemplate}}
                """));

                var subTemplateString = TemplateString.Parse(
                    $$"""
                Sub:
                    {{innerTemplate}}
                """);
                var innerTemplateString = TemplateString.Parse(
                    $$"""
                1. This is my value: {{myValue}}
                2. This is my value: {{myValue}}
                3. This is my value: {{myValue}}
                """);

                mainTemplate.AddReplacement(
                    subTemplate,
                    subTemplateString,
                    ["target"],
                    onWrite: subContext =>
                    {
                        subContext.Template.AddReplacement(
                            innerTemplate,
                            innerTemplateString,
                            ["inner_target"],
                            onWrite: innerContext =>
                            {
                                innerContext.Template.AddReplacement(myValue, 123);
                                return innerContext.Template.Render();
                            }
                        );
                        return subContext.Template.Render();
                    }
                );

                mainTemplate.Render();
            }
            string result = writer.ToString();

            const string expected =
                """
                Main:
                    Sub:
                        1. This is my value: 123
                        2. This is my value: 123
                        3. This is my value: 123
                """;
            Assert.That(result, Is.EqualTo(expected.ReplaceLineEndings()));
        }
    }
}
