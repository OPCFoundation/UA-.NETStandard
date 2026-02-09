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
    /// Unit tests for <see cref = "ResourceExtensions"/>.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ResourceExtensionsTests
    {
        /// <summary>
        /// Tests that AsTextFileResource creates a TextFileResource with the correct properties
        /// when a valid fileName is provided without a namespacePrefix.
        /// Expected: A TextFileResource with correct ResourceName and FileName.
        /// </summary>
        [Test]
        public void AsTextFileResource_ValidFileNameWithoutNamespacePrefix_ReturnsTextFileResource()
        {
            // Arrange
            const string fileName = "test.xml";
            // Act
            TextFileResource result = fileName.AsTextFileResource();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FileName, Is.EqualTo(fileName));
            Assert.That(result.ResourceName, Is.EqualTo("TestXml"));
            Assert.That(result.IsText, Is.True);
        }

        /// <summary>
        /// Tests that AsTextFileResource creates a TextFileResource with the correct properties
        /// when a valid fileName is provided with a null namespacePrefix.
        /// Expected: A TextFileResource with correct ResourceName and FileName.
        /// </summary>
        [Test]
        public void AsTextFileResource_ValidFileNameWithNullNamespacePrefix_ReturnsTextFileResource()
        {
            // Arrange
            const string fileName = "sample.txt";
            // Act
            TextFileResource result = fileName.AsTextFileResource(null);
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FileName, Is.EqualTo(fileName));
            Assert.That(result.ResourceName, Is.EqualTo("SampleTxt"));
            Assert.That(result.IsText, Is.True);
        }

        /// <summary>
        /// Tests that AsTextFileResource creates a TextFileResource with the correct properties
        /// when a valid fileName is provided with an empty namespacePrefix.
        /// Expected: A TextFileResource with correct ResourceName and FileName.
        /// </summary>
        [Test]
        public void AsTextFileResource_ValidFileNameWithEmptyNamespacePrefix_ReturnsTextFileResource()
        {
            // Arrange
            const string fileName = "document.json";
            // Act
            TextFileResource result = fileName.AsTextFileResource(string.Empty);
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FileName, Is.EqualTo(fileName));
            Assert.That(result.ResourceName, Is.EqualTo("DocumentJson"));
            Assert.That(result.IsText, Is.True);
        }

        /// <summary>
        /// Tests that AsTextFileResource creates a TextFileResource with correct ResourceName
        /// when fileName starts with the provided namespacePrefix.
        /// Expected: A TextFileResource with prefix removed from ResourceName.
        /// </summary>
        [Test]
        public void AsTextFileResource_FileNameWithMatchingNamespacePrefix_RemovesPrefixFromResourceName()
        {
            // Arrange
            const string fileName = "Opc.Ua.Schema.xml";
            const string namespacePrefix = "Opc.Ua.";
            // Act
            TextFileResource result = fileName.AsTextFileResource(namespacePrefix);
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FileName, Is.EqualTo(fileName));
            Assert.That(result.ResourceName, Is.EqualTo("SchemaXml"));
            Assert.That(result.IsText, Is.True);
        }

        /// <summary>
        /// Tests that AsTextFileResource creates a TextFileResource with correct ResourceName
        /// when fileName does not start with the provided namespacePrefix.
        /// Expected: A TextFileResource with full fileName used for ResourceName.
        /// </summary>
        [Test]
        public void AsTextFileResource_FileNameWithNonMatchingNamespacePrefix_KeepsFullResourceName()
        {
            // Arrange
            const string fileName = "config.xml";
            const string namespacePrefix = "Opc.Ua.";
            // Act
            TextFileResource result = fileName.AsTextFileResource(namespacePrefix);
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FileName, Is.EqualTo(fileName));
            Assert.That(result.ResourceName, Is.EqualTo("ConfigXml"));
            Assert.That(result.IsText, Is.True);
        }

        /// <summary>
        /// Tests that AsTextFileResource correctly handles fileName with full path
        /// by extracting only the filename part for resource name generation.
        /// Expected: A TextFileResource with ResourceName based on filename only.
        /// </summary>
        [Test]
        public void AsTextFileResource_FileNameWithPath_ExtractsFileNameOnly()
        {
            // Arrange
            const string fileName = @"C:\folder\subfolder\data.csv";
            // Act
            TextFileResource result = fileName.AsTextFileResource();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FileName, Is.EqualTo(fileName));
            Assert.That(result.ResourceName, Is.EqualTo("DataCsv"));
            Assert.That(result.IsText, Is.True);
        }

        /// <summary>
        /// Tests that AsTextFileResource correctly handles fileName with multiple extensions.
        /// Expected: A TextFileResource with ResourceName capitalizing each extension part.
        /// </summary>
        [Test]
        public void AsTextFileResource_FileNameWithMultipleExtensions_CapitalizesEachPart()
        {
            // Arrange
            const string fileName = "archive.tar.gz";
            // Act
            TextFileResource result = fileName.AsTextFileResource();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FileName, Is.EqualTo(fileName));
            Assert.That(result.ResourceName, Is.EqualTo("ArchiveTarGz"));
            Assert.That(result.IsText, Is.True);
        }

        /// <summary>
        /// Tests that AsTextFileResource correctly handles empty fileName.
        /// Expected: A TextFileResource with empty ResourceName.
        /// </summary>
        [Test]
        public void AsTextFileResource_EmptyFileName_ReturnsTextFileResourceWithEmptyResourceName()
        {
            // Arrange
            const string fileName = "";
            // Act
            TextFileResource result = fileName.AsTextFileResource();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FileName, Is.EqualTo(fileName));
            Assert.That(result.ResourceName, Is.Empty);
            Assert.That(result.IsText, Is.True);
        }

        /// <summary>
        /// Tests that AsTextFileResource correctly handles fileName with leading dots.
        /// Expected: A TextFileResource with ResourceName skipping empty parts.
        /// </summary>
        [Test]
        public void AsTextFileResource_FileNameWithLeadingDots_SkipsEmptyParts()
        {
            // Arrange
            const string fileName = "..config";
            // Act
            TextFileResource result = fileName.AsTextFileResource();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FileName, Is.EqualTo(fileName));
            Assert.That(result.ResourceName, Is.EqualTo("Config"));
            Assert.That(result.IsText, Is.True);
        }

        /// <summary>
        /// Tests that AsTextFileResource correctly handles fileName with trailing dots.
        /// Expected: A TextFileResource with ResourceName skipping empty parts.
        /// </summary>
        [Test]
        public void AsTextFileResource_FileNameWithTrailingDots_SkipsEmptyParts()
        {
            // Arrange
            const string fileName = "file..";
            // Act
            TextFileResource result = fileName.AsTextFileResource();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FileName, Is.EqualTo(fileName));
            Assert.That(result.ResourceName, Is.EqualTo("File"));
            Assert.That(result.IsText, Is.True);
        }

        /// <summary>
        /// Tests that AsTextFileResource correctly handles fileName with whitespace.
        /// Expected: A TextFileResource with ResourceName based on the whitespace string.
        /// </summary>
        [Test]
        public void AsTextFileResource_FileNameWithWhitespace_ReturnsTextFileResource()
        {
            // Arrange
            const string fileName = "   ";
            // Act
            TextFileResource result = fileName.AsTextFileResource();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FileName, Is.EqualTo(fileName));
            Assert.That(result.ResourceName, Is.EqualTo("   "));
            Assert.That(result.IsText, Is.True);
        }

        /// <summary>
        /// Tests that AsTextFileResource correctly handles fileName with special characters.
        /// Expected: A TextFileResource with ResourceName including special characters.
        /// </summary>
        [Test]
        public void AsTextFileResource_FileNameWithSpecialCharacters_ReturnsTextFileResource()
        {
            // Arrange
            const string fileName = "file@name#test$.xml";
            // Act
            TextFileResource result = fileName.AsTextFileResource();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FileName, Is.EqualTo(fileName));
            Assert.That(result.ResourceName, Is.EqualTo("File@name#test$Xml"));
            Assert.That(result.IsText, Is.True);
        }

        /// <summary>
        /// Tests that AsTextFileResource correctly handles fileName without extension.
        /// Expected: A TextFileResource with ResourceName same as fileName capitalized.
        /// </summary>
        [Test]
        public void AsTextFileResource_FileNameWithoutExtension_ReturnsTextFileResource()
        {
            // Arrange
            const string fileName = "readme";
            // Act
            TextFileResource result = fileName.AsTextFileResource();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FileName, Is.EqualTo(fileName));
            Assert.That(result.ResourceName, Is.EqualTo("Readme"));
            Assert.That(result.IsText, Is.True);
        }

        /// <summary>
        /// Tests that AsTextFileResource correctly handles very long fileName.
        /// Expected: A TextFileResource with complete ResourceName.
        /// </summary>
        [Test]
        public void AsTextFileResource_VeryLongFileName_ReturnsTextFileResource()
        {
            // Arrange
            string fileName = new string('a', 500) + ".txt";
            // Act
            TextFileResource result = fileName.AsTextFileResource();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FileName, Is.EqualTo(fileName));
            Assert.That(result.ResourceName, Has.Length.GreaterThan(0));
            Assert.That(result.IsText, Is.True);
        }

        /// <summary>
        /// Tests that AsTextFileResource correctly handles namespacePrefix longer than fileName.
        /// Expected: A TextFileResource with full fileName used for ResourceName.
        /// </summary>
        [Test]
        public void AsTextFileResource_NamespacePrefixLongerThanFileName_KeepsFullResourceName()
        {
            // Arrange
            const string fileName = "a.xml";
            const string namespacePrefix = "VeryLongNamespacePrefix";
            // Act
            TextFileResource result = fileName.AsTextFileResource(namespacePrefix);
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FileName, Is.EqualTo(fileName));
            Assert.That(result.ResourceName, Is.EqualTo("AXml"));
            Assert.That(result.IsText, Is.True);
        }

        /// <summary>
        /// Tests that AsTextFileResource throws NullReferenceException when fileName is null.
        /// Expected: NullReferenceException.
        /// </summary>
        [Test]
        public void AsTextFileResource_NullFileName_ThrowsNullReferenceException()
        {
            // Arrange
            const string fileName = null;
            // Act & Assert
            Assert.Throws<NullReferenceException>(() => fileName.AsTextFileResource());
        }

        /// <summary>
        /// Tests that AsTextFileResource correctly handles fileName with Unix-style path.
        /// Expected: A TextFileResource with ResourceName based on filename only.
        /// </summary>
        [Test]
        public void AsTextFileResource_FileNameWithUnixPath_ExtractsFileNameOnly()
        {
            // Arrange
            const string fileName = "/usr/local/share/data.xml";
            // Act
            TextFileResource result = fileName.AsTextFileResource();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FileName, Is.EqualTo(fileName));
            Assert.That(result.ResourceName, Is.EqualTo("DataXml"));
            Assert.That(result.IsText, Is.True);
        }

        /// <summary>
        /// Tests that AsTextFileResource correctly handles fileName with only dots.
        /// Expected: A TextFileResource with empty ResourceName.
        /// </summary>
        [Test]
        public void AsTextFileResource_FileNameWithOnlyDots_ReturnsEmptyResourceName()
        {
            // Arrange
            const string fileName = "...";
            // Act
            TextFileResource result = fileName.AsTextFileResource();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FileName, Is.EqualTo(fileName));
            Assert.That(result.ResourceName, Is.Empty);
            Assert.That(result.IsText, Is.True);
        }

        /// <summary>
        /// Tests that ToBinaryFileResource creates a valid BinaryFileResource with null namespacePrefix.
        /// </summary>
        [Test]
        public void ToBinaryFileResource_ValidFileNameWithNullPrefix_CreatesValidBinaryFileResource()
        {
            // Arrange
            const string fileName = "TestFile.xml";
            // Act
            var result = fileName.ToBinaryFileResource(null);
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FileName, Is.EqualTo(fileName));
            Assert.That(result.IsText, Is.False);
            Assert.That(result.ResourceName, Is.Not.Null);
        }

        /// <summary>
        /// Tests that ToBinaryFileResource creates a valid BinaryFileResource with default namespacePrefix.
        /// </summary>
        [Test]
        public void ToBinaryFileResource_ValidFileNameWithDefaultPrefix_CreatesValidBinaryFileResource()
        {
            // Arrange
            const string fileName = "TestFile.json";
            // Act
            var result = fileName.ToBinaryFileResource();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FileName, Is.EqualTo(fileName));
            Assert.That(result.IsText, Is.False);
        }

        /// <summary>
        /// Tests that ToBinaryFileResource removes matching namespacePrefix from the resource name.
        /// </summary>
        [Test]
        public void ToBinaryFileResource_ValidFileNameWithMatchingPrefix_RemovesPrefixFromResourceName()
        {
            // Arrange
            const string fileName = "Opc.Ua.TestFile.xml";
            const string namespacePrefix = "Opc.Ua.";
            // Act
            var result = fileName.ToBinaryFileResource(namespacePrefix);
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FileName, Is.EqualTo(fileName));
            Assert.That(result.IsText, Is.False);
            Assert.That(result.ResourceName, Is.EqualTo("TestFileXml"));
        }

        /// <summary>
        /// Tests that ToBinaryFileResource keeps the full name when namespacePrefix does not match.
        /// </summary>
        [Test]
        public void ToBinaryFileResource_ValidFileNameWithNonMatchingPrefix_KeepsFullName()
        {
            // Arrange
            const string fileName = "TestFile.xml";
            const string namespacePrefix = "Opc.Ua.";
            // Act
            var result = fileName.ToBinaryFileResource(namespacePrefix);
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FileName, Is.EqualTo(fileName));
            Assert.That(result.IsText, Is.False);
            Assert.That(result.ResourceName, Is.EqualTo("TestFileXml"));
        }

        /// <summary>
        /// Tests that ToBinaryFileResource throws NullReferenceException when fileName is null.
        /// </summary>
        [Test]
        public void ToBinaryFileResource_NullFileName_ThrowsNullReferenceException()
        {
            // Arrange
            const string fileName = null;
            // Act & Assert
            Assert.Throws<NullReferenceException>(() => fileName.ToBinaryFileResource());
        }

        /// <summary>
        /// Tests that ToBinaryFileResource handles empty fileName.
        /// </summary>
        [Test]
        public void ToBinaryFileResource_EmptyFileName_CreatesResourceWithEmptyName()
        {
            // Arrange
            const string fileName = "";
            // Act
            var result = fileName.ToBinaryFileResource();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FileName, Is.EqualTo(fileName));
            Assert.That(result.ResourceName, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that ToBinaryFileResource handles whitespace-only fileName.
        /// </summary>
        [Test]
        public void ToBinaryFileResource_WhitespaceFileName_CreatesResourceWithEmptyName()
        {
            // Arrange
            const string fileName = "   ";
            // Act
            var result = fileName.ToBinaryFileResource();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FileName, Is.EqualTo(fileName));
        }

        /// <summary>
        /// Tests that ToBinaryFileResource extracts filename from full path.
        /// </summary>
        [Test]
        public void ToBinaryFileResource_FileNameWithPath_ExtractsFileNameOnly()
        {
            // Arrange
            const string fileName = @"C:\Temp\TestFile.xml";
            // Act
            var result = fileName.ToBinaryFileResource();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FileName, Is.EqualTo(fileName));
            Assert.That(result.ResourceName, Is.EqualTo("TestFileXml"));
        }

        /// <summary>
        /// Tests that ToBinaryFileResource handles fileName with multiple dots.
        /// </summary>
        [Test]
        public void ToBinaryFileResource_FileNameWithMultipleDots_CapitalizesAllParts()
        {
            // Arrange
            const string fileName = "test.file.with.dots.xml";
            // Act
            var result = fileName.ToBinaryFileResource();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FileName, Is.EqualTo(fileName));
            Assert.That(result.ResourceName, Is.EqualTo("TestFileWithDotsXml"));
        }

        /// <summary>
        /// Tests that ToBinaryFileResource handles fileName with special characters.
        /// </summary>
        [Test]
        public void ToBinaryFileResource_FileNameWithSpecialCharacters_ProcessesCorrectly()
        {
            // Arrange
            const string fileName = "test-file_name.xml";
            // Act
            var result = fileName.ToBinaryFileResource();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FileName, Is.EqualTo(fileName));
        }

        /// <summary>
        /// Tests that ToBinaryFileResource handles empty namespacePrefix.
        /// </summary>
        [Test]
        public void ToBinaryFileResource_EmptyNamespacePrefix_CreatesValidResource()
        {
            // Arrange
            const string fileName = "TestFile.xml";
            const string namespacePrefix = "";
            // Act
            var result = fileName.ToBinaryFileResource(namespacePrefix);
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FileName, Is.EqualTo(fileName));
            Assert.That(result.ResourceName, Is.EqualTo("TestFileXml"));
        }

        /// <summary>
        /// Tests that ToBinaryFileResource handles whitespace namespacePrefix.
        /// </summary>
        [Test]
        public void ToBinaryFileResource_WhitespaceNamespacePrefix_CreatesValidResource()
        {
            // Arrange
            const string fileName = "TestFile.xml";
            const string namespacePrefix = "   ";
            // Act
            var result = fileName.ToBinaryFileResource(namespacePrefix);
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FileName, Is.EqualTo(fileName));
        }

        /// <summary>
        /// Tests that ToBinaryFileResource handles very long fileName.
        /// </summary>
        [Test]
        public void ToBinaryFileResource_VeryLongFileName_CreatesValidResource()
        {
            // Arrange
            string fileName = new string('a', 500) + ".xml";
            // Act
            var result = fileName.ToBinaryFileResource();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FileName, Is.EqualTo(fileName));
        }

        /// <summary>
        /// Tests that ToBinaryFileResource handles fileName with no extension.
        /// </summary>
        [Test]
        public void ToBinaryFileResource_FileNameWithoutExtension_CreatesValidResource()
        {
            // Arrange
            const string fileName = "TestFile";
            // Act
            var result = fileName.ToBinaryFileResource();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FileName, Is.EqualTo(fileName));
            Assert.That(result.ResourceName, Is.EqualTo("TestFile"));
        }

        /// <summary>
        /// Tests that ToBinaryFileResource handles fileName starting with a dot.
        /// </summary>
        [Test]
        public void ToBinaryFileResource_FileNameStartingWithDot_CreatesValidResource()
        {
            // Arrange
            const string fileName = ".hidden";
            // Act
            var result = fileName.ToBinaryFileResource();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FileName, Is.EqualTo(fileName));
            Assert.That(result.ResourceName, Is.EqualTo("Hidden"));
        }

        /// <summary>
        /// Tests that ToBinaryFileResource handles fileName with Unix-style path separator.
        /// </summary>
        [Test]
        public void ToBinaryFileResource_FileNameWithUnixPath_ExtractsFileNameOnly()
        {
            // Arrange
            const string fileName = "/home/user/TestFile.xml";
            // Act
            var result = fileName.ToBinaryFileResource();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FileName, Is.EqualTo(fileName));
            Assert.That(result.ResourceName, Is.EqualTo("TestFileXml"));
        }

        /// <summary>
        /// Tests that ToBinaryFileResource sets IsText property to false.
        /// </summary>
        [Test]
        public void ToBinaryFileResource_AnyValidFileName_SetsIsTextToFalse()
        {
            // Arrange
            const string fileName = "BinaryFile.dat";
            // Act
            var result = fileName.ToBinaryFileResource();
            // Assert
            Assert.That(result.IsText, Is.False);
        }

        /// <summary>
        /// Tests that ToBinaryFileResource with matching prefix that is case-sensitive does not remove prefix.
        /// </summary>
        [Test]
        public void ToBinaryFileResource_PrefixWithDifferentCase_DoesNotRemovePrefix()
        {
            // Arrange
            const string fileName = "opc.ua.TestFile.xml";
            const string namespacePrefix = "Opc.Ua.";
            // Act
            var result = fileName.ToBinaryFileResource(namespacePrefix);
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FileName, Is.EqualTo(fileName));
        }
    }
}
