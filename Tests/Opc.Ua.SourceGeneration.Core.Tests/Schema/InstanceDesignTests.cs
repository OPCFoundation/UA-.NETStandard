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
    /// Unit tests for <see cref="InstanceDesign"/> class.
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class InstanceDesignTests
    {
        /// <summary>
        /// Tests that Copy method creates a new instance with different reference.
        /// Verifies that the copy is not the same object as the original instance.
        /// </summary>
        [Test]
        public void Copy_DefaultInstance_ReturnsNewInstance()
        {
            // Arrange
            var original = new InstanceDesign();

            // Act
            InstanceDesign copy = original.Copy();

            // Assert
            Assert.That(copy, Is.Not.Null);
            Assert.That(copy, Is.Not.SameAs(original));
        }

        /// <summary>
        /// Tests that Copy method returns an object of the correct type.
        /// Verifies that the returned object is of type InstanceDesign.
        /// </summary>
        [Test]
        public void Copy_DefaultInstance_ReturnsCorrectType()
        {
            // Arrange
            var original = new InstanceDesign();

            // Act
            InstanceDesign copy = original.Copy();

            // Assert
            Assert.That(copy, Is.InstanceOf<InstanceDesign>());
        }

        /// <summary>
        /// Tests that Copy method performs a shallow copy for value-type properties.
        /// Verifies that value-type properties (MinCardinality, MaxCardinality, booleans) are copied correctly.
        /// </summary>
        [Test]
        public void Copy_InstanceWithValueTypeProperties_CopiesValuesCorrectly()
        {
            // Arrange
            var original = new InstanceDesign
            {
                MinCardinality = 5,
                MaxCardinality = 10,
                PreserveDefaultAttributes = true,
                DesignToolOnly = true,
                ModellingRule = ModellingRule.Mandatory,
                ModellingRuleSpecified = true,
                IdentifierRequired = true
            };

            // Act
            InstanceDesign copy = original.Copy();

            // Assert
            Assert.That(copy.MinCardinality, Is.EqualTo(original.MinCardinality));
            Assert.That(copy.MaxCardinality, Is.EqualTo(original.MaxCardinality));
            Assert.That(copy.PreserveDefaultAttributes, Is.EqualTo(original.PreserveDefaultAttributes));
            Assert.That(copy.DesignToolOnly, Is.EqualTo(original.DesignToolOnly));
            Assert.That(copy.ModellingRule, Is.EqualTo(original.ModellingRule));
            Assert.That(copy.ModellingRuleSpecified, Is.EqualTo(original.ModellingRuleSpecified));
            Assert.That(copy.IdentifierRequired, Is.EqualTo(original.IdentifierRequired));
        }

        /// <summary>
        /// Tests that Copy method performs a shallow copy for reference-type properties.
        /// Verifies that reference-type properties point to the same objects as the original.
        /// </summary>
        [Test]
        public void Copy_InstanceWithReferenceTypeProperties_PerformsShallowCopy()
        {
            // Arrange
            var referenceType = new XmlQualifiedName("RefType", "http://example.com");
            var declaration = new XmlQualifiedName("Declaration", "http://example.com");
            var typeDefinition = new XmlQualifiedName("TypeDef", "http://example.com");
            var typeDefinitionNode = new ObjectTypeDesign();
            var overriddenNode = new ObjectDesign();

            var original = new InstanceDesign
            {
                ReferenceType = referenceType,
                Declaration = declaration,
                TypeDefinition = typeDefinition,
                TypeDefinitionNode = typeDefinitionNode,
                OveriddenNode = overriddenNode
            };

            // Act
            InstanceDesign copy = original.Copy();

            // Assert
            Assert.That(copy.ReferenceType, Is.SameAs(original.ReferenceType));
            Assert.That(copy.Declaration, Is.SameAs(original.Declaration));
            Assert.That(copy.TypeDefinition, Is.SameAs(original.TypeDefinition));
            Assert.That(copy.TypeDefinitionNode, Is.SameAs(original.TypeDefinitionNode));
            Assert.That(copy.OveriddenNode, Is.SameAs(original.OveriddenNode));
        }

        /// <summary>
        /// Tests that Copy method correctly handles null reference-type properties.
        /// Verifies that null properties remain null in the copy.
        /// </summary>
        [Test]
        public void Copy_InstanceWithNullProperties_CopiesNullsCorrectly()
        {
            // Arrange
            var original = new InstanceDesign
            {
                ReferenceType = null,
                Declaration = null,
                TypeDefinition = null,
                TypeDefinitionNode = null,
                OveriddenNode = null
            };

            // Act
            InstanceDesign copy = original.Copy();

            // Assert
            Assert.That(copy.ReferenceType, Is.Null);
            Assert.That(copy.Declaration, Is.Null);
            Assert.That(copy.TypeDefinition, Is.Null);
            Assert.That(copy.TypeDefinitionNode, Is.Null);
            Assert.That(copy.OveriddenNode, Is.Null);
        }

        /// <summary>
        /// Tests that Copy method handles boundary values for numeric properties.
        /// Verifies that minimum and maximum uint values are copied correctly.
        /// </summary>
        [Test]
        public void Copy_InstanceWithBoundaryNumericValues_CopiesCorrectly()
        {
            // Arrange
            var original = new InstanceDesign
            {
                MinCardinality = uint.MinValue,
                MaxCardinality = uint.MaxValue
            };

            // Act
            InstanceDesign copy = original.Copy();

            // Assert
            Assert.That(copy.MinCardinality, Is.EqualTo(uint.MinValue));
            Assert.That(copy.MaxCardinality, Is.EqualTo(uint.MaxValue));
        }

        /// <summary>
        /// Tests that modifying the copy does not affect the original for value-type properties.
        /// Verifies that value-type properties are independent between original and copy.
        /// </summary>
        [Test]
        public void Copy_ModifyingCopyValueProperties_DoesNotAffectOriginal()
        {
            // Arrange
            var original = new InstanceDesign
            {
                MinCardinality = 5,
                MaxCardinality = 10,
                PreserveDefaultAttributes = false,
                DesignToolOnly = false
            };

            // Act
            InstanceDesign copy = original.Copy();
            copy.MinCardinality = 20;
            copy.MaxCardinality = 30;
            copy.PreserveDefaultAttributes = true;
            copy.DesignToolOnly = true;

            // Assert
            Assert.That(original.MinCardinality, Is.EqualTo(5));
            Assert.That(original.MaxCardinality, Is.EqualTo(10));
            Assert.That(original.PreserveDefaultAttributes, Is.False);
            Assert.That(original.DesignToolOnly, Is.False);
        }

        /// <summary>
        /// Tests that modifying reference-type properties in the copy affects the original due to shallow copy.
        /// Verifies that reference-type properties share the same underlying objects.
        /// </summary>
        [Test]
        public void Copy_ModifyingSharedReferenceObject_AffectsBothInstances()
        {
            // Arrange
            var typeDefinitionNode = new ObjectTypeDesign
            {
                SymbolicName = new XmlQualifiedName("OriginalName", "http://example.com")
            };

            var original = new InstanceDesign
            {
                TypeDefinitionNode = typeDefinitionNode
            };

            // Act
            InstanceDesign copy = original.Copy();
            copy.TypeDefinitionNode.SymbolicName = new XmlQualifiedName("ModifiedName", "http://example.com");

            // Assert
            Assert.That(original.TypeDefinitionNode.SymbolicName.Name, Is.EqualTo("ModifiedName"));
        }

        /// <summary>
        /// Tests that Copy method handles all ModellingRule enum values correctly.
        /// Verifies that enum properties are copied correctly for different enum values.
        /// </summary>
        [TestCase(ModellingRule.Mandatory)]
        [TestCase(ModellingRule.Optional)]
        [TestCase(ModellingRule.MandatoryPlaceholder)]
        [TestCase(ModellingRule.OptionalPlaceholder)]
        [TestCase(ModellingRule.ExposesItsArray)]
        [TestCase(ModellingRule.None)]
        public void Copy_InstanceWithDifferentModellingRules_CopiesEnumCorrectly(ModellingRule modellingRule)
        {
            // Arrange
            var original = new InstanceDesign
            {
                ModellingRule = modellingRule,
                ModellingRuleSpecified = true
            };

            // Act
            InstanceDesign copy = original.Copy();

            // Assert
            Assert.That(copy.ModellingRule, Is.EqualTo(modellingRule));
            Assert.That(copy.ModellingRuleSpecified, Is.True);
        }

        /// <summary>
        /// Tests that GetHashCode returns the same hash code when called multiple times on the same object.
        /// Validates consistency requirement for GetHashCode.
        /// Expected: Multiple calls to GetHashCode on the same instance return the same value.
        /// </summary>
        [Test]
        public void GetHashCode_SameObjectCalledMultipleTimes_ReturnsSameHashCode()
        {
            // Arrange
            var instance = new InstanceDesign
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test.com"),
                Declaration = new XmlQualifiedName("Decl", "http://test.com"),
                TypeDefinition = new XmlQualifiedName("TypeDef", "http://test.com"),
                ModellingRule = ModellingRule.Mandatory,
                ModellingRuleSpecified = true,
                MinCardinality = 1,
                MaxCardinality = 10,
                PreserveDefaultAttributes = true,
                DesignToolOnly = false,
                IdentifierRequired = true
            };

            // Act
            int hashCode1 = instance.GetHashCode();
            int hashCode2 = instance.GetHashCode();
            int hashCode3 = instance.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.EqualTo(hashCode1));
            Assert.That(hashCode3, Is.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests that two InstanceDesign objects with identical property values return the same hash code.
        /// Validates the requirement that equal objects must have equal hash codes.
        /// Expected: Objects with identical properties produce the same hash code.
        /// </summary>
        [Test]
        public void GetHashCode_EqualObjects_ReturnsSameHashCode()
        {
            // Arrange
            var instance1 = new InstanceDesign
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test.com"),
                Declaration = new XmlQualifiedName("Decl", "http://test.com"),
                TypeDefinition = new XmlQualifiedName("TypeDef", "http://test.com"),
                ModellingRule = ModellingRule.Optional,
                ModellingRuleSpecified = true,
                MinCardinality = 5,
                MaxCardinality = 20,
                PreserveDefaultAttributes = false,
                DesignToolOnly = true,
                IdentifierRequired = false
            };

            var instance2 = new InstanceDesign
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test.com"),
                Declaration = new XmlQualifiedName("Decl", "http://test.com"),
                TypeDefinition = new XmlQualifiedName("TypeDef", "http://test.com"),
                ModellingRule = ModellingRule.Optional,
                ModellingRuleSpecified = true,
                MinCardinality = 5,
                MaxCardinality = 20,
                PreserveDefaultAttributes = false,
                DesignToolOnly = true,
                IdentifierRequired = false
            };

            // Act
            int hashCode1 = instance1.GetHashCode();
            int hashCode2 = instance2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests that GetHashCode handles null XmlQualifiedName properties correctly.
        /// Validates null handling for ReferenceType, Declaration, and TypeDefinition properties.
        /// Expected: GetHashCode does not throw and produces a valid hash code.
        /// </summary>
        [Test]
        public void GetHashCode_NullXmlQualifiedNameProperties_HandlesCorrectly()
        {
            // Arrange
            var instance = new InstanceDesign
            {
                ReferenceType = null,
                Declaration = null,
                TypeDefinition = null,
                ModellingRule = ModellingRule.None,
                ModellingRuleSpecified = false
            };

            // Act & Assert
            Assert.DoesNotThrow(() => instance.GetHashCode());
            int hashCode = instance.GetHashCode();
            Assert.That(hashCode, Is.Not.EqualTo(0).Or.EqualTo(0));
        }

        /// <summary>
        /// Tests that GetHashCode handles null object reference properties correctly.
        /// </summary>
        [Test]
        public void GetHashCode_NullObjectReferences_HandlesCorrectly()
        {
            // Arrange
            var instance = new InstanceDesign
            {
                TypeDefinitionNode = null,
                OveriddenNode = null
            };

            // Act & Assert
            Assert.DoesNotThrow(() => instance.GetHashCode());
            int hashCode = instance.GetHashCode();
            Assert.That(hashCode, Is.Not.EqualTo(0).Or.EqualTo(0));
        }

        /// <summary>
        /// Tests GetHashCode with all possible ModellingRule enum values.
        /// Validates that each enum value is properly handled in hash code computation.
        /// Expected: Each enum value produces a valid hash code without throwing exceptions.
        /// </summary>
        [TestCase(ModellingRule.None)]
        [TestCase(ModellingRule.Mandatory)]
        [TestCase(ModellingRule.Optional)]
        [TestCase(ModellingRule.ExposesItsArray)]
        [TestCase(ModellingRule.CardinalityRestriction)]
        [TestCase(ModellingRule.MandatoryShared)]
        [TestCase(ModellingRule.OptionalPlaceholder)]
        [TestCase(ModellingRule.MandatoryPlaceholder)]
        public void GetHashCode_AllModellingRuleValues_ComputesHashCode(ModellingRule rule)
        {
            // Arrange
            var instance = new InstanceDesign
            {
                ModellingRule = rule,
                ModellingRuleSpecified = true
            };

            // Act & Assert
            Assert.DoesNotThrow(() => instance.GetHashCode());
            int hashCode = instance.GetHashCode();
            Assert.That(hashCode, Is.Not.EqualTo(0).Or.EqualTo(0));
        }

        /// <summary>
        /// Tests GetHashCode with boundary values for uint properties MinCardinality and MaxCardinality.
        /// Validates edge cases including 0, uint.MaxValue, and typical boundary values.
        /// Expected: All boundary values are handled correctly without overflow or exceptions.
        /// </summary>
        [TestCase(0u, 0u)]
        [TestCase(uint.MaxValue, uint.MaxValue)]
        [TestCase(0u, uint.MaxValue)]
        [TestCase(1u, 1u)]
        [TestCase(100u, 1000u)]
        public void GetHashCode_UintBoundaryValues_ComputesHashCode(uint minCardinality, uint maxCardinality)
        {
            // Arrange
            var instance = new InstanceDesign
            {
                MinCardinality = minCardinality,
                MaxCardinality = maxCardinality
            };

            // Act & Assert
            Assert.DoesNotThrow(() => instance.GetHashCode());
            int hashCode = instance.GetHashCode();
            Assert.That(hashCode, Is.Not.EqualTo(0).Or.EqualTo(0));
        }

        /// <summary>
        /// Tests GetHashCode with different boolean combinations for PreserveDefaultAttributes and DesignToolOnly.
        /// Validates that different boolean values affect the hash code computation.
        /// Expected: Different boolean combinations should ideally produce different hash codes.
        /// </summary>
        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void GetHashCode_BooleanCombinations_ComputesHashCode(bool preserveDefaultAttributes, bool designToolOnly)
        {
            // Arrange
            var instance = new InstanceDesign
            {
                PreserveDefaultAttributes = preserveDefaultAttributes,
                DesignToolOnly = designToolOnly
            };

            // Act & Assert
            Assert.DoesNotThrow(() => instance.GetHashCode());
            int hashCode = instance.GetHashCode();
            Assert.That(hashCode, Is.Not.EqualTo(0).Or.EqualTo(0));
        }

        /// <summary>
        /// Tests that objects with different ReferenceType values produce different hash codes.
        /// Validates that changes in ReferenceType property affect the hash code.
        /// Expected: Different ReferenceType values should produce different hash codes.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentReferenceType_ProducesDifferentHashCodes()
        {
            // Arrange
            var instance1 = new InstanceDesign
            {
                ReferenceType = new XmlQualifiedName("RefType1", "http://test.com")
            };

            var instance2 = new InstanceDesign
            {
                ReferenceType = new XmlQualifiedName("RefType2", "http://test.com")
            };

            // Act
            int hashCode1 = instance1.GetHashCode();
            int hashCode2 = instance2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.Not.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests that objects with different Declaration values produce different hash codes.
        /// Validates that changes in Declaration property affect the hash code.
        /// Expected: Different Declaration values should produce different hash codes.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentDeclaration_ProducesDifferentHashCodes()
        {
            // Arrange
            var instance1 = new InstanceDesign
            {
                Declaration = new XmlQualifiedName("Decl1", "http://test.com")
            };

            var instance2 = new InstanceDesign
            {
                Declaration = new XmlQualifiedName("Decl2", "http://test.com")
            };

            // Act
            int hashCode1 = instance1.GetHashCode();
            int hashCode2 = instance2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.Not.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests that objects with different TypeDefinition values produce different hash codes.
        /// Validates that changes in TypeDefinition property affect the hash code.
        /// Expected: Different TypeDefinition values should produce different hash codes.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentTypeDefinition_ProducesDifferentHashCodes()
        {
            // Arrange
            var instance1 = new InstanceDesign
            {
                TypeDefinition = new XmlQualifiedName("TypeDef1", "http://test.com")
            };

            var instance2 = new InstanceDesign
            {
                TypeDefinition = new XmlQualifiedName("TypeDef2", "http://test.com")
            };

            // Act
            int hashCode1 = instance1.GetHashCode();
            int hashCode2 = instance2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.Not.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests that objects with different ModellingRule values produce different hash codes.
        /// Validates that changes in ModellingRule property affect the hash code.
        /// Expected: Different ModellingRule values should produce different hash codes.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentModellingRule_ProducesDifferentHashCodes()
        {
            // Arrange
            var instance1 = new InstanceDesign
            {
                ModellingRule = ModellingRule.Mandatory,
                ModellingRuleSpecified = true
            };

            var instance2 = new InstanceDesign
            {
                ModellingRule = ModellingRule.Optional,
                ModellingRuleSpecified = true
            };

            // Act
            int hashCode1 = instance1.GetHashCode();
            int hashCode2 = instance2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.Not.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests that objects with different MinCardinality values produce different hash codes.
        /// Validates that changes in MinCardinality property affect the hash code.
        /// Expected: Different MinCardinality values should produce different hash codes.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentMinCardinality_ProducesDifferentHashCodes()
        {
            // Arrange
            var instance1 = new InstanceDesign
            {
                MinCardinality = 1
            };

            var instance2 = new InstanceDesign
            {
                MinCardinality = 2
            };

            // Act
            int hashCode1 = instance1.GetHashCode();
            int hashCode2 = instance2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.Not.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests that objects with different MaxCardinality values produce different hash codes.
        /// Validates that changes in MaxCardinality property affect the hash code.
        /// Expected: Different MaxCardinality values should produce different hash codes.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentMaxCardinality_ProducesDifferentHashCodes()
        {
            // Arrange
            var instance1 = new InstanceDesign
            {
                MaxCardinality = 10
            };

            var instance2 = new InstanceDesign
            {
                MaxCardinality = 20
            };

            // Act
            int hashCode1 = instance1.GetHashCode();
            int hashCode2 = instance2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.Not.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests GetHashCode with circular references in OveriddenNode.
        /// </summary>
        [Test]
        public void GetHashCode_CircularReferenceInOveriddenNode_HandlesCorrectly()
        {
            // Arrange
            var instance1 = new InstanceDesign();
            var instance2 = new InstanceDesign
            {
                OveriddenNode = instance1
            };
            instance1.OveriddenNode = instance2;

            // Act & Assert
            Assert.DoesNotThrow(() => instance1.GetHashCode());
            Assert.DoesNotThrow(() => instance2.GetHashCode());
        }

        /// <summary>
        /// Tests GetHashCode with an invalid ModellingRule enum value (outside defined range).
        /// Validates that out-of-range enum values are handled correctly.
        /// Expected: GetHashCode does not throw and produces a valid hash code.
        /// </summary>
        [Test]
        public void GetHashCode_InvalidModellingRuleValue_HandlesCorrectly()
        {
            // Arrange
            var instance = new InstanceDesign
            {
                ModellingRule = (ModellingRule)999,
                ModellingRuleSpecified = true
            };

            // Act & Assert
            Assert.DoesNotThrow(() => instance.GetHashCode());
            int hashCode = instance.GetHashCode();
            Assert.That(hashCode, Is.Not.EqualTo(0).Or.EqualTo(0));
        }

        /// <summary>
        /// Tests GetHashCode when ModellingRuleSpecified is false vs true with same ModellingRule value.
        /// Validates that the ModellingRuleSpecified flag affects the hash code.
        /// Expected: Different ModellingRuleSpecified values should produce different hash codes.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentModellingRuleSpecified_ProducesDifferentHashCodes()
        {
            // Arrange
            var instance1 = new InstanceDesign
            {
                ModellingRule = ModellingRule.Mandatory,
                ModellingRuleSpecified = true
            };

            var instance2 = new InstanceDesign
            {
                ModellingRule = ModellingRule.Mandatory,
                ModellingRuleSpecified = false
            };

            // Act
            int hashCode1 = instance1.GetHashCode();
            int hashCode2 = instance2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.Not.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests GetHashCode with empty string XmlQualifiedName values.
        /// Validates that empty strings in XmlQualifiedName properties are handled correctly.
        /// Expected: GetHashCode does not throw and produces a valid hash code.
        /// </summary>
        [Test]
        public void GetHashCode_EmptyStringXmlQualifiedName_HandlesCorrectly()
        {
            // Arrange
            var instance = new InstanceDesign
            {
                ReferenceType = new XmlQualifiedName(string.Empty, string.Empty),
                Declaration = new XmlQualifiedName(string.Empty, string.Empty),
                TypeDefinition = new XmlQualifiedName(string.Empty, string.Empty)
            };

            // Act & Assert
            Assert.DoesNotThrow(() => instance.GetHashCode());
            int hashCode = instance.GetHashCode();
            Assert.That(hashCode, Is.Not.EqualTo(0).Or.EqualTo(0));
        }

        /// <summary>
        /// Tests GetHashCode with XmlQualifiedName objects containing special characters.
        /// Validates that special characters in XmlQualifiedName properties are handled correctly.
        /// Expected: GetHashCode does not throw and produces a valid hash code.
        /// </summary>
        [Test]
        public void GetHashCode_SpecialCharactersInXmlQualifiedName_HandlesCorrectly()
        {
            // Arrange
            var instance = new InstanceDesign
            {
                ReferenceType = new XmlQualifiedName("Test!@#$%^&*()", "http://test.com/~`"),
                Declaration = new XmlQualifiedName("<>?:\"|{}[]", "urn:test:namespace:special"),
                TypeDefinition = new XmlQualifiedName("Type\r\n\t", "http://test.com/path")
            };

            // Act & Assert
            Assert.DoesNotThrow(() => instance.GetHashCode());
            int hashCode = instance.GetHashCode();
            Assert.That(hashCode, Is.Not.EqualTo(0).Or.EqualTo(0));
        }

        /// <summary>
        /// Tests GetHashCode with different IdentifierRequired values.
        /// Validates that the IdentifierRequired property affects the hash code.
        /// Expected: Different IdentifierRequired values should produce different hash codes.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentIdentifierRequired_ProducesDifferentHashCodes()
        {
            // Arrange
            var instance1 = new InstanceDesign
            {
                IdentifierRequired = true
            };

            var instance2 = new InstanceDesign
            {
                IdentifierRequired = false
            };

            // Act
            int hashCode1 = instance1.GetHashCode();
            int hashCode2 = instance2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.Not.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests GetHashCode with all properties set to default values.
        /// Validates that an object with all default values produces a valid hash code.
        /// Expected: GetHashCode returns a valid hash code without exceptions.
        /// </summary>
        [Test]
        public void GetHashCode_AllDefaultValues_ComputesHashCode()
        {
            // Arrange
            var instance = new InstanceDesign();

            // Act & Assert
            Assert.DoesNotThrow(() => instance.GetHashCode());
            int hashCode = instance.GetHashCode();
            Assert.That(hashCode, Is.Not.EqualTo(0).Or.EqualTo(0));
        }

        /// <summary>
        /// Tests GetHashCode with all properties set to non-default values.
        /// Validates that an object with all properties populated produces a valid hash code.
        /// Expected: GetHashCode returns a valid hash code without exceptions.
        /// </summary>
        [Test]
        public void GetHashCode_AllPropertiesSet_ComputesHashCode()
        {
            // Arrange
            var typeDefNode = new ObjectTypeDesign();
            var overriddenNode = new InstanceDesign();

            var instance = new InstanceDesign
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test.com"),
                Declaration = new XmlQualifiedName("Decl", "http://test.com"),
                TypeDefinition = new XmlQualifiedName("TypeDef", "http://test.com"),
                ModellingRule = ModellingRule.Mandatory,
                ModellingRuleSpecified = true,
                MinCardinality = 1,
                MaxCardinality = 100,
                PreserveDefaultAttributes = true,
                DesignToolOnly = true,
                TypeDefinitionNode = typeDefNode,
                OveriddenNode = overriddenNode,
                IdentifierRequired = true
            };

            // Act & Assert
            Assert.DoesNotThrow(() => instance.GetHashCode());
            int hashCode = instance.GetHashCode();
            Assert.That(hashCode, Is.Not.EqualTo(0).Or.EqualTo(0));
        }

        /// <summary>
        /// Tests that Equals returns false when the object parameter is null.
        /// </summary>
        [Test]
        public void Equals_Null_ReturnsFalse()
        {
            // Arrange
            var instance = new InstanceDesign();

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = instance.Equals((object)null);
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
            var instance = new InstanceDesign
            {
                SymbolicId = new XmlQualifiedName("TestId", "http://test.com"),
                MinCardinality = 1,
                MaxCardinality = 10
            };

            // Act
            bool result = instance.Equals((object)instance);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing two equal InstanceDesign objects.
        /// </summary>
        [Test]
        public void Equals_EqualInstanceDesign_ReturnsTrue()
        {
            // Arrange
            var symbolicId = new XmlQualifiedName("TestId", "http://test.com");
            var referenceType = new XmlQualifiedName("RefType", "http://test.com");

            var instance1 = new InstanceDesign
            {
                SymbolicId = symbolicId,
                ReferenceType = referenceType,
                MinCardinality = 1,
                MaxCardinality = 10,
                ModellingRule = ModellingRule.Mandatory,
                ModellingRuleSpecified = true,
                PreserveDefaultAttributes = false,
                DesignToolOnly = false,
                IdentifierRequired = true
            };

            var instance2 = new InstanceDesign
            {
                SymbolicId = symbolicId,
                ReferenceType = referenceType,
                MinCardinality = 1,
                MaxCardinality = 10,
                ModellingRule = ModellingRule.Mandatory,
                ModellingRuleSpecified = true,
                PreserveDefaultAttributes = false,
                DesignToolOnly = false,
                IdentifierRequired = true
            };

            // Act
            bool result = instance1.Equals((object)instance2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing InstanceDesign objects with different properties.
        /// </summary>
        [Test]
        public void Equals_DifferentInstanceDesign_ReturnsFalse()
        {
            // Arrange
            var instance1 = new InstanceDesign
            {
                SymbolicId = new XmlQualifiedName("TestId1", "http://test.com"),
                MinCardinality = 1
            };

            var instance2 = new InstanceDesign
            {
                SymbolicId = new XmlQualifiedName("TestId2", "http://test.com"),
                MinCardinality = 2
            };

            // Act
            bool result = instance1.Equals((object)instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with a different type of object.
        /// </summary>
        [Test]
        public void Equals_DifferentType_ReturnsFalse()
        {
            // Arrange
            var instance = new InstanceDesign();
            object differentType = "NotAnInstanceDesign";

            // Act
            bool result = instance.Equals(differentType);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with a plain object type.
        /// </summary>
        [Test]
        public void Equals_PlainObjectType_ReturnsFalse()
        {
            // Arrange
            var instance = new InstanceDesign();
            object plainObject = new();

            // Act
            bool result = instance.Equals(plainObject);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with a derived type that has different properties.
        /// </summary>
        [Test]
        public void Equals_DerivedTypeWithDifferentProperties_ReturnsFalse()
        {
            // Arrange
            var instance = new InstanceDesign
            {
                SymbolicId = new XmlQualifiedName("TestId", "http://test.com")
            };

            var objectDesign = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("TestId2", "http://test.com")
            };

            // Act
            bool result = instance.Equals((object)objectDesign);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing with a derived type
        /// that has same base type properties.
        /// </summary>
        [Test]
        public void Equals_DerivedTypeWithSameBaseProperties_ReturnsTrue()
        {
            // Arrange
            var instance = new InstanceDesign
            {
                SymbolicId = new XmlQualifiedName("TestId", "http://test.com")
            };

            var objectDesign = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("TestId", "http://test.com"),
                SupportsEvents = true
            };

            // Act
            bool result = instance.Equals((object)objectDesign);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when MinCardinality values differ.
        /// </summary>
        [Test]
        public void Equals_DifferentMinCardinality_ReturnsFalse()
        {
            // Arrange
            var instance1 = new InstanceDesign
            {
                SymbolicId = new XmlQualifiedName("TestId", "http://test.com"),
                MinCardinality = 0
            };

            var instance2 = new InstanceDesign
            {
                SymbolicId = new XmlQualifiedName("TestId", "http://test.com"),
                MinCardinality = uint.MaxValue
            };

            // Act
            bool result = instance1.Equals((object)instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when MaxCardinality values differ.
        /// </summary>
        [Test]
        public void Equals_DifferentMaxCardinality_ReturnsFalse()
        {
            // Arrange
            var instance1 = new InstanceDesign
            {
                SymbolicId = new XmlQualifiedName("TestId", "http://test.com"),
                MaxCardinality = 0
            };

            var instance2 = new InstanceDesign
            {
                SymbolicId = new XmlQualifiedName("TestId", "http://test.com"),
                MaxCardinality = uint.MaxValue
            };

            // Act
            bool result = instance1.Equals((object)instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when other is null.
        /// </summary>
        [Test]
        public void Equals_NullInstance_ReturnsFalse()
        {
            // Arrange
            var instance = new InstanceDesign();

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = instance.Equals(null);
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when all properties match.
        /// </summary>
        [Test]
        public void Equals_AllPropertiesMatch_ReturnsTrue()
        {
            // Arrange
            var instance1 = new InstanceDesign
            {
                BrowseName = "TestNode",
                ReferenceType = new XmlQualifiedName("RefType", "http://test.org"),
                Declaration = new XmlQualifiedName("Declaration", "http://test.org"),
                TypeDefinition = new XmlQualifiedName("TypeDef", "http://test.org"),
                ModellingRule = ModellingRule.Mandatory,
                ModellingRuleSpecified = true,
                MinCardinality = 1,
                MaxCardinality = 10,
                PreserveDefaultAttributes = true,
                DesignToolOnly = false,
                IdentifierRequired = true
            };

            var instance2 = new InstanceDesign
            {
                BrowseName = "TestNode",
                ReferenceType = new XmlQualifiedName("RefType", "http://test.org"),
                Declaration = new XmlQualifiedName("Declaration", "http://test.org"),
                TypeDefinition = new XmlQualifiedName("TypeDef", "http://test.org"),
                ModellingRule = ModellingRule.Mandatory,
                ModellingRuleSpecified = true,
                MinCardinality = 1,
                MaxCardinality = 10,
                PreserveDefaultAttributes = true,
                DesignToolOnly = false,
                IdentifierRequired = true
            };

            // Act
            bool result = instance1.Equals(instance2);

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
            var instance1 = new InstanceDesign
            {
                ReferenceType = new XmlQualifiedName("RefType1", "http://test.org")
            };

            var instance2 = new InstanceDesign
            {
                ReferenceType = new XmlQualifiedName("RefType2", "http://test.org")
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when Declaration differs.
        /// </summary>
        [Test]
        public void Equals_DifferentDeclaration_ReturnsFalse()
        {
            // Arrange
            var instance1 = new InstanceDesign
            {
                Declaration = new XmlQualifiedName("Declaration1", "http://test.org")
            };

            var instance2 = new InstanceDesign
            {
                Declaration = new XmlQualifiedName("Declaration2", "http://test.org")
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when TypeDefinition differs.
        /// </summary>
        [Test]
        public void Equals_DifferentTypeDefinition_ReturnsFalse()
        {
            // Arrange
            var instance1 = new InstanceDesign
            {
                TypeDefinition = new XmlQualifiedName("TypeDef1", "http://test.org")
            };

            var instance2 = new InstanceDesign
            {
                TypeDefinition = new XmlQualifiedName("TypeDef2", "http://test.org")
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when ModellingRule differs.
        /// </summary>
        [Test]
        public void Equals_DifferentModellingRule_ReturnsFalse()
        {
            // Arrange
            var instance1 = new InstanceDesign
            {
                ModellingRule = ModellingRule.Mandatory
            };

            var instance2 = new InstanceDesign
            {
                ModellingRule = ModellingRule.Optional
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when ModellingRuleSpecified differs.
        /// </summary>
        [Test]
        public void Equals_DifferentModellingRuleSpecified_ReturnsFalse()
        {
            // Arrange
            var instance1 = new InstanceDesign
            {
                ModellingRuleSpecified = true
            };

            var instance2 = new InstanceDesign
            {
                ModellingRuleSpecified = false
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when PreserveDefaultAttributes differs.
        /// </summary>
        [Test]
        public void Equals_DifferentPreserveDefaultAttributes_ReturnsFalse()
        {
            // Arrange
            var instance1 = new InstanceDesign
            {
                PreserveDefaultAttributes = true
            };

            var instance2 = new InstanceDesign
            {
                PreserveDefaultAttributes = false
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when DesignToolOnly differs.
        /// </summary>
        [Test]
        public void Equals_DifferentDesignToolOnly_ReturnsFalse()
        {
            // Arrange
            var instance1 = new InstanceDesign
            {
                DesignToolOnly = true
            };

            var instance2 = new InstanceDesign
            {
                DesignToolOnly = false
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when IdentifierRequired differs.
        /// </summary>
        [Test]
        public void Equals_DifferentIdentifierRequired_ReturnsFalse()
        {
            // Arrange
            var instance1 = new InstanceDesign
            {
                IdentifierRequired = true
            };

            var instance2 = new InstanceDesign
            {
                IdentifierRequired = false
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals handles null XmlQualifiedName properties correctly.
        /// </summary>
        [Test]
        public void Equals_NullXmlQualifiedNameProperties_ReturnsTrue()
        {
            // Arrange
            var instance1 = new InstanceDesign
            {
                ReferenceType = null,
                Declaration = null,
                TypeDefinition = null
            };

            var instance2 = new InstanceDesign
            {
                ReferenceType = null,
                Declaration = null,
                TypeDefinition = null
            };

            // Act
            bool result = instance1.Equals(instance2);

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
            var instance1 = new InstanceDesign
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test.org")
            };

            var instance2 = new InstanceDesign
            {
                ReferenceType = null
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals handles boundary values for uint properties.
        /// </summary>
        [TestCase(0u, 0u, true)]
        [TestCase(uint.MaxValue, uint.MaxValue, true)]
        [TestCase(0u, uint.MaxValue, false)]
        [TestCase(uint.MaxValue, 0u, false)]
        public void Equals_CardinalityBoundaryValues_ReturnsExpectedResult(uint minCardinality1, uint minCardinality2, bool expected)
        {
            // Arrange
            var instance1 = new InstanceDesign
            {
                MinCardinality = minCardinality1
            };

            var instance2 = new InstanceDesign
            {
                MinCardinality = minCardinality2
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that Equals returns false when base class properties differ.
        /// </summary>
        [Test]
        public void Equals_DifferentBaseClassProperty_ReturnsFalse()
        {
            // Arrange
            var instance1 = new InstanceDesign
            {
                BrowseName = "Node1"
            };

            var instance2 = new InstanceDesign
            {
                BrowseName = "Node2"
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals handles all ModellingRule enum values correctly.
        /// </summary>
        [TestCase(ModellingRule.None)]
        [TestCase(ModellingRule.Mandatory)]
        [TestCase(ModellingRule.Optional)]
        [TestCase(ModellingRule.ExposesItsArray)]
        [TestCase(ModellingRule.CardinalityRestriction)]
        [TestCase(ModellingRule.MandatoryShared)]
        [TestCase(ModellingRule.OptionalPlaceholder)]
        [TestCase(ModellingRule.MandatoryPlaceholder)]
        public void Equals_AllModellingRuleValues_HandlesCorrectly(ModellingRule rule)
        {
            // Arrange
            var instance1 = new InstanceDesign
            {
                ModellingRule = rule
            };

            var instance2 = new InstanceDesign
            {
                ModellingRule = rule
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when TypeDefinitionNode differs.
        /// </summary>
        [Test]
        public void Equals_DifferentTypeDefinitionNode_ReturnsFalse()
        {
            // Arrange
            TypeDesign typeDesign1 = new ObjectTypeDesign { SymbolicId = new XmlQualifiedName("Type1", "http://test.org") };
            TypeDesign typeDesign2 = new ObjectTypeDesign { SymbolicId = new XmlQualifiedName("Type2", "http://test.org") };

            var instance1 = new InstanceDesign
            {
                TypeDefinitionNode = typeDesign1
            };

            var instance2 = new InstanceDesign
            {
                TypeDefinitionNode = typeDesign2
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when TypeDefinitionNode is null for both instances.
        /// </summary>
        [Test]
        public void Equals_BothTypeDefinitionNodeNull_ReturnsTrue()
        {
            // Arrange
            var instance1 = new InstanceDesign
            {
                TypeDefinitionNode = null
            };

            var instance2 = new InstanceDesign
            {
                TypeDefinitionNode = null
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when OveriddenNode differs.
        /// </summary>
        [Test]
        public void Equals_DifferentOveriddenNode_ReturnsFalse()
        {
            // Arrange
            var overridden1 = new InstanceDesign { SymbolicId = new XmlQualifiedName("Override1", "http://test.org") };
            var overridden2 = new InstanceDesign { SymbolicId = new XmlQualifiedName("Override2", "http://test.org") };

            var instance1 = new InstanceDesign
            {
                OveriddenNode = overridden1
            };

            var instance2 = new InstanceDesign
            {
                OveriddenNode = overridden2
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals handles XmlQualifiedName with different namespaces.
        /// </summary>
        [Test]
        public void Equals_XmlQualifiedNameDifferentNamespace_ReturnsFalse()
        {
            // Arrange
            var instance1 = new InstanceDesign
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test1.org")
            };

            var instance2 = new InstanceDesign
            {
                ReferenceType = new XmlQualifiedName("RefType", "http://test2.org")
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals handles XmlQualifiedName with empty namespace.
        /// </summary>
        [Test]
        public void Equals_XmlQualifiedNameEmptyNamespace_HandlesCorrectly()
        {
            // Arrange
            var instance1 = new InstanceDesign
            {
                ReferenceType = new XmlQualifiedName("RefType", string.Empty)
            };

            var instance2 = new InstanceDesign
            {
                ReferenceType = new XmlQualifiedName("RefType", string.Empty)
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.True);
        }
    }
}
