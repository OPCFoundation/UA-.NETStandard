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

using System.Collections.Generic;
using System.Xml;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration.Shared.Tests
{
    /// <summary>
    /// Unit tests for the <see cref="SourceGenerationUtils"/> class.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class SourceGenerationUtilsTests
    {
        /// <summary>
        /// Tests that IsNull returns the expected result for various XmlQualifiedName inputs.
        /// </summary>
        /// <param name="qname">The XmlQualifiedName to test.</param>
        /// <param name="expected">The expected result.</param>
        [TestCaseSource(nameof(IsNullTestCases))]
        public void IsNull_VariousInputs_ReturnsExpectedResult(XmlQualifiedName qname, bool expected)
        {
            // Act
            bool result = qname.IsNull();

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        private static IEnumerable<TestCaseData> IsNullTestCases()
        {
            yield return new TestCaseData(null, true);
            yield return new TestCaseData(new XmlQualifiedName(null), true);
            yield return new TestCaseData(new XmlQualifiedName(string.Empty), true);
            yield return new TestCaseData(new XmlQualifiedName("ValidName"), false);
            yield return new TestCaseData(new XmlQualifiedName("ValidName", "http://namespace.com"), false);
            yield return new TestCaseData(new XmlQualifiedName("   "), false);
            yield return new TestCaseData(new XmlQualifiedName(" \t\n\r "), false);
        }

        /// <summary>
        /// Tests the ToLowerCamelCase method with a null input.
        /// Verifies that a null input returns null.
        /// </summary>
        [Test]
        public void ToLowerCamelCase_NullInput_ReturnsNull()
        {
            // Arrange
            const string input = null;

            // Act
            string result = input.ToLowerCamelCase();

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests the ToLowerCamelCase method with an empty string.
        /// Verifies that an empty string returns an empty string.
        /// </summary>
        [Test]
        public void ToLowerCamelCase_EmptyString_ReturnsEmptyString()
        {
            // Arrange
            string input = string.Empty;

            // Act
            string result = input.ToLowerCamelCase();

            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests the ToLowerCamelCase method with various string inputs.
        /// Verifies that the first character is converted to lowercase when needed,
        /// and returns the input unchanged when the first character is already lowercase
        /// or is not an alphabetic character.
        /// </summary>
        /// <param name="input">The input string to test.</param>
        /// <param name="expected">The expected output string.</param>
        [TestCase("a", "a", Description = "Single lowercase character")]
        [TestCase("A", "a", Description = "Single uppercase character")]
        [TestCase("helloWorld", "helloWorld", Description = "Already lowercase first character")]
        [TestCase("HelloWorld", "helloWorld", Description = "Uppercase first character")]
        [TestCase("HELLO", "hELLO", Description = "All uppercase string")]
        [TestCase("Ab", "ab", Description = "Two characters, uppercase first")]
        [TestCase("ab", "ab", Description = "Two characters, lowercase first")]
        [TestCase("X", "x", Description = "Single uppercase letter X")]
        [TestCase("z", "z", Description = "Single lowercase letter z")]
        [TestCase("MyProperty", "myProperty", Description = "Typical property name")]
        [TestCase("thisIsAlreadyLowerCamel", "thisIsAlreadyLowerCamel", Description = "Already in lower camel case")]
        [TestCase("URL", "uRL", Description = "Acronym")]
        public void ToLowerCamelCase_AlphabeticStrings_ConvertsFirstCharacterCorrectly(string input, string expected)
        {
            // Act
            string result = input.ToLowerCamelCase();

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests the ToLowerCamelCase method with strings starting with non-alphabetic characters.
        /// Verifies that strings starting with digits, symbols, or whitespace are returned unchanged.
        /// </summary>
        /// <param name="input">The input string to test.</param>
        /// <param name="expected">The expected output string.</param>
        [TestCase("123Test", "123Test", Description = "String starting with digit")]
        [TestCase("0Value", "0Value", Description = "String starting with zero")]
        [TestCase("_test", "_test", Description = "String starting with underscore")]
        [TestCase("$value", "$value", Description = "String starting with dollar sign")]
        [TestCase(" Test", " Test", Description = "String starting with space")]
        [TestCase("\tTest", "\tTest", Description = "String starting with tab")]
        [TestCase("@Property", "@Property", Description = "String starting with at symbol")]
        [TestCase("-negative", "-negative", Description = "String starting with hyphen")]
        public void ToLowerCamelCase_NonAlphabeticStart_ReturnsUnchanged(string input, string expected)
        {
            // Act
            string result = input.ToLowerCamelCase();

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests the ToLowerCamelCase method with Unicode characters.
        /// Verifies that Unicode uppercase characters are correctly converted to lowercase,
        /// and lowercase Unicode characters remain unchanged.
        /// </summary>
        /// <param name="input">The input string to test.</param>
        /// <param name="expected">The expected output string.</param>
        [TestCase("Über", "über", Description = "German umlaut uppercase")]
        [TestCase("über", "über", Description = "German umlaut lowercase")]
        [TestCase("École", "école", Description = "French accented character uppercase")]
        [TestCase("école", "école", Description = "French accented character lowercase")]
        [TestCase("Ñame", "ñame", Description = "Spanish ñ uppercase")]
        [TestCase("ñame", "ñame", Description = "Spanish ñ lowercase")]
        public void ToLowerCamelCase_UnicodeCharacters_HandlesCorrectly(string input, string expected)
        {
            // Act
            string result = input.ToLowerCamelCase();

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests the ToLowerCamelCase method with a very long string.
        /// Verifies that the method handles long strings correctly without performance issues.
        /// </summary>
        [Test]
        public void ToLowerCamelCase_VeryLongString_ConvertsCorrectly()
        {
            // Arrange
            string input = "A" + new string('x', 10000);
            string expected = "a" + new string('x', 10000);

            // Act
            string result = input.ToLowerCamelCase();

            // Assert
            Assert.That(result, Is.EqualTo(expected));
            Assert.That(result.Length, Is.EqualTo(10001));
        }

        /// <summary>
        /// Tests the ToLowerCamelCase method with whitespace-only strings.
        /// Verifies that strings containing only whitespace characters are handled correctly.
        /// </summary>
        /// <param name="input">The input string to test.</param>
        /// <param name="expected">The expected output string.</param>
        [TestCase("   ", "   ", Description = "Multiple spaces")]
        [TestCase("\t\t", "\t\t", Description = "Multiple tabs")]
        [TestCase("\n", "\n", Description = "Newline character")]
        [TestCase("\r\n", "\r\n", Description = "Carriage return and newline")]
        public void ToLowerCamelCase_WhitespaceOnly_ReturnsUnchanged(string input, string expected)
        {
            // Act
            string result = input.ToLowerCamelCase();

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests the ToLowerCamelCase method with special character strings.
        /// Verifies that strings consisting entirely of special characters are handled correctly.
        /// </summary>
        /// <param name="input">The input string to test.</param>
        /// <param name="expected">The expected output string.</param>
        [TestCase("_", "_", Description = "Single underscore")]
        [TestCase("$", "$", Description = "Single dollar sign")]
        [TestCase("@#$%", "@#$%", Description = "Multiple special characters")]
        [TestCase("123", "123", Description = "Only digits")]
        public void ToLowerCamelCase_SpecialCharactersOnly_ReturnsUnchanged(string input, string expected)
        {
            // Act
            string result = input.ToLowerCamelCase();

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }
    }
}
