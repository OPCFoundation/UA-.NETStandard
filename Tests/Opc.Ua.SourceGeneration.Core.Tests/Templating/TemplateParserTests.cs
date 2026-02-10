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
using System.Globalization;
using System.Linq;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration.Templating.Tests
{
    /// <summary>
    /// Unit tests for the TemplateParser.AppendLiteral method.
    /// </summary>
    [TestFixture]
    [Category("Templating")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class TemplateParserTests
    {
        /// <summary>
        /// Tests that AppendLiteral correctly adds an empty string literal without creating any operations.
        /// </summary>
        [Test]
        public void AppendLiteral_EmptyString_AddsNoOperations()
        {
            // Arrange
            var parser = new TemplateParser(0, 0);

            // Act
            parser.AppendLiteral(string.Empty);

            // Assert
            var operations = parser.Parsed.Operations.ToList();
            Assert.That(operations, Is.Empty);
        }

        /// <summary>
        /// Tests that AppendLiteral correctly adds a whitespace-only string as a WhiteSpace operation.
        /// </summary>
        [TestCase(" ")]
        [TestCase("  ")]
        [TestCase("     ")]
        public void AppendLiteral_WhitespaceOnlyString_AddsWhiteSpaceOperation(string whitespace)
        {
            // Arrange
            var parser = new TemplateParser(10, 0);

            // Act
            parser.AppendLiteral(whitespace);

            // Assert
            var operations = parser.Parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(1));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.WhiteSpace));
            Assert.That(operations[0].Item, Is.EqualTo(whitespace));
            Assert.That(operations[0].Offset, Is.EqualTo(0));
            Assert.That(operations[0].LineNumber, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that AppendLiteral correctly handles a string with a single Unix-style line break.
        /// </summary>
        [Test]
        public void AppendLiteral_StringWithUnixLineBreak_AddsLineBreakOperation()
        {
            // Arrange
            var parser = new TemplateParser(10, 0);

            // Act
            parser.AppendLiteral("Hello\nWorld");

            // Assert
            var operations = parser.Parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(3));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(operations[0].Item, Is.EqualTo("Hello"));
            Assert.That(operations[0].Offset, Is.EqualTo(0));
            Assert.That(operations[0].LineNumber, Is.EqualTo(0));
            Assert.That(operations[1].Type, Is.EqualTo(ParsedTemplateString.OpType.LineBreak));
            Assert.That(operations[1].Item, Is.EqualTo(Environment.NewLine));
            Assert.That(operations[1].Offset, Is.EqualTo(5));
            Assert.That(operations[1].LineNumber, Is.EqualTo(0));
            Assert.That(operations[2].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(operations[2].Item, Is.EqualTo("World"));
            Assert.That(operations[2].Offset, Is.EqualTo(0));
            Assert.That(operations[2].LineNumber, Is.EqualTo(1));
        }

        /// <summary>
        /// Tests that AppendLiteral correctly handles a string with a Windows-style line break (\r\n).
        /// </summary>
        [Test]
        public void AppendLiteral_StringWithWindowsLineBreak_AddsLineBreakOperation()
        {
            // Arrange
            var parser = new TemplateParser(10, 0);

            // Act
            parser.AppendLiteral("Hello\r\nWorld");

            // Assert
            var operations = parser.Parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(3));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(operations[0].Item, Is.EqualTo("Hello"));
            Assert.That(operations[1].Type, Is.EqualTo(ParsedTemplateString.OpType.LineBreak));
            Assert.That(operations[1].Item, Is.EqualTo(Environment.NewLine));
            Assert.That(operations[2].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(operations[2].Item, Is.EqualTo("World"));
        }

        /// <summary>
        /// Tests that AppendLiteral correctly handles a string with multiple line breaks.
        /// </summary>
        [Test]
        public void AppendLiteral_StringWithMultipleLineBreaks_AddsMultipleLineBreakOperations()
        {
            // Arrange
            var parser = new TemplateParser(20, 0);

            // Act
            parser.AppendLiteral("Line1\nLine2\nLine3");

            // Assert
            var operations = parser.Parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(5));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(operations[0].Item, Is.EqualTo("Line1"));
            Assert.That(operations[0].LineNumber, Is.EqualTo(0));
            Assert.That(operations[1].Type, Is.EqualTo(ParsedTemplateString.OpType.LineBreak));
            Assert.That(operations[1].LineNumber, Is.EqualTo(0));
            Assert.That(operations[2].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(operations[2].Item, Is.EqualTo("Line2"));
            Assert.That(operations[2].LineNumber, Is.EqualTo(1));
            Assert.That(operations[3].Type, Is.EqualTo(ParsedTemplateString.OpType.LineBreak));
            Assert.That(operations[3].LineNumber, Is.EqualTo(1));
            Assert.That(operations[4].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(operations[4].Item, Is.EqualTo("Line3"));
            Assert.That(operations[4].LineNumber, Is.EqualTo(2));
        }

        /// <summary>
        /// Tests that AppendLiteral correctly handles a string that starts with a line break.
        /// </summary>
        [Test]
        public void AppendLiteral_StringStartingWithLineBreak_AddsLineBreakOperationFirst()
        {
            // Arrange
            var parser = new TemplateParser(10, 0);

            // Act
            parser.AppendLiteral("\nContent");

            // Assert
            var operations = parser.Parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(2));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.LineBreak));
            Assert.That(operations[0].Item, Is.EqualTo(Environment.NewLine));
            Assert.That(operations[1].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(operations[1].Item, Is.EqualTo("Content"));
            Assert.That(operations[1].LineNumber, Is.EqualTo(1));
        }

        /// <summary>
        /// Tests that AppendLiteral correctly handles a string that ends with a line break.
        /// </summary>
        [Test]
        public void AppendLiteral_StringEndingWithLineBreak_AddsLineBreakOperationLast()
        {
            // Arrange
            var parser = new TemplateParser(10, 0);

            // Act
            parser.AppendLiteral("Content\n");

            // Assert
            var operations = parser.Parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(2));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(operations[0].Item, Is.EqualTo("Content"));
            Assert.That(operations[1].Type, Is.EqualTo(ParsedTemplateString.OpType.LineBreak));
            Assert.That(operations[1].Item, Is.EqualTo(Environment.NewLine));
        }

        /// <summary>
        /// Tests that AppendLiteral correctly handles a string with only a line break character.
        /// </summary>
        [Test]
        public void AppendLiteral_OnlyLineBreak_AddsLineBreakOperation()
        {
            // Arrange
            var parser = new TemplateParser(1, 0);

            // Act
            parser.AppendLiteral("\n");

            // Assert
            var operations = parser.Parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(1));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.LineBreak));
            Assert.That(operations[0].Item, Is.EqualTo(Environment.NewLine));
        }

        /// <summary>
        /// Tests that AppendLiteral correctly handles a string with special characters.
        /// </summary>
        [TestCase("Hello@World!")]
        [TestCase("Test#123$%^")]
        [TestCase("Tab\tCharacter")]
        public void AppendLiteral_StringWithSpecialCharacters_AddsLiteralOperation(string input)
        {
            // Arrange
            var parser = new TemplateParser(20, 0);

            // Act
            parser.AppendLiteral(input);

            // Assert
            var operations = parser.Parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(1));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(operations[0].Item, Is.EqualTo(input));
        }

        /// <summary>
        /// Tests that AppendLiteral correctly tracks offset across multiple calls.
        /// </summary>
        [Test]
        public void AppendLiteral_MultipleCalls_TracksOffsetCorrectly()
        {
            // Arrange
            var parser = new TemplateParser(20, 0);

            // Act
            parser.AppendLiteral("Hello");
            parser.AppendLiteral(" ");
            parser.AppendLiteral("World");

            // Assert
            var operations = parser.Parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(3));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(operations[0].Item, Is.EqualTo("Hello"));
            Assert.That(operations[0].Offset, Is.EqualTo(0));
            Assert.That(operations[1].Type, Is.EqualTo(ParsedTemplateString.OpType.WhiteSpace));
            Assert.That(operations[1].Item, Is.EqualTo(" "));
            Assert.That(operations[1].Offset, Is.EqualTo(5));
            Assert.That(operations[2].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(operations[2].Item, Is.EqualTo("World"));
            Assert.That(operations[2].Offset, Is.EqualTo(6));
        }

        /// <summary>
        /// Tests that AppendLiteral correctly resets offset after line breaks.
        /// </summary>
        [Test]
        public void AppendLiteral_LineBreakResetsOffset_OffsetIsZeroAfterLineBreak()
        {
            // Arrange
            var parser = new TemplateParser(20, 0);

            // Act
            parser.AppendLiteral("Hello\nWorld");

            // Assert
            var operations = parser.Parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(3));
            Assert.That(operations[0].Offset, Is.EqualTo(0));
            Assert.That(operations[1].Offset, Is.EqualTo(5)); // lb
            Assert.That(operations[2].Offset, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that AppendLiteral correctly handles a very long string without line breaks.
        /// </summary>
        [Test]
        public void AppendLiteral_VeryLongString_AddsLiteralOperation()
        {
            // Arrange
            var parser = new TemplateParser(10000, 0);
            string longString = new('A', 5000);

            // Act
            parser.AppendLiteral(longString);

            // Assert
            var operations = parser.Parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(1));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(operations[0].Item, Is.EqualTo(longString));
            Assert.That(operations[0].Item.Length, Is.EqualTo(5000));
        }

        /// <summary>
        /// Tests that AppendLiteral correctly handles mixed content with whitespace and line breaks.
        /// </summary>
        [Test]
        public void AppendLiteral_MixedContentWithWhitespaceAndLineBreaks_AddsMultipleOperations()
        {
            // Arrange
            var parser = new TemplateParser(30, 0);

            // Act
            parser.AppendLiteral("Hello  \nWorld");

            // Assert
            var operations = parser.Parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(3));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(operations[0].Item, Is.EqualTo("Hello  "));
            Assert.That(operations[1].Type, Is.EqualTo(ParsedTemplateString.OpType.LineBreak));
            Assert.That(operations[2].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(operations[2].Item, Is.EqualTo("World"));
        }

        /// <summary>
        /// Tests that AppendLiteral correctly handles consecutive line breaks.
        /// </summary>
        [Test]
        public void AppendLiteral_ConsecutiveLineBreaks_AddsMultipleLineBreakOperations()
        {
            // Arrange
            var parser = new TemplateParser(10, 0);

            // Act
            parser.AppendLiteral("\n\n\n");

            // Assert
            var operations = parser.Parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(3));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.LineBreak));
            Assert.That(operations[0].LineNumber, Is.EqualTo(0));
            Assert.That(operations[1].Type, Is.EqualTo(ParsedTemplateString.OpType.LineBreak));
            Assert.That(operations[1].LineNumber, Is.EqualTo(1));
            Assert.That(operations[2].Type, Is.EqualTo(ParsedTemplateString.OpType.LineBreak));
            Assert.That(operations[2].LineNumber, Is.EqualTo(2));
        }

        /// <summary>
        /// Tests that AppendLiteral correctly handles a carriage return not followed by newline.
        /// </summary>
        [Test]
        public void AppendLiteral_CarriageReturnWithoutNewline_AddsLiteralOperation()
        {
            // Arrange
            var parser = new TemplateParser(10, 0);

            // Act
            parser.AppendLiteral("Hello\rWorld");

            // Assert
            var operations = parser.Parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(1));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(operations[0].Item, Is.EqualTo("Hello\rWorld"));
        }

        /// <summary>
        /// Tests that AppendLiteral correctly handles a string ending with carriage return.
        /// </summary>
        [Test]
        public void AppendLiteral_StringEndingWithCarriageReturn_AddsLiteralOperation()
        {
            // Arrange
            var parser = new TemplateParser(10, 0);

            // Act
            parser.AppendLiteral("Content\r");

            // Assert
            var operations = parser.Parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(1));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Literal));
            Assert.That(operations[0].Item, Is.EqualTo("Content\r"));
        }

        /// <summary>
        /// Tests that the TemplateParser constructor initializes correctly with both parameters set to zero.
        /// This represents an empty template with no literals or formatted segments.
        /// Expected: Parsed property should be initialized with LiteralLength = 0 and FormattedCount = 0.
        /// </summary>
        [Test]
        public void Constructor_BothParametersZero_InitializesParsedCorrectly()
        {
            // Arrange & Act
            var parser = new TemplateParser(0, 0);

            // Assert
            Assert.That(parser.Parsed, Is.Not.Null);
            Assert.That(parser.Parsed.LiteralLength, Is.EqualTo(0));
            Assert.That(parser.Parsed.FormattedCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that the TemplateParser constructor correctly handles various valid combinations of parameters.
        /// This includes positive values, zero values, and different combinations.
        /// Expected: Parsed property should be initialized with correct LiteralLength and FormattedCount values.
        /// </summary>
        [TestCase(10, 0, TestName = "Constructor_PositiveLiteralZeroFormatted_InitializesParsedCorrectly")]
        [TestCase(0, 5, TestName = "Constructor_ZeroLiteralPositiveFormatted_InitializesParsedCorrectly")]
        [TestCase(5, 2, TestName = "Constructor_BothPositive_InitializesParsedCorrectly")]
        [TestCase(1, 1, TestName = "Constructor_BothOne_InitializesParsedCorrectly")]
        [TestCase(100, 50, TestName = "Constructor_LargePositiveValues_InitializesParsedCorrectly")]
        public void Constructor_ValidParameters_InitializesParsedCorrectly(int literalLength, int formattedCount)
        {
            // Arrange & Act
            var parser = new TemplateParser(literalLength, formattedCount);

            // Assert
            Assert.That(parser.Parsed, Is.Not.Null);
            Assert.That(parser.Parsed.LiteralLength, Is.EqualTo(literalLength));
            Assert.That(parser.Parsed.FormattedCount, Is.EqualTo(formattedCount));
        }

        /// <summary>
        /// Tests that the TemplateParser constructor throws ArgumentOutOfRangeException when both parameters are negative.
        /// This is because the sum would be negative, causing List capacity to be invalid.
        /// Expected: ArgumentOutOfRangeException should be thrown.
        /// </summary>
        [Test]
        public void Constructor_BothNegative_ThrowsArgumentOutOfRangeException()
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => new TemplateParser(-1, -1));
        }

        /// <summary>
        /// Tests that the TemplateParser constructor throws ArgumentOutOfRangeException when one parameter is negative
        /// and the sum of both parameters is negative.
        /// Expected: ArgumentOutOfRangeException should be thrown.
        /// </summary>
        [TestCase(-5, 3, TestName = "Constructor_NegativeLiteralSumNegative_ThrowsArgumentOutOfRangeException")]
        [TestCase(2, -10, TestName = "Constructor_NegativeFormattedSumNegative_ThrowsArgumentOutOfRangeException")]
        [TestCase(-100, 50, TestName = "Constructor_LargeNegativeLiteralSumNegative_ThrowsArgumentOutOfRangeException")]
        public void Constructor_NegativeParametersWithNegativeSum_ThrowsArgumentOutOfRangeException(int literalLength, int formattedCount)
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => new TemplateParser(literalLength, formattedCount));
        }

        /// <summary>
        /// Tests that the TemplateParser constructor handles single minimum integer value parameter.
        /// Expected: ArgumentOutOfRangeException due to negative capacity.
        /// </summary>
        [TestCase(
            int.MinValue,
            0,
            TestName = "Constructor_MinValueLiteralZeroFormatted_ThrowsArgumentOutOfRangeException")]
        [TestCase(
            0,
            int.MinValue,
            TestName = "Constructor_ZeroLiteralMinValueFormatted_ThrowsArgumentOutOfRangeException")]
        [TestCase(
            int.MaxValue,
            0,
            TestName = "Constructor_MaxValueLiteralZeroFormatted_ThrowsArgumentOutOfRangeException")]
        [TestCase(
            0,
            int.MaxValue,
            TestName = "Constructor_ZeroLiteralMaxValueFormatted_ThrowsArgumentOutOfRangeException")]
        [TestCase(
            int.MaxValue,
            int.MaxValue,
            TestName = "Constructor_MaxValueLiteralMaxValueFormatted_ThrowsArgumentOutOfRangeException")]
        [TestCase(
            ParsedTemplateString.MaxLiteralLength + 1,
            0,
            TestName = "Constructor_MaxLiteralLengthZeroFormatted_ThrowsArgumentOutOfRangeException")]
        [TestCase(
            0,
            ParsedTemplateString.MaxFormattedCount + 1,
            TestName = "Constructor_ZeroMaxFormattedLengthFormatted_ThrowsArgumentOutOfRangeException")]
        public void Constructor_SingleMinValueParameter_ThrowsArgumentOutOfRangeException(
            int literalLength,
            int formattedCount)
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => new TemplateParser(literalLength, formattedCount));
        }

        /// <summary>
        /// Tests that AppendFormatted correctly handles various integer values.
        /// </summary>
        [TestCase(0, "0", TestName = "AppendFormatted_IntegerZero_AddsValueOperation")]
        [TestCase(42, "42", TestName = "AppendFormatted_IntegerPositive_AddsValueOperation")]
        [TestCase(-42, "-42", TestName = "AppendFormatted_IntegerNegative_AddsValueOperation")]
        [TestCase(int.MaxValue, "2147483647", TestName = "AppendFormatted_IntegerMaxValue_AddsValueOperation")]
        [TestCase(int.MinValue, "-2147483648", TestName = "AppendFormatted_IntegerMinValue_AddsValueOperation")]
        public void AppendFormatted_Integer_AddsValueOperation(int value, string expectedString)
        {
            // Arrange
            var parser = new TemplateParser(0, 1);

            // Act
            parser.AppendFormatted(value);

            // Assert
            var operations = parser.Parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(1));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Value));
            Assert.That(operations[0].Item, Is.EqualTo(expectedString));
        }

        /// <summary>
        /// Tests that AppendFormatted correctly handles various long values.
        /// </summary>
        [TestCase(0L, "0", TestName = "AppendFormatted_LongZero_AddsValueOperation")]
        [TestCase(42L, "42", TestName = "AppendFormatted_LongPositive_AddsValueOperation")]
        [TestCase(-42L, "-42", TestName = "AppendFormatted_LongNegative_AddsValueOperation")]
        [TestCase(long.MaxValue, "9223372036854775807", TestName = "AppendFormatted_LongMaxValue_AddsValueOperation")]
        [TestCase(long.MinValue, "-9223372036854775808", TestName = "AppendFormatted_LongMinValue_AddsValueOperation")]
        public void AppendFormatted_Long_AddsValueOperation(long value, string expectedString)
        {
            // Arrange
            var parser = new TemplateParser(0, 1);

            // Act
            parser.AppendFormatted(value);

            // Assert
            var operations = parser.Parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(1));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Value));
            Assert.That(operations[0].Item, Is.EqualTo(expectedString));
        }

        /// <summary>
        /// Tests that AppendFormatted correctly handles various double values including special values.
        /// </summary>
        [TestCase(0.0, "0", TestName = "AppendFormatted_DoubleZero_AddsValueOperation")]
        [TestCase(42.5, "42.5", TestName = "AppendFormatted_DoublePositive_AddsValueOperation")]
        [TestCase(-42.5, "-42.5", TestName = "AppendFormatted_DoubleNegative_AddsValueOperation")]
        [TestCase(double.MaxValue, "1.7976931348623", TestName = "AppendFormatted_DoubleMaxValue_AddsValueOperation")]
        [TestCase(double.MinValue, "-1.7976931348623", TestName = "AppendFormatted_DoubleMinValue_AddsValueOperation")]
        [TestCase(double.NaN, "NaN", TestName = "AppendFormatted_DoubleNaN_AddsValueOperation")]
        [TestCase(double.PositiveInfinity, "Infinity", TestName = "AppendFormatted_DoublePositiveInfinity_AddsValueOperation")]
        [TestCase(double.NegativeInfinity, "-Infinity", TestName = "AppendFormatted_DoubleNegativeInfinity_AddsValueOperation")]
        public void AppendFormatted_Double_AddsValueOperation(double value, string expectedString)
        {
            // Arrange
            var parser = new TemplateParser(0, 1);

            // Act
            parser.AppendFormatted(value);

            // Assert
            var operations = parser.Parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(1));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Value));
            Assert.That(operations[0].Item, Does.StartWith(expectedString));
        }

        /// <summary>
        /// Tests that AppendFormatted correctly handles various float values including special values.
        /// </summary>
        [TestCase(0.0f, "0", TestName = "AppendFormatted_FloatZero_AddsValueOperation")]
        [TestCase(42.5f, "42.5", TestName = "AppendFormatted_FloatPositive_AddsValueOperation")]
        [TestCase(-42.5f, "-42.5", TestName = "AppendFormatted_FloatNegative_AddsValueOperation")]
        [TestCase(float.MaxValue, "3.402823", TestName = "AppendFormatted_FloatMaxValue_AddsValueOperation")]
        [TestCase(float.MinValue, "-3.402823", TestName = "AppendFormatted_FloatMinValue_AddsValueOperation")]
        [TestCase(float.NaN, "NaN", TestName = "AppendFormatted_FloatNaN_AddsValueOperation")]
        [TestCase(float.PositiveInfinity, "Infinity", TestName = "AppendFormatted_FloatPositiveInfinity_AddsValueOperation")]
        [TestCase(float.NegativeInfinity, "-Infinity", TestName = "AppendFormatted_FloatNegativeInfinity_AddsValueOperation")]
        public void AppendFormatted_Float_AddsValueOperation(float value, string expectedString)
        {
            // Arrange
            var parser = new TemplateParser(0, 1);

            // Act
            parser.AppendFormatted(value);

            // Assert
            var operations = parser.Parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(1));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Value));
            Assert.That(operations[0].Item, Does.StartWith(expectedString));
        }

        /// <summary>
        /// Tests that AppendFormatted correctly handles decimal values.
        /// </summary>
        [TestCase("0", "0", TestName = "AppendFormatted_DecimalZero_AddsValueOperation")]
        [TestCase("42.5", "42.5", TestName = "AppendFormatted_DecimalPositive_AddsValueOperation")]
        [TestCase("-42.5", "-42.5", TestName = "AppendFormatted_DecimalNegative_AddsValueOperation")]
        public void AppendFormatted_Decimal_AddsValueOperation(string valueString, string expectedString)
        {
            // Arrange
            var parser = new TemplateParser(0, 1);
            decimal value = decimal.Parse(valueString, CultureInfo.InvariantCulture);

            // Act
            parser.AppendFormatted(value);

            // Assert
            var operations = parser.Parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(1));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Value));
            Assert.That(operations[0].Item, Is.EqualTo(expectedString));
        }

        /// <summary>
        /// Tests that AppendFormatted correctly handles decimal boundary values.
        /// </summary>
        [Test]
        public void AppendFormatted_DecimalMaxValue_AddsValueOperation()
        {
            // Arrange
            var parser = new TemplateParser(0, 1);
            const decimal value = decimal.MaxValue;

            // Act
            parser.AppendFormatted(value);

            // Assert
            var operations = parser.Parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(1));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Value));
            Assert.That(operations[0].Item, Is.EqualTo(decimal.MaxValue.ToString(CultureInfo.InvariantCulture)));
        }

        /// <summary>
        /// Tests that AppendFormatted correctly handles decimal minimum value.
        /// </summary>
        [Test]
        public void AppendFormatted_DecimalMinValue_AddsValueOperation()
        {
            // Arrange
            var parser = new TemplateParser(0, 1);
            const decimal value = decimal.MinValue;

            // Act
            parser.AppendFormatted(value);

            // Assert
            var operations = parser.Parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(1));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Value));
            Assert.That(operations[0].Item, Is.EqualTo(decimal.MinValue.ToString(CultureInfo.InvariantCulture)));
        }

        /// <summary>
        /// Tests that AppendFormatted correctly handles boolean values.
        /// </summary>
        [TestCase(true, "True", TestName = "AppendFormatted_BooleanTrue_AddsValueOperation")]
        [TestCase(false, "False", TestName = "AppendFormatted_BooleanFalse_AddsValueOperation")]
        public void AppendFormatted_Boolean_AddsValueOperation(bool value, string expectedString)
        {
            // Arrange
            var parser = new TemplateParser(0, 1);

            // Act
            parser.AppendFormatted(value);

            // Assert
            var operations = parser.Parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(1));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Value));
            Assert.That(operations[0].Item, Is.EqualTo(expectedString));
        }

        /// <summary>
        /// Tests that AppendFormatted correctly handles string values and adds Token operation.
        /// </summary>
        [TestCase("TestValue", TestName = "AppendFormatted_StringNormal_AddsTokenOperation")]
        [TestCase("", TestName = "AppendFormatted_StringEmpty_AddsTokenOperation")]
        [TestCase(" ", TestName = "AppendFormatted_StringSingleSpace_AddsTokenOperation")]
        [TestCase("   ", TestName = "AppendFormatted_StringMultipleSpaces_AddsTokenOperation")]
        [TestCase("\t", TestName = "AppendFormatted_StringTab_AddsTokenOperation")]
        [TestCase("\n", TestName = "AppendFormatted_StringNewline_AddsTokenOperation")]
        [TestCase("\r\n", TestName = "AppendFormatted_StringCRLF_AddsTokenOperation")]
        public void AppendFormatted_String_AddsTokenOperation(string value)
        {
            // Arrange
            var parser = new TemplateParser(0, 1);

            // Act
            parser.AppendFormatted(value);

            // Assert
            var operations = parser.Parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(1));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Token));
            Assert.That(operations[0].Item, Is.EqualTo(value));
        }

        /// <summary>
        /// Tests that AppendFormatted correctly handles very long strings.
        /// </summary>
        [Test]
        public void AppendFormatted_VeryLongString_AddsTokenOperation()
        {
            // Arrange
            var parser = new TemplateParser(0, 1);
            string value = new('A', 10000);

            // Act
            parser.AppendFormatted(value);

            // Assert
            var operations = parser.Parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(1));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Token));
            Assert.That(operations[0].Item, Is.EqualTo(value));
            Assert.That(operations[0].Item.Length, Is.EqualTo(10000));
        }

        /// <summary>
        /// Tests that AppendFormatted correctly handles strings with special characters.
        /// </summary>
        [Test]
        public void AppendFormatted_StringWithSpecialCharacters_AddsTokenOperation()
        {
            // Arrange
            var parser = new TemplateParser(0, 1);
            const string value = "Hello\0World\u0001\u001F";

            // Act
            parser.AppendFormatted(value);

            // Assert
            var operations = parser.Parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(1));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Token));
            Assert.That(operations[0].Item, Is.EqualTo(value));
        }

        /// <summary>
        /// Tests that AppendFormatted correctly handles byte values.
        /// </summary>
        [TestCase((byte)0, "0", TestName = "AppendFormatted_ByteZero_AddsValueOperation")]
        [TestCase((byte)42, "42", TestName = "AppendFormatted_BytePositive_AddsValueOperation")]
        [TestCase(byte.MaxValue, "255", TestName = "AppendFormatted_ByteMaxValue_AddsValueOperation")]
        [TestCase(byte.MinValue, "0", TestName = "AppendFormatted_ByteMinValue_AddsValueOperation")]
        public void AppendFormatted_Byte_AddsValueOperation(byte value, string expectedString)
        {
            // Arrange
            var parser = new TemplateParser(0, 1);

            // Act
            parser.AppendFormatted(value);

            // Assert
            var operations = parser.Parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(1));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Value));
            Assert.That(operations[0].Item, Is.EqualTo(expectedString));
        }

        /// <summary>
        /// Tests that AppendFormatted correctly handles short values.
        /// </summary>
        [TestCase((short)0, "0", TestName = "AppendFormatted_ShortZero_AddsValueOperation")]
        [TestCase((short)42, "42", TestName = "AppendFormatted_ShortPositive_AddsValueOperation")]
        [TestCase((short)-42, "-42", TestName = "AppendFormatted_ShortNegative_AddsValueOperation")]
        [TestCase(short.MaxValue, "32767", TestName = "AppendFormatted_ShortMaxValue_AddsValueOperation")]
        [TestCase(short.MinValue, "-32768", TestName = "AppendFormatted_ShortMinValue_AddsValueOperation")]
        public void AppendFormatted_Short_AddsValueOperation(short value, string expectedString)
        {
            // Arrange
            var parser = new TemplateParser(0, 1);

            // Act
            parser.AppendFormatted(value);

            // Assert
            var operations = parser.Parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(1));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Value));
            Assert.That(operations[0].Item, Is.EqualTo(expectedString));
        }

        /// <summary>
        /// Tests that AppendFormatted correctly handles char values.
        /// </summary>
        [TestCase('A', "A", TestName = "AppendFormatted_CharLetter_AddsValueOperation")]
        [TestCase('0', "0", TestName = "AppendFormatted_CharDigit_AddsValueOperation")]
        [TestCase(' ', " ", TestName = "AppendFormatted_CharSpace_AddsValueOperation")]
        [TestCase('\n', "\n", TestName = "AppendFormatted_CharNewline_AddsValueOperation")]
        [TestCase('\0', "\0", TestName = "AppendFormatted_CharNull_AddsValueOperation")]
        public void AppendFormatted_Char_AddsValueOperation(char value, string expectedString)
        {
            // Arrange
            var parser = new TemplateParser(0, 1);

            // Act
            parser.AppendFormatted(value);

            // Assert
            var operations = parser.Parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(1));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Value));
            Assert.That(operations[0].Item, Is.EqualTo(expectedString));
        }

        /// <summary>
        /// Tests that AppendFormatted correctly handles Guid values.
        /// </summary>
        [Test]
        public void AppendFormatted_Guid_AddsValueOperation()
        {
            // Arrange
            var parser = new TemplateParser(0, 1);
            var value = Guid.Parse("12345678-1234-1234-1234-123456789012");

            // Act
            parser.AppendFormatted(value);

            // Assert
            var operations = parser.Parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(1));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Value));
            Assert.That(operations[0].Item, Is.EqualTo(value.ToString()));
        }

        /// <summary>
        /// Tests that AppendFormatted correctly handles DateTime values.
        /// </summary>
        [Test]
        public void AppendFormatted_DateTime_AddsValueOperation()
        {
            // Arrange
            var parser = new TemplateParser(0, 1);
            var value = new DateTime(2025, 1, 15, 12, 30, 45);

            // Act
            parser.AppendFormatted(value);

            // Assert
            var operations = parser.Parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(1));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Value));
            Assert.That(operations[0].Item, Is.EqualTo(value.ToString(CultureInfo.InvariantCulture)));
        }

        /// <summary>
        /// Tests that AppendFormatted correctly handles custom object with overridden ToString.
        /// </summary>
        [Test]
        public void AppendFormatted_CustomObject_AddsValueOperation()
        {
            // Arrange
            var parser = new TemplateParser(0, 1);
            var value = new CustomTestObject("TestName");

            // Act
            parser.AppendFormatted(value);

            // Assert
            var operations = parser.Parsed.Operations.ToList();
            Assert.That(operations, Has.Count.EqualTo(1));
            Assert.That(operations[0].Type, Is.EqualTo(ParsedTemplateString.OpType.Value));
            Assert.That(operations[0].Item, Is.EqualTo("CustomTestObject: TestName"));
        }

        /// <summary>
        /// Helper class for testing custom object ToString behavior.
        /// </summary>
        private sealed class CustomTestObject
        {
            private readonly string m_name;

            public CustomTestObject(string name)
            {
                m_name = name;
            }

            public override string ToString()
            {
                return $"CustomTestObject: {m_name}";
            }
        }

        /// <summary>
        /// Tests that GetFormattedText returns an empty string when the parser is empty with no literals or formatted items.
        /// </summary>
        [Test]
        public void GetFormattedText_EmptyParser_ReturnsEmptyString()
        {
            // Arrange
            var parser = new TemplateParser(0, 0);

            // Act
            string result = parser.GetFormattedText();

            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that GetFormattedText returns the correct string when the parser contains only literals.
        /// </summary>
        [Test]
        public void GetFormattedText_OnlyLiterals_ReturnsCorrectString()
        {
            // Arrange
            var parser = new TemplateParser(10, 0);
            parser.AppendLiteral("Hello");
            parser.AppendLiteral(" ");
            parser.AppendLiteral("World");

            // Act
            string result = parser.GetFormattedText();

            // Assert
            Assert.That(result, Is.EqualTo("Hello World"));
        }

        /// <summary>
        /// Tests that GetFormattedText returns the correct formatted string when the parser contains only formatted items.
        /// </summary>
        [Test]
        public void GetFormattedText_OnlyFormattedItems_ReturnsCorrectString()
        {
            // Arrange
            var parser = new TemplateParser(0, 2);
            parser.AppendFormatted(42);
            parser.AppendFormatted(3.14);

            // Act
            string result = parser.GetFormattedText();

            // Assert
            Assert.That(result, Is.EqualTo("423.14"));
        }

        /// <summary>
        /// Tests that GetFormattedText returns the correct string when the parser contains a mix of literals and formatted items.
        /// </summary>
        [Test]
        public void GetFormattedText_MixedContent_ReturnsCorrectString()
        {
            // Arrange
            var parser = new TemplateParser(6, 1);
            parser.AppendLiteral("Hello ");
            parser.AppendFormatted("World");

            // Act
            string result = parser.GetFormattedText();

            // Assert
            Assert.That(result, Is.EqualTo("Hello World"));
        }

        /// <summary>
        /// Tests that GetFormattedText handles special characters in literals correctly.
        /// </summary>
        [Test]
        public void GetFormattedText_SpecialCharactersInLiterals_ReturnsCorrectString()
        {
            // Arrange
            var parser = new TemplateParser(20, 0);
            parser.AppendLiteral("Special: @#$%^&*()");

            // Act
            string result = parser.GetFormattedText();

            // Assert
            Assert.That(result, Is.EqualTo("Special: @#$%^&*()"));
        }

        /// <summary>
        /// Tests that GetFormattedText handles whitespace-only literals correctly.
        /// </summary>
        [Test]
        public void GetFormattedText_WhitespaceOnlyLiterals_ReturnsCorrectString()
        {
            // Arrange
            var parser = new TemplateParser(10, 0);
            parser.AppendLiteral("   ");
            parser.AppendLiteral("\t");
            parser.AppendLiteral("  ");

            // Act
            string result = parser.GetFormattedText();

            // Assert
            Assert.That(result, Is.EqualTo("   \t  "));
        }

        /// <summary>
        /// Tests that GetFormattedText handles line breaks in literals correctly.
        /// </summary>
        [Test]
        public void GetFormattedText_LiteralsWithLineBreaks_ReturnsCorrectString()
        {
            // Arrange
            var parser = new TemplateParser(20, 0);
            parser.AppendLiteral("Line 1\nLine 2\nLine 3");

            // Act
            string result = parser.GetFormattedText();

            // Assert
            Assert.That(result, Is.EqualTo($"Line 1{Environment.NewLine}Line 2{Environment.NewLine}Line 3"));
        }

        /// <summary>
        /// Tests that GetFormattedText handles Windows-style line breaks correctly.
        /// </summary>
        [Test]
        public void GetFormattedText_LiteralsWithWindowsLineBreaks_ReturnsCorrectString()
        {
            // Arrange
            var parser = new TemplateParser(20, 0);
            parser.AppendLiteral("Line 1\r\nLine 2\r\nLine 3");

            // Act
            string result = parser.GetFormattedText();

            // Assert
            Assert.That(result, Is.EqualTo($"Line 1{Environment.NewLine}Line 2{Environment.NewLine}Line 3"));
        }

        /// <summary>
        /// Tests that GetFormattedText handles multiple formatted items with different types correctly.
        /// </summary>
        [Test]
        public void GetFormattedText_MultipleFormattedItemsWithDifferentTypes_ReturnsCorrectString()
        {
            // Arrange
            var parser = new TemplateParser(10, 4);
            parser.AppendLiteral("Integer: ");
            parser.AppendFormatted(42);
            parser.AppendLiteral(", Double: ");
            parser.AppendFormatted(3.14);
            parser.AppendLiteral(", String: ");
            parser.AppendFormatted("Test");
            parser.AppendLiteral(", Bool: ");
            parser.AppendFormatted(true);

            // Act
            string result = parser.GetFormattedText();

            // Assert
            Assert.That(result, Is.EqualTo("Integer: 42, Double: 3.14, String: Test, Bool: True"));
        }

        /// <summary>
        /// Tests that GetFormattedText uses InvariantCulture for formatting numeric values.
        /// </summary>
        [Test]
        public void GetFormattedText_NumericValues_UsesInvariantCulture()
        {
            // Arrange
            var parser = new TemplateParser(5, 1);
            parser.AppendLiteral("Value: ");
            parser.AppendFormatted(1234.56);

            // Act
            string result = parser.GetFormattedText();

            // Assert
            Assert.That(result, Is.EqualTo("Value: 1234.56"));
            Assert.That(result, Does.Contain("."));
            Assert.That(result, Does.Not.Contain(","));
        }

        /// <summary>
        /// Tests that GetFormattedText handles empty string literals correctly.
        /// </summary>
        [Test]
        public void GetFormattedText_EmptyStringLiterals_ReturnsCorrectString()
        {
            // Arrange
            var parser = new TemplateParser(10, 1);
            parser.AppendLiteral(string.Empty);
            parser.AppendFormatted("Value");
            parser.AppendLiteral(string.Empty);

            // Act
            string result = parser.GetFormattedText();

            // Assert
            Assert.That(result, Is.EqualTo("Value"));
        }

        /// <summary>
        /// Tests that GetFormattedText handles negative numbers correctly.
        /// </summary>
        [Test]
        public void GetFormattedText_NegativeNumbers_ReturnsCorrectString()
        {
            // Arrange
            var parser = new TemplateParser(5, 2);
            parser.AppendLiteral("Neg: ");
            parser.AppendFormatted(-42);
            parser.AppendLiteral(", ");
            parser.AppendFormatted(-3.14);

            // Act
            string result = parser.GetFormattedText();

            // Assert
            Assert.That(result, Is.EqualTo("Neg: -42, -3.14"));
        }

        /// <summary>
        /// Tests that GetFormattedText handles zero values correctly.
        /// </summary>
        [Test]
        public void GetFormattedText_ZeroValues_ReturnsCorrectString()
        {
            // Arrange
            var parser = new TemplateParser(5, 2);
            parser.AppendLiteral("Zero: ");
            parser.AppendFormatted(0);
            parser.AppendLiteral(", ");
            parser.AppendFormatted(0.0);

            // Act
            string result = parser.GetFormattedText();

            // Assert
            Assert.That(result, Is.EqualTo("Zero: 0, 0"));
        }

        /// <summary>
        /// Tests that GetFormattedText handles int.MaxValue correctly.
        /// </summary>
        [Test]
        public void GetFormattedText_IntMaxValue_ReturnsCorrectString()
        {
            // Arrange
            var parser = new TemplateParser(5, 1);
            parser.AppendLiteral("Max: ");
            parser.AppendFormatted(int.MaxValue);

            // Act
            string result = parser.GetFormattedText();

            // Assert
            Assert.That(result, Is.EqualTo($"Max: {int.MaxValue}"));
        }

        /// <summary>
        /// Tests that GetFormattedText handles int.MinValue correctly.
        /// </summary>
        [Test]
        public void GetFormattedText_IntMinValue_ReturnsCorrectString()
        {
            // Arrange
            var parser = new TemplateParser(5, 1);
            parser.AppendLiteral("Min: ");
            parser.AppendFormatted(int.MinValue);

            // Act
            string result = parser.GetFormattedText();

            // Assert
            Assert.That(result, Is.EqualTo($"Min: {int.MinValue}"));
        }

        /// <summary>
        /// Tests that GetFormattedText handles double.NaN correctly.
        /// </summary>
        [Test]
        public void GetFormattedText_DoubleNaN_ReturnsCorrectString()
        {
            // Arrange
            var parser = new TemplateParser(5, 1);
            parser.AppendLiteral("NaN: ");
            parser.AppendFormatted(double.NaN);

            // Act
            string result = parser.GetFormattedText();

            // Assert
            Assert.That(result, Is.EqualTo("NaN: NaN"));
        }

        /// <summary>
        /// Tests that GetFormattedText handles double.PositiveInfinity correctly.
        /// </summary>
        [Test]
        public void GetFormattedText_DoublePositiveInfinity_ReturnsCorrectString()
        {
            // Arrange
            var parser = new TemplateParser(10, 1);
            parser.AppendLiteral("Infinity: ");
            parser.AppendFormatted(double.PositiveInfinity);

            // Act
            string result = parser.GetFormattedText();

            // Assert
            Assert.That(result, Is.EqualTo($"Infinity: Infinity"));
        }

        /// <summary>
        /// Tests that GetFormattedText handles double.NegativeInfinity correctly.
        /// </summary>
        [Test]
        public void GetFormattedText_DoubleNegativeInfinity_ReturnsCorrectString()
        {
            // Arrange
            var parser = new TemplateParser(10, 1);
            parser.AppendLiteral("NegInf: ");
            parser.AppendFormatted(double.NegativeInfinity);

            // Act
            string result = parser.GetFormattedText();

            // Assert
            Assert.That(result, Is.EqualTo($"NegInf: -Infinity"));
        }

        /// <summary>
        /// Tests that GetFormattedText handles empty string formatted items correctly.
        /// </summary>
        [Test]
        public void GetFormattedText_EmptyStringFormattedItem_ReturnsCorrectString()
        {
            // Arrange
            var parser = new TemplateParser(10, 1);
            parser.AppendLiteral("Value: ");
            parser.AppendFormatted(string.Empty);

            // Act
            string result = parser.GetFormattedText();

            // Assert
            Assert.That(result, Is.EqualTo("Value: "));
        }

        /// <summary>
        /// Tests that GetFormattedText handles very long strings correctly.
        /// </summary>
        [Test]
        public void GetFormattedText_VeryLongString_ReturnsCorrectString()
        {
            // Arrange
            string longString = new('A', 10000);
            var parser = new TemplateParser(longString.Length, 0);
            parser.AppendLiteral(longString);

            // Act
            string result = parser.GetFormattedText();

            // Assert
            Assert.That(result, Is.EqualTo(longString));
            Assert.That(result.Length, Is.EqualTo(10000));
        }

        /// <summary>
        /// Tests that GetFormattedText handles complex multi-line templates correctly.
        /// </summary>
        [Test]
        public void GetFormattedText_ComplexMultiLineTemplate_ReturnsCorrectString()
        {
            // Arrange
            var parser = new TemplateParser(50, 3);
            parser.AppendLiteral("Name: ");
            parser.AppendFormatted("John");
            parser.AppendLiteral("\nAge: ");
            parser.AppendFormatted(30);
            parser.AppendLiteral("\nScore: ");
            parser.AppendFormatted(95.5);

            // Act
            string result = parser.GetFormattedText();

            // Assert
            string expectedResult = $"Name: John{Environment.NewLine}Age: 30{Environment.NewLine}Score: 95.5";
            Assert.That(result, Is.EqualTo(expectedResult));
        }

        /// <summary>
        /// Tests that GetFormattedText handles Unicode characters correctly.
        /// </summary>
        [Test]
        public void GetFormattedText_UnicodeCharacters_ReturnsCorrectString()
        {
            // Arrange
            var parser = new TemplateParser(20, 0);
            parser.AppendLiteral("Unicode: 你好 🌍 ñ");

            // Act
            string result = parser.GetFormattedText();

            // Assert
            Assert.That(result, Is.EqualTo("Unicode: 你好 🌍 ñ"));
        }

        /// <summary>
        /// Tests that GetFormattedText handles tab characters correctly.
        /// </summary>
        [Test]
        public void GetFormattedText_TabCharacters_ReturnsCorrectString()
        {
            // Arrange
            var parser = new TemplateParser(20, 0);
            parser.AppendLiteral("Col1\tCol2\tCol3");

            // Act
            string result = parser.GetFormattedText();

            // Assert
            Assert.That(result, Is.EqualTo("Col1\tCol2\tCol3"));
        }

        /// <summary>
        /// Tests that GetFormattedText handles boolean values correctly.
        /// </summary>
        [TestCase(true, "True")]
        [TestCase(false, "False")]
        public void GetFormattedText_BooleanValues_ReturnsCorrectString(bool value, string expected)
        {
            // Arrange
            var parser = new TemplateParser(5, 1);
            parser.AppendLiteral("Bool: ");
            parser.AppendFormatted(value);

            // Act
            string result = parser.GetFormattedText();

            // Assert
            Assert.That(result, Is.EqualTo($"Bool: {expected}"));
        }
    }
}
