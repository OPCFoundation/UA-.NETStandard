// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration.Templating.Tests
{
    [TestFixture]
    [Category("Templating")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class TemplateStringTests
    {
        [Test]
        public void CreateFromString_SimpleText_CreatesValidTemplateString()
        {
            // Arrange
            const string input = "Hello World";

            // Act
            TemplateString templateString = input;

            // Assert
            Assert.That(templateString, Is.Not.Null);
            Assert.That(templateString.ParsedTemplate, Is.Not.Null);
            Assert.That(templateString.ParsedTemplate.LiteralLength, Is.EqualTo(input.Length));
            Assert.That(templateString.ParsedTemplate.FormattedCount, Is.Zero);
        }

        [Test]
        public void CreateFromString_EmptyString_CreatesValidTemplateString()
        {
            // Act
            TemplateString templateString = string.Empty;

            // Assert
            Assert.That(templateString, Is.Not.Null);
            Assert.That(templateString.ParsedTemplate, Is.Not.Null);
            Assert.That(templateString.ParsedTemplate.LiteralLength, Is.Zero);
            Assert.That(templateString.ParsedTemplate.FormattedCount, Is.Zero);
        }

        [Test]
        public void CreateFromString_NullString_CreatesValidTemplateString()
        {
            // Act
            TemplateString templateString = (string)null!;

            // Assert
            Assert.That(templateString, Is.Not.Null);
            Assert.That(templateString.ParsedTemplate, Is.Not.Null);
            Assert.That(templateString.ParsedTemplate.LiteralLength, Is.Zero);
            Assert.That(templateString.ParsedTemplate.FormattedCount, Is.Zero);
        }

        [Test]
        public void CreateFromInterpolatedStringCreatesValidTemplateString()
        {
            // Act
            const uint value = 555555;
            var templateString = TemplateString.Parse(
                $$"""
                Hello {World} {{value}}
                {{value}}

                    {{typeof(TemplateStringTests).FullName}}
                """);

            // Assert
            Assert.That(templateString, Is.Not.Null);
            Assert.That(templateString.ParsedTemplate, Is.Not.Null);
            Assert.That(templateString.ParsedTemplate.FormattedCount, Is.EqualTo(3));
            Assert.That(templateString.ParsedTemplate.Operations, Has.Count.EqualTo(8));
            Assert.That(templateString.ParsedTemplate.LiteralLength, Is.EqualTo(24).Or.EqualTo(21)); // In case of optimizations
        }

        /// <summary>
        /// Tests that Parse creates with default parerser
        /// </summary>
        [Test]
        public void Parse_DefaultStructInitialization_ReturnsNullParsedTemplateString()
        {
            // Arrange
            var parser = default(TemplateParser);

            // Act
            var result = TemplateString.Parse(parser);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.ParsedTemplate, Is.Null);
        }

        /// <summary>
        /// Tests that Parse creates a valid TemplateString with empty parser.
        /// Input: Parser initialized with zero literals and zero formatted items
        /// Expected: Non-null TemplateString with LiteralLength=0 and FormattedCount=0
        /// </summary>
        [Test]
        public void Parse_EmptyParser_CreatesValidTemplateString()
        {
            // Arrange
            var parser = new TemplateParser(0, 0);

            // Act
            var result = TemplateString.Parse(parser);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.ParsedTemplate, Is.Not.Null);
            Assert.That(result.ParsedTemplate.LiteralLength, Is.Zero);
            Assert.That(result.ParsedTemplate.FormattedCount, Is.Zero);
        }

        /// <summary>
        /// Tests that Parse creates a valid TemplateString with parser containing only formatted items.
        /// Input: Parser with zero literals and multiple formatted items
        /// Expected: Non-null TemplateString with LiteralLength=0 and non-zero FormattedCount
        /// </summary>
        [Test]
        public void Parse_ParserWithOnlyFormattedItems_CreatesValidTemplateString()
        {
            // Arrange
            var parser = new TemplateParser(0, 3);
            parser.AppendFormatted(42);
            parser.AppendFormatted("test");
            parser.AppendFormatted(DateTime.UtcNow);

            // Act
            var result = TemplateString.Parse(parser);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.ParsedTemplate, Is.Not.Null);
            Assert.That(result.ParsedTemplate.LiteralLength, Is.Zero);
            Assert.That(result.ParsedTemplate.FormattedCount, Is.EqualTo(3));
        }

        /// <summary>
        /// Tests that Parse creates a valid TemplateString with parser containing multiline content.
        /// Input: Parser with content containing newlines and multiple lines
        /// Expected: Non-null TemplateString with IsMultiLine=true
        /// </summary>
        [Test]
        public void Parse_ParserWithMultilineContent_CreatesValidTemplateString()
        {
            // Arrange
            var parser = new TemplateParser(15, 1);
            parser.AppendLiteral("Line 1\n");
            parser.AppendFormatted("value");
            parser.AppendLiteral("\nLine 3");

            // Act
            var result = TemplateString.Parse(parser);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.ParsedTemplate, Is.Not.Null);
            Assert.That(result.ParsedTemplate.IsMultiLine, Is.True);
        }

        /// <summary>
        /// Tests that Parse creates a valid TemplateString with parser containing only whitespace.
        /// Input: Parser with only whitespace literals
        /// Expected: Non-null TemplateString with correct literal length
        /// </summary>
        [Test]
        public void Parse_ParserWithWhitespaceOnly_CreatesValidTemplateString()
        {
            // Arrange
            var parser = new TemplateParser(10, 0);
            parser.AppendLiteral("   ");
            parser.AppendLiteral("\t");
            parser.AppendLiteral("  ");

            // Act
            var result = TemplateString.Parse(parser);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.ParsedTemplate, Is.Not.Null);
            Assert.That(result.ParsedTemplate.LiteralLength, Is.EqualTo(10));
            Assert.That(result.ParsedTemplate.FormattedCount, Is.Zero);
        }

        /// <summary>
        /// Tests that Parse creates a valid TemplateString with parser containing special characters.
        /// Input: Parser with literals containing special characters like quotes, backslashes, and Unicode
        /// Expected: Non-null TemplateString preserving special characters
        /// </summary>
        [Test]
        public void Parse_ParserWithSpecialCharacters_CreatesValidTemplateString()
        {
            // Arrange
            var parser = new TemplateParser(20, 0);
            parser.AppendLiteral("Quote: \"");
            parser.AppendLiteral("\\Backslash\\");
            parser.AppendLiteral("Unicode: \u00A9");

            // Act
            var result = TemplateString.Parse(parser);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.ParsedTemplate, Is.Not.Null);
            Assert.That(result.ParsedTemplate.LiteralLength, Is.EqualTo(20));
        }

        /// <summary>
        /// Tests that Parse creates a valid TemplateString with parser having empty string literals.
        /// Input: Parser with multiple empty string literals
        /// Expected: Non-null TemplateString with correct counts
        /// </summary>
        [Test]
        public void Parse_ParserWithEmptyStringLiterals_CreatesValidTemplateString()
        {
            // Arrange
            var parser = new TemplateParser(5, 0);
            parser.AppendLiteral(string.Empty);
            parser.AppendLiteral(string.Empty);
            parser.AppendLiteral("test");
            parser.AppendLiteral(string.Empty);

            // Act
            var result = TemplateString.Parse(parser);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.ParsedTemplate, Is.Not.Null);
            Assert.That(result.ParsedTemplate.LiteralLength, Is.EqualTo(5));
        }

        /// <summary>
        /// Tests that Parse creates a valid TemplateString with parser initialized with large values.
        /// Input: Parser initialized with int.MaxValue for both literalLength and formattedCount
        /// Expected: Non-null TemplateString with correct initialization values
        /// </summary>
        [Test]
        public void Parse_ParserWithMaxValues_CreatesValidTemplateString()
        {
            // Arrange
            var parser = new TemplateParser(ParsedTemplateString.MaxLiteralLength, ParsedTemplateString.MaxFormattedCount);

            // Act
            var result = TemplateString.Parse(parser);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.ParsedTemplate, Is.Not.Null);
            Assert.That(result.ParsedTemplate.LiteralLength, Is.EqualTo(ParsedTemplateString.MaxLiteralLength));
            Assert.That(result.ParsedTemplate.FormattedCount, Is.EqualTo(ParsedTemplateString.MaxFormattedCount));
        }

        /// <summary>
        /// Tests that Parse creates a valid TemplateString with parser containing mixed numeric values.
        /// Input: Parser with various numeric types (int, double, decimal)
        /// Expected: Non-null TemplateString with correct formatted count
        /// </summary>
        [Test]
        public void Parse_ParserWithMixedNumericTypes_CreatesValidTemplateString()
        {
            // Arrange
            var parser = new TemplateParser(5, 4);
            parser.AppendLiteral("Values: ");
            parser.AppendFormatted(int.MinValue);
            parser.AppendFormatted(double.NaN);
            parser.AppendFormatted(double.PositiveInfinity);
            parser.AppendFormatted(0);

            // Act
            var result = TemplateString.Parse(parser);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.ParsedTemplate, Is.Not.Null);
            Assert.That(result.ParsedTemplate.FormattedCount, Is.EqualTo(4));
        }

        /// <summary>
        /// Tests that Parse creates a valid TemplateString with parser containing very long string literal.
        /// Input: Parser with a very long string (10000+ characters)
        /// Expected: Non-null TemplateString preserving the long string
        /// </summary>
        [Test]
        public void Parse_ParserWithVeryLongString_CreatesValidTemplateString()
        {
            // Arrange
            string longString = new('x', 10000);
            var parser = new TemplateParser(10000, 0);
            parser.AppendLiteral(longString);

            // Act
            var result = TemplateString.Parse(parser);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.ParsedTemplate, Is.Not.Null);
            Assert.That(result.ParsedTemplate.LiteralLength, Is.EqualTo(10000));
        }
    }
}
