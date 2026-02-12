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
    /// Unit tests for the <see cref="ReferenceTypeDesign"/> class.
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ReferenceTypeDesignTests
    {
        /// <summary>
        /// Tests that GetHashCode returns consistent results when called multiple times on the same instance.
        /// </summary>
        [Test]
        public void GetHashCode_CalledMultipleTimes_ReturnsConsistentValue()
        {
            // Arrange
            var referenceType = new ReferenceTypeDesign
            {
                InverseName = new LocalizedText { Value = "TestInverse" },
                Symmetric = true,
                SymmetricSpecified = true
            };

            // Act
            int hashCode1 = referenceType.GetHashCode();
            int hashCode2 = referenceType.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests that two instances with identical property values produce the same hash code.
        /// </summary>
        [Test]
        public void GetHashCode_EqualInstances_ReturnsSameHashCode()
        {
            // Arrange
            var inverseName = new LocalizedText { Value = "TestInverse" };
            var referenceType1 = new ReferenceTypeDesign
            {
                InverseName = inverseName,
                Symmetric = true,
                SymmetricSpecified = true
            };
            var referenceType2 = new ReferenceTypeDesign
            {
                InverseName = inverseName,
                Symmetric = true,
                SymmetricSpecified = true
            };

            // Act
            int hashCode1 = referenceType1.GetHashCode();
            int hashCode2 = referenceType2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests that GetHashCode handles null InverseName property correctly.
        /// </summary>
        [Test]
        public void GetHashCode_NullInverseName_ReturnsValidHashCode()
        {
            // Arrange
            var referenceType = new ReferenceTypeDesign
            {
                InverseName = null,
                Symmetric = false,
                SymmetricSpecified = false
            };

            // Act
            int hashCode = referenceType.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero);
        }

        /// <summary>
        /// Tests that different InverseName values typically produce different hash codes.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentInverseName_ProducesDifferentHashCode()
        {
            // Arrange
            var referenceType1 = new ReferenceTypeDesign
            {
                InverseName = new LocalizedText { Value = "InverseName1" },
                Symmetric = true,
                SymmetricSpecified = true
            };
            var referenceType2 = new ReferenceTypeDesign
            {
                InverseName = new LocalizedText { Value = "InverseName2" },
                Symmetric = true,
                SymmetricSpecified = true
            };

            // Act
            int hashCode1 = referenceType1.GetHashCode();
            int hashCode2 = referenceType2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.Not.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests that different Symmetric values produce different hash codes.
        /// </summary>
        /// <param name="symmetric1">First Symmetric value.</param>
        /// <param name="symmetric2">Second Symmetric value.</param>
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void GetHashCode_DifferentSymmetric_ProducesDifferentHashCode(bool symmetric1, bool symmetric2)
        {
            // Arrange
            var inverseName = new LocalizedText { Value = "TestInverse" };
            var referenceType1 = new ReferenceTypeDesign
            {
                InverseName = inverseName,
                Symmetric = symmetric1,
                SymmetricSpecified = true
            };
            var referenceType2 = new ReferenceTypeDesign
            {
                InverseName = inverseName,
                Symmetric = symmetric2,
                SymmetricSpecified = true
            };

            // Act
            int hashCode1 = referenceType1.GetHashCode();
            int hashCode2 = referenceType2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.Not.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests that different SymmetricSpecified values produce different hash codes.
        /// </summary>
        /// <param name="symmetricSpecified1">First SymmetricSpecified value.</param>
        /// <param name="symmetricSpecified2">Second SymmetricSpecified value.</param>
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void GetHashCode_DifferentSymmetricSpecified_ProducesDifferentHashCode(bool symmetricSpecified1, bool symmetricSpecified2)
        {
            // Arrange
            var inverseName = new LocalizedText { Value = "TestInverse" };
            var referenceType1 = new ReferenceTypeDesign
            {
                InverseName = inverseName,
                Symmetric = true,
                SymmetricSpecified = symmetricSpecified1
            };
            var referenceType2 = new ReferenceTypeDesign
            {
                InverseName = inverseName,
                Symmetric = true,
                SymmetricSpecified = symmetricSpecified2
            };

            // Act
            int hashCode1 = referenceType1.GetHashCode();
            int hashCode2 = referenceType2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.Not.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests GetHashCode with all boolean combinations to ensure consistent behavior.
        /// </summary>
        /// <param name="symmetric">Value for Symmetric property.</param>
        /// <param name="symmetricSpecified">Value for SymmetricSpecified property.</param>
        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void GetHashCode_AllBooleanCombinations_ReturnsValidHashCode(bool symmetric, bool symmetricSpecified)
        {
            // Arrange
            var referenceType = new ReferenceTypeDesign
            {
                InverseName = new LocalizedText { Value = "Test" },
                Symmetric = symmetric,
                SymmetricSpecified = symmetricSpecified
            };

            // Act
            int hashCode = referenceType.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode with minimal properties set returns a valid hash code.
        /// </summary>
        [Test]
        public void GetHashCode_MinimalProperties_ReturnsValidHashCode()
        {
            // Arrange
            var referenceType = new ReferenceTypeDesign
            {
                InverseName = null,
                Symmetric = false,
                SymmetricSpecified = false
            };

            // Act
            int hashCode = referenceType.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode incorporates base class properties by verifying different base properties affect the hash.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentBaseProperties_ProducesDifferentHashCode()
        {
            // Arrange
            var referenceType1 = new ReferenceTypeDesign
            {
                SymbolicId = new XmlQualifiedName("Type1", "http://example.com"),
                InverseName = new LocalizedText { Value = "Test" },
                Symmetric = true,
                SymmetricSpecified = true
            };
            var referenceType2 = new ReferenceTypeDesign
            {
                SymbolicId = new XmlQualifiedName("Type2", "http://example.com"),
                InverseName = new LocalizedText { Value = "Test" },
                Symmetric = true,
                SymmetricSpecified = true
            };

            // Act
            int hashCode1 = referenceType1.GetHashCode();
            int hashCode2 = referenceType2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.Not.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests that GetHashCode handles empty LocalizedText InverseName correctly.
        /// </summary>
        [Test]
        public void GetHashCode_EmptyInverseName_ReturnsValidHashCode()
        {
            // Arrange
            var referenceType = new ReferenceTypeDesign
            {
                InverseName = new LocalizedText { Value = string.Empty },
                Symmetric = true,
                SymmetricSpecified = false
            };

            // Act
            int hashCode = referenceType.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode handles LocalizedText with special characters in InverseName correctly.
        /// </summary>
        [Test]
        public void GetHashCode_InverseNameWithSpecialCharacters_ReturnsValidHashCode()
        {
            // Arrange
            var referenceType = new ReferenceTypeDesign
            {
                InverseName = new LocalizedText { Value = "Test@#$%^&*()!~`<>?/\\" },
                Symmetric = false,
                SymmetricSpecified = true
            };

            // Act
            int hashCode = referenceType.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero);
        }

        /// <summary>
        /// Tests that Equals returns false when other is null.
        /// </summary>
        [Test]
        public void Equals_NullOther_ReturnsFalse()
        {
            // Arrange
            var referenceType = new ReferenceTypeDesign
            {
                SymbolicId = new XmlQualifiedName("Test"),
                Symmetric = true,
                SymmetricSpecified = true,
                InverseName = new LocalizedText { Key = "Key1", Value = "Value1" }
            };

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = referenceType.Equals(null);
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
            var referenceType = new ReferenceTypeDesign
            {
                SymbolicId = new XmlQualifiedName("Test"),
                Symmetric = true,
                SymmetricSpecified = true,
                InverseName = new LocalizedText { Key = "Key1", Value = "Value1" }
            };

            // Act
            bool result = referenceType.Equals(referenceType);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when all properties are equal.
        /// </summary>
        [Test]
        public void Equals_EqualInstances_ReturnsTrue()
        {
            // Arrange
            var referenceType1 = new ReferenceTypeDesign
            {
                SymbolicId = new XmlQualifiedName("Test"),
                Symmetric = true,
                SymmetricSpecified = true,
                InverseName = new LocalizedText { Key = "Key1", Value = "Value1" }
            };

            var referenceType2 = new ReferenceTypeDesign
            {
                SymbolicId = new XmlQualifiedName("Test"),
                Symmetric = true,
                SymmetricSpecified = true,
                InverseName = new LocalizedText { Key = "Key1", Value = "Value1" }
            };

            // Act
            bool result = referenceType1.Equals(referenceType2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when InverseName differs.
        /// </summary>
        [Test]
        public void Equals_DifferentInverseName_ReturnsFalse()
        {
            // Arrange
            var referenceType1 = new ReferenceTypeDesign
            {
                SymbolicId = new XmlQualifiedName("Test"),
                Symmetric = true,
                SymmetricSpecified = true,
                InverseName = new LocalizedText { Key = "Key1", Value = "Value1" }
            };

            var referenceType2 = new ReferenceTypeDesign
            {
                SymbolicId = new XmlQualifiedName("Test"),
                Symmetric = true,
                SymmetricSpecified = true,
                InverseName = new LocalizedText { Key = "Key2", Value = "Value2" }
            };

            // Act
            bool result = referenceType1.Equals(referenceType2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when one InverseName is null and the other is not.
        /// </summary>
        [Test]
        public void Equals_OneInverseNameNull_ReturnsFalse()
        {
            // Arrange
            var referenceType1 = new ReferenceTypeDesign
            {
                SymbolicId = new XmlQualifiedName("Test"),
                Symmetric = true,
                SymmetricSpecified = true,
                InverseName = new LocalizedText { Key = "Key1", Value = "Value1" }
            };

            var referenceType2 = new ReferenceTypeDesign
            {
                SymbolicId = new XmlQualifiedName("Test"),
                Symmetric = true,
                SymmetricSpecified = true,
                InverseName = null
            };

            // Act
            bool result = referenceType1.Equals(referenceType2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both InverseName properties are null.
        /// </summary>
        [Test]
        public void Equals_BothInverseNameNull_ReturnsTrue()
        {
            // Arrange
            var referenceType1 = new ReferenceTypeDesign
            {
                SymbolicId = new XmlQualifiedName("Test"),
                Symmetric = true,
                SymmetricSpecified = true,
                InverseName = null
            };

            var referenceType2 = new ReferenceTypeDesign
            {
                SymbolicId = new XmlQualifiedName("Test"),
                Symmetric = true,
                SymmetricSpecified = true,
                InverseName = null
            };

            // Act
            bool result = referenceType1.Equals(referenceType2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when Symmetric property differs.
        /// </summary>
        [Test]
        public void Equals_DifferentSymmetric_ReturnsFalse()
        {
            // Arrange
            var referenceType1 = new ReferenceTypeDesign
            {
                SymbolicId = new XmlQualifiedName("Test"),
                Symmetric = true,
                SymmetricSpecified = true,
                InverseName = new LocalizedText { Key = "Key1", Value = "Value1" }
            };

            var referenceType2 = new ReferenceTypeDesign
            {
                SymbolicId = new XmlQualifiedName("Test"),
                Symmetric = false,
                SymmetricSpecified = true,
                InverseName = new LocalizedText { Key = "Key1", Value = "Value1" }
            };

            // Act
            bool result = referenceType1.Equals(referenceType2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when SymmetricSpecified property differs.
        /// </summary>
        [Test]
        public void Equals_DifferentSymmetricSpecified_ReturnsFalse()
        {
            // Arrange
            var referenceType1 = new ReferenceTypeDesign
            {
                SymbolicId = new XmlQualifiedName("Test"),
                Symmetric = true,
                SymmetricSpecified = true,
                InverseName = new LocalizedText { Key = "Key1", Value = "Value1" }
            };

            var referenceType2 = new ReferenceTypeDesign
            {
                SymbolicId = new XmlQualifiedName("Test"),
                Symmetric = true,
                SymmetricSpecified = false,
                InverseName = new LocalizedText { Key = "Key1", Value = "Value1" }
            };

            // Act
            bool result = referenceType1.Equals(referenceType2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when base class properties differ.
        /// </summary>
        [Test]
        public void Equals_DifferentBaseClassProperties_ReturnsFalse()
        {
            // Arrange
            var referenceType1 = new ReferenceTypeDesign
            {
                SymbolicId = new XmlQualifiedName("Test1"),
                Symmetric = true,
                SymmetricSpecified = true,
                InverseName = new LocalizedText { Key = "Key1", Value = "Value1" }
            };

            var referenceType2 = new ReferenceTypeDesign
            {
                SymbolicId = new XmlQualifiedName("Test2"),
                Symmetric = true,
                SymmetricSpecified = true,
                InverseName = new LocalizedText { Key = "Key1", Value = "Value1" }
            };

            // Act
            bool result = referenceType1.Equals(referenceType2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both Symmetric and SymmetricSpecified are false.
        /// </summary>
        [Test]
        public void Equals_BothSymmetricAndSymmetricSpecifiedFalse_ReturnsTrue()
        {
            // Arrange
            var referenceType1 = new ReferenceTypeDesign
            {
                SymbolicId = new XmlQualifiedName("Test"),
                Symmetric = false,
                SymmetricSpecified = false,
                InverseName = new LocalizedText { Key = "Key1", Value = "Value1" }
            };

            var referenceType2 = new ReferenceTypeDesign
            {
                SymbolicId = new XmlQualifiedName("Test"),
                Symmetric = false,
                SymmetricSpecified = false,
                InverseName = new LocalizedText { Key = "Key1", Value = "Value1" }
            };

            // Act
            bool result = referenceType1.Equals(referenceType2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when multiple properties differ.
        /// </summary>
        [Test]
        public void Equals_MultiplePropertiesDiffer_ReturnsFalse()
        {
            // Arrange
            var referenceType1 = new ReferenceTypeDesign
            {
                SymbolicId = new XmlQualifiedName("Test"),
                Symmetric = true,
                SymmetricSpecified = true,
                InverseName = new LocalizedText { Key = "Key1", Value = "Value1" }
            };

            var referenceType2 = new ReferenceTypeDesign
            {
                SymbolicId = new XmlQualifiedName("Test"),
                Symmetric = false,
                SymmetricSpecified = false,
                InverseName = new LocalizedText { Key = "Key2", Value = "Value2" }
            };

            // Act
            bool result = referenceType1.Equals(referenceType2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true with default-initialized instances.
        /// </summary>
        [Test]
        public void Equals_DefaultInitializedInstances_ReturnsTrue()
        {
            // Arrange
            var referenceType1 = new ReferenceTypeDesign();
            var referenceType2 = new ReferenceTypeDesign();

            // Act
            bool result = referenceType1.Equals(referenceType2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when InverseName has different Key but same Value and DoNotIgnore.
        /// The implementation uses EqualityComparer default which checks all properties of LocalizedText.
        /// </summary>
        [Test]
        public void Equals_InverseNameDifferentKeyOnly_ReturnsFalse()
        {
            // Arrange
            var referenceType1 = new ReferenceTypeDesign
            {
                SymbolicId = new XmlQualifiedName("Test"),
                Symmetric = true,
                SymmetricSpecified = true,
                InverseName = new LocalizedText { Key = "Key1", Value = "Value1", DoNotIgnore = true }
            };

            var referenceType2 = new ReferenceTypeDesign
            {
                SymbolicId = new XmlQualifiedName("Test"),
                Symmetric = true,
                SymmetricSpecified = true,
                InverseName = new LocalizedText { Key = "Key2", Value = "Value1", DoNotIgnore = true }
            };

            // Act
            bool result = referenceType1.Equals(referenceType2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with null object.
        /// </summary>
        [Test]
        public void Equals_NullObject_ReturnsFalse()
        {
            // Arrange
            var referenceType = new ReferenceTypeDesign
            {
                SymbolicName = new XmlQualifiedName("TestReference", "http://test.org"),
                InverseName = new LocalizedText { Value = "InverseTest" },
                Symmetric = false,
                SymmetricSpecified = true
            };

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = referenceType.Equals(null);
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with an object of a different type (string).
        /// </summary>
        [Test]
        public void Equals_DifferentTypeString_ReturnsFalse()
        {
            // Arrange
            var referenceType = new ReferenceTypeDesign
            {
                SymbolicName = new XmlQualifiedName("TestReference", "http://test.org"),
                InverseName = new LocalizedText { Value = "InverseTest" },
                Symmetric = false,
                SymmetricSpecified = true
            };
            object differentType = "not a ReferenceTypeDesign";

            // Act
            bool result = referenceType.Equals(differentType);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with an object of a different type (integer).
        /// </summary>
        [Test]
        public void Equals_DifferentTypeInteger_ReturnsFalse()
        {
            // Arrange
            var referenceType = new ReferenceTypeDesign
            {
                SymbolicName = new XmlQualifiedName("TestReference", "http://test.org"),
                InverseName = new LocalizedText { Value = "InverseTest" },
                Symmetric = false,
                SymmetricSpecified = true
            };
            object differentType = 42;

            // Act
            bool result = referenceType.Equals(differentType);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with an unrelated type.
        /// </summary>
        [Test]
        public void Equals_UnrelatedType_ReturnsFalse()
        {
            // Arrange
            var referenceType = new ReferenceTypeDesign
            {
                SymbolicName = new XmlQualifiedName("TestReference", "http://test.org"),
                InverseName = new LocalizedText { Value = "InverseTest" },
                Symmetric = false,
                SymmetricSpecified = true
            };
            object differentType = new ObjectDesign();

            // Act
            bool result = referenceType.Equals(differentType);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing two ReferenceTypeDesign instances with different base type properties.
        /// </summary>
        [Test]
        public void Equals_DifferentBaseTypeProperty_ReturnsFalse()
        {
            // Arrange
            var referenceType1 = new ReferenceTypeDesign
            {
                SymbolicName = new XmlQualifiedName("TestReference1", "http://test.org"),
                InverseName = new LocalizedText { Value = "InverseTest" },
                Symmetric = true,
                SymmetricSpecified = true
            };

            var referenceType2 = new ReferenceTypeDesign
            {
                SymbolicName = new XmlQualifiedName("TestReference2", "http://test.org"),
                InverseName = new LocalizedText { Value = "InverseTest" },
                Symmetric = true,
                SymmetricSpecified = true
            };

            // Act
            bool result = referenceType1.Equals((object)referenceType2);

            // Assert
            Assert.That(result, Is.False);
        }
    }
}
