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

using System.Xml;
using NUnit.Framework;

namespace Opc.Ua.Schema.Model.Tests
{
    /// <summary>
    /// Unit tests for the <see cref="HierarchyNode"/> class.
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class HierarchyNodeTests
    {
        /// <summary>
        /// Tests that ToString returns formatted string when Instance and SymbolicId are not null.
        /// Expected format: "{RelativePath}={Instance.SymbolicId.Name}"
        /// </summary>
        [Test]
        public void ToString_WithInstanceAndSymbolicId_ReturnsFormattedString()
        {
            // Arrange
            var symbolicId = new XmlQualifiedName("TestNodeName", "http://test.namespace");
            var instance = new NodeDesign { SymbolicId = symbolicId };
            var hierarchyNode = new HierarchyNode
            {
                RelativePath = "TestPath",
                Instance = instance
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo("TestPath=TestNodeName"));
        }

        /// <summary>
        /// Tests that ToString returns RelativePath when Instance is null.
        /// </summary>
        [Test]
        public void ToString_WithNullInstance_ReturnsRelativePath()
        {
            // Arrange
            var hierarchyNode = new HierarchyNode
            {
                RelativePath = "TestPath",
                Instance = null
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo("TestPath"));
        }

        /// <summary>
        /// Tests that ToString returns RelativePath when Instance.SymbolicId is null.
        /// </summary>
        [Test]
        public void ToString_WithNullSymbolicId_ReturnsRelativePath()
        {
            // Arrange
            var instance = new NodeDesign { SymbolicId = null };
            var hierarchyNode = new HierarchyNode
            {
                RelativePath = "TestPath",
                Instance = instance
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo("TestPath"));
        }

        /// <summary>
        /// Tests that ToString returns null when RelativePath is null and Instance is null.
        /// </summary>
        [Test]
        public void ToString_WithNullRelativePathAndNullInstance_ReturnsNull()
        {
            // Arrange
            var hierarchyNode = new HierarchyNode
            {
                RelativePath = null,
                Instance = null
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that ToString returns empty string when RelativePath is empty.
        /// </summary>
        [Test]
        public void ToString_WithEmptyRelativePath_ReturnsEmptyString()
        {
            // Arrange
            var hierarchyNode = new HierarchyNode
            {
                RelativePath = string.Empty,
                Instance = null
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that ToString returns formatted string with empty SymbolicId name.
        /// </summary>
        [Test]
        public void ToString_WithEmptySymbolicIdName_ReturnsFormattedStringWithEmptyName()
        {
            // Arrange
            var symbolicId = new XmlQualifiedName(string.Empty, "http://test.namespace");
            var instance = new NodeDesign { SymbolicId = symbolicId };
            var hierarchyNode = new HierarchyNode
            {
                RelativePath = "TestPath",
                Instance = instance
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo("TestPath="));
        }

        /// <summary>
        /// Tests that ToString handles whitespace-only RelativePath correctly.
        /// </summary>
        [Test]
        public void ToString_WithWhitespaceRelativePath_ReturnsWhitespace()
        {
            // Arrange
            var hierarchyNode = new HierarchyNode
            {
                RelativePath = "   ",
                Instance = null
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo("   "));
        }

        /// <summary>
        /// Tests that ToString handles special characters in RelativePath correctly.
        /// </summary>
        [Test]
        public void ToString_WithSpecialCharactersInRelativePath_ReturnsPathWithSpecialCharacters()
        {
            // Arrange
            var hierarchyNode = new HierarchyNode
            {
                RelativePath = "Test/Path\\With:Special*Chars",
                Instance = null
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo("Test/Path\\With:Special*Chars"));
        }

        /// <summary>
        /// Tests that ToString handles special characters in SymbolicId name correctly.
        /// </summary>
        [Test]
        public void ToString_WithSpecialCharactersInSymbolicIdName_ReturnsFormattedStringWithSpecialCharacters()
        {
            // Arrange
            var symbolicId = new XmlQualifiedName("Node:Name<With>Special&Chars", "http://test.namespace");
            var instance = new NodeDesign { SymbolicId = symbolicId };
            var hierarchyNode = new HierarchyNode
            {
                RelativePath = "TestPath",
                Instance = instance
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo("TestPath=Node:Name<With>Special&Chars"));
        }

        /// <summary>
        /// Tests that ToString works with very long strings.
        /// </summary>
        [Test]
        public void ToString_WithVeryLongRelativePath_ReturnsLongString()
        {
            // Arrange
            string longPath = new('a', 10000);
            var hierarchyNode = new HierarchyNode
            {
                RelativePath = longPath,
                Instance = null
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo(longPath));
            Assert.That(result.Length, Is.EqualTo(10000));
        }

        /// <summary>
        /// Tests that ToString handles null RelativePath with valid Instance and SymbolicId.
        /// Expected: Returns formatted string "null={SymbolicIdName}"
        /// </summary>
        [Test]
        public void ToString_WithNullRelativePathAndValidSymbolicId_ReturnsFormattedString()
        {
            // Arrange
            var symbolicId = new XmlQualifiedName("TestNodeName", "http://test.namespace");
            var instance = new NodeDesign { SymbolicId = symbolicId };
            var hierarchyNode = new HierarchyNode
            {
                RelativePath = null,
                Instance = instance
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo("=TestNodeName"));
        }

        /// <summary>
        /// Tests that ToString does not throw when other properties are set to various values.
        /// Verifies that ToString only depends on RelativePath and Instance.SymbolicId.
        /// </summary>
        [Test]
        public void ToString_WithOtherPropertiesSet_ReturnsCorrectString()
        {
            // Arrange
            var symbolicId = new XmlQualifiedName("TestNodeName", "http://test.namespace");
            var instance = new NodeDesign { SymbolicId = symbolicId };
            var hierarchyNode = new HierarchyNode
            {
                RelativePath = "TestPath",
                Instance = instance,
                ExplicitlyDefined = true,
                AdHocInstance = true,
                StaticValue = false,
                Inherited = true,
                Identifier = new object()
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo("TestPath=TestNodeName"));
        }

        /// <summary>
        /// Tests that ToString returns the RelativePath when Instance exists but SymbolicId is null.
        /// </summary>
        [Test]
        public void ToString_WithInstanceButNullSymbolicId_ReturnsRelativePath()
        {
            // Arrange
            var node = new HierarchyNode
            {
                RelativePath = "TestPath",
                Instance = new NodeDesign
                {
                    SymbolicId = null
                }
            };

            // Act
            string result = node.ToString();

            // Assert
            Assert.That(result, Is.EqualTo("TestPath"));
        }

        /// <summary>
        /// Tests that ToString returns empty string when RelativePath is empty and Instance is null.
        /// </summary>
        [Test]
        public void ToString_WithEmptyRelativePathAndNullInstance_ReturnsEmpty()
        {
            // Arrange
            var node = new HierarchyNode
            {
                RelativePath = string.Empty,
                Instance = null
            };

            // Act
            string result = node.ToString();

            // Assert
            Assert.That(result, Is.Empty);
        }

        /// <summary>
        /// Tests that ToString returns formatted string with null RelativePath when Instance and SymbolicId are present.
        /// </summary>
        [Test]
        public void ToString_WithNullRelativePathButValidInstance_ReturnsFormattedString()
        {
            // Arrange
            var node = new HierarchyNode
            {
                RelativePath = null,
                Instance = new NodeDesign
                {
                    SymbolicId = new XmlQualifiedName("TestName", "http://test.namespace")
                }
            };

            // Act
            string result = node.ToString();

            // Assert
            Assert.That(result, Is.EqualTo("=TestName"));
        }

        /// <summary>
        /// Tests that ToString handles whitespace RelativePath correctly.
        /// </summary>
        [TestCase("   ")]
        [TestCase("\t")]
        [TestCase("\n")]
        public void ToString_WithWhitespaceRelativePath_ReturnsWhitespace(string relativePath)
        {
            // Arrange
            var node = new HierarchyNode
            {
                RelativePath = relativePath,
                Instance = null
            };

            // Act
            string result = node.ToString();

            // Assert
            Assert.That(result, Is.EqualTo(relativePath));
        }

        /// <summary>
        /// Tests that ToString handles special characters in RelativePath.
        /// </summary>
        [TestCase("Path/With/Slashes")]
        [TestCase("Path\\With\\Backslashes")]
        [TestCase("Path:With:Colons")]
        [TestCase("Path.With.Dots")]
        [TestCase("Path_With_Underscores")]
        public void ToString_WithSpecialCharactersInRelativePath_ReturnsPath(string relativePath)
        {
            // Arrange
            var node = new HierarchyNode
            {
                RelativePath = relativePath,
                Instance = null
            };

            // Act
            string result = node.ToString();

            // Assert
            Assert.That(result, Is.EqualTo(relativePath));
        }

        /// <summary>
        /// Tests that ToString handles special characters in SymbolicId name.
        /// </summary>
        [TestCase("Name:With:Colons")]
        [TestCase("Name.With.Dots")]
        [TestCase("Name_With_Underscores")]
        public void ToString_WithSpecialCharactersInSymbolicIdName_ReturnsFormattedString(string symbolicIdName)
        {
            // Arrange
            var node = new HierarchyNode
            {
                RelativePath = "Path",
                Instance = new NodeDesign
                {
                    SymbolicId = new XmlQualifiedName(symbolicIdName, "http://test.namespace")
                }
            };

            // Act
            string result = node.ToString();

            // Assert
            Assert.That(result, Is.EqualTo($"Path={symbolicIdName}"));
        }

        /// <summary>
        /// Tests that ToString returns empty formatted string when both RelativePath and SymbolicId name are empty.
        /// </summary>
        [Test]
        public void ToString_WithEmptyRelativePathAndEmptySymbolicIdName_ReturnsEquals()
        {
            // Arrange
            var node = new HierarchyNode
            {
                RelativePath = string.Empty,
                Instance = new NodeDesign
                {
                    SymbolicId = new XmlQualifiedName(string.Empty, "http://test.namespace")
                }
            };

            // Act
            string result = node.ToString();

            // Assert
            Assert.That(result, Is.EqualTo("="));
        }

        /// <summary>
        /// Tests that ToString handles very long RelativePath correctly.
        /// </summary>
        [Test]
        public void ToString_WithVeryLongRelativePath_ReturnsFullPath()
        {
            // Arrange
            string longPath = new('x', 10000);
            var node = new HierarchyNode
            {
                RelativePath = longPath,
                Instance = null
            };

            // Act
            string result = node.ToString();

            // Assert
            Assert.That(result, Is.EqualTo(longPath));
            Assert.That(result.Length, Is.EqualTo(10000));
        }

        /// <summary>
        /// Tests that ToString handles very long SymbolicId name correctly.
        /// </summary>
        [Test]
        public void ToString_WithVeryLongSymbolicIdName_ReturnsFormattedString()
        {
            // Arrange
            string longName = new('y', 10000);
            var node = new HierarchyNode
            {
                RelativePath = "Path",
                Instance = new NodeDesign
                {
                    SymbolicId = new XmlQualifiedName(longName, "http://test.namespace")
                }
            };

            // Act
            string result = node.ToString();

            // Assert
            Assert.That(result, Is.EqualTo($"Path={longName}"));
        }

        /// <summary>
        /// Tests that ToString() without parameters returns the RelativePath when Instance is null.
        /// </summary>
        [Test]
        public void ToString_InstanceIsNull_ReturnsRelativePath()
        {
            // Arrange
            var hierarchyNode = new HierarchyNode
            {
                RelativePath = "TestPath",
                Instance = null
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo("TestPath"));
        }

        /// <summary>
        /// Tests that ToString() without parameters returns the RelativePath when Instance.SymbolicId is null.
        /// </summary>
        [Test]
        public void ToString_InstanceSymbolicIdIsNull_ReturnsRelativePath()
        {
            // Arrange
            var hierarchyNode = new HierarchyNode
            {
                RelativePath = "TestPath",
                Instance = new ObjectDesign
                {
                    SymbolicId = null
                }
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo("TestPath"));
        }

        /// <summary>
        /// Tests that ToString() without parameters returns formatted string when Instance and SymbolicId are not null.
        /// </summary>
        [Test]
        public void ToString_InstanceAndSymbolicIdNotNull_ReturnsFormattedString()
        {
            // Arrange
            var hierarchyNode = new HierarchyNode
            {
                RelativePath = "TestPath",
                Instance = new ObjectDesign
                {
                    SymbolicId = new XmlQualifiedName("TestSymbolic", "http://test.com")
                }
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo("TestPath=TestSymbolic"));
        }

        /// <summary>
        /// Tests that ToString(null, formatProvider) returns the RelativePath when Instance.SymbolicId is null.
        /// </summary>
        [Test]
        public void ToString_FormatNullSymbolicIdNull_ReturnsRelativePath()
        {
            // Arrange
            var hierarchyNode = new HierarchyNode
            {
                RelativePath = "MyPath",
                Instance = new ObjectDesign
                {
                    SymbolicId = null
                }
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo("MyPath"));
        }

        /// <summary>
        /// Tests that ToString(null, formatProvider) returns formatted string using the formatProvider.
        /// </summary>
        [Test]
        public void ToString_FormatNullWithFormatProvider_ReturnsFormattedStringWithProvider()
        {
            // Arrange
            var hierarchyNode = new HierarchyNode
            {
                RelativePath = "Path1",
                Instance = new ObjectDesign
                {
                    SymbolicId = new XmlQualifiedName("Symbol1", "http://test.com")
                }
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo("Path1=Symbol1"));
        }

        /// <summary>
        /// Tests that ToString returns null when RelativePath is null and Instance is null.
        /// </summary>
        [Test]
        public void ToString_RelativePathNullInstanceNull_ReturnsNull()
        {
            // Arrange
            var hierarchyNode = new HierarchyNode
            {
                RelativePath = null,
                Instance = null
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that ToString returns empty string when RelativePath is empty and Instance is null.
        /// </summary>
        [Test]
        public void ToString_RelativePathEmptyInstanceNull_ReturnsEmptyString()
        {
            // Arrange
            var hierarchyNode = new HierarchyNode
            {
                RelativePath = string.Empty,
                Instance = null
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that ToString handles RelativePath with special characters correctly.
        /// </summary>
        [Test]
        public void ToString_RelativePathWithSpecialCharacters_ReturnsRelativePath()
        {
            // Arrange
            var hierarchyNode = new HierarchyNode
            {
                RelativePath = "Path/With\\Special_Characters-123",
                Instance = null
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo("Path/With\\Special_Characters-123"));
        }

        /// <summary>
        /// Tests that ToString formats correctly when SymbolicId.Name contains special characters.
        /// </summary>
        [Test]
        public void ToString_SymbolicIdNameWithSpecialCharacters_ReturnsFormattedString()
        {
            // Arrange
            var hierarchyNode = new HierarchyNode
            {
                RelativePath = "Path",
                Instance = new ObjectDesign
                {
                    SymbolicId = new XmlQualifiedName("Name_With-Special.Chars", "http://test.com")
                }
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo("Path=Name_With-Special.Chars"));
        }

        /// <summary>
        /// Tests that ToString with different formatProvider cultures produces correct result.
        /// </summary>
        [TestCase("en-US")]
        [TestCase("de-DE")]
        [TestCase("ja-JP")]
        public void ToString_WithDifferentCultures_ReturnsFormattedString(string cultureName)
        {
            // Arrange
            var hierarchyNode = new HierarchyNode
            {
                RelativePath = "TestPath",
                Instance = new ObjectDesign
                {
                    SymbolicId = new XmlQualifiedName("TestName", "http://test.com")
                }
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo("TestPath=TestName"));
        }

        /// <summary>
        /// Tests that ToString handles very long RelativePath values correctly.
        /// </summary>
        [Test]
        public void ToString_VeryLongRelativePath_ReturnsLongString()
        {
            // Arrange
            string longPath = new('a', 10000);
            var hierarchyNode = new HierarchyNode
            {
                RelativePath = longPath,
                Instance = null
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo(longPath));
        }

        /// <summary>
        /// Tests that ToString handles very long SymbolicId.Name values correctly.
        /// </summary>
        [Test]
        public void ToString_VeryLongSymbolicIdName_ReturnsFormattedString()
        {
            // Arrange
            string longName = new('b', 10000);
            var hierarchyNode = new HierarchyNode
            {
                RelativePath = "Path",
                Instance = new ObjectDesign
                {
                    SymbolicId = new XmlQualifiedName(longName, "http://test.com")
                }
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo($"Path={longName}"));
        }

        /// <summary>
        /// Tests that ToString handles null SymbolicId.Name correctly when SymbolicId is not null.
        /// Note: XmlQualifiedName.Name should not be null in normal usage, but we test the edge case.
        /// </summary>
        [Test]
        public void ToString_SymbolicIdNameNull_ReturnsFormattedStringWithNull()
        {
            // Arrange
            var hierarchyNode = new HierarchyNode
            {
                RelativePath = "Path",
                Instance = new ObjectDesign
                {
                    SymbolicId = new XmlQualifiedName(null, "http://test.com")
                }
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo("Path="));
        }

        /// <summary>
        /// Tests that ToString returns RelativePath when Instance is null.
        /// Input: RelativePath is set, Instance is null.
        /// Expected: Returns RelativePath string.
        /// </summary>
        [Test]
        public void ToString_InstanceNull_ReturnsRelativePath()
        {
            // Arrange
            const string expectedPath = "TestPath";
            var hierarchyNode = new HierarchyNode
            {
                RelativePath = expectedPath,
                Instance = null
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo(expectedPath));
        }

        /// <summary>
        /// Tests that ToString returns RelativePath when Instance.SymbolicId is null.
        /// Input: RelativePath is set, Instance is set but SymbolicId is null.
        /// Expected: Returns RelativePath string.
        /// </summary>
        [Test]
        public void ToString_InstanceSymbolicIdNull_ReturnsRelativePath()
        {
            // Arrange
            const string expectedPath = "TestPath";
            var mockNodeDesign = new NodeDesign
            {
                SymbolicId = null
            };

            var hierarchyNode = new HierarchyNode
            {
                RelativePath = expectedPath,
                Instance = mockNodeDesign
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo(expectedPath));
        }

        /// <summary>
        /// Tests that ToString returns null when RelativePath is null and Instance is null.
        /// Input: RelativePath is null, Instance is null.
        /// Expected: Returns null.
        /// </summary>
        [Test]
        public void ToString_RelativePathNullAndInstanceNull_ReturnsNull()
        {
            // Arrange
            var hierarchyNode = new HierarchyNode
            {
                RelativePath = null,
                Instance = null
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that ToString returns empty string when RelativePath is empty and Instance is null.
        /// Input: RelativePath is empty string, Instance is null.
        /// Expected: Returns empty string.
        /// </summary>
        [Test]
        public void ToString_RelativePathEmptyAndInstanceNull_ReturnsEmpty()
        {
            // Arrange
            var hierarchyNode = new HierarchyNode
            {
                RelativePath = string.Empty,
                Instance = null
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that ToString returns whitespace when RelativePath is whitespace and Instance is null.
        /// Input: RelativePath contains only whitespace, Instance is null.
        /// Expected: Returns whitespace string.
        /// </summary>
        [Test]
        public void ToString_RelativePathWhitespaceAndInstanceNull_ReturnsWhitespace()
        {
            // Arrange
            const string whitespace = "   ";
            var hierarchyNode = new HierarchyNode
            {
                RelativePath = whitespace,
                Instance = null
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo(whitespace));
        }

        /// <summary>
        /// Tests that ToString handles special characters in RelativePath.
        /// Input: RelativePath contains special characters, Instance is null.
        /// Expected: Returns RelativePath with special characters intact.
        /// </summary>
        [Test]
        public void ToString_RelativePathWithSpecialCharacters_ReturnsSpecialCharacters()
        {
            // Arrange
            const string specialPath = "Path/With\\Special@#$%Characters";
            var hierarchyNode = new HierarchyNode
            {
                RelativePath = specialPath,
                Instance = null
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo(specialPath));
        }

        /// <summary>
        /// Tests that ToString handles special characters in both RelativePath and SymbolicId.Name.
        /// Input: RelativePath and SymbolicId.Name both contain special characters.
        /// Expected: Returns formatted string with special characters intact.
        /// </summary>
        [Test]
        public void ToString_SpecialCharactersInBothPathAndName_ReturnsFormattedStringWithSpecialCharacters()
        {
            // Arrange
            const string relativePath = "Path<>&\"'";
            const string symbolicIdName = "Name!@#$%";
            var symbolicId = new XmlQualifiedName(symbolicIdName);
            var mockNodeDesign = new NodeDesign
            {
                SymbolicId = symbolicId
            };

            var hierarchyNode = new HierarchyNode
            {
                RelativePath = relativePath,
                Instance = mockNodeDesign
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo($"{relativePath}={symbolicIdName}"));
        }

        /// <summary>
        /// Tests that ToString handles null RelativePath with non-null Instance.SymbolicId.
        /// Input: RelativePath is null, Instance.SymbolicId is set.
        /// Expected: Returns formatted string with null path.
        /// </summary>
        [Test]
        public void ToString_RelativePathNullWithSymbolicId_ReturnsFormattedStringWithNullPath()
        {
            // Arrange
            const string symbolicIdName = "SymbolicName";
            var symbolicId = new XmlQualifiedName(symbolicIdName);
            var mockNodeDesign = new NodeDesign
            {
                SymbolicId = symbolicId
            };

            var hierarchyNode = new HierarchyNode
            {
                RelativePath = null,
                Instance = mockNodeDesign
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo($"={symbolicIdName}"));
        }

        /// <summary>
        /// Tests that ToString handles empty RelativePath with non-null Instance.SymbolicId.
        /// Input: RelativePath is empty, Instance.SymbolicId is set.
        /// Expected: Returns formatted string with empty path.
        /// </summary>
        [Test]
        public void ToString_RelativePathEmptyWithSymbolicId_ReturnsFormattedStringWithEmptyPath()
        {
            // Arrange
            const string symbolicIdName = "SymbolicName";
            var symbolicId = new XmlQualifiedName(symbolicIdName);
            var mockNodeDesign = new NodeDesign
            {
                SymbolicId = symbolicId
            };

            var hierarchyNode = new HierarchyNode
            {
                RelativePath = string.Empty,
                Instance = mockNodeDesign
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo($"={symbolicIdName}"));
        }

        /// <summary>
        /// Tests that ToString handles empty SymbolicId.Name.
        /// Input: RelativePath is set, Instance.SymbolicId.Name is empty.
        /// Expected: Returns formatted string with empty name.
        /// </summary>
        [Test]
        public void ToString_SymbolicIdNameEmpty_ReturnsFormattedStringWithEmptyName()
        {
            // Arrange
            const string relativePath = "TestPath";
            var symbolicId = new XmlQualifiedName(string.Empty);
            var mockNodeDesign = new NodeDesign
            {
                SymbolicId = symbolicId
            };

            var hierarchyNode = new HierarchyNode
            {
                RelativePath = relativePath,
                Instance = mockNodeDesign
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo($"{relativePath}="));
        }

        /// <summary>
        /// Tests that ToString handles very long strings.
        /// Input: RelativePath and SymbolicId.Name are very long strings.
        /// Expected: Returns formatted string with full content.
        /// </summary>
        [Test]
        public void ToString_VeryLongStrings_ReturnsFormattedStringWithFullContent()
        {
            // Arrange
            string relativePath = new('A', 10000);
            string symbolicIdName = new('B', 10000);
            var symbolicId = new XmlQualifiedName(symbolicIdName);
            var mockNodeDesign = new NodeDesign
            {
                SymbolicId = symbolicId
            };

            var hierarchyNode = new HierarchyNode
            {
                RelativePath = relativePath,
                Instance = mockNodeDesign
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo($"{relativePath}={symbolicIdName}"));
            Assert.That(result.Length, Is.EqualTo(20001)); // 10000 + 1 ('=') + 10000
        }

        /// <summary>
        /// Tests that ToString handles XmlQualifiedName with namespace.
        /// Input: SymbolicId has both Name and Namespace set.
        /// Expected: Returns formatted string using only Name part.
        /// </summary>
        [Test]
        public void ToString_SymbolicIdWithNamespace_UsesOnlyName()
        {
            // Arrange
            const string relativePath = "TestPath";
            const string symbolicIdName = "LocalName";
            const string namespaceUri = "http://test.namespace.com";
            var symbolicId = new XmlQualifiedName(symbolicIdName, namespaceUri);
            var mockNodeDesign = new NodeDesign
            {
                SymbolicId = symbolicId
            };

            var hierarchyNode = new HierarchyNode
            {
                RelativePath = relativePath,
                Instance = mockNodeDesign
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo($"{relativePath}={symbolicIdName}"));
            Assert.That(result, Does.Not.Contain(namespaceUri));
        }

        /// <summary>
        /// Tests that ToString returns consistent results on multiple calls.
        /// Input: Same HierarchyNode instance.
        /// Expected: Multiple calls return the same string.
        /// </summary>
        [Test]
        public void ToString_MultipleCalls_ReturnsConsistentResults()
        {
            // Arrange
            const string relativePath = "Path";
            const string symbolicIdName = "Name";
            var symbolicId = new XmlQualifiedName(symbolicIdName);
            var mockNodeDesign = new NodeDesign
            {
                SymbolicId = symbolicId
            };

            var hierarchyNode = new HierarchyNode
            {
                RelativePath = relativePath,
                Instance = mockNodeDesign
            };

            // Act
            string result1 = hierarchyNode.ToString();
            string result2 = hierarchyNode.ToString();
            string result3 = hierarchyNode.ToString();

            // Assert
            Assert.That(result1, Is.EqualTo(result2));
            Assert.That(result2, Is.EqualTo(result3));
            Assert.That(result1, Is.EqualTo($"{relativePath}={symbolicIdName}"));
        }

        /// <summary>
        /// Tests that ToString handles control characters in strings.
        /// Input: RelativePath contains control characters.
        /// Expected: Returns string with control characters intact.
        /// </summary>
        [TestCase("\t", TestName = "ToString_RelativePathWithTab_ReturnsTab")]
        [TestCase("\n", TestName = "ToString_RelativePathWithNewline_ReturnsNewline")]
        [TestCase("\r", TestName = "ToString_RelativePathWithCarriageReturn_ReturnsCarriageReturn")]
        [TestCase("\t\n\r", TestName = "ToString_RelativePathWithMultipleControlChars_ReturnsAll")]
        public void ToString_RelativePathWithControlCharacters_ReturnsControlCharacters(string controlChars)
        {
            // Arrange
            var hierarchyNode = new HierarchyNode
            {
                RelativePath = controlChars,
                Instance = null
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo(controlChars));
        }

        /// <summary>
        /// Tests that ToString handles Unicode characters.
        /// Input: RelativePath and SymbolicId.Name contain Unicode characters.
        /// Expected: Returns formatted string with Unicode characters intact.
        /// </summary>
        [Test]
        public void ToString_UnicodeCharacters_ReturnsUnicodeCharacters()
        {
            // Arrange
            const string relativePath = "Path日本語中文";
            const string symbolicIdName = "Nameрусский한국어";
            var symbolicId = new XmlQualifiedName(symbolicIdName);
            var mockNodeDesign = new NodeDesign
            {
                SymbolicId = symbolicId
            };

            var hierarchyNode = new HierarchyNode
            {
                RelativePath = relativePath,
                Instance = mockNodeDesign
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo($"{relativePath}={symbolicIdName}"));
        }

        /// <summary>
        /// Tests that ToString handles other properties being set.
        /// Input: HierarchyNode with all properties set, checking ToString ignores non-relevant properties.
        /// Expected: Returns formatted string based only on RelativePath and Instance.SymbolicId.
        /// </summary>
        [Test]
        public void ToString_AllPropertiesSet_UsesOnlyRelevantProperties()
        {
            // Arrange
            const string relativePath = "TestPath";
            const string symbolicIdName = "TestName";
            var symbolicId = new XmlQualifiedName(symbolicIdName);
            var mockNodeDesign = new NodeDesign
            {
                SymbolicId = symbolicId
            };

            var hierarchyNode = new HierarchyNode
            {
                RelativePath = relativePath,
                Instance = mockNodeDesign,
                ExplicitlyDefined = true,
                AdHocInstance = true,
                StaticValue = true,
                Inherited = true,
                Identifier = new object()
            };

            // Act
            string result = hierarchyNode.ToString();

            // Assert
            Assert.That(result, Is.EqualTo($"{relativePath}={symbolicIdName}"));
        }
    }
}
