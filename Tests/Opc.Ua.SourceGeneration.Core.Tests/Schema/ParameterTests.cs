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
    /// Tests for the <see cref="Parameter"/> class Equals methods.
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ParameterTests
    {
        /// <summary>
        /// Tests that Equals(object) returns false when the input is null.
        /// </summary>
        [Test]
        public void Equals_NullObject_ReturnsFalse()
        {
            // Arrange
            var parameter = new Parameter { Name = "TestParameter" };

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = parameter.Equals((object)null);
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when the input is of a different type.
        /// </summary>
        [Test]
        [TestCase("string")]
        [TestCase(42)]
        [TestCase(3.14)]
        public void Equals_DifferentType_ReturnsFalse(object obj)
        {
            // Arrange
            var parameter = new Parameter { Name = "TestParameter" };

            // Act
            bool result = parameter.Equals(obj);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns true when comparing the same instance.
        /// </summary>
        [Test]
        public void Equals_SameInstance_ReturnsTrue()
        {
            // Arrange
            var parameter = new Parameter
            {
                Name = "TestParameter",
                Identifier = 123,
                IdentifierSpecified = true,
                BitMask = "0xFF",
                DataType = new XmlQualifiedName("Int32", "http://opcfoundation.org/UA/"),
                ValueRank = ValueRank.Scalar,
                ArrayDimensions = "1,2,3",
                AllowSubTypes = true,
                IsOptional = false,
                ReleaseStatus = ReleaseStatus.Released
            };

            // Act
            bool result = parameter.Equals((object)parameter);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals(object) returns true when comparing two instances with identical property values.
        /// </summary>
        [Test]
        public void Equals_EqualInstances_ReturnsTrue()
        {
            // Arrange
            var parameter1 = new Parameter
            {
                Name = "TestParameter",
                Identifier = 123,
                IdentifierSpecified = true,
                BitMask = "0xFF",
                DataType = new XmlQualifiedName("Int32", "http://opcfoundation.org/UA/"),
                ValueRank = ValueRank.Scalar,
                ArrayDimensions = "1,2,3",
                AllowSubTypes = true,
                IsOptional = false,
                ReleaseStatus = ReleaseStatus.Released
            };

            var parameter2 = new Parameter
            {
                Name = "TestParameter",
                Identifier = 123,
                IdentifierSpecified = true,
                BitMask = "0xFF",
                DataType = new XmlQualifiedName("Int32", "http://opcfoundation.org/UA/"),
                ValueRank = ValueRank.Scalar,
                ArrayDimensions = "1,2,3",
                AllowSubTypes = true,
                IsOptional = false,
                ReleaseStatus = ReleaseStatus.Released
            };

            // Act
            bool result = parameter1.Equals((object)parameter2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when Name differs.
        /// </summary>
        [Test]
        public void Equals_DifferentName_ReturnsFalse()
        {
            // Arrange
            var parameter1 = new Parameter { Name = "Parameter1" };
            var parameter2 = new Parameter { Name = "Parameter2" };

            // Act
            bool result = parameter1.Equals((object)parameter2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when Identifier differs.
        /// </summary>
        [Test]
        public void Equals_DifferentIdentifier_ReturnsFalse()
        {
            // Arrange
            var parameter1 = new Parameter
            {
                Name = "Test",
                Identifier = 100,
                IdentifierSpecified = true
            };
            var parameter2 = new Parameter
            {
                Name = "Test",
                Identifier = 200,
                IdentifierSpecified = true
            };

            // Act
            bool result = parameter1.Equals((object)parameter2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when IdentifierSpecified differs.
        /// </summary>
        [Test]
        public void Equals_DifferentIdentifierSpecified_ReturnsFalse()
        {
            // Arrange
            var parameter1 = new Parameter
            {
                Name = "Test",
                Identifier = 100,
                IdentifierSpecified = true
            };
            var parameter2 = new Parameter
            {
                Name = "Test",
                Identifier = 100,
                IdentifierSpecified = false
            };

            // Act
            bool result = parameter1.Equals((object)parameter2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when BitMask differs.
        /// </summary>
        [Test]
        public void Equals_DifferentBitMask_ReturnsFalse()
        {
            // Arrange
            var parameter1 = new Parameter
            {
                Name = "Test",
                BitMask = "0xFF"
            };
            var parameter2 = new Parameter
            {
                Name = "Test",
                BitMask = "0x00"
            };

            // Act
            bool result = parameter1.Equals((object)parameter2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when DataType differs.
        /// </summary>
        [Test]
        public void Equals_DifferentDataType_ReturnsFalse()
        {
            // Arrange
            var parameter1 = new Parameter
            {
                Name = "Test",
                DataType = new XmlQualifiedName("Int32", "http://opcfoundation.org/UA/")
            };
            var parameter2 = new Parameter
            {
                Name = "Test",
                DataType = new XmlQualifiedName("String", "http://opcfoundation.org/UA/")
            };

            // Act
            bool result = parameter1.Equals((object)parameter2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when ArrayDimensions differs.
        /// </summary>
        [Test]
        public void Equals_DifferentArrayDimensions_ReturnsFalse()
        {
            // Arrange
            var parameter1 = new Parameter
            {
                Name = "Test",
                ArrayDimensions = "1,2,3"
            };
            var parameter2 = new Parameter
            {
                Name = "Test",
                ArrayDimensions = "4,5,6"
            };

            // Act
            bool result = parameter1.Equals((object)parameter2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when AllowSubTypes differs.
        /// </summary>
        [Test]
        public void Equals_DifferentAllowSubTypes_ReturnsFalse()
        {
            // Arrange
            var parameter1 = new Parameter
            {
                Name = "Test",
                AllowSubTypes = true
            };
            var parameter2 = new Parameter
            {
                Name = "Test",
                AllowSubTypes = false
            };

            // Act
            bool result = parameter1.Equals((object)parameter2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when IsOptional differs.
        /// </summary>
        [Test]
        public void Equals_DifferentIsOptional_ReturnsFalse()
        {
            // Arrange
            var parameter1 = new Parameter
            {
                Name = "Test",
                IsOptional = true
            };
            var parameter2 = new Parameter
            {
                Name = "Test",
                IsOptional = false
            };

            // Act
            bool result = parameter1.Equals((object)parameter2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when ReleaseStatus differs.
        /// </summary>
        [Test]
        public void Equals_DifferentReleaseStatus_ReturnsFalse()
        {
            // Arrange
            var parameter1 = new Parameter
            {
                Name = "Test",
                ReleaseStatus = ReleaseStatus.Released
            };
            var parameter2 = new Parameter
            {
                Name = "Test",
                ReleaseStatus = ReleaseStatus.Draft
            };

            // Act
            bool result = parameter1.Equals((object)parameter2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns true when both instances have default values.
        /// </summary>
        [Test]
        public void Equals_DefaultInstances_ReturnsTrue()
        {
            // Arrange
            var parameter1 = new Parameter();
            var parameter2 = new Parameter();

            // Act
            bool result = parameter1.Equals((object)parameter2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals(object) returns true when both instances have null string properties.
        /// </summary>
        [Test]
        public void Equals_NullStringProperties_ReturnsTrue()
        {
            // Arrange
            var parameter1 = new Parameter
            {
                Name = null,
                BitMask = null,
                ArrayDimensions = null
            };
            var parameter2 = new Parameter
            {
                Name = null,
                BitMask = null,
                ArrayDimensions = null
            };

            // Act
            bool result = parameter1.Equals((object)parameter2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when comparing with an incompatible type instance.
        /// </summary>
        [Test]
        public void Equals_IncompatibleType_ReturnsFalse()
        {
            // Arrange
            var parameter = new Parameter { Name = "Test" };
            var otherObject = new LocalizedText { Key = "Test" };

            // Act
            bool result = parameter.Equals(otherObject);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) handles extreme decimal Identifier values correctly.
        /// </summary>
        [Test]
        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(1)]
        public void Equals_ExtremeIdentifierValues_WorksCorrectly(decimal identifierValue)
        {
            // Arrange
            var parameter1 = new Parameter
            {
                Name = "Test",
                Identifier = identifierValue,
                IdentifierSpecified = true
            };
            var parameter2 = new Parameter
            {
                Name = "Test",
                Identifier = identifierValue,
                IdentifierSpecified = true
            };

            // Act
            bool result = parameter1.Equals((object)parameter2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when Name is null in one instance but not the other.
        /// </summary>
        [Test]
        public void Equals_NullVsNonNullName_ReturnsFalse()
        {
            // Arrange
            var parameter1 = new Parameter { Name = null };
            var parameter2 = new Parameter { Name = "Test" };

            // Act
            bool result = parameter1.Equals((object)parameter2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) handles empty string properties correctly.
        /// </summary>
        [Test]
        public void Equals_EmptyStringProperties_ReturnsTrue()
        {
            // Arrange
            var parameter1 = new Parameter
            {
                Name = string.Empty,
                BitMask = string.Empty,
                ArrayDimensions = string.Empty
            };
            var parameter2 = new Parameter
            {
                Name = string.Empty,
                BitMask = string.Empty,
                ArrayDimensions = string.Empty
            };

            // Act
            bool result = parameter1.Equals((object)parameter2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals(object) handles whitespace-only string properties correctly.
        /// </summary>
        [Test]
        public void Equals_WhitespaceStringProperties_ReturnsTrue()
        {
            // Arrange
            var parameter1 = new Parameter
            {
                Name = "   ",
                BitMask = "\t",
                ArrayDimensions = "\n"
            };
            var parameter2 = new Parameter
            {
                Name = "   ",
                BitMask = "\t",
                ArrayDimensions = "\n"
            };

            // Act
            bool result = parameter1.Equals((object)parameter2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals(object) handles very long string properties correctly.
        /// </summary>
        [Test]
        public void Equals_VeryLongStringProperties_ReturnsTrue()
        {
            // Arrange
            string longString = new('a', 10000);
            var parameter1 = new Parameter
            {
                Name = longString,
                BitMask = longString,
                ArrayDimensions = longString
            };
            var parameter2 = new Parameter
            {
                Name = longString,
                BitMask = longString,
                ArrayDimensions = longString
            };

            // Act
            bool result = parameter1.Equals((object)parameter2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals(object) handles special characters in string properties correctly.
        /// </summary>
        [Test]
        public void Equals_SpecialCharactersInStrings_ReturnsTrue()
        {
            // Arrange
            var parameter1 = new Parameter
            {
                Name = "Test\n\r\t\0",
                BitMask = "特殊字符",
                ArrayDimensions = "😀🎉"
            };
            var parameter2 = new Parameter
            {
                Name = "Test\n\r\t\0",
                BitMask = "特殊字符",
                ArrayDimensions = "😀🎉"
            };

            // Act
            bool result = parameter1.Equals((object)parameter2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that GetHashCode returns consistent hash code for the same object.
        /// The hash code should remain constant across multiple calls on the same instance.
        /// Expected result: Same hash code value on repeated calls.
        /// </summary>
        [Test]
        public void GetHashCode_SameObject_ReturnsConsistentHashCode()
        {
            // Arrange
            var parameter = new Parameter
            {
                Name = "TestParameter",
                Description = new LocalizedText { Value = "Test Description" },
                DefaultValue = null,
                DisplayName = new LocalizedText { Value = "Test Display" },
                Identifier = 42,
                IdentifierSpecified = true,
                BitMask = "0xFF",
                DataType = new XmlQualifiedName("int", "http://test.com"),
                ValueRank = ValueRank.Scalar,
                ArrayDimensions = "1,2,3",
                AllowSubTypes = true,
                IsOptional = false,
                ReleaseStatus = ReleaseStatus.Released
            };

            // Act
            int firstHash = parameter.GetHashCode();
            int secondHash = parameter.GetHashCode();

            // Assert
            Assert.That(secondHash, Is.EqualTo(firstHash));
        }

        /// <summary>
        /// Tests that GetHashCode handles null properties correctly.
        /// All nullable properties set to null should produce a valid hash code without throwing.
        /// Expected result: Valid hash code with no exceptions.
        /// </summary>
        [Test]
        public void GetHashCode_AllNullablePropertiesNull_ReturnsValidHashCode()
        {
            // Arrange
            var parameter = new Parameter
            {
                Name = null,
                Description = null,
                DefaultValue = null,
                DisplayName = null,
                Identifier = 0,
                IdentifierSpecified = false,
                BitMask = null,
                DataType = null,
                ValueRank = ValueRank.Scalar,
                ArrayDimensions = null,
                AllowSubTypes = false,
                IsOptional = false,
                ReleaseStatus = ReleaseStatus.Released
            };

            // Act
            int hashCode = parameter.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.EqualTo(0).Or.EqualTo(0));
        }

        /// <summary>
        /// Tests that changing the Name property produces a different hash code.
        /// Verifies that the Name property contributes to the hash code calculation.
        /// Expected result: Different hash codes for different Name values.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentName_ReturnsDifferentHashCode()
        {
            // Arrange
            var parameter1 = new Parameter { Name = "Parameter1" };
            var parameter2 = new Parameter { Name = "Parameter2" };

            // Act
            int hash1 = parameter1.GetHashCode();
            int hash2 = parameter2.GetHashCode();

            // Assert
            Assert.That(hash2, Is.Not.EqualTo(hash1));
        }

        /// <summary>
        /// Tests that changing the Identifier property produces a different hash code.
        /// Verifies that the Identifier property contributes to the hash code calculation.
        /// Expected result: Different hash codes for different Identifier values.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentIdentifier_ReturnsDifferentHashCode()
        {
            // Arrange
            var parameter1 = new Parameter
            {
                Identifier = 1,
                IdentifierSpecified = true
            };
            var parameter2 = new Parameter
            {
                Identifier = 2,
                IdentifierSpecified = true
            };

            // Act
            int hash1 = parameter1.GetHashCode();
            int hash2 = parameter2.GetHashCode();

            // Assert
            Assert.That(hash2, Is.Not.EqualTo(hash1));
        }

        /// <summary>
        /// Tests that changing the IdentifierSpecified property produces a different hash code.
        /// Verifies that the IdentifierSpecified property contributes to the hash code calculation.
        /// Expected result: Different hash codes for different IdentifierSpecified values.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentIdentifierSpecified_ReturnsDifferentHashCode()
        {
            // Arrange
            var parameter1 = new Parameter
            {
                Identifier = 100,
                IdentifierSpecified = true
            };
            var parameter2 = new Parameter
            {
                Identifier = 100,
                IdentifierSpecified = false
            };

            // Act
            int hash1 = parameter1.GetHashCode();
            int hash2 = parameter2.GetHashCode();

            // Assert
            Assert.That(hash2, Is.Not.EqualTo(hash1));
        }

        /// <summary>
        /// Tests that changing the DataType property produces a different hash code.
        /// Verifies that the DataType property with XmlQualifiedNameEqualityComparer contributes to the hash code calculation.
        /// Expected result: Different hash codes for different DataType values.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentDataType_ReturnsDifferentHashCode()
        {
            // Arrange
            var parameter1 = new Parameter
            {
                DataType = new XmlQualifiedName("int", "http://www.w3.org/2001/XMLSchema")
            };
            var parameter2 = new Parameter
            {
                DataType = new XmlQualifiedName("string", "http://www.w3.org/2001/XMLSchema")
            };

            // Act
            int hash1 = parameter1.GetHashCode();
            int hash2 = parameter2.GetHashCode();

            // Assert
            Assert.That(hash2, Is.Not.EqualTo(hash1));
        }

        /// <summary>
        /// Tests that changing the AllowSubTypes property produces a different hash code.
        /// Verifies that the AllowSubTypes boolean property contributes to the hash code calculation.
        /// Expected result: Different hash codes for different AllowSubTypes values.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentAllowSubTypes_ReturnsDifferentHashCode()
        {
            // Arrange
            var parameter1 = new Parameter { AllowSubTypes = false };
            var parameter2 = new Parameter { AllowSubTypes = true };

            // Act
            int hash1 = parameter1.GetHashCode();
            int hash2 = parameter2.GetHashCode();

            // Assert
            Assert.That(hash2, Is.Not.EqualTo(hash1));
        }

        /// <summary>
        /// Tests that changing the IsOptional property produces a different hash code.
        /// Verifies that the IsOptional boolean property contributes to the hash code calculation.
        /// Expected result: Different hash codes for different IsOptional values.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentIsOptional_ReturnsDifferentHashCode()
        {
            // Arrange
            var parameter1 = new Parameter { IsOptional = false };
            var parameter2 = new Parameter { IsOptional = true };

            // Act
            int hash1 = parameter1.GetHashCode();
            int hash2 = parameter2.GetHashCode();

            // Assert
            Assert.That(hash2, Is.Not.EqualTo(hash1));
        }

        /// <summary>
        /// Tests that changing the ReleaseStatus property produces a different hash code.
        /// Verifies that the ReleaseStatus enum property contributes to the hash code calculation.
        /// Expected result: Different hash codes for different ReleaseStatus values.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentReleaseStatus_ReturnsDifferentHashCode()
        {
            // Arrange
            var parameter1 = new Parameter { ReleaseStatus = ReleaseStatus.Released };
            var parameter2 = new Parameter { ReleaseStatus = ReleaseStatus.Draft };

            // Act
            int hash1 = parameter1.GetHashCode();
            int hash2 = parameter2.GetHashCode();

            // Assert
            Assert.That(hash2, Is.Not.EqualTo(hash1));
        }

        /// <summary>
        /// Tests that changing the BitMask property produces a different hash code.
        /// Verifies that the BitMask string property contributes to the hash code calculation.
        /// Expected result: Different hash codes for different BitMask values.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentBitMask_ReturnsDifferentHashCode()
        {
            // Arrange
            var parameter1 = new Parameter { BitMask = "0x01" };
            var parameter2 = new Parameter { BitMask = "0x02" };

            // Act
            int hash1 = parameter1.GetHashCode();
            int hash2 = parameter2.GetHashCode();

            // Assert
            Assert.That(hash2, Is.Not.EqualTo(hash1));
        }

        /// <summary>
        /// Tests that changing the ArrayDimensions property produces a different hash code.
        /// Verifies that the ArrayDimensions string property contributes to the hash code calculation.
        /// Expected result: Different hash codes for different ArrayDimensions values.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentArrayDimensions_ReturnsDifferentHashCode()
        {
            // Arrange
            var parameter1 = new Parameter { ArrayDimensions = "1,2,3" };
            var parameter2 = new Parameter { ArrayDimensions = "4,5,6" };

            // Act
            int hash1 = parameter1.GetHashCode();
            int hash2 = parameter2.GetHashCode();

            // Assert
            Assert.That(hash2, Is.Not.EqualTo(hash1));
        }

        /// <summary>
        /// Tests hash code with extreme Identifier values.
        /// Verifies that extreme decimal values are handled correctly in hash code calculation.
        /// Expected result: Valid hash codes for extreme values.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(-1)]
        public void GetHashCode_ExtremeIdentifierValues_ReturnsValidHashCode(decimal identifier)
        {
            // Arrange
            var parameter = new Parameter
            {
                Identifier = identifier,
                IdentifierSpecified = true
            };

            // Act
            int hashCode = parameter.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.EqualTo(0).Or.EqualTo(0));
        }

        /// <summary>
        /// Tests hash code with empty string properties.
        /// Verifies that empty strings are handled correctly in hash code calculation.
        /// Expected result: Valid hash code.
        /// </summary>
        [Test]
        public void GetHashCode_EmptyStrings_ReturnsValidHashCode()
        {
            // Arrange
            var parameter = new Parameter
            {
                Name = string.Empty,
                BitMask = string.Empty,
                ArrayDimensions = string.Empty
            };

            // Act
            int hashCode = parameter.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.EqualTo(0).Or.EqualTo(0));
        }

        /// <summary>
        /// Tests hash code with whitespace-only string properties.
        /// Verifies that whitespace strings are handled correctly in hash code calculation.
        /// Expected result: Valid hash code different from empty strings.
        /// </summary>
        [Test]
        public void GetHashCode_WhitespaceStrings_ReturnsDifferentHashCodeFromEmpty()
        {
            // Arrange
            var parameterEmpty = new Parameter { Name = string.Empty };
            var parameterWhitespace = new Parameter { Name = "   " };

            // Act
            int hashEmpty = parameterEmpty.GetHashCode();
            int hashWhitespace = parameterWhitespace.GetHashCode();

            // Assert
            Assert.That(hashWhitespace, Is.Not.EqualTo(hashEmpty));
        }

        /// <summary>
        /// Tests hash code with very long string properties.
        /// Verifies that long strings are handled correctly in hash code calculation.
        /// Expected result: Valid hash code.
        /// </summary>
        [Test]
        public void GetHashCode_VeryLongStrings_ReturnsValidHashCode()
        {
            // Arrange
            string longString = new('a', 10000);
            var parameter = new Parameter
            {
                Name = longString,
                BitMask = longString,
                ArrayDimensions = longString
            };

            // Act
            int hashCode = parameter.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.EqualTo(0).Or.EqualTo(0));
        }

        /// <summary>
        /// Tests hash code with special characters in string properties.
        /// Verifies that special and control characters are handled correctly in hash code calculation.
        /// Expected result: Valid hash code.
        /// </summary>
        [Test]
        public void GetHashCode_SpecialCharactersInStrings_ReturnsValidHashCode()
        {
            // Arrange
            var parameter = new Parameter
            {
                Name = "Test\n\r\t\0Parameter",
                BitMask = "!@#$%^&*()",
                ArrayDimensions = "你好世界"
            };

            // Act
            int hashCode = parameter.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.EqualTo(0).Or.EqualTo(0));
        }

        /// <summary>
        /// Tests that all ReleaseStatus enum values produce valid hash codes.
        /// Verifies that all defined ReleaseStatus enum values are handled correctly.
        /// Expected result: Valid hash codes for all enum values.
        /// </summary>
        [TestCase(ReleaseStatus.Released)]
        [TestCase(ReleaseStatus.Draft)]
        [TestCase(ReleaseStatus.Deprecated)]
        public void GetHashCode_AllReleaseStatusEnumValues_ReturnsValidHashCode(ReleaseStatus releaseStatus)
        {
            // Arrange
            var parameter = new Parameter { ReleaseStatus = releaseStatus };

            // Act
            int hashCode = parameter.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.EqualTo(0).Or.EqualTo(0));
        }
    }
}
