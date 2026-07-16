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
using System.Runtime.InteropServices;
using NUnit.Framework;

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

        [Test]
        public void GetNameForFile_FullFilePath_ExtractsFilenameOnly()
        {
            string inputFile = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
                "C:\\test\\test\\file.xml" :
                "test/test/file.xml";
            // Arrange
            const string namespacePrefix = null;
            // Act
            string result = Resource.GetNameForFile(inputFile, namespacePrefix);
            // Assert
            Assert.That(result, Is.EqualTo("FileXml"));
        }

        [Test]
        public void GetNameForFile_RelativeFilePath_ExtractsFilenameOnly()
        {
            string inputFile = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
                "..\\..\\file.xml" :
                "../../file.xml";
            // Arrange
            const string namespacePrefix = null;
            // Act
            string result = Resource.GetNameForFile(inputFile, namespacePrefix);
            // Assert
            Assert.That(result, Is.EqualTo("FileXml"));
        }

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

        [Test]
        public void GetNameForFile_NullInputFile_ThrowsNullReferenceException()
        {
            // Arrange
            const string inputFile = null;
            const string namespacePrefix = null;
            // Act & Assert
            Assert.Throws<NullReferenceException>(() => Resource.GetNameForFile(inputFile, namespacePrefix));
        }

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

        [Test]
        public void GetNameForFile_FullPathWithMatchingPrefix_ExtractsAndStripsPrefix()
        {
            // Arrange
            string inputFile = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
                "C:\\path\\to\\Opc.Ua.test.xml" :
                "/path/to/Opc.Ua.test.xml";
            const string namespacePrefix = "Opc.Ua.";
            // Act
            string result = Resource.GetNameForFile(inputFile, namespacePrefix);
            // Assert
            Assert.That(result, Is.EqualTo("TestXml"));
        }

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
            Assert.That(resource.IsText, Is.False);
        }

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
            Assert.That(resource.IsText, Is.True);
        }

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
            Assert.That(resource.IsText, Is.False);
        }

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
            Assert.That(resource.IsText, Is.True);
        }

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
            Assert.That(resource.IsText, Is.False);
        }

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
            Assert.That(resource.IsText, Is.True);
        }

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
            Assert.That(resource.IsText, Is.False);
        }

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
            Assert.That(resource.IsText, Is.True);
        }
    }
}
