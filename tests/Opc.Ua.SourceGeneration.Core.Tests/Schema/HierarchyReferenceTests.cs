// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Xml;
using NUnit.Framework;

namespace Opc.Ua.Schema.Model.Tests
{
    /// <summary>
    /// Unit tests for the HierarchyReference class.
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class HierarchyReferenceTests
    {
        /// <summary>
        /// Tests that ToString with null format and non-null TargetId returns the correct formatted string using TargetId.Name.
        /// </summary>
        [Test]
        public void ToString_WithTargetId_ReturnsFormattedStringWithTargetIdName()
        {
            // Arrange
            var hierarchyReference = new HierarchyReference
            {
                SourcePath = "Source/Path",
                TargetPath = "Target/Path",
                TargetId = new XmlQualifiedName("TargetName", "http://example.com")
            };

            // Act
            string result = hierarchyReference.ToString();

            // Assert
            Assert.That(result, Is.EqualTo("Source/Path => TargetName"));
        }

        /// <summary>
        /// Tests that ToString with null format and null TargetId returns the correct formatted string using TargetPath.
        /// </summary>
        [Test]
        public void ToString_WithoutTargetId_ReturnsFormattedStringWithTargetPath()
        {
            // Arrange
            var hierarchyReference = new HierarchyReference
            {
                SourcePath = "Source/Path",
                TargetPath = "Target/Path",
                TargetId = null!
            };

            // Act
            string result = hierarchyReference.ToString();

            // Assert
            Assert.That(result, Is.EqualTo("Source/Path => Target/Path"));
        }

        /// <summary>
        /// Tests that ToString with null SourcePath returns a formatted string with null SourcePath.
        /// </summary>
        [Test]
        public void ToString_NullSourcePath_ReturnsFormattedStringWithNull()
        {
            // Arrange
            var hierarchyReference = new HierarchyReference
            {
                SourcePath = null!,
                TargetPath = "Target/Path",
                TargetId = null!
            };

            // Act
            string result = hierarchyReference.ToString();

            // Assert
            Assert.That(result, Is.EqualTo(" => Target/Path"));
        }

        /// <summary>
        /// Tests that ToString with empty SourcePath returns a formatted string with empty SourcePath.
        /// </summary>
        [Test]
        public void ToString_EmptySourcePath_ReturnsFormattedStringWithEmpty()
        {
            // Arrange
            var hierarchyReference = new HierarchyReference
            {
                SourcePath = string.Empty,
                TargetPath = "Target/Path",
                TargetId = null!
            };

            // Act
            string result = hierarchyReference.ToString();

            // Assert
            Assert.That(result, Is.EqualTo(" => Target/Path"));
        }

        /// <summary>
        /// Tests that ToString with null TargetPath and null TargetId returns a formatted string with null TargetPath.
        /// </summary>
        [Test]
        public void ToString_NullTargetPathAndNullTargetId_ReturnsFormattedStringWithNull()
        {
            // Arrange
            var hierarchyReference = new HierarchyReference
            {
                SourcePath = "Source/Path",
                TargetPath = null!,
                TargetId = null!
            };

            // Act
            string result = hierarchyReference.ToString();

            // Assert
            Assert.That(result, Is.EqualTo("Source/Path => "));
        }

        /// <summary>
        /// Tests that ToString with empty TargetPath and null TargetId returns a formatted string with empty TargetPath.
        /// </summary>
        [Test]
        public void ToString_EmptyTargetPathAndNullTargetId_ReturnsFormattedStringWithEmpty()
        {
            // Arrange
            var hierarchyReference = new HierarchyReference
            {
                SourcePath = "Source/Path",
                TargetPath = string.Empty,
                TargetId = null!
            };

            // Act
            string result = hierarchyReference.ToString();

            // Assert
            Assert.That(result, Is.EqualTo("Source/Path => "));
        }

        /// <summary>
        /// Tests that ToString with TargetId having empty Name returns a formatted string with empty Name.
        /// </summary>
        [Test]
        public void ToString_TargetIdWithEmptyName_ReturnsFormattedStringWithEmptyName()
        {
            // Arrange
            var hierarchyReference = new HierarchyReference
            {
                SourcePath = "Source/Path",
                TargetPath = "Target/Path",
                TargetId = new XmlQualifiedName(string.Empty, "http://example.com")
            };

            // Act
            string result = hierarchyReference.ToString();

            // Assert
            Assert.That(result, Is.EqualTo("Source/Path => "));
        }

        /// <summary>
        /// Tests that ToString with special characters in paths returns correctly formatted string.
        /// </summary>
        [TestCase("Source/Path!@#", "Target/Path$%^", "Source/Path!@# => Target/Path$%^")]
        [TestCase("Source\tPath", "Target\nPath", "Source\tPath => Target\nPath")]
        [TestCase("Source Path With Spaces", "Target Path", "Source Path With Spaces => Target Path")]
        public void ToString_SpecialCharactersInPaths_ReturnsCorrectFormattedString(string sourcePath, string targetPath, string expected)
        {
            // Arrange
            var hierarchyReference = new HierarchyReference
            {
                SourcePath = sourcePath,
                TargetPath = targetPath,
                TargetId = null!
            };

            // Act
            string result = hierarchyReference.ToString();

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }
    }
}
