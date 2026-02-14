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
    /// Unit tests for the <see cref="TypeDesign"/> class.
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class TypeDesignTests
    {
        /// <summary>
        /// Tests that GetHashCode returns a consistent value when called multiple times on the same object.
        /// Input: TypeDesign instance with default values.
        /// Expected: Same hash code on multiple calls.
        /// </summary>
        [Test]
        public void GetHashCode_SameObject_ReturnsConsistentHashCode()
        {
            // Arrange
            var typeDesign = new TypeDesign();

            // Act
            int hashCode1 = typeDesign.GetHashCode();
            int hashCode2 = typeDesign.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests that GetHashCode returns the same value for two objects with identical property values.
        /// Input: Two TypeDesign instances with same property values.
        /// Expected: Same hash code.
        /// </summary>
        [Test]
        public void GetHashCode_EqualObjects_ReturnsSameHashCode()
        {
            // Arrange
            var typeDesign1 = new TypeDesign
            {
                ClassName = "TestClass",
                BaseType = new XmlQualifiedName("BaseType", "http://test.org"),
                IsAbstract = true,
                NoClassGeneration = false
            };

            var typeDesign2 = new TypeDesign
            {
                ClassName = "TestClass",
                BaseType = new XmlQualifiedName("BaseType", "http://test.org"),
                IsAbstract = true,
                NoClassGeneration = false
            };

            // Act
            int hashCode1 = typeDesign1.GetHashCode();
            int hashCode2 = typeDesign2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests that GetHashCode handles null ClassName property.
        /// Input: TypeDesign with null ClassName.
        /// Expected: Valid hash code without exception.
        /// </summary>
        [Test]
        public void GetHashCode_NullClassName_ReturnsValidHashCode()
        {
            // Arrange
            var typeDesign = new TypeDesign
            {
                ClassName = null,
                BaseType = new XmlQualifiedName("BaseType", "http://test.org"),
                IsAbstract = false,
                NoClassGeneration = false
            };

            // Act
            int hashCode = typeDesign.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.TypeOf<int>());
        }

        /// <summary>
        /// Tests that GetHashCode handles empty string ClassName property.
        /// Input: TypeDesign with empty ClassName.
        /// Expected: Valid hash code without exception.
        /// </summary>
        [Test]
        public void GetHashCode_EmptyClassName_ReturnsValidHashCode()
        {
            // Arrange
            var typeDesign = new TypeDesign
            {
                ClassName = string.Empty,
                BaseType = new XmlQualifiedName("BaseType", "http://test.org"),
                IsAbstract = false,
                NoClassGeneration = false
            };

            // Act
            int hashCode = typeDesign.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.TypeOf<int>());
        }

        /// <summary>
        /// Tests that GetHashCode handles null BaseType property.
        /// Input: TypeDesign with null BaseType.
        /// Expected: Valid hash code without exception.
        /// </summary>
        [Test]
        public void GetHashCode_NullBaseType_ReturnsValidHashCode()
        {
            // Arrange
            var typeDesign = new TypeDesign
            {
                ClassName = "TestClass",
                BaseType = null,
                IsAbstract = false,
                NoClassGeneration = false
            };

            // Act
            int hashCode = typeDesign.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.TypeOf<int>());
        }

        /// <summary>
        /// Tests that GetHashCode handles null BaseTypeNode property.
        /// Input: TypeDesign with null BaseTypeNode.
        /// Expected: Valid hash code without exception.
        /// </summary>
        [Test]
        public void GetHashCode_NullBaseTypeNode_ReturnsValidHashCode()
        {
            // Arrange
            var typeDesign = new TypeDesign
            {
                ClassName = "TestClass",
                BaseType = new XmlQualifiedName("BaseType", "http://test.org"),
                IsAbstract = false,
                NoClassGeneration = false,
                BaseTypeNode = null
            };

            // Act
            int hashCode = typeDesign.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.TypeOf<int>());
        }

        /// <summary>
        /// Tests that GetHashCode handles non-null BaseTypeNode property.
        /// Input: TypeDesign with non-null BaseTypeNode.
        /// Expected: Valid hash code without exception.
        /// </summary>
        [Test]
        public void GetHashCode_NonNullBaseTypeNode_ReturnsValidHashCode()
        {
            // Arrange
            var baseTypeNode = new TypeDesign
            {
                ClassName = "BaseClass"
            };

            var typeDesign = new TypeDesign
            {
                ClassName = "TestClass",
                BaseType = new XmlQualifiedName("BaseType", "http://test.org"),
                IsAbstract = false,
                NoClassGeneration = false,
                BaseTypeNode = baseTypeNode
            };

            // Act
            int hashCode = typeDesign.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.TypeOf<int>());
        }

        /// <summary>
        /// Tests that GetHashCode produces different hash codes for objects with different ClassName values.
        /// Input: Two TypeDesign instances with different ClassName values.
        /// Expected: Different hash codes (most likely, though not guaranteed).
        /// </summary>
        [Test]
        public void GetHashCode_DifferentClassName_ProducesDifferentHashCode()
        {
            // Arrange
            var typeDesign1 = new TypeDesign
            {
                ClassName = "Class1",
                BaseType = new XmlQualifiedName("BaseType", "http://test.org"),
                IsAbstract = false,
                NoClassGeneration = false
            };

            var typeDesign2 = new TypeDesign
            {
                ClassName = "Class2",
                BaseType = new XmlQualifiedName("BaseType", "http://test.org"),
                IsAbstract = false,
                NoClassGeneration = false
            };

            // Act
            int hashCode1 = typeDesign1.GetHashCode();
            int hashCode2 = typeDesign2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.Not.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests that GetHashCode produces different hash codes for objects with different BaseType values.
        /// Input: Two TypeDesign instances with different BaseType values.
        /// Expected: Different hash codes (most likely, though not guaranteed).
        /// </summary>
        [Test]
        public void GetHashCode_DifferentBaseType_ProducesDifferentHashCode()
        {
            // Arrange
            var typeDesign1 = new TypeDesign
            {
                ClassName = "TestClass",
                BaseType = new XmlQualifiedName("BaseType1", "http://test.org"),
                IsAbstract = false,
                NoClassGeneration = false
            };

            var typeDesign2 = new TypeDesign
            {
                ClassName = "TestClass",
                BaseType = new XmlQualifiedName("BaseType2", "http://test.org"),
                IsAbstract = false,
                NoClassGeneration = false
            };

            // Act
            int hashCode1 = typeDesign1.GetHashCode();
            int hashCode2 = typeDesign2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.Not.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests that GetHashCode produces different hash codes for objects with different IsAbstract values.
        /// Input: Two TypeDesign instances with different IsAbstract values.
        /// Expected: Different hash codes (most likely, though not guaranteed).
        /// </summary>
        [Test]
        public void GetHashCode_DifferentIsAbstract_ProducesDifferentHashCode()
        {
            // Arrange
            var typeDesign1 = new TypeDesign
            {
                ClassName = "TestClass",
                BaseType = new XmlQualifiedName("BaseType", "http://test.org"),
                IsAbstract = true,
                NoClassGeneration = false
            };

            var typeDesign2 = new TypeDesign
            {
                ClassName = "TestClass",
                BaseType = new XmlQualifiedName("BaseType", "http://test.org"),
                IsAbstract = false,
                NoClassGeneration = false
            };

            // Act
            int hashCode1 = typeDesign1.GetHashCode();
            int hashCode2 = typeDesign2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.Not.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests that GetHashCode produces different hash codes for objects with different NoClassGeneration values.
        /// Input: Two TypeDesign instances with different NoClassGeneration values.
        /// Expected: Different hash codes (most likely, though not guaranteed).
        /// </summary>
        [Test]
        public void GetHashCode_DifferentNoClassGeneration_ProducesDifferentHashCode()
        {
            // Arrange
            var typeDesign1 = new TypeDesign
            {
                ClassName = "TestClass",
                BaseType = new XmlQualifiedName("BaseType", "http://test.org"),
                IsAbstract = false,
                NoClassGeneration = true
            };

            var typeDesign2 = new TypeDesign
            {
                ClassName = "TestClass",
                BaseType = new XmlQualifiedName("BaseType", "http://test.org"),
                IsAbstract = false,
                NoClassGeneration = false
            };

            // Act
            int hashCode1 = typeDesign1.GetHashCode();
            int hashCode2 = typeDesign2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.Not.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests that GetHashCode produces different hash codes for objects with different BaseTypeNode values.
        /// Input: Two TypeDesign instances with different BaseTypeNode values.
        /// Expected: Different hash codes (most likely, though not guaranteed).
        /// </summary>
        [Test]
        public void GetHashCode_DifferentBaseTypeNode_ProducesDifferentHashCode()
        {
            // Arrange
            var baseTypeNode1 = new TypeDesign
            {
                ClassName = "BaseClass1"
            };

            var baseTypeNode2 = new TypeDesign
            {
                ClassName = "BaseClass2"
            };

            var typeDesign1 = new TypeDesign
            {
                ClassName = "TestClass",
                BaseType = new XmlQualifiedName("BaseType", "http://test.org"),
                IsAbstract = false,
                NoClassGeneration = false,
                BaseTypeNode = baseTypeNode1
            };

            var typeDesign2 = new TypeDesign
            {
                ClassName = "TestClass",
                BaseType = new XmlQualifiedName("BaseType", "http://test.org"),
                IsAbstract = false,
                NoClassGeneration = false,
                BaseTypeNode = baseTypeNode2
            };

            // Act
            int hashCode1 = typeDesign1.GetHashCode();
            int hashCode2 = typeDesign2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.Not.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests that GetHashCode handles all null properties.
        /// Input: TypeDesign with all nullable properties set to null.
        /// Expected: Valid hash code without exception.
        /// </summary>
        [Test]
        public void GetHashCode_AllNullProperties_ReturnsValidHashCode()
        {
            // Arrange
            var typeDesign = new TypeDesign
            {
                ClassName = null,
                BaseType = null,
                IsAbstract = false,
                NoClassGeneration = false,
                BaseTypeNode = null
            };

            // Act
            int hashCode = typeDesign.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.TypeOf<int>());
        }

        /// <summary>
        /// Tests that GetHashCode handles BaseType with different namespaces.
        /// Input: Two TypeDesign instances with BaseType having different namespaces.
        /// Expected: Different hash codes.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentBaseTypeNamespace_ProducesDifferentHashCode()
        {
            // Arrange
            var typeDesign1 = new TypeDesign
            {
                ClassName = "TestClass",
                BaseType = new XmlQualifiedName("BaseType", "http://test1.org"),
                IsAbstract = false,
                NoClassGeneration = false
            };

            var typeDesign2 = new TypeDesign
            {
                ClassName = "TestClass",
                BaseType = new XmlQualifiedName("BaseType", "http://test2.org"),
                IsAbstract = false,
                NoClassGeneration = false
            };

            // Act
            int hashCode1 = typeDesign1.GetHashCode();
            int hashCode2 = typeDesign2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.Not.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests that GetHashCode handles whitespace-only ClassName.
        /// Input: TypeDesign with whitespace-only ClassName.
        /// Expected: Valid hash code without exception.
        /// </summary>
        [Test]
        public void GetHashCode_WhitespaceClassName_ReturnsValidHashCode()
        {
            // Arrange
            var typeDesign = new TypeDesign
            {
                ClassName = "   ",
                BaseType = new XmlQualifiedName("BaseType", "http://test.org"),
                IsAbstract = false,
                NoClassGeneration = false
            };

            // Act
            int hashCode = typeDesign.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.TypeOf<int>());
        }

        /// <summary>
        /// Tests that GetHashCode handles very long ClassName string.
        /// Input: TypeDesign with very long ClassName.
        /// Expected: Valid hash code without exception.
        /// </summary>
        [Test]
        public void GetHashCode_VeryLongClassName_ReturnsValidHashCode()
        {
            // Arrange
            var typeDesign = new TypeDesign
            {
                ClassName = new string('A', 10000),
                BaseType = new XmlQualifiedName("BaseType", "http://test.org"),
                IsAbstract = false,
                NoClassGeneration = false
            };

            // Act
            int hashCode = typeDesign.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.TypeOf<int>());
        }

        /// <summary>
        /// Tests that GetHashCode handles ClassName with special characters.
        /// Input: TypeDesign with ClassName containing special characters.
        /// Expected: Valid hash code without exception.
        /// </summary>
        [Test]
        public void GetHashCode_ClassNameWithSpecialCharacters_ReturnsValidHashCode()
        {
            // Arrange
            var typeDesign = new TypeDesign
            {
                ClassName = "Test!@#$%^&*()_+{}[]|\\:;\"'<>,.?/~`Class",
                BaseType = new XmlQualifiedName("BaseType", "http://test.org"),
                IsAbstract = false,
                NoClassGeneration = false
            };

            // Act
            int hashCode = typeDesign.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.TypeOf<int>());
        }

        /// <summary>
        /// Tests that GetHashCode handles all boolean property combinations.
        /// Input: TypeDesign instances with all combinations of IsAbstract and NoClassGeneration.
        /// Expected: Valid hash codes for all combinations.
        /// </summary>
        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void GetHashCode_BooleanCombinations_ReturnsValidHashCode(bool isAbstract, bool noClassGeneration)
        {
            // Arrange
            var typeDesign = new TypeDesign
            {
                ClassName = "TestClass",
                BaseType = new XmlQualifiedName("BaseType", "http://test.org"),
                IsAbstract = isAbstract,
                NoClassGeneration = noClassGeneration
            };

            // Act
            int hashCode = typeDesign.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.TypeOf<int>());
        }

        /// <summary>
        /// Tests that Copy method returns a non-null instance.
        /// </summary>
        [Test]
        public void Copy_DefaultInstance_ReturnsNonNull()
        {
            // Arrange
            var original = new TypeDesign();

            // Act
            TypeDesign copy = original.Copy();

            // Assert
            Assert.That(copy, Is.Not.Null);
        }

        /// <summary>
        /// Tests that Copy method creates a new instance with a different reference.
        /// </summary>
        [Test]
        public void Copy_DefaultInstance_ReturnsDifferentInstance()
        {
            // Arrange
            var original = new TypeDesign();

            // Act
            TypeDesign copy = original.Copy();

            // Assert
            Assert.That(copy, Is.Not.SameAs(original));
        }

        /// <summary>
        /// Tests that Copy method creates a shallow copy with same value-type properties.
        /// Verifies that boolean properties are copied correctly.
        /// </summary>
        [Test]
        public void Copy_WithBooleanProperties_CopiesValues()
        {
            // Arrange
            var original = new TypeDesign
            {
                IsAbstract = true,
                NoClassGeneration = true
            };

            // Act
            TypeDesign copy = original.Copy();

            // Assert
            Assert.That(copy.IsAbstract, Is.EqualTo(original.IsAbstract));
            Assert.That(copy.NoClassGeneration, Is.EqualTo(original.NoClassGeneration));
        }

        /// <summary>
        /// Tests that Copy method creates a shallow copy with same string properties.
        /// Verifies that ClassName property is copied correctly.
        /// </summary>
        [Test]
        public void Copy_WithStringProperties_CopiesReferences()
        {
            // Arrange
            var original = new TypeDesign
            {
                ClassName = "TestClassName"
            };

            // Act
            TypeDesign copy = original.Copy();

            // Assert
            Assert.That(copy.ClassName, Is.EqualTo(original.ClassName));
            Assert.That(copy.ClassName, Is.SameAs(original.ClassName));
        }

        /// <summary>
        /// Tests that Copy method creates a shallow copy with same reference-type properties.
        /// Verifies that BaseType property references the same object (shallow copy behavior).
        /// </summary>
        [Test]
        public void Copy_WithReferenceTypeProperties_CopiesSameReference()
        {
            // Arrange
            var baseType = new XmlQualifiedName("BaseTypeName", "http://example.com");
            var original = new TypeDesign
            {
                BaseType = baseType
            };

            // Act
            TypeDesign copy = original.Copy();

            // Assert
            Assert.That(copy.BaseType, Is.EqualTo(original.BaseType));
            Assert.That(copy.BaseType, Is.SameAs(original.BaseType));
        }

        /// <summary>
        /// Tests that Copy method creates a shallow copy preserving all properties.
        /// Verifies comprehensive property copying with multiple property types.
        /// </summary>
        [Test]
        public void Copy_WithAllPropertiesSet_CopiesAllValues()
        {
            // Arrange
            var baseType = new XmlQualifiedName("BaseType", "http://test.com");
            var original = new TypeDesign
            {
                ClassName = "TestClass",
                BaseType = baseType,
                IsAbstract = true,
                NoClassGeneration = false
            };

            // Act
            TypeDesign copy = original.Copy();

            // Assert
            Assert.That(copy, Is.Not.SameAs(original));
            Assert.That(copy.ClassName, Is.EqualTo(original.ClassName));
            Assert.That(copy.BaseType, Is.SameAs(original.BaseType));
            Assert.That(copy.IsAbstract, Is.EqualTo(original.IsAbstract));
            Assert.That(copy.NoClassGeneration, Is.EqualTo(original.NoClassGeneration));
        }

        /// <summary>
        /// Tests that Copy method performs shallow copy for reference type properties.
        /// Verifies that modifying the referenced object affects both original and copy.
        /// </summary>
        [Test]
        public void Copy_ShallowCopyBehavior_ReferencedObjectIsShared()
        {
            // Arrange
            var baseType = new XmlQualifiedName("OriginalName", "http://test.com");
            var original = new TypeDesign
            {
                BaseType = baseType
            };

            // Act
            TypeDesign copy = original.Copy();
            original.BaseType = new XmlQualifiedName("ModifiedName", "http://modified.com");

            // Assert
            Assert.That(copy.BaseType, Is.Not.SameAs(original.BaseType));
            Assert.That(copy.BaseType.Name, Is.EqualTo("OriginalName"));
            Assert.That(original.BaseType.Name, Is.EqualTo("ModifiedName"));
        }

        /// <summary>
        /// Tests that Copy method with null reference properties handles correctly.
        /// Verifies that null properties are copied as null.
        /// </summary>
        [Test]
        public void Copy_WithNullProperties_CopiesNullValues()
        {
            // Arrange
            var original = new TypeDesign
            {
                ClassName = null,
                BaseType = null
            };

            // Act
            TypeDesign copy = original.Copy();

            // Assert
            Assert.That(copy.ClassName, Is.Null);
            Assert.That(copy.BaseType, Is.Null);
        }

        /// <summary>
        /// Tests that Copy method with empty string properties handles correctly.
        /// Verifies that empty strings are copied correctly.
        /// </summary>
        [Test]
        public void Copy_WithEmptyString_CopiesEmptyString()
        {
            // Arrange
            var original = new TypeDesign
            {
                ClassName = string.Empty
            };

            // Act
            TypeDesign copy = original.Copy();

            // Assert
            Assert.That(copy.ClassName, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that Copy method with default boolean values handles correctly.
        /// Verifies that default false values are copied correctly.
        /// </summary>
        [Test]
        public void Copy_WithDefaultBooleanValues_CopiesDefaults()
        {
            // Arrange
            var original = new TypeDesign
            {
                IsAbstract = false,
                NoClassGeneration = false
            };

            // Act
            TypeDesign copy = original.Copy();

            // Assert
            Assert.That(copy.IsAbstract, Is.False);
            Assert.That(copy.NoClassGeneration, Is.False);
        }

        /// <summary>
        /// Tests that Copy method with very long string properties handles correctly.
        /// Verifies that long strings are copied correctly.
        /// </summary>
        [Test]
        public void Copy_WithLongString_CopiesLongString()
        {
            // Arrange
            string longClassName = new('A', 10000);
            var original = new TypeDesign
            {
                ClassName = longClassName
            };

            // Act
            TypeDesign copy = original.Copy();

            // Assert
            Assert.That(copy.ClassName, Is.EqualTo(longClassName));
            Assert.That(copy.ClassName.Length, Is.EqualTo(10000));
        }

        /// <summary>
        /// Tests that Copy method with special characters in string properties handles correctly.
        /// Verifies that strings with special characters are copied correctly.
        /// </summary>
        [Test]
        public void Copy_WithSpecialCharactersInString_CopiesSpecialCharacters()
        {
            // Arrange
            var original = new TypeDesign
            {
                ClassName = "Class\n\r\t\0Name"
            };

            // Act
            TypeDesign copy = original.Copy();

            // Assert
            Assert.That(copy.ClassName, Is.EqualTo(original.ClassName));
        }

        /// <summary>
        /// Tests that Copy method can be called multiple times on the same instance.
        /// Verifies that multiple copies are independent.
        /// </summary>
        [Test]
        public void Copy_CalledMultipleTimes_CreatesIndependentCopies()
        {
            // Arrange
            var original = new TypeDesign
            {
                ClassName = "OriginalClass",
                IsAbstract = true
            };

            // Act
            TypeDesign copy1 = original.Copy();
            TypeDesign copy2 = original.Copy();

            // Assert
            Assert.That(copy1, Is.Not.SameAs(copy2));
            Assert.That(copy1.ClassName, Is.EqualTo(copy2.ClassName));
            Assert.That(copy1.IsAbstract, Is.EqualTo(copy2.IsAbstract));
        }

        /// <summary>
        /// Tests that Copy method on a copied instance creates a new independent copy.
        /// Verifies that copying a copy works correctly.
        /// </summary>
        [Test]
        public void Copy_OfCopy_CreatesNewIndependentInstance()
        {
            // Arrange
            var original = new TypeDesign
            {
                ClassName = "Original",
                IsAbstract = true
            };
            TypeDesign firstCopy = original.Copy();

            // Act
            TypeDesign secondCopy = firstCopy.Copy();

            // Assert
            Assert.That(secondCopy, Is.Not.SameAs(original));
            Assert.That(secondCopy, Is.Not.SameAs(firstCopy));
            Assert.That(secondCopy.ClassName, Is.EqualTo(original.ClassName));
            Assert.That(secondCopy.IsAbstract, Is.EqualTo(original.IsAbstract));
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with null.
        /// Input: null other parameter.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_NullOther_ReturnsFalse()
        {
            // Arrange
            TypeDesign typeDesign = CreateTypeDesign("ClassName1", "BaseType1", true, false);

            // Act
            bool result = typeDesign.Equals(null);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing with itself.
        /// Input: Same instance.
        /// Expected: Returns true.
        /// </summary>
        [Test]
        public void Equals_SameInstance_ReturnsTrue()
        {
            // Arrange
            TypeDesign typeDesign = CreateTypeDesign("ClassName1", "BaseType1", true, false);

            // Act
            bool result = typeDesign.Equals(typeDesign);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when all properties are equal.
        /// Input: Two instances with identical property values.
        /// Expected: Returns true.
        /// </summary>
        [Test]
        public void Equals_AllPropertiesEqual_ReturnsTrue()
        {
            // Arrange
            TypeDesign typeDesign1 = CreateTypeDesign("ClassName1", "BaseType1", true, false);
            TypeDesign typeDesign2 = CreateTypeDesign("ClassName1", "BaseType1", true, false);

            // Act
            bool result = typeDesign1.Equals(typeDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when ClassName differs.
        /// Input: Two instances with different ClassName values.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_DifferentClassName_ReturnsFalse()
        {
            // Arrange
            TypeDesign typeDesign1 = CreateTypeDesign("ClassName1", "BaseType1", true, false);
            TypeDesign typeDesign2 = CreateTypeDesign("ClassName2", "BaseType1", true, false);

            // Act
            bool result = typeDesign1.Equals(typeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when one ClassName is null.
        /// Input: One instance with null ClassName, another with non-null.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_OneClassNameNull_ReturnsFalse()
        {
            // Arrange
            TypeDesign typeDesign1 = CreateTypeDesign(null, "BaseType1", true, false);
            TypeDesign typeDesign2 = CreateTypeDesign("ClassName2", "BaseType1", true, false);

            // Act
            bool result = typeDesign1.Equals(typeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both ClassNames are null.
        /// Input: Two instances with null ClassNames.
        /// Expected: Returns true.
        /// </summary>
        [Test]
        public void Equals_BothClassNamesNull_ReturnsTrue()
        {
            // Arrange
            TypeDesign typeDesign1 = CreateTypeDesign(null, "BaseType1", true, false);
            TypeDesign typeDesign2 = CreateTypeDesign(null, "BaseType1", true, false);

            // Act
            bool result = typeDesign1.Equals(typeDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when BaseType differs.
        /// Input: Two instances with different BaseType values.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_DifferentBaseType_ReturnsFalse()
        {
            // Arrange
            TypeDesign typeDesign1 = CreateTypeDesign("ClassName1", "BaseType1", true, false);
            TypeDesign typeDesign2 = CreateTypeDesign("ClassName1", "BaseType2", true, false);

            // Act
            bool result = typeDesign1.Equals(typeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when BaseType namespace differs.
        /// Input: Two instances with BaseType having different namespaces.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_DifferentBaseTypeNamespace_ReturnsFalse()
        {
            // Arrange
            var typeDesign1 = new TypeDesign
            {
                ClassName = "ClassName1",
                BaseType = new XmlQualifiedName("BaseType1", "http://namespace1.com"),
                IsAbstract = true,
                NoClassGeneration = false,
                BrowseName = "Node1"
            };
            var typeDesign2 = new TypeDesign
            {
                ClassName = "ClassName1",
                BaseType = new XmlQualifiedName("BaseType1", "http://namespace2.com"),
                IsAbstract = true,
                NoClassGeneration = false,
                BrowseName = "Node1"
            };

            // Act
            bool result = typeDesign1.Equals(typeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both BaseTypes are null.
        /// Input: Two instances with null BaseTypes.
        /// Expected: Returns true.
        /// </summary>
        [Test]
        public void Equals_BothBaseTypesNull_ReturnsTrue()
        {
            // Arrange
            TypeDesign typeDesign1 = CreateTypeDesign("ClassName1", null, true, false);
            TypeDesign typeDesign2 = CreateTypeDesign("ClassName1", null, true, false);

            // Act
            bool result = typeDesign1.Equals(typeDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when one BaseType is null.
        /// Input: One instance with null BaseType, another with non-null.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_OneBaseTypeNull_ReturnsFalse()
        {
            // Arrange
            TypeDesign typeDesign1 = CreateTypeDesign("ClassName1", null, true, false);
            TypeDesign typeDesign2 = CreateTypeDesign("ClassName1", "BaseType1", true, false);

            // Act
            bool result = typeDesign1.Equals(typeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when IsAbstract differs.
        /// Input: Two instances with different IsAbstract values.
        /// Expected: Returns false.
        /// </summary>
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void Equals_DifferentIsAbstract_ReturnsFalse(bool isAbstract1, bool isAbstract2)
        {
            // Arrange
            TypeDesign typeDesign1 = CreateTypeDesign("ClassName1", "BaseType1", isAbstract1, false);
            TypeDesign typeDesign2 = CreateTypeDesign("ClassName1", "BaseType1", isAbstract2, false);

            // Act
            bool result = typeDesign1.Equals(typeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when NoClassGeneration differs.
        /// Input: Two instances with different NoClassGeneration values.
        /// Expected: Returns false.
        /// </summary>
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void Equals_DifferentNoClassGeneration_ReturnsFalse(bool noClassGen1, bool noClassGen2)
        {
            // Arrange
            TypeDesign typeDesign1 = CreateTypeDesign("ClassName1", "BaseType1", true, noClassGen1);
            TypeDesign typeDesign2 = CreateTypeDesign("ClassName1", "BaseType1", true, noClassGen2);

            // Act
            bool result = typeDesign1.Equals(typeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when BaseTypeNode differs.
        /// Input: Two instances with different BaseTypeNode values.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_DifferentBaseTypeNode_ReturnsFalse()
        {
            // Arrange
            TypeDesign baseTypeNode1 = CreateTypeDesign("BaseClass1", "Root", false, false);
            TypeDesign baseTypeNode2 = CreateTypeDesign("BaseClass2", "Root", false, false);

            TypeDesign typeDesign1 = CreateTypeDesign("ClassName1", "BaseType1", true, false);
            typeDesign1.BaseTypeNode = baseTypeNode1;

            TypeDesign typeDesign2 = CreateTypeDesign("ClassName1", "BaseType1", true, false);
            typeDesign2.BaseTypeNode = baseTypeNode2;

            // Act
            bool result = typeDesign1.Equals(typeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both BaseTypeNodes are null.
        /// Input: Two instances with null BaseTypeNodes.
        /// Expected: Returns true.
        /// </summary>
        [Test]
        public void Equals_BothBaseTypeNodesNull_ReturnsTrue()
        {
            // Arrange
            TypeDesign typeDesign1 = CreateTypeDesign("ClassName1", "BaseType1", true, false);
            typeDesign1.BaseTypeNode = null;

            TypeDesign typeDesign2 = CreateTypeDesign("ClassName1", "BaseType1", true, false);
            typeDesign2.BaseTypeNode = null;

            // Act
            bool result = typeDesign1.Equals(typeDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when one BaseTypeNode is null.
        /// Input: One instance with null BaseTypeNode, another with non-null.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_OneBaseTypeNodeNull_ReturnsFalse()
        {
            // Arrange
            TypeDesign baseTypeNode = CreateTypeDesign("BaseClass1", "Root", false, false);

            TypeDesign typeDesign1 = CreateTypeDesign("ClassName1", "BaseType1", true, false);
            typeDesign1.BaseTypeNode = null;

            TypeDesign typeDesign2 = CreateTypeDesign("ClassName1", "BaseType1", true, false);
            typeDesign2.BaseTypeNode = baseTypeNode;

            // Act
            bool result = typeDesign1.Equals(typeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when base properties differ (BrowseName).
        /// Input: Two instances with different BrowseName (base property).
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_DifferentBrowseName_ReturnsFalse()
        {
            // Arrange
            var typeDesign1 = new TypeDesign
            {
                ClassName = "ClassName1",
                BaseType = new XmlQualifiedName("BaseType1"),
                IsAbstract = true,
                NoClassGeneration = false,
                BrowseName = "Node1"
            };
            var typeDesign2 = new TypeDesign
            {
                ClassName = "ClassName1",
                BaseType = new XmlQualifiedName("BaseType1"),
                IsAbstract = true,
                NoClassGeneration = false,
                BrowseName = "Node2"
            };

            // Act
            bool result = typeDesign1.Equals(typeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when all properties including defaults are equal.
        /// Input: Two instances with default boolean values.
        /// Expected: Returns true.
        /// </summary>
        [Test]
        public void Equals_DefaultValues_ReturnsTrue()
        {
            // Arrange
            var typeDesign1 = new TypeDesign
            {
                BrowseName = "Node1"
            };
            var typeDesign2 = new TypeDesign
            {
                BrowseName = "Node1"
            };

            // Act
            bool result = typeDesign1.Equals(typeDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when multiple properties differ.
        /// Input: Two instances with multiple different properties.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_MultiplePropertiesDifferent_ReturnsFalse()
        {
            // Arrange
            TypeDesign typeDesign1 = CreateTypeDesign("ClassName1", "BaseType1", true, false);
            TypeDesign typeDesign2 = CreateTypeDesign("ClassName2", "BaseType2", false, true);

            // Act
            bool result = typeDesign1.Equals(typeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals handles empty string ClassName correctly.
        /// Input: Two instances with empty string ClassNames.
        /// Expected: Returns true.
        /// </summary>
        [Test]
        public void Equals_EmptyStringClassName_ReturnsTrue()
        {
            // Arrange
            TypeDesign typeDesign1 = CreateTypeDesign(string.Empty, "BaseType1", true, false);
            TypeDesign typeDesign2 = CreateTypeDesign(string.Empty, "BaseType1", true, false);

            // Act
            bool result = typeDesign1.Equals(typeDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when one ClassName is empty and other is not.
        /// Input: One instance with empty ClassName, another with non-empty.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_OneEmptyClassName_ReturnsFalse()
        {
            // Arrange
            TypeDesign typeDesign1 = CreateTypeDesign(string.Empty, "BaseType1", true, false);
            TypeDesign typeDesign2 = CreateTypeDesign("ClassName2", "BaseType1", true, false);

            // Act
            bool result = typeDesign1.Equals(typeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Helper method to create a TypeDesign instance with specified properties.
        /// </summary>
        private static TypeDesign CreateTypeDesign(string className, string baseTypeName, bool isAbstract, bool noClassGeneration)
        {
            return new TypeDesign
            {
                ClassName = className,
                BaseType = baseTypeName != null ? new XmlQualifiedName(baseTypeName) : null,
                IsAbstract = isAbstract,
                NoClassGeneration = noClassGeneration,
                BrowseName = "TestNode"
            };
        }

        /// <summary>
        /// Tests that Equals returns false when obj is null.
        /// </summary>
        [Test]
        public void Equals_NullObject_ReturnsFalse()
        {
            // Arrange
            var typeDesign = new TypeDesign
            {
                ClassName = "TestClass",
                BaseType = new XmlQualifiedName("BaseType", "http://test.com"),
                IsAbstract = false,
                NoClassGeneration = false
            };

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = typeDesign.Equals(null);
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing two different instances with identical values.
        /// </summary>
        [Test]
        public void Equals_DifferentInstancesWithSameValues_ReturnsTrue()
        {
            // Arrange
            var typeDesign1 = new TypeDesign
            {
                SymbolicId = new XmlQualifiedName("TypeId", "http://test.com"),
                ClassName = "TestClass",
                BaseType = new XmlQualifiedName("BaseType", "http://test.com"),
                IsAbstract = false,
                NoClassGeneration = false
            };

            var typeDesign2 = new TypeDesign
            {
                SymbolicId = new XmlQualifiedName("TypeId", "http://test.com"),
                ClassName = "TestClass",
                BaseType = new XmlQualifiedName("BaseType", "http://test.com"),
                IsAbstract = false,
                NoClassGeneration = false
            };

            // Act
            bool result = typeDesign1.Equals((object)typeDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing instances with different IsAbstract values.
        /// </summary>
        [Test]
        public void Equals_DifferentIsAbstract_ReturnsFalse()
        {
            // Arrange
            var typeDesign1 = new TypeDesign
            {
                SymbolicId = new XmlQualifiedName("TypeId", "http://test.com"),
                ClassName = "TestClass",
                BaseType = new XmlQualifiedName("BaseType", "http://test.com"),
                IsAbstract = true,
                NoClassGeneration = false
            };

            var typeDesign2 = new TypeDesign
            {
                SymbolicId = new XmlQualifiedName("TypeId", "http://test.com"),
                ClassName = "TestClass",
                BaseType = new XmlQualifiedName("BaseType", "http://test.com"),
                IsAbstract = false,
                NoClassGeneration = false
            };

            // Act
            bool result = typeDesign1.Equals((object)typeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing instances with different NoClassGeneration values.
        /// </summary>
        [Test]
        public void Equals_DifferentNoClassGeneration_ReturnsFalse()
        {
            // Arrange
            var typeDesign1 = new TypeDesign
            {
                SymbolicId = new XmlQualifiedName("TypeId", "http://test.com"),
                ClassName = "TestClass",
                BaseType = new XmlQualifiedName("BaseType", "http://test.com"),
                IsAbstract = false,
                NoClassGeneration = true
            };

            var typeDesign2 = new TypeDesign
            {
                SymbolicId = new XmlQualifiedName("TypeId", "http://test.com"),
                ClassName = "TestClass",
                BaseType = new XmlQualifiedName("BaseType", "http://test.com"),
                IsAbstract = false,
                NoClassGeneration = false
            };

            // Act
            bool result = typeDesign1.Equals((object)typeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when obj is of a different type.
        /// </summary>
        [Test]
        public void Equals_DifferentType_ReturnsFalse()
        {
            // Arrange
            var typeDesign = new TypeDesign
            {
                ClassName = "TestClass",
                BaseType = new XmlQualifiedName("BaseType", "http://test.com"),
                IsAbstract = false,
                NoClassGeneration = false
            };
            var differentTypeObject = new ObjectDesign();

            // Act
            bool result = typeDesign.Equals((object)differentTypeObject);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when obj is a completely unrelated type.
        /// </summary>
        [Test]
        public void Equals_CompletelyDifferentType_ReturnsFalse()
        {
            // Arrange
            var typeDesign = new TypeDesign
            {
                ClassName = "TestClass",
                BaseType = new XmlQualifiedName("BaseType", "http://test.com"),
                IsAbstract = false,
                NoClassGeneration = false
            };
            const string differentObject = "StringObject";

            // Act
            bool result = typeDesign.Equals(differentObject);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both instances have null ClassName values.
        /// </summary>
        [Test]
        public void Equals_BothNullClassName_ReturnsTrue()
        {
            // Arrange
            var typeDesign1 = new TypeDesign
            {
                SymbolicId = new XmlQualifiedName("TypeId", "http://test.com"),
                ClassName = null,
                BaseType = new XmlQualifiedName("BaseType", "http://test.com"),
                IsAbstract = false,
                NoClassGeneration = false
            };

            var typeDesign2 = new TypeDesign
            {
                SymbolicId = new XmlQualifiedName("TypeId", "http://test.com"),
                ClassName = null,
                BaseType = new XmlQualifiedName("BaseType", "http://test.com"),
                IsAbstract = false,
                NoClassGeneration = false
            };

            // Act
            bool result = typeDesign1.Equals((object)typeDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when both instances have null BaseType values.
        /// </summary>
        [Test]
        public void Equals_BothNullBaseType_ReturnsTrue()
        {
            // Arrange
            var typeDesign1 = new TypeDesign
            {
                SymbolicId = new XmlQualifiedName("TypeId", "http://test.com"),
                ClassName = "TestClass",
                BaseType = null,
                IsAbstract = false,
                NoClassGeneration = false
            };

            var typeDesign2 = new TypeDesign
            {
                SymbolicId = new XmlQualifiedName("TypeId", "http://test.com"),
                ClassName = "TestClass",
                BaseType = null,
                IsAbstract = false,
                NoClassGeneration = false
            };

            // Act
            bool result = typeDesign1.Equals((object)typeDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when one ClassName is null and the other is not.
        /// </summary>
        [Test]
        public void Equals_OneNullClassName_ReturnsFalse()
        {
            // Arrange
            var typeDesign1 = new TypeDesign
            {
                SymbolicId = new XmlQualifiedName("TypeId", "http://test.com"),
                ClassName = "TestClass",
                BaseType = new XmlQualifiedName("BaseType", "http://test.com"),
                IsAbstract = false,
                NoClassGeneration = false
            };

            var typeDesign2 = new TypeDesign
            {
                SymbolicId = new XmlQualifiedName("TypeId", "http://test.com"),
                ClassName = null,
                BaseType = new XmlQualifiedName("BaseType", "http://test.com"),
                IsAbstract = false,
                NoClassGeneration = false
            };

            // Act
            bool result = typeDesign1.Equals((object)typeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when one BaseType is null and the other is not.
        /// </summary>
        [Test]
        public void Equals_OneNullBaseType_ReturnsFalse()
        {
            // Arrange
            var typeDesign1 = new TypeDesign
            {
                SymbolicId = new XmlQualifiedName("TypeId", "http://test.com"),
                ClassName = "TestClass",
                BaseType = new XmlQualifiedName("BaseType", "http://test.com"),
                IsAbstract = false,
                NoClassGeneration = false
            };

            var typeDesign2 = new TypeDesign
            {
                SymbolicId = new XmlQualifiedName("TypeId", "http://test.com"),
                ClassName = "TestClass",
                BaseType = null,
                IsAbstract = false,
                NoClassGeneration = false
            };

            // Act
            bool result = typeDesign1.Equals((object)typeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when ClassName values differ in case sensitivity (strings are case-sensitive by default).
        /// </summary>
        [Test]
        public void Equals_ClassNameCaseDifference_ReturnsFalse()
        {
            // Arrange
            var typeDesign1 = new TypeDesign
            {
                SymbolicId = new XmlQualifiedName("TypeId", "http://test.com"),
                ClassName = "TestClass",
                BaseType = new XmlQualifiedName("BaseType", "http://test.com"),
                IsAbstract = false,
                NoClassGeneration = false
            };

            var typeDesign2 = new TypeDesign
            {
                SymbolicId = new XmlQualifiedName("TypeId", "http://test.com"),
                ClassName = "testclass",
                BaseType = new XmlQualifiedName("BaseType", "http://test.com"),
                IsAbstract = false,
                NoClassGeneration = false
            };

            // Act
            bool result = typeDesign1.Equals((object)typeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals correctly handles BaseType with different namespaces.
        /// </summary>
        [Test]
        public void Equals_BaseTypeWithDifferentNamespace_ReturnsFalse()
        {
            // Arrange
            var typeDesign1 = new TypeDesign
            {
                SymbolicId = new XmlQualifiedName("TypeId", "http://test.com"),
                ClassName = "TestClass",
                BaseType = new XmlQualifiedName("BaseType", "http://test1.com"),
                IsAbstract = false,
                NoClassGeneration = false
            };

            var typeDesign2 = new TypeDesign
            {
                SymbolicId = new XmlQualifiedName("TypeId", "http://test.com"),
                ClassName = "TestClass",
                BaseType = new XmlQualifiedName("BaseType", "http://test2.com"),
                IsAbstract = false,
                NoClassGeneration = false
            };

            // Act
            bool result = typeDesign1.Equals((object)typeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }
    }
}
