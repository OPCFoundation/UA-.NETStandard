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
using System;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Unit tests for the Resource class.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ResourceTests
    {
        /// <summary>
        /// Tests GetNameForFile with a simple filename containing a single dot.
        /// Input: "test.xml" with null namespace prefix.
        /// Expected: Returns "TestXml" with each part capitalized.
        /// </summary>
        [Test]
        public void GetNameForFile_SimpleFilename_ReturnsPascalCaseName()
        {
            // Arrange
            const string inputFile = "test.xml";
            const string namespacePrefix = null;
            // Act
            string result = Resource.GetNameForFile(inputFile, namespacePrefix);
            // Assert
            Assert.That(result, Is.EqualTo("TestXml"));
        }

        /// <summary>
        /// Tests GetNameForFile with a filename containing multiple dots.
        /// Input: "test.file.config.xml" with null namespace prefix.
        /// Expected: Returns "TestFileConfigXml" with all parts capitalized.
        /// </summary>
        [Test]
        public void GetNameForFile_MultipleDotsInFilename_ReturnsAllPartsConcatenated()
        {
            // Arrange
            const string inputFile = "test.file.config.xml";
            const string namespacePrefix = null;
            // Act
            string result = Resource.GetNameForFile(inputFile, namespacePrefix);
            // Assert
            Assert.That(result, Is.EqualTo("TestFileConfigXml"));
        }

        /// <summary>
        /// Tests GetNameForFile with a full file path.
        /// Input: "C:\\path\\to\\file.xml" with null namespace prefix.
        /// Expected: Returns "FileXml", extracting only the filename.
        /// </summary>
        [TestCase("C:\\path\\to\\file.xml", "FileXml")]
        [TestCase("/usr/local/bin/file.xml", "FileXml")]
        [TestCase("..\\..\\file.xml", "FileXml")]
        public void GetNameForFile_FullFilePath_ExtractsFilenameOnly(string inputFile, string expected)
        {
            // Arrange
            const string namespacePrefix = null;
            // Act
            string result = Resource.GetNameForFile(inputFile, namespacePrefix);
            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests GetNameForFile with a matching namespace prefix.
        /// Input: "Opc.Ua.Test.xml" with namespace prefix "Opc.Ua.".
        /// Expected: Returns "TestXml" with the prefix stripped.
        /// </summary>
        [Test]
        public void GetNameForFile_MatchingNamespacePrefix_StripsPrefixAndReturnsName()
        {
            // Arrange
            const string inputFile = "Opc.Ua.Test.xml";
            const string namespacePrefix = "Opc.Ua.";
            // Act
            string result = Resource.GetNameForFile(inputFile, namespacePrefix);
            // Assert
            Assert.That(result, Is.EqualTo("TestXml"));
        }

        /// <summary>
        /// Tests GetNameForFile with a non-matching namespace prefix.
        /// Input: "Other.Test.xml" with namespace prefix "Opc.Ua.".
        /// Expected: Returns "OtherTestXml" without stripping prefix.
        /// </summary>
        [Test]
        public void GetNameForFile_NonMatchingNamespacePrefix_ReturnsFullName()
        {
            // Arrange
            const string inputFile = "Other.Test.xml";
            const string namespacePrefix = "Opc.Ua.";
            // Act
            string result = Resource.GetNameForFile(inputFile, namespacePrefix);
            // Assert
            Assert.That(result, Is.EqualTo("OtherTestXml"));
        }

        /// <summary>
        /// Tests GetNameForFile with null namespace prefix.
        /// Input: "test.xml" with null namespace prefix.
        /// Expected: Returns "TestXml" without attempting to strip prefix.
        /// </summary>
        [Test]
        public void GetNameForFile_NullNamespacePrefix_ReturnsTransformedName()
        {
            // Arrange
            const string inputFile = "test.xml";
            const string namespacePrefix = null;
            // Act
            string result = Resource.GetNameForFile(inputFile, namespacePrefix);
            // Assert
            Assert.That(result, Is.EqualTo("TestXml"));
        }

        /// <summary>
        /// Tests GetNameForFile with empty namespace prefix.
        /// Input: "test.xml" with empty string namespace prefix.
        /// Expected: Returns "TestXml" without stripping anything.
        /// </summary>
        [Test]
        public void GetNameForFile_EmptyNamespacePrefix_ReturnsTransformedName()
        {
            // Arrange
            const string inputFile = "test.xml";
            const string namespacePrefix = "";
            // Act
            string result = Resource.GetNameForFile(inputFile, namespacePrefix);
            // Assert
            Assert.That(result, Is.EqualTo("TestXml"));
        }

        /// <summary>
        /// Tests GetNameForFile with leading and trailing dots in filename.
        /// Input: ".test.xml." with null namespace prefix.
        /// Expected: Returns "TestXml" with empty parts skipped.
        /// </summary>
        [Test]
        public void GetNameForFile_LeadingAndTrailingDots_SkipsEmptyParts()
        {
            // Arrange
            const string inputFile = ".test.xml.";
            const string namespacePrefix = null;
            // Act
            string result = Resource.GetNameForFile(inputFile, namespacePrefix);
            // Assert
            Assert.That(result, Is.EqualTo("TestXml"));
        }

        /// <summary>
        /// Tests GetNameForFile with consecutive dots in filename.
        /// Input: "test..xml" with null namespace prefix.
        /// Expected: Returns "TestXml" with empty parts between dots skipped.
        /// </summary>
        [Test]
        public void GetNameForFile_ConsecutiveDots_SkipsEmptyParts()
        {
            // Arrange
            const string inputFile = "test..xml";
            const string namespacePrefix = null;
            // Act
            string result = Resource.GetNameForFile(inputFile, namespacePrefix);
            // Assert
            Assert.That(result, Is.EqualTo("TestXml"));
        }

        /// <summary>
        /// Tests GetNameForFile with filename containing no dots.
        /// Input: "testfile" with null namespace prefix.
        /// Expected: Returns "Testfile" with first character uppercased.
        /// </summary>
        [Test]
        public void GetNameForFile_NoDots_ReturnsCapitalizedName()
        {
            // Arrange
            const string inputFile = "testfile";
            const string namespacePrefix = null;
            // Act
            string result = Resource.GetNameForFile(inputFile, namespacePrefix);
            // Assert
            Assert.That(result, Is.EqualTo("Testfile"));
        }

        /// <summary>
        /// Tests GetNameForFile with single character parts.
        /// Input: "a.b.c" with null namespace prefix.
        /// Expected: Returns "ABC" with each single character uppercased.
        /// </summary>
        [Test]
        public void GetNameForFile_SingleCharacterParts_UppercasesAllChars()
        {
            // Arrange
            const string inputFile = "a.b.c";
            const string namespacePrefix = null;
            // Act
            string result = Resource.GetNameForFile(inputFile, namespacePrefix);
            // Assert
            Assert.That(result, Is.EqualTo("ABC"));
        }

        /// <summary>
        /// Tests GetNameForFile with filename containing only dots.
        /// Input: "..." with null namespace prefix.
        /// Expected: Returns empty string as all parts are empty.
        /// </summary>
        [Test]
        public void GetNameForFile_OnlyDots_ReturnsEmptyString()
        {
            // Arrange
            const string inputFile = "...";
            const string namespacePrefix = null;
            // Act
            string result = Resource.GetNameForFile(inputFile, namespacePrefix);
            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests GetNameForFile with empty string input.
        /// Input: "" with null namespace prefix.
        /// Expected: Returns empty string.
        /// </summary>
        [Test]
        public void GetNameForFile_EmptyString_ReturnsEmptyString()
        {
            // Arrange
            const string inputFile = "";
            const string namespacePrefix = null;
            // Act
            string result = Resource.GetNameForFile(inputFile, namespacePrefix);
            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests GetNameForFile with null input file.
        /// Input: null file with null namespace prefix.
        /// Expected: Throws NullReferenceException from Path.GetFileName.
        /// </summary>
        [Test]
        public void GetNameForFile_NullInputFile_ThrowsNullReferenceException()
        {
            // Arrange
            const string inputFile = null;
            const string namespacePrefix = null;
            // Act & Assert
            Assert.Throws<NullReferenceException>(() => Resource.GetNameForFile(inputFile, namespacePrefix));
        }

        /// <summary>
        /// Tests GetNameForFile with special characters in filename.
        /// Input: "test-file_name.xml" with null namespace prefix.
        /// Expected: Returns "Test-file_nameXml" with special chars preserved.
        /// </summary>
        [Test]
        public void GetNameForFile_SpecialCharacters_PreservesSpecialChars()
        {
            // Arrange
            const string inputFile = "test-file_name.xml";
            const string namespacePrefix = null;
            // Act
            string result = Resource.GetNameForFile(inputFile, namespacePrefix);
            // Assert
            Assert.That(result, Is.EqualTo("Test-file_nameXml"));
        }

        /// <summary>
        /// Tests GetNameForFile with uppercase filename.
        /// Input: "TEST.XML" with null namespace prefix.
        /// Expected: Returns "TESTXML" preserving uppercase.
        /// </summary>
        [Test]
        public void GetNameForFile_UppercaseFilename_PreservesUppercase()
        {
            // Arrange
            const string inputFile = "TEST.XML";
            const string namespacePrefix = null;
            // Act
            string result = Resource.GetNameForFile(inputFile, namespacePrefix);
            // Assert
            Assert.That(result, Is.EqualTo("TESTXML"));
        }

        /// <summary>
        /// Tests GetNameForFile with mixed case filename.
        /// Input: "TeSt.XmL" with null namespace prefix.
        /// Expected: Returns "TeStXmL" capitalizing first char only.
        /// </summary>
        [Test]
        public void GetNameForFile_MixedCase_CapitalizesFirstCharOnly()
        {
            // Arrange
            const string inputFile = "TeSt.XmL";
            const string namespacePrefix = null;
            // Act
            string result = Resource.GetNameForFile(inputFile, namespacePrefix);
            // Assert
            Assert.That(result, Is.EqualTo("TeStXmL"));
        }

        /// <summary>
        /// Tests GetNameForFile with namespace prefix matching entire filename.
        /// Input: "Opc.Ua.xml" with namespace prefix "Opc.Ua.xml".
        /// Expected: Returns empty string as entire filename is stripped.
        /// </summary>
        [Test]
        public void GetNameForFile_NamespacePrefixMatchesEntireFilename_ReturnsEmptyString()
        {
            // Arrange
            const string inputFile = "Opc.Ua.xml";
            const string namespacePrefix = "Opc.Ua.xml";
            // Act
            string result = Resource.GetNameForFile(inputFile, namespacePrefix);
            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests GetNameForFile with namespace prefix longer than filename.
        /// Input: "test.xml" with namespace prefix "VeryLongNamespace.test.xml.extra".
        /// Expected: Returns "TestXml" as prefix doesn't match.
        /// </summary>
        [Test]
        public void GetNameForFile_NamespacePrefixLongerThanFilename_ReturnsFullName()
        {
            // Arrange
            const string inputFile = "test.xml";
            const string namespacePrefix = "VeryLongNamespace.test.xml.extra";
            // Act
            string result = Resource.GetNameForFile(inputFile, namespacePrefix);
            // Assert
            Assert.That(result, Is.EqualTo("TestXml"));
        }

        /// <summary>
        /// Tests GetNameForFile with full path and matching namespace prefix.
        /// Input: "C:\\path\\to\\Opc.Ua.test.xml" with namespace prefix "Opc.Ua.".
        /// Expected: Returns "TestXml" extracting filename and stripping prefix.
        /// </summary>
        [Test]
        public void GetNameForFile_FullPathWithMatchingPrefix_ExtractsAndStripsPrefix()
        {
            // Arrange
            const string inputFile = "C:\\path\\to\\Opc.Ua.test.xml";
            const string namespacePrefix = "Opc.Ua.";
            // Act
            string result = Resource.GetNameForFile(inputFile, namespacePrefix);
            // Assert
            Assert.That(result, Is.EqualTo("TestXml"));
        }

        /// <summary>
        /// Tests GetNameForFile with whitespace in part.
        /// Input: " test.xml" with null namespace prefix.
        /// Expected: Returns " testXml" with whitespace preserved and first non-whitespace char uppercased.
        /// </summary>
        [Test]
        public void GetNameForFile_WhitespaceInPart_PreservesWhitespace()
        {
            // Arrange
            const string inputFile = " test.xml";
            const string namespacePrefix = null;
            // Act
            string result = Resource.GetNameForFile(inputFile, namespacePrefix);
            // Assert
            Assert.That(result, Is.EqualTo(" testXml"));
        }

        /// <summary>
        /// Tests GetNameForFile with very long filename.
        /// Input: Long filename with multiple parts.
        /// Expected: Returns concatenated PascalCase name with all parts.
        /// </summary>
        [Test]
        public void GetNameForFile_VeryLongFilename_ReturnsFullConcatenatedName()
        {
            // Arrange
            const string inputFile = "very.long.filename.with.many.parts.and.dots.xml";
            const string namespacePrefix = null;
            // Act
            string result = Resource.GetNameForFile(inputFile, namespacePrefix);
            // Assert
            Assert.That(result, Is.EqualTo("VeryLongFilenameWithManyPartsAndDotsXml"));
        }

        /// <summary>
        /// Tests GetNameForFile with numeric parts in filename.
        /// Input: "version2.0.1.xml" with null namespace prefix.
        /// Expected: Returns "Version201Xml" with numbers preserved.
        /// </summary>
        [Test]
        public void GetNameForFile_NumericParts_PreservesNumbers()
        {
            // Arrange
            const string inputFile = "version2.0.1.xml";
            const string namespacePrefix = null;
            // Act
            string result = Resource.GetNameForFile(inputFile, namespacePrefix);
            // Assert
            Assert.That(result, Is.EqualTo("Version201Xml"));
        }

        /// <summary>
        /// Tests GetNameForFile with namespace prefix that is a partial match.
        /// Input: "Opc.Test.xml" with namespace prefix "Opc.Ua.".
        /// Expected: Returns "OpcTestXml" as prefix doesn't match exactly.
        /// </summary>
        [Test]
        public void GetNameForFile_PartialPrefixMatch_DoesNotStripPrefix()
        {
            // Arrange
            const string inputFile = "Opc.Test.xml";
            const string namespacePrefix = "Opc.Ua.";
            // Act
            string result = Resource.GetNameForFile(inputFile, namespacePrefix);
            // Assert
            Assert.That(result, Is.EqualTo("OpcTestXml"));
        }

        /// <summary>
        /// Tests GetNameForFile with namespace prefix without trailing dot.
        /// Input: "OpcUaTest.xml" with namespace prefix "OpcUa".
        /// Expected: Returns "Test.xml" with prefix stripped exactly.
        /// </summary>
        [Test]
        public void GetNameForFile_PrefixWithoutTrailingDot_StripsExactMatch()
        {
            // Arrange
            const string inputFile = "OpcUaTest.xml";
            const string namespacePrefix = "OpcUa";
            // Act
            string result = Resource.GetNameForFile(inputFile, namespacePrefix);
            // Assert
            Assert.That(result, Is.EqualTo("TestXml"));
        }

        /// <summary>
        /// Tests GetNameForFile with Unicode characters in filename.
        /// Input: "test\u00E9.xml" with null namespace prefix.
        /// Expected: Returns "Test\u00E9Xml" with Unicode preserved.
        /// </summary>
        [Test]
        public void GetNameForFile_UnicodeCharacters_PreservesUnicode()
        {
            // Arrange
            const string inputFile = "test\u00E9.xml";
            const string namespacePrefix = null;
            // Act
            string result = Resource.GetNameForFile(inputFile, namespacePrefix);
            // Assert
            Assert.That(result, Is.EqualTo("Test\u00E9Xml"));
        }

        /// <summary>
        /// Tests that the constructor throws NullReferenceException when resourceName is null.
        /// </summary>
        [Test]
        public void Constructor_NullResourceName_ThrowsNullReferenceException()
        {
            // Arrange
            const string resourceName = null;
            byte[] data =
            [
                1,
                2,
                3
            ];
            // Act & Assert
            Assert.Throws<NullReferenceException>(() => new BinaryResource(resourceName, data, false));
        }

        /// <summary>
        /// Tests the constructor with very long resourceName string.
        /// </summary>
        [Test]
        public void Constructor_VeryLongResourceName_SetsPropertiesCorrectly()
        {
            // Arrange
            string longGroup = new('A', 10000);
            string longName = new('B', 10000);
            string resourceName = longGroup + "." + longName;
            byte[] data =
            [
                1
            ];
            // Act
            var resource = new BinaryResource(resourceName, data, false);
            // Assert
            Assert.That(resource.ResourceGroup, Is.EqualTo(longGroup));
            Assert.That(resource.ResourceName, Is.EqualTo(longName));
            Assert.That(resource.IsText, Is.EqualTo(false));
        }

        /// <summary>
        /// Tests the constructor with resourceName containing only dots followed by single character.
        /// </summary>
        [Test]
        public void Constructor_ManyLeadingDotsWithSingleChar_SetsResourceNameWithDots()
        {
            // Arrange
            const string resourceName = ".....X";
            byte[] data =
            [
                1
            ];
            // Act
            var resource = new BinaryResource(resourceName, data, true);
            // Assert
            Assert.That(resource.ResourceGroup, Is.EqualTo(string.Empty));
            Assert.That(resource.ResourceName, Is.EqualTo(".....X"));
            Assert.That(resource.IsText, Is.EqualTo(true));
        }

        /// <summary>
        /// Tests the constructor with resourceName containing control characters.
        /// </summary>
        [Test]
        public void Constructor_ControlCharactersInResourceName_HandlesCorrectly()
        {
            // Arrange
            const string resourceName = "Group\t.Name\n";
            byte[] data =
            [
                1
            ];
            // Act
            var resource = new BinaryResource(resourceName, data, false);
            // Assert
            Assert.That(resource.ResourceGroup, Is.EqualTo("Group\t"));
            Assert.That(resource.ResourceName, Is.EqualTo("Name\n"));
            Assert.That(resource.IsText, Is.EqualTo(false));
        }

        /// <summary>
        /// Tests the IsText property is set correctly when true.
        /// </summary>
        [Test]
        public void Constructor_IsTextTrue_SetsIsTextPropertyTrue()
        {
            // Arrange
            const string resourceName = "Test";
            byte[] data =
            [
                1
            ];
            // Act
            var resource = new BinaryResource(resourceName, data, true);
            // Assert
            Assert.That(resource.IsText, Is.EqualTo(true));
        }

        /// <summary>
        /// Tests the IsText property is set correctly when false.
        /// </summary>
        [Test]
        public void Constructor_IsTextFalse_SetsIsTextPropertyFalse()
        {
            // Arrange
            const string resourceName = "Test";
            byte[] data =
            [
                1
            ];
            // Act
            var resource = new BinaryResource(resourceName, data, false);
            // Assert
            Assert.That(resource.IsText, Is.EqualTo(false));
        }

        /// <summary>
        /// Tests the constructor with unicode characters in resourceName.
        /// </summary>
        [Test]
        public void Constructor_UnicodeCharacters_HandlesCorrectly()
        {
            // Arrange
            const string resourceName = "グループ.名前";
            byte[] data =
            [
                1
            ];
            // Act
            var resource = new BinaryResource(resourceName, data, true);
            // Assert
            Assert.That(resource.ResourceGroup, Is.EqualTo("グループ"));
            Assert.That(resource.ResourceName, Is.EqualTo("名前"));
            Assert.That(resource.IsText, Is.EqualTo(true));
        }

        /// <summary>
        /// Tests the constructor with resourceName containing consecutive dots in the middle.
        /// </summary>
        [Test]
        public void Constructor_ConsecutiveDotsInMiddle_SplitsOnFirstAfterTrim()
        {
            // Arrange
            const string resourceName = "Group..Name";
            byte[] data =
            [
                1
            ];
            // Act
            var resource = new BinaryResource(resourceName, data, false);
            // Assert
            Assert.That(resource.ResourceGroup, Is.EqualTo("Group"));
            Assert.That(resource.ResourceName, Is.EqualTo(".Name"));
            Assert.That(resource.IsText, Is.EqualTo(false));
        }

        /// <summary>
        /// Tests the constructor with resourceName that has dots trimmed completely leaving no dot.
        /// </summary>
        [Test]
        public void Constructor_DotsOnlyAtEndsNoDotInMiddle_SetsEmptyGroup()
        {
            // Arrange
            const string resourceName = ".....Name.....";
            byte[] data =
            [
                1
            ];
            // Act
            var resource = new BinaryResource(resourceName, data, true);
            // Assert
            Assert.That(resource.ResourceGroup, Is.EqualTo(string.Empty));
            Assert.That(resource.ResourceName, Is.EqualTo(".....Name....."));
            Assert.That(resource.IsText, Is.EqualTo(true));
        }
    }
}
