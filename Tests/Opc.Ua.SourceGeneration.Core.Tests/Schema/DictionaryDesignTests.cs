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
    /// Unit tests for DictionaryDesign.GetHashCode method.
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class DictionaryDesignTests
    {
        /// <summary>
        /// Tests that GetHashCode returns consistent value when called multiple times
        /// on the same object.
        /// Input: Same DictionaryDesign instance.
        /// Expected: Hash code remains the same across multiple calls.
        /// </summary>
        [Test]
        public void GetHashCode_CalledMultipleTimes_ReturnsConsistentValue()
        {
            // Arrange
            var design = new DictionaryDesign
            {
                EncodingName = new XmlQualifiedName("TestEncoding", "http://test.namespace")
            };

            // Act
            int hashCode1 = design.GetHashCode();
            int hashCode2 = design.GetHashCode();
            int hashCode3 = design.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.EqualTo(hashCode2));
            Assert.That(hashCode2, Is.EqualTo(hashCode3));
        }

        /// <summary>
        /// Tests that GetHashCode returns the same value for equal objects.
        /// Input: Two DictionaryDesign objects with identical EncodingName.
        /// Expected: Both objects return the same hash code.
        /// </summary>
        [Test]
        public void GetHashCode_EqualObjects_ReturnsSameHashCode()
        {
            // Arrange
            var design1 = new DictionaryDesign
            {
                EncodingName = new XmlQualifiedName("TestEncoding", "http://test.namespace")
            };
            var design2 = new DictionaryDesign
            {
                EncodingName = new XmlQualifiedName("TestEncoding", "http://test.namespace")
            };

            // Act
            int hashCode1 = design1.GetHashCode();
            int hashCode2 = design2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode handles null EncodingName property.
        /// Input: DictionaryDesign with null EncodingName.
        /// Expected: Method does not throw and returns a valid hash code.
        /// </summary>
        [Test]
        public void GetHashCode_NullEncodingName_DoesNotThrow()
        {
            // Arrange
            var design = new DictionaryDesign
            {
                EncodingName = null
            };

            // Act & Assert
            Assert.DoesNotThrow(() => design.GetHashCode());
        }

        /// <summary>
        /// Tests that GetHashCode returns different values for objects with different EncodingName.
        /// Input: Two DictionaryDesign objects with different EncodingName values.
        /// Expected: Different hash codes (not guaranteed but highly likely).
        /// </summary>
        [Test]
        public void GetHashCode_DifferentEncodingName_ReturnsDifferentHashCodes()
        {
            // Arrange
            var design1 = new DictionaryDesign
            {
                EncodingName = new XmlQualifiedName("TestEncoding1", "http://test.namespace")
            };
            var design2 = new DictionaryDesign
            {
                EncodingName = new XmlQualifiedName("TestEncoding2", "http://test.namespace")
            };

            // Act
            int hashCode1 = design1.GetHashCode();
            int hashCode2 = design2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different values for objects with different namespace in EncodingName.
        /// Input: Two DictionaryDesign objects with same local name but different namespace.
        /// Expected: Different hash codes (not guaranteed but highly likely).
        /// </summary>
        [Test]
        public void GetHashCode_DifferentNamespace_ReturnsDifferentHashCodes()
        {
            // Arrange
            var design1 = new DictionaryDesign
            {
                EncodingName = new XmlQualifiedName("TestEncoding", "http://test.namespace1")
            };
            var design2 = new DictionaryDesign
            {
                EncodingName = new XmlQualifiedName("TestEncoding", "http://test.namespace2")
            };

            // Act
            int hashCode1 = design1.GetHashCode();
            int hashCode2 = design2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode handles empty string values in XmlQualifiedName.
        /// Input: DictionaryDesign with empty string for name and namespace.
        /// Expected: Method does not throw and returns a valid hash code.
        /// </summary>
        [Test]
        public void GetHashCode_EmptyStringsInEncodingName_DoesNotThrow()
        {
            // Arrange
            var design = new DictionaryDesign
            {
                EncodingName = new XmlQualifiedName(string.Empty, string.Empty)
            };

            // Act & Assert
            Assert.DoesNotThrow(() => design.GetHashCode());
        }

        /// <summary>
        /// Tests that GetHashCode consistency with Equals method.
        /// Input: Two equal DictionaryDesign objects.
        /// Expected: Equal objects must have equal hash codes.
        /// </summary>
        [Test]
        public void GetHashCode_ConsistencyWithEquals_EqualObjectsHaveEqualHashCodes()
        {
            // Arrange
            var design1 = new DictionaryDesign
            {
                EncodingName = new XmlQualifiedName("TestEncoding", "http://test.namespace")
            };
            var design2 = new DictionaryDesign
            {
                EncodingName = new XmlQualifiedName("TestEncoding", "http://test.namespace")
            };

            // Act
            bool areEqual = design1.Equals(design2);
            int hashCode1 = design1.GetHashCode();
            int hashCode2 = design2.GetHashCode();

            // Assert
            Assert.That(areEqual, Is.True);
            Assert.That(hashCode1, Is.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different values for one null and
        /// one non-null EncodingName.
        /// Input: Two DictionaryDesign objects, one with null EncodingName,
        /// one with non-null.
        /// Expected: Different hash codes.
        /// </summary>
        [Test]
        public void GetHashCode_NullVsNonNullEncodingName_ReturnsDifferentHashCodes()
        {
            // Arrange
            var design1 = new DictionaryDesign
            {
                EncodingName = null
            };
            var design2 = new DictionaryDesign
            {
                EncodingName = new XmlQualifiedName("TestEncoding", "http://test.namespace")
            };

            // Act
            int hashCode1 = design1.GetHashCode();
            int hashCode2 = design2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode handles special characters in EncodingName.
        /// Input: DictionaryDesign with special characters in name and namespace.
        /// Expected: Method does not throw and returns a valid hash code.
        /// </summary>
        [Test]
        public void GetHashCode_SpecialCharactersInEncodingName_DoesNotThrow()
        {
            // Arrange
            var design = new DictionaryDesign
            {
                EncodingName = new XmlQualifiedName("Test!@#$%Encoding", "http://test.namespace/<>?")
            };

            // Act & Assert
            Assert.DoesNotThrow(() => design.GetHashCode());
        }

        /// <summary>
        /// Tests that GetHashCode handles very long strings in EncodingName.
        /// Input: DictionaryDesign with very long strings for name and namespace.
        /// Expected: Method does not throw and returns a valid hash code.
        /// </summary>
        [Test]
        public void GetHashCode_VeryLongStringsInEncodingName_DoesNotThrow()
        {
            // Arrange
            string longString = new('a', 10000);
            var design = new DictionaryDesign
            {
                EncodingName = new XmlQualifiedName(longString, longString)
            };

            // Act & Assert
            Assert.DoesNotThrow(() => design.GetHashCode());
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with null.
        /// </summary>
        [Test]
        public void Equals_NullOther_ReturnsFalse()
        {
            // Arrange
            var instance = new DictionaryDesign
            {
                EncodingName = new XmlQualifiedName("TestEncoding", "http://test.org")
            };

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = instance.Equals(null);
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing the same instance with itself.
        /// </summary>
        [Test]
        public void Equals_SameInstance_ReturnsTrue()
        {
            // Arrange
            var instance = new DictionaryDesign
            {
                EncodingName = new XmlQualifiedName("TestEncoding", "http://test.org")
            };

            // Act
            bool result = instance.Equals(instance);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing two instances with equal properties.
        /// </summary>
        [Test]
        public void Equals_EqualInstances_ReturnsTrue()
        {
            // Arrange
            var encodingName = new XmlQualifiedName("TestEncoding", "http://test.org");
            var instance1 = new DictionaryDesign
            {
                SymbolicName = new XmlQualifiedName("Dictionary1", "http://test.org"),
                EncodingName = encodingName
            };
            var instance2 = new DictionaryDesign
            {
                SymbolicName = new XmlQualifiedName("Dictionary1", "http://test.org"),
                EncodingName = encodingName
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when both instances have different but
        /// equivalent EncodingName.
        /// </summary>
        [Test]
        public void Equals_DifferentButEquivalentEncodingName_ReturnsTrue()
        {
            // Arrange
            var instance1 = new DictionaryDesign
            {
                SymbolicName = new XmlQualifiedName("Dictionary1", "http://test.org"),
                EncodingName = new XmlQualifiedName("TestEncoding", "http://test.org")
            };
            var instance2 = new DictionaryDesign
            {
                SymbolicName = new XmlQualifiedName("Dictionary1", "http://test.org"),
                EncodingName = new XmlQualifiedName("TestEncoding", "http://test.org")
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when EncodingName differs.
        /// </summary>
        [Test]
        public void Equals_DifferentEncodingName_ReturnsFalse()
        {
            // Arrange
            var instance1 = new DictionaryDesign
            {
                SymbolicName = new XmlQualifiedName("Dictionary1", "http://test.org"),
                EncodingName = new XmlQualifiedName("Encoding1", "http://test.org")
            };
            var instance2 = new DictionaryDesign
            {
                SymbolicName = new XmlQualifiedName("Dictionary1", "http://test.org"),
                EncodingName = new XmlQualifiedName("Encoding2", "http://test.org")
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when EncodingName namespace differs.
        /// </summary>
        [Test]
        public void Equals_DifferentEncodingNameNamespace_ReturnsFalse()
        {
            // Arrange
            var instance1 = new DictionaryDesign
            {
                SymbolicName = new XmlQualifiedName("Dictionary1", "http://test.org"),
                EncodingName = new XmlQualifiedName("TestEncoding", "http://namespace1.org")
            };
            var instance2 = new DictionaryDesign
            {
                SymbolicName = new XmlQualifiedName("Dictionary1", "http://test.org"),
                EncodingName = new XmlQualifiedName("TestEncoding", "http://namespace2.org")
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both instances have null EncodingName.
        /// </summary>
        [Test]
        public void Equals_BothEncodingNameNull_ReturnsTrue()
        {
            // Arrange
            var instance1 = new DictionaryDesign
            {
                SymbolicName = new XmlQualifiedName("Dictionary1", "http://test.org"),
                EncodingName = null
            };
            var instance2 = new DictionaryDesign
            {
                SymbolicName = new XmlQualifiedName("Dictionary1", "http://test.org"),
                EncodingName = null
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when one instance has null EncodingName and the other doesn't.
        /// </summary>
        [Test]
        public void Equals_OneEncodingNameNull_ReturnsFalse()
        {
            // Arrange
            var instance1 = new DictionaryDesign
            {
                SymbolicName = new XmlQualifiedName("Dictionary1", "http://test.org"),
                EncodingName = new XmlQualifiedName("TestEncoding", "http://test.org")
            };
            var instance2 = new DictionaryDesign
            {
                SymbolicName = new XmlQualifiedName("Dictionary1", "http://test.org"),
                EncodingName = null
            };

            // Act
            bool result1 = instance1.Equals(instance2);
            bool result2 = instance2.Equals(instance1);

            // Assert
            Assert.That(result1, Is.False);
            Assert.That(result2, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when base properties differ even if
        /// EncodingName is same.
        /// </summary>
        [Test]
        public void Equals_DifferentBaseProperties_ReturnsFalse()
        {
            // Arrange
            var encodingName = new XmlQualifiedName("TestEncoding", "http://test.org");
            var instance1 = new DictionaryDesign
            {
                SymbolicName = new XmlQualifiedName("Dictionary1", "http://test.org"),
                EncodingName = encodingName,
                DataType = new XmlQualifiedName("String", "http://test.org")
            };
            var instance2 = new DictionaryDesign
            {
                SymbolicName = new XmlQualifiedName("Dictionary2", "http://test.org"),
                EncodingName = encodingName,
                DataType = new XmlQualifiedName("Int32", "http://test.org")
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when only SymbolicName differs.
        /// </summary>
        [Test]
        public void Equals_DifferentSymbolicName_ReturnsFalse()
        {
            // Arrange
            var encodingName = new XmlQualifiedName("TestEncoding", "http://test.org");
            var instance1 = new DictionaryDesign
            {
                SymbolicName = new XmlQualifiedName("Dictionary1", "http://test.org"),
                EncodingName = encodingName
            };
            var instance2 = new DictionaryDesign
            {
                SymbolicName = new XmlQualifiedName("Dictionary2", "http://test.org"),
                EncodingName = encodingName
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when EncodingName is empty string versus non-empty.
        /// </summary>
        [Test]
        public void Equals_EncodingNameEmptyVsNonEmpty_ReturnsFalse()
        {
            // Arrange
            var instance1 = new DictionaryDesign
            {
                SymbolicName = new XmlQualifiedName("Dictionary1", "http://test.org"),
                EncodingName = new XmlQualifiedName(string.Empty, "http://test.org")
            };
            var instance2 = new DictionaryDesign
            {
                SymbolicName = new XmlQualifiedName("Dictionary1", "http://test.org"),
                EncodingName = new XmlQualifiedName("TestEncoding", "http://test.org")
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both EncodingNames are empty strings.
        /// </summary>
        [Test]
        public void Equals_BothEncodingNameEmpty_ReturnsTrue()
        {
            // Arrange
            var instance1 = new DictionaryDesign
            {
                SymbolicName = new XmlQualifiedName("Dictionary1", "http://test.org"),
                EncodingName = new XmlQualifiedName(string.Empty, "http://test.org")
            };
            var instance2 = new DictionaryDesign
            {
                SymbolicName = new XmlQualifiedName("Dictionary1", "http://test.org"),
                EncodingName = new XmlQualifiedName(string.Empty, "http://test.org")
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with null object.
        /// </summary>
        [Test]
        public void Equals_NullObject_ReturnsFalse()
        {
            // Arrange
            var dictionaryDesign = new DictionaryDesign
            {
                EncodingName = new XmlQualifiedName("TestEncoding", "http://test.namespace")
            };

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = dictionaryDesign.Equals(null);
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with a different type.
        /// </summary>
        [Test]
        public void Equals_DifferentType_ReturnsFalse()
        {
            // Arrange
            var dictionaryDesign = new DictionaryDesign
            {
                EncodingName = new XmlQualifiedName("TestEncoding", "http://test.namespace")
            };
            object differentObject = new();

            // Act
            bool result = dictionaryDesign.Equals(differentObject);

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
            var dictionaryDesign = new DictionaryDesign
            {
                EncodingName = new XmlQualifiedName("TestEncoding", "http://test.namespace")
            };
            const string stringObject = "SomeString";

            // Act
            bool result = dictionaryDesign.Equals(stringObject);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing two DictionaryDesign instances
        /// with same EncodingName.
        /// </summary>
        [Test]
        public void Equals_SameEncodingName_ReturnsTrue()
        {
            // Arrange
            var encodingName = new XmlQualifiedName("TestEncoding", "http://test.namespace");
            var dictionaryDesign1 = new DictionaryDesign
            {
                EncodingName = encodingName
            };
            var dictionaryDesign2 = new DictionaryDesign
            {
                EncodingName = new XmlQualifiedName("TestEncoding", "http://test.namespace")
            };

            // Act
            bool result = dictionaryDesign1.Equals((object)dictionaryDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing two DictionaryDesign instances
        /// with different EncodingName local names.
        /// </summary>
        [Test]
        public void Equals_DifferentEncodingNameLocalName_ReturnsFalse()
        {
            // Arrange
            var dictionaryDesign1 = new DictionaryDesign
            {
                EncodingName = new XmlQualifiedName("TestEncoding1", "http://test.namespace")
            };
            var dictionaryDesign2 = new DictionaryDesign
            {
                EncodingName = new XmlQualifiedName("TestEncoding2", "http://test.namespace")
            };

            // Act
            bool result = dictionaryDesign1.Equals((object)dictionaryDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when second DictionaryDesign has null
        /// EncodingName and the first has a value.
        /// </summary>
        [Test]
        public void Equals_SecondEncodingNameNull_ReturnsFalse()
        {
            // Arrange
            var dictionaryDesign1 = new DictionaryDesign
            {
                EncodingName = null
            };
            var dictionaryDesign2 = new DictionaryDesign
            {
                EncodingName = new XmlQualifiedName("TestEncoding", "http://test.namespace")
            };

            // Act
            bool result = dictionaryDesign1.Equals((object)dictionaryDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing with empty EncodingName
        /// (empty string local name and namespace).
        /// </summary>
        [Test]
        public void Equals_EmptyEncodingName_ReturnsTrue()
        {
            // Arrange
            var dictionaryDesign1 = new DictionaryDesign
            {
                EncodingName = new XmlQualifiedName(string.Empty, string.Empty)
            };
            var dictionaryDesign2 = new DictionaryDesign
            {
                EncodingName = new XmlQualifiedName(string.Empty, string.Empty)
            };

            // Act
            bool result = dictionaryDesign1.Equals((object)dictionaryDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing with EncodingName
        /// containing special characters.
        /// </summary>
        [Test]
        public void Equals_EncodingNameWithSpecialCharacters_ReturnsTrue()
        {
            // Arrange
            var encodingName = new XmlQualifiedName("Test_Encoding-123", "http://test.namespace/v1.0");
            var dictionaryDesign1 = new DictionaryDesign
            {
                EncodingName = encodingName
            };
            var dictionaryDesign2 = new DictionaryDesign
            {
                EncodingName = new XmlQualifiedName("Test_Encoding-123", "http://test.namespace/v1.0")
            };

            // Act
            bool result = dictionaryDesign1.Equals((object)dictionaryDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals is case-sensitive for EncodingName local name.
        /// </summary>
        [Test]
        public void Equals_EncodingNameDifferentCase_ReturnsFalse()
        {
            // Arrange
            var dictionaryDesign1 = new DictionaryDesign
            {
                EncodingName = new XmlQualifiedName("TestEncoding", "http://test.namespace")
            };
            var dictionaryDesign2 = new DictionaryDesign
            {
                EncodingName = new XmlQualifiedName("testencoding", "http://test.namespace")
            };

            // Act
            bool result = dictionaryDesign1.Equals((object)dictionaryDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals is case-sensitive for EncodingName namespace.
        /// </summary>
        [Test]
        public void Equals_EncodingNameNamespaceDifferentCase_ReturnsFalse()
        {
            // Arrange
            var dictionaryDesign1 = new DictionaryDesign
            {
                EncodingName = new XmlQualifiedName("TestEncoding", "http://test.namespace")
            };
            var dictionaryDesign2 = new DictionaryDesign
            {
                EncodingName = new XmlQualifiedName("TestEncoding", "HTTP://TEST.NAMESPACE")
            };

            // Act
            bool result = dictionaryDesign1.Equals((object)dictionaryDesign2);

            // Assert
            Assert.That(result, Is.False);
        }
    }
}
