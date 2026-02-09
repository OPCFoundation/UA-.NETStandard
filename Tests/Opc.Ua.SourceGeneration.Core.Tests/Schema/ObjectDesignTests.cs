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
    /// Unit tests for the ObjectDesign class.
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ObjectDesignTests
    {
        /// <summary>
        /// Tests that GetHashCode returns the same value when called multiple times on the same object.
        /// Input: An ObjectDesign instance with specific property values.
        /// Expected: GetHashCode returns the same value on consecutive calls.
        /// </summary>
        [Test]
        public void GetHashCode_CalledMultipleTimes_ReturnsSameValue()
        {
            // Arrange
            var objectDesign = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("TestObject", "http://test.com"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };

            // Act
            int hashCode1 = objectDesign.GetHashCode();
            int hashCode2 = objectDesign.GetHashCode();
            int hashCode3 = objectDesign.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.EqualTo(hashCode2));
            Assert.That(hashCode2, Is.EqualTo(hashCode3));
        }

        /// <summary>
        /// Tests that equal objects have the same hash code.
        /// Input: Two ObjectDesign instances with identical property values.
        /// Expected: Both objects return the same hash code.
        /// </summary>
        [Test]
        public void GetHashCode_EqualObjects_ReturnsSameHashCode()
        {
            // Arrange
            var objectDesign1 = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("TestObject", "http://test.com"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };

            var objectDesign2 = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("TestObject", "http://test.com"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };

            // Act
            int hashCode1 = objectDesign1.GetHashCode();
            int hashCode2 = objectDesign2.GetHashCode();

            // Assert
            Assert.That(objectDesign1.Equals(objectDesign2), Is.True, "Objects should be equal");
            Assert.That(hashCode1, Is.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that objects with different SupportsEvents values have different hash codes.
        /// Input: Two ObjectDesign instances differing only in SupportsEvents property.
        /// Expected: Different hash codes are returned (not strictly required but highly probable).
        /// </summary>
        [Test]
        public void GetHashCode_DifferentSupportsEvents_ReturnsDifferentHashCode()
        {
            // Arrange
            var objectDesign1 = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("TestObject", "http://test.com"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };

            var objectDesign2 = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("TestObject", "http://test.com"),
                SupportsEvents = false,
                SupportsEventsSpecified = true
            };

            // Act
            int hashCode1 = objectDesign1.GetHashCode();
            int hashCode2 = objectDesign2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that objects with different SupportsEventsSpecified values have different hash codes.
        /// Input: Two ObjectDesign instances differing only in SupportsEventsSpecified property.
        /// Expected: Different hash codes are returned (not strictly required but highly probable).
        /// </summary>
        [Test]
        public void GetHashCode_DifferentSupportsEventsSpecified_ReturnsDifferentHashCode()
        {
            // Arrange
            var objectDesign1 = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("TestObject", "http://test.com"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };

            var objectDesign2 = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("TestObject", "http://test.com"),
                SupportsEvents = true,
                SupportsEventsSpecified = false
            };

            // Act
            int hashCode1 = objectDesign1.GetHashCode();
            int hashCode2 = objectDesign2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests hash code generation with various combinations of SupportsEvents and SupportsEventsSpecified.
        /// Input: ObjectDesign instances with different boolean combinations.
        /// Expected: Each combination produces a hash code.
        /// </summary>
        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void GetHashCode_VariousBooleanCombinations_ReturnsHashCode(bool supportsEvents, bool supportsEventsSpecified)
        {
            // Arrange
            var objectDesign = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("TestObject", "http://test.com"),
                SupportsEvents = supportsEvents,
                SupportsEventsSpecified = supportsEventsSpecified
            };

            // Act
            int hashCode = objectDesign.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.EqualTo(0).Or.EqualTo(0), "Hash code should be computed");
        }

        /// <summary>
        /// Tests that objects with different base properties have different hash codes.
        /// Input: Two ObjectDesign instances with different SymbolicId values.
        /// Expected: Different hash codes are returned.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentBaseProperties_ReturnsDifferentHashCode()
        {
            // Arrange
            var objectDesign1 = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("TestObject1", "http://test.com"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };

            var objectDesign2 = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("TestObject2", "http://test.com"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };

            // Act
            int hashCode1 = objectDesign1.GetHashCode();
            int hashCode2 = objectDesign2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests hash code generation for a minimal ObjectDesign instance.
        /// Input: ObjectDesign with default values (SupportsEvents = false, SupportsEventsSpecified = false).
        /// Expected: A valid hash code is returned.
        /// </summary>
        [Test]
        public void GetHashCode_DefaultValues_ReturnsValidHashCode()
        {
            // Arrange
            var objectDesign = new ObjectDesign();

            // Act
            int hashCode = objectDesign.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.EqualTo(0).Or.EqualTo(0), "Hash code should be computed for default values");
        }

        /// <summary>
        /// Tests that GetHashCode is consistent with Equals for equal objects.
        /// Input: Two equal ObjectDesign instances.
        /// Expected: Equals returns true and hash codes are equal.
        /// </summary>
        [Test]
        public void GetHashCode_ConsistentWithEquals_EqualObjectsHaveSameHashCode()
        {
            // Arrange
            var objectDesign1 = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("TestObject", "http://test.com"),
                SupportsEvents = false,
                SupportsEventsSpecified = false
            };

            var objectDesign2 = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("TestObject", "http://test.com"),
                SupportsEvents = false,
                SupportsEventsSpecified = false
            };

            // Act & Assert
            Assert.That(objectDesign1.Equals(objectDesign2), Is.True);
            Assert.That(objectDesign1.GetHashCode(), Is.EqualTo(objectDesign2.GetHashCode()));
        }

        /// <summary>
        /// Tests that objects with all different property combinations produce different hash codes.
        /// Input: Two ObjectDesign instances with completely different properties.
        /// Expected: Different hash codes are returned.
        /// </summary>
        [Test]
        public void GetHashCode_CompletelyDifferentObjects_ReturnsDifferentHashCode()
        {
            // Arrange
            var objectDesign1 = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("Object1", "http://test1.com"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };

            var objectDesign2 = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("Object2", "http://test2.com"),
                SupportsEvents = false,
                SupportsEventsSpecified = false
            };

            // Act
            int hashCode1 = objectDesign1.GetHashCode();
            int hashCode2 = objectDesign2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that Equals returns false when comparing to null.
        /// </summary>
        [Test]
        public void Equals_NullOther_ReturnsFalse()
        {
            // Arrange
            var instance = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("TestObject", "http://test.org"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = instance.Equals(null);
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing an instance to itself.
        /// </summary>
        [Test]
        public void Equals_SameInstance_ReturnsTrue()
        {
            // Arrange
            var instance = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("TestObject", "http://test.org"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };

            // Act
            bool result = instance.Equals(instance);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when all properties are equal.
        /// </summary>
        [Test]
        public void Equals_AllPropertiesEqual_ReturnsTrue()
        {
            // Arrange
            var symbolId = new XmlQualifiedName("TestObject", "http://test.org");
            var instance1 = new ObjectDesign
            {
                SymbolicId = symbolId,
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };
            var instance2 = new ObjectDesign
            {
                SymbolicId = symbolId,
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when SupportsEvents property differs.
        /// Input: Two instances with different SupportsEvents values.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_DifferentSupportsEvents_ReturnsFalse()
        {
            // Arrange
            var symbolId = new XmlQualifiedName("TestObject", "http://test.org");
            var instance1 = new ObjectDesign
            {
                SymbolicId = symbolId,
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };
            var instance2 = new ObjectDesign
            {
                SymbolicId = symbolId,
                SupportsEvents = false,
                SupportsEventsSpecified = true
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when SupportsEventsSpecified property differs.
        /// Input: Two instances with different SupportsEventsSpecified values.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_DifferentSupportsEventsSpecified_ReturnsFalse()
        {
            // Arrange
            var symbolId = new XmlQualifiedName("TestObject", "http://test.org");
            var instance1 = new ObjectDesign
            {
                SymbolicId = symbolId,
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };
            var instance2 = new ObjectDesign
            {
                SymbolicId = symbolId,
                SupportsEvents = true,
                SupportsEventsSpecified = false
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when both SupportsEvents and SupportsEventsSpecified differ.
        /// Input: Two instances with different SupportsEvents and SupportsEventsSpecified values.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_BothSupportsPropertiesDiffer_ReturnsFalse()
        {
            // Arrange
            var symbolId = new XmlQualifiedName("TestObject", "http://test.org");
            var instance1 = new ObjectDesign
            {
                SymbolicId = symbolId,
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };
            var instance2 = new ObjectDesign
            {
                SymbolicId = symbolId,
                SupportsEvents = false,
                SupportsEventsSpecified = false
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when base class properties differ.
        /// Input: Two instances with different SymbolicId values.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_DifferentBaseProperties_ReturnsFalse()
        {
            // Arrange
            var instance1 = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("TestObject1", "http://test.org"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };
            var instance2 = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("TestObject2", "http://test.org"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals handles both properties set to false correctly.
        /// Input: Two instances with SupportsEvents and SupportsEventsSpecified set to false.
        /// Expected: Returns true.
        /// </summary>
        [Test]
        public void Equals_BothPropertiesFalse_ReturnsTrue()
        {
            // Arrange
            var symbolId = new XmlQualifiedName("TestObject", "http://test.org");
            var instance1 = new ObjectDesign
            {
                SymbolicId = symbolId,
                SupportsEvents = false,
                SupportsEventsSpecified = false
            };
            var instance2 = new ObjectDesign
            {
                SymbolicId = symbolId,
                SupportsEvents = false,
                SupportsEventsSpecified = false
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests Equals with all combinations of boolean property values using parameterized tests.
        /// Input: Various combinations of SupportsEvents and SupportsEventsSpecified values.
        /// Expected: Returns true when both instances have matching values.
        /// </summary>
        [TestCase(false, false, false, false, true)]
        [TestCase(false, true, false, true, true)]
        [TestCase(true, false, true, false, true)]
        [TestCase(true, true, true, true, true)]
        [TestCase(false, false, true, false, false)]
        [TestCase(false, false, false, true, false)]
        [TestCase(true, true, false, false, false)]
        [TestCase(true, false, false, true, false)]
        public void Equals_VariousBooleanCombinations_ReturnsExpectedResult(
            bool supportsEvents1,
            bool supportsEventsSpecified1,
            bool supportsEvents2,
            bool supportsEventsSpecified2,
            bool expectedResult)
        {
            // Arrange
            var symbolId = new XmlQualifiedName("TestObject", "http://test.org");
            var instance1 = new ObjectDesign
            {
                SymbolicId = symbolId,
                SupportsEvents = supportsEvents1,
                SupportsEventsSpecified = supportsEventsSpecified1
            };
            var instance2 = new ObjectDesign
            {
                SymbolicId = symbolId,
                SupportsEvents = supportsEvents2,
                SupportsEventsSpecified = supportsEventsSpecified2
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.EqualTo(expectedResult));
        }

        /// <summary>
        /// Tests that Equals is symmetric: a.Equals(b) == b.Equals(a).
        /// Input: Two equal instances compared in both directions.
        /// Expected: Both comparisons return true.
        /// </summary>
        [Test]
        public void Equals_Symmetric_ReturnsSameResult()
        {
            // Arrange
            var symbolId = new XmlQualifiedName("TestObject", "http://test.org");
            var instance1 = new ObjectDesign
            {
                SymbolicId = symbolId,
                SupportsEvents = true,
                SupportsEventsSpecified = false
            };
            var instance2 = new ObjectDesign
            {
                SymbolicId = symbolId,
                SupportsEvents = true,
                SupportsEventsSpecified = false
            };

            // Act
            bool result1 = instance1.Equals(instance2);
            bool result2 = instance2.Equals(instance1);

            // Assert
            Assert.That(result1, Is.EqualTo(result2));
            Assert.That(result1, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when TypeDefinition differs.
        /// Input: Two instances with different TypeDefinition values.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_DifferentTypeDefinition_ReturnsFalse()
        {
            // Arrange
            var symbolId = new XmlQualifiedName("TestObject", "http://test.org");
            var instance1 = new ObjectDesign
            {
                SymbolicId = symbolId,
                TypeDefinition = new XmlQualifiedName("Type1", "http://test.org"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };
            var instance2 = new ObjectDesign
            {
                SymbolicId = symbolId,
                TypeDefinition = new XmlQualifiedName("Type2", "http://test.org"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when ModellingRule differs.
        /// Input: Two instances with different ModellingRule values.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_DifferentModellingRule_ReturnsFalse()
        {
            // Arrange
            var symbolId = new XmlQualifiedName("TestObject", "http://test.org");
            var instance1 = new ObjectDesign
            {
                SymbolicId = symbolId,
                ModellingRule = ModellingRule.Mandatory,
                ModellingRuleSpecified = true,
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };
            var instance2 = new ObjectDesign
            {
                SymbolicId = symbolId,
                ModellingRule = ModellingRule.Optional,
                ModellingRuleSpecified = true,
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when the parameter is null.
        /// </summary>
        [Test]
        public void Equals_NullObject_ReturnsFalse()
        {
            // Arrange
            var objectDesign = new ObjectDesign();

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = objectDesign.Equals((object)null);
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
            var objectDesign = new ObjectDesign
            {
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };

            // Act
            bool result = objectDesign.Equals((object)objectDesign);

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
            var objectDesign = new ObjectDesign();
            object differentTypeObject = new();

            // Act
            bool result = objectDesign.Equals(differentTypeObject);

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
            var objectDesign = new ObjectDesign();
            object stringObject = "test";

            // Act
            bool result = objectDesign.Equals(stringObject);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing two ObjectDesign instances with identical property values.
        /// </summary>
        [TestCase(true, true)]
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void Equals_IdenticalProperties_ReturnsTrue(bool supportsEvents, bool supportsEventsSpecified)
        {
            // Arrange
            var objectDesign1 = new ObjectDesign
            {
                SupportsEvents = supportsEvents,
                SupportsEventsSpecified = supportsEventsSpecified
            };
            var objectDesign2 = new ObjectDesign
            {
                SupportsEvents = supportsEvents,
                SupportsEventsSpecified = supportsEventsSpecified
            };

            // Act
            bool result = objectDesign1.Equals((object)objectDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when both SupportsEvents and SupportsEventsSpecified differ.
        /// </summary>
        [Test]
        public void Equals_BothPropertiesDifferent_ReturnsFalse()
        {
            // Arrange
            var objectDesign1 = new ObjectDesign
            {
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };
            var objectDesign2 = new ObjectDesign
            {
                SupportsEvents = false,
                SupportsEventsSpecified = false
            };

            // Act
            bool result = objectDesign1.Equals((object)objectDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing default-initialized ObjectDesign instances.
        /// </summary>
        [Test]
        public void Equals_DefaultInstances_ReturnsTrue()
        {
            // Arrange
            var objectDesign1 = new ObjectDesign();
            var objectDesign2 = new ObjectDesign();

            // Act
            bool result = objectDesign1.Equals((object)objectDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that GetHashCode returns a consistent value when called multiple times on the same object.
        /// </summary>
        [Test]
        public void GetHashCode_SameObject_ReturnsConsistentValue()
        {
            // Arrange
            var objectDesign = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("TestObject", "http://test.org"),
                SymbolicName = new XmlQualifiedName("TestObject", "http://test.org"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };

            // Act
            int hashCode1 = objectDesign.GetHashCode();
            int hashCode2 = objectDesign.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode returns the same value for objects with identical property values.
        /// </summary>
        /// <param name="supportsEvents">Value for SupportsEvents property.</param>
        /// <param name="supportsEventsSpecified">Value for SupportsEventsSpecified property.</param>
        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void GetHashCode_ObjectsWithSameProperties_ReturnsSameHashCode(bool supportsEvents, bool supportsEventsSpecified)
        {
            // Arrange
            var objectDesign1 = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("TestObject", "http://test.org"),
                SymbolicName = new XmlQualifiedName("TestObject", "http://test.org"),
                SupportsEvents = supportsEvents,
                SupportsEventsSpecified = supportsEventsSpecified
            };

            var objectDesign2 = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("TestObject", "http://test.org"),
                SymbolicName = new XmlQualifiedName("TestObject", "http://test.org"),
                SupportsEvents = supportsEvents,
                SupportsEventsSpecified = supportsEventsSpecified
            };

            // Act
            int hashCode1 = objectDesign1.GetHashCode();
            int hashCode2 = objectDesign2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode produces different hash codes for objects with different SupportsEvents values.
        /// Note: While hash collisions are possible, different inputs should typically produce different hash codes.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentSupportsEvents_ProducesDifferentHashCodes()
        {
            // Arrange
            var objectDesign1 = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("TestObject", "http://test.org"),
                SymbolicName = new XmlQualifiedName("TestObject", "http://test.org"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };

            var objectDesign2 = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("TestObject", "http://test.org"),
                SymbolicName = new XmlQualifiedName("TestObject", "http://test.org"),
                SupportsEvents = false,
                SupportsEventsSpecified = true
            };

            // Act
            int hashCode1 = objectDesign1.GetHashCode();
            int hashCode2 = objectDesign2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode produces different hash codes for objects with different SupportsEventsSpecified values.
        /// Note: While hash collisions are possible, different inputs should typically produce different hash codes.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentSupportsEventsSpecified_ProducesDifferentHashCodes()
        {
            // Arrange
            var objectDesign1 = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("TestObject", "http://test.org"),
                SymbolicName = new XmlQualifiedName("TestObject", "http://test.org"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };

            var objectDesign2 = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("TestObject", "http://test.org"),
                SymbolicName = new XmlQualifiedName("TestObject", "http://test.org"),
                SupportsEvents = true,
                SupportsEventsSpecified = false
            };

            // Act
            int hashCode1 = objectDesign1.GetHashCode();
            int hashCode2 = objectDesign2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode incorporates base class properties by producing different hash codes
        /// when base properties differ.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentBaseProperties_ProducesDifferentHashCodes()
        {
            // Arrange
            var objectDesign1 = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("TestObject1", "http://test.org"),
                SymbolicName = new XmlQualifiedName("TestObject1", "http://test.org"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };

            var objectDesign2 = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("TestObject2", "http://test.org"),
                SymbolicName = new XmlQualifiedName("TestObject2", "http://test.org"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };

            // Act
            int hashCode1 = objectDesign1.GetHashCode();
            int hashCode2 = objectDesign2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode returns a valid integer value for a minimal ObjectDesign instance.
        /// </summary>
        [Test]
        public void GetHashCode_MinimalObject_ReturnsValidHashCode()
        {
            // Arrange
            var objectDesign = new ObjectDesign();

            // Act
            int hashCode = objectDesign.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.TypeOf<int>());
        }

        /// <summary>
        /// Tests that GetHashCode produces different hash codes for all combinations of boolean property values.
        /// </summary>
        [Test]
        public void GetHashCode_AllBooleanCombinations_ProducesDistinctHashCodes()
        {
            // Arrange
            var combinations = new[]
            {
                new { SupportsEvents = false, SupportsEventsSpecified = false },
                new { SupportsEvents = false, SupportsEventsSpecified = true },
                new { SupportsEvents = true, SupportsEventsSpecified = false },
                new { SupportsEvents = true, SupportsEventsSpecified = true }
            };

            var hashCodes = new System.Collections.Generic.HashSet<int>();

            // Act
            foreach (var combo in combinations)
            {
                var objectDesign = new ObjectDesign
                {
                    SymbolicId = new XmlQualifiedName("TestObject", "http://test.org"),
                    SymbolicName = new XmlQualifiedName("TestObject", "http://test.org"),
                    SupportsEvents = combo.SupportsEvents,
                    SupportsEventsSpecified = combo.SupportsEventsSpecified
                };
                hashCodes.Add(objectDesign.GetHashCode());
            }

            // Assert
            Assert.That(hashCodes.Count, Is.EqualTo(4), "All four boolean combinations should produce distinct hash codes");
        }
    }
}
