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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration.Templating.Tests
{
    /// <summary>
    /// Tests for the ParsedTemplateString.ToString method
    /// </summary>
    [TestFixture]
    [Category("Templating")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ParsedTemplateStringTests
    {
        /// <summary>
        /// Tests ToString with null format provider.
        /// Verifies that null format provider is handled correctly (uses current culture).
        /// </summary>
        [Test]
        public void ToString_NullFormatProvider_UsesCurrentCulture()
        {
            // Arrange
            var parsed = new ParsedTemplateString(6, 1);
            parsed.AddLiteral("Hello ");
            parsed.AddFormatted("World", typeof(string));

            // Act
            string result = parsed.ToString(null);

            // Assert
            Assert.That(result, Is.EqualTo("Hello World"));
        }

        /// <summary>
        /// Tests ToString with an empty ParsedTemplateString.
        /// Verifies that an empty string is returned when there are no operations.
        /// </summary>
        [Test]
        public void ToString_EmptyParsedTemplateString_ReturnsEmptyString()
        {
            // Arrange
            var parsed = new ParsedTemplateString(0, 0);

            // Act
            string result = parsed.ToString(CultureInfo.InvariantCulture);

            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests ToString with only literal content and no formatted values.
        /// Verifies that literals are returned as-is without any formatting.
        /// </summary>
        [Test]
        public void ToString_OnlyLiterals_ReturnsLiteralText()
        {
            // Arrange
            var parsed = new ParsedTemplateString(11, 0);
            parsed.AddLiteral("Hello World");

            // Act
            string result = parsed.ToString(CultureInfo.InvariantCulture);

            // Assert
            Assert.That(result, Is.EqualTo("Hello World"));
        }

        /// <summary>
        /// Tests ToString with multiple formatted values.
        /// Verifies that multiple placeholders are correctly replaced with arguments.
        /// </summary>
        [Test]
        public void ToString_MultipleFormattedValues_FormatsAllCorrectly()
        {
            // Arrange
            var parsed = new ParsedTemplateString(10, 3);
            parsed.AddFormatted("First", typeof(string));
            parsed.AddLiteral(" and ");
            parsed.AddFormatted("Second", typeof(string));
            parsed.AddLiteral(" and ");
            parsed.AddFormatted("Third", typeof(string));

            // Act
            string result = parsed.ToString(CultureInfo.InvariantCulture);

            // Assert
            Assert.That(result, Is.EqualTo("First and Second and Third"));
        }

        /// <summary>
        /// Tests ToString with culture-specific number formatting.
        /// Verifies that numeric values are formatted according to the provided culture.
        /// </summary>
        [Test]
        public void ToString_WithCultureSpecificNumberFormatting_FormatsAccordingToCulture()
        {
            // Arrange - US culture uses period as decimal separator
            var parsed = new ParsedTemplateString(7, 1);
            parsed.AddLiteral("Value: ");
            parsed.AddFormatted("1234.56", typeof(double));
            var usCulture = CultureInfo.GetCultureInfo("en-US");

            // Act
            string result = parsed.ToString(usCulture);

            // Assert
            Assert.That(result, Is.EqualTo("Value: 1234.56"));
        }

        /// <summary>
        /// Tests ToString with numeric value formatting using invariant culture.
        /// Verifies that numeric arguments are formatted correctly with InvariantCulture.
        /// </summary>
        [Test]
        public void ToString_WithNumericValue_FormatsCorrectly()
        {
            // Arrange
            var parsed = new ParsedTemplateString(9, 1);
            parsed.AddLiteral("Number: ");
            parsed.AddFormatted("42", typeof(int));

            // Act
            string result = parsed.ToString(CultureInfo.InvariantCulture);

            // Assert
            Assert.That(result, Is.EqualTo("Number: 42"));
        }

        /// <summary>
        /// Tests ToString with whitespace and line breaks.
        /// Verifies that whitespace and line breaks are preserved in the output.
        /// </summary>
        [Test]
        public void ToString_WithWhitespaceAndLineBreaks_PreservesFormatting()
        {
            // Arrange
            var parsed = new ParsedTemplateString(20, 1);
            parsed.AddLiteral("Line1\n");
            parsed.AddFormatted("Value", typeof(string));
            parsed.AddLiteral("\nLine3");

            // Act
            string result = parsed.ToString(CultureInfo.InvariantCulture);

            // Assert
            Assert.That(result, Does.Contain("Line1"));
            Assert.That(result, Does.Contain("Value"));
            Assert.That(result, Does.Contain("Line3"));
        }

        /// <summary>
        /// Tests ToString with only formatted values and no literals.
        /// Verifies that formatted values alone are correctly formatted.
        /// </summary>
        [Test]
        public void ToString_OnlyFormattedValues_FormatsCorrectly()
        {
            // Arrange
            var parsed = new ParsedTemplateString(0, 2);
            parsed.AddFormatted("First", typeof(string));
            parsed.AddFormatted("Second", typeof(string));

            // Act
            string result = parsed.ToString(CultureInfo.InvariantCulture);

            // Assert
            Assert.That(result, Is.EqualTo("FirstSecond"));
        }

        /// <summary>
        /// Tests ToString with special characters in literals.
        /// Verifies that special characters are preserved correctly.
        /// </summary>
        [Test]
        public void ToString_WithSpecialCharactersInLiterals_PreservesCharacters()
        {
            // Arrange
            var parsed = new ParsedTemplateString(20, 1);
            parsed.AddLiteral("Special: @#$%^& ");
            parsed.AddFormatted("Value", typeof(string));

            // Act
            string result = parsed.ToString(CultureInfo.InvariantCulture);

            // Assert
            Assert.That(result, Is.EqualTo("Special: @#$%^& Value"));
        }

        /// <summary>
        /// Tests ToString with empty string as formatted value.
        /// Verifies that empty formatted values are handled correctly.
        /// </summary>
        [Test]
        public void ToString_WithEmptyFormattedValue_HandlesCorrectly()
        {
            // Arrange
            var parsed = new ParsedTemplateString(6, 1);
            parsed.AddLiteral("Value:");
            parsed.AddFormatted(string.Empty, typeof(string));

            // Act
            string result = parsed.ToString(CultureInfo.InvariantCulture);

            // Assert
            Assert.That(result, Is.EqualTo("Value:"));
        }

        /// <summary>
        /// Tests ToString with mixed string and non-string types.
        /// Verifies that different value types are handled correctly.
        /// </summary>
        [Test]
        public void ToString_WithMixedTypes_FormatsAllCorrectly()
        {
            // Arrange
            var parsed = new ParsedTemplateString(20, 3);
            parsed.AddLiteral("String: ");
            parsed.AddFormatted("text", typeof(string));
            parsed.AddLiteral(", Number: ");
            parsed.AddFormatted("123", typeof(int));
            parsed.AddLiteral(", Bool: ");
            parsed.AddFormatted("True", typeof(bool));

            // Act
            string result = parsed.ToString(CultureInfo.InvariantCulture);

            // Assert
            Assert.That(result, Is.EqualTo("String: text, Number: 123, Bool: True"));
        }

        /// <summary>
        /// Tests that AddLiteral with an empty string does not add any operations.
        /// </summary>
        [Test]
        public void AddLiteral_EmptyString_DoesNotAddOperation()
        {
            // Arrange
            var parsed = new ParsedTemplateString(0, 0);

            // Act
            parsed.AddLiteral(string.Empty);

            // Assert
            Assert.That(parsed.Operations.Count, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that AddLiteral with whitespace-only string creates a WhiteSpace operation.
        /// </summary>
        [TestCase(" ")]
        [TestCase("  ")]
        [TestCase("     ")]
        [TestCase("          ")]
        public void AddLiteral_WhitespaceOnlyString_CreatesWhiteSpaceOperation(string whitespace)
        {
            // Arrange
            var parsed = new ParsedTemplateString(10, 0);

            // Act
            parsed.AddLiteral(whitespace);

            // Assert
            Assert.That(parsed.Operations.Count, Is.EqualTo(1));
            Assert.That(parsed.Operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.WhiteSpace));
            Assert.That(parsed.Operations[0].Item, Is.EqualTo(whitespace));
            Assert.That(parsed.Operations[0].Offset, Is.EqualTo(0));
            Assert.That(parsed.Operations[0].LineNumber, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that AddLiteral with regular text creates a Literal operation.
        /// </summary>
        [TestCase("Hello")]
        [TestCase("World")]
        [TestCase("Test123")]
        [TestCase("a")]
        public void AddLiteral_RegularText_CreatesLiteralOperation(string text)
        {
            // Arrange
            var parsed = new ParsedTemplateString(20, 0);

            // Act
            parsed.AddLiteral(text);

            // Assert
            Assert.That(parsed.Operations.Count, Is.EqualTo(1));
            Assert.That(parsed.Operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(parsed.Operations[0].Item, Is.EqualTo(text));
            Assert.That(parsed.Operations[0].Offset, Is.EqualTo(0));
            Assert.That(parsed.Operations[0].LineNumber, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that AddLiteral with Unix line ending creates correct operations.
        /// </summary>
        [Test]
        public void AddLiteral_UnixLineEnding_CreatesLineBreakOperation()
        {
            // Arrange
            var parsed = new ParsedTemplateString(20, 0);
            const string text = "Line1\nLine2";

            // Act
            parsed.AddLiteral(text);

            // Assert
            Assert.That(parsed.Operations.Count, Is.EqualTo(3));
            Assert.That(parsed.Operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(parsed.Operations[0].Item, Is.EqualTo("Line1"));
            Assert.That(parsed.Operations[1].Type, Is.EqualTo(ParsedTemplateString.OpType.LineBreak));
            Assert.That(parsed.Operations[1].Item, Is.EqualTo(Environment.NewLine));
            Assert.That(parsed.Operations[2].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(parsed.Operations[2].Item, Is.EqualTo("Line2"));
        }

        /// <summary>
        /// Tests that AddLiteral with Windows line ending strips \r and creates LineBreak operation.
        /// </summary>
        [Test]
        public void AddLiteral_WindowsLineEnding_StripsCarriageReturnAndCreatesLineBreak()
        {
            // Arrange
            var parsed = new ParsedTemplateString(20, 0);
            const string text = "Line1\r\nLine2";

            // Act
            parsed.AddLiteral(text);

            // Assert
            Assert.That(parsed.Operations.Count, Is.EqualTo(3));
            Assert.That(parsed.Operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(parsed.Operations[0].Item, Is.EqualTo("Line1"));
            Assert.That(parsed.Operations[1].Type, Is.EqualTo(ParsedTemplateString.OpType.LineBreak));
            Assert.That(parsed.Operations[1].Item, Is.EqualTo(Environment.NewLine));
            Assert.That(parsed.Operations[2].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(parsed.Operations[2].Item, Is.EqualTo("Line2"));
        }

        /// <summary>
        /// Tests that AddLiteral with multiline text splits into correct operations.
        /// </summary>
        [Test]
        public void AddLiteral_MultilineText_SplitsIntoCorrectOperations()
        {
            // Arrange
            var parsed = new ParsedTemplateString(20, 0);
            const string multilineText = "Line1\nLine2\nLine3";

            // Act
            parsed.AddLiteral(multilineText);

            // Assert
            var operations = parsed.Operations.ToList();
            var literalOps = operations.Where(op => op.Type == ParsedTemplateString.OpType.Literal).ToList();
            var lineBreakOps = operations.Where(op => op.Type == ParsedTemplateString.OpType.LineBreak).ToList();

            Assert.That(literalOps, Has.Count.EqualTo(3));
            Assert.That(lineBreakOps, Has.Count.EqualTo(2));
            Assert.That(literalOps[0].Item, Is.EqualTo("Line1"));
            Assert.That(literalOps[1].Item, Is.EqualTo("Line2"));
            Assert.That(literalOps[2].Item, Is.EqualTo("Line3"));
        }

        /// <summary>
        /// Tests that AddLiteral with consecutive line breaks creates correct operations.
        /// </summary>
        [Test]
        public void AddLiteral_ConsecutiveLineBreaks_CreatesMultipleLineBreakOperations()
        {
            // Arrange
            var parsed = new ParsedTemplateString(20, 0);
            const string text = "Line1\n\nLine2";

            // Act
            parsed.AddLiteral(text);

            // Assert
            Assert.That(parsed.Operations.Count, Is.EqualTo(4));
            Assert.That(parsed.Operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(parsed.Operations[0].Item, Is.EqualTo("Line1"));
            Assert.That(parsed.Operations[1].Type, Is.EqualTo(ParsedTemplateString.OpType.LineBreak));
            Assert.That(parsed.Operations[2].Type, Is.EqualTo(ParsedTemplateString.OpType.LineBreak));
            Assert.That(parsed.Operations[3].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(parsed.Operations[3].Item, Is.EqualTo("Line2"));
        }

        /// <summary>
        /// Tests that AddLiteral with line break at start creates LineBreak operation first.
        /// </summary>
        [Test]
        public void AddLiteral_LineBreakAtStart_CreatesLineBreakFirst()
        {
            // Arrange
            var parsed = new ParsedTemplateString(20, 0);
            const string text = "\nLine1";

            // Act
            parsed.AddLiteral(text);

            // Assert
            Assert.That(parsed.Operations.Count, Is.EqualTo(2));
            Assert.That(parsed.Operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.LineBreak));
            Assert.That(parsed.Operations[1].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(parsed.Operations[1].Item, Is.EqualTo("Line1"));
        }

        /// <summary>
        /// Tests that AddLiteral with line break at end creates LineBreak operation last.
        /// </summary>
        [Test]
        public void AddLiteral_LineBreakAtEnd_CreatesLineBreakLast()
        {
            // Arrange
            var parsed = new ParsedTemplateString(20, 0);
            const string text = "Line1\n";

            // Act
            parsed.AddLiteral(text);

            // Assert
            Assert.That(parsed.Operations.Count, Is.EqualTo(2));
            Assert.That(parsed.Operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(parsed.Operations[0].Item, Is.EqualTo("Line1"));
            Assert.That(parsed.Operations[1].Type, Is.EqualTo(ParsedTemplateString.OpType.LineBreak));
        }

        /// <summary>
        /// Tests that AddLiteral with only line breaks creates only LineBreak operations.
        /// </summary>
        [Test]
        public void AddLiteral_OnlyLineBreaks_CreatesOnlyLineBreakOperations()
        {
            // Arrange
            var parsed = new ParsedTemplateString(20, 0);
            const string text = "\n\n\n";

            // Act
            parsed.AddLiteral(text);

            // Assert
            Assert.That(parsed.Operations.Count, Is.EqualTo(3));
            Assert.That(parsed.Operations.All(op => op.Type == ParsedTemplateString.OpType.LineBreak), Is.True);
        }

        /// <summary>
        /// Tests that AddLiteral tracks offset correctly across operations.
        /// </summary>
        [Test]
        public void AddLiteral_MultipleOperations_TracksOffsetCorrectly()
        {
            // Arrange
            var parsed = new ParsedTemplateString(50, 0);
            const string text = "Hello World Test";

            // Act
            parsed.AddLiteral(text);

            // Assert
            Assert.That(parsed.Operations.Count, Is.EqualTo(1));
            Assert.That(parsed.Operations[0].Offset, Is.EqualTo(0));

            // Add another literal
            parsed.AddLiteral(" More");

            Assert.That(parsed.Operations.Count, Is.EqualTo(2));
            Assert.That(parsed.Operations[1].Offset, Is.EqualTo(16));
        }

        /// <summary>
        /// Tests that AddLiteral resets offset at line breaks.
        /// </summary>
        [Test]
        public void AddLiteral_LineBreak_ResetsOffset()
        {
            // Arrange
            var parsed = new ParsedTemplateString(50, 0);
            const string text = "Hello\nWorld";

            // Act
            parsed.AddLiteral(text);

            // Assert
            Assert.That(parsed.Operations.Count, Is.EqualTo(3));
            Assert.That(parsed.Operations[0].Offset, Is.EqualTo(0)); // "Hello"
            Assert.That(parsed.Operations[1].Offset, Is.EqualTo(5)); // LineBreak at offset 5
            Assert.That(parsed.Operations[2].Offset, Is.EqualTo(0)); // "World" starts at 0 on new line
        }

        /// <summary>
        /// Tests that AddLiteral tracks line numbers correctly across line breaks.
        /// </summary>
        [Test]
        public void AddLiteral_MultipleLineBreaks_TracksLineNumberCorrectly()
        {
            // Arrange
            var parsed = new ParsedTemplateString(50, 0);
            const string text = "Line0\nLine1\nLine2";

            // Act
            parsed.AddLiteral(text);

            // Assert
            Assert.That(parsed.Operations[0].LineNumber, Is.EqualTo(0)); // "Line0"
            Assert.That(parsed.Operations[1].LineNumber, Is.EqualTo(0)); // First LineBreak
            Assert.That(parsed.Operations[2].LineNumber, Is.EqualTo(1)); // "Line1"
            Assert.That(parsed.Operations[3].LineNumber, Is.EqualTo(1)); // Second LineBreak
            Assert.That(parsed.Operations[4].LineNumber, Is.EqualTo(2)); // "Line2"
        }

        /// <summary>
        /// Tests that AddLiteral with mixed Windows and Unix line endings handles both correctly.
        /// </summary>
        [Test]
        public void AddLiteral_MixedLineEndings_HandlesCorrectly()
        {
            // Arrange
            var parsed = new ParsedTemplateString(50, 0);
            const string text = "Line1\r\nLine2\nLine3";

            // Act
            parsed.AddLiteral(text);

            // Assert
            var operations = parsed.Operations.ToList();
            var literalOps = operations.Where(op => op.Type == ParsedTemplateString.OpType.Literal).ToList();
            var lineBreakOps = operations.Where(op => op.Type == ParsedTemplateString.OpType.LineBreak).ToList();

            Assert.That(literalOps, Has.Count.EqualTo(3));
            Assert.That(lineBreakOps, Has.Count.EqualTo(2));
            Assert.That(literalOps[0].Item, Is.EqualTo("Line1"));
            Assert.That(literalOps[1].Item, Is.EqualTo("Line2"));
            Assert.That(literalOps[2].Item, Is.EqualTo("Line3"));
        }

        /// <summary>
        /// Tests that AddLiteral with carriage return not followed by newline treats it as regular character.
        /// </summary>
        [Test]
        public void AddLiteral_CarriageReturnNotFollowedByNewline_TreatsAsRegularCharacter()
        {
            // Arrange
            var parsed = new ParsedTemplateString(20, 0);
            const string text = "Line1\rLine2";

            // Act
            parsed.AddLiteral(text);

            // Assert
            Assert.That(parsed.Operations.Count, Is.EqualTo(1));
            Assert.That(parsed.Operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(parsed.Operations[0].Item, Is.EqualTo("Line1\rLine2"));
        }

        /// <summary>
        /// Tests that AddLiteral with text containing spaces but not only spaces creates Literal operation.
        /// </summary>
        [Test]
        public void AddLiteral_TextWithSpaces_CreatesLiteralOperation()
        {
            // Arrange
            var parsed = new ParsedTemplateString(20, 0);
            const string text = "Hello World";

            // Act
            parsed.AddLiteral(text);

            // Assert
            Assert.That(parsed.Operations.Count, Is.EqualTo(1));
            Assert.That(parsed.Operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(parsed.Operations[0].Item, Is.EqualTo(text));
        }

        /// <summary>
        /// Tests that AddLiteral with whitespace between text on different lines classifies correctly.
        /// </summary>
        [Test]
        public void AddLiteral_WhitespaceBetweenLines_ClassifiesCorrectly()
        {
            // Arrange
            var parsed = new ParsedTemplateString(50, 0);
            const string text = "Text1\n   \nText2";

            // Act
            parsed.AddLiteral(text);

            // Assert
            Assert.That(parsed.Operations.Count, Is.EqualTo(5));
            Assert.That(parsed.Operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(parsed.Operations[0].Item, Is.EqualTo("Text1"));
            Assert.That(parsed.Operations[1].Type, Is.EqualTo(ParsedTemplateString.OpType.LineBreak));
            Assert.That(parsed.Operations[2].Type, Is.EqualTo(ParsedTemplateString.OpType.WhiteSpace));
            Assert.That(parsed.Operations[2].Item, Is.EqualTo("   "));
            Assert.That(parsed.Operations[3].Type, Is.EqualTo(ParsedTemplateString.OpType.LineBreak));
            Assert.That(parsed.Operations[4].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(parsed.Operations[4].Item, Is.EqualTo("Text2"));
        }

        /// <summary>
        /// Tests that AddLiteral with very long string handles correctly.
        /// </summary>
        [Test]
        public void AddLiteral_VeryLongString_HandlesCorrectly()
        {
            // Arrange
            var parsed = new ParsedTemplateString(10000, 0);
            string longText = new('A', 5000);

            // Act
            parsed.AddLiteral(longText);

            // Assert
            Assert.That(parsed.Operations.Count, Is.EqualTo(1));
            Assert.That(parsed.Operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(parsed.Operations[0].Item, Is.EqualTo(longText));
            Assert.That(parsed.Operations[0].Item.Length, Is.EqualTo(5000));
        }

        /// <summary>
        /// Tests that AddLiteral with special characters creates Literal operation.
        /// </summary>
        [TestCase("Hello\tWorld")]
        [TestCase("Test@#$%")]
        [TestCase("Unicode™©®")]
        [TestCase("Emoji😀")]
        public void AddLiteral_SpecialCharacters_CreatesLiteralOperation(string text)
        {
            // Arrange
            var parsed = new ParsedTemplateString(50, 0);

            // Act
            parsed.AddLiteral(text);

            // Assert
            Assert.That(parsed.Operations.Count, Is.GreaterThanOrEqualTo(1));
            var literalOps = parsed.Operations.Where(op => op.Type == ParsedTemplateString.OpType.Literal).ToList();
            Assert.That(literalOps, Has.Count.GreaterThanOrEqualTo(1));
        }

        /// <summary>
        /// Tests that AddLiteral with single character creates Literal operation.
        /// </summary>
        [TestCase("A")]
        [TestCase("1")]
        [TestCase("@")]
        public void AddLiteral_SingleCharacter_CreatesLiteralOperation(string text)
        {
            // Arrange
            var parsed = new ParsedTemplateString(10, 0);

            // Act
            parsed.AddLiteral(text);

            // Assert
            Assert.That(parsed.Operations.Count, Is.EqualTo(1));
            Assert.That(parsed.Operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(parsed.Operations[0].Item, Is.EqualTo(text));
        }

        /// <summary>
        /// Tests that AddLiteral with single space creates WhiteSpace operation.
        /// </summary>
        [Test]
        public void AddLiteral_SingleSpace_CreatesWhiteSpaceOperation()
        {
            // Arrange
            var parsed = new ParsedTemplateString(10, 0);

            // Act
            parsed.AddLiteral(" ");

            // Assert
            Assert.That(parsed.Operations.Count, Is.EqualTo(1));
            Assert.That(parsed.Operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.WhiteSpace));
            Assert.That(parsed.Operations[0].Item, Is.EqualTo(" "));
        }

        /// <summary>
        /// Tests that multiple calls to AddLiteral accumulate operations correctly.
        /// </summary>
        [Test]
        public void AddLiteral_MultipleCalls_AccumulatesOperationsCorrectly()
        {
            // Arrange
            var parsed = new ParsedTemplateString(50, 0);

            // Act
            parsed.AddLiteral("First");
            parsed.AddLiteral(" ");
            parsed.AddLiteral("Second");

            // Assert
            Assert.That(parsed.Operations.Count, Is.EqualTo(3));
            Assert.That(parsed.Operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(parsed.Operations[0].Item, Is.EqualTo("First"));
            Assert.That(parsed.Operations[1].Type, Is.EqualTo(ParsedTemplateString.OpType.WhiteSpace));
            Assert.That(parsed.Operations[1].Item, Is.EqualTo(" "));
            Assert.That(parsed.Operations[2].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(parsed.Operations[2].Item, Is.EqualTo("Second"));
        }

        /// <summary>
        /// Tests that AddLiteral with carriage return at end not followed by newline includes it.
        /// </summary>
        [Test]
        public void AddLiteral_CarriageReturnAtEnd_IncludesInLiteral()
        {
            // Arrange
            var parsed = new ParsedTemplateString(20, 0);
            const string text = "Line1\r";

            // Act
            parsed.AddLiteral(text);

            // Assert
            Assert.That(parsed.Operations.Count, Is.EqualTo(1));
            Assert.That(parsed.Operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(parsed.Operations[0].Item, Is.EqualTo("Line1\r"));
        }

        /// <summary>
        /// Tests that AddLiteral with only carriage return creates Literal operation.
        /// </summary>
        [Test]
        public void AddLiteral_OnlyCarriageReturn_CreatesLiteralOperation()
        {
            // Arrange
            var parsed = new ParsedTemplateString(10, 0);
            const string text = "\r";

            // Act
            parsed.AddLiteral(text);

            // Assert
            Assert.That(parsed.Operations.Count, Is.EqualTo(1));
            Assert.That(parsed.Operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(parsed.Operations[0].Item, Is.EqualTo("\r"));
        }

        /// <summary>
        /// Tests that AddLiteral with tab character creates Literal operation.
        /// </summary>
        [Test]
        public void AddLiteral_TabCharacter_CreatesLiteralOperation()
        {
            // Arrange
            var parsed = new ParsedTemplateString(10, 0);
            const string text = "\t";

            // Act
            parsed.AddLiteral(text);

            // Assert
            Assert.That(parsed.Operations.Count, Is.EqualTo(1));
            Assert.That(parsed.Operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(parsed.Operations[0].Item, Is.EqualTo("\t"));
        }

        /// <summary>
        /// Tests that AddLiteral preserves offset across multiple calls with no line breaks.
        /// </summary>
        [Test]
        public void AddLiteral_MultipleCallsNoLineBreaks_PreservesOffsetContinuity()
        {
            // Arrange
            var parsed = new ParsedTemplateString(50, 0);

            // Act
            parsed.AddLiteral("Hello");  // Offset 0, length 5
            parsed.AddLiteral("World");  // Offset 5, length 5

            // Assert
            Assert.That(parsed.Operations.Count, Is.EqualTo(2));
            Assert.That(parsed.Operations[0].Offset, Is.EqualTo(0));
            Assert.That(parsed.Operations[1].Offset, Is.EqualTo(5));
        }

        /// <summary>
        /// Tests that AddLiteral with line break in the middle correctly splits and tracks offsets.
        /// </summary>
        [Test]
        public void AddLiteral_LineBreakInMiddle_SplitsAndTracksOffsetsCorrectly()
        {
            // Arrange
            var parsed = new ParsedTemplateString(50, 0);
            const string text = "ABC\nDEF";

            // Act
            parsed.AddLiteral(text);

            // Assert
            Assert.That(parsed.Operations.Count, Is.EqualTo(3));
            Assert.That(parsed.Operations[0].Offset, Is.EqualTo(0)); // "ABC" at offset 0
            Assert.That(parsed.Operations[1].Offset, Is.EqualTo(3)); // LineBreak at offset 3
            Assert.That(parsed.Operations[2].Offset, Is.EqualTo(0)); // "DEF" at offset 0 (new line)
        }

        /// <summary>
        /// Tests that Format returns an empty string when no operations are added.
        /// </summary>
        [Test]
        public void Format_NoOperations_ReturnsEmptyString()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(0, 0);

            // Act
            string result = parsedTemplate.Format;

            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that Format returns the correct concatenation of a single literal operation.
        /// </summary>
        [Test]
        public void Format_SingleLiteral_ReturnsLiteralValue()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(1, 0);
            parsedTemplate.AddLiteral("Hello");

            // Act
            string result = parsedTemplate.Format;

            // Assert
            Assert.That(result, Is.EqualTo("Hello"));
        }

        /// <summary>
        /// Tests that Format returns the correct concatenation of multiple literal operations.
        /// </summary>
        [Test]
        public void Format_MultipleLiterals_ReturnsConcatenatedValue()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(2, 0);
            parsedTemplate.AddLiteral("Hello");
            parsedTemplate.AddLiteral("World");

            // Act
            string result = parsedTemplate.Format;

            // Assert
            Assert.That(result, Is.EqualTo("HelloWorld"));
        }

        /// <summary>
        /// Tests that Format returns the correct concatenation when operations include formatted values.
        /// </summary>
        [Test]
        public void Format_WithFormattedValue_ReturnsConcatenatedValue()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(2, 1);
            parsedTemplate.AddLiteral("Value: ");
            parsedTemplate.AddFormatted("42", typeof(int));

            // Act
            string result = parsedTemplate.Format;

            // Assert
            Assert.That(result, Is.EqualTo("Value: 42"));
        }

        /// <summary>
        /// Tests that Format returns the correct concatenation when operations include string tokens.
        /// </summary>
        [Test]
        public void Format_WithStringToken_ReturnsConcatenatedValue()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(2, 1);
            parsedTemplate.AddLiteral("Name: ");
            parsedTemplate.AddFormatted("John", typeof(string));

            // Act
            string result = parsedTemplate.Format;

            // Assert
            Assert.That(result, Is.EqualTo("Name: John"));
        }

        /// <summary>
        /// Tests that Format returns the correct concatenation when operations include line breaks.
        /// </summary>
        [Test]
        public void Format_WithLineBreaks_ReturnsConcatenatedValue()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(3, 0);
            parsedTemplate.AddLiteral("Line1\nLine2\nLine3");

            // Act
            string result = parsedTemplate.Format;

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Contains("Line1", StringComparison.Ordinal), Is.True);
            Assert.That(result.Contains("Line2", StringComparison.Ordinal), Is.True);
            Assert.That(result.Contains("Line3", StringComparison.Ordinal), Is.True);
        }

        /// <summary>
        /// Tests that Format returns the correct concatenation when operations include whitespace.
        /// </summary>
        [Test]
        public void Format_WithWhitespace_ReturnsConcatenatedValue()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(3, 0);
            parsedTemplate.AddLiteral("   ");
            parsedTemplate.AddLiteral("Text");
            parsedTemplate.AddLiteral("   ");

            // Act
            string result = parsedTemplate.Format;

            // Assert
            Assert.That(result, Is.EqualTo("   Text   "));
        }

        /// <summary>
        /// Tests that Format returns the correct concatenation with empty string literals.
        /// </summary>
        [Test]
        public void Format_WithEmptyLiterals_ReturnsExpectedValue()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(3, 0);
            parsedTemplate.AddLiteral(string.Empty);
            parsedTemplate.AddLiteral("Middle");
            parsedTemplate.AddLiteral(string.Empty);

            // Act
            string result = parsedTemplate.Format;

            // Assert
            Assert.That(result, Is.EqualTo("Middle"));
        }

        /// <summary>
        /// Tests that Format returns the correct concatenation when operations are mixed (literals, values, whitespace).
        /// </summary>
        [Test]
        public void Format_MixedOperations_ReturnsConcatenatedValue()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(5, 2);
            parsedTemplate.AddLiteral("Start ");
            parsedTemplate.AddFormatted("Value1", typeof(string));
            parsedTemplate.AddLiteral(" Middle ");
            parsedTemplate.AddFormatted("Value2", typeof(int));
            parsedTemplate.AddLiteral(" End");

            // Act
            string result = parsedTemplate.Format;

            // Assert
            Assert.That(result, Is.EqualTo("Start Value1 Middle Value2 End"));
        }

        /// <summary>
        /// Tests that Format handles special characters correctly.
        /// </summary>
        [Test]
        public void Format_WithSpecialCharacters_ReturnsConcatenatedValue()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(3, 0);
            parsedTemplate.AddLiteral("Special: \t\r");
            parsedTemplate.AddLiteral("chars!");
            parsedTemplate.AddLiteral("@#$%");

            // Act
            string result = parsedTemplate.Format;

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Contains("Special:", StringComparison.Ordinal), Is.True);
            Assert.That(result.Contains("chars!", StringComparison.Ordinal), Is.True);
            Assert.That(result.Contains("@#$%", StringComparison.Ordinal), Is.True);
        }

        /// <summary>
        /// Tests that Format handles long strings correctly.
        /// </summary>
        [Test]
        public void Format_WithLongString_ReturnsConcatenatedValue()
        {
            // Arrange
            string longString = new('A', 10000);
            var parsedTemplate = new ParsedTemplateString(1, 0);
            parsedTemplate.AddLiteral(longString);

            // Act
            string result = parsedTemplate.Format;

            // Assert
            Assert.That(result, Is.EqualTo(longString));
            Assert.That(result.Length, Is.EqualTo(10000));
        }

        /// <summary>
        /// Tests that Format returns the correct concatenation using FromString factory method.
        /// </summary>
        [Test]
        public void Format_FromString_ReturnsConcatenatedValue()
        {
            // Arrange
            var parsedTemplate = ParsedTemplateString.FromString("Simple text");

            // Act
            string result = parsedTemplate.Format;

            // Assert
            Assert.That(result, Is.EqualTo("Simple text"));
        }

        /// <summary>
        /// Tests that Format returns empty string when FromString is called with empty string.
        /// </summary>
        [Test]
        public void Format_FromEmptyString_ReturnsEmptyString()
        {
            // Arrange
            var parsedTemplate = ParsedTemplateString.FromString(string.Empty);

            // Act
            string result = parsedTemplate.Format;

            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that Format handles unicode characters correctly.
        /// </summary>
        [Test]
        public void Format_WithUnicodeCharacters_ReturnsConcatenatedValue()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(1, 0);
            parsedTemplate.AddLiteral("Unicode: 你好世界 🌍");

            // Act
            string result = parsedTemplate.Format;

            // Assert
            Assert.That(result, Is.EqualTo("Unicode: 你好世界 🌍"));
        }

        /// <summary>
        /// Tests that Format returns the correct concatenation with multiple formatted values.
        /// </summary>
        [Test]
        public void Format_MultipleFormattedValues_ReturnsConcatenatedValue()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(0, 5);
            parsedTemplate.AddFormatted("1", typeof(int));
            parsedTemplate.AddFormatted("2", typeof(int));
            parsedTemplate.AddFormatted("3", typeof(int));
            parsedTemplate.AddFormatted("4", typeof(int));
            parsedTemplate.AddFormatted("5", typeof(int));

            // Act
            string result = parsedTemplate.Format;

            // Assert
            Assert.That(result, Is.EqualTo("12345"));
        }

        /// <summary>
        /// Tests that Format handles Windows line endings correctly.
        /// </summary>
        [Test]
        public void Format_WithWindowsLineEndings_ReturnsConcatenatedValue()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(1, 0);
            parsedTemplate.AddLiteral("Line1\r\nLine2");

            // Act
            string result = parsedTemplate.Format;

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Contains("Line1", StringComparison.Ordinal), Is.True);
            Assert.That(result.Contains("Line2", StringComparison.Ordinal), Is.True);
        }

        /// <summary>
        /// Tests that Format returns consistent results when called multiple times.
        /// </summary>
        [Test]
        public void Format_CalledMultipleTimes_ReturnsSameValue()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(2, 1);
            parsedTemplate.AddLiteral("Test ");
            parsedTemplate.AddFormatted("Value", typeof(string));

            // Act
            string result1 = parsedTemplate.Format;
            string result2 = parsedTemplate.Format;

            // Assert
            Assert.That(result1, Is.EqualTo(result2));
            Assert.That(result1, Is.EqualTo("Test Value"));
        }

        /// <summary>
        /// Tests that ArgumentCount returns 0 for an empty ParsedTemplateString with no operations.
        /// </summary>
        [Test]
        public void ArgumentCount_EmptyParsedTemplateString_ReturnsZero()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(0, 0);

            // Act
            int argumentCount = parsedTemplate.ArgumentCount;

            // Assert
            Assert.That(argumentCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that ArgumentCount returns 0 for a ParsedTemplateString created from a plain string literal.
        /// </summary>
        [Test]
        public void ArgumentCount_FromStringWithLiteralsOnly_ReturnsZero()
        {
            // Arrange
            var parsedTemplate = ParsedTemplateString.FromString("Hello World");

            // Act
            int argumentCount = parsedTemplate.ArgumentCount;

            // Assert
            Assert.That(argumentCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that ArgumentCount returns 0 for a ParsedTemplateString created from an empty string.
        /// </summary>
        [Test]
        public void ArgumentCount_FromEmptyString_ReturnsZero()
        {
            // Arrange
            var parsedTemplate = ParsedTemplateString.FromString(string.Empty);

            // Act
            int argumentCount = parsedTemplate.ArgumentCount;

            // Assert
            Assert.That(argumentCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that ArgumentCount returns 0 for a ParsedTemplateString with only whitespace.
        /// </summary>
        [Test]
        public void ArgumentCount_OnlyWhitespace_ReturnsZero()
        {
            // Arrange
            var parsedTemplate = ParsedTemplateString.FromString("   \t   ");

            // Act
            int argumentCount = parsedTemplate.ArgumentCount;

            // Assert
            Assert.That(argumentCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that ArgumentCount returns 0 for a ParsedTemplateString with line breaks.
        /// </summary>
        [Test]
        public void ArgumentCount_WithLineBreaks_ReturnsZero()
        {
            // Arrange
            var parsedTemplate = ParsedTemplateString.FromString("Line1\nLine2\nLine3");

            // Act
            int argumentCount = parsedTemplate.ArgumentCount;

            // Assert
            Assert.That(argumentCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that ArgumentCount returns 1 for a ParsedTemplateString with a single formatted non-string value.
        /// </summary>
        [Test]
        public void ArgumentCount_SingleFormattedValue_ReturnsOne()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(0, 1);
            parsedTemplate.AddFormatted("42", typeof(int));

            // Act
            int argumentCount = parsedTemplate.ArgumentCount;

            // Assert
            Assert.That(argumentCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Tests that ArgumentCount returns 1 for a ParsedTemplateString with a single formatted string token.
        /// </summary>
        [Test]
        public void ArgumentCount_SingleFormattedToken_ReturnsOne()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(0, 1);
            parsedTemplate.AddFormatted("token", typeof(string));

            // Act
            int argumentCount = parsedTemplate.ArgumentCount;

            // Assert
            Assert.That(argumentCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Tests that ArgumentCount returns the correct count for multiple formatted items.
        /// </summary>
        [TestCase(2)]
        [TestCase(5)]
        [TestCase(10)]
        public void ArgumentCount_MultipleFormattedItems_ReturnsCorrectCount(int count)
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(0, count);
            for (int i = 0; i < count; i++)
            {
                parsedTemplate.AddFormatted(i.ToString(CultureInfo.InvariantCulture), typeof(int));
            }

            // Act
            int argumentCount = parsedTemplate.ArgumentCount;

            // Assert
            Assert.That(argumentCount, Is.EqualTo(count));
        }

        /// <summary>
        /// Tests that ArgumentCount correctly counts only formatted items when mixed with literals.
        /// </summary>
        [Test]
        public void ArgumentCount_MixedLiteralsAndFormattedItems_ReturnsFormattedItemsCount()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(10, 3);
            parsedTemplate.AddLiteral("Hello ");
            parsedTemplate.AddFormatted("World", typeof(string));
            parsedTemplate.AddLiteral(" and ");
            parsedTemplate.AddFormatted("42", typeof(int));
            parsedTemplate.AddLiteral(" items");
            parsedTemplate.AddFormatted("end", typeof(string));

            // Act
            int argumentCount = parsedTemplate.ArgumentCount;

            // Assert
            Assert.That(argumentCount, Is.EqualTo(3));
        }

        /// <summary>
        /// Tests that ArgumentCount returns consistent results across multiple calls.
        /// </summary>
        [Test]
        public void ArgumentCount_MultipleAccesses_ReturnsConsistentValue()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(5, 2);
            parsedTemplate.AddLiteral("Test ");
            parsedTemplate.AddFormatted("value", typeof(string));
            parsedTemplate.AddLiteral(" and ");
            parsedTemplate.AddFormatted("123", typeof(int));

            // Act
            int firstAccess = parsedTemplate.ArgumentCount;
            int secondAccess = parsedTemplate.ArgumentCount;
            int thirdAccess = parsedTemplate.ArgumentCount;

            // Assert
            Assert.That(firstAccess, Is.EqualTo(2));
            Assert.That(secondAccess, Is.EqualTo(2));
            Assert.That(thirdAccess, Is.EqualTo(2));
            Assert.That(firstAccess, Is.EqualTo(secondAccess));
            Assert.That(secondAccess, Is.EqualTo(thirdAccess));
        }

        /// <summary>
        /// Tests that ArgumentCount handles a large number of formatted items correctly.
        /// </summary>
        [Test]
        public void ArgumentCount_LargeNumberOfFormattedItems_ReturnsCorrectCount()
        {
            // Arrange
            const int largeCount = 1000;
            var parsedTemplate = new ParsedTemplateString(0, largeCount);
            for (int i = 0; i < largeCount; i++)
            {
                parsedTemplate.AddFormatted(i.ToString(CultureInfo.InvariantCulture), typeof(int));
            }

            // Act
            int argumentCount = parsedTemplate.ArgumentCount;

            // Assert
            Assert.That(argumentCount, Is.EqualTo(largeCount));
        }

        /// <summary>
        /// Tests that ArgumentCount returns correct value when mixing different formatted types.
        /// </summary>
        [Test]
        public void ArgumentCount_MixedFormattedTypes_ReturnsCorrectCount()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(0, 5);
            parsedTemplate.AddFormatted("text", typeof(string));
            parsedTemplate.AddFormatted("42", typeof(int));
            parsedTemplate.AddFormatted("3.14", typeof(double));
            parsedTemplate.AddFormatted("True", typeof(bool));
            parsedTemplate.AddFormatted("X", typeof(char));

            // Act
            int argumentCount = parsedTemplate.ArgumentCount;

            // Assert
            Assert.That(argumentCount, Is.EqualTo(5));
        }

        /// <summary>
        /// Tests that ArgumentCount returns 0 for a ParsedTemplateString with only whitespace and line breaks.
        /// </summary>
        [Test]
        public void ArgumentCount_OnlyWhitespaceAndLineBreaks_ReturnsZero()
        {
            // Arrange
            var parsedTemplate = ParsedTemplateString.FromString("  \n  \n  ");

            // Act
            int argumentCount = parsedTemplate.ArgumentCount;

            // Assert
            Assert.That(argumentCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that the constructor correctly initializes properties with typical positive values.
        /// Input: literalLength = 10, formattedCount = 5
        /// Expected: Properties are assigned correctly and Operations list is initialized.
        /// </summary>
        [TestCase(10, 5)]
        [TestCase(1, 1)]
        [TestCase(100, 50)]
        [TestCase(1000, 2000)]
        public void Constructor_WithPositiveValues_InitializesPropertiesCorrectly(int literalLength, int formattedCount)
        {
            // Arrange & Act
            var parsed = new ParsedTemplateString(literalLength, formattedCount);

            // Assert
            Assert.That(parsed.LiteralLength, Is.EqualTo(literalLength));
            Assert.That(parsed.FormattedCount, Is.EqualTo(formattedCount));
            Assert.That(parsed.Operations, Is.Not.Null);
            Assert.That(parsed.Operations, Is.Empty);
        }

        /// <summary>
        /// Tests that the constructor handles zero values for both parameters.
        /// Input: literalLength = 0, formattedCount = 0
        /// Expected: Properties are set to zero and Operations list is initialized as empty.
        /// </summary>
        [Test]
        public void Constructor_WithZeroValues_InitializesPropertiesCorrectly()
        {
            // Arrange
            const int literalLength = 0;
            const int formattedCount = 0;

            // Act
            var parsed = new ParsedTemplateString(literalLength, formattedCount);

            // Assert
            Assert.That(parsed.LiteralLength, Is.EqualTo(0));
            Assert.That(parsed.FormattedCount, Is.EqualTo(0));
            Assert.That(parsed.Operations, Is.Not.Null);
            Assert.That(parsed.Operations, Is.Empty);
        }

        /// <summary>
        /// Tests that the constructor handles one parameter being zero.
        /// Input: literalLength = 0, formattedCount = 5
        /// Expected: Properties are assigned correctly.
        /// </summary>
        [TestCase(0, 5)]
        [TestCase(10, 0)]
        public void Constructor_WithOneZeroValue_InitializesPropertiesCorrectly(int literalLength, int formattedCount)
        {
            // Arrange & Act
            var parsed = new ParsedTemplateString(literalLength, formattedCount);

            // Assert
            Assert.That(parsed.LiteralLength, Is.EqualTo(literalLength));
            Assert.That(parsed.FormattedCount, Is.EqualTo(formattedCount));
            Assert.That(parsed.Operations, Is.Not.Null);
        }

        /// <summary>
        /// Tests that the constructor throws ArgumentOutOfRangeException when literalLength is negative.
        /// Input: literalLength = -1, formattedCount = 5
        /// Expected: ArgumentOutOfRangeException is thrown from List constructor.
        /// </summary>
        [Test]
        public void Constructor_WithNegativeLiteralLength_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            const int literalLength = -1;
            const int formattedCount = 5;

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => new ParsedTemplateString(literalLength, formattedCount));
        }

        /// <summary>
        /// Tests that the constructor throws ArgumentOutOfRangeException when formattedCount is negative.
        /// Input: literalLength = 10, formattedCount = -1
        /// Expected: ArgumentOutOfRangeException is thrown from List constructor.
        /// </summary>
        [Test]
        public void Constructor_WithNegativeFormattedCount_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            const int literalLength = 10;
            const int formattedCount = -1;

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => new ParsedTemplateString(literalLength, formattedCount));
        }

        /// <summary>
        /// Tests that the constructor throws ArgumentOutOfRangeException when both parameters are negative.
        /// Input: literalLength = -5, formattedCount = -10
        /// Expected: ArgumentOutOfRangeException is thrown from List constructor.
        /// </summary>
        [Test]
        public void Constructor_WithBothNegativeValues_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            const int literalLength = -5;
            const int formattedCount = -10;

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => new ParsedTemplateString(literalLength, formattedCount));
        }

        /// <summary>
        /// Tests that the constructor throws ArgumentOutOfRangeException when literalLength is int.MinValue.
        /// Input: literalLength = int.MinValue, formattedCount = 0
        /// Expected: ArgumentOutOfRangeException is thrown from List constructor.
        /// </summary>
        [Test]
        public void Constructor_WithMinValueLiteralLength_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            const int literalLength = int.MinValue;
            const int formattedCount = 0;

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => new ParsedTemplateString(literalLength, formattedCount));
        }

        /// <summary>
        /// Tests that the constructor throws ArgumentOutOfRangeException when formattedCount is int.MinValue.
        /// Input: literalLength = 0, formattedCount = int.MinValue
        /// Expected: ArgumentOutOfRangeException is thrown from List constructor.
        /// </summary>
        [Test]
        public void Constructor_WithMinValueFormattedCount_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            const int literalLength = 0;
            const int formattedCount = int.MinValue;

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => new ParsedTemplateString(literalLength, formattedCount));
        }

        /// <summary>
        /// Tests that the constructor throws ArgumentOutOfRangeException 16k exceeded
        /// </summary>
        [Test]
        public void Constructor_WithOverflowingSum_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            const int literalLength = ParsedTemplateString.MaxLiteralLength + 1;
            const int formattedCount = 1;

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => new ParsedTemplateString(literalLength, formattedCount));
        }

        /// <summary>
        /// Tests that the constructor throws OverflowException when both parameters are int.MaxValue.
        /// Input: literalLength = int.MaxValue, formattedCount = int.MaxValue
        /// Expected: OverflowException is thrown due to integer overflow.
        /// </summary>
        [Test]
        public void Constructor_WithBothMaxValues_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            const int literalLength = int.MaxValue;
            const int formattedCount = int.MaxValue;

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => new ParsedTemplateString(literalLength, formattedCount));
        }

        /// <summary>
        /// Tests that the constructor correctly handles int.MaxValue for literalLength when formattedCount is zero.
        /// Input: literalLength = int.MaxValue, formattedCount = 0
        /// Expected: Properties are assigned correctly and no exception is thrown.
        /// </summary>
        [Test]
        public void Constructor_WithMaxValueLiteralLengthAndZeroFormattedCount_InitializesCorrectly()
        {
            // Arrange
            const int literalLength = ParsedTemplateString.MaxLiteralLength;
            const int formattedCount = 0;

            // Act
            var parsed = new ParsedTemplateString(literalLength, formattedCount);

            // Assert
            Assert.That(parsed.LiteralLength, Is.EqualTo(ParsedTemplateString.MaxLiteralLength));
            Assert.That(parsed.FormattedCount, Is.EqualTo(0));
            Assert.That(parsed.Operations, Is.Not.Null);
        }

        /// <summary>
        /// Tests that the constructor correctly handles max count for formatted count.
        /// </summary>
        [Test]
        public void Constructor_WithZeroLiteralLengthAndMaxValueFormattedCount_InitializesCorrectly()
        {
            // Arrange
            const int literalLength = 0;
            const int formattedCount = ParsedTemplateString.MaxFormattedCount;

            // Act
            var parsed = new ParsedTemplateString(literalLength, formattedCount);

            // Assert
            Assert.That(parsed.LiteralLength, Is.EqualTo(0));
            Assert.That(parsed.FormattedCount, Is.EqualTo(ParsedTemplateString.MaxFormattedCount));
            Assert.That(parsed.Operations, Is.Not.Null);
        }

        /// <summary>
        /// Tests that the constructor correctly handles Max count const for each.
        /// </summary>
        [Test]
        public void Constructor_WithLargeNonOverflowingValues_InitializesCorrectly()
        {
            // Arrange
            const int literalLength = ParsedTemplateString.MaxLiteralLength;
            const int formattedCount = ParsedTemplateString.MaxFormattedCount;

            // Act
            var parsed = new ParsedTemplateString(literalLength, formattedCount);

            // Assert
            Assert.That(parsed.LiteralLength, Is.EqualTo(ParsedTemplateString.MaxLiteralLength));
            Assert.That(parsed.FormattedCount, Is.EqualTo(ParsedTemplateString.MaxFormattedCount));
            Assert.That(parsed.Operations, Is.Not.Null);
        }

        /// <summary>
        /// Tests that GetArgument returns the correct argument when a valid index is provided.
        /// Input: Valid index (0, 1, 2) within the bounds of the arguments array.
        /// Expected: Returns the correct argument value at the specified index.
        /// </summary>
        [TestCase(0, "FirstValue")]
        [TestCase(1, "SecondValue")]
        [TestCase(2, "ThirdValue")]
        public void GetArgument_ValidIndex_ReturnsCorrectArgument(int index, string expectedValue)
        {
            // Arrange
            var parsed = new ParsedTemplateString(0, 3);
            parsed.AddFormatted("FirstValue", typeof(string));
            parsed.AddFormatted("SecondValue", typeof(string));
            parsed.AddFormatted("ThirdValue", typeof(string));

            // Act
            object argument = parsed.GetArgument(index);

            // Assert
            Assert.That(argument, Is.EqualTo(expectedValue));
        }

        /// <summary>
        /// Tests that GetArgument throws IndexOutOfRangeException when an invalid index is provided.
        /// Input: Invalid index values (negative, equal to or greater than array length, extreme values).
        /// Expected: Throws IndexOutOfRangeException.
        /// </summary>
        [TestCase(-1)]
        [TestCase(-100)]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(100)]
        [TestCase(int.MinValue)]
        [TestCase(int.MaxValue)]
        public void GetArgument_InvalidIndex_ThrowsIndexOutOfRangeException(int invalidIndex)
        {
            // Arrange
            var parsed = new ParsedTemplateString(0, 0);

            // Act & Assert
            Assert.Throws<IndexOutOfRangeException>(() => parsed.GetArgument(invalidIndex));
        }

        /// <summary>
        /// Tests that GetArgument throws IndexOutOfRangeException when index equals the array length.
        /// Input: Index equal to the number of arguments.
        /// Expected: Throws IndexOutOfRangeException.
        /// </summary>
        [Test]
        public void GetArgument_IndexEqualToArrayLength_ThrowsIndexOutOfRangeException()
        {
            // Arrange
            var parsed = new ParsedTemplateString(0, 2);
            parsed.AddFormatted("Value1", typeof(string));
            parsed.AddFormatted("Value2", typeof(string));

            // Act & Assert
            Assert.Throws<IndexOutOfRangeException>(() => parsed.GetArgument(2));
        }

        /// <summary>
        /// Tests that GetArgument throws IndexOutOfRangeException when index is greater than array length.
        /// Input: Index greater than the number of arguments.
        /// Expected: Throws IndexOutOfRangeException.
        /// </summary>
        [Test]
        public void GetArgument_IndexGreaterThanArrayLength_ThrowsIndexOutOfRangeException()
        {
            // Arrange
            var parsed = new ParsedTemplateString(0, 1);
            parsed.AddFormatted("Value", typeof(string));

            // Act & Assert
            Assert.Throws<IndexOutOfRangeException>(() => parsed.GetArgument(5));
        }

        /// <summary>
        /// Tests that GetArgument returns the correct argument for different value types.
        /// Input: Valid index for non-string formatted values.
        /// Expected: Returns the correct argument value.
        /// </summary>
        [Test]
        public void GetArgument_NonStringValue_ReturnsCorrectArgument()
        {
            // Arrange
            var parsed = new ParsedTemplateString(0, 1);
            parsed.AddFormatted("123", typeof(int));

            // Act
            object argument = parsed.GetArgument(0);

            // Assert
            Assert.That(argument, Is.EqualTo("123"));
        }

        /// <summary>
        /// Tests that IsMultiLine returns false when no operations have been added to the ParsedTemplateString.
        /// </summary>
        [Test]
        public void IsMultiLine_EmptyOperations_ReturnsFalse()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(0, 0);

            // Act
            bool result = parsedTemplate.IsMultiLine;

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that IsMultiLine returns false when only literals without newlines are added.
        /// </summary>
        [Test]
        public void IsMultiLine_SingleLineLiteral_ReturnsFalse()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(10, 0);
            parsedTemplate.AddLiteral("Hello World");

            // Act
            bool result = parsedTemplate.IsMultiLine;

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that IsMultiLine returns false when only whitespace literals without newlines are added.
        /// </summary>
        [Test]
        public void IsMultiLine_WhitespaceOnly_ReturnsFalse()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(5, 0);
            parsedTemplate.AddLiteral("     ");

            // Act
            bool result = parsedTemplate.IsMultiLine;

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that IsMultiLine returns true when a literal contains a single newline character.
        /// </summary>
        [Test]
        public void IsMultiLine_LiteralWithSingleNewline_ReturnsTrue()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(20, 0);
            parsedTemplate.AddLiteral("First line\nSecond line");

            // Act
            bool result = parsedTemplate.IsMultiLine;

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that IsMultiLine returns true when a literal contains multiple newline characters.
        /// </summary>
        [Test]
        public void IsMultiLine_LiteralWithMultipleNewlines_ReturnsTrue()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(30, 0);
            parsedTemplate.AddLiteral("Line 1\nLine 2\nLine 3\nLine 4");

            // Act
            bool result = parsedTemplate.IsMultiLine;

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that IsMultiLine returns true when a literal contains Windows-style line endings (CRLF).
        /// </summary>
        [Test]
        public void IsMultiLine_LiteralWithWindowsNewline_ReturnsTrue()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(25, 0);
            parsedTemplate.AddLiteral("First line\r\nSecond line");

            // Act
            bool result = parsedTemplate.IsMultiLine;

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that IsMultiLine returns false when only formatted values are added without any newlines.
        /// </summary>
        [Test]
        public void IsMultiLine_OnlyFormattedValues_ReturnsFalse()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(0, 2);
            parsedTemplate.AddFormatted("value1", typeof(string));
            parsedTemplate.AddFormatted("123", typeof(int));

            // Act
            bool result = parsedTemplate.IsMultiLine;

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that IsMultiLine returns false when a mix of literals and formatted values are added without any newlines.
        /// </summary>
        [Test]
        public void IsMultiLine_MixedOperationsWithoutNewlines_ReturnsFalse()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(15, 1);
            parsedTemplate.AddLiteral("Hello ");
            parsedTemplate.AddFormatted("World", typeof(string));
            parsedTemplate.AddLiteral("!");

            // Act
            bool result = parsedTemplate.IsMultiLine;

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that IsMultiLine returns true when operations include at least one newline among literals and formatted values.
        /// </summary>
        [Test]
        public void IsMultiLine_MixedOperationsWithNewline_ReturnsTrue()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(20, 1);
            parsedTemplate.AddLiteral("First line\n");
            parsedTemplate.AddFormatted("value", typeof(string));
            parsedTemplate.AddLiteral("\nLast line");

            // Act
            bool result = parsedTemplate.IsMultiLine;

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that IsMultiLine returns true when a literal starts with a newline character.
        /// </summary>
        [Test]
        public void IsMultiLine_LiteralStartingWithNewline_ReturnsTrue()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(10, 0);
            parsedTemplate.AddLiteral("\nStarting text");

            // Act
            bool result = parsedTemplate.IsMultiLine;

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that IsMultiLine returns true when a literal ends with a newline character.
        /// </summary>
        [Test]
        public void IsMultiLine_LiteralEndingWithNewline_ReturnsTrue()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(10, 0);
            parsedTemplate.AddLiteral("Ending text\n");

            // Act
            bool result = parsedTemplate.IsMultiLine;

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that IsMultiLine returns true when a literal contains only a newline character.
        /// </summary>
        [Test]
        public void IsMultiLine_LiteralOnlyNewline_ReturnsTrue()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(1, 0);
            parsedTemplate.AddLiteral("\n");

            // Act
            bool result = parsedTemplate.IsMultiLine;

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that IsMultiLine returns false when an empty string literal is added.
        /// </summary>
        [Test]
        public void IsMultiLine_EmptyStringLiteral_ReturnsFalse()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(0, 0);
            parsedTemplate.AddLiteral(string.Empty);

            // Act
            bool result = parsedTemplate.IsMultiLine;

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that IsMultiLine returns false when multiple single-line literals are added.
        /// </summary>
        [Test]
        public void IsMultiLine_MultipleSingleLineLiterals_ReturnsFalse()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(30, 0);
            parsedTemplate.AddLiteral("First part");
            parsedTemplate.AddLiteral("Second part");
            parsedTemplate.AddLiteral("Third part");

            // Act
            bool result = parsedTemplate.IsMultiLine;

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that IsMultiLine returns true when one of multiple literals contains a newline.
        /// </summary>
        [Test]
        public void IsMultiLine_OneOfMultipleLiteralsWithNewline_ReturnsTrue()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(35, 0);
            parsedTemplate.AddLiteral("First part");
            parsedTemplate.AddLiteral("Second\npart");
            parsedTemplate.AddLiteral("Third part");

            // Act
            bool result = parsedTemplate.IsMultiLine;

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that IsMultiLine returns true when multiple literals each contain newlines.
        /// </summary>
        [Test]
        public void IsMultiLine_MultipleLiteralsWithNewlines_ReturnsTrue()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(40, 0);
            parsedTemplate.AddLiteral("First\npart");
            parsedTemplate.AddLiteral("Second\npart");
            parsedTemplate.AddLiteral("Third\npart");

            // Act
            bool result = parsedTemplate.IsMultiLine;

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that IsMultiLine returns true when consecutive newlines are present in a literal.
        /// </summary>
        [Test]
        public void IsMultiLine_ConsecutiveNewlines_ReturnsTrue()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(15, 0);
            parsedTemplate.AddLiteral("Text\n\n\nMore text");

            // Act
            bool result = parsedTemplate.IsMultiLine;

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that the Operations property returns an empty list when no operations have been added.
        /// Input: A newly constructed ParsedTemplateString with no operations.
        /// Expected: Returns an empty IReadOnlyList.
        /// </summary>
        [Test]
        public void Operations_NoOperationsAdded_ReturnsEmptyList()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(0, 0);

            // Act
            IReadOnlyList<ParsedTemplateString.Op> operations = parsedTemplate.Operations;

            // Assert
            Assert.That(operations, Is.Not.Null);
            Assert.That(operations, Is.Empty);
            Assert.That(operations.Count, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that the Operations property returns operations after AddLiteral is called.
        /// Input: A ParsedTemplateString with a single literal added.
        /// Expected: Returns a list containing one operation with the literal text.
        /// </summary>
        [Test]
        public void Operations_AfterAddLiteral_ReturnsOperationsList()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(10, 0);
            const string literalText = "Hello";

            // Act
            parsedTemplate.AddLiteral(literalText);
            IReadOnlyList<ParsedTemplateString.Op> operations = parsedTemplate.Operations;

            // Assert
            Assert.That(operations, Is.Not.Null);
            Assert.That(operations.Count, Is.EqualTo(1));
            Assert.That(operations[0].Item, Is.EqualTo(literalText));
        }

        /// <summary>
        /// Tests that the Operations property returns operations after AddFormatted is called.
        /// Input: A ParsedTemplateString with a formatted value added.
        /// Expected: Returns a list containing one operation with the formatted value.
        /// </summary>
        [Test]
        public void Operations_AfterAddFormatted_ReturnsOperationsList()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(0, 1);
            const string formattedValue = "42";

            // Act
            parsedTemplate.AddFormatted(formattedValue, typeof(int));
            IReadOnlyList<ParsedTemplateString.Op> operations = parsedTemplate.Operations;

            // Assert
            Assert.That(operations, Is.Not.Null);
            Assert.That(operations.Count, Is.EqualTo(1));
            Assert.That(operations[0].Item, Is.EqualTo(formattedValue));
        }

        /// <summary>
        /// Tests that the Operations property returns operations in the correct order they were added.
        /// Input: Multiple literals and formatted values added in sequence.
        /// Expected: Returns a list with operations in the same order they were added.
        /// </summary>
        [Test]
        public void Operations_MultipleOperations_ReturnsInCorrectOrder()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(20, 2);
            const string literal1 = "Start";
            const string formatted1 = "100";
            const string literal2 = "End";

            // Act
            parsedTemplate.AddLiteral(literal1);
            parsedTemplate.AddFormatted(formatted1, typeof(int));
            parsedTemplate.AddLiteral(literal2);
            IReadOnlyList<ParsedTemplateString.Op> operations = parsedTemplate.Operations;

            // Assert
            Assert.That(operations, Is.Not.Null);
            Assert.That(operations.Count, Is.EqualTo(3));
            Assert.That(operations[0].Item, Is.EqualTo(literal1));
            Assert.That(operations[1].Item, Is.EqualTo(formatted1));
            Assert.That(operations[2].Item, Is.EqualTo(literal2));
        }

        /// <summary>
        /// Tests that the Operations property handles empty string literals correctly.
        /// Input: An empty string added as a literal.
        /// Expected: Returns an empty list as empty literals are not added.
        /// </summary>
        [Test]
        public void Operations_EmptyStringLiteral_ReturnsEmptyList()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(0, 0);

            // Act
            parsedTemplate.AddLiteral(string.Empty);
            IReadOnlyList<ParsedTemplateString.Op> operations = parsedTemplate.Operations;

            // Assert
            Assert.That(operations, Is.Not.Null);
            Assert.That(operations, Is.Empty);
        }

        /// <summary>
        /// Tests that the Operations property handles whitespace-only literals correctly.
        /// Input: A whitespace-only string added as a literal.
        /// Expected: Returns a list containing one operation with OpType.WhiteSpace.
        /// </summary>
        [Test]
        public void Operations_WhitespaceLiteral_ReturnsWhiteSpaceOperation()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(5, 0);
            const string whitespace = "     ";

            // Act
            parsedTemplate.AddLiteral(whitespace);
            IReadOnlyList<ParsedTemplateString.Op> operations = parsedTemplate.Operations;

            // Assert
            Assert.That(operations, Is.Not.Null);
            Assert.That(operations.Count, Is.EqualTo(1));
            Assert.That(operations[0].Item, Is.EqualTo(whitespace));
        }

        /// <summary>
        /// Tests that the Operations property handles literals with line breaks correctly.
        /// Input: A literal containing newline characters.
        /// Expected: Returns a list with separate operations for text and line breaks.
        /// </summary>
        [Test]
        public void Operations_LiteralWithLineBreaks_ReturnsMultipleOperations()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(20, 0);
            const string literalWithNewline = "Line1\nLine2";

            // Act
            parsedTemplate.AddLiteral(literalWithNewline);
            IReadOnlyList<ParsedTemplateString.Op> operations = parsedTemplate.Operations;

            // Assert
            Assert.That(operations, Is.Not.Null);
            Assert.That(operations.Count, Is.GreaterThan(1));
            Assert.That(operations.Any(op => op.Item == "Line1"), Is.True);
            Assert.That(operations.Any(op => op.Item == "Line2"), Is.True);
        }

        /// <summary>
        /// Tests that the Operations property is read-only and returns the same reference on multiple calls.
        /// Input: A ParsedTemplateString with operations added.
        /// Expected: Multiple calls to Operations return the same instance.
        /// </summary>
        [Test]
        public void Operations_MultipleCalls_ReturnsSameReference()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(10, 1);
            parsedTemplate.AddLiteral("Test");

            // Act
            IReadOnlyList<ParsedTemplateString.Op> operations1 = parsedTemplate.Operations;
            IReadOnlyList<ParsedTemplateString.Op> operations2 = parsedTemplate.Operations;

            // Assert
            Assert.That(ReferenceEquals(operations1, operations2), Is.True);
        }

        /// <summary>
        /// Tests that the Operations property returns a list containing operations after FromString is used.
        /// Input: A ParsedTemplateString created from a raw string.
        /// Expected: Returns a list containing the operations from the raw string.
        /// </summary>
        [Test]
        public void Operations_FromString_ReturnsOperationsFromRawString()
        {
            // Arrange
            const string rawString = "Simple text";

            // Act
            var parsedTemplate = ParsedTemplateString.FromString(rawString);
            IReadOnlyList<ParsedTemplateString.Op> operations = parsedTemplate.Operations;

            // Assert
            Assert.That(operations, Is.Not.Null);
            Assert.That(operations.Count, Is.GreaterThan(0));
            Assert.That(operations.Any(op => op.Item == rawString), Is.True);
        }

        /// <summary>
        /// Tests that the Operations property handles very long literals correctly.
        /// Input: A very long string added as a literal.
        /// Expected: Returns a list containing the operation with the full long string.
        /// </summary>
        [Test]
        public void Operations_VeryLongLiteral_ReturnsOperationWithFullString()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(10000, 0);
            string longString = new('A', 10000);

            // Act
            parsedTemplate.AddLiteral(longString);
            IReadOnlyList<ParsedTemplateString.Op> operations = parsedTemplate.Operations;

            // Assert
            Assert.That(operations, Is.Not.Null);
            Assert.That(operations.Count, Is.EqualTo(1));
            Assert.That(operations[0].Item, Is.EqualTo(longString));
        }

        /// <summary>
        /// Tests that the Operations property handles special characters in literals correctly.
        /// Input: A literal containing special characters like tabs, quotes, and backslashes.
        /// Expected: Returns a list containing the operation with the special characters preserved.
        /// </summary>
        [Test]
        public void Operations_SpecialCharactersInLiteral_PreservesCharacters()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(50, 0);
            const string specialChars = "Tab:\t Quote:\" Backslash:\\ End";

            // Act
            parsedTemplate.AddLiteral(specialChars);
            IReadOnlyList<ParsedTemplateString.Op> operations = parsedTemplate.Operations;

            // Assert
            Assert.That(operations, Is.Not.Null);
            Assert.That(operations.Any(op => op.Item.Contains('\t', StringComparison.Ordinal)), Is.True);
        }

        /// <summary>
        /// Tests that the Operations property handles formatted string types correctly.
        /// Input: A formatted string value added with typeof(string).
        /// Expected: Returns a list containing the operation as a Token type.
        /// </summary>
        [Test]
        public void Operations_FormattedStringType_AddsAsToken()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(0, 1);
            const string stringValue = "TokenValue";

            // Act
            parsedTemplate.AddFormatted(stringValue, typeof(string));
            IReadOnlyList<ParsedTemplateString.Op> operations = parsedTemplate.Operations;

            // Assert
            Assert.That(operations, Is.Not.Null);
            Assert.That(operations.Count, Is.EqualTo(1));
            Assert.That(operations[0].Item, Is.EqualTo(stringValue));
        }

        /// <summary>
        /// Tests that the Operations property handles formatted non-string types correctly.
        /// Input: A formatted integer value added with typeof(int).
        /// Expected: Returns a list containing the operation as a Value type.
        /// </summary>
        [Test]
        public void Operations_FormattedNonStringType_AddsAsValue()
        {
            // Arrange
            var parsedTemplate = new ParsedTemplateString(0, 1);
            const string intValue = "123";

            // Act
            parsedTemplate.AddFormatted(intValue, typeof(int));
            IReadOnlyList<ParsedTemplateString.Op> operations = parsedTemplate.Operations;

            // Assert
            Assert.That(operations, Is.Not.Null);
            Assert.That(operations.Count, Is.EqualTo(1));
            Assert.That(operations[0].Item, Is.EqualTo(intValue));
        }

        /// <summary>
        /// Tests that FromString treats null input as an empty string.
        /// </summary>
        [Test]
        public void FromString_NullString_ParsesAsEmptyString()
        {
            // Arrange
            const string input = null;

            // Act
            var parsed = ParsedTemplateString.FromString(input);

            // Assert
            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed.LiteralLength, Is.EqualTo(0));
            Assert.That(parsed.FormattedCount, Is.EqualTo(0));
            Assert.That(parsed.ArgumentCount, Is.EqualTo(0));
            Assert.That(parsed.Format, Is.EqualTo(string.Empty));
            Assert.That(parsed.Operations, Is.Empty);
        }

        /// <summary>
        /// Tests that FromString correctly parses an empty string with zero length and no operations.
        /// </summary>
        [Test]
        public void FromString_EmptyString_ParsesCorrectly()
        {
            // Arrange
            const string input = "";

            // Act
            var parsed = ParsedTemplateString.FromString(input);

            // Assert
            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed.LiteralLength, Is.EqualTo(0));
            Assert.That(parsed.FormattedCount, Is.EqualTo(0));
            Assert.That(parsed.ArgumentCount, Is.EqualTo(0));
            Assert.That(parsed.Format, Is.EqualTo(string.Empty));
            Assert.That(parsed.Operations, Is.Empty);
        }

        /// <summary>
        /// Tests that FromString correctly parses a simple string without special characters.
        /// </summary>
        [Test]
        public void FromString_SimpleString_ParsesCorrectly()
        {
            // Arrange
            const string input = "Hello World";

            // Act
            var parsed = ParsedTemplateString.FromString(input);

            // Assert
            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed.LiteralLength, Is.EqualTo(input.Length));
            Assert.That(parsed.FormattedCount, Is.EqualTo(0));
            Assert.That(parsed.ArgumentCount, Is.EqualTo(0));
            Assert.That(parsed.Format, Is.EqualTo(input));
            Assert.That(parsed.Operations, Has.Count.EqualTo(1));
            Assert.That(parsed.Operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
        }

        /// <summary>
        /// Tests that FromString correctly identifies and parses whitespace-only strings (all spaces).
        /// </summary>
        [Test]
        public void FromString_WhitespaceOnlySpaces_ParsesAsWhitespace()
        {
            // Arrange
            const string input = "     ";

            // Act
            var parsed = ParsedTemplateString.FromString(input);

            // Assert
            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed.Operations, Has.Count.EqualTo(1));
            Assert.That(parsed.Operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.WhiteSpace));
            Assert.That(parsed.Operations[0].Item, Is.EqualTo(input));
        }

        /// <summary>
        /// Tests that FromString correctly handles tabs (not identified as all-spaces whitespace).
        /// </summary>
        [Test]
        public void FromString_WhitespaceWithTabs_ParsesAsLiteral()
        {
            // Arrange
            const string input = "\t\t\t";

            // Act
            var parsed = ParsedTemplateString.FromString(input);

            // Assert
            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed.Operations, Has.Count.EqualTo(1));
            Assert.That(parsed.Operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
        }

        /// <summary>
        /// Tests that FromString correctly parses strings with Unix-style line breaks (\n).
        /// </summary>
        [Test]
        public void FromString_WithUnixNewlines_ParsesLineBreaksCorrectly()
        {
            // Arrange
            const string input = "Line1\nLine2\nLine3";

            // Act
            var parsed = ParsedTemplateString.FromString(input);

            // Assert
            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed.IsMultiLine, Is.True);
            var operations = parsed.Operations.ToList();
            Assert.That(operations.Any(op => op.Type == ParsedTemplateString.OpType.LineBreak), Is.True);
            Assert.That(operations.Count(op => op.Type == ParsedTemplateString.OpType.LineBreak), Is.EqualTo(2));
            Assert.That(operations.Count(op => op.Type == ParsedTemplateString.OpType.Literal), Is.EqualTo(3));
        }

        /// <summary>
        /// Tests that FromString correctly parses strings with Windows-style line breaks (\r\n).
        /// </summary>
        [Test]
        public void FromString_WithWindowsNewlines_ParsesLineBreaksCorrectly()
        {
            // Arrange
            const string input = "Line1\r\nLine2\r\nLine3";

            // Act
            var parsed = ParsedTemplateString.FromString(input);

            // Assert
            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed.IsMultiLine, Is.True);
            var operations = parsed.Operations.ToList();
            Assert.That(operations.Count(op => op.Type == ParsedTemplateString.OpType.LineBreak), Is.EqualTo(2));
        }

        /// <summary>
        /// Tests that FromString correctly parses strings with mixed line break styles.
        /// </summary>
        [Test]
        public void FromString_WithMixedNewlines_ParsesLineBreaksCorrectly()
        {
            // Arrange
            const string input = "Line1\nLine2\r\nLine3";

            // Act
            var parsed = ParsedTemplateString.FromString(input);

            // Assert
            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed.IsMultiLine, Is.True);
            var operations = parsed.Operations.ToList();
            Assert.That(operations.Count(op => op.Type == ParsedTemplateString.OpType.LineBreak), Is.EqualTo(2));
        }

        /// <summary>
        /// Tests that FromString correctly handles strings containing only a single newline character.
        /// </summary>
        [Test]
        public void FromString_SingleNewline_ParsesCorrectly()
        {
            // Arrange
            const string input = "\n";

            // Act
            var parsed = ParsedTemplateString.FromString(input);

            // Assert
            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed.IsMultiLine, Is.True);
            Assert.That(parsed.Operations, Has.Count.EqualTo(1));
            Assert.That(parsed.Operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.LineBreak));
        }

        /// <summary>
        /// Tests that FromString correctly handles strings containing only a Windows line break.
        /// </summary>
        [Test]
        public void FromString_SingleWindowsNewline_ParsesCorrectly()
        {
            // Arrange
            const string input = "\r\n";

            // Act
            var parsed = ParsedTemplateString.FromString(input);

            // Assert
            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed.IsMultiLine, Is.True);
            Assert.That(parsed.Operations, Has.Count.EqualTo(1));
            Assert.That(parsed.Operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.LineBreak));
        }

        /// <summary>
        /// Tests that FromString correctly handles strings with special and control characters.
        /// </summary>
        [Test]
        public void FromString_WithSpecialCharacters_ParsesCorrectly()
        {
            // Arrange
            const string input = "Hello\tWorld\0Test";

            // Act
            var parsed = ParsedTemplateString.FromString(input);

            // Assert
            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed.LiteralLength, Is.EqualTo(input.Length));
            Assert.That(parsed.Format, Is.EqualTo(input));
        }

        /// <summary>
        /// Tests that FromString correctly handles strings with Unicode characters.
        /// </summary>
        [Test]
        public void FromString_WithUnicodeCharacters_ParsesCorrectly()
        {
            // Arrange
            const string input = "Hello 世界 🌍";

            // Act
            var parsed = ParsedTemplateString.FromString(input);

            // Assert
            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed.LiteralLength, Is.EqualTo(input.Length));
            Assert.That(parsed.Format, Is.EqualTo(input));
        }

        /// <summary>
        /// Tests that FromString correctly handles very long strings.
        /// </summary>
        [Test]
        public void FromString_VeryLongString_ParsesCorrectly()
        {
            // Arrange
            string input = new('a', 10000);

            // Act
            var parsed = ParsedTemplateString.FromString(input);

            // Assert
            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed.LiteralLength, Is.EqualTo(input.Length));
            Assert.That(parsed.Format, Is.EqualTo(input));
            Assert.That(parsed.Operations, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Tests that FromString correctly handles strings with mixed content including whitespace and text.
        /// </summary>
        [Test]
        public void FromString_MixedWhitespaceAndContent_ParsesCorrectly()
        {
            // Arrange
            const string input = "  Hello  World  ";

            // Act
            var parsed = ParsedTemplateString.FromString(input);

            // Assert
            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed.LiteralLength, Is.EqualTo(input.Length));
            Assert.That(parsed.Format, Is.EqualTo(input));
        }

        /// <summary>
        /// Tests that FromString correctly tracks line numbers when parsing multi-line strings.
        /// </summary>
        [Test]
        public void FromString_MultiLine_TracksLineNumbersCorrectly()
        {
            // Arrange
            const string input = "Line0\nLine1\nLine2";

            // Act
            var parsed = ParsedTemplateString.FromString(input);

            // Assert
            Assert.That(parsed, Is.Not.Null);
            var operations = parsed.Operations.ToList();
            Assert.That(operations[0].LineNumber, Is.EqualTo(0));
            Assert.That(operations[1].LineNumber, Is.EqualTo(0));
            Assert.That(operations[2].LineNumber, Is.EqualTo(1));
            Assert.That(operations[3].LineNumber, Is.EqualTo(1));
            Assert.That(operations[4].LineNumber, Is.EqualTo(2));
        }

        /// <summary>
        /// Tests that FromString correctly resets offset at line breaks.
        /// </summary>
        [Test]
        public void FromString_MultiLine_ResetsOffsetAtLineBreaks()
        {
            // Arrange
            const string input = "ABC\nDEF";

            // Act
            var parsed = ParsedTemplateString.FromString(input);

            // Assert
            Assert.That(parsed, Is.Not.Null);
            var operations = parsed.Operations.ToList();
            var literalOps = operations.Where(op => op.Type == ParsedTemplateString.OpType.Literal).ToList();
            Assert.That(literalOps[0].Offset, Is.EqualTo(0));
            Assert.That(literalOps[1].Offset, Is.EqualTo(0)); // Offset reset after line break
        }

        /// <summary>
        /// Tests that FromString correctly handles strings ending with a newline.
        /// </summary>
        [Test]
        public void FromString_EndsWithNewline_ParsesCorrectly()
        {
            // Arrange
            const string input = "Line1\n";

            // Act
            var parsed = ParsedTemplateString.FromString(input);

            // Assert
            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed.IsMultiLine, Is.True);
            var operations = parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(2));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(operations[1].Type, Is.EqualTo(ParsedTemplateString.OpType.LineBreak));
        }

        /// <summary>
        /// Tests that FromString correctly handles strings starting with a newline.
        /// </summary>
        [Test]
        public void FromString_StartsWithNewline_ParsesCorrectly()
        {
            // Arrange
            const string input = "\nLine1";

            // Act
            var parsed = ParsedTemplateString.FromString(input);

            // Assert
            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed.IsMultiLine, Is.True);
            var operations = parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(2));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.LineBreak));
            Assert.That(operations[1].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
        }

        /// <summary>
        /// Tests that FromString correctly handles consecutive newlines.
        /// </summary>
        [Test]
        public void FromString_ConsecutiveNewlines_ParsesCorrectly()
        {
            // Arrange
            const string input = "Line1\n\nLine2";

            // Act
            var parsed = ParsedTemplateString.FromString(input);

            // Assert
            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed.IsMultiLine, Is.True);
            var operations = parsed.Operations.ToList();
            Assert.That(operations.Count(op => op.Type == ParsedTemplateString.OpType.LineBreak), Is.EqualTo(2));
        }

        /// <summary>
        /// Tests that FromString produces ArgumentCount of zero since no formatted arguments are present.
        /// </summary>
        [Test]
        public void FromString_AnyString_HasZeroArgumentCount()
        {
            // Arrange
            const string input = "Test String";

            // Act
            var parsed = ParsedTemplateString.FromString(input);

            // Assert
            Assert.That(parsed.ArgumentCount, Is.EqualTo(0));
            Assert.That(parsed.GetArguments(), Is.Empty);
        }

        /// <summary>
        /// Tests that FromString produces FormattedCount of zero.
        /// </summary>
        [Test]
        public void FromString_AnyString_HasZeroFormattedCount()
        {
            // Arrange
            const string input = "Test String";

            // Act
            var parsed = ParsedTemplateString.FromString(input);

            // Assert
            Assert.That(parsed.FormattedCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that GetArguments returns an empty array when no operations have been added.
        /// </summary>
        [Test]
        public void GetArguments_NoOperations_ReturnsEmptyArray()
        {
            // Arrange
            var parsed = new ParsedTemplateString(0, 0);

            // Act
            object[] arguments = parsed.GetArguments();

            // Assert
            Assert.That(arguments, Is.Not.Null);
            Assert.That(arguments, Is.Empty);
        }

        /// <summary>
        /// Tests that GetArguments returns an empty array when only literal operations exist.
        /// </summary>
        [Test]
        public void GetArguments_OnlyLiterals_ReturnsEmptyArray()
        {
            // Arrange
            var parsed = new ParsedTemplateString(20, 0);
            parsed.AddLiteral("Hello World");
            parsed.AddLiteral(" ");
            parsed.AddLiteral("Test");

            // Act
            object[] arguments = parsed.GetArguments();

            // Assert
            Assert.That(arguments, Is.Not.Null);
            Assert.That(arguments, Is.Empty);
        }

        /// <summary>
        /// Tests that GetArguments returns only Token operations when all formatted items are strings.
        /// </summary>
        [Test]
        public void GetArguments_OnlyTokens_ReturnsAllTokens()
        {
            // Arrange
            var parsed = new ParsedTemplateString(0, 3);
            parsed.AddFormatted("First", typeof(string));
            parsed.AddFormatted("Second", typeof(string));
            parsed.AddFormatted("Third", typeof(string));

            // Act
            object[] arguments = parsed.GetArguments();

            // Assert
            Assert.That(arguments, Has.Length.EqualTo(3));
            Assert.That(arguments[0], Is.EqualTo("First"));
            Assert.That(arguments[1], Is.EqualTo("Second"));
            Assert.That(arguments[2], Is.EqualTo("Third"));
        }

        /// <summary>
        /// Tests that GetArguments returns only Value operations when all formatted items are non-strings.
        /// </summary>
        [Test]
        public void GetArguments_OnlyValues_ReturnsAllValues()
        {
            // Arrange
            var parsed = new ParsedTemplateString(0, 3);
            parsed.AddFormatted("42", typeof(int));
            parsed.AddFormatted("3.14", typeof(double));
            parsed.AddFormatted("True", typeof(bool));

            // Act
            object[] arguments = parsed.GetArguments();

            // Assert
            Assert.That(arguments, Has.Length.EqualTo(3));
            Assert.That(arguments[0], Is.EqualTo("42"));
            Assert.That(arguments[1], Is.EqualTo("3.14"));
            Assert.That(arguments[2], Is.EqualTo("True"));
        }

        /// <summary>
        /// Tests that GetArguments returns both Token and Value operations in correct order.
        /// </summary>
        [Test]
        public void GetArguments_MixedTokensAndValues_ReturnsInCorrectOrder()
        {
            // Arrange
            var parsed = new ParsedTemplateString(10, 4);
            parsed.AddFormatted("First", typeof(string));
            parsed.AddFormatted("42", typeof(int));
            parsed.AddFormatted("Second", typeof(string));
            parsed.AddFormatted("3.14", typeof(double));

            // Act
            object[] arguments = parsed.GetArguments();

            // Assert
            Assert.That(arguments, Has.Length.EqualTo(4));
            Assert.That(arguments[0], Is.EqualTo("First"));
            Assert.That(arguments[1], Is.EqualTo("42"));
            Assert.That(arguments[2], Is.EqualTo("Second"));
            Assert.That(arguments[3], Is.EqualTo("3.14"));
        }

        /// <summary>
        /// Tests that GetArguments filters out non-Token/Value operations and returns only formatted items.
        /// </summary>
        [Test]
        public void GetArguments_MixedAllOperationTypes_ReturnsOnlyTokensAndValues()
        {
            // Arrange
            var parsed = new ParsedTemplateString(30, 3);
            parsed.AddLiteral("Start");
            parsed.AddFormatted("Token1", typeof(string));
            parsed.AddLiteral(" ");
            parsed.AddFormatted("123", typeof(int));
            parsed.AddLiteral("\n");
            parsed.AddFormatted("Token2", typeof(string));
            parsed.AddLiteral("End");

            // Act
            object[] arguments = parsed.GetArguments();

            // Assert
            Assert.That(arguments, Has.Length.EqualTo(3));
            Assert.That(arguments[0], Is.EqualTo("Token1"));
            Assert.That(arguments[1], Is.EqualTo("123"));
            Assert.That(arguments[2], Is.EqualTo("Token2"));
        }

        /// <summary>
        /// Tests that GetArguments handles empty string items correctly.
        /// </summary>
        [Test]
        public void GetArguments_EmptyStringItems_ReturnsEmptyStrings()
        {
            // Arrange
            var parsed = new ParsedTemplateString(0, 3);
            parsed.AddFormatted(string.Empty, typeof(string));
            parsed.AddFormatted("NonEmpty", typeof(string));
            parsed.AddFormatted(string.Empty, typeof(int));

            // Act
            object[] arguments = parsed.GetArguments();

            // Assert
            Assert.That(arguments, Has.Length.EqualTo(3));
            Assert.That(arguments[0], Is.EqualTo(string.Empty));
            Assert.That(arguments[1], Is.EqualTo("NonEmpty"));
            Assert.That(arguments[2], Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that GetArguments maintains order with multiple operations of same type.
        /// </summary>
        [Test]
        public void GetArguments_MultipleOperationsSameType_MaintainsOrder()
        {
            // Arrange
            var parsed = new ParsedTemplateString(0, 10);
            for (int i = 0; i < 10; i++)
            {
                parsed.AddFormatted($"Item{i}", typeof(string));
            }

            // Act
            object[] arguments = parsed.GetArguments();

            // Assert
            Assert.That(arguments, Has.Length.EqualTo(10));
            for (int i = 0; i < 10; i++)
            {
                Assert.That(arguments[i], Is.EqualTo($"Item{i}"));
            }
        }

        /// <summary>
        /// Tests that GetArguments returns items for various non-string types as Value operations.
        /// </summary>
        [TestCase(typeof(int), "42")]
        [TestCase(typeof(double), "3.14")]
        [TestCase(typeof(bool), "True")]
        [TestCase(typeof(decimal), "99.99")]
        [TestCase(typeof(long), "9223372036854775807")]
        [TestCase(typeof(char), "A")]
        [TestCase(typeof(object), "SomeObject")]
        public void GetArguments_VariousNonStringTypes_ReturnsAsValues(Type type, string itemValue)
        {
            // Arrange
            var parsed = new ParsedTemplateString(0, 1);
            parsed.AddFormatted(itemValue, type);

            // Act
            object[] arguments = parsed.GetArguments();

            // Assert
            Assert.That(arguments, Has.Length.EqualTo(1));
            Assert.That(arguments[0], Is.EqualTo(itemValue));
        }

        /// <summary>
        /// Tests that AddFormatted with string type creates a Token operation.
        /// Input: item = "TestToken", type = typeof(string)
        /// Expected: Operation with OpType.Token is added to Operations list.
        /// </summary>
        [Test]
        public void AddFormatted_WithStringType_AddsTokenOperation()
        {
            // Arrange
            var parsed = new ParsedTemplateString(0, 1);
            const string item = "TestToken";

            // Act
            parsed.AddFormatted(item, typeof(string));

            // Assert
            var operations = parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(1));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Token));
            Assert.That(operations[0].Item, Is.EqualTo(item));
            Assert.That(operations[0].Offset, Is.EqualTo(0));
            Assert.That(operations[0].LineNumber, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that AddFormatted with non-string types creates a Value operation.
        /// Input: item with various non-string types (int, double, object, bool, decimal)
        /// Expected: Operation with OpType.Value is added for each type.
        /// </summary>
        [TestCase(typeof(int), "42")]
        [TestCase(typeof(double), "3.14")]
        [TestCase(typeof(object), "Object")]
        [TestCase(typeof(bool), "True")]
        [TestCase(typeof(decimal), "100.50")]
        [TestCase(typeof(long), "9223372036854775807")]
        [TestCase(typeof(char), "A")]
        public void AddFormatted_WithNonStringType_AddsValueOperation(Type type, string item)
        {
            // Arrange
            var parsed = new ParsedTemplateString(0, 1);

            // Act
            parsed.AddFormatted(item, type);

            // Assert
            var operations = parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(1));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Value));
            Assert.That(operations[0].Item, Is.EqualTo(item));
        }

        /// <summary>
        /// Tests that AddFormatted with empty string adds operation and maintains offset at zero.
        /// Input: item = "", type = typeof(string)
        /// Expected: Operation is added with empty item, offset remains 0.
        /// </summary>
        [Test]
        public void AddFormatted_WithEmptyString_AddsOperationAndMaintainsOffset()
        {
            // Arrange
            var parsed = new ParsedTemplateString(0, 1);
            const string item = "";

            // Act
            parsed.AddFormatted(item, typeof(string));

            // Assert
            var operations = parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(1));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Token));
            Assert.That(operations[0].Item, Is.EqualTo(item));
            Assert.That(operations[0].Offset, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that AddFormatted correctly increments offset after adding operation.
        /// Input: item = "Test" (length 4), type = typeof(string)
        /// Expected: Offset in operation is 0, subsequent operation would have offset 4.
        /// </summary>
        [Test]
        public void AddFormatted_SingleCall_IncrementsOffsetCorrectly()
        {
            // Arrange
            var parsed = new ParsedTemplateString(0, 2);
            const string firstItem = "Test";
            const string secondItem = "Value";

            // Act
            parsed.AddFormatted(firstItem, typeof(string));
            parsed.AddFormatted(secondItem, typeof(int));

            // Assert
            var operations = parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(2));
            Assert.That(operations[0].Offset, Is.EqualTo(0));
            Assert.That(operations[1].Offset, Is.EqualTo(firstItem.Length));
        }

        /// <summary>
        /// Tests that multiple calls to AddFormatted accumulate operations correctly.
        /// Input: Multiple AddFormatted calls with different items and types
        /// Expected: All operations are added, offsets increment correctly.
        /// </summary>
        [Test]
        public void AddFormatted_MultipleCalls_AccumulatesOperationsAndOffsetsCorrectly()
        {
            // Arrange
            var parsed = new ParsedTemplateString(0, 3);
            const string item1 = "Hello";
            const string item2 = "123";
            const string item3 = "World";

            // Act
            parsed.AddFormatted(item1, typeof(string));
            parsed.AddFormatted(item2, typeof(int));
            parsed.AddFormatted(item3, typeof(string));

            // Assert
            var operations = parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(3));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Token));
            Assert.That(operations[0].Item, Is.EqualTo(item1));
            Assert.That(operations[0].Offset, Is.EqualTo(0));
            Assert.That(operations[1].Type, Is.EqualTo(ParsedTemplateString.OpType.Value));
            Assert.That(operations[1].Item, Is.EqualTo(item2));
            Assert.That(operations[1].Offset, Is.EqualTo(item1.Length));
            Assert.That(operations[2].Type, Is.EqualTo(ParsedTemplateString.OpType.Token));
            Assert.That(operations[2].Item, Is.EqualTo(item3));
            Assert.That(operations[2].Offset, Is.EqualTo(item1.Length + item2.Length));
        }

        /// <summary>
        /// Tests that AddFormatted handles strings with special characters correctly.
        /// Input: item with special characters, type = typeof(string)
        /// Expected: Operation is added with the exact string including special characters.
        /// </summary>
        [TestCase("Hello\tWorld")]
        [TestCase("Line1\nLine2")]
        [TestCase("Quote\"Test")]
        [TestCase("Backslash\\Test")]
        [TestCase("Unicode\u00A9\u00AE")]
        [TestCase("Mixed!@#$%^&*()")]
        public void AddFormatted_WithSpecialCharacters_AddsOperationCorrectly(string item)
        {
            // Arrange
            var parsed = new ParsedTemplateString(0, 1);

            // Act
            parsed.AddFormatted(item, typeof(string));

            // Assert
            var operations = parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(1));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Token));
            Assert.That(operations[0].Item, Is.EqualTo(item));
            Assert.That(operations[0].Offset, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that AddFormatted handles very long strings correctly.
        /// Input: Very long string (1000+ characters), type = typeof(string)
        /// Expected: Operation is added with correct offset increment.
        /// </summary>
        [Test]
        public void AddFormatted_WithLongString_AddsOperationAndIncrementsOffsetCorrectly()
        {
            // Arrange
            var parsed = new ParsedTemplateString(0, 1);
            string longString = new('A', 10000);

            // Act
            parsed.AddFormatted(longString, typeof(string));

            // Assert
            var operations = parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(1));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Token));
            Assert.That(operations[0].Item, Is.EqualTo(longString));
            Assert.That(operations[0].Item.Length, Is.EqualTo(10000));
            Assert.That(operations[0].Offset, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that AddFormatted captures the correct line number in the operation.
        /// Input: item = "Test", type = typeof(string)
        /// Expected: Operation is added with LineNumber = 0 (default initial value).
        /// </summary>
        [Test]
        public void AddFormatted_CapturesLineNumberCorrectly()
        {
            // Arrange
            var parsed = new ParsedTemplateString(0, 1);
            const string item = "Test";

            // Act
            parsed.AddFormatted(item, typeof(string));

            // Assert
            var operations = parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(1));
            Assert.That(operations[0].LineNumber, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that AddFormatted with whitespace-only string adds operation correctly.
        /// Input: item = "   " (spaces only), type = typeof(string)
        /// Expected: Operation is added as Token with the whitespace string.
        /// </summary>
        [Test]
        public void AddFormatted_WithWhitespaceOnlyString_AddsOperationCorrectly()
        {
            // Arrange
            var parsed = new ParsedTemplateString(0, 1);
            const string item = "   ";

            // Act
            parsed.AddFormatted(item, typeof(string));

            // Assert
            var operations = parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(1));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Token));
            Assert.That(operations[0].Item, Is.EqualTo(item));
            Assert.That(operations[0].Offset, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that AddFormatted with single character string adds operation correctly.
        /// Input: item = "X" (single character), type = typeof(string)
        /// Expected: Operation is added with offset incremented by 1.
        /// </summary>
        [Test]
        public void AddFormatted_WithSingleCharacterString_AddsOperationCorrectly()
        {
            // Arrange
            var parsed = new ParsedTemplateString(0, 1);
            const string item = "X";

            // Act
            parsed.AddFormatted(item, typeof(string));

            // Assert
            var operations = parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(1));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Token));
            Assert.That(operations[0].Item, Is.EqualTo(item));
            Assert.That(operations[0].Item.Length, Is.EqualTo(1));
        }

        /// <summary>
        /// Tests that AddFormatted with control characters adds operation correctly.
        /// Input: item with various control characters, type = typeof(string)
        /// Expected: Operation is added with the exact string including control characters.
        /// </summary>
        [TestCase("\r")]
        [TestCase("\n")]
        [TestCase("\r\n")]
        [TestCase("\t")]
        [TestCase("\0")]
        public void AddFormatted_WithControlCharacters_AddsOperationCorrectly(string item)
        {
            // Arrange
            var parsed = new ParsedTemplateString(0, 1);

            // Act
            parsed.AddFormatted(item, typeof(string));

            // Assert
            var operations = parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(1));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Token));
            Assert.That(operations[0].Item, Is.EqualTo(item));
        }
    }
}
