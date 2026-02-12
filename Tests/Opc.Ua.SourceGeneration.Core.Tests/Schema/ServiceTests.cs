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

namespace Opc.Ua.Schema.Model.Tests
{
    /// <summary>
    /// Unit tests for the <see cref="Service"/> class GetHashCode method.
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ServiceTests
    {
        /// <summary>
        /// Tests that GetHashCode returns the same value when called multiple times on the same instance.
        /// This verifies the consistency requirement of GetHashCode.
        /// </summary>
        [Test]
        public void GetHashCode_SameInstance_ReturnsConsistentHashCode()
        {
            // Arrange
            var service = new Service { Category = ServiceCategory.Session, Name = "TestService" };

            // Act
            int hashCode1 = service.GetHashCode();
            int hashCode2 = service.GetHashCode();
            int hashCode3 = service.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.EqualTo(hashCode2));
            Assert.That(hashCode2, Is.EqualTo(hashCode3));
        }

        /// <summary>
        /// Tests that two Service objects with identical Category and Name values return the same hash code.
        /// This verifies the equality-consistency requirement: equal objects must have equal hash codes.
        /// </summary>
        [Test]
        public void GetHashCode_EqualObjects_ReturnSameHashCode()
        {
            // Arrange
            var service1 = new Service { Category = ServiceCategory.Discovery, Name = "FindServers" };
            var service2 = new Service { Category = ServiceCategory.Discovery, Name = "FindServers" };

            // Act
            int hashCode1 = service1.GetHashCode();
            int hashCode2 = service2.GetHashCode();

            // Assert
            Assert.That(service1.Equals(service2), Is.True);
            Assert.That(hashCode1, Is.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that Service objects with different Category values produce different hash codes.
        /// While collisions are allowed, different values should typically produce different hash codes.
        /// </summary>
        /// <param name="category1">The first service category.</param>
        /// <param name="category2">The second service category.</param>
        [TestCase(ServiceCategory.Session, ServiceCategory.SecureChannel)]
        [TestCase(ServiceCategory.Discovery, ServiceCategory.Registration)]
        [TestCase(ServiceCategory.None, ServiceCategory.Test)]
        [TestCase(ServiceCategory.Session, ServiceCategory.None)]
        [TestCase(ServiceCategory.SecureChannel, ServiceCategory.Discovery)]
        public void GetHashCode_DifferentCategory_ProducesDifferentHashCode(ServiceCategory category1, ServiceCategory category2)
        {
            // Arrange
            var service1 = new Service { Category = category1, Name = "SameName" };
            var service2 = new Service { Category = category2, Name = "SameName" };

            // Act
            int hashCode1 = service1.GetHashCode();
            int hashCode2 = service2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that Service objects with different Name values produce different hash codes.
        /// While collisions are allowed, different values should typically produce different hash codes.
        /// </summary>
        /// <param name="name1">The first service name.</param>
        /// <param name="name2">The second service name.</param>
        [TestCase("Service1", "Service2")]
        [TestCase("", "NonEmpty")]
        [TestCase("Test", "TEST")]
        [TestCase("a", "b")]
        [TestCase("LongServiceName", "ShortName")]
        public void GetHashCode_DifferentName_ProducesDifferentHashCode(string name1, string name2)
        {
            // Arrange
            var service1 = new Service { Category = ServiceCategory.Session, Name = name1 };
            var service2 = new Service { Category = ServiceCategory.Session, Name = name2 };

            // Act
            int hashCode1 = service1.GetHashCode();
            int hashCode2 = service2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode handles null Name values correctly without throwing exceptions.
        /// Verifies that the hash code can be computed even when Name is null.
        /// </summary>
        [Test]
        public void GetHashCode_NullName_HandlesCorrectly()
        {
            // Arrange
            var service = new Service { Category = ServiceCategory.Session, Name = null };

            // Act & Assert
            Assert.DoesNotThrow(() => service.GetHashCode());
        }

        /// <summary>
        /// Tests that GetHashCode handles empty string Name values correctly.
        /// Verifies consistent behavior with empty strings.
        /// </summary>
        [Test]
        public void GetHashCode_EmptyName_HandlesCorrectly()
        {
            // Arrange
            var service = new Service { Category = ServiceCategory.Session, Name = string.Empty };

            // Act
            int hashCode = service.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode handles whitespace-only Name values correctly.
        /// Verifies that whitespace strings produce valid hash codes.
        /// </summary>
        [TestCase("   ")]
        [TestCase("\t")]
        [TestCase("\n")]
        [TestCase(" \t\r\n ")]
        public void GetHashCode_WhitespaceName_HandlesCorrectly(string whitespaceName)
        {
            // Arrange
            var service = new Service { Category = ServiceCategory.Session, Name = whitespaceName };

            // Act & Assert
            Assert.DoesNotThrow(() => service.GetHashCode());
        }

        /// <summary>
        /// Tests that GetHashCode handles all valid ServiceCategory enum values correctly.
        /// Verifies that each enum value can be used in hash code computation.
        /// </summary>
        /// <param name="category">The service category to test.</param>
        [TestCase(ServiceCategory.None)]
        [TestCase(ServiceCategory.Session)]
        [TestCase(ServiceCategory.SecureChannel)]
        [TestCase(ServiceCategory.Discovery)]
        [TestCase(ServiceCategory.Registration)]
        [TestCase(ServiceCategory.Test)]
        public void GetHashCode_AllCategoryValues_HandlesCorrectly(ServiceCategory category)
        {
            // Arrange
            var service = new Service { Category = category, Name = "TestService" };

            // Act
            int hashCode = service.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero);
        }

        /// <summary>
        /// Tests that Service objects with null Name values that are otherwise identical produce the same hash code.
        /// Verifies consistency in hash code computation for null values.
        /// </summary>
        [Test]
        public void GetHashCode_BothNullNames_ReturnSameHashCode()
        {
            // Arrange
            var service1 = new Service { Category = ServiceCategory.Discovery, Name = null };
            var service2 = new Service { Category = ServiceCategory.Discovery, Name = null };

            // Act
            int hashCode1 = service1.GetHashCode();
            int hashCode2 = service2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that Service objects with the same Name but different Category values produce different hash codes.
        /// Verifies that Category contributes meaningfully to the hash code.
        /// </summary>
        [Test]
        public void GetHashCode_SameNameDifferentCategory_ProducesDifferentHashCode()
        {
            // Arrange
            var service1 = new Service { Category = ServiceCategory.Session, Name = "CommonName" };
            var service2 = new Service { Category = ServiceCategory.Discovery, Name = "CommonName" };

            // Act
            int hashCode1 = service1.GetHashCode();
            int hashCode2 = service2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that Service objects with null vs. empty string Name values produce different hash codes.
        /// Verifies that null and empty string are treated as distinct values.
        /// </summary>
        [Test]
        public void GetHashCode_NullVsEmptyName_ProducesDifferentHashCode()
        {
            // Arrange
            var serviceWithNull = new Service { Category = ServiceCategory.Session, Name = null };
            var serviceWithEmpty = new Service { Category = ServiceCategory.Session, Name = string.Empty };

            // Act
            int hashCodeNull = serviceWithNull.GetHashCode();
            int hashCodeEmpty = serviceWithEmpty.GetHashCode();

            // Assert
            Assert.That(hashCodeNull, Is.Not.EqualTo(hashCodeEmpty));
        }

        /// <summary>
        /// Tests that GetHashCode handles special characters in Name values correctly.
        /// Verifies robust handling of various special character inputs.
        /// </summary>
        /// <param name="specialName">The special character name to test.</param>
        [TestCase("Service@#$%")]
        [TestCase("Service\u0000WithNull")]
        [TestCase("Service\u200BWithZeroWidthSpace")]
        [TestCase("Service\uFFFDWithReplacementChar")]
        [TestCase("Service™®©")]
        public void GetHashCode_SpecialCharactersInName_HandlesCorrectly(string specialName)
        {
            // Arrange
            var service = new Service { Category = ServiceCategory.Session, Name = specialName };

            // Act & Assert
            Assert.DoesNotThrow(() => service.GetHashCode());
        }

        /// <summary>
        /// Tests that GetHashCode handles very long Name values correctly.
        /// Verifies that the hash code computation works efficiently with large strings.
        /// </summary>
        [Test]
        public void GetHashCode_VeryLongName_HandlesCorrectly()
        {
            // Arrange
            string veryLongName = new('a', 10000);
            var service = new Service { Category = ServiceCategory.Session, Name = veryLongName };

            // Act & Assert
            Assert.DoesNotThrow(() => service.GetHashCode());
        }

        /// <summary>
        /// Tests that Equals returns true when comparing a Service instance with itself.
        /// Input: Same instance
        /// Expected: Returns true
        /// </summary>
        [Test]
        public void Equals_SameInstance_ReturnsTrue()
        {
            // Arrange
            var service = new Service { Category = ServiceCategory.Session, Name = "TestService" };

            // Act
            bool result = service.Equals((object)service);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing two Service instances with identical Category and Name.
        /// Input: Two different instances with same Category and Name
        /// Expected: Returns true
        /// </summary>
        [Test]
        public void Equals_IdenticalCategoryAndName_ReturnsTrue()
        {
            // Arrange
            var service1 = new Service { Category = ServiceCategory.Session, Name = "TestService" };
            var service2 = new Service { Category = ServiceCategory.Session, Name = "TestService" };

            // Act
            bool result = service1.Equals((object)service2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with null.
        /// Input: null
        /// Expected: Returns false
        /// </summary>
        [Test]
        public void Equals_Null_ReturnsFalse()
        {
            // Arrange
            var service = new Service { Category = ServiceCategory.Session, Name = "TestService" };

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = service.Equals((object)null);
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with an object of a different type.
        /// Input: Object of different type (string)
        /// Expected: Returns false
        /// </summary>
        [Test]
        public void Equals_DifferentType_ReturnsFalse()
        {
            // Arrange
            var service = new Service { Category = ServiceCategory.Session, Name = "TestService" };
            object differentType = "NotAService";

            // Act
            bool result = service.Equals(differentType);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing Service instances with different Category values.
        /// Input: Two instances with different Category but same Name
        /// Expected: Returns false
        /// </summary>
        [Test]
        public void Equals_DifferentCategory_ReturnsFalse()
        {
            // Arrange
            var service1 = new Service { Category = ServiceCategory.Session, Name = "TestService" };
            var service2 = new Service { Category = ServiceCategory.Discovery, Name = "TestService" };

            // Act
            bool result = service1.Equals((object)service2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing Service instances with different Name values.
        /// Input: Two instances with same Category but different Name
        /// Expected: Returns false
        /// </summary>
        [Test]
        public void Equals_DifferentName_ReturnsFalse()
        {
            // Arrange
            var service1 = new Service { Category = ServiceCategory.Session, Name = "Service1" };
            var service2 = new Service { Category = ServiceCategory.Session, Name = "Service2" };

            // Act
            bool result = service1.Equals((object)service2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing Service instances with both different Category and Name.
        /// Input: Two instances with different Category and different Name
        /// Expected: Returns false
        /// </summary>
        [Test]
        public void Equals_DifferentCategoryAndName_ReturnsFalse()
        {
            // Arrange
            var service1 = new Service { Category = ServiceCategory.Session, Name = "Service1" };
            var service2 = new Service { Category = ServiceCategory.Discovery, Name = "Service2" };

            // Act
            bool result = service1.Equals((object)service2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing instances with null Name values.
        /// Input: Two instances with same Category and both having null Name
        /// Expected: Returns true
        /// </summary>
        [Test]
        public void Equals_BothNullNames_ReturnsTrue()
        {
            // Arrange
            var service1 = new Service { Category = ServiceCategory.Session, Name = null };
            var service2 = new Service { Category = ServiceCategory.Session, Name = null };

            // Act
            bool result = service1.Equals((object)service2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when one instance has null Name and the other has a non-null Name.
        /// Input: Two instances with same Category, one with null Name and one with non-null Name
        /// Expected: Returns false
        /// </summary>
        [Test]
        public void Equals_OneNullName_ReturnsFalse()
        {
            // Arrange
            var service1 = new Service { Category = ServiceCategory.Session, Name = null };
            var service2 = new Service { Category = ServiceCategory.Session, Name = "TestService" };

            // Act
            bool result = service1.Equals((object)service2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing instances with empty string Name values.
        /// Input: Two instances with same Category and both having empty string Name
        /// Expected: Returns true
        /// </summary>
        [Test]
        public void Equals_BothEmptyNames_ReturnsTrue()
        {
            // Arrange
            var service1 = new Service { Category = ServiceCategory.Session, Name = string.Empty };
            var service2 = new Service { Category = ServiceCategory.Session, Name = string.Empty };

            // Act
            bool result = service1.Equals((object)service2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals is case-sensitive for Name property.
        /// Input: Two instances with same Category but different case in Name
        /// Expected: Returns false
        /// </summary>
        [Test]
        public void Equals_DifferentNameCase_ReturnsFalse()
        {
            // Arrange
            var service1 = new Service { Category = ServiceCategory.Session, Name = "TestService" };
            var service2 = new Service { Category = ServiceCategory.Session, Name = "testservice" };

            // Act
            bool result = service1.Equals((object)service2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals handles whitespace in Name property correctly.
        /// Input: Two instances with whitespace differences in Name
        /// Expected: Returns false (whitespace matters)
        /// </summary>
        [Test]
        public void Equals_NameWithWhitespace_ReturnsFalse()
        {
            // Arrange
            var service1 = new Service { Category = ServiceCategory.Session, Name = "TestService" };
            var service2 = new Service { Category = ServiceCategory.Session, Name = " TestService " };

            // Act
            bool result = service1.Equals((object)service2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals handles special characters in Name property.
        /// Input: Two instances with special characters in Name
        /// Expected: Returns true when names are identical
        /// </summary>
        [Test]
        public void Equals_NameWithSpecialCharacters_ReturnsTrue()
        {
            // Arrange
            var service1 = new Service { Category = ServiceCategory.Session, Name = "Test$Service@123!#" };
            var service2 = new Service { Category = ServiceCategory.Session, Name = "Test$Service@123!#" };

            // Act
            bool result = service1.Equals((object)service2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing to null.
        /// </summary>
        [Test]
        public void Equals_NullInput_ReturnsFalse()
        {
            // Arrange
            var service = new Service { Category = ServiceCategory.Session, Name = "TestService" };

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = service.Equals(null);
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing two services with identical Category and Name.
        /// </summary>
        [Test]
        public void Equals_EqualCategoryAndName_ReturnsTrue()
        {
            // Arrange
            var service1 = new Service { Category = ServiceCategory.Session, Name = "TestService" };
            var service2 = new Service { Category = ServiceCategory.Session, Name = "TestService" };

            // Act
            bool result = service1.Equals(service2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when both services have null names and the same category.
        /// </summary>
        [Test]
        public void Equals_BothNamesNull_ReturnsTrue()
        {
            // Arrange
            var service1 = new Service { Category = ServiceCategory.Session, Name = null };
            var service2 = new Service { Category = ServiceCategory.Session, Name = null };

            // Act
            bool result = service1.Equals(service2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when one name is null and the other is not.
        /// </summary>
        [Test]
        public void Equals_OneNameNull_ReturnsFalse()
        {
            // Arrange
            var service1 = new Service { Category = ServiceCategory.Session, Name = null };
            var service2 = new Service { Category = ServiceCategory.Session, Name = "TestService" };

            // Act
            bool result = service1.Equals(service2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both services have empty string names and the same category.
        /// </summary>
        [Test]
        public void Equals_EmptyStringNames_ReturnsTrue()
        {
            // Arrange
            var service1 = new Service { Category = ServiceCategory.Session, Name = string.Empty };
            var service2 = new Service { Category = ServiceCategory.Session, Name = string.Empty };

            // Act
            bool result = service1.Equals(service2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when both services have whitespace-only names and the same category.
        /// </summary>
        [Test]
        public void Equals_WhitespaceNames_ReturnsTrue()
        {
            // Arrange
            var service1 = new Service { Category = ServiceCategory.Session, Name = "   " };
            var service2 = new Service { Category = ServiceCategory.Session, Name = "   " };

            // Act
            bool result = service1.Equals(service2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals correctly differentiates between empty string and whitespace-only names.
        /// </summary>
        [Test]
        public void Equals_EmptyVsWhitespace_ReturnsFalse()
        {
            // Arrange
            var service1 = new Service { Category = ServiceCategory.Session, Name = string.Empty };
            var service2 = new Service { Category = ServiceCategory.Session, Name = " " };

            // Act
            bool result = service1.Equals(service2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when services have identical long names with special characters.
        /// </summary>
        [Test]
        public void Equals_LongNameWithSpecialCharacters_ReturnsTrue()
        {
            // Arrange
            string longName = new string('A', 1000) + "!@#$%^&*()_+-=[]{}|;:',.<>?/";
            var service1 = new Service { Category = ServiceCategory.Session, Name = longName };
            var service2 = new Service { Category = ServiceCategory.Session, Name = longName };

            // Act
            bool result = service1.Equals(service2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when Request and Response differ but Category and Name are the same.
        /// This validates that only Category and Name are used for equality comparison.
        /// </summary>
        [Test]
        public void Equals_DifferentRequestResponse_ReturnsTrue()
        {
            // Arrange
            var service1 = new Service
            {
                Category = ServiceCategory.Session,
                Name = "TestService",
                Request = new DataTypeDesign { SymbolicName = new System.Xml.XmlQualifiedName("Request1") },
                Response = new DataTypeDesign { SymbolicName = new System.Xml.XmlQualifiedName("Response1") }
            };
            var service2 = new Service
            {
                Category = ServiceCategory.Session,
                Name = "TestService",
                Request = new DataTypeDesign { SymbolicName = new System.Xml.XmlQualifiedName("Request2") },
                Response = new DataTypeDesign { SymbolicName = new System.Xml.XmlQualifiedName("Response2") }
            };

            // Act
            bool result = service1.Equals(service2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing services with null Request and Response properties.
        /// </summary>
        [Test]
        public void Equals_NullRequestResponse_ReturnsTrue()
        {
            // Arrange
            var service1 = new Service
            {
                Category = ServiceCategory.Session,
                Name = "TestService",
                Request = null,
                Response = null
            };
            var service2 = new Service
            {
                Category = ServiceCategory.Session,
                Name = "TestService",
                Request = null,
                Response = null
            };

            // Act
            bool result = service1.Equals(service2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests Equals with all valid ServiceCategory enum values.
        /// </summary>
        [TestCase(ServiceCategory.None)]
        [TestCase(ServiceCategory.Session)]
        [TestCase(ServiceCategory.SecureChannel)]
        [TestCase(ServiceCategory.Discovery)]
        [TestCase(ServiceCategory.Registration)]
        [TestCase(ServiceCategory.Test)]
        public void Equals_AllValidCategories_ReturnsTrue(ServiceCategory category)
        {
            // Arrange
            var service1 = new Service { Category = category, Name = "TestService" };
            var service2 = new Service { Category = category, Name = "TestService" };

            // Act
            bool result = service1.Equals(service2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals is case-sensitive for Name comparison.
        /// </summary>
        [Test]
        public void Equals_CaseDifferentNames_ReturnsFalse()
        {
            // Arrange
            var service1 = new Service { Category = ServiceCategory.Session, Name = "TestService" };
            var service2 = new Service { Category = ServiceCategory.Session, Name = "testservice" };

            // Act
            bool result = service1.Equals(service2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests Equals with an invalid enum value (outside defined range).
        /// </summary>
        [Test]
        public void Equals_InvalidCategoryValue_ReturnsTrue()
        {
            // Arrange
            const ServiceCategory invalidCategory = (ServiceCategory)999;
            var service1 = new Service { Category = invalidCategory, Name = "TestService" };
            var service2 = new Service { Category = invalidCategory, Name = "TestService" };

            // Act
            bool result = service1.Equals(service2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests Equals with Unicode characters in the name.
        /// </summary>
        [Test]
        public void Equals_UnicodeNames_ReturnsTrue()
        {
            // Arrange
            const string unicodeName = "Test服务 🚀 Señor";
            var service1 = new Service { Category = ServiceCategory.Session, Name = unicodeName };
            var service2 = new Service { Category = ServiceCategory.Session, Name = unicodeName };

            // Act
            bool result = service1.Equals(service2);

            // Assert
            Assert.That(result, Is.True);
        }
    }
}
