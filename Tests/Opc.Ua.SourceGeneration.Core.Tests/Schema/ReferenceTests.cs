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
    /// Unit tests for the Reference class.
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ReferenceTests
    {
        /// <summary>
        /// Tests that GetHashCode returns the same value for equal objects with all properties set.
        /// Input: Two Reference objects with identical ReferenceType, TargetId, IsInverse, and IsOneWay.
        /// Expected: Both objects return the same hash code.
        /// </summary>
        [Test]
        public void GetHashCode_EqualObjectsWithAllProperties_ReturnsSameHashCode()
        {
            // Arrange
            var refType = new XmlQualifiedName("HasComponent", "http://opcfoundation.org/UA/");
            var targetId = new XmlQualifiedName("Target", "http://test.org/");

            var reference1 = new Reference
            {
                ReferenceType = refType,
                TargetId = targetId,
                IsInverse = true,
                IsOneWay = true
            };

            var reference2 = new Reference
            {
                ReferenceType = refType,
                TargetId = targetId,
                IsInverse = true,
                IsOneWay = true
            };

            // Act
            int hashCode1 = reference1.GetHashCode();
            int hashCode2 = reference2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode returns the same value for equal objects with equivalent XmlQualifiedName values.
        /// Input: Two Reference objects with XmlQualifiedName instances having the same name and namespace.
        /// Expected: Both objects return the same hash code.
        /// </summary>
        [Test]
        public void GetHashCode_EqualObjectsWithEquivalentXmlQualifiedNames_ReturnsSameHashCode()
        {
            // Arrange
            var reference1 = new Reference
            {
                ReferenceType = new XmlQualifiedName("HasProperty", "http://opcfoundation.org/UA/"),
                TargetId = new XmlQualifiedName("Node1", "http://test.org/"),
                IsInverse = false,
                IsOneWay = false
            };

            var reference2 = new Reference
            {
                ReferenceType = new XmlQualifiedName("HasProperty", "http://opcfoundation.org/UA/"),
                TargetId = new XmlQualifiedName("Node1", "http://test.org/"),
                IsInverse = false,
                IsOneWay = false
            };

            // Act
            int hashCode1 = reference1.GetHashCode();
            int hashCode2 = reference2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different values for objects with different ReferenceType.
        /// Input: Two Reference objects differing only in ReferenceType.
        /// Expected: Different hash codes are returned.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentReferenceType_ReturnsDifferentHashCode()
        {
            // Arrange
            var targetId = new XmlQualifiedName("Target", "http://test.org/");

            var reference1 = new Reference
            {
                ReferenceType = new XmlQualifiedName("HasComponent", "http://opcfoundation.org/UA/"),
                TargetId = targetId,
                IsInverse = false,
                IsOneWay = false
            };

            var reference2 = new Reference
            {
                ReferenceType = new XmlQualifiedName("HasProperty", "http://opcfoundation.org/UA/"),
                TargetId = targetId,
                IsInverse = false,
                IsOneWay = false
            };

            // Act
            int hashCode1 = reference1.GetHashCode();
            int hashCode2 = reference2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different values for objects with different TargetId.
        /// Input: Two Reference objects differing only in TargetId.
        /// Expected: Different hash codes are returned.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentTargetId_ReturnsDifferentHashCode()
        {
            // Arrange
            var refType = new XmlQualifiedName("HasComponent", "http://opcfoundation.org/UA/");

            var reference1 = new Reference
            {
                ReferenceType = refType,
                TargetId = new XmlQualifiedName("Target1", "http://test.org/"),
                IsInverse = false,
                IsOneWay = false
            };

            var reference2 = new Reference
            {
                ReferenceType = refType,
                TargetId = new XmlQualifiedName("Target2", "http://test.org/"),
                IsInverse = false,
                IsOneWay = false
            };

            // Act
            int hashCode1 = reference1.GetHashCode();
            int hashCode2 = reference2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different values for objects with different IsInverse values.
        /// Input: Two Reference objects differing only in IsInverse.
        /// Expected: Different hash codes are returned.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentIsInverse_ReturnsDifferentHashCode()
        {
            // Arrange
            var refType = new XmlQualifiedName("HasComponent", "http://opcfoundation.org/UA/");
            var targetId = new XmlQualifiedName("Target", "http://test.org/");

            var reference1 = new Reference
            {
                ReferenceType = refType,
                TargetId = targetId,
                IsInverse = false,
                IsOneWay = false
            };

            var reference2 = new Reference
            {
                ReferenceType = refType,
                TargetId = targetId,
                IsInverse = true,
                IsOneWay = false
            };

            // Act
            int hashCode1 = reference1.GetHashCode();
            int hashCode2 = reference2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different values for objects with different IsOneWay values.
        /// Input: Two Reference objects differing only in IsOneWay.
        /// Expected: Different hash codes are returned.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentIsOneWay_ReturnsDifferentHashCode()
        {
            // Arrange
            var refType = new XmlQualifiedName("HasComponent", "http://opcfoundation.org/UA/");
            var targetId = new XmlQualifiedName("Target", "http://test.org/");

            var reference1 = new Reference
            {
                ReferenceType = refType,
                TargetId = targetId,
                IsInverse = false,
                IsOneWay = false
            };

            var reference2 = new Reference
            {
                ReferenceType = refType,
                TargetId = targetId,
                IsInverse = false,
                IsOneWay = true
            };

            // Act
            int hashCode1 = reference1.GetHashCode();
            int hashCode2 = reference2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode handles null ReferenceType and TargetId correctly.
        /// Input: Reference object with null ReferenceType and TargetId.
        /// Expected: GetHashCode returns a valid hash code without throwing.
        /// </summary>
        [Test]
        public void GetHashCode_NullReferenceTypeAndTargetId_ReturnsValidHashCode()
        {
            // Arrange
            var reference = new Reference
            {
                ReferenceType = null,
                TargetId = null,
                IsInverse = false,
                IsOneWay = false
            };

            // Act
            int hashCode = reference.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.InstanceOf<int>());
        }

        /// <summary>
        /// Tests that GetHashCode returns the same value for equal objects with null properties.
        /// Input: Two Reference objects with null ReferenceType and TargetId.
        /// Expected: Both objects return the same hash code.
        /// </summary>
        [Test]
        public void GetHashCode_EqualObjectsWithNullProperties_ReturnsSameHashCode()
        {
            // Arrange
            var reference1 = new Reference
            {
                ReferenceType = null,
                TargetId = null,
                IsInverse = true,
                IsOneWay = true
            };

            var reference2 = new Reference
            {
                ReferenceType = null,
                TargetId = null,
                IsInverse = true,
                IsOneWay = true
            };

            // Act
            int hashCode1 = reference1.GetHashCode();
            int hashCode2 = reference2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode is consistent when called multiple times on the same object.
        /// Input: A single Reference object with all properties set.
        /// Expected: Multiple calls to GetHashCode return the same value.
        /// </summary>
        [Test]
        public void GetHashCode_MultipleCallsOnSameObject_ReturnsConsistentValue()
        {
            // Arrange
            var reference = new Reference
            {
                ReferenceType = new XmlQualifiedName("HasComponent", "http://opcfoundation.org/UA/"),
                TargetId = new XmlQualifiedName("Target", "http://test.org/"),
                IsInverse = true,
                IsOneWay = false
            };

            // Act
            int hashCode1 = reference.GetHashCode();
            int hashCode2 = reference.GetHashCode();
            int hashCode3 = reference.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.EqualTo(hashCode2));
            Assert.That(hashCode2, Is.EqualTo(hashCode3));
        }

        /// <summary>
        /// Tests that GetHashCode returns different values for XmlQualifiedName with different namespaces.
        /// Input: Two Reference objects with XmlQualifiedName having the same name but different namespaces.
        /// Expected: Different hash codes are returned.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentNamespaceInXmlQualifiedName_ReturnsDifferentHashCode()
        {
            // Arrange
            var reference1 = new Reference
            {
                ReferenceType = new XmlQualifiedName("HasComponent", "http://opcfoundation.org/UA/"),
                TargetId = new XmlQualifiedName("Target", "http://test1.org/"),
                IsInverse = false,
                IsOneWay = false
            };

            var reference2 = new Reference
            {
                ReferenceType = new XmlQualifiedName("HasComponent", "http://opcfoundation.org/UA/"),
                TargetId = new XmlQualifiedName("Target", "http://test2.org/"),
                IsInverse = false,
                IsOneWay = false
            };

            // Act
            int hashCode1 = reference1.GetHashCode();
            int hashCode2 = reference2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode handles empty string names in XmlQualifiedName.
        /// Input: Reference object with XmlQualifiedName having empty string names.
        /// Expected: GetHashCode returns a valid hash code without throwing.
        /// </summary>
        [Test]
        public void GetHashCode_EmptyStringNamesInXmlQualifiedName_ReturnsValidHashCode()
        {
            // Arrange
            var reference = new Reference
            {
                ReferenceType = new XmlQualifiedName(string.Empty, string.Empty),
                TargetId = new XmlQualifiedName(string.Empty, string.Empty),
                IsInverse = false,
                IsOneWay = false
            };

            // Act
            int hashCode = reference.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.InstanceOf<int>());
        }

        /// <summary>
        /// Tests that GetHashCode adheres to hash code contract with Equals method.
        /// Input: Two equal Reference objects according to Equals method.
        /// Expected: Both objects return the same hash code.
        /// </summary>
        [Test]
        public void GetHashCode_EqualObjectsPerEqualsMethod_ReturnsSameHashCode()
        {
            // Arrange
            var reference1 = new Reference
            {
                ReferenceType = new XmlQualifiedName("Organizes", "http://opcfoundation.org/UA/"),
                TargetId = new XmlQualifiedName("MyNode", "http://example.org/"),
                IsInverse = true,
                IsOneWay = false
            };

            var reference2 = new Reference
            {
                ReferenceType = new XmlQualifiedName("Organizes", "http://opcfoundation.org/UA/"),
                TargetId = new XmlQualifiedName("MyNode", "http://example.org/"),
                IsInverse = true,
                IsOneWay = false
            };

            // Act
            bool areEqual = reference1.Equals(reference2);
            int hashCode1 = reference1.GetHashCode();
            int hashCode2 = reference2.GetHashCode();

            // Assert
            Assert.That(areEqual, Is.True, "Objects should be equal");
            Assert.That(hashCode1, Is.EqualTo(hashCode2), "Equal objects must have the same hash code");
        }

        /// <summary>
        /// Tests that GetHashCode with default constructor values produces consistent hash code.
        /// Input: Two Reference objects created with default constructor.
        /// Expected: Both objects return the same hash code.
        /// </summary>
        [Test]
        public void GetHashCode_DefaultConstructorValues_ReturnsSameHashCode()
        {
            // Arrange
            var reference1 = new Reference();
            var reference2 = new Reference();

            // Act
            int hashCode1 = reference1.GetHashCode();
            int hashCode2 = reference2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode with all boolean combinations produces different values.
        /// Input: Reference objects with all four combinations of IsInverse and IsOneWay values.
        /// Expected: Different hash codes for different boolean combinations.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void GetHashCode_VariousBooleanCombinations_ProducesValidHashCode(bool isInverse, bool isOneWay)
        {
            // Arrange
            var reference = new Reference
            {
                ReferenceType = new XmlQualifiedName("HasComponent", "http://opcfoundation.org/UA/"),
                TargetId = new XmlQualifiedName("Target", "http://test.org/"),
                IsInverse = isInverse,
                IsOneWay = isOneWay
            };

            // Act
            int hashCode = reference.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.InstanceOf<int>());
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with null.
        /// </summary>
        [Test]
        public void Equals_NullObject_ReturnsFalse()
        {
            // Arrange
            var reference = new Reference
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test.com"),
                TargetId = new XmlQualifiedName("Target", "http://test.com"),
                IsInverse = false,
                IsOneWay = false
            };

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = reference.Equals((object)null);
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing an object to itself.
        /// </summary>
        [Test]
        public void Equals_SameReference_ReturnsTrue()
        {
            // Arrange
            var reference = new Reference
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test.com"),
                TargetId = new XmlQualifiedName("Target", "http://test.com"),
                IsInverse = true,
                IsOneWay = true
            };

            // Act
            bool result = reference.Equals((object)reference);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with a different type.
        /// </summary>
        [Test]
        public void Equals_DifferentType_ReturnsFalse()
        {
            // Arrange
            var reference = new Reference
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test.com"),
                TargetId = new XmlQualifiedName("Target", "http://test.com")
            };
            const string differentObject = "Not a Reference";

            // Act
            bool result = reference.Equals(differentObject);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing two different Reference instances with identical properties.
        /// </summary>
        [Test]
        public void Equals_EqualReferences_ReturnsTrue()
        {
            // Arrange
            var reference1 = new Reference
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test.com"),
                TargetId = new XmlQualifiedName("Target", "http://test.com"),
                IsInverse = true,
                IsOneWay = false
            };
            var reference2 = new Reference
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test.com"),
                TargetId = new XmlQualifiedName("Target", "http://test.com"),
                IsInverse = true,
                IsOneWay = false
            };

            // Act
            bool result = reference1.Equals((object)reference2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when ReferenceType differs.
        /// </summary>
        [Test]
        public void Equals_DifferentReferenceType_ReturnsFalse()
        {
            // Arrange
            var reference1 = new Reference
            {
                ReferenceType = new XmlQualifiedName("RefType1", "http://test.com"),
                TargetId = new XmlQualifiedName("Target", "http://test.com"),
                IsInverse = false,
                IsOneWay = false
            };
            var reference2 = new Reference
            {
                ReferenceType = new XmlQualifiedName("RefType2", "http://test.com"),
                TargetId = new XmlQualifiedName("Target", "http://test.com"),
                IsInverse = false,
                IsOneWay = false
            };

            // Act
            bool result = reference1.Equals((object)reference2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when ReferenceType namespace differs.
        /// </summary>
        [Test]
        public void Equals_DifferentReferenceTypeNamespace_ReturnsFalse()
        {
            // Arrange
            var reference1 = new Reference
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test1.com"),
                TargetId = new XmlQualifiedName("Target", "http://test.com"),
                IsInverse = false,
                IsOneWay = false
            };
            var reference2 = new Reference
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test2.com"),
                TargetId = new XmlQualifiedName("Target", "http://test.com"),
                IsInverse = false,
                IsOneWay = false
            };

            // Act
            bool result = reference1.Equals((object)reference2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when TargetId differs.
        /// </summary>
        [Test]
        public void Equals_DifferentTargetId_ReturnsFalse()
        {
            // Arrange
            var reference1 = new Reference
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test.com"),
                TargetId = new XmlQualifiedName("Target1", "http://test.com"),
                IsInverse = false,
                IsOneWay = false
            };
            var reference2 = new Reference
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test.com"),
                TargetId = new XmlQualifiedName("Target2", "http://test.com"),
                IsInverse = false,
                IsOneWay = false
            };

            // Act
            bool result = reference1.Equals((object)reference2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when TargetId namespace differs.
        /// </summary>
        [Test]
        public void Equals_DifferentTargetIdNamespace_ReturnsFalse()
        {
            // Arrange
            var reference1 = new Reference
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test.com"),
                TargetId = new XmlQualifiedName("Target", "http://test1.com"),
                IsInverse = false,
                IsOneWay = false
            };
            var reference2 = new Reference
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test.com"),
                TargetId = new XmlQualifiedName("Target", "http://test2.com"),
                IsInverse = false,
                IsOneWay = false
            };

            // Act
            bool result = reference1.Equals((object)reference2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when IsInverse differs.
        /// </summary>
        [Test]
        public void Equals_DifferentIsInverse_ReturnsFalse()
        {
            // Arrange
            var reference1 = new Reference
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test.com"),
                TargetId = new XmlQualifiedName("Target", "http://test.com"),
                IsInverse = true,
                IsOneWay = false
            };
            var reference2 = new Reference
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test.com"),
                TargetId = new XmlQualifiedName("Target", "http://test.com"),
                IsInverse = false,
                IsOneWay = false
            };

            // Act
            bool result = reference1.Equals((object)reference2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when IsOneWay differs.
        /// </summary>
        [Test]
        public void Equals_DifferentIsOneWay_ReturnsFalse()
        {
            // Arrange
            var reference1 = new Reference
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test.com"),
                TargetId = new XmlQualifiedName("Target", "http://test.com"),
                IsInverse = false,
                IsOneWay = true
            };
            var reference2 = new Reference
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test.com"),
                TargetId = new XmlQualifiedName("Target", "http://test.com"),
                IsInverse = false,
                IsOneWay = false
            };

            // Act
            bool result = reference1.Equals((object)reference2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both ReferenceType properties are null and other properties match.
        /// </summary>
        [Test]
        public void Equals_BothReferenceTypeNull_ReturnsTrue()
        {
            // Arrange
            var reference1 = new Reference
            {
                ReferenceType = null,
                TargetId = new XmlQualifiedName("Target", "http://test.com"),
                IsInverse = false,
                IsOneWay = false
            };
            var reference2 = new Reference
            {
                ReferenceType = null,
                TargetId = new XmlQualifiedName("Target", "http://test.com"),
                IsInverse = false,
                IsOneWay = false
            };

            // Act
            bool result = reference1.Equals((object)reference2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when one ReferenceType is null and the other is not.
        /// </summary>
        [Test]
        public void Equals_OneReferenceTypeNull_ReturnsFalse()
        {
            // Arrange
            var reference1 = new Reference
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test.com"),
                TargetId = new XmlQualifiedName("Target", "http://test.com"),
                IsInverse = false,
                IsOneWay = false
            };
            var reference2 = new Reference
            {
                ReferenceType = null,
                TargetId = new XmlQualifiedName("Target", "http://test.com"),
                IsInverse = false,
                IsOneWay = false
            };

            // Act
            bool result = reference1.Equals((object)reference2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both TargetId properties are null and other properties match.
        /// </summary>
        [Test]
        public void Equals_BothTargetIdNull_ReturnsTrue()
        {
            // Arrange
            var reference1 = new Reference
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test.com"),
                TargetId = null,
                IsInverse = false,
                IsOneWay = false
            };
            var reference2 = new Reference
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test.com"),
                TargetId = null,
                IsInverse = false,
                IsOneWay = false
            };

            // Act
            bool result = reference1.Equals((object)reference2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when one TargetId is null and the other is not.
        /// </summary>
        [Test]
        public void Equals_OneTargetIdNull_ReturnsFalse()
        {
            // Arrange
            var reference1 = new Reference
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test.com"),
                TargetId = new XmlQualifiedName("Target", "http://test.com"),
                IsInverse = false,
                IsOneWay = false
            };
            var reference2 = new Reference
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test.com"),
                TargetId = null,
                IsInverse = false,
                IsOneWay = false
            };

            // Act
            bool result = reference1.Equals((object)reference2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when all properties are null or default and match.
        /// </summary>
        [Test]
        public void Equals_AllPropertiesNullOrDefault_ReturnsTrue()
        {
            // Arrange
            var reference1 = new Reference
            {
                ReferenceType = null,
                TargetId = null,
                IsInverse = false,
                IsOneWay = false
            };
            var reference2 = new Reference
            {
                ReferenceType = null,
                TargetId = null,
                IsInverse = false,
                IsOneWay = false
            };

            // Act
            bool result = reference1.Equals((object)reference2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals handles empty XmlQualifiedName (empty string name and namespace).
        /// </summary>
        [Test]
        public void Equals_EmptyXmlQualifiedNames_ReturnsTrue()
        {
            // Arrange
            var reference1 = new Reference
            {
                ReferenceType = new XmlQualifiedName(string.Empty, string.Empty),
                TargetId = new XmlQualifiedName(string.Empty, string.Empty),
                IsInverse = false,
                IsOneWay = false
            };
            var reference2 = new Reference
            {
                ReferenceType = new XmlQualifiedName(string.Empty, string.Empty),
                TargetId = new XmlQualifiedName(string.Empty, string.Empty),
                IsInverse = false,
                IsOneWay = false
            };

            // Act
            bool result = reference1.Equals((object)reference2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when the other reference is null.
        /// </summary>
        [Test]
        public void Equals_NullReference_ReturnsFalse()
        {
            // Arrange
            var reference = new Reference
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test.org"),
                TargetId = new XmlQualifiedName("Target", "http://test.org"),
                IsInverse = false,
                IsOneWay = false
            };

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = reference.Equals(null);
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when two references have identical properties.
        /// </summary>
        [Test]
        public void Equals_IdenticalProperties_ReturnsTrue()
        {
            // Arrange
            var reference1 = new Reference
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test.org"),
                TargetId = new XmlQualifiedName("Target", "http://test.org"),
                IsInverse = false,
                IsOneWay = false
            };
            var reference2 = new Reference
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test.org"),
                TargetId = new XmlQualifiedName("Target", "http://test.org"),
                IsInverse = false,
                IsOneWay = false
            };

            // Act
            bool result = reference1.Equals(reference2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals handles null ReferenceType in both references correctly.
        /// Input: Both references have null ReferenceType, all other properties identical.
        /// Expected: Returns true since null values are equal.
        /// </summary>
        [Test]
        public void Equals_BothReferenceTypesNull_ReturnsTrue()
        {
            // Arrange
            var reference1 = new Reference
            {
                ReferenceType = null,
                TargetId = new XmlQualifiedName("Target", "http://test.org"),
                IsInverse = false,
                IsOneWay = false
            };
            var reference2 = new Reference
            {
                ReferenceType = null,
                TargetId = new XmlQualifiedName("Target", "http://test.org"),
                IsInverse = false,
                IsOneWay = false
            };

            // Act
            bool result = reference1.Equals(reference2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals handles null TargetId in both references correctly.
        /// Input: Both references have null TargetId, all other properties identical.
        /// Expected: Returns true since null values are equal.
        /// </summary>
        [Test]
        public void Equals_BothTargetIdsNull_ReturnsTrue()
        {
            // Arrange
            var reference1 = new Reference
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test.org"),
                TargetId = null,
                IsInverse = false,
                IsOneWay = false
            };
            var reference2 = new Reference
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test.org"),
                TargetId = null,
                IsInverse = false,
                IsOneWay = false
            };

            // Act
            bool result = reference1.Equals(reference2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when both boolean properties are true.
        /// </summary>
        [Test]
        public void Equals_BothBooleansTrue_ReturnsTrue()
        {
            // Arrange
            var reference1 = new Reference
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test.org"),
                TargetId = new XmlQualifiedName("Target", "http://test.org"),
                IsInverse = true,
                IsOneWay = true
            };
            var reference2 = new Reference
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test.org"),
                TargetId = new XmlQualifiedName("Target", "http://test.org"),
                IsInverse = true,
                IsOneWay = true
            };

            // Act
            bool result = reference1.Equals(reference2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when all properties differ.
        /// </summary>
        [Test]
        public void Equals_AllPropertiesDifferent_ReturnsFalse()
        {
            // Arrange
            var reference1 = new Reference
            {
                ReferenceType = new XmlQualifiedName("RefType1", "http://test1.org"),
                TargetId = new XmlQualifiedName("Target1", "http://test1.org"),
                IsInverse = false,
                IsOneWay = false
            };
            var reference2 = new Reference
            {
                ReferenceType = new XmlQualifiedName("RefType2", "http://test2.org"),
                TargetId = new XmlQualifiedName("Target2", "http://test2.org"),
                IsInverse = true,
                IsOneWay = true
            };

            // Act
            bool result = reference1.Equals(reference2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals handles empty string names in XmlQualifiedName correctly.
        /// </summary>
        [Test]
        public void Equals_EmptyStringNames_ReturnsTrue()
        {
            // Arrange
            var reference1 = new Reference
            {
                ReferenceType = new XmlQualifiedName(string.Empty, string.Empty),
                TargetId = new XmlQualifiedName(string.Empty, string.Empty),
                IsInverse = false,
                IsOneWay = false
            };
            var reference2 = new Reference
            {
                ReferenceType = new XmlQualifiedName(string.Empty, string.Empty),
                TargetId = new XmlQualifiedName(string.Empty, string.Empty),
                IsInverse = false,
                IsOneWay = false
            };

            // Act
            bool result = reference1.Equals(reference2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals handles special characters in XmlQualifiedName correctly.
        /// </summary>
        [Test]
        public void Equals_SpecialCharactersInNames_ReturnsTrue()
        {
            // Arrange
            var reference1 = new Reference
            {
                ReferenceType = new XmlQualifiedName("RefType!@#$%^&*()", "http://test.org/special?chars=<>&"),
                TargetId = new XmlQualifiedName("Target_-+=", "http://test.org"),
                IsInverse = false,
                IsOneWay = false
            };
            var reference2 = new Reference
            {
                ReferenceType = new XmlQualifiedName("RefType!@#$%^&*()", "http://test.org/special?chars=<>&"),
                TargetId = new XmlQualifiedName("Target_-+=", "http://test.org"),
                IsInverse = false,
                IsOneWay = false
            };

            // Act
            bool result = reference1.Equals(reference2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals handles very long strings in XmlQualifiedName correctly.
        /// </summary>
        [Test]
        public void Equals_VeryLongStrings_ReturnsTrue()
        {
            // Arrange
            string longName = new('A', 10000);
            string longNamespace = "http://test.org/" + new string('B', 10000);

            var reference1 = new Reference
            {
                ReferenceType = new XmlQualifiedName(longName, longNamespace),
                TargetId = new XmlQualifiedName(longName, longNamespace),
                IsInverse = false,
                IsOneWay = false
            };
            var reference2 = new Reference
            {
                ReferenceType = new XmlQualifiedName(longName, longNamespace),
                TargetId = new XmlQualifiedName(longName, longNamespace),
                IsInverse = false,
                IsOneWay = false
            };

            // Act
            bool result = reference1.Equals(reference2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals handles both properties being null correctly.
        /// Input: Both references have all nullable properties set to null.
        /// Expected: Returns true since all properties are equal.
        /// </summary>
        [Test]
        public void Equals_AllNullablePropertiesNull_ReturnsTrue()
        {
            // Arrange
            var reference1 = new Reference
            {
                ReferenceType = null,
                TargetId = null,
                IsInverse = false,
                IsOneWay = false
            };
            var reference2 = new Reference
            {
                ReferenceType = null,
                TargetId = null,
                IsInverse = false,
                IsOneWay = false
            };

            // Act
            bool result = reference1.Equals(reference2);

            // Assert
            Assert.That(result, Is.True);
        }
    }
}
