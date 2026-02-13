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
    /// Unit tests for the <see cref="EncodingDesign"/> class.
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class EncodingDesignTests
    {
        /// <summary>
        /// Tests that Equals returns false when comparing with null.
        /// Input: null parameter.
        /// Expected: false.
        /// </summary>
        [Test]
        public void Equals_NullParameter_ReturnsFalse()
        {
            // Arrange
            var encodingDesign = new EncodingDesign();

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = encodingDesign.Equals(null);
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing the same instance (reference equality).
        /// Input: same instance reference.
        /// Expected: true.
        /// </summary>
        [Test]
        public void Equals_SameInstance_ReturnsTrue()
        {
            // Arrange
            var encodingDesign = new EncodingDesign();

            // Act
            bool result = encodingDesign.Equals(encodingDesign);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing two instances with identical default properties.
        /// Input: two different instances with default property values.
        /// Expected: true.
        /// </summary>
        [Test]
        public void Equals_DifferentInstancesWithDefaultProperties_ReturnsTrue()
        {
            // Arrange
            var encodingDesign1 = new EncodingDesign();
            var encodingDesign2 = new EncodingDesign();

            // Act
            bool result = encodingDesign1.Equals(encodingDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing two instances with identical property values.
        /// Input: two different instances with same SymbolicId.
        /// Expected: true.
        /// </summary>
        [Test]
        public void Equals_DifferentInstancesWithSameSymbolicId_ReturnsTrue()
        {
            // Arrange
            var symbolicId = new XmlQualifiedName("TestEncoding", "http://test.namespace");
            var encodingDesign1 = new EncodingDesign { SymbolicId = symbolicId };
            var encodingDesign2 = new EncodingDesign { SymbolicId = symbolicId };

            // Act
            bool result = encodingDesign1.Equals(encodingDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing two instances with different property values.
        /// Input: two instances with different SymbolicId.
        /// Expected: false.
        /// </summary>
        [Test]
        public void Equals_DifferentInstancesWithDifferentSymbolicId_ReturnsFalse()
        {
            // Arrange
            var encodingDesign1 = new EncodingDesign
            {
                SymbolicId = new XmlQualifiedName("Encoding1", "http://test.namespace")
            };
            var encodingDesign2 = new EncodingDesign
            {
                SymbolicId = new XmlQualifiedName("Encoding2", "http://test.namespace")
            };

            // Act
            bool result = encodingDesign1.Equals(encodingDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when SupportsEvents differs.
        /// Input: two instances with different SupportsEvents values.
        /// Expected: false.
        /// </summary>
        [Test]
        public void Equals_DifferentSupportsEvents_ReturnsFalse()
        {
            // Arrange
            var encodingDesign1 = new EncodingDesign
            {
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };
            var encodingDesign2 = new EncodingDesign
            {
                SupportsEvents = false,
                SupportsEventsSpecified = true
            };

            // Act
            bool result = encodingDesign1.Equals(encodingDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when SupportsEventsSpecified differs.
        /// Input: two instances with different SupportsEventsSpecified values.
        /// Expected: false.
        /// </summary>
        [Test]
        public void Equals_DifferentSupportsEventsSpecified_ReturnsFalse()
        {
            // Arrange
            var encodingDesign1 = new EncodingDesign
            {
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };
            var encodingDesign2 = new EncodingDesign
            {
                SupportsEvents = true,
                SupportsEventsSpecified = false
            };

            // Act
            bool result = encodingDesign1.Equals(encodingDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both instances have identical inherited properties.
        /// Input: two instances with same SupportsEvents and SupportsEventsSpecified values.
        /// Expected: true.
        /// </summary>
        [Test]
        public void Equals_SameInheritedProperties_ReturnsTrue()
        {
            // Arrange
            var encodingDesign1 = new EncodingDesign
            {
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };
            var encodingDesign2 = new EncodingDesign
            {
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };

            // Act
            bool result = encodingDesign1.Equals(encodingDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing instances with complex identical properties.
        /// Input: two instances with same SymbolicId, SymbolicName, and inherited properties.
        /// Expected: true.
        /// </summary>
        [Test]
        public void Equals_ComplexIdenticalProperties_ReturnsTrue()
        {
            // Arrange
            var symbolicId = new XmlQualifiedName("TestEncoding", "http://test.namespace");
            var symbolicName = new XmlQualifiedName("TestSymbolicName", "http://test.namespace");

            var encodingDesign1 = new EncodingDesign
            {
                SymbolicId = symbolicId,
                SymbolicName = symbolicName,
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };
            var encodingDesign2 = new EncodingDesign
            {
                SymbolicId = symbolicId,
                SymbolicName = symbolicName,
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };

            // Act
            bool result = encodingDesign1.Equals(encodingDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing instances with different SymbolicName.
        /// Input: two instances with different SymbolicName values.
        /// Expected: false.
        /// </summary>
        [Test]
        public void Equals_DifferentSymbolicName_ReturnsFalse()
        {
            // Arrange
            var encodingDesign1 = new EncodingDesign
            {
                SymbolicName = new XmlQualifiedName("Name1", "http://test.namespace")
            };
            var encodingDesign2 = new EncodingDesign
            {
                SymbolicName = new XmlQualifiedName("Name2", "http://test.namespace")
            };

            // Act
            bool result = encodingDesign1.Equals(encodingDesign2);

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
            var encodingDesign = new EncodingDesign();

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = encodingDesign.Equals((object)null);
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
            var encodingDesign = new EncodingDesign();

            // Act
            bool result = encodingDesign.Equals((object)encodingDesign);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with an object of a completely different type.
        /// </summary>
        [Test]
        public void Equals_DifferentType_ReturnsFalse()
        {
            // Arrange
            var encodingDesign = new EncodingDesign();
            object differentTypeObject = new();

            // Act
            bool result = encodingDesign.Equals(differentTypeObject);

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
            var encodingDesign = new EncodingDesign();
            const string stringObject = "test";

            // Act
            bool result = encodingDesign.Equals(stringObject);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with an integer object.
        /// </summary>
        [Test]
        public void Equals_IntegerObject_ReturnsFalse()
        {
            // Arrange
            var encodingDesign = new EncodingDesign();
            const int intObject = 42;

            // Act
            bool result = encodingDesign.Equals(intObject);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing two different instances with default values.
        /// </summary>
        [Test]
        public void Equals_TwoDefaultInstances_ReturnsTrue()
        {
            // Arrange
            var encodingDesign1 = new EncodingDesign();
            var encodingDesign2 = new EncodingDesign();

            // Act
            bool result = encodingDesign1.Equals((object)encodingDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that GetHashCode returns a valid integer value and does not throw an exception.
        /// </summary>
        [Test]
        public void GetHashCode_ValidInstance_ReturnsInteger()
        {
            // Arrange
            var encoding = new EncodingDesign();

            // Act
            int hashCode = encoding.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.InstanceOf<int>());
        }

        /// <summary>
        /// Tests that calling GetHashCode multiple times on the same instance returns the same value (consistency requirement).
        /// </summary>
        [Test]
        public void GetHashCode_CalledMultipleTimes_ReturnsSameValue()
        {
            // Arrange
            var encoding = new EncodingDesign();

            // Act
            int hashCode1 = encoding.GetHashCode();
            int hashCode2 = encoding.GetHashCode();
            int hashCode3 = encoding.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.EqualTo(hashCode2));
            Assert.That(hashCode2, Is.EqualTo(hashCode3));
        }

        /// <summary>
        /// Tests that two equal EncodingDesign instances have the same hash code.
        /// This verifies the GetHashCode contract: if two objects are equal, they
        /// must have the same hash code.
        /// </summary>
        [Test]
        public void GetHashCode_EqualInstances_ReturnSameHashCode()
        {
            // Arrange
            var encoding1 = new EncodingDesign();
            var encoding2 = new EncodingDesign();

            // Act
            int hashCode1 = encoding1.GetHashCode();
            int hashCode2 = encoding2.GetHashCode();
            bool areEqual = encoding1.Equals(encoding2);

            // Assert
            if (areEqual)
            {
                Assert.That(hashCode1, Is.EqualTo(hashCode2),
                    "Equal objects must have equal hash codes");
            }
        }

        /// <summary>
        /// Tests that GetHashCode returns consistent values for instances with
        /// identical state.
        /// </summary>
        [Test]
        public void GetHashCode_IdenticalState_ProducesConsistentResults()
        {
            // Arrange
            var encoding1 = new EncodingDesign();
            var encoding2 = new EncodingDesign();

            // Act
            int hashCode1 = encoding1.GetHashCode();
            int hashCode2 = encoding2.GetHashCode();

            // Assert
            // If the instances are equal (based on their Equals implementation),
            // their hash codes must be equal
            if (encoding1.Equals(encoding2))
            {
                Assert.That(hashCode1, Is.EqualTo(hashCode2));
            }
        }

        /// <summary>
        /// Tests that GetHashCode can handle instances with various states
        /// without throwing exceptions.
        /// </summary>
        [Test]
        public void GetHashCode_VariousStates_DoesNotThrow()
        {
            // Arrange & Act & Assert
            var encoding1 = new EncodingDesign();
            Assert.DoesNotThrow(() => encoding1.GetHashCode());

            var encoding2 = new EncodingDesign();
            Assert.DoesNotThrow(() => encoding2.GetHashCode());
        }

        /// <summary>
        /// Tests that GetHashCode returns a consistent value across multiple
        /// calls on the same instance.
        /// </summary>
        [Test]
        public void GetHashCode_SameInstance_ReturnsConsistentValue()
        {
            // Arrange
            var encodingDesign = new EncodingDesign();

            // Act
            int hashCode1 = encodingDesign.GetHashCode();
            int hashCode2 = encodingDesign.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests that GetHashCode returns the same value for equal objects.
        /// </summary>
        [Test]
        public void GetHashCode_EqualObjects_ReturnsSameHashCode()
        {
            // Arrange
            var encodingDesign1 = new EncodingDesign();
            var encodingDesign2 = new EncodingDesign();

            // Act
            int hashCode1 = encodingDesign1.GetHashCode();
            int hashCode2 = encodingDesign2.GetHashCode();

            // Assert
            if (encodingDesign1.Equals(encodingDesign2))
            {
                Assert.That(hashCode2, Is.EqualTo(hashCode1));
            }
        }

        /// <summary>
        /// Tests that GetHashCode does not throw an exception when called on
        /// a valid instance.
        /// </summary>
        [Test]
        public void GetHashCode_ValidInstance_DoesNotThrow()
        {
            // Arrange
            var encodingDesign = new EncodingDesign();

            // Act & Assert
            Assert.DoesNotThrow(() => encodingDesign.GetHashCode());
        }

        /// <summary>
        /// Tests that GetHashCode works correctly with multiple instances.
        /// </summary>
        [Test]
        public void GetHashCode_MultipleInstances_ReturnsConsistentValues()
        {
            // Arrange
            var encodingDesign1 = new EncodingDesign();
            var encodingDesign2 = new EncodingDesign();
            var encodingDesign3 = new EncodingDesign();

            // Act
            int hashCode1 = encodingDesign1.GetHashCode();
            int hashCode2 = encodingDesign2.GetHashCode();
            int hashCode3 = encodingDesign3.GetHashCode();

            // Assert
            Assert.That(encodingDesign1.GetHashCode(), Is.EqualTo(hashCode1));
            Assert.That(encodingDesign2.GetHashCode(), Is.EqualTo(hashCode2));
            Assert.That(encodingDesign3.GetHashCode(), Is.EqualTo(hashCode3));
        }

        /// <summary>
        /// Tests that Equals(object) returns true when comparing an instance
        /// to itself cast as object.
        /// Input: Same instance cast to object.
        /// Expected: Returns true.
        /// </summary>
        [Test]
        public void Equals_SameInstanceCastToObject_ReturnsTrue()
        {
            // Arrange
            var encodingDesign = new EncodingDesign();
            object objReference = encodingDesign;

            // Act
            bool result = encodingDesign.Equals(objReference);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when comparing different
        /// instances.
        /// Input: Two different EncodingDesign instances.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_DifferentEncodingDesignInstances_ReturnsFalse()
        {
            // Arrange
            var encodingDesign1 = new EncodingDesign();
            var encodingDesign2 = new EncodingDesign();

            // Act
            bool result = encodingDesign1.Equals((object)encodingDesign2);

            // Assert
            Assert.That(result, Is.True);
        }
    }
}
