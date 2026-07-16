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
    /// Unit tests for <see cref="ViewDesign"/> class.
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ViewDesignTests
    {
        /// <summary>
        /// Tests that Equals returns false when comparing with null.
        /// </summary>
        [Test]
        public void Equals_NullParameter_ReturnsFalse()
        {
            // Arrange
            var instance = new ViewDesign
            {
                SupportsEvents = true,
                ContainsNoLoops = true
            };

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = instance.Equals(null);
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
            var instance = new ViewDesign
            {
                SupportsEvents = true,
                ContainsNoLoops = false
            };

            // Act
            bool result = instance.Equals(instance);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing two instances with identical property values.
        /// </summary>
        [Test]
        public void Equals_IdenticalInstances_ReturnsTrue()
        {
            // Arrange
            var instance1 = new ViewDesign
            {
                SupportsEvents = true,
                ContainsNoLoops = true,
                SymbolicId = new XmlQualifiedName("TestId", "http://test.org"),
                SymbolicName = new XmlQualifiedName("TestName", "http://test.org")
            };

            var instance2 = new ViewDesign
            {
                SupportsEvents = true,
                ContainsNoLoops = true,
                SymbolicId = new XmlQualifiedName("TestId", "http://test.org"),
                SymbolicName = new XmlQualifiedName("TestName", "http://test.org")
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when SupportsEvents differs between instances.
        /// </summary>
        [Test]
        public void Equals_DifferentSupportsEvents_ReturnsFalse()
        {
            // Arrange
            var instance1 = new ViewDesign
            {
                SupportsEvents = true,
                ContainsNoLoops = false,
                SymbolicId = new XmlQualifiedName("TestId", "http://test.org")
            };

            var instance2 = new ViewDesign
            {
                SupportsEvents = false,
                ContainsNoLoops = false,
                SymbolicId = new XmlQualifiedName("TestId", "http://test.org")
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when ContainsNoLoops differs between instances.
        /// </summary>
        [Test]
        public void Equals_DifferentContainsNoLoops_ReturnsFalse()
        {
            // Arrange
            var instance1 = new ViewDesign
            {
                SupportsEvents = true,
                ContainsNoLoops = true,
                SymbolicId = new XmlQualifiedName("TestId", "http://test.org")
            };

            var instance2 = new ViewDesign
            {
                SupportsEvents = true,
                ContainsNoLoops = false,
                SymbolicId = new XmlQualifiedName("TestId", "http://test.org")
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when base class properties differ.
        /// This tests the base.Equals(other) call in the implementation.
        /// </summary>
        [Test]
        public void Equals_DifferentBaseClassProperty_ReturnsFalse()
        {
            // Arrange
            var instance1 = new ViewDesign
            {
                SupportsEvents = true,
                ContainsNoLoops = true,
                SymbolicId = new XmlQualifiedName("TestId1", "http://test.org")
            };

            var instance2 = new ViewDesign
            {
                SupportsEvents = true,
                ContainsNoLoops = true,
                SymbolicId = new XmlQualifiedName("TestId2", "http://test.org")
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when multiple properties differ.
        /// </summary>
        [Test]
        public void Equals_MultipleDifferentProperties_ReturnsFalse()
        {
            // Arrange
            var instance1 = new ViewDesign
            {
                SupportsEvents = true,
                ContainsNoLoops = true,
                SymbolicId = new XmlQualifiedName("TestId1", "http://test.org")
            };

            var instance2 = new ViewDesign
            {
                SupportsEvents = false,
                ContainsNoLoops = false,
                SymbolicId = new XmlQualifiedName("TestId2", "http://test.org")
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both instances have default values (false for bool properties).
        /// </summary>
        [Test]
        public void Equals_DefaultValues_ReturnsTrue()
        {
            // Arrange
            var instance1 = new ViewDesign();
            var instance2 = new ViewDesign();

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals handles boundary case where SupportsEvents is true and ContainsNoLoops is false.
        /// </summary>
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        [TestCase(false, false)]
        public void Equals_VariousBooleanCombinations_ReturnsExpectedResult(bool supportsEvents, bool containsNoLoops)
        {
            // Arrange
            var instance1 = new ViewDesign
            {
                SupportsEvents = supportsEvents,
                ContainsNoLoops = containsNoLoops,
                SymbolicId = new XmlQualifiedName("TestId", "http://test.org")
            };

            var instance2 = new ViewDesign
            {
                SupportsEvents = supportsEvents,
                ContainsNoLoops = containsNoLoops,
                SymbolicId = new XmlQualifiedName("TestId", "http://test.org")
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when only one of two boolean properties differs.
        /// Verifies that all properties must match for equality.
        /// </summary>
        [TestCase(true, false, false, false)]
        [TestCase(true, true, true, false)]
        [TestCase(false, true, true, false)]
        public void Equals_OneBooleanPropertyDiffers_ReturnsFalse(
            bool instance1SupportsEvents,
            bool instance1ContainsNoLoops,
            bool instance2SupportsEvents,
            bool instance2ContainsNoLoops)
        {
            // Arrange
            var instance1 = new ViewDesign
            {
                SupportsEvents = instance1SupportsEvents,
                ContainsNoLoops = instance1ContainsNoLoops,
                SymbolicId = new XmlQualifiedName("TestId", "http://test.org")
            };

            var instance2 = new ViewDesign
            {
                SupportsEvents = instance2SupportsEvents,
                ContainsNoLoops = instance2ContainsNoLoops,
                SymbolicId = new XmlQualifiedName("TestId", "http://test.org")
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when passed a null object.
        /// </summary>
        [Test]
        public void EqualsObject_NullObject_ReturnsFalse()
        {
            // Arrange
            var viewDesign = new ViewDesign
            {
                SupportsEvents = true,
                ContainsNoLoops = false
            };

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = viewDesign.Equals(null);
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns true when comparing an instance to itself (reference equality).
        /// </summary>
        [Test]
        public void EqualsObject_SameInstance_ReturnsTrue()
        {
            // Arrange
            var viewDesign = new ViewDesign
            {
                SupportsEvents = true,
                ContainsNoLoops = true
            };

            // Act
            bool result = viewDesign.Equals((object)viewDesign);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals(object) returns true when comparing two ViewDesign instances with identical property values.
        /// </summary>
        /// <param name="supportsEvents">Value for SupportsEvents property</param>
        /// <param name="containsNoLoops">Value for ContainsNoLoops property</param>
        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void EqualsObject_EqualViewDesignInstances_ReturnsTrue(bool supportsEvents, bool containsNoLoops)
        {
            // Arrange
            var viewDesign1 = new ViewDesign
            {
                SupportsEvents = supportsEvents,
                ContainsNoLoops = containsNoLoops
            };
            var viewDesign2 = new ViewDesign
            {
                SupportsEvents = supportsEvents,
                ContainsNoLoops = containsNoLoops
            };

            // Act
            bool result = viewDesign1.Equals((object)viewDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when comparing ViewDesign instances with different SupportsEvents values.
        /// </summary>
        [Test]
        public void EqualsObject_DifferentSupportsEvents_ReturnsFalse()
        {
            // Arrange
            var viewDesign1 = new ViewDesign
            {
                SupportsEvents = true,
                ContainsNoLoops = false
            };
            var viewDesign2 = new ViewDesign
            {
                SupportsEvents = false,
                ContainsNoLoops = false
            };

            // Act
            bool result = viewDesign1.Equals((object)viewDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when comparing ViewDesign instances with different ContainsNoLoops values.
        /// </summary>
        [Test]
        public void EqualsObject_DifferentContainsNoLoops_ReturnsFalse()
        {
            // Arrange
            var viewDesign1 = new ViewDesign
            {
                SupportsEvents = true,
                ContainsNoLoops = true
            };
            var viewDesign2 = new ViewDesign
            {
                SupportsEvents = true,
                ContainsNoLoops = false
            };

            // Act
            bool result = viewDesign1.Equals((object)viewDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when comparing ViewDesign instances with both properties different.
        /// </summary>
        [Test]
        public void EqualsObject_BothPropertiesDifferent_ReturnsFalse()
        {
            // Arrange
            var viewDesign1 = new ViewDesign
            {
                SupportsEvents = true,
                ContainsNoLoops = true
            };
            var viewDesign2 = new ViewDesign
            {
                SupportsEvents = false,
                ContainsNoLoops = false
            };

            // Act
            bool result = viewDesign1.Equals((object)viewDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when passed a string object instead of ViewDesign.
        /// </summary>
        [Test]
        public void EqualsObject_StringObject_ReturnsFalse()
        {
            // Arrange
            var viewDesign = new ViewDesign
            {
                SupportsEvents = true,
                ContainsNoLoops = false
            };
            object otherObject = "some string";

            // Act
            bool result = viewDesign.Equals(otherObject);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when passed an integer object instead of ViewDesign.
        /// </summary>
        [Test]
        public void EqualsObject_IntegerObject_ReturnsFalse()
        {
            // Arrange
            var viewDesign = new ViewDesign
            {
                SupportsEvents = true,
                ContainsNoLoops = false
            };
            object otherObject = 42;

            // Act
            bool result = viewDesign.Equals(otherObject);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when passed an object of a different type in the same namespace.
        /// </summary>
        [Test]
        public void EqualsObject_DifferentTypeInSameNamespace_ReturnsFalse()
        {
            // Arrange
            var viewDesign = new ViewDesign
            {
                SupportsEvents = true,
                ContainsNoLoops = false
            };
            object otherObject = new ObjectDesign();

            // Act
            bool result = viewDesign.Equals(otherObject);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when passed an arbitrary object.
        /// </summary>
        [Test]
        public void EqualsObject_ArbitraryObject_ReturnsFalse()
        {
            // Arrange
            var viewDesign = new ViewDesign
            {
                SupportsEvents = true,
                ContainsNoLoops = false
            };
            object otherObject = new();

            // Act
            bool result = viewDesign.Equals(otherObject);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns true for default constructed ViewDesign instances.
        /// Both instances should have SupportsEvents = false and ContainsNoLoops = false by default.
        /// </summary>
        [Test]
        public void EqualsObject_DefaultConstructedInstances_ReturnsTrue()
        {
            // Arrange
            var viewDesign1 = new ViewDesign();
            var viewDesign2 = new ViewDesign();

            // Act
            bool result = viewDesign1.Equals((object)viewDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when comparing ViewDesign instances with different SymbolicId values.
        /// This tests that base class property differences are detected.
        /// </summary>
        [Test]
        public void EqualsObject_DifferentSymbolicId_ReturnsFalse()
        {
            // Arrange
            var viewDesign1 = new ViewDesign
            {
                SymbolicId = new XmlQualifiedName("View1", "http://example.com"),
                SupportsEvents = true,
                ContainsNoLoops = false
            };
            var viewDesign2 = new ViewDesign
            {
                SymbolicId = new XmlQualifiedName("View2", "http://example.com"),
                SupportsEvents = true,
                ContainsNoLoops = false
            };

            // Act
            bool result = viewDesign1.Equals((object)viewDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that GetHashCode returns consistent hash codes when called multiple times on the same object.
        /// Input: ViewDesign instance with default property values.
        /// Expected: Multiple calls return the same hash code.
        /// </summary>
        [Test]
        public void GetHashCode_CalledMultipleTimes_ReturnsConsistentValue()
        {
            // Arrange
            var viewDesign = new ViewDesign();

            // Act
            int hashCode1 = viewDesign.GetHashCode();
            int hashCode2 = viewDesign.GetHashCode();
            int hashCode3 = viewDesign.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.EqualTo(hashCode1));
            Assert.That(hashCode3, Is.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests that GetHashCode returns the same hash code for equal ViewDesign objects.
        /// Input: Two ViewDesign instances with identical property values.
        /// Expected: Both instances return the same hash code.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void GetHashCode_EqualObjects_ReturnsSameHashCode(bool supportsEvents, bool containsNoLoops)
        {
            // Arrange
            var viewDesign1 = new ViewDesign
            {
                SupportsEvents = supportsEvents,
                ContainsNoLoops = containsNoLoops
            };
            var viewDesign2 = new ViewDesign
            {
                SupportsEvents = supportsEvents,
                ContainsNoLoops = containsNoLoops
            };

            // Act
            int hashCode1 = viewDesign1.GetHashCode();
            int hashCode2 = viewDesign2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes for objects with different SupportsEvents values.
        /// Input: Two ViewDesign instances differing only in SupportsEvents.
        /// Expected: Hash codes are different (though not strictly guaranteed by contract, highly likely).
        /// </summary>
        [Test]
        public void GetHashCode_DifferentSupportsEvents_ReturnsDifferentHashCodes()
        {
            // Arrange
            var viewDesign1 = new ViewDesign
            {
                SupportsEvents = false,
                ContainsNoLoops = false
            };
            var viewDesign2 = new ViewDesign
            {
                SupportsEvents = true,
                ContainsNoLoops = false
            };

            // Act
            int hashCode1 = viewDesign1.GetHashCode();
            int hashCode2 = viewDesign2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.Not.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes for objects with different ContainsNoLoops values.
        /// Input: Two ViewDesign instances differing only in ContainsNoLoops.
        /// Expected: Hash codes are different (though not strictly guaranteed by contract, highly likely).
        /// </summary>
        [Test]
        public void GetHashCode_DifferentContainsNoLoops_ReturnsDifferentHashCodes()
        {
            // Arrange
            var viewDesign1 = new ViewDesign
            {
                SupportsEvents = false,
                ContainsNoLoops = false
            };
            var viewDesign2 = new ViewDesign
            {
                SupportsEvents = false,
                ContainsNoLoops = true
            };

            // Act
            int hashCode1 = viewDesign1.GetHashCode();
            int hashCode2 = viewDesign2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.Not.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes when both SupportsEvents and ContainsNoLoops differ.
        /// Input: Two ViewDesign instances with completely different property values.
        /// Expected: Hash codes are different.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentProperties_ReturnsDifferentHashCodes()
        {
            // Arrange
            var viewDesign1 = new ViewDesign
            {
                SupportsEvents = false,
                ContainsNoLoops = false
            };
            var viewDesign2 = new ViewDesign
            {
                SupportsEvents = true,
                ContainsNoLoops = true
            };

            // Act
            int hashCode1 = viewDesign1.GetHashCode();
            int hashCode2 = viewDesign2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.Not.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests that GetHashCode includes base class properties in hash code calculation.
        /// Input: Two ViewDesign instances with different base class properties.
        /// Expected: Hash codes are different because base.GetHashCode() is included.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentBaseProperties_ReturnsDifferentHashCodes()
        {
            // Arrange
            var viewDesign1 = new ViewDesign
            {
                SupportsEvents = false,
                ContainsNoLoops = false,
                SymbolicName = new XmlQualifiedName("View1", "http://test.org")
            };
            var viewDesign2 = new ViewDesign
            {
                SupportsEvents = false,
                ContainsNoLoops = false,
                SymbolicName = new XmlQualifiedName("View2", "http://test.org")
            };

            // Act
            int hashCode1 = viewDesign1.GetHashCode();
            int hashCode2 = viewDesign2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.Not.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests that GetHashCode does not throw exceptions for default-initialized ViewDesign instances.
        /// Input: Newly created ViewDesign with default values.
        /// Expected: GetHashCode executes successfully and returns a valid hash code.
        /// </summary>
        [Test]
        public void GetHashCode_DefaultInitialization_ExecutesSuccessfully()
        {
            // Arrange
            var viewDesign = new ViewDesign();

            // Act
            int hashCode = viewDesign.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.TypeOf<int>());
        }

        /// <summary>
        /// Tests that GetHashCode satisfies the equality contract: equal objects have equal hash codes.
        /// Input: Two ViewDesign instances that are equal according to Equals method.
        /// Expected: Hash codes are identical.
        /// </summary>
        [Test]
        public void GetHashCode_EqualObjectsByEqualsContract_HaveEqualHashCodes()
        {
            // Arrange
            var viewDesign1 = new ViewDesign
            {
                SupportsEvents = true,
                ContainsNoLoops = true,
                SymbolicName = new XmlQualifiedName("TestView", "http://test.org")
            };
            var viewDesign2 = new ViewDesign
            {
                SupportsEvents = true,
                ContainsNoLoops = true,
                SymbolicName = new XmlQualifiedName("TestView", "http://test.org")
            };

            // Act
            bool areEqual = viewDesign1.Equals(viewDesign2);
            int hashCode1 = viewDesign1.GetHashCode();
            int hashCode2 = viewDesign2.GetHashCode();

            // Assert
            Assert.That(areEqual, Is.True, "Objects should be equal");
            Assert.That(hashCode2, Is.EqualTo(hashCode1), "Equal objects must have equal hash codes");
        }
    }
}
