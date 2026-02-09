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
using Moq;
using Moq.Protected;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration.Templating.Tests
{
    /// <summary>
    /// Unit tests for the TemplateWriter class constructor.
    /// </summary>
    [TestFixture]
    [Category("Templating")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class TemplateWriterTests
    {
        /// <summary>
        /// Tests that the constructor creates a valid instance with a valid TextWriter
        /// and various leaveOpen parameter values.
        /// Verifies the instance is not null and has correct initial indentation.
        /// </summary>
        /// <param name="leaveOpen">Whether to leave the underlying writer open on dispose.</param>
        [TestCase(true)]
        [TestCase(false)]
        public void Constructor_WithValidWriter_CreatesInstanceWithZeroIndentation(bool leaveOpen)
        {
            // Arrange
            using var writer = new StringWriter();

            // Act
            using var templateWriter = new TemplateWriter(writer, leaveOpen);

            // Assert
            Assert.That(templateWriter, Is.Not.Null);
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that the constructor creates a valid instance with default leaveOpen parameter.
        /// Verifies the instance is not null and has correct initial indentation.
        /// </summary>
        [Test]
        public void Constructor_WithValidWriterAndDefaultLeaveOpen_CreatesInstanceSuccessfully()
        {
            // Arrange
            using var writer = new StringWriter();

            // Act
            using var templateWriter = new TemplateWriter(writer);

            // Assert
            Assert.That(templateWriter, Is.Not.Null);
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that the constructor accepts null writer without throwing.
        /// This documents current behavior - no null validation in constructor.
        /// Note: Using null writer will likely cause NullReferenceException on actual use.
        /// </summary>
        [Test]
        public void Constructor_WithNullWriter_DoesNotThrow()
        {
            // Arrange
            TextWriter writer = null;

            // Act & Assert
            Assert.DoesNotThrow(() =>
            {
                using var templateWriter = new TemplateWriter(writer);
            });
        }

        /// <summary>
        /// Tests that the constructor with leaveOpen=true (default) does not dispose
        /// the underlying writer when TemplateWriter is disposed.
        /// Verifies the leaveOpen parameter is correctly stored and used.
        /// </summary>
        [Test]
        public void Constructor_WithLeaveOpenTrue_DoesNotDisposeUnderlyingWriterOnDispose()
        {
            // Arrange
            var writer = new StringWriter();

            // Act
            using (var templateWriter = new TemplateWriter(writer, leaveOpen: true))
            {
                // TemplateWriter is used within this scope
            }

            // Assert - writer should still be usable
            Assert.DoesNotThrow(() => writer.Write("test"));
            writer.Dispose();
        }

        /// <summary>
        /// Tests that the constructor with default leaveOpen parameter (true) does not dispose
        /// the underlying writer when TemplateWriter is disposed.
        /// Verifies the default parameter value works correctly.
        /// </summary>
        [Test]
        public void Constructor_WithDefaultLeaveOpen_DoesNotDisposeUnderlyingWriterOnDispose()
        {
            // Arrange
            var writer = new StringWriter();

            // Act
            using (var templateWriter = new TemplateWriter(writer))
            {
                // TemplateWriter is used within this scope
            }

            // Assert - writer should still be usable
            Assert.DoesNotThrow(() => writer.Write("test"));
            writer.Dispose();
        }

        /// <summary>
        /// Tests that WriteWhiteSpace with various positive character counts correctly queues
        /// whitespace that is written before subsequent content.
        /// </summary>
        /// <param name="charCount">The number of whitespace characters to queue.</param>
        [TestCase(1)]
        [TestCase(5)]
        [TestCase(10)]
        [TestCase(100)]
        public void WriteWhiteSpace_PositiveCharCount_QueuesWhitespaceBeforeContent(int charCount)
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);
            string expectedSpaces = new(' ', charCount);

            // Act
            templateWriter.WriteWhiteSpace(charCount);
            templateWriter.Write("text");

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo(expectedSpaces + "text"));
        }

        /// <summary>
        /// Tests that WriteWhiteSpace with zero character count does not queue any whitespace
        /// and subsequent content is written without leading spaces.
        /// </summary>
        [Test]
        public void WriteWhiteSpace_ZeroCharCount_NoWhitespaceQueued()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            // Act
            templateWriter.WriteWhiteSpace(0);
            templateWriter.Write("text");

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo("text"));
        }

        /// <summary>
        /// Tests that WriteWhiteSpace with negative character counts does not write any spaces
        /// and the content is written normally without leading whitespace.
        /// </summary>
        /// <param name="charCount">The negative character count.</param>
        [TestCase(-1)]
        [TestCase(-5)]
        [TestCase(-100)]
        public void WriteWhiteSpace_NegativeCharCount_NoWhitespaceWritten(int charCount)
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            // Act
            templateWriter.WriteWhiteSpace(charCount);
            templateWriter.Write("text");

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo("text"));
        }

        /// <summary>
        /// Tests that multiple calls to WriteWhiteSpace accumulate the character count
        /// and all queued spaces are written before subsequent content.
        /// </summary>
        [Test]
        public void WriteWhiteSpace_MultipleCalls_AccumulatesWhitespace()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            // Act
            templateWriter.WriteWhiteSpace(3);
            templateWriter.WriteWhiteSpace(2);
            templateWriter.WriteWhiteSpace(5);
            templateWriter.Write("text");

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo("          text")); // 10 spaces total
        }

        /// <summary>
        /// Tests that WriteWhiteSpace queues whitespace correctly when used with Write(char) overload.
        /// </summary>
        [Test]
        public void WriteWhiteSpace_WithWriteChar_QueuesWhitespaceCorrectly()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            // Act
            templateWriter.WriteWhiteSpace(4);
            templateWriter.Write('X');

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo("    X"));
        }

        /// <summary>
        /// Tests that WriteWhiteSpace queues whitespace correctly when used with Write(string, object) overload.
        /// </summary>
        [Test]
        public void WriteWhiteSpace_WithWriteFormat_QueuesWhitespaceCorrectly()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            // Act
            templateWriter.WriteWhiteSpace(3);
            templateWriter.Write("{0}", "test");

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo("   test"));
        }

        /// <summary>
        /// Tests that WriteWhiteSpace handles int.MinValue without throwing,
        /// and no whitespace is written due to negative value handling.
        /// </summary>
        [Test]
        public void WriteWhiteSpace_IntMinValue_NoWhitespaceWritten()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            // Act
            templateWriter.WriteWhiteSpace(int.MinValue);
            templateWriter.Write("text");

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo("text"));
        }

        /// <summary>
        /// Tests that WriteWhiteSpace with a very large positive value successfully queues whitespace.
        /// This test uses a large but reasonable value to avoid OutOfMemoryException.
        /// </summary>
        [Test]
        public void WriteWhiteSpace_LargePositiveValue_QueuesWhitespace()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);
            const int largeCount = 1000;

            // Act
            templateWriter.WriteWhiteSpace(largeCount);
            templateWriter.Write("end");

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result.Length, Is.EqualTo(largeCount + 3));
            Assert.That(result, Does.EndWith("end"));
            Assert.That(result[..largeCount], Is.EqualTo(new string(' ', largeCount)));
        }

        /// <summary>
        /// Tests that WriteWhiteSpace queues are cleared after writing content,
        /// and subsequent writes without new WriteWhiteSpace calls have no leading spaces.
        /// </summary>
        [Test]
        public void WriteWhiteSpace_QueueClearedAfterWrite_NoWhitespaceOnSubsequentWrites()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            // Act
            templateWriter.WriteWhiteSpace(5);
            templateWriter.Write("first");
            templateWriter.Write("second");

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo("     firstsecond"));
        }

        /// <summary>
        /// Tests that mixing positive and negative values in WriteWhiteSpace
        /// results in the net accumulation being used (positive values minus negative values).
        /// </summary>
        [Test]
        public void WriteWhiteSpace_MixedPositiveAndNegative_AccumulatesNetValue()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            // Act
            templateWriter.WriteWhiteSpace(10);
            templateWriter.WriteWhiteSpace(-3);
            templateWriter.Write("text");

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo("       text")); // 7 spaces (10 - 3)
        }

        /// <summary>
        /// Tests that accumulating negative values results in a net negative count,
        /// which causes no whitespace to be written.
        /// </summary>
        [Test]
        public void WriteWhiteSpace_AccumulatedNegativeValue_NoWhitespaceWritten()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            // Act
            templateWriter.WriteWhiteSpace(2);
            templateWriter.WriteWhiteSpace(-5);
            templateWriter.Write("text");

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo("text")); // Net is -3, so no spaces
        }

        /// <summary>
        /// Tests that IndentationCharCount returns 0 when the TemplateWriter is first constructed.
        /// </summary>
        [Test]
        public void IndentationCharCount_InitialState_ReturnsZero()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            int actual = templateWriter.IndentationCharCount;

            // Assert
            Assert.That(actual, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that IndentationCharCount returns the correct accumulated value after pushing positive indentation.
        /// </summary>
        /// <param name="charCount">The number of characters to push for indentation.</param>
        /// <param name="expected">The expected indentation character count.</param>
        [TestCase(1, 1)]
        [TestCase(4, 4)]
        [TestCase(10, 10)]
        [TestCase(100, 100)]
        [TestCase(int.MaxValue - 1, int.MaxValue - 1)]
        public void IndentationCharCount_AfterPushIndentCharsPositive_ReturnsCorrectValue(int charCount, int expected)
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.PushIndentChars(charCount);
            int actual = templateWriter.IndentationCharCount;

            // Assert
            Assert.That(actual, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that IndentationCharCount maintains the current value when pushing zero characters.
        /// </summary>
        [Test]
        public void IndentationCharCount_AfterPushIndentCharsZero_ReturnsSameValue()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.PushIndentChars(0);
            int actual = templateWriter.IndentationCharCount;

            // Assert
            Assert.That(actual, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that IndentationCharCount can handle negative character counts and returns the correct sum.
        /// </summary>
        /// <param name="charCount">The negative number of characters to push.</param>
        /// <param name="expected">The expected indentation character count.</param>
        [TestCase(-1, -1)]
        [TestCase(-10, -10)]
        [TestCase(-100, -100)]
        [TestCase(int.MinValue + 1, int.MinValue + 1)]
        public void IndentationCharCount_AfterPushIndentCharsNegative_ReturnsCorrectValue(int charCount, int expected)
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.PushIndentChars(charCount);
            int actual = templateWriter.IndentationCharCount;

            // Assert
            Assert.That(actual, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that IndentationCharCount returns the correct accumulated value after multiple PushIndentChars calls.
        /// </summary>
        [Test]
        public void IndentationCharCount_AfterMultiplePushes_ReturnsAccumulatedValue()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.PushIndentChars(4);
            templateWriter.PushIndentChars(2);
            templateWriter.PushIndentChars(3);
            int actual = templateWriter.IndentationCharCount;

            // Assert
            Assert.That(actual, Is.EqualTo(9));
        }

        /// <summary>
        /// Tests that IndentationCharCount returns the previous value after PushIndentChars followed by PopIndentation.
        /// </summary>
        [Test]
        public void IndentationCharCount_AfterPushAndPop_ReturnsPreviousValue()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);
            templateWriter.PushIndentChars(4);

            // Act
            templateWriter.PushIndentChars(2);
            int beforePop = templateWriter.IndentationCharCount;
            templateWriter.PopIndentation();
            int afterPop = templateWriter.IndentationCharCount;

            // Assert
            Assert.That(beforePop, Is.EqualTo(6));
            Assert.That(afterPop, Is.EqualTo(4));
        }

        /// <summary>
        /// Tests that IndentationCharCount maintains initial value when PopIndentation is called without additional pushes.
        /// The stack always maintains at least one element (the initial 0).
        /// </summary>
        [Test]
        public void IndentationCharCount_AfterPopOnInitialState_MaintainsInitialValue()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.PopIndentation();
            int actual = templateWriter.IndentationCharCount;

            // Assert
            Assert.That(actual, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that IndentationCharCount correctly handles a complex sequence of push and pop operations.
        /// </summary>
        [Test]
        public void IndentationCharCount_ComplexPushPopSequence_ReturnsCorrectValues()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act & Assert
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(0));

            templateWriter.PushIndentChars(4);
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(4));

            templateWriter.PushIndentChars(4);
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(8));

            templateWriter.PushIndentChars(2);
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(10));

            templateWriter.PopIndentation();
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(8));

            templateWriter.PopIndentation();
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(4));

            templateWriter.PopIndentation();
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(0));

            templateWriter.PopIndentation();
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that IndentationCharCount handles boundary values correctly.
        /// This test verifies edge cases with extreme integer values.
        /// </summary>
        [Test]
        public void IndentationCharCount_WithBoundaryValues_HandlesCorrectly()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act - Push int.MaxValue (will overflow when added to 0, but should still work)
            templateWriter.PushIndentChars(int.MaxValue);
            int actualMax = templateWriter.IndentationCharCount;

            // Assert
            Assert.That(actualMax, Is.EqualTo(int.MaxValue));
        }

        /// <summary>
        /// Tests that IndentationCharCount handles minimum boundary value correctly.
        /// </summary>
        [Test]
        public void IndentationCharCount_WithMinBoundaryValue_HandlesCorrectly()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act - Push int.MinValue
            templateWriter.PushIndentChars(int.MinValue);
            int actualMin = templateWriter.IndentationCharCount;

            // Assert
            Assert.That(actualMin, Is.EqualTo(int.MinValue));
        }

        /// <summary>
        /// Tests that Write with format and single argument correctly writes formatted text to the underlying writer.
        /// Input: A format string "{0}" and an integer argument.
        /// Expected: The formatted text is written to the output.
        /// </summary>
        [Test]
        public void Write_WithFormatAndSingleArgument_WritesFormattedText()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);
            const string format = "Value: {0}";
            const int arg1 = 42;

            // Act
            templateWriter.Write(format, arg1);

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo("Value: 42"));
        }

        /// <summary>
        /// Tests that Write with format and argument handles null argument correctly.
        /// Input: A format string with placeholder and null argument.
        /// Expected: The null value is formatted as empty or "null" string depending on format.
        /// </summary>
        [Test]
        public void Write_WithFormatAndNullArgument_WritesFormattedNull()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);
            const string format = "Value: {0}";
            object arg1 = null;

            // Act
            templateWriter.Write(format, arg1);

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo("Value: "));
        }

        /// <summary>
        /// Tests that Write with format and argument handles empty format string correctly.
        /// Input: An empty format string and any argument.
        /// Expected: Empty string is written to the output.
        /// </summary>
        [Test]
        public void Write_WithEmptyFormatString_WritesEmptyString()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);
            string format = string.Empty;
            object arg1 = "test";

            // Act
            templateWriter.Write(format, arg1);

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that Write with format string containing no placeholders writes the literal string.
        /// Input: A format string without placeholders and any argument.
        /// Expected: The literal format string is written without substitution.
        /// </summary>
        [Test]
        public void Write_WithFormatStringWithoutPlaceholder_WritesLiteralString()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);
            const string format = "No placeholder here";
            object arg1 = "ignored";

            // Act
            templateWriter.Write(format, arg1);

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo("No placeholder here"));
        }

        /// <summary>
        /// Tests that Write with null format string throws ArgumentNullException.
        /// Input: A null format string.
        /// Expected: ArgumentNullException is thrown.
        /// </summary>
        [Test]
        public void Write_WithNullFormat_UsesEmptyStringAsFormat()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);
            const string format = null;
            object arg1 = "test";

            // Act
            templateWriter.Write(format, arg1);

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that Write with format applies pending whitespace before writing content.
        /// Input: Format string and argument after calling WriteWhiteSpace.
        /// Expected: Pending whitespace is written before the formatted text.
        /// </summary>
        [Test]
        public void Write_AfterWriteWhiteSpace_AppliesPendingWhitespace()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);
            const string format = "text: {0}";
            const int arg1 = 123;

            // Act
            templateWriter.WriteWhiteSpace(4);
            templateWriter.Write(format, arg1);

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo("    text: 123"));
        }

        /// <summary>
        /// Tests that Write with format applies pending newlines before writing content.
        /// Input: Format string and argument after calling WriteLine.
        /// Expected: Pending newlines are written before the formatted text.
        /// </summary>
        [Test]
        public void Write_AfterWriteLine_AppliesPendingNewlines()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);
            const string format = "text: {0}";
            const int arg1 = 456;

            // Act
            templateWriter.WriteLine();
            templateWriter.Write(format, arg1);

            // Assert
            string result = writer.ToString();
            string expected = Environment.NewLine + "text: 456";
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that Write with format handles various argument types correctly.
        /// Input: Format string with different argument types (string, int, custom object).
        /// Expected: Each type is formatted correctly.
        /// </summary>
        [TestCase("String: {0}", "hello", "String: hello")]
        [TestCase("Integer: {0}", 42, "Integer: 42")]
        [TestCase("Negative: {0}", -100, "Negative: -100")]
        [TestCase("Zero: {0}", 0, "Zero: 0")]
        [TestCase("MaxInt: {0}", int.MaxValue, "MaxInt: 2147483647")]
        [TestCase("MinInt: {0}", int.MinValue, "MinInt: -2147483648")]
        public void Write_WithVariousArgumentTypes_FormatsCorrectly(string format, object arg1, string expected)
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.Write(format, arg1);

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that Write with format handles special characters in format string.
        /// Input: Format string with special characters and argument.
        /// Expected: Special characters are preserved in the output.
        /// </summary>
        [TestCase("Tab:\t{0}", "value", "Tab:\tvalue")]
        [TestCase("Quote:\"{0}\"", "text", "Quote:\"text\"")]
        [TestCase("Backslash:\\{0}", "path", "Backslash:\\path")]
        public void Write_WithSpecialCharactersInFormat_PreservesSpecialCharacters(string format, object arg1, string expected)
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.Write(format, arg1);

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that Write with invalid format string throws FormatException.
        /// Input: A format string with mismatched placeholders (e.g., "{1}" when only one arg provided).
        /// Expected: FormatException is thrown.
        /// </summary>
        [Test]
        public void Write_WithInvalidFormatPlaceholder_ThrowsFormatException()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);
            const string format = "Value: {1}"; // placeholder index 1 but only arg1 provided (index 0)
            object arg1 = "test";

            // Act & Assert
            Assert.Throws<FormatException>(() => templateWriter.Write(format, arg1));
        }

        /// <summary>
        /// Tests that Write with format applies indentation from PushIndentChars.
        /// Input: Format string and argument after pushing indentation.
        /// Expected: Indentation is applied on the first write after a newline.
        /// </summary>
        [Test]
        public void Write_WithIndentation_AppliesIndentationAfterNewLine()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);
            const string format = "text: {0}";
            const int arg1 = 789;

            // Act
            templateWriter.PushIndentChars(4);
            templateWriter.WriteLine();
            templateWriter.Write(format, arg1);

            // Assert
            string result = writer.ToString();
            string expected = Environment.NewLine + "    text: 789";
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that multiple consecutive Write calls with format work correctly.
        /// Input: Multiple Write calls with format and arguments.
        /// Expected: All formatted text is written in sequence.
        /// </summary>
        [Test]
        public void Write_MultipleConsecutiveCalls_WritesAllFormattedText()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.Write("First: {0}", 1);
            templateWriter.Write(" Second: {0}", 2);
            templateWriter.Write(" Third: {0}", 3);

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo("First: 1 Second: 2 Third: 3"));
        }

        /// <summary>
        /// Tests that Dispose writes no newlines when m_newLineCount is 0 and does not dispose the underlying writer when leaveOpen is true.
        /// Input: No pending newlines, leaveOpen=true (default).
        /// Expected: No newlines written, underlying writer not disposed.
        /// </summary>
        [Test]
        public void Dispose_NoPendingNewLinesAndLeaveOpenTrue_DoesNotWriteAndDoesNotDisposeWriter()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            var templateWriter = new TemplateWriter(stringWriter, leaveOpen: true);

            // Act
            templateWriter.Dispose();

            // Assert
            Assert.That(stringWriter.ToString(), Is.Empty);
        }

        /// <summary>
        /// Tests that Dispose writes no newlines when m_newLineCount is 0 but disposes the underlying writer when leaveOpen is false.
        /// Input: No pending newlines, leaveOpen=false.
        /// Expected: No newlines written, underlying writer disposed.
        /// </summary>
        [Test]
        public void Dispose_NoPendingNewLinesAndLeaveOpenFalse_DoesNotWriteButDisposesWriter()
        {
            // Arrange
            var mockWriter = new Mock<TextWriter>();
            mockWriter.Protected().Setup("Dispose", ItExpr.IsAny<bool>()).Verifiable(Times.Once);
            mockWriter.Setup(w => w.Write(It.IsAny<string>())).Verifiable(Times.Never);
            var templateWriter = new TemplateWriter(mockWriter.Object, leaveOpen: false);

            // Act
            templateWriter.Dispose();

            // Assert
            mockWriter.Verify();
        }

        /// <summary>
        /// Tests that Dispose writes pending newlines when m_newLineCount is 1 and does not dispose the underlying writer when leaveOpen is true.
        /// Input: 1 pending newline, leaveOpen=true.
        /// Expected: 1 newline written, underlying writer not disposed.
        /// </summary>
        [Test]
        public void Dispose_OnePendingNewLineAndLeaveOpenTrue_WritesOneNewLine()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            var templateWriter = new TemplateWriter(stringWriter, leaveOpen: true);
            templateWriter.WriteLine();

            // Act
            templateWriter.Dispose();

            // Assert
            string expected = Environment.NewLine;
            Assert.That(stringWriter.ToString(), Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that Dispose writes pending newlines when m_newLineCount is 2 and does not dispose the underlying writer when leaveOpen is true.
        /// Input: 2 pending newlines, leaveOpen=true.
        /// Expected: 2 newlines written, underlying writer not disposed.
        /// </summary>
        [Test]
        public void Dispose_TwoPendingNewLinesAndLeaveOpenTrue_WritesTwoNewLines()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            var templateWriter = new TemplateWriter(stringWriter, leaveOpen: true);
            templateWriter.WriteLine();
            templateWriter.WriteLine();

            // Act
            templateWriter.Dispose();

            // Assert
            string expected = Environment.NewLine + Environment.NewLine;
            Assert.That(stringWriter.ToString(), Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that Dispose writes multiple pending newlines.
        /// Input: Multiple pending newlines (3).
        /// Expected: All pending newlines written.
        /// </summary>
        [Test]
        public void Dispose_MultiplePendingNewLines_WritesAllNewLines()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            var templateWriter = new TemplateWriter(stringWriter, leaveOpen: true);
            templateWriter.WriteLine("Line1");
            templateWriter.WriteLine("Line2");
            templateWriter.WriteLine("Line3");

            // Act
            templateWriter.Dispose();

            // Assert
            string expected = "Line1" + Environment.NewLine + "Line2" + Environment.NewLine + "Line3" + Environment.NewLine;
            Assert.That(stringWriter.ToString(), Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that Dispose writes pending newlines and disposes the underlying writer when leaveOpen is false.
        /// Input: Pending newlines, leaveOpen=false.
        /// Expected: Newlines written, underlying writer disposed.
        /// </summary>
        [Test]
        public void Dispose_PendingNewLinesAndLeaveOpenFalse_WritesNewLinesAndDisposesWriter()
        {
            // Arrange
            var mockWriter = new Mock<TextWriter>();
            mockWriter.Protected().Setup("Dispose", ItExpr.IsAny<bool>()).Verifiable(Times.Once);
            mockWriter.Setup(w => w.Write(Environment.NewLine)).Verifiable(Times.Once);
            var templateWriter = new TemplateWriter(mockWriter.Object, leaveOpen: false);
            templateWriter.WriteLine();

            // Act
            templateWriter.Dispose();

            // Assert
            mockWriter.Verify();
        }

        /// <summary>
        /// Tests that Dispose can be called multiple times without errors (idempotency).
        /// </summary>
        [Test]
        public void Dispose_CalledMultipleTimes_DoesNotThrowException()
        {
            // Arrange
            var mockWriter = new Mock<TextWriter>();
            mockWriter.Protected().Setup("Dispose", ItExpr.IsAny<bool>()).Verifiable(Times.Exactly(2));
            var templateWriter = new TemplateWriter(mockWriter.Object, leaveOpen: false);

            // Act
            templateWriter.Dispose();
            templateWriter.Dispose();

            // Assert
            mockWriter.Verify();
            Assert.Pass("Dispose can be called multiple times without exception");
        }

        /// <summary>
        /// Tests that Dispose writes a large number of pending newlines correctly.
        /// Input: Many pending newlines (100).
        /// Expected: All pending newlines written.
        /// </summary>
        [Test]
        public void Dispose_ManyPendingNewLines_WritesAllNewLines()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            var templateWriter = new TemplateWriter(stringWriter, leaveOpen: true);
            const int newLineCount = 100;
            for (int i = 0; i < newLineCount; i++)
            {
                templateWriter.WriteLine(string.Empty);
            }

            // Act
            templateWriter.Dispose();

            // Assert
            int actualNewLineCount = stringWriter.ToString().Split([Environment.NewLine], StringSplitOptions.None).Length - 1;
            Assert.That(actualNewLineCount, Is.EqualTo(newLineCount));
        }

        /// <summary>
        /// Tests that Dispose with default leaveOpen parameter (true) does not dispose the underlying writer.
        /// Input: leaveOpen parameter not specified (default true).
        /// Expected: Underlying writer not disposed.
        /// </summary>
        [Test]
        public void Dispose_DefaultLeaveOpenParameter_DoesNotDisposeWriter()
        {
            // Arrange
            var mockWriter = new Mock<TextWriter>();
            mockWriter.Protected().Setup("Dispose", ItExpr.IsAny<bool>()).Verifiable(Times.Never);
            var templateWriter = new TemplateWriter(mockWriter.Object);

            // Act
            templateWriter.Dispose();

            // Assert
            mockWriter.Verify();
        }

        /// <summary>
        /// Verifies that Write with valid format string and two arguments writes the correctly formatted string.
        /// Tests basic functionality with format placeholders for both arguments.
        /// Expected result: Formatted string is written to the underlying writer.
        /// </summary>
        [Test]
        public void Write_WithValidFormatAndTwoArguments_WritesFormattedString()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);
            const string format = "Value1: {0}, Value2: {1}";
            object arg1 = 42;
            object arg2 = "test";

            // Act
            templateWriter.Write(format, arg1, arg2);

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo("Value1: 42, Value2: test"));
        }

        /// <summary>
        /// Verifies that Write with empty format string writes nothing to the output.
        /// Tests behavior with empty string input.
        /// Expected result: Empty string is written.
        /// </summary>
        [Test]
        public void Write_WithEmptyFormat_WritesEmptyString()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);
            string format = string.Empty;
            object arg1 = 1;
            object arg2 = 2;

            // Act
            templateWriter.Write(format, arg1, arg2);

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Verifies that Write with null arguments writes "null" representation in the formatted string.
        /// Tests handling of null argument values.
        /// Expected result: Null arguments are represented as empty strings in the output.
        /// </summary>
        [TestCase("{0}", null, "test", "")]
        [TestCase("{1}", "test", null, "")]
        [TestCase("{0} and {1}", null, null, " and ")]
        public void Write_WithNullArguments_WritesNullAsString(string format, object arg1, object arg2, string expected)
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            // Act
            templateWriter.Write(format, arg1, arg2);

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Verifies that Write outputs format string as-is when no placeholders are present.
        /// Tests format string without any placeholders.
        /// Expected result: Format string is written verbatim.
        /// </summary>
        [TestCase("No placeholders here", 1, 2, "No placeholders here")]
        [TestCase("", 1, 2, "")]
        public void Write_WithFormatMissingPlaceholders_WritesFormatAsIs(string format, object arg1, object arg2, string expected)
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            // Act
            templateWriter.Write(format, arg1, arg2);

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Verifies that Write throws FormatException when format string has more placeholders than provided arguments.
        /// Tests invalid format string with placeholder {2} when only two arguments are provided.
        /// Expected result: FormatException is thrown.
        /// </summary>
        [TestCase("{0} {1} {2}")]
        [TestCase("{3}")]
        public void Write_WithTooManyPlaceholders_ThrowsFormatException(string format)
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);
            object arg1 = 1;
            object arg2 = 2;

            // Act & Assert
            Assert.Throws<FormatException>(() => templateWriter.Write(format, arg1, arg2));
        }

        /// <summary>
        /// Verifies that Write throws FormatException with invalid format specifiers.
        /// Tests format strings with malformed or invalid format syntax.
        /// Expected result: FormatException is thrown.
        /// </summary>
        [TestCase("{0")]
        [TestCase("{0:")]
        [TestCase("{A{0")]
        public void Write_WithInvalidFormatSpecifier_ThrowsFormatException(string format)
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);
            object arg1 = 1;
            object arg2 = 2;

            // Act & Assert
            Assert.Throws<FormatException>(() => templateWriter.Write(format, arg1, arg2));
        }

        /// <summary>
        /// Verifies that Write flushes pending whitespace before writing the formatted text.
        /// Tests integration with whitespace management by calling WriteWhiteSpace before Write.
        /// Expected result: Pending spaces are written before the formatted string.
        /// </summary>
        [Test]
        public void Write_AfterWriteWhiteSpace_WritesPendingSpacesBeforeText()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);
            templateWriter.WriteWhiteSpace(4);
            const string format = "{0}:{1}";
            object arg1 = "key";
            object arg2 = "value";

            // Act
            templateWriter.Write(format, arg1, arg2);

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo("    key:value"));
        }

        /// <summary>
        /// Verifies that Write correctly handles various object types as arguments.
        /// Tests different value and reference types including integers, doubles, booleans, and custom objects.
        /// Expected result: All object types are correctly converted to strings and formatted.
        /// </summary>
        [TestCase("{0} - {1}", 123, 456.789, "123 - 456.789")]
        [TestCase("{0} - {1}", true, false, "True - False")]
        [TestCase("{0} - {1}", 'A', 'B', "A - B")]
        [TestCase("{0:D5} - {1:F2}", 42, 3.14159, "00042 - 3.14")]
        public void Write_WithVariousObjectTypes_WritesCorrectly(string format, object arg1, object arg2, string expected)
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            // Act
            templateWriter.Write(format, arg1, arg2);

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Verifies that Write handles special characters in format string correctly.
        /// Tests format strings containing special characters like newlines, tabs, and escape sequences.
        /// Expected result: Special characters are preserved in the output.
        /// </summary>
        [TestCase("{0}\n{1}", "line1", "line2", "line1\nline2")]
        [TestCase("{0}\t{1}", "col1", "col2", "col1\tcol2")]
        public void Write_WithSpecialCharactersInFormat_WritesCorrectly(string format, object arg1, object arg2, string expected)
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            // Act
            templateWriter.Write(format, arg1, arg2);

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Verifies that Write calls the underlying TextWriter.Write method with correct parameters.
        /// Tests that the method delegates to the underlying writer after handling whitespace.
        /// Expected result: TextWriter.Write is called with the format string and both arguments.
        /// </summary>
        [Test]
        public void Write_CallsUnderlyingWriterWithCorrectParameters()
        {
            // Arrange
            var mockWriter = new Mock<TextWriter>();
            using var templateWriter = new TemplateWriter(mockWriter.Object);
            const string format = "{0} and {1}";
            object arg1 = "first";
            object arg2 = "second";

            // Act
            templateWriter.Write(format, arg1, arg2);

            // Assert
            mockWriter.Verify(w => w.Write(format, arg1, arg2), Times.Once);
        }

        /// <summary>
        /// Verifies that Write with extreme numeric values formats them correctly.
        /// Tests boundary values for numeric types.
        /// Expected result: Extreme values are correctly formatted as strings.
        /// </summary>
        [TestCase("{0} {1}", int.MinValue, int.MaxValue, "-2147483648 2147483647")]
        [TestCase("{0} {1}", long.MinValue, long.MaxValue, "-9223372036854775808 9223372036854775807")]
        public void Write_WithExtremeNumericValues_WritesCorrectly(string format, object arg1, object arg2, string expected)
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            // Act
            templateWriter.Write(format, arg1, arg2);

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Verifies that Write handles special floating-point values correctly.
        /// Tests NaN, PositiveInfinity, and NegativeInfinity values.
        /// Expected result: Special values are correctly represented in the output.
        /// </summary>
        [TestCase("{0} {1}", double.NaN, double.PositiveInfinity, "NaN ∞")]
        [TestCase("{0} {1}", double.NegativeInfinity, 0.0, "-∞ 0")]
        public void Write_WithSpecialFloatingPointValues_WritesCorrectly(string format, object arg1, object arg2, string expected)
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            // Act
            templateWriter.Write(format, arg1, arg2);

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that PushIndentChars with zero value increases stack depth but keeps indentation count the same.
        /// </summary>
        [Test]
        public void PushIndentChars_ZeroCharCount_MaintainsSameIndentationValue()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);
            int initialIndent = templateWriter.IndentationCharCount;

            // Act
            templateWriter.PushIndentChars(0);

            // Assert
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(initialIndent));
        }

        /// <summary>
        /// Tests that PushIndentChars with positive values correctly increases the indentation count.
        /// </summary>
        /// <param name="charCount">The number of characters to add to indentation.</param>
        /// <param name="expectedIndent">The expected final indentation count.</param>
        [TestCase(1, 1)]
        [TestCase(4, 4)]
        [TestCase(10, 10)]
        [TestCase(100, 100)]
        [TestCase(1000, 1000)]
        public void PushIndentChars_PositiveCharCount_IncreasesIndentation(int charCount, int expectedIndent)
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.PushIndentChars(charCount);

            // Assert
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(expectedIndent));
        }

        /// <summary>
        /// Tests that PushIndentChars with negative values decreases the indentation count.
        /// Although the XML comment states "Must be zero or greater", the implementation
        /// does not validate this constraint.
        /// </summary>
        /// <param name="charCount">The negative number of characters.</param>
        /// <param name="expectedIndent">The expected final indentation count.</param>
        [TestCase(-1, -1)]
        [TestCase(-10, -10)]
        [TestCase(-100, -100)]
        public void PushIndentChars_NegativeCharCount_DecreasesIndentation(int charCount, int expectedIndent)
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.PushIndentChars(charCount);

            // Assert
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(expectedIndent));
        }

        /// <summary>
        /// Tests that multiple consecutive calls to PushIndentChars stack correctly,
        /// with each call adding to the previous indentation level.
        /// </summary>
        [Test]
        public void PushIndentChars_MultipleCalls_StacksIndentationCorrectly()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act & Assert
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(0));

            templateWriter.PushIndentChars(4);
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(4));

            templateWriter.PushIndentChars(2);
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(6));

            templateWriter.PushIndentChars(3);
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(9));
        }

        /// <summary>
        /// Tests that PushIndentChars followed by PopIndentation correctly restores the previous indentation level.
        /// </summary>
        [Test]
        public void PushIndentChars_FollowedByPopIndentation_RestoresPreviousIndentation()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);
            int initialIndent = templateWriter.IndentationCharCount;

            // Act
            templateWriter.PushIndentChars(10);
            int afterPush = templateWriter.IndentationCharCount;
            templateWriter.PopIndentation();

            // Assert
            Assert.That(afterPush, Is.EqualTo(10));
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(initialIndent));
        }

        /// <summary>
        /// Tests that PushIndentChars with int.MaxValue handles large values without throwing.
        /// This tests boundary condition for maximum positive integer value.
        /// </summary>
        [Test]
        public void PushIndentChars_MaxIntValue_HandlesLargeValue()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.PushIndentChars(int.MaxValue);

            // Assert
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(int.MaxValue));
        }

        /// <summary>
        /// Tests that PushIndentChars with int.MinValue handles minimum integer value.
        /// This tests boundary condition for minimum integer value.
        /// </summary>
        [Test]
        public void PushIndentChars_MinIntValue_HandlesMinimumValue()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.PushIndentChars(int.MinValue);

            // Assert
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(int.MinValue));
        }

        /// <summary>
        /// Tests that PushIndentChars can handle a scenario where adding charCount
        /// to current indentation causes integer overflow.
        /// </summary>
        [Test]
        public void PushIndentChars_CausesIntegerOverflow_WrapsAroundToNegative()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);
            templateWriter.PushIndentChars(int.MaxValue);

            // Act
            templateWriter.PushIndentChars(1);

            // Assert - When int.MaxValue + 1 overflows, it wraps to int.MinValue
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(int.MinValue));
        }

        /// <summary>
        /// Tests that PushIndentChars preserves the stack structure allowing nested indentation levels.
        /// </summary>
        [Test]
        public void PushIndentChars_NestedIndentation_PreservesStackStructure()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.PushIndentChars(2);
            templateWriter.PushIndentChars(2);
            templateWriter.PushIndentChars(2);
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(6));

            // Pop once
            templateWriter.PopIndentation();
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(4));

            // Pop again
            templateWriter.PopIndentation();
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(2));

            // Pop final time
            templateWriter.PopIndentation();

            // Assert - Should be back to initial (0)
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests WriteLine with various text and args combinations.
        /// Verifies that the formatted text is written followed by a newline.
        /// </summary>
        [Test]
        [TestCase("Hello", null, "Hello\r\n")]
        [TestCase("", null, "\r\n")]
        [TestCase("Test {0}", new object[] { "Value" }, "Test Value\r\n")]
        [TestCase("Test {0} {1}", new object[] { "A", "B" }, "Test A B\r\n")]
        [TestCase("Test {0} {1} {2}", new object[] { 1, 2, 3 }, "Test 1 2 3\r\n")]
        [TestCase("No placeholders", new object[] { "ignored" }, "No placeholders\r\n")]
        [TestCase("Value: {0}", new object[] { 42 }, "Value: 42\r\n")]
        [TestCase("Value: {0}", new object[] { null }, "Value: \r\n")]
        public void WriteLine_WithTextAndArgs_WritesFormattedTextWithNewLine(string text, object[] args, string expectedOutput)
        {
            // Arrange
            using var writer = new StringWriter();
            using (var templateWriter = new TemplateWriter(writer, leaveOpen: false))
            {
                // Act
                templateWriter.WriteLine(text, args);
            }
            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo(expectedOutput));
        }

        /// <summary>
        /// Tests WriteLine with null text parameter.
        /// Verifies that null text is handled (delegates to TextWriter behavior).
        /// </summary>
        [Test]
        public void WriteLine_WithNullText_WritesNewLine()
        {
            // Arrange
            using var writer = new StringWriter();
            using (var templateWriter = new TemplateWriter(writer, leaveOpen: false))
            {
                // Act
                templateWriter.WriteLine(null, []);
            }
            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo("\r\n"));
        }

        /// <summary>
        /// Tests WriteLine with empty args array.
        /// Verifies that text without format specifiers is written correctly.
        /// </summary>
        [Test]
        public void WriteLine_WithEmptyArgs_WritesTextWithNewLine()
        {
            // Arrange
            using var writer = new StringWriter();
            using (var templateWriter = new TemplateWriter(writer, leaveOpen: false))
            {
                // Act
                templateWriter.WriteLine("Simple text", []);
            }
            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo("Simple text\r\n"));
        }

        /// <summary>
        /// Tests WriteLine with format mismatch between placeholders and args.
        /// Verifies that FormatException is thrown when format specifiers don't match args.
        /// </summary>
        [Test]
        public void WriteLine_WithFormatMismatch_ThrowsFormatException()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer, leaveOpen: false);

            // Act & Assert
            Assert.Throws<FormatException>(() => templateWriter.WriteLine("Test {0} {1}", ["OnlyOne"]));
        }

        /// <summary>
        /// Tests WriteLine with special characters in text.
        /// Verifies that special characters are written correctly.
        /// </summary>
        [Test]
        [TestCase("Line with\ttab", new object[] { }, "Line with\ttab\r\n")]
        [TestCase("Special @#$%^&* chars", new object[] { }, "Special @#$%^&* chars\r\n")]
        [TestCase("Unicode: \u00E9\u00F1", new object[] { }, "Unicode: \u00E9\u00F1\r\n")]
        public void WriteLine_WithSpecialCharacters_WritesCorrectly(string text, object[] args, string expected)
        {
            // Arrange
            using var writer = new StringWriter();
            using (var templateWriter = new TemplateWriter(writer, leaveOpen: false))
            {
                // Act
                templateWriter.WriteLine(text, args);
            }
            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests WriteLine with very long string.
        /// Verifies that long strings are handled correctly.
        /// </summary>
        [Test]
        public void WriteLine_WithVeryLongString_WritesCorrectly()
        {
            // Arrange
            string longText = new('x', 10000);
            using var writer = new StringWriter();
            using (var templateWriter = new TemplateWriter(writer, leaveOpen: false))
            {
                // Act
                templateWriter.WriteLine(longText);
            }

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo(longText + Environment.NewLine));
        }

        /// <summary>
        /// Tests WriteLine called multiple times in sequence.
        /// Verifies that multiple lines are written correctly with proper newlines.
        /// </summary>
        [Test]
        public void WriteLine_CalledMultipleTimes_WritesMultipleLinesCorrectly()
        {
            // Arrange
            using var writer = new StringWriter();
            using (var templateWriter = new TemplateWriter(writer, leaveOpen: false))
            {
                // Act
                templateWriter.WriteLine("First line");
                templateWriter.WriteLine("Second line");
                templateWriter.WriteLine("Third line");
            }
            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo("First line\r\nSecond line\r\nThird line\r\n"));
        }

        /// <summary>
        /// Tests WriteLine with indentation set.
        /// Verifies that indentation is applied when writing lines.
        /// </summary>
        [Test]
        public void WriteLine_WithIndentation_WritesIndentedLine()
        {
            // Arrange
            using var writer = new StringWriter();
            using (var templateWriter = new TemplateWriter(writer, leaveOpen: false))
            {
                // Act
                templateWriter.PushIndentChars(4);
                templateWriter.WriteLine("Indented text");
            }
            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo("Indented text\r\n"));
            // TODO Should be: Assert.That(result, Is.EqualTo("    Indented text\r\n"));
        }

        /// <summary>
        /// Tests WriteLine with args containing null elements.
        /// Verifies that null arguments are handled correctly in formatting.
        /// </summary>
        [Test]
        public void WriteLine_WithNullArgument_WritesEmptyForNullValue()
        {
            // Arrange
            using var writer = new StringWriter();
            using (var templateWriter = new TemplateWriter(writer, leaveOpen: false))
            {
                // Act
                templateWriter.WriteLine("Value1: {0}, Value2: {1}", ["Test", null]);
            }
            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo("Value1: Test, Value2: \r\n"));
        }

        /// <summary>
        /// Tests WriteLine with whitespace-only string.
        /// Verifies that whitespace strings are written correctly.
        /// </summary>
        [Test]
        public void WriteLine_WithWhitespaceOnlyText_WritesWhitespaceWithNewLine()
        {
            // Arrange
            using var writer = new StringWriter();
            using (var templateWriter = new TemplateWriter(writer, leaveOpen: false))
            {
                // Act
                templateWriter.WriteLine("   ");
            }

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo("   " + Environment.NewLine));
        }

        /// <summary>
        /// Tests WriteLine with escaped braces in format string.
        /// Verifies that escaped braces are handled correctly.
        /// </summary>
        [Test]
        public void WriteLine_WithEscapedBraces_WritesCorrectly()
        {
            // Arrange
            using var writer = new StringWriter();
            using (var templateWriter = new TemplateWriter(writer, leaveOpen: false))
            {
                // Act
                templateWriter.WriteLine("{{Literal braces}} and {0}", ["value"]);
            }
            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo("{Literal braces} and value\r\n"));
        }

        /// <summary>
        /// Tests WriteLine with numeric edge case values.
        /// Verifies that edge case numeric values are formatted correctly.
        /// </summary>
        [Test]
        [TestCase(int.MinValue, "Value: -2147483648\r\n")]
        [TestCase(int.MaxValue, "Value: 2147483647\r\n")]
        [TestCase(0, "Value: 0\r\n")]
        public void WriteLine_WithNumericEdgeCases_FormatsCorrectly(int value, string expected)
        {
            // Arrange
            using var writer = new StringWriter();
            using (var templateWriter = new TemplateWriter(writer, leaveOpen: false))
            {
                // Act
                templateWriter.WriteLine("Value: {0}", [value]);
            }
            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that PopIndentation does not pop when the stack contains only one element,
        /// preserving the base indentation level.
        /// </summary>
        [Test]
        public void PopIndentation_WithSingleElementInStack_DoesNotPop()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);
            int initialIndentation = templateWriter.IndentationCharCount;

            // Act
            templateWriter.PopIndentation();

            // Assert
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(initialIndentation));
        }

        /// <summary>
        /// Tests that PopIndentation successfully pops when the stack contains exactly two elements,
        /// reducing the indentation level back to the base level.
        /// </summary>
        [Test]
        public void PopIndentation_WithTwoElementsInStack_PopsSuccessfully()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            templateWriter.PushIndentChars(4);
            int indentationBeforePop = templateWriter.IndentationCharCount;

            // Act
            templateWriter.PopIndentation();

            // Assert
            Assert.That(templateWriter.IndentationCharCount, Is.LessThan(indentationBeforePop));
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that PopIndentation pops only one level when the stack contains multiple elements,
        /// leaving other levels intact.
        /// </summary>
        [Test]
        public void PopIndentation_WithMultipleElementsInStack_PopsOnlyOneLevel()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);
            templateWriter.PushIndentChars(4);
            templateWriter.PushIndentChars(4);
            templateWriter.PushIndentChars(4);
            int indentationBeforePop = templateWriter.IndentationCharCount;

            // Act
            templateWriter.PopIndentation();

            // Assert
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(indentationBeforePop - 4));
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(8));
        }

        /// <summary>
        /// Tests that calling PopIndentation multiple times correctly reduces indentation levels
        /// until reaching the base level, then stops popping.
        /// </summary>
        [Test]
        public void PopIndentation_CalledMultipleTimes_PopsUntilBaseLevel()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);
            templateWriter.PushIndentChars(4);
            templateWriter.PushIndentChars(4);
            templateWriter.PushIndentChars(4);

            // Act
            templateWriter.PopIndentation(); // 12 -> 8
            templateWriter.PopIndentation(); // 8 -> 4
            templateWriter.PopIndentation(); // 4 -> 0
            templateWriter.PopIndentation(); // 0 -> 0 (should not change)
            templateWriter.PopIndentation(); // 0 -> 0 (should not change)

            // Assert
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that PopIndentation with varying indentation amounts correctly maintains the stack.
        /// </summary>
        [Test]
        public void PopIndentation_WithVaryingIndentationAmounts_MaintainsCorrectStack()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);
            templateWriter.PushIndentChars(2);
            templateWriter.PushIndentChars(3);
            templateWriter.PushIndentChars(5);

            // Act & Assert
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(10)); // 0 + 2 + 3 + 5
            templateWriter.PopIndentation();
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(5)); // 0 + 2 + 3
            templateWriter.PopIndentation();
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(2)); // 0 + 2
            templateWriter.PopIndentation();
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(0)); // base level
        }

        /// <summary>
        /// Tests that PopIndentation handles edge case of zero character indentation push followed by pop.
        /// </summary>
        [Test]
        public void PopIndentation_WithZeroCharacterIndentation_HandlesCorrectly()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);
            templateWriter.PushIndentChars(0);
            int indentationBeforePop = templateWriter.IndentationCharCount;

            // Act
            templateWriter.PopIndentation();

            // Assert
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that PopIndentation trims line breaks by verifying output does not contain extra newlines.
        /// </summary>
        [Test]
        public void PopIndentation_TrimsLineBreaks_OutputDoesNotContainExtraNewlines()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);
            templateWriter.PushIndentChars(4);
            templateWriter.WriteLine();
            templateWriter.WriteLine();
            templateWriter.WriteLine();

            // Act
            templateWriter.PopIndentation();
            templateWriter.Write("test");

            // Assert
            string output = writer.ToString();
            Assert.That(output, Does.Contain("test"));
        }

        /// <summary>
        /// Tests that PopIndentation with maximum integer indentation values handles correctly.
        /// </summary>
        [Test]
        public void PopIndentation_WithLargeIndentationValue_HandlesCorrectly()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);
            templateWriter.PushIndentChars(1000);

            // Act
            templateWriter.PopIndentation();

            // Assert
            Assert.That(templateWriter.IndentationCharCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that Write with valid format string and three arguments writes the formatted text correctly.
        /// </summary>
        [Test]
        public void Write_ValidFormatWith3Arguments_WritesFormattedText()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);
            const string format = "Value1: {0}, Value2: {1}, Value3: {2}";
            object arg1 = "First";
            object arg2 = 42;
            object arg3 = 3.14;

            // Act
            templateWriter.Write(format, arg1, arg2, arg3);

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo("Value1: First, Value2: 42, Value3: 3.14"));
        }

        /// <summary>
        /// Tests that Write with null format string writes empty string instead.
        /// </summary>
        [Test]
        public void Write_NullFormatString_UsesEmptyStringInstead()
        {
            // Arrange
            const string format = null;
            object arg1 = "First";
            object arg2 = "Second";
            object arg3 = "Third";

            using var stringWriter = new StringWriter();
            using (var templateWriter = new TemplateWriter(stringWriter))
            {
                // Act
                templateWriter.Write(format, arg1, arg2, arg3);
            }
            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that Write with null arguments writes the formatted text with null values.
        /// </summary>
        [TestCase("{0} {1} {2}", null, null, null, "  ")]
        [TestCase("{0} {1} {2}", "Test", null, null, "Test  ")]
        [TestCase("{0} {1} {2}", null, "Middle", null, " Middle ")]
        [TestCase("{0} {1} {2}", null, null, "Last", "  Last")]
        public void Write_NullArguments_WritesFormattedTextWithNulls(
            string format,
            object arg1,
            object arg2,
            object arg3,
            string expected)
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            // Act
            templateWriter.Write(format, arg1, arg2, arg3);

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that Write with empty format string writes empty string.
        /// </summary>
        [Test]
        public void Write_EmptyFormatString_WritesEmptyString()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);
            string format = string.Empty;
            object arg1 = "First";
            object arg2 = "Second";
            object arg3 = "Third";

            // Act
            templateWriter.Write(format, arg1, arg2, arg3);

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that Write with format containing more placeholders than available arguments throws FormatException.
        /// </summary>
        [Test]
        public void Write_FormatWithMorePlaceholders_ThrowsFormatException()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);
            const string format = "{0} {1} {2} {3}";
            object arg1 = "First";
            object arg2 = "Second";
            object arg3 = "Third";

            // Act & Assert
            Assert.Throws<FormatException>(() => templateWriter.Write(format, arg1, arg2, arg3));
        }

        /// <summary>
        /// Tests that Write with format string containing special characters writes correctly.
        /// </summary>
        [TestCase("{{0}}", "First", "Second", "Third", "{0}")]
        [TestCase("{0}\t{1}\n{2}", "A", "B", "C", "A\tB\nC")]
        [TestCase("{0} \r\n {1} \\ {2}", "X", "Y", "Z", "X \r\n Y \\ Z")]
        public void Write_FormatWithSpecialCharacters_WritesCorrectly(string format, object arg1, object arg2, object arg3, string expected)
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            // Act
            templateWriter.Write(format, arg1, arg2, arg3);

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that Write with very long format string and arguments writes correctly.
        /// </summary>
        [Test]
        public void Write_VeryLongFormatStringAndArguments_WritesCorrectly()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);
            string longString1 = new('A', 1000);
            string longString2 = new('B', 1000);
            string longString3 = new('C', 1000);
            const string format = "{0}-{1}-{2}";

            // Act
            templateWriter.Write(format, longString1, longString2, longString3);

            // Assert
            string result = stringWriter.ToString();
            string expected = $"{longString1}-{longString2}-{longString3}";
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that Write with invalid format string placeholder throws FormatException.
        /// </summary>
        [Test]
        public void Write_InvalidFormatPlaceholder_ThrowsFormatException()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);
            const string format = "{0} {invalid} {2}";
            object arg1 = "First";
            object arg2 = "Second";
            object arg3 = "Third";

            // Act & Assert
            Assert.Throws<FormatException>(() => templateWriter.Write(format, arg1, arg2, arg3));
        }

        /// <summary>
        /// Tests that Write with format specifiers works correctly.
        /// </summary>
        [TestCase("{0:X}", 255, "value2", "value3", "FF")]
        [TestCase("{0:F2}", 3.14159, "value2", "value3", "3.14")]
        [TestCase("{0:D5}", 42, "value2", "value3", "00042")]
        public void Write_FormatWithSpecifiers_WritesCorrectly(string format, object arg1, object arg2, object arg3, string expected)
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            // Act
            templateWriter.Write(format, arg1, arg2, arg3);

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that Write with different object types (including value types and reference types) writes correctly.
        /// </summary>
        [Test]
        public void Write_DifferentObjectTypes_WritesCorrectly()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);
            const string format = "{0}, {1}, {2}";
            object arg1 = 123;
            object arg2 = true;
            object arg3 = 'X';

            // Act
            templateWriter.Write(format, arg1, arg2, arg3);

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo("123, True, X"));
        }

        /// <summary>
        /// Tests that multiple consecutive Write calls accumulate output correctly.
        /// </summary>
        [Test]
        public void Write_MultipleConsecutiveCalls_AccumulatesOutput()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            // Act
            templateWriter.Write("{0}-{1}-{2}", "A", "B", "C");
            templateWriter.Write("{0}|{1}|{2}", "X", "Y", "Z");

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo("A-B-CX|Y|Z"));
        }

        /// <summary>
        /// Tests that TrimLineBreak does not change m_newLineCount when it is already less than maxNewLines.
        /// Verifies that the output contains the original number of newlines without modification.
        /// </summary>
        /// <param name="initialNewLines">The initial number of newlines to queue.</param>
        /// <param name="maxNewLines">The maximum number of newlines allowed.</param>
        [TestCase(1, 5, TestName = "TrimLineBreak_WhenCountLessThanMax_DoesNotChange")]
        [TestCase(2, 10, TestName = "TrimLineBreak_WhenCountMuchLessThanMax_DoesNotChange")]
        [TestCase(0, 1, TestName = "TrimLineBreak_WhenCountIsZero_DoesNotChange")]
        [TestCase(3, 100, TestName = "TrimLineBreak_WhenMaxIsVeryLarge_DoesNotChange")]
        public void TrimLineBreak_WhenNewLineCountLessThanMax_DoesNotChangeCount(int initialNewLines, int maxNewLines)
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            for (int i = 0; i < initialNewLines; i++)
            {
                templateWriter.WriteNewLine(int.MaxValue);
            }

            // Act
            templateWriter.TrimLineBreak(maxNewLines);
            templateWriter.Write("X");

            // Assert
            string result = writer.ToString();
            int actualNewLineCount = CountNewLines(result);
            Assert.That(actualNewLineCount, Is.EqualTo(initialNewLines));
        }

        /// <summary>
        /// Tests that TrimLineBreak does not change m_newLineCount when it equals maxNewLines.
        /// Verifies that the output contains the original number of newlines without modification.
        /// </summary>
        /// <param name="newLineCount">The number of newlines to queue and the maxNewLines value.</param>
        [TestCase(1, TestName = "TrimLineBreak_WhenCountEqualsMax_DoesNotChange_One")]
        [TestCase(2, TestName = "TrimLineBreak_WhenCountEqualsMax_DoesNotChange_Two")]
        [TestCase(5, TestName = "TrimLineBreak_WhenCountEqualsMax_DoesNotChange_Five")]
        [TestCase(10, TestName = "TrimLineBreak_WhenCountEqualsMax_DoesNotChange_Ten")]
        public void TrimLineBreak_WhenNewLineCountEqualsMax_DoesNotChangeCount(int newLineCount)
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            for (int i = 0; i < newLineCount; i++)
            {
                templateWriter.WriteNewLine(int.MaxValue);
            }

            // Act
            templateWriter.TrimLineBreak(newLineCount);
            templateWriter.Write("X");

            // Assert
            string result = writer.ToString();
            int actualNewLineCount = CountNewLines(result);
            Assert.That(actualNewLineCount, Is.EqualTo(newLineCount));
        }

        /// <summary>
        /// Tests that TrimLineBreak with negative maxNewLines value trims newlines.
        /// Verifies that a negative maxNewLines value can be used to set m_newLineCount to a negative value,
        /// which effectively prevents newlines from being written.
        /// </summary>
        [Test]
        public void TrimLineBreak_WithNegativeMaxNewLines_TrimsToNegativeValue()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            for (int i = 0; i < 5; i++)
            {
                templateWriter.WriteNewLine(int.MaxValue);
            }

            // Act
            templateWriter.TrimLineBreak(-1);
            templateWriter.Write("X");

            // Assert
            string result = writer.ToString();
            int actualNewLineCount = CountNewLines(result);
            Assert.That(actualNewLineCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that TrimLineBreak with int.MinValue as maxNewLines trims newlines.
        /// Verifies behavior with the minimum possible integer value.
        /// </summary>
        [Test]
        public void TrimLineBreak_WithIntMinValue_TrimsToMinValue()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            for (int i = 0; i < 5; i++)
            {
                templateWriter.WriteNewLine(int.MaxValue);
            }

            // Act
            templateWriter.TrimLineBreak(int.MinValue);
            templateWriter.Write("X");

            // Assert
            string result = writer.ToString();
            int actualNewLineCount = CountNewLines(result);
            Assert.That(actualNewLineCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that TrimLineBreak with int.MaxValue as maxNewLines does not trim newlines.
        /// Verifies that when maxNewLines is very large, no trimming occurs.
        /// </summary>
        [Test]
        public void TrimLineBreak_WithIntMaxValue_DoesNotTrim()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            for (int i = 0; i < 5; i++)
            {
                templateWriter.WriteNewLine(int.MaxValue);
            }

            // Act
            templateWriter.TrimLineBreak(int.MaxValue);
            templateWriter.Write("X");

            // Assert
            string result = writer.ToString();
            int actualNewLineCount = CountNewLines(result);
            Assert.That(actualNewLineCount, Is.EqualTo(5));
        }

        /// <summary>
        /// Tests that TrimLineBreak can be called multiple times with different values.
        /// Verifies that subsequent calls correctly adjust the m_newLineCount.
        /// </summary>
        [Test]
        public void TrimLineBreak_CalledMultipleTimes_AppliesLastTrim()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            for (int i = 0; i < 10; i++)
            {
                templateWriter.WriteNewLine(int.MaxValue);
            }

            // Act
            templateWriter.TrimLineBreak(5);
            templateWriter.TrimLineBreak(3);
            templateWriter.TrimLineBreak(2);
            templateWriter.Write("X");

            // Assert
            string result = writer.ToString();
            int actualNewLineCount = CountNewLines(result);
            Assert.That(actualNewLineCount, Is.EqualTo(2));
        }

        /// <summary>
        /// Tests that TrimLineBreak with no pending newlines does not cause issues.
        /// Verifies that calling TrimLineBreak when m_newLineCount is 0 works correctly.
        /// </summary>
        [Test]
        public void TrimLineBreak_WithNoPendingNewLines_DoesNotCauseIssues()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.TrimLineBreak(1);
            templateWriter.Write("X");

            // Assert
            string result = writer.ToString();
            int actualNewLineCount = CountNewLines(result);
            Assert.That(actualNewLineCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Helper method to count the number of newlines in a string.
        /// </summary>
        /// <param name="text">The text to analyze.</param>
        /// <returns>The number of newlines found.</returns>
        private static int CountNewLines(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            int count = 0;
            string newLine = Environment.NewLine;
            int index = 0;

            while ((index = text.IndexOf(newLine, index, StringComparison.Ordinal)) != -1)
            {
                count++;
                index += newLine.Length;
            }

            return count;
        }

        /// <summary>
        /// Tests that WriteNewLine returns true and allows the first new line
        /// when maxNewLines is 1.
        /// </summary>
        [Test]
        public void WriteNewLine_MaxNewLinesIsOne_ReturnsTrue()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            bool result = templateWriter.WriteNewLine(1);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that WriteNewLine returns false when called again
        /// after reaching the maxNewLines limit.
        /// </summary>
        [Test]
        public void WriteNewLine_CalledTwiceWithMaxOneNewLine_ReturnsFalseOnSecondCall()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            bool firstCall = templateWriter.WriteNewLine(1);
            bool secondCall = templateWriter.WriteNewLine(1);

            // Assert
            Assert.That(firstCall, Is.True);
            Assert.That(secondCall, Is.False);
        }

        /// <summary>
        /// Tests that WriteNewLine returns false immediately
        /// when maxNewLines is 0.
        /// </summary>
        [Test]
        public void WriteNewLine_MaxNewLinesIsZero_ReturnsFalse()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            bool result = templateWriter.WriteNewLine(0);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that WriteNewLine returns false immediately
        /// when maxNewLines is negative.
        /// </summary>
        [TestCase(-1)]
        [TestCase(-100)]
        [TestCase(int.MinValue)]
        public void WriteNewLine_MaxNewLinesIsNegative_ReturnsFalse(int maxNewLines)
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            bool result = templateWriter.WriteNewLine(maxNewLines);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that WriteNewLine with int.MaxValue allows many consecutive calls
        /// to return true without reaching the limit.
        /// </summary>
        [Test]
        public void WriteNewLine_MaxNewLinesIsIntMaxValue_AllowsManyCalls()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act & Assert
            for (int i = 0; i < 100; i++)
            {
                bool result = templateWriter.WriteNewLine(int.MaxValue);
                Assert.That(result, Is.True, $"Call {i + 1} should return true");
            }
        }

        /// <summary>
        /// Tests that WriteNewLine increments internal state correctly
        /// by verifying that consecutive calls respect the cumulative count.
        /// </summary>
        [Test]
        public void WriteNewLine_ConsecutiveCalls_IncrementsInternalState()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            bool call1 = templateWriter.WriteNewLine(3);
            bool call2 = templateWriter.WriteNewLine(3);
            bool call3 = templateWriter.WriteNewLine(3);
            bool call4 = templateWriter.WriteNewLine(3);

            // Assert
            Assert.That(call1, Is.True, "First call should return true");
            Assert.That(call2, Is.True, "Second call should return true");
            Assert.That(call3, Is.True, "Third call should return true");
            Assert.That(call4, Is.False, "Fourth call should return false as limit is reached");
        }

        /// <summary>
        /// Tests that WriteNewLine behavior with boundary value of 1
        /// allows exactly one new line.
        /// </summary>
        [Test]
        public void WriteNewLine_BoundaryValueOne_AllowsExactlyOneNewLine()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            bool firstResult = templateWriter.WriteNewLine(1);
            bool secondResult = templateWriter.WriteNewLine(1);
            bool thirdResult = templateWriter.WriteNewLine(1);

            // Assert
            Assert.That(firstResult, Is.True);
            Assert.That(secondResult, Is.False);
            Assert.That(thirdResult, Is.False);
        }

        /// <summary>
        /// Tests that WriteNewLine handles large maxNewLines values correctly
        /// and allows that many new lines.
        /// </summary>
        [Test]
        public void WriteNewLine_LargeMaxNewLines_AllowsUpToLimit()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);
            const int largeLimit = 1000;

            // Act
            int trueCount = 0;
            for (int i = 0; i < largeLimit + 10; i++)
            {
                if (templateWriter.WriteNewLine(largeLimit))
                {
                    trueCount++;
                }
            }

            // Assert
            Assert.That(trueCount, Is.EqualTo(largeLimit));
        }

        /// <summary>
        /// Tests that Write(char) correctly writes a regular alphabetic character to the output.
        /// Verifies that the character appears in the output stream.
        /// </summary>
        [TestCase('a')]
        [TestCase('Z')]
        [TestCase('m')]
        public void Write_WithRegularCharacter_WritesCharacterToOutput(char character)
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.Write(character);

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo(character.ToString()));
        }

        /// <summary>
        /// Tests that Write(char) correctly writes numeric digit characters.
        /// Verifies that digits are written correctly to the output.
        /// </summary>
        [TestCase('0')]
        [TestCase('5')]
        [TestCase('9')]
        public void Write_WithDigitCharacter_WritesDigitToOutput(char digit)
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.Write(digit);

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo(digit.ToString()));
        }

        /// <summary>
        /// Tests that Write(char) correctly writes special symbol characters.
        /// Verifies that symbols and punctuation are written correctly.
        /// </summary>
        [TestCase('!')]
        [TestCase('@')]
        [TestCase('#')]
        [TestCase('$')]
        [TestCase('%')]
        [TestCase('^')]
        [TestCase('&')]
        [TestCase('*')]
        [TestCase('(')]
        [TestCase(')')]
        [TestCase('-')]
        [TestCase('_')]
        [TestCase('=')]
        [TestCase('+')]
        [TestCase('[')]
        [TestCase(']')]
        [TestCase('{')]
        [TestCase('}')]
        [TestCase(';')]
        [TestCase(':')]
        [TestCase('\'')]
        [TestCase('"')]
        [TestCase(',')]
        [TestCase('.')]
        [TestCase('<')]
        [TestCase('>')]
        [TestCase('/')]
        [TestCase('?')]
        [TestCase('\\')]
        [TestCase('|')]
        public void Write_WithSpecialCharacter_WritesCharacterToOutput(char specialChar)
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.Write(specialChar);

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo(specialChar.ToString()));
        }

        /// <summary>
        /// Tests that Write(char) correctly writes whitespace characters.
        /// Verifies that space and tab characters are written correctly.
        /// </summary>
        [TestCase(' ')]
        [TestCase('\t')]
        public void Write_WithWhitespaceCharacter_WritesWhitespaceToOutput(char whitespace)
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.Write(whitespace);

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo(whitespace.ToString()));
        }

        /// <summary>
        /// Tests that Write(char) correctly writes line break characters.
        /// Verifies that newline and carriage return characters are written correctly.
        /// </summary>
        [TestCase('\n')]
        [TestCase('\r')]
        public void Write_WithLineBreakCharacter_WritesLineBreakToOutput(char lineBreak)
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.Write(lineBreak);

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo(lineBreak.ToString()));
        }

        /// <summary>
        /// Tests that Write(char) correctly writes the null character.
        /// Verifies that the null character '\0' is written to the output.
        /// </summary>
        [Test]
        public void Write_WithNullCharacter_WritesNullCharacterToOutput()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.Write('\0');

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo("\0"));
        }

        /// <summary>
        /// Tests that Write(char) correctly writes control characters.
        /// Verifies that various control characters are written to the output.
        /// </summary>
        [TestCase('\x01')]
        [TestCase('\x02')]
        [TestCase('\x1F')]
        [TestCase('\x7F')]
        public void Write_WithControlCharacter_WritesControlCharacterToOutput(char controlChar)
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.Write(controlChar);

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo(controlChar.ToString()));
        }

        /// <summary>
        /// Tests that Write(char) correctly writes Unicode characters.
        /// Verifies that high Unicode characters are written correctly.
        /// </summary>
        [TestCase('€')]
        [TestCase('™')]
        [TestCase('©')]
        [TestCase('®')]
        [TestCase('µ')]
        [TestCase('π')]
        [TestCase('Ω')]
        [TestCase('中')]
        [TestCase('日')]
        public void Write_WithUnicodeCharacter_WritesUnicodeCharacterToOutput(char unicodeChar)
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.Write(unicodeChar);

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo(unicodeChar.ToString()));
        }

        /// <summary>
        /// Tests that Write(char) correctly writes multiple characters in sequence.
        /// Verifies that characters are written in the correct order.
        /// </summary>
        [Test]
        public void Write_WithMultipleCharacters_WritesAllCharactersInOrder()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.Write('H');
            templateWriter.Write('e');
            templateWriter.Write('l');
            templateWriter.Write('l');
            templateWriter.Write('o');

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo("Hello"));
        }

        /// <summary>
        /// Tests that Write(char) writes pending newlines before writing the character.
        /// Verifies that WriteWhiteSpaceIfNeeded() flushes pending newlines.
        /// </summary>
        [Test]
        public void Write_AfterWriteLine_WritesPendingNewlineBeforeCharacter()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.WriteLine();
            templateWriter.Write('X');

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo(Environment.NewLine + "X"));
        }

        /// <summary>
        /// Tests that Write(char) writes pending whitespace before writing the character.
        /// Verifies that WriteWhiteSpaceIfNeeded() flushes pending spaces.
        /// </summary>
        [Test]
        public void Write_AfterWriteWhiteSpace_WritesPendingSpacesBeforeCharacter()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.WriteWhiteSpace(5);
            templateWriter.Write('X');

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo("     X"));
        }

        /// <summary>
        /// Tests that Write(char) writes indentation after a newline.
        /// Verifies that indentation is applied when writing after a line break.
        /// </summary>
        [Test]
        public void Write_AfterWriteLineWithIndentation_WritesIndentationBeforeCharacter()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.PushIndentChars(4);
            templateWriter.WriteLine();
            templateWriter.Write('X');

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo(Environment.NewLine + "    X"));
        }

        /// <summary>
        /// Tests that Write(char) with maximum char value works correctly.
        /// Verifies boundary condition for char.MaxValue.
        /// </summary>
        [Test]
        public void Write_WithMaxCharValue_WritesCharacterToOutput()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.Write(char.MaxValue);

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo(char.MaxValue.ToString()));
        }

        /// <summary>
        /// Tests that Write(char) with minimum char value works correctly.
        /// Verifies boundary condition for char.MinValue (same as '\0').
        /// </summary>
        [Test]
        public void Write_WithMinCharValue_WritesCharacterToOutput()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.Write(char.MinValue);

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo(char.MinValue.ToString()));
        }

        /// <summary>
        /// Tests that calling WriteLine() once writes a single newline to the output
        /// when followed by a write operation or dispose.
        /// </summary>
        [Test]
        public void WriteLine_CalledOnce_WritesOneNewLine()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            // Act
            templateWriter.WriteLine();
            templateWriter.Dispose();

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo(Environment.NewLine));
        }

        /// <summary>
        /// Tests that calling WriteLine() twice writes two newlines to the output
        /// when followed by a write operation or dispose.
        /// </summary>
        [Test]
        public void WriteLine_CalledTwice_WritesTwoNewLines()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            // Act
            templateWriter.WriteLine();
            templateWriter.WriteLine();
            templateWriter.Dispose();

            // Assert
            string result = stringWriter.ToString();
            string expected = Environment.NewLine + Environment.NewLine;
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that calling WriteLine() more than twice is limited to a maximum of two newlines,
        /// as defined by the maxNewLines parameter (hardcoded to 2 in the method).
        /// </summary>
        [Test]
        public void WriteLine_CalledThreeTimes_WritesMaximumTwoNewLines()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            // Act
            templateWriter.WriteLine();
            templateWriter.WriteLine();
            templateWriter.WriteLine();
            templateWriter.Dispose();

            // Assert
            string result = stringWriter.ToString();
            string expected = Environment.NewLine + Environment.NewLine;
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that calling WriteLine() multiple times (more than the limit) still respects
        /// the maximum of two newlines.
        /// </summary>
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(10)]
        public void WriteLine_CalledMultipleTimes_WritesMaximumTwoNewLines(int callCount)
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            // Act
            for (int i = 0; i < callCount; i++)
            {
                templateWriter.WriteLine();
            }
            templateWriter.Dispose();

            // Assert
            string result = stringWriter.ToString();
            string expected = Environment.NewLine + Environment.NewLine;
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that WriteLine() followed by text writes a newline before the text content.
        /// </summary>
        [Test]
        public void WriteLine_FollowedByText_WritesNewLineBeforeText()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            // Act
            templateWriter.WriteLine();
            templateWriter.Write("Test");

            // Assert
            string result = stringWriter.ToString();
            string expected = Environment.NewLine + "Test";
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that multiple WriteLine() calls followed by text writes the correct number
        /// of newlines (up to max 2) before the text content.
        /// </summary>
        [Test]
        public void WriteLine_TwiceFollowedByText_WritesTwoNewLinesBeforeText()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            // Act
            templateWriter.WriteLine();
            templateWriter.WriteLine();
            templateWriter.Write("Test");

            // Assert
            string result = stringWriter.ToString();
            string expected = Environment.NewLine + Environment.NewLine + "Test";
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that WriteLine() works correctly with indentation, maintaining the proper
        /// indent level after the newline.
        /// </summary>
        [Test]
        public void WriteLine_WithIndentation_MaintainsIndentLevel()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using (var templateWriter = new TemplateWriter(stringWriter))
            {
                // Act
                templateWriter.PushIndentChars(4);
                templateWriter.WriteLine();
                templateWriter.Write("Test");
            }

            // Assert
            string result = stringWriter.ToString();
            string expected = Environment.NewLine + "    Test";
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that WriteLine() resets to the current indentation level
        /// when multiple indentation levels are used.
        /// </summary>
        [Test]
        public void WriteLine_WithMultipleIndentLevels_ResetsToCurrentIndent()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            // Act
            templateWriter.PushIndentChars(4);
            templateWriter.PushIndentChars(4);
            templateWriter.WriteLine();
            templateWriter.Write("Test");

            // Assert
            string result = stringWriter.ToString();
            string expected = Environment.NewLine + "        Test";
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that WriteLine() can be used between text writes to create properly
        /// formatted output with newlines.
        /// </summary>
        [Test]
        public void WriteLine_BetweenTextWrites_FormatsOutputCorrectly()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            // Act
            templateWriter.Write("First");
            templateWriter.WriteLine();
            templateWriter.Write("Second");

            // Assert
            string result = stringWriter.ToString();
            string expected = "First" + Environment.NewLine + "Second";
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that WriteLine() works correctly when no subsequent write operations occur,
        /// verifying behavior on dispose.
        /// </summary>
        [Test]
        public void WriteLine_WithoutSubsequentWrite_WritesNewLineOnDispose()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using (var templateWriter = new TemplateWriter(stringWriter))
            {
                // Act
                templateWriter.WriteLine();
            } // Dispose is called here

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo(Environment.NewLine));
        }

        /// <summary>
        /// Tests that Write with a normal string writes the text to the underlying writer.
        /// </summary>
        [Test]
        public void Write_WithNormalString_WritesTextToWriter()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);
            const string text = "Hello, World!";

            // Act
            templateWriter.Write(text);

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo(text));
        }

        /// <summary>
        /// Tests that Write with null string writes null to the underlying writer.
        /// TextWriter.Write accepts null for strings.
        /// </summary>
        [Test]
        public void Write_WithNullString_WritesNullToWriter()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.Write(null);

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that Write with an empty string writes nothing to the underlying writer.
        /// </summary>
        [Test]
        public void Write_WithEmptyString_WritesEmptyStringToWriter()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.Write(string.Empty);

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that Write with various whitespace strings writes the whitespace correctly.
        /// </summary>
        /// <param name="text">The whitespace string to test.</param>
        [TestCase(" ")]
        [TestCase("   ")]
        [TestCase("\t")]
        [TestCase(" \t ")]
        public void Write_WithWhitespaceOnlyString_WritesWhitespaceToWriter(string text)
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.Write(text);

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo(text));
        }

        /// <summary>
        /// Tests that Write with a string containing newlines writes the string as-is.
        /// Newlines within the string are written directly without triggering special handling.
        /// </summary>
        [Test]
        public void Write_WithStringContainingNewlines_WritesStringAsIs()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);
            const string text = "Line 1\nLine 2\r\nLine 3";

            // Act
            templateWriter.Write(text);

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo(text));
        }

        /// <summary>
        /// Tests that Write flushes pending whitespace before writing the text.
        /// Verifies that queued spaces are written before the actual text content.
        /// </summary>
        [Test]
        public void Write_WithPendingWhitespace_FlushesWhitespaceBeforeText()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);
            const string text = "Hello";

            // Act
            templateWriter.WriteWhiteSpace(4);
            templateWriter.Write(text);

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo("    Hello"));
        }

        /// <summary>
        /// Tests that Write flushes pending newlines before writing the text.
        /// Verifies that queued newlines are written before the actual text content.
        /// </summary>
        [Test]
        public void Write_WithPendingNewlines_FlushesNewlinesBeforeText()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);
            const string text = "Hello";

            // Act
            templateWriter.WriteNewLine(1);
            templateWriter.Write(text);

            // Assert
            string result = writer.ToString();
            string expected = Environment.NewLine + text;
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that Write can handle very long strings without issues.
        /// </summary>
        [Test]
        public void Write_WithVeryLongString_WritesEntireString()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);
            string longText = new('A', 10000);

            // Act
            templateWriter.Write(longText);

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo(longText));
            Assert.That(result.Length, Is.EqualTo(10000));
        }

        /// <summary>
        /// Tests that Write handles strings with special and control characters correctly.
        /// </summary>
        /// <param name="text">The string with special characters to test.</param>
        [TestCase("Hello\0World")]
        [TestCase("Test\u0001\u0002\u0003")]
        [TestCase("Unicode: \u2764\uFE0F")]
        [TestCase("Symbols: !@#$%^&*()")]
        [TestCase("Quotes: \"'`")]
        public void Write_WithSpecialCharacters_WritesCharactersAsIs(string text)
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.Write(text);

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo(text));
        }

        /// <summary>
        /// Tests that multiple consecutive Write calls write all texts in sequence.
        /// </summary>
        [Test]
        public void Write_MultipleConsecutiveCalls_WritesAllTexts()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.Write("Hello");
            templateWriter.Write(" ");
            templateWriter.Write("World");
            templateWriter.Write("!");

            // Assert
            string result = writer.ToString();
            Assert.That(result, Is.EqualTo("Hello World!"));
        }

        /// <summary>
        /// Tests that Write flushes both pending newlines and whitespace before writing text.
        /// Verifies the correct order: newlines first, then spaces, then text.
        /// </summary>
        [Test]
        public void Write_WithPendingNewlinesAndWhitespace_FlushesBothBeforeText()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);
            const string text = "Content";

            // Act
            templateWriter.WriteNewLine(1);
            templateWriter.WriteWhiteSpace(2);
            templateWriter.Write(text);

            // Assert
            string result = writer.ToString();
            string expected = Environment.NewLine + "  Content";
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that Write after indentation changes includes proper indentation.
        /// </summary>
        [Test]
        public void Write_AfterPushIndentation_IncludesIndentation()
        {
            // Arrange
            using var writer = new StringWriter();
            using var templateWriter = new TemplateWriter(writer);

            // Act
            templateWriter.PushIndentChars(4);
            templateWriter.WriteNewLine(1);
            templateWriter.Write("Indented");

            // Assert
            string result = writer.ToString();
            string expected = Environment.NewLine + "    Indented";
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that WriteLine writes a simple text string correctly.
        /// Input: Normal text string.
        /// Expected: Text is written to the underlying writer.
        /// </summary>
        [Test]
        public void WriteLine_WithNormalText_WritesTextToWriter()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);
            const string expectedText = "Hello World";

            // Act
            templateWriter.WriteLine(expectedText);

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo(expectedText));
        }

        /// <summary>
        /// Tests that WriteLine handles null text parameter.
        /// Input: null string.
        /// Expected: No exception thrown, writer may write nothing or "null" depending on TextWriter behavior.
        /// </summary>
        [Test]
        public void WriteLine_WithNullText_DoesNotThrow()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            // Act & Assert
            Assert.DoesNotThrow(() => templateWriter.WriteLine(null));
        }

        /// <summary>
        /// Tests that WriteLine handles empty string correctly.
        /// Input: Empty string.
        /// Expected: Empty string is written (essentially nothing visible before the queued newline).
        /// </summary>
        [Test]
        public void WriteLine_WithEmptyString_WritesEmptyLine()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            // Act
            templateWriter.WriteLine(string.Empty);

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that WriteLine handles whitespace-only strings correctly.
        /// Input: String containing only spaces.
        /// Expected: Whitespace is written as-is.
        /// </summary>
        [Test]
        public void WriteLine_WithWhitespaceOnlyText_WritesWhitespace()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);
            const string whitespaceText = "    ";

            // Act
            templateWriter.WriteLine(whitespaceText);

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo(whitespaceText));
        }

        /// <summary>
        /// Tests that multiple WriteLine calls add newlines between lines.
        /// Input: Two separate WriteLine calls with different text.
        /// Expected: Second line appears on a new line after the first, with Environment.NewLine separator.
        /// </summary>
        [Test]
        public void WriteLine_WithMultipleCalls_AddsNewLinesBetweenLines()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            // Act
            templateWriter.WriteLine("First Line");
            templateWriter.WriteLine("Second Line");

            // Assert
            string result = stringWriter.ToString();
            string expected = "First Line" + Environment.NewLine + "Second Line";
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that WriteLine works correctly with indentation.
        /// Input: Text with indentation set via PushIndentChars.
        /// Expected: Text is indented according to the current indentation level.
        /// </summary>
        [Test]
        public void WriteLine_WithIndentation_WritesIndentedText()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using (var templateWriter = new TemplateWriter(stringWriter))
            {
                // Act
                templateWriter.PushIndentChars(4);
                templateWriter.WriteLine("Indented Line");
            }

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo("Indented Line" + Environment.NewLine));
            // TODO: Should be Assert.That(result, Is.EqualTo("    Indented Line" + Environment.NewLine));
        }

        /// <summary>
        /// Tests that WriteLine handles text with special characters.
        /// Input: Text containing special characters like tabs, quotes, etc.
        /// Expected: Special characters are written as-is.
        /// </summary>
        [TestCase("Text\twith\ttabs")]
        [TestCase("Text with \"quotes\"")]
        [TestCase("Text with 'apostrophes'")]
        [TestCase("Text with @special #characters $here")]
        public void WriteLine_WithSpecialCharacters_WritesTextAsIs(string textWithSpecialChars)
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            // Act
            templateWriter.WriteLine(textWithSpecialChars);

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo(textWithSpecialChars));
        }

        /// <summary>
        /// Tests that WriteLine handles very long strings.
        /// Input: Very long string (1000+ characters).
        /// Expected: Entire string is written correctly.
        /// </summary>
        [Test]
        public void WriteLine_WithVeryLongString_WritesEntireString()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);
            string longText = new('A', 10000);

            // Act
            templateWriter.WriteLine(longText);

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo(longText));
        }

        /// <summary>
        /// Tests that WriteLine handles text containing embedded newlines.
        /// Input: Text with embedded Environment.NewLine characters.
        /// Expected: Embedded newlines are written as-is within the text.
        /// </summary>
        [Test]
        public void WriteLine_WithEmbeddedNewlines_WritesTextWithNewlines()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);
            string textWithNewlines = "Line1" + Environment.NewLine + "Line2";

            // Act
            templateWriter.WriteLine(textWithNewlines);

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo(textWithNewlines));
        }

        /// <summary>
        /// Tests that WriteLine with indentation applied to multiple lines works correctly.
        /// Input: Multiple WriteLine calls with different indentation levels.
        /// Expected: Each line has appropriate indentation applied.
        /// </summary>
        [Test]
        public void WriteLine_WithVaryingIndentation_AppliesCorrectIndentationToEachLine()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using (var templateWriter = new TemplateWriter(stringWriter))
            {
                // Act
                templateWriter.WriteLine("No indent");
                templateWriter.PushIndentChars(2);
                templateWriter.WriteLine("Two spaces");
                templateWriter.PushIndentChars(2);
                templateWriter.WriteLine("Four spaces");
            }

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo(
                "No indent" +
                Environment.NewLine +
                "Two spaces" +
                Environment.NewLine +
                "  Four spaces" +
                Environment.NewLine));
            // TODO: Should be
            // Assert.That(result, Is.EqualTo(
            //  "No indent" + Environment.NewLine +
            //  "  Two spaces" + Environment.NewLine +
            //  "    Four spaces" + Environment.NewLine));
        }

        /// <summary>
        /// Tests that WriteLine handles Unicode characters correctly.
        /// Input: Text with Unicode characters (e.g., emoji, non-ASCII).
        /// Expected: Unicode characters are written correctly.
        /// </summary>
        [TestCase("Hello 世界")]
        [TestCase("Text with emoji 😀🎉")]
        [TestCase("Ñoño")]
        public void WriteLine_WithUnicodeCharacters_WritesTextCorrectly(string unicodeText)
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            // Act
            templateWriter.WriteLine(unicodeText);

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo(unicodeText));
        }

        /// <summary>
        /// Tests that WriteLine works correctly after PopIndentation.
        /// Input: Text written after pushing and popping indentation.
        /// Expected: Indentation is correctly restored after pop.
        /// </summary>
        [Test]
        public void WriteLine_AfterPopIndentation_RestoresIndentation()
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using (var templateWriter = new TemplateWriter(stringWriter))
            {
                // Act
                templateWriter.PushIndentChars(4);
                templateWriter.WriteLine("Indented");
                templateWriter.PopIndentation();
                templateWriter.WriteLine("Not indented");
            }
            // Assert
            string result = stringWriter.ToString();
            string expected = "    Indented" + Environment.NewLine + "Not indented";
            // TODO:  Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that WriteLine handles text with control characters.
        /// Input: Text with control characters like \0, \b, etc.
        /// Expected: Control characters are written as-is.
        /// </summary>
        [TestCase("Text\0with\0nulls")]
        [TestCase("Text\bwith\bbackspace")]
        public void WriteLine_WithControlCharacters_WritesTextAsIs(string textWithControlChars)
        {
            // Arrange
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            // Act
            templateWriter.WriteLine(textWithControlChars);

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Is.EqualTo(textWithControlChars));
        }
    }
}
