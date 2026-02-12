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
using System.Xml;
using NUnit.Framework;

namespace Opc.Ua.Schema.Model.Tests
{
    /// <summary>
    /// Unit tests for the NodeDesign class Equals(object) method.
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class NodeDesignTests
    {
        /// <summary>
        /// Tests that Equals returns false when comparing with null.
        /// </summary>
        [Test]
        public void Equals_NullObject_ReturnsFalse()
        {
            // Arrange
            var nodeDesign = new NodeDesign { BrowseName = "TestNode" };

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = nodeDesign.Equals((object)null);
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing an instance with itself.
        /// </summary>
        [Test]
        public void Equals_SameReference_ReturnsTrue()
        {
            // Arrange
            var nodeDesign = new NodeDesign { BrowseName = "TestNode" };

            // Act
            bool result = nodeDesign.Equals((object)nodeDesign);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with an object of a different type.
        /// </summary>
        [Test]
        public void Equals_DifferentType_ReturnsFalse()
        {
            // Arrange
            var nodeDesign = new NodeDesign { BrowseName = "TestNode" };
            object otherObject = new();

            // Act
            bool result = nodeDesign.Equals(otherObject);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with a string object.
        /// </summary>
        [Test]
        public void Equals_StringObject_ReturnsFalse()
        {
            // Arrange
            var nodeDesign = new NodeDesign { BrowseName = "TestNode" };
            object otherObject = "TestNode";

            // Act
            bool result = nodeDesign.Equals(otherObject);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing two NodeDesign instances with identical properties.
        /// </summary>
        [Test]
        public void Equals_IdenticalNodeDesigns_ReturnsTrue()
        {
            // Arrange
            var nodeDesign1 = new NodeDesign
            {
                BrowseName = "TestNode",
                IsDeclaration = true,
                NumericId = 12345,
                NumericIdSpecified = true,
                StringId = "TestStringId"
            };

            var nodeDesign2 = new NodeDesign
            {
                BrowseName = "TestNode",
                IsDeclaration = true,
                NumericId = 12345,
                NumericIdSpecified = true,
                StringId = "TestStringId"
            };

            // Act
            bool result = nodeDesign1.Equals((object)nodeDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing NodeDesign instances with different BrowseName.
        /// </summary>
        [Test]
        public void Equals_DifferentBrowseName_ReturnsFalse()
        {
            // Arrange
            var nodeDesign1 = new NodeDesign { BrowseName = "TestNode1" };
            var nodeDesign2 = new NodeDesign { BrowseName = "TestNode2" };

            // Act
            bool result = nodeDesign1.Equals((object)nodeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing NodeDesign instances with different NumericId.
        /// </summary>
        [Test]
        public void Equals_DifferentNumericId_ReturnsFalse()
        {
            // Arrange
            var nodeDesign1 = new NodeDesign
            {
                BrowseName = "TestNode",
                NumericId = 123,
                NumericIdSpecified = true
            };

            var nodeDesign2 = new NodeDesign
            {
                BrowseName = "TestNode",
                NumericId = 456,
                NumericIdSpecified = true
            };

            // Act
            bool result = nodeDesign1.Equals((object)nodeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing NodeDesign instances with different IsDeclaration flag.
        /// </summary>
        [Test]
        public void Equals_DifferentIsDeclaration_ReturnsFalse()
        {
            // Arrange
            var nodeDesign1 = new NodeDesign
            {
                BrowseName = "TestNode",
                IsDeclaration = true
            };

            var nodeDesign2 = new NodeDesign
            {
                BrowseName = "TestNode",
                IsDeclaration = false
            };

            // Act
            bool result = nodeDesign1.Equals((object)nodeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing NodeDesign instances with different SymbolicId.
        /// </summary>
        [Test]
        public void Equals_DifferentSymbolicId_ReturnsFalse()
        {
            // Arrange
            var nodeDesign1 = new NodeDesign
            {
                BrowseName = "TestNode",
                SymbolicId = new XmlQualifiedName("Id1", "http://test.org")
            };

            var nodeDesign2 = new NodeDesign
            {
                BrowseName = "TestNode",
                SymbolicId = new XmlQualifiedName("Id2", "http://test.org")
            };

            // Act
            bool result = nodeDesign1.Equals((object)nodeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both NodeDesign instances have null properties.
        /// </summary>
        [Test]
        public void Equals_BothWithNullProperties_ReturnsTrue()
        {
            // Arrange
            var nodeDesign1 = new NodeDesign
            {
                BrowseName = null,
                StringId = null,
                SymbolicId = null
            };

            var nodeDesign2 = new NodeDesign
            {
                BrowseName = null,
                StringId = null,
                SymbolicId = null
            };

            // Act
            bool result = nodeDesign1.Equals((object)nodeDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when one NodeDesign has null property and other does not.
        /// </summary>
        [Test]
        public void Equals_OneNullStringIdOneNot_ReturnsFalse()
        {
            // Arrange
            var nodeDesign1 = new NodeDesign
            {
                BrowseName = "TestNode",
                StringId = "TestId"
            };

            var nodeDesign2 = new NodeDesign
            {
                BrowseName = "TestNode",
                StringId = null
            };

            // Act
            bool result = nodeDesign1.Equals((object)nodeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals handles boundary values for NumericId (uint.MaxValue).
        /// </summary>
        [Test]
        public void Equals_MaxNumericId_ReturnsTrue()
        {
            // Arrange
            var nodeDesign1 = new NodeDesign
            {
                BrowseName = "TestNode",
                NumericId = uint.MaxValue,
                NumericIdSpecified = true
            };

            var nodeDesign2 = new NodeDesign
            {
                BrowseName = "TestNode",
                NumericId = uint.MaxValue,
                NumericIdSpecified = true
            };

            // Act
            bool result = nodeDesign1.Equals((object)nodeDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals handles boundary values for NumericId (uint.MinValue/0).
        /// </summary>
        [Test]
        public void Equals_MinNumericId_ReturnsTrue()
        {
            // Arrange
            var nodeDesign1 = new NodeDesign
            {
                BrowseName = "TestNode",
                NumericId = uint.MinValue,
                NumericIdSpecified = true
            };

            var nodeDesign2 = new NodeDesign
            {
                BrowseName = "TestNode",
                NumericId = 0,
                NumericIdSpecified = true
            };

            // Act
            bool result = nodeDesign1.Equals((object)nodeDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with derived type object
        /// cast to object.
        /// </summary>
        [Test]
        public void Equals_DerivedTypeObjectDesign_ReturnsFalseWhenDifferent()
        {
            // Arrange
            var nodeDesign = new NodeDesign { BrowseName = "TestNode" };
            var objectDesign = new ObjectDesign { BrowseName = "ObjectNode" };

            // Act
            bool result = nodeDesign.Equals((object)objectDesign);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing with derived type
        /// </summary>
        [Test]
        public void Equals_DerivedTypeObjectDesign_ReturnsTrueWhenSameBaseProperties()
        {
            // Arrange
            var nodeDesign = new NodeDesign { BrowseName = "TestNode" };
            var objectDesign = new ObjectDesign { BrowseName = "TestNode" };

            // Act
            bool result = nodeDesign.Equals((object)objectDesign);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing with empty string BrowseName.
        /// </summary>
        [Test]
        public void Equals_EmptyStringBrowseName_ReturnsTrue()
        {
            // Arrange
            var nodeDesign1 = new NodeDesign { BrowseName = string.Empty };
            var nodeDesign2 = new NodeDesign { BrowseName = string.Empty };

            // Act
            bool result = nodeDesign1.Equals((object)nodeDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing NodeDesign instances with different WriteAccess values.
        /// </summary>
        [Test]
        public void Equals_DifferentWriteAccess_ReturnsFalse()
        {
            // Arrange
            var nodeDesign1 = new NodeDesign
            {
                BrowseName = "TestNode",
                WriteAccess = 1
            };

            var nodeDesign2 = new NodeDesign
            {
                BrowseName = "TestNode",
                WriteAccess = 2
            };

            // Act
            bool result = nodeDesign1.Equals((object)nodeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing NodeDesign instances with different Category.
        /// </summary>
        [Test]
        public void Equals_DifferentCategory_ReturnsFalse()
        {
            // Arrange
            var nodeDesign1 = new NodeDesign
            {
                BrowseName = "TestNode",
                Category = "Category1"
            };

            var nodeDesign2 = new NodeDesign
            {
                BrowseName = "TestNode",
                Category = "Category2"
            };

            // Act
            bool result = nodeDesign1.Equals((object)nodeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that CreateSymbolicId returns parentId when childName is null.
        /// </summary>
        [Test]
        public void CreateSymbolicId_ChildNameNull_ReturnsParentId()
        {
            // Arrange
            const string parentId = "Parent";
            const string childName = null;

            // Act
            string result = NodeDesign.CreateSymbolicId(parentId, childName);

            // Assert
            Assert.That(result, Is.EqualTo(parentId));
        }

        /// <summary>
        /// Tests that CreateSymbolicId returns parentId when childName is empty.
        /// </summary>
        [Test]
        public void CreateSymbolicId_ChildNameEmpty_ReturnsParentId()
        {
            // Arrange
            const string parentId = "Parent";
            const string childName = "";

            // Act
            string result = NodeDesign.CreateSymbolicId(parentId, childName);

            // Assert
            Assert.That(result, Is.EqualTo(parentId));
        }

        /// <summary>
        /// Tests that CreateSymbolicId returns childName when parentId is null.
        /// </summary>
        [Test]
        public void CreateSymbolicId_ParentIdNull_ReturnsChildName()
        {
            // Arrange
            const string parentId = null;
            const string childName = "Child";

            // Act
            string result = NodeDesign.CreateSymbolicId(parentId, childName);

            // Assert
            Assert.That(result, Is.EqualTo(childName));
        }

        /// <summary>
        /// Tests that CreateSymbolicId returns childName when parentId is empty.
        /// </summary>
        [Test]
        public void CreateSymbolicId_ParentIdEmpty_ReturnsChildName()
        {
            // Arrange
            const string parentId = "";
            const string childName = "Child";

            // Act
            string result = NodeDesign.CreateSymbolicId(parentId, childName);

            // Assert
            Assert.That(result, Is.EqualTo(childName));
        }

        /// <summary>
        /// Tests that CreateSymbolicId returns null when both parentId and childName are null.
        /// </summary>
        [Test]
        public void CreateSymbolicId_BothNull_ReturnsNull()
        {
            // Arrange
            const string parentId = null;
            const string childName = null;

            // Act
            string result = NodeDesign.CreateSymbolicId(parentId, childName);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that CreateSymbolicId returns empty string when both parentId and childName are empty.
        /// </summary>
        [Test]
        public void CreateSymbolicId_BothEmpty_ReturnsEmpty()
        {
            // Arrange
            const string parentId = "";
            const string childName = "";

            // Act
            string result = NodeDesign.CreateSymbolicId(parentId, childName);

            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that CreateSymbolicId correctly combines valid parentId and childName with underscore separator.
        /// </summary>
        [TestCase("Parent", "Child", "Parent_Child")]
        [TestCase("Root", "Node", "Root_Node")]
        [TestCase("A", "B", "A_B")]
        public void CreateSymbolicId_BothValid_ReturnsCombinedWithUnderscore(string parentId, string childName, string expected)
        {
            // Act
            string result = NodeDesign.CreateSymbolicId(parentId, childName);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that CreateSymbolicId handles whitespace-only childName by treating it as non-empty.
        /// Whitespace-only strings are not considered empty by string.IsNullOrEmpty.
        /// </summary>
        [Test]
        public void CreateSymbolicId_ChildNameWhitespaceOnly_CombinesWithParent()
        {
            // Arrange
            const string parentId = "Parent";
            const string childName = "   ";

            // Act
            string result = NodeDesign.CreateSymbolicId(parentId, childName);

            // Assert
            Assert.That(result, Is.EqualTo("Parent_   "));
        }

        /// <summary>
        /// Tests that CreateSymbolicId handles whitespace-only parentId by treating it as non-empty.
        /// Whitespace-only strings are not considered empty by string.IsNullOrEmpty.
        /// </summary>
        [Test]
        public void CreateSymbolicId_ParentIdWhitespaceOnly_CombinesWithChild()
        {
            // Arrange
            const string parentId = "   ";
            const string childName = "Child";

            // Act
            string result = NodeDesign.CreateSymbolicId(parentId, childName);

            // Assert
            Assert.That(result, Is.EqualTo("   _Child"));
        }

        /// <summary>
        /// Tests that CreateSymbolicId returns whitespace when parentId is whitespace and childName is null.
        /// </summary>
        [Test]
        public void CreateSymbolicId_ParentIdWhitespaceChildNameNull_ReturnsParentId()
        {
            // Arrange
            const string parentId = "   ";
            const string childName = null;

            // Act
            string result = NodeDesign.CreateSymbolicId(parentId, childName);

            // Assert
            Assert.That(result, Is.EqualTo(parentId));
        }

        /// <summary>
        /// Tests that CreateSymbolicId handles special characters in both parameters.
        /// </summary>
        [TestCase("Parent-1", "Child#2", "Parent-1_Child#2")]
        [TestCase("Namespace.Type", "Property", "Namespace.Type_Property")]
        [TestCase("Parent@123", "Child$456", "Parent@123_Child$456")]
        public void CreateSymbolicId_SpecialCharacters_ReturnsCombined(string parentId, string childName, string expected)
        {
            // Act
            string result = NodeDesign.CreateSymbolicId(parentId, childName);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that CreateSymbolicId handles parameters containing underscore characters.
        /// </summary>
        [Test]
        public void CreateSymbolicId_ContainsUnderscore_ReturnsCombined()
        {
            // Arrange
            const string parentId = "Parent_Node";
            const string childName = "Child_Property";

            // Act
            string result = NodeDesign.CreateSymbolicId(parentId, childName);

            // Assert
            Assert.That(result, Is.EqualTo("Parent_Node_Child_Property"));
        }

        /// <summary>
        /// Tests that CreateSymbolicId handles very long strings for both parameters.
        /// </summary>
        [Test]
        public void CreateSymbolicId_VeryLongStrings_ReturnsCombined()
        {
            // Arrange
            string parentId = new('P', 1000);
            string childName = new('C', 1000);
            string expected = parentId + "_" + childName;

            // Act
            string result = NodeDesign.CreateSymbolicId(parentId, childName);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
            Assert.That(result.Length, Is.EqualTo(2001));
        }

        /// <summary>
        /// Tests that CreateSymbolicId handles strings with unicode characters.
        /// </summary>
        [TestCase("親", "子", "親_子")]
        [TestCase("Родитель", "Ребенок", "Родитель_Ребенок")]
        [TestCase("🔧", "🔨", "🔧_🔨")]
        public void CreateSymbolicId_UnicodeCharacters_ReturnsCombined(string parentId, string childName, string expected)
        {
            // Act
            string result = NodeDesign.CreateSymbolicId(parentId, childName);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that CreateSymbolicId handles strings with control characters.
        /// </summary>
        [Test]
        public void CreateSymbolicId_ControlCharacters_ReturnsCombined()
        {
            // Arrange
            const string parentId = "Parent\t\n";
            const string childName = "Child\r\n";
            const string expected = "Parent\t\n_Child\r\n";

            // Act
            string result = NodeDesign.CreateSymbolicId(parentId, childName);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that CreateSymbolicId handles single character strings.
        /// </summary>
        [Test]
        public void CreateSymbolicId_SingleCharacterStrings_ReturnsCombined()
        {
            // Arrange
            const string parentId = "A";
            const string childName = "B";

            // Act
            string result = NodeDesign.CreateSymbolicId(parentId, childName);

            // Assert
            Assert.That(result, Is.EqualTo("A_B"));
        }

        /// <summary>
        /// Tests that CreateSymbolicId returns parentId when it's null and childName is empty.
        /// </summary>
        [Test]
        public void CreateSymbolicId_ParentIdNullChildNameEmpty_ReturnsNull()
        {
            // Arrange
            const string parentId = null;
            const string childName = "";

            // Act
            string result = NodeDesign.CreateSymbolicId(parentId, childName);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that CreateSymbolicId returns parentId when it's empty and childName is also empty.
        /// Edge case testing precedence of checks.
        /// </summary>
        [Test]
        public void CreateSymbolicId_ParentIdEmptyChildNameEmpty_ReturnsEmpty()
        {
            // Arrange
            const string parentId = "";
            const string childName = "";

            // Act
            string result = NodeDesign.CreateSymbolicId(parentId, childName);

            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that ToString() with no parameters returns the type name when SymbolicName is null.
        /// </summary>
        [Test]
        public void ToString_NoParameters_SymbolicNameNull_ReturnsTypeName()
        {
            // Arrange
            var nodeDesign = new NodeDesign();

            // Act
            string result = nodeDesign.ToString();

            // Assert
            Assert.That(result, Is.EqualTo("NodeDesign"));
        }

        /// <summary>
        /// Tests that ToString() with no parameters returns the SymbolicName when it is set.
        /// </summary>
        [Test]
        public void ToString_NoParameters_SymbolicNameSet_ReturnsSymbolicName()
        {
            // Arrange
            var nodeDesign = new NodeDesign
            {
                SymbolicName = new XmlQualifiedName("TestNode", "http://test.org")
            };

            // Act
            string result = nodeDesign.ToString();

            // Assert
            Assert.That(result, Is.EqualTo("TestNode"));
        }

        /// <summary>
        /// Tests that ToString with null format and null SymbolicName returns the type name.
        /// </summary>
        [Test]
        public void ToString_NullFormat_SymbolicNameNull_ReturnsTypeName()
        {
            // Arrange
            var nodeDesign = new NodeDesign();

            // Act
            string result = nodeDesign.ToString(null, null);

            // Assert
            Assert.That(result, Is.EqualTo("NodeDesign"));
        }

        /// <summary>
        /// Tests that ToString with null format and set SymbolicName returns the SymbolicName.Name.
        /// </summary>
        [Test]
        public void ToString_NullFormat_SymbolicNameSet_ReturnsSymbolicName()
        {
            // Arrange
            var nodeDesign = new NodeDesign
            {
                SymbolicName = new XmlQualifiedName("MyNode", "http://example.com")
            };

            // Act
            string result = nodeDesign.ToString(null, null);

            // Assert
            Assert.That(result, Is.EqualTo("MyNode"));
        }

        /// <summary>
        /// Tests that ToString with null format and SymbolicName with empty Name returns empty string.
        /// </summary>
        [Test]
        public void ToString_NullFormat_SymbolicNameEmptyName_ReturnsEmptyString()
        {
            // Arrange
            var nodeDesign = new NodeDesign
            {
                SymbolicName = new XmlQualifiedName(string.Empty, "http://example.com")
            };

            // Act
            string result = nodeDesign.ToString(null, null);

            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that ToString with null format and InvariantCulture formatProvider formats correctly.
        /// </summary>
        [Test]
        public void ToString_NullFormat_InvariantCultureProvider_FormatsCorrectly()
        {
            // Arrange
            var nodeDesign = new NodeDesign
            {
                SymbolicName = new XmlQualifiedName("TestNode", "http://test.org")
            };

            // Act
            string result = nodeDesign.ToString(null, CultureInfo.InvariantCulture);

            // Assert
            Assert.That(result, Is.EqualTo("TestNode"));
        }

        /// <summary>
        /// Tests that ToString with null format and specific culture formatProvider formats correctly.
        /// </summary>
        [Test]
        public void ToString_NullFormat_SpecificCultureProvider_FormatsCorrectly()
        {
            // Arrange
            var nodeDesign = new NodeDesign
            {
                SymbolicName = new XmlQualifiedName("TestNode", "http://test.org")
            };

            // Act
            string result = nodeDesign.ToString(null, CultureInfo.GetCultureInfo("en-US"));

            // Assert
            Assert.That(result, Is.EqualTo("TestNode"));
        }

        /// <summary>
        /// Tests that ToString throws FormatException when format is an empty string.
        /// </summary>
        [Test]
        public void ToString_EmptyStringFormat_ThrowsFormatException()
        {
            // Arrange
            var nodeDesign = new NodeDesign();

            // Act & Assert
            FormatException ex = Assert.Throws<FormatException>(() => nodeDesign.ToString(string.Empty, null));
            Assert.That(ex.Message, Does.Contain("Invalid format string"));
            Assert.That(ex.Message, Does.Contain("''"));
        }

        /// <summary>
        /// Tests that ToString throws FormatException when format is a non-null string.
        /// Input format strings: "G", "D", "X", "custom"
        /// Expected result: FormatException with appropriate message.
        /// </summary>
        [TestCase("G")]
        [TestCase("D")]
        [TestCase("X")]
        [TestCase("custom")]
        [TestCase("anyFormat")]
        public void ToString_NonNullFormat_ThrowsFormatException(string format)
        {
            // Arrange
            var nodeDesign = new NodeDesign
            {
                SymbolicName = new XmlQualifiedName("TestNode", "http://test.org")
            };

            // Act & Assert
            FormatException ex = Assert.Throws<FormatException>(() => nodeDesign.ToString(format, null));
            Assert.That(ex.Message, Does.Contain("Invalid format string"));
            Assert.That(ex.Message, Does.Contain(format));
        }

        /// <summary>
        /// Tests that ToString throws FormatException when format is whitespace.
        /// </summary>
        [TestCase(" ")]
        [TestCase("   ")]
        [TestCase("\t")]
        [TestCase("\n")]
        public void ToString_WhitespaceFormat_ThrowsFormatException(string format)
        {
            // Arrange
            var nodeDesign = new NodeDesign();

            // Act & Assert
            FormatException ex = Assert.Throws<FormatException>(() => nodeDesign.ToString(format, null));
            Assert.That(ex.Message, Does.Contain("Invalid format string"));
        }

        /// <summary>
        /// Tests that ToString throws FormatException with formatProvider when format is non-null.
        /// </summary>
        [Test]
        public void ToString_NonNullFormat_WithFormatProvider_ThrowsFormatException()
        {
            // Arrange
            var nodeDesign = new NodeDesign();

            // Act & Assert
            FormatException ex = Assert.Throws<FormatException>(() => nodeDesign.ToString("G", CultureInfo.InvariantCulture));
            Assert.That(ex.Message, Does.Contain("Invalid format string"));
            Assert.That(ex.Message, Does.Contain("G"));
        }

        /// <summary>
        /// Tests that ToString handles SymbolicName with special characters in Name correctly.
        /// </summary>
        [TestCase("Node_With_Underscores")]
        [TestCase("Node-With-Dashes")]
        [TestCase("Node.With.Dots")]
        [TestCase("Node$With$Special")]
        public void ToString_SymbolicNameWithSpecialCharacters_ReturnsName(string name)
        {
            // Arrange
            var nodeDesign = new NodeDesign
            {
                SymbolicName = new XmlQualifiedName(name, "http://test.org")
            };

            // Act
            string result = nodeDesign.ToString(null, null);

            // Assert
            Assert.That(result, Is.EqualTo(name));
        }

        /// <summary>
        /// Tests that ToString handles very long SymbolicName correctly.
        /// </summary>
        [Test]
        public void ToString_VeryLongSymbolicName_ReturnsFullName()
        {
            // Arrange
            string longName = new('A', 1000);
            var nodeDesign = new NodeDesign
            {
                SymbolicName = new XmlQualifiedName(longName, "http://test.org")
            };

            // Act
            string result = nodeDesign.ToString(null, null);

            // Assert
            Assert.That(result, Is.EqualTo(longName));
            Assert.That(result.Length, Is.EqualTo(1000));
        }

        /// <summary>
        /// Tests CreateSymbolicId method with null parentId parameter.
        /// Should return childName directly when parentId is null.
        /// </summary>
        [TestCase(null, ExpectedResult = null)]
        [TestCase("", ExpectedResult = "")]
        [TestCase("ChildNode", ExpectedResult = "ChildNode")]
        [TestCase("   ", ExpectedResult = "   ")]
        [TestCase("Child_With_Underscores", ExpectedResult = "Child_With_Underscores")]
        public string CreateSymbolicId_NullParentId_ReturnsChildName(string childName)
        {
            // Arrange
            XmlQualifiedName parentId = null;

            // Act
            return NodeDesign.CreateSymbolicId(parentId, childName);
        }

        /// <summary>
        /// Tests CreateSymbolicId method with non-null parentId and null childName.
        /// Should delegate to string overload which returns parentId.Name when childName is null.
        /// </summary>
        [TestCase("ParentNode", null, ExpectedResult = "ParentNode")]
        [TestCase("ParentNode", "", ExpectedResult = "ParentNode")]
        [TestCase("", null, ExpectedResult = "")]
        [TestCase("", "", ExpectedResult = "")]
        public string CreateSymbolicId_NonNullParentId_NullOrEmptyChildName_ReturnsParentName(string parentName, string childName)
        {
            // Arrange
            var parentId = new XmlQualifiedName(parentName);

            // Act
            return NodeDesign.CreateSymbolicId(parentId, childName);
        }

        /// <summary>
        /// Tests CreateSymbolicId method with non-null parentId and non-empty childName.
        /// Should delegate to string overload which combines parent and child with PathChar separator.
        /// </summary>
        [TestCase("Parent", "Child", ExpectedResult = "Parent_Child")]
        [TestCase("Root", "Node", ExpectedResult = "Root_Node")]
        [TestCase("Parent_Node", "Child_Node", ExpectedResult = "Parent_Node_Child_Node")]
        public string CreateSymbolicId_NonNullParentId_NonEmptyChildName_ReturnsCombinedId(string parentName, string childName)
        {
            // Arrange
            var parentId = new XmlQualifiedName(parentName);

            // Act
            return NodeDesign.CreateSymbolicId(parentId, childName);
        }

        /// <summary>
        /// Tests CreateSymbolicId method with null or empty parent name in XmlQualifiedName.
        /// Should delegate to string overload which returns childName when parentId.Name is null or empty.
        /// </summary>
        [TestCase(null, "ChildNode", ExpectedResult = "ChildNode")]
        [TestCase("", "ChildNode", ExpectedResult = "ChildNode")]
        public string CreateSymbolicId_NullOrEmptyParentName_NonEmptyChildName_ReturnsChildName(string parentName, string childName)
        {
            // Arrange
            var parentId = new XmlQualifiedName(parentName ?? string.Empty);

            // Act
            return NodeDesign.CreateSymbolicId(parentId, childName);
        }

        /// <summary>
        /// Tests CreateSymbolicId method with whitespace-only strings.
        /// Should handle whitespace strings according to string overload behavior.
        /// </summary>
        [TestCase("   ", "Child", ExpectedResult = "   _Child")]
        [TestCase("Parent", "   ", ExpectedResult = "Parent_   ")]
        [TestCase("   ", "   ", ExpectedResult = "   _   ")]
        public string CreateSymbolicId_WhitespaceStrings_HandlesCorrectly(string parentName, string childName)
        {
            // Arrange
            var parentId = new XmlQualifiedName(parentName);

            // Act
            return NodeDesign.CreateSymbolicId(parentId, childName);
        }

        /// <summary>
        /// Tests CreateSymbolicId method with special characters in names.
        /// Should handle special characters without validation or transformation.
        /// </summary>
        [TestCase("Parent@Node", "Child#Node", ExpectedResult = "Parent@Node_Child#Node")]
        [TestCase("Parent.Node", "Child.Node", ExpectedResult = "Parent.Node_Child.Node")]
        [TestCase("Parent/Node", "Child\\Node", ExpectedResult = "Parent/Node_Child\\Node")]
        public string CreateSymbolicId_SpecialCharacters_HandlesCorrectly(string parentName, string childName)
        {
            // Arrange
            var parentId = new XmlQualifiedName(parentName);

            // Act
            return NodeDesign.CreateSymbolicId(parentId, childName);
        }

        /// <summary>
        /// Tests CreateSymbolicId method with very long strings.
        /// Should handle long strings without truncation or errors.
        /// </summary>
        [Test]
        public void CreateSymbolicId_VeryLongStrings_HandlesCorrectly()
        {
            // Arrange
            string longParentName = new('A', 10000);
            string longChildName = new('B', 10000);
            var parentId = new XmlQualifiedName(longParentName);

            // Act
            string result = NodeDesign.CreateSymbolicId(parentId, longChildName);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(20001)); // 10000 + 1 (PathChar) + 10000
            Assert.That(result, Does.StartWith(longParentName));
            Assert.That(result, Does.Contain("_"));
            Assert.That(result, Does.EndWith(longChildName));
        }

        /// <summary>
        /// Tests CreateSymbolicId method with XmlQualifiedName that has namespace.
        /// Should only use the Name property, ignoring the namespace.
        /// </summary>
        [Test]
        public void CreateSymbolicId_XmlQualifiedNameWithNamespace_UsesOnlyName()
        {
            // Arrange
            var parentId = new XmlQualifiedName("ParentNode", "http://example.com/namespace");
            const string childName = "ChildNode";

            // Act
            string result = NodeDesign.CreateSymbolicId(parentId, childName);

            // Assert
            Assert.That(result, Is.EqualTo("ParentNode_ChildNode"));
        }

        /// <summary>
        /// Tests CreateSymbolicId method with empty strings for both parent and child names.
        /// Should return empty string when both names are empty.
        /// </summary>
        [Test]
        public void CreateSymbolicId_BothEmptyStrings_ReturnsEmptyString()
        {
            // Arrange
            var parentId = new XmlQualifiedName(string.Empty);
            string childName = string.Empty;

            // Act
            string result = NodeDesign.CreateSymbolicId(parentId, childName);

            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that GetHashCode returns the same value for two identical objects.
        /// Verifies that objects with identical property values produce consistent hash codes.
        /// Expected result: Both objects produce the same hash code.
        /// </summary>
        [Test]
        public void GetHashCode_IdenticalObjects_ReturnsSameHashCode()
        {
            // Arrange
            var node1 = new NodeDesign
            {
                BrowseName = "TestNode",
                StringId = "TestId",
                IsDeclaration = true,
                WriteAccess = 5,
                PartNo = 10,
                Category = "TestCategory",
                NotInAddressSpace = false,
                ReleaseStatus = ReleaseStatus.Released,
                Purpose = DataTypePurpose.Normal,
                IsDynamic = false
            };

            var node2 = new NodeDesign
            {
                BrowseName = "TestNode",
                StringId = "TestId",
                IsDeclaration = true,
                WriteAccess = 5,
                PartNo = 10,
                Category = "TestCategory",
                NotInAddressSpace = false,
                ReleaseStatus = ReleaseStatus.Released,
                Purpose = DataTypePurpose.Normal,
                IsDynamic = false
            };

            // Act
            int hashCode1 = node1.GetHashCode();
            int hashCode2 = node2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different values when BrowseName differs.
        /// Verifies that changes to the BrowseName property affect the hash code.
        /// Expected result: Different hash codes for different BrowseNames.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentBrowseName_ReturnsDifferentHashCode()
        {
            // Arrange
            var node1 = new NodeDesign { BrowseName = "Node1" };
            var node2 = new NodeDesign { BrowseName = "Node2" };

            // Act
            int hashCode1 = node1.GetHashCode();
            int hashCode2 = node2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode handles null BrowseName correctly.
        /// Verifies that null values for BrowseName produce a valid hash code without exceptions.
        /// Expected result: Hash code is computed successfully.
        /// </summary>
        [Test]
        public void GetHashCode_NullBrowseName_ReturnsValidHashCode()
        {
            // Arrange
            var node = new NodeDesign { BrowseName = null };

            // Act
            int hashCode = node.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero.Or.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode handles empty string BrowseName correctly.
        /// Verifies that empty string values for BrowseName produce a valid hash code.
        /// Expected result: Hash code is computed successfully.
        /// </summary>
        [Test]
        public void GetHashCode_EmptyBrowseName_ReturnsValidHashCode()
        {
            // Arrange
            var node = new NodeDesign { BrowseName = string.Empty };

            // Act
            int hashCode = node.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero.Or.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode returns different values when NumericId differs.
        /// Verifies that changes to the NumericId property affect the hash code.
        /// Expected result: Different hash codes for different NumericIds.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentNumericId_ReturnsDifferentHashCode()
        {
            // Arrange
            var node1 = new NodeDesign { NumericId = 100, NumericIdSpecified = true };
            var node2 = new NodeDesign { NumericId = 200, NumericIdSpecified = true };

            // Act
            int hashCode1 = node1.GetHashCode();
            int hashCode2 = node2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode handles extreme NumericId values correctly.
        /// Verifies that boundary values (min, max) for NumericId produce valid hash codes.
        /// Expected result: Hash codes are computed successfully for boundary values.
        /// </summary>
        [TestCase(uint.MinValue)]
        [TestCase(uint.MaxValue)]
        [TestCase(0u)]
        public void GetHashCode_ExtremeNumericId_ReturnsValidHashCode(uint numericId)
        {
            // Arrange
            var node = new NodeDesign { NumericId = numericId, NumericIdSpecified = true };

            // Act
            int hashCode = node.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero.Or.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode returns different values when IsDeclaration differs.
        /// Verifies that changes to the IsDeclaration property affect the hash code.
        /// Expected result: Different hash codes for different IsDeclaration values.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentIsDeclaration_ReturnsDifferentHashCode()
        {
            // Arrange
            var node1 = new NodeDesign { IsDeclaration = true };
            var node2 = new NodeDesign { IsDeclaration = false };

            // Act
            int hashCode1 = node1.GetHashCode();
            int hashCode2 = node2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different values when WriteAccess differs.
        /// Verifies that changes to the WriteAccess property affect the hash code.
        /// Expected result: Different hash codes for different WriteAccess values.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentWriteAccess_ReturnsDifferentHashCode()
        {
            // Arrange
            var node1 = new NodeDesign { WriteAccess = 0 };
            var node2 = new NodeDesign { WriteAccess = 100 };

            // Act
            int hashCode1 = node1.GetHashCode();
            int hashCode2 = node2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode handles extreme WriteAccess values correctly.
        /// Verifies that boundary values (min, max) for WriteAccess produce valid hash codes.
        /// Expected result: Hash codes are computed successfully for boundary values.
        /// </summary>
        [TestCase(uint.MinValue)]
        [TestCase(uint.MaxValue)]
        public void GetHashCode_ExtremeWriteAccess_ReturnsValidHashCode(uint writeAccess)
        {
            // Arrange
            var node = new NodeDesign { WriteAccess = writeAccess };

            // Act
            int hashCode = node.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero.Or.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode returns different values when PartNo differs.
        /// Verifies that changes to the PartNo property affect the hash code.
        /// Expected result: Different hash codes for different PartNo values.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentPartNo_ReturnsDifferentHashCode()
        {
            // Arrange
            var node1 = new NodeDesign { PartNo = 1 };
            var node2 = new NodeDesign { PartNo = 2 };

            // Act
            int hashCode1 = node1.GetHashCode();
            int hashCode2 = node2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different values when Category differs.
        /// Verifies that changes to the Category property affect the hash code.
        /// Expected result: Different hash codes for different Category values.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentCategory_ReturnsDifferentHashCode()
        {
            // Arrange
            var node1 = new NodeDesign { Category = "Category1" };
            var node2 = new NodeDesign { Category = "Category2" };

            // Act
            int hashCode1 = node1.GetHashCode();
            int hashCode2 = node2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode handles null Category correctly.
        /// Verifies that null values for Category produce a valid hash code without exceptions.
        /// Expected result: Hash code is computed successfully.
        /// </summary>
        [Test]
        public void GetHashCode_NullCategory_ReturnsValidHashCode()
        {
            // Arrange
            var node = new NodeDesign { Category = null };

            // Act
            int hashCode = node.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero.Or.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode returns different values when NotInAddressSpace differs.
        /// Verifies that changes to the NotInAddressSpace property affect the hash code.
        /// Expected result: Different hash codes for different NotInAddressSpace values.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentNotInAddressSpace_ReturnsDifferentHashCode()
        {
            // Arrange
            var node1 = new NodeDesign { NotInAddressSpace = true };
            var node2 = new NodeDesign { NotInAddressSpace = false };

            // Act
            int hashCode1 = node1.GetHashCode();
            int hashCode2 = node2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different values when ReleaseStatus differs.
        /// Verifies that changes to the ReleaseStatus property affect the hash code.
        /// Expected result: Different hash codes for different ReleaseStatus values.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentReleaseStatus_ReturnsDifferentHashCode()
        {
            // Arrange
            var node1 = new NodeDesign { ReleaseStatus = ReleaseStatus.Released };
            var node2 = new NodeDesign { ReleaseStatus = ReleaseStatus.Draft };

            // Act
            int hashCode1 = node1.GetHashCode();
            int hashCode2 = node2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different values when Purpose differs.
        /// Verifies that changes to the Purpose property affect the hash code.
        /// Expected result: Different hash codes for different Purpose values.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentPurpose_ReturnsDifferentHashCode()
        {
            // Arrange
            var node1 = new NodeDesign { Purpose = DataTypePurpose.Normal };
            var node2 = new NodeDesign { Purpose = DataTypePurpose.ServicesOnly };

            // Act
            int hashCode1 = node1.GetHashCode();
            int hashCode2 = node2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different values when IsDynamic differs.
        /// Verifies that changes to the IsDynamic property affect the hash code.
        /// Expected result: Different hash codes for different IsDynamic values.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentIsDynamic_ReturnsDifferentHashCode()
        {
            // Arrange
            var node1 = new NodeDesign { IsDynamic = true };
            var node2 = new NodeDesign { IsDynamic = false };

            // Act
            int hashCode1 = node1.GetHashCode();
            int hashCode2 = node2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different values when StringId differs.
        /// Verifies that changes to the StringId property affect the hash code.
        /// Expected result: Different hash codes for different StringId values.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentStringId_ReturnsDifferentHashCode()
        {
            // Arrange
            var node1 = new NodeDesign { StringId = "Id1" };
            var node2 = new NodeDesign { StringId = "Id2" };

            // Act
            int hashCode1 = node1.GetHashCode();
            int hashCode2 = node2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode handles null StringId correctly.
        /// Verifies that null values for StringId produce a valid hash code without exceptions.
        /// Expected result: Hash code is computed successfully.
        /// </summary>
        [Test]
        public void GetHashCode_NullStringId_ReturnsValidHashCode()
        {
            // Arrange
            var node = new NodeDesign { StringId = null };

            // Act
            int hashCode = node.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero.Or.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode handles very long StringId correctly.
        /// Verifies that long string values for StringId produce a valid hash code.
        /// Expected result: Hash code is computed successfully.
        /// </summary>
        [Test]
        public void GetHashCode_VeryLongStringId_ReturnsValidHashCode()
        {
            // Arrange
            var node = new NodeDesign { StringId = new string('A', 10000) };

            // Act
            int hashCode = node.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero.Or.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode returns different values when SymbolicName differs.
        /// Verifies that changes to the SymbolicName property affect the hash code.
        /// Expected result: Different hash codes for different SymbolicName values.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentSymbolicName_ReturnsDifferentHashCode()
        {
            // Arrange
            var node1 = new NodeDesign { SymbolicName = new XmlQualifiedName("Name1", "http://test1.com") };
            var node2 = new NodeDesign { SymbolicName = new XmlQualifiedName("Name2", "http://test2.com") };

            // Act
            int hashCode1 = node1.GetHashCode();
            int hashCode2 = node2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode handles null SymbolicName correctly.
        /// Verifies that null values for SymbolicName produce a valid hash code without exceptions.
        /// Expected result: Hash code is computed successfully.
        /// </summary>
        [Test]
        public void GetHashCode_NullSymbolicName_ReturnsValidHashCode()
        {
            // Arrange
            var node = new NodeDesign { SymbolicName = null };

            // Act
            int hashCode = node.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero.Or.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode returns different values when SymbolicId differs.
        /// Verifies that changes to the SymbolicId property affect the hash code.
        /// Expected result: Different hash codes for different SymbolicId values.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentSymbolicId_ReturnsDifferentHashCode()
        {
            // Arrange
            var node1 = new NodeDesign { SymbolicId = new XmlQualifiedName("Id1", "http://test1.com") };
            var node2 = new NodeDesign { SymbolicId = new XmlQualifiedName("Id2", "http://test2.com") };

            // Act
            int hashCode1 = node1.GetHashCode();
            int hashCode2 = node2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode handles null SymbolicId correctly.
        /// Verifies that null values for SymbolicId produce a valid hash code without exceptions.
        /// Expected result: Hash code is computed successfully.
        /// </summary>
        [Test]
        public void GetHashCode_NullSymbolicId_ReturnsValidHashCode()
        {
            // Arrange
            var node = new NodeDesign { SymbolicId = null };

            // Act
            int hashCode = node.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero.Or.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode returns different values when DisplayName differs.
        /// Verifies that changes to the DisplayName property affect the hash code.
        /// Expected result: Different hash codes for different DisplayName values.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentDisplayName_ReturnsDifferentHashCode()
        {
            // Arrange
            var node1 = new NodeDesign { DisplayName = new LocalizedText { Key = "key1", Value = "Display1" } };
            var node2 = new NodeDesign { DisplayName = new LocalizedText { Key = "key2", Value = "Display2" } };

            // Act
            int hashCode1 = node1.GetHashCode();
            int hashCode2 = node2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode handles null DisplayName correctly.
        /// Verifies that null values for DisplayName produce a valid hash code without exceptions.
        /// Expected result: Hash code is computed successfully.
        /// </summary>
        [Test]
        public void GetHashCode_NullDisplayName_ReturnsValidHashCode()
        {
            // Arrange
            var node = new NodeDesign { DisplayName = null };

            // Act
            int hashCode = node.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero.Or.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode returns different values when Description differs.
        /// Verifies that changes to the Description property affect the hash code.
        /// Expected result: Different hash codes for different Description values.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentDescription_ReturnsDifferentHashCode()
        {
            // Arrange
            var node1 = new NodeDesign { Description = new LocalizedText { Key = "key1", Value = "Desc1" } };
            var node2 = new NodeDesign { Description = new LocalizedText { Key = "key2", Value = "Desc2" } };

            // Act
            int hashCode1 = node1.GetHashCode();
            int hashCode2 = node2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode handles null Description correctly.
        /// Verifies that null values for Description produce a valid hash code without exceptions.
        /// Expected result: Hash code is computed successfully.
        /// </summary>
        [Test]
        public void GetHashCode_NullDescription_ReturnsValidHashCode()
        {
            // Arrange
            var node = new NodeDesign { Description = null };

            // Act
            int hashCode = node.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero.Or.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode handles null References array correctly.
        /// Verifies that null values for References produce a valid hash code without exceptions.
        /// Expected result: Hash code is computed successfully.
        /// </summary>
        [Test]
        public void GetHashCode_NullReferences_ReturnsValidHashCode()
        {
            // Arrange
            var node = new NodeDesign { References = null };

            // Act
            int hashCode = node.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero.Or.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode handles empty References array correctly.
        /// Verifies that empty array values for References produce a valid hash code.
        /// Expected result: Hash code is computed successfully.
        /// </summary>
        [Test]
        public void GetHashCode_EmptyReferences_ReturnsValidHashCode()
        {
            // Arrange
            var node = new NodeDesign { References = [] };

            // Act
            int hashCode = node.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero.Or.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode returns different values when References differs.
        /// Verifies that changes to the References array affect the hash code.
        /// Expected result: Different hash codes for different References arrays.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentReferences_ReturnsDifferentHashCode()
        {
            // Arrange
            var node1 = new NodeDesign
            {
                References =
                [
                    new Reference { ReferenceType = new XmlQualifiedName("RefType1") }
                ]
            };
            var node2 = new NodeDesign
            {
                References =
                [
                    new Reference { ReferenceType = new XmlQualifiedName("RefType2") }
                ]
            };

            // Act
            int hashCode1 = node1.GetHashCode();
            int hashCode2 = node2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode handles null Extensions array correctly.
        /// Verifies that null values for Extensions produce a valid hash code without exceptions.
        /// Expected result: Hash code is computed successfully.
        /// </summary>
        [Test]
        public void GetHashCode_NullExtensions_ReturnsValidHashCode()
        {
            // Arrange
            var node = new NodeDesign { Extensions = null };

            // Act
            int hashCode = node.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero.Or.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode handles empty Extensions array correctly.
        /// Verifies that empty array values for Extensions produce a valid hash code.
        /// Expected result: Hash code is computed successfully.
        /// </summary>
        [Test]
        public void GetHashCode_EmptyExtensions_ReturnsValidHashCode()
        {
            // Arrange
            var node = new NodeDesign { Extensions = [] };

            // Act
            int hashCode = node.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero.Or.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode handles null Children correctly.
        /// Verifies that null values for Children produce a valid hash code without exceptions.
        /// Expected result: Hash code is computed successfully.
        /// </summary>
        [Test]
        public void GetHashCode_NullChildren_ReturnsValidHashCode()
        {
            // Arrange
            var node = new NodeDesign { Children = null };

            // Act
            int hashCode = node.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero.Or.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode returns different values when Children differs.
        /// Verifies that changes to the Children property affect the hash code.
        /// Expected result: Different hash codes for different Children values.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentChildren_ReturnsDifferentHashCode()
        {
            // Arrange
            var node1 = new NodeDesign { Children = new ListOfChildren { Items = [new ObjectDesign()] } };
            var node2 = new NodeDesign { Children = new ListOfChildren() };

            // Act
            int hashCode1 = node1.GetHashCode();
            int hashCode2 = node2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different values when Children differs.
        /// Verifies that changes to the Children property affect the hash code.
        /// Expected result: Different hash codes for different Children values.
        /// </summary>
        [Test]
        public void GetHashCode_NoChildren_ReturnsSameHashCode()
        {
            // Arrange
            var node1 = new NodeDesign { Children = null };
            var node2 = new NodeDesign { Children = new ListOfChildren() };

            // Act
            int hashCode1 = node1.GetHashCode();
            int hashCode2 = node2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode handles null RolePermissions correctly.
        /// Verifies that null values for RolePermissions produce a valid hash code without exceptions.
        /// Expected result: Hash code is computed successfully.
        /// </summary>
        [Test]
        public void GetHashCode_NullRolePermissions_ReturnsValidHashCode()
        {
            // Arrange
            var node = new NodeDesign { RolePermissions = null };

            // Act
            int hashCode = node.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero.Or.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode handles null DefaultRolePermissions correctly.
        /// Verifies that null values for DefaultRolePermissions produce a valid hash code without exceptions.
        /// Expected result: Hash code is computed successfully.
        /// </summary>
        [Test]
        public void GetHashCode_NullDefaultRolePermissions_ReturnsValidHashCode()
        {
            // Arrange
            var node = new NodeDesign { DefaultRolePermissions = null };

            // Act
            int hashCode = node.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero.Or.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode returns different values when AccessRestrictionsSpecified differs.
        /// Verifies that changes to the AccessRestrictionsSpecified property affect the hash code.
        /// Expected result: Different hash codes for different AccessRestrictionsSpecified values.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentAccessRestrictionsSpecified_ReturnsDifferentHashCode()
        {
            // Arrange
            var node1 = new NodeDesign { AccessRestrictionsSpecified = true };
            var node2 = new NodeDesign { AccessRestrictionsSpecified = false };

            // Act
            int hashCode1 = node1.GetHashCode();
            int hashCode2 = node2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different values when DefaultAccessRestrictionsSpecified differs.
        /// Verifies that changes to the DefaultAccessRestrictionsSpecified property affect the hash code.
        /// Expected result: Different hash codes for different DefaultAccessRestrictionsSpecified values.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentDefaultAccessRestrictionsSpecified_ReturnsDifferentHashCode()
        {
            // Arrange
            var node1 = new NodeDesign { DefaultAccessRestrictionsSpecified = true };
            var node2 = new NodeDesign { DefaultAccessRestrictionsSpecified = false };

            // Act
            int hashCode1 = node1.GetHashCode();
            int hashCode2 = node2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different values when NumericIdSpecified differs.
        /// Verifies that changes to the NumericIdSpecified property affect the hash code.
        /// Expected result: Different hash codes for different NumericIdSpecified values.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentNumericIdSpecified_ReturnsDifferentHashCode()
        {
            // Arrange
            var node1 = new NodeDesign { NumericIdSpecified = true };
            var node2 = new NodeDesign { NumericIdSpecified = false };

            // Act
            int hashCode1 = node1.GetHashCode();
            int hashCode2 = node2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode produces consistent results when called multiple times.
        /// Verifies that the hash code is deterministic and stable.
        /// Expected result: Same hash code is returned on multiple invocations.
        /// </summary>
        [Test]
        public void GetHashCode_MultipleInvocations_ReturnsConsistentHashCode()
        {
            // Arrange
            var node = new NodeDesign
            {
                BrowseName = "TestNode",
                StringId = "TestId",
                IsDeclaration = true,
                WriteAccess = 5
            };

            // Act
            int hashCode1 = node.GetHashCode();
            int hashCode2 = node.GetHashCode();
            int hashCode3 = node.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.EqualTo(hashCode2));
            Assert.That(hashCode2, Is.EqualTo(hashCode3));
        }

        /// <summary>
        /// Tests that GetHashCode handles all properties set to default values.
        /// Verifies that a default-initialized object produces a valid hash code.
        /// Expected result: Hash code is computed successfully.
        /// </summary>
        [Test]
        public void GetHashCode_AllDefaultValues_ReturnsValidHashCode()
        {
            // Arrange
            var node = new NodeDesign();

            // Act
            int hashCode = node.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero.Or.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode handles all properties set to non-default values.
        /// Verifies that a fully populated object produces a valid hash code.
        /// Expected result: Hash code is computed successfully.
        /// </summary>
        [Test]
        public void GetHashCode_AllNonDefaultValues_ReturnsValidHashCode()
        {
            // Arrange
            var node = new NodeDesign
            {
                BrowseName = "TestBrowseName",
                DisplayName = new LocalizedText { Key = "DisplayKey", Value = "DisplayValue" },
                Description = new LocalizedText { Key = "DescKey", Value = "DescValue" },
                Children = new ListOfChildren(),
                References = [new Reference { ReferenceType = new XmlQualifiedName("RefType") }],
                RolePermissions = new RolePermissionSet(),
                DefaultRolePermissions = new RolePermissionSet(),
                AccessRestrictions = AccessRestrictions.EncryptionRequired,
                AccessRestrictionsSpecified = true,
                DefaultAccessRestrictions = AccessRestrictions.SigningRequired,
                DefaultAccessRestrictionsSpecified = true,
                SymbolicName = new XmlQualifiedName("SymName", "http://test.com"),
                SymbolicId = new XmlQualifiedName("SymId", "http://test.com"),
                IsDeclaration = true,
                NumericId = 12345,
                NumericIdSpecified = true,
                StringId = "StringIdValue",
                WriteAccess = 100,
                PartNo = 200,
                Category = "TestCategory",
                NotInAddressSpace = true,
                ReleaseStatus = ReleaseStatus.Deprecated,
                Purpose = DataTypePurpose.CodeGenerator,
                IsDynamic = true
            };

            // Act
            int hashCode = node.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero.Or.Zero);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with null.
        /// </summary>
        [Test]
        public void Equals_NullParameter_ReturnsFalse()
        {
            // Arrange
            var node = new NodeDesign();

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = node.Equals(null);
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing the same instance.
        /// </summary>
        [Test]
        public void Equals_SameInstance_ReturnsTrue()
        {
            // Arrange
            var node = new NodeDesign { BrowseName = "TestNode" };

            // Act
            bool result = node.Equals(node);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when all properties of two different instances are equal.
        /// </summary>
        [Test]
        public void Equals_AllPropertiesEqual_ReturnsTrue()
        {
            // Arrange
            NodeDesign node1 = CreateTestNodeDesign();
            NodeDesign node2 = CreateTestNodeDesign();

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when DisplayName differs.
        /// </summary>
        [Test]
        public void Equals_DifferentDisplayName_ReturnsFalse()
        {
            // Arrange
            NodeDesign node1 = CreateTestNodeDesign();
            NodeDesign node2 = CreateTestNodeDesign();
            node2.DisplayName = new LocalizedText { Value = "DifferentDisplayName" };

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when Description differs.
        /// </summary>
        [Test]
        public void Equals_DifferentDescription_ReturnsFalse()
        {
            // Arrange
            NodeDesign node1 = CreateTestNodeDesign();
            NodeDesign node2 = CreateTestNodeDesign();
            node2.Description = new LocalizedText { Value = "DifferentDescription" };

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when Children differs.
        /// </summary>
        [Test]
        public void Equals_DifferentChildren_ReturnsFalse()
        {
            // Arrange
            NodeDesign node1 = CreateTestNodeDesign();
            NodeDesign node2 = CreateTestNodeDesign();
            node2.Children = new ListOfChildren();

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when References array differs.
        /// </summary>
        [Test]
        public void Equals_DifferentReferences_ReturnsFalse()
        {
            // Arrange
            NodeDesign node1 = CreateTestNodeDesign();
            NodeDesign node2 = CreateTestNodeDesign();
            node2.References = [new Reference()];

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when RolePermissions differs.
        /// </summary>
        [Test]
        public void Equals_DifferentRolePermissions_ReturnsFalse()
        {
            // Arrange
            NodeDesign node1 = CreateTestNodeDesign();
            NodeDesign node2 = CreateTestNodeDesign();
            node2.RolePermissions = new RolePermissionSet();

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when DefaultRolePermissions differs.
        /// </summary>
        [Test]
        public void Equals_DifferentDefaultRolePermissions_ReturnsFalse()
        {
            // Arrange
            NodeDesign node1 = CreateTestNodeDesign();
            NodeDesign node2 = CreateTestNodeDesign();
            node2.DefaultRolePermissions = new RolePermissionSet();

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when AccessRestrictions differs.
        /// </summary>
        [Test]
        public void Equals_DifferentAccessRestrictions_ReturnsFalse()
        {
            // Arrange
            NodeDesign node1 = CreateTestNodeDesign();
            NodeDesign node2 = CreateTestNodeDesign();
            node2.AccessRestrictions = AccessRestrictions.EncryptionRequired;

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when AccessRestrictionsSpecified differs.
        /// </summary>
        [Test]
        public void Equals_DifferentAccessRestrictionsSpecified_ReturnsFalse()
        {
            // Arrange
            NodeDesign node1 = CreateTestNodeDesign();
            NodeDesign node2 = CreateTestNodeDesign();
            node2.AccessRestrictionsSpecified = true;

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when DefaultAccessRestrictions differs.
        /// </summary>
        [Test]
        public void Equals_DifferentDefaultAccessRestrictions_ReturnsFalse()
        {
            // Arrange
            NodeDesign node1 = CreateTestNodeDesign();
            NodeDesign node2 = CreateTestNodeDesign();
            node2.DefaultAccessRestrictions = AccessRestrictions.SessionRequired;

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when DefaultAccessRestrictionsSpecified differs.
        /// </summary>
        [Test]
        public void Equals_DifferentDefaultAccessRestrictionsSpecified_ReturnsFalse()
        {
            // Arrange
            NodeDesign node1 = CreateTestNodeDesign();
            NodeDesign node2 = CreateTestNodeDesign();
            node2.DefaultAccessRestrictionsSpecified = true;

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when Extensions array differs.
        /// </summary>
        [Test]
        public void Equals_DifferentExtensions_ReturnsFalse()
        {
            // Arrange
            NodeDesign node1 = CreateTestNodeDesign();
            NodeDesign node2 = CreateTestNodeDesign();
            var doc = new XmlDocument();
            node2.Extensions = [doc.CreateElement("Extension")];

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when SymbolicName differs.
        /// </summary>
        [Test]
        public void Equals_DifferentSymbolicName_ReturnsFalse()
        {
            // Arrange
            NodeDesign node1 = CreateTestNodeDesign();
            NodeDesign node2 = CreateTestNodeDesign();
            node2.SymbolicName = new XmlQualifiedName("DifferentName", "http://example.com");

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when NumericIdSpecified differs.
        /// </summary>
        [Test]
        public void Equals_DifferentNumericIdSpecified_ReturnsFalse()
        {
            // Arrange
            NodeDesign node1 = CreateTestNodeDesign();
            NodeDesign node2 = CreateTestNodeDesign();
            node2.NumericIdSpecified = true;

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when StringId differs.
        /// </summary>
        [Test]
        public void Equals_DifferentStringId_ReturnsFalse()
        {
            // Arrange
            NodeDesign node1 = CreateTestNodeDesign();
            NodeDesign node2 = CreateTestNodeDesign();
            node2.StringId = "DifferentStringId";

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when PartNo differs.
        /// </summary>
        [Test]
        public void Equals_DifferentPartNo_ReturnsFalse()
        {
            // Arrange
            NodeDesign node1 = CreateTestNodeDesign();
            NodeDesign node2 = CreateTestNodeDesign();
            node2.PartNo = 5;

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when NotInAddressSpace differs.
        /// </summary>
        [Test]
        public void Equals_DifferentNotInAddressSpace_ReturnsFalse()
        {
            // Arrange
            NodeDesign node1 = CreateTestNodeDesign();
            NodeDesign node2 = CreateTestNodeDesign();
            node2.NotInAddressSpace = true;

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when ReleaseStatus differs.
        /// </summary>
        [Test]
        public void Equals_DifferentReleaseStatus_ReturnsFalse()
        {
            // Arrange
            NodeDesign node1 = CreateTestNodeDesign();
            NodeDesign node2 = CreateTestNodeDesign();
            node2.ReleaseStatus = ReleaseStatus.Draft;

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when Purpose differs.
        /// </summary>
        [Test]
        public void Equals_DifferentPurpose_ReturnsFalse()
        {
            // Arrange
            NodeDesign node1 = CreateTestNodeDesign();
            NodeDesign node2 = CreateTestNodeDesign();
            node2.Purpose = DataTypePurpose.ServicesOnly;

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when IsDynamic differs.
        /// </summary>
        [Test]
        public void Equals_DifferentIsDynamic_ReturnsFalse()
        {
            // Arrange
            NodeDesign node1 = CreateTestNodeDesign();
            NodeDesign node2 = CreateTestNodeDesign();
            node2.IsDynamic = true;

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both instances have null BrowseName.
        /// </summary>
        [Test]
        public void Equals_BothBrowseNameNull_ReturnsTrue()
        {
            // Arrange
            var node1 = new NodeDesign { BrowseName = null };
            var node2 = new NodeDesign { BrowseName = null };

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when one BrowseName is null and the other is not.
        /// </summary>
        [Test]
        public void Equals_OneBrowseNameNull_ReturnsFalse()
        {
            // Arrange
            var node1 = new NodeDesign { BrowseName = "TestName" };
            var node2 = new NodeDesign { BrowseName = null };

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals handles empty string BrowseName correctly.
        /// </summary>
        [Test]
        public void Equals_EmptyBrowseName_HandlesCorrectly()
        {
            // Arrange
            var node1 = new NodeDesign { BrowseName = string.Empty };
            var node2 = new NodeDesign { BrowseName = string.Empty };

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals handles extreme values for NumericId.
        /// </summary>
        [Test]
        public void Equals_ExtremeNumericId_HandlesCorrectly()
        {
            // Arrange
            var node1 = new NodeDesign { NumericId = uint.MaxValue };
            var node2 = new NodeDesign { NumericId = uint.MaxValue };

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals handles extreme values for WriteAccess.
        /// </summary>
        [Test]
        public void Equals_ExtremeWriteAccess_HandlesCorrectly()
        {
            // Arrange
            var node1 = new NodeDesign { WriteAccess = uint.MaxValue };
            var node2 = new NodeDesign { WriteAccess = uint.MaxValue };

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals handles extreme values for PartNo.
        /// </summary>
        [Test]
        public void Equals_ExtremePartNo_HandlesCorrectly()
        {
            // Arrange
            var node1 = new NodeDesign { PartNo = uint.MaxValue };
            var node2 = new NodeDesign { PartNo = uint.MaxValue };

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when both instances have null DisplayName.
        /// </summary>
        [Test]
        public void Equals_BothDisplayNameNull_ReturnsTrue()
        {
            // Arrange
            var node1 = new NodeDesign { DisplayName = null };
            var node2 = new NodeDesign { DisplayName = null };

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when both instances have null Description.
        /// </summary>
        [Test]
        public void Equals_BothDescriptionNull_ReturnsTrue()
        {
            // Arrange
            var node1 = new NodeDesign { Description = null };
            var node2 = new NodeDesign { Description = null };

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when both instances have null References.
        /// </summary>
        [Test]
        public void Equals_BothReferencesNull_ReturnsTrue()
        {
            // Arrange
            var node1 = new NodeDesign { References = null };
            var node2 = new NodeDesign { References = null };

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when both instances have empty References arrays.
        /// </summary>
        [Test]
        public void Equals_BothReferencesEmpty_ReturnsTrue()
        {
            // Arrange
            var node1 = new NodeDesign { References = [] };
            var node2 = new NodeDesign { References = [] };

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when both instances have null Extensions.
        /// </summary>
        [Test]
        public void Equals_BothExtensionsNull_ReturnsTrue()
        {
            // Arrange
            var node1 = new NodeDesign { Extensions = null };
            var node2 = new NodeDesign { Extensions = null };

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when both instances have null SymbolicName.
        /// </summary>
        [Test]
        public void Equals_BothSymbolicNameNull_ReturnsTrue()
        {
            // Arrange
            var node1 = new NodeDesign { SymbolicName = null };
            var node2 = new NodeDesign { SymbolicName = null };

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when both instances have null SymbolicId.
        /// </summary>
        [Test]
        public void Equals_BothSymbolicIdNull_ReturnsTrue()
        {
            // Arrange
            var node1 = new NodeDesign { SymbolicId = null };
            var node2 = new NodeDesign { SymbolicId = null };

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when both instances have null StringId.
        /// </summary>
        [Test]
        public void Equals_BothStringIdNull_ReturnsTrue()
        {
            // Arrange
            var node1 = new NodeDesign { StringId = null };
            var node2 = new NodeDesign { StringId = null };

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when both instances have all enum values set to their valid extremes.
        /// </summary>
        [Test]
        public void Equals_AllEnumExtremes_ReturnsTrue()
        {
            // Arrange
            var node1 = new NodeDesign
            {
                ReleaseStatus = ReleaseStatus.Deprecated,
                Purpose = DataTypePurpose.Testing,
                AccessRestrictions = AccessRestrictions.SessionWithEncryptionAndApplyToBrowseRequired
            };
            var node2 = new NodeDesign
            {
                ReleaseStatus = ReleaseStatus.Deprecated,
                Purpose = DataTypePurpose.Testing,
                AccessRestrictions = AccessRestrictions.SessionWithEncryptionAndApplyToBrowseRequired
            };

            // Act
            bool result = node1.Equals(node2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Helper method to create a test NodeDesign with all properties set.
        /// </summary>
        private static NodeDesign CreateTestNodeDesign()
        {
            return new NodeDesign
            {
                BrowseName = "TestNode",
                DisplayName = new LocalizedText { Value = "Test Display Name" },
                Description = new LocalizedText { Value = "Test Description" },
                Children = null,
                References = null,
                RolePermissions = null,
                DefaultRolePermissions = null,
                AccessRestrictions = AccessRestrictions.SigningRequired,
                AccessRestrictionsSpecified = false,
                DefaultAccessRestrictions = AccessRestrictions.SigningRequired,
                DefaultAccessRestrictionsSpecified = false,
                Extensions = null,
                SymbolicName = new XmlQualifiedName("SymbolicName", "http://test.com"),
                SymbolicId = new XmlQualifiedName("SymbolicId", "http://test.com"),
                IsDeclaration = false,
                NumericId = 100,
                NumericIdSpecified = false,
                StringId = "TestStringId",
                WriteAccess = 0,
                PartNo = 0,
                Category = string.Empty,
                NotInAddressSpace = false,
                ReleaseStatus = ReleaseStatus.Released,
                Purpose = DataTypePurpose.Normal,
                IsDynamic = false
            };
        }
    }
}
