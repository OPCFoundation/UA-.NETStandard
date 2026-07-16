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
    /// Unit tests for ObjectTypeDesign class
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ObjectTypeDesignTests
    {
        /// <summary>
        /// Tests that Equals returns false when comparing to null.
        /// Input: null ObjectTypeDesign
        /// Expected: false
        /// </summary>
        [Test]
        public void Equals_NullObjectTypeDesign_ReturnsFalse()
        {
            // Arrange
            var objectTypeDesign = new ObjectTypeDesign();

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = objectTypeDesign.Equals(null);
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing an instance to itself.
        /// Input: Same instance
        /// Expected: true
        /// </summary>
        [Test]
        public void Equals_SameInstance_ReturnsTrue()
        {
            // Arrange
            var objectTypeDesign = new ObjectTypeDesign
            {
                SymbolicName = new XmlQualifiedName("TestType", "http://test.org"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };

            // Act
            bool result = objectTypeDesign.Equals(objectTypeDesign);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when all properties are equal.
        /// Input: Two ObjectTypeDesign instances with identical property values
        /// Expected: true
        /// </summary>
        [Test]
        public void Equals_EqualObjects_ReturnsTrue()
        {
            // Arrange
            var objectTypeDesign1 = new ObjectTypeDesign
            {
                SymbolicName = new XmlQualifiedName("TestType", "http://test.org"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };
            var objectTypeDesign2 = new ObjectTypeDesign
            {
                SymbolicName = new XmlQualifiedName("TestType", "http://test.org"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };

            // Act
            bool result = objectTypeDesign1.Equals(objectTypeDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when SupportsEvents differs.
        /// Input: Two ObjectTypeDesign instances where SupportsEvents differs
        /// Expected: false
        /// </summary>
        [Test]
        public void Equals_DifferentSupportsEvents_ReturnsFalse()
        {
            // Arrange
            var objectTypeDesign1 = new ObjectTypeDesign
            {
                SymbolicName = new XmlQualifiedName("TestType", "http://test.org"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };
            var objectTypeDesign2 = new ObjectTypeDesign
            {
                SymbolicName = new XmlQualifiedName("TestType", "http://test.org"),
                SupportsEvents = false,
                SupportsEventsSpecified = true
            };

            // Act
            bool result = objectTypeDesign1.Equals(objectTypeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when SupportsEventsSpecified differs.
        /// Input: Two ObjectTypeDesign instances where SupportsEventsSpecified differs
        /// Expected: false
        /// </summary>
        [Test]
        public void Equals_DifferentSupportsEventsSpecified_ReturnsFalse()
        {
            // Arrange
            var objectTypeDesign1 = new ObjectTypeDesign
            {
                SymbolicName = new XmlQualifiedName("TestType", "http://test.org"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };
            var objectTypeDesign2 = new ObjectTypeDesign
            {
                SymbolicName = new XmlQualifiedName("TestType", "http://test.org"),
                SupportsEvents = true,
                SupportsEventsSpecified = false
            };

            // Act
            bool result = objectTypeDesign1.Equals(objectTypeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when base TypeDesign properties differ.
        /// Input: Two ObjectTypeDesign instances where SymbolicName differs
        /// Expected: false
        /// </summary>
        [Test]
        public void Equals_DifferentBaseProperties_ReturnsFalse()
        {
            // Arrange
            var objectTypeDesign1 = new ObjectTypeDesign
            {
                SymbolicName = new XmlQualifiedName("TestType1", "http://test.org"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };
            var objectTypeDesign2 = new ObjectTypeDesign
            {
                SymbolicName = new XmlQualifiedName("TestType2", "http://test.org"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };

            // Act
            bool result = objectTypeDesign1.Equals(objectTypeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both SupportsEvents and SupportsEventsSpecified are false.
        /// Input: Two ObjectTypeDesign instances with default boolean values
        /// Expected: true
        /// </summary>
        [Test]
        public void Equals_BothWithFalseBooleans_ReturnsTrue()
        {
            // Arrange
            var objectTypeDesign1 = new ObjectTypeDesign
            {
                SymbolicName = new XmlQualifiedName("TestType", "http://test.org"),
                SupportsEvents = false,
                SupportsEventsSpecified = false
            };
            var objectTypeDesign2 = new ObjectTypeDesign
            {
                SymbolicName = new XmlQualifiedName("TestType", "http://test.org"),
                SupportsEvents = false,
                SupportsEventsSpecified = false
            };

            // Act
            bool result = objectTypeDesign1.Equals(objectTypeDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when both SupportsEvents and SupportsEventsSpecified differ.
        /// Input: Two ObjectTypeDesign instances where both boolean properties differ
        /// Expected: false
        /// </summary>
        [Test]
        public void Equals_BothBooleansDifferent_ReturnsFalse()
        {
            // Arrange
            var objectTypeDesign1 = new ObjectTypeDesign
            {
                SymbolicName = new XmlQualifiedName("TestType", "http://test.org"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };
            var objectTypeDesign2 = new ObjectTypeDesign
            {
                SymbolicName = new XmlQualifiedName("TestType", "http://test.org"),
                SupportsEvents = false,
                SupportsEventsSpecified = false
            };

            // Act
            bool result = objectTypeDesign1.Equals(objectTypeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing objects with default properties.
        /// Input: Two newly created ObjectTypeDesign instances
        /// Expected: true
        /// </summary>
        [Test]
        public void Equals_DefaultObjects_ReturnsTrue()
        {
            // Arrange
            var objectTypeDesign1 = new ObjectTypeDesign();
            var objectTypeDesign2 = new ObjectTypeDesign();

            // Act
            bool result = objectTypeDesign1.Equals(objectTypeDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when ClassName differs in base type.
        /// Input: Two ObjectTypeDesign instances where ClassName differs
        /// Expected: false
        /// </summary>
        [Test]
        public void Equals_DifferentClassName_ReturnsFalse()
        {
            // Arrange
            var objectTypeDesign1 = new ObjectTypeDesign
            {
                SymbolicName = new XmlQualifiedName("TestType", "http://test.org"),
                ClassName = "Class1",
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };
            var objectTypeDesign2 = new ObjectTypeDesign
            {
                SymbolicName = new XmlQualifiedName("TestType", "http://test.org"),
                ClassName = "Class2",
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };

            // Act
            bool result = objectTypeDesign1.Equals(objectTypeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when BaseType differs.
        /// Input: Two ObjectTypeDesign instances where BaseType differs
        /// Expected: false
        /// </summary>
        [Test]
        public void Equals_DifferentBaseType_ReturnsFalse()
        {
            // Arrange
            var objectTypeDesign1 = new ObjectTypeDesign
            {
                SymbolicName = new XmlQualifiedName("TestType", "http://test.org"),
                BaseType = new XmlQualifiedName("BaseType1", "http://test.org"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };
            var objectTypeDesign2 = new ObjectTypeDesign
            {
                SymbolicName = new XmlQualifiedName("TestType", "http://test.org"),
                BaseType = new XmlQualifiedName("BaseType2", "http://test.org"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };

            // Act
            bool result = objectTypeDesign1.Equals(objectTypeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when IsAbstract differs in base type.
        /// Input: Two ObjectTypeDesign instances where IsAbstract differs
        /// Expected: false
        /// </summary>
        [Test]
        public void Equals_DifferentIsAbstract_ReturnsFalse()
        {
            // Arrange
            var objectTypeDesign1 = new ObjectTypeDesign
            {
                SymbolicName = new XmlQualifiedName("TestType", "http://test.org"),
                IsAbstract = true,
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };
            var objectTypeDesign2 = new ObjectTypeDesign
            {
                SymbolicName = new XmlQualifiedName("TestType", "http://test.org"),
                IsAbstract = false,
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };

            // Act
            bool result = objectTypeDesign1.Equals(objectTypeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when the parameter is null.
        /// </summary>
        [Test]
        public void Equals_NullObject_ReturnsFalse()
        {
            // Arrange
            var objectTypeDesign = new ObjectTypeDesign
            {
                SymbolicId = new XmlQualifiedName("TestType", "http://test.org"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = objectTypeDesign.Equals((object)null);
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when the parameter is a different type.
        /// </summary>
        [Test]
        public void Equals_DifferentType_ReturnsFalse()
        {
            // Arrange
            var objectTypeDesign = new ObjectTypeDesign
            {
                SymbolicId = new XmlQualifiedName("TestType", "http://test.org"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };
            object differentType = "not an ObjectTypeDesign";

            // Act
            bool result = objectTypeDesign.Equals(differentType);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when the parameter is a different type (integer).
        /// </summary>
        [Test]
        public void Equals_IntegerType_ReturnsFalse()
        {
            // Arrange
            var objectTypeDesign = new ObjectTypeDesign
            {
                SymbolicId = new XmlQualifiedName("TestType", "http://test.org"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };
            object differentType = 42;

            // Act
            bool result = objectTypeDesign.Equals(differentType);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns true when comparing two equal ObjectTypeDesign instances.
        /// </summary>
        [Test]
        public void Equals_EqualObjectTypeDesign_ReturnsTrue()
        {
            // Arrange
            var objectTypeDesign1 = new ObjectTypeDesign
            {
                SymbolicId = new XmlQualifiedName("TestType", "http://test.org"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };
            var objectTypeDesign2 = new ObjectTypeDesign
            {
                SymbolicId = new XmlQualifiedName("TestType", "http://test.org"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };

            // Act
            bool result = objectTypeDesign1.Equals((object)objectTypeDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when both SupportsEvents and SupportsEventsSpecified differ.
        /// </summary>
        [Test]
        public void Equals_BothPropertiesDifferent_ReturnsFalse()
        {
            // Arrange
            var objectTypeDesign1 = new ObjectTypeDesign
            {
                SymbolicId = new XmlQualifiedName("TestType", "http://test.org"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };
            var objectTypeDesign2 = new ObjectTypeDesign
            {
                SymbolicId = new XmlQualifiedName("TestType", "http://test.org"),
                SupportsEvents = false,
                SupportsEventsSpecified = false
            };

            // Act
            bool result = objectTypeDesign1.Equals((object)objectTypeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when comparing to a different NodeDesign subclass.
        /// </summary>
        [Test]
        public void Equals_DifferentNodeDesignSubclass_ReturnsFalse()
        {
            // Arrange
            var objectTypeDesign = new ObjectTypeDesign
            {
                SymbolicId = new XmlQualifiedName("TestType", "http://test.org"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };
            var objectDesign = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("TestType", "http://test.org"),
                SupportsEvents = true,
                SupportsEventsSpecified = true
            };

            // Act
            bool result = objectTypeDesign.Equals((object)objectDesign);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns true when both instances have default property values.
        /// </summary>
        [Test]
        public void Equals_DefaultValues_ReturnsTrue()
        {
            // Arrange
            var objectTypeDesign1 = new ObjectTypeDesign();
            var objectTypeDesign2 = new ObjectTypeDesign();

            // Act
            bool result = objectTypeDesign1.Equals((object)objectTypeDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals(object) handles SupportsEvents boundary values correctly.
        /// </summary>
        /// <param name="supportsEvents1">First instance SupportsEvents value</param>
        /// <param name="supportsEvents2">Second instance SupportsEvents value</param>
        /// <param name="expected">Expected equality result</param>
        [TestCase(true, true, true)]
        [TestCase(false, false, true)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        public void Equals_SupportsEventsBoundaryValues_ReturnsExpected(bool supportsEvents1, bool supportsEvents2, bool expected)
        {
            // Arrange
            var objectTypeDesign1 = new ObjectTypeDesign
            {
                SymbolicId = new XmlQualifiedName("TestType", "http://test.org"),
                SupportsEvents = supportsEvents1,
                SupportsEventsSpecified = true
            };
            var objectTypeDesign2 = new ObjectTypeDesign
            {
                SymbolicId = new XmlQualifiedName("TestType", "http://test.org"),
                SupportsEvents = supportsEvents2,
                SupportsEventsSpecified = true
            };

            // Act
            bool result = objectTypeDesign1.Equals((object)objectTypeDesign2);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that Equals(object) handles SupportsEventsSpecified boundary values correctly.
        /// </summary>
        /// <param name="specified1">First instance SupportsEventsSpecified value</param>
        /// <param name="specified2">Second instance SupportsEventsSpecified value</param>
        /// <param name="expected">Expected equality result</param>
        [TestCase(true, true, true)]
        [TestCase(false, false, true)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        public void Equals_SupportsEventsSpecifiedBoundaryValues_ReturnsExpected(bool specified1, bool specified2, bool expected)
        {
            // Arrange
            var objectTypeDesign1 = new ObjectTypeDesign
            {
                SymbolicId = new XmlQualifiedName("TestType", "http://test.org"),
                SupportsEvents = true,
                SupportsEventsSpecified = specified1
            };
            var objectTypeDesign2 = new ObjectTypeDesign
            {
                SymbolicId = new XmlQualifiedName("TestType", "http://test.org"),
                SupportsEvents = true,
                SupportsEventsSpecified = specified2
            };

            // Act
            bool result = objectTypeDesign1.Equals((object)objectTypeDesign2);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }
    }
}
