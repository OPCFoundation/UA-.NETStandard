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

using System;
using System.Xml;
using NUnit.Framework;

namespace Opc.Ua.Schema.Model.Tests
{
    /// <summary>
    /// Unit tests for the <see cref="ModelDesign"/> Equals method.
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ModelDesignTests
    {
        /// <summary>
        /// Tests that Equals returns true when comparing the same instance.
        /// Input: Same ModelDesign instance.
        /// Expected: Returns true.
        /// </summary>
        [Test]
        public void Equals_SameInstance_ReturnsTrue()
        {
            // Arrange
            var design = new ModelDesign
            {
                TargetNamespace = "http://test.org",
                DefaultLocale = "en"
            };

            // Act
            bool result = design.Equals(design);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing two instances with
        /// identical values.
        /// Input: Two ModelDesign instances with all properties set to the
        /// same values.
        /// Expected: Returns true.
        /// </summary>
        [Test]
        public void Equals_IdenticalInstances_ReturnsTrue()
        {
            // Arrange
            var date = new DateTime(2025, 1, 1);
            var design1 = new ModelDesign
            {
                Namespaces = [new Namespace()],
                PermissionSets = [new RolePermissionSet()],
                Items = [new ObjectDesign()],
                Extensions = [CreateXmlElement("test")],
                TargetNamespace = "http://test.org",
                TargetVersion = "1.0.0",
                TargetPublicationDate = date,
                TargetXmlNamespace = "http://test.org/xml",
                DefaultLocale = "en"
            };

            var design2 = new ModelDesign
            {
                Namespaces = [new Namespace()],
                PermissionSets = [new RolePermissionSet()],
                Items = [new ObjectDesign()],
                Extensions = [CreateXmlElement("test")],
                TargetNamespace = "http://test.org",
                TargetVersion = "1.0.0",
                TargetPublicationDate = date,
                TargetXmlNamespace = "http://test.org/xml",
                DefaultLocale = "en"
            };

            // Act
            bool result = design1.Equals(design2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when both instances have all
        /// null properties.
        /// Input: Two ModelDesign instances with all nullable properties
        /// set to null.
        /// Expected: Returns true.
        /// </summary>
        [Test]
        public void Equals_BothInstancesWithNullProperties_ReturnsTrue()
        {
            // Arrange
            var design1 = new ModelDesign
            {
                Namespaces = null,
                PermissionSets = null,
                Items = null,
                Extensions = null,
                TargetNamespace = null,
                TargetVersion = null,
                TargetXmlNamespace = null,
                DefaultLocale = null
            };

            var design2 = new ModelDesign
            {
                Namespaces = null,
                PermissionSets = null,
                Items = null,
                Extensions = null,
                TargetNamespace = null,
                TargetVersion = null,
                TargetXmlNamespace = null,
                DefaultLocale = null
            };

            // Act
            bool result = design1.Equals(design2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when TargetNamespace differs.
        /// Input: Two ModelDesign instances where only TargetNamespace differs.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_DifferentTargetNamespace_ReturnsFalse()
        {
            // Arrange
            var design1 = new ModelDesign { TargetNamespace = "http://test1.org" };
            var design2 = new ModelDesign { TargetNamespace = "http://test2.org" };

            // Act
            bool result = design1.Equals(design2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when TargetVersion differs.
        /// Input: Two ModelDesign instances where only TargetVersion differs.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_DifferentTargetVersion_ReturnsFalse()
        {
            // Arrange
            var design1 = new ModelDesign { TargetVersion = "1.0.0" };
            var design2 = new ModelDesign { TargetVersion = "2.0.0" };

            // Act
            bool result = design1.Equals(design2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when TargetPublicationDate differs.
        /// Input: Two ModelDesign instances where only TargetPublicationDate
        /// differs.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_DifferentTargetPublicationDate_ReturnsFalse()
        {
            // Arrange
            var design1 = new ModelDesign { TargetPublicationDate = new DateTime(2025, 1, 1) };
            var design2 = new ModelDesign { TargetPublicationDate = new DateTime(2025, 12, 31) };

            // Act
            bool result = design1.Equals(design2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when TargetXmlNamespace differs.
        /// Input: Two ModelDesign instances where only TargetXmlNamespace differs.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_DifferentTargetXmlNamespace_ReturnsFalse()
        {
            // Arrange
            var design1 = new ModelDesign { TargetXmlNamespace = "http://test1.org/xml" };
            var design2 = new ModelDesign { TargetXmlNamespace = "http://test2.org/xml" };

            // Act
            bool result = design1.Equals(design2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when DefaultLocale differs.
        /// Input: Two ModelDesign instances where only DefaultLocale differs.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_DifferentDefaultLocale_ReturnsFalse()
        {
            // Arrange
            var design1 = new ModelDesign { DefaultLocale = "en" };
            var design2 = new ModelDesign { DefaultLocale = "de" };

            // Act
            bool result = design1.Equals(design2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when Namespaces arrays differ.
        /// Input: Two ModelDesign instances with different Namespaces arrays.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_DifferentNamespaces_ReturnsFalse()
        {
            // Arrange
            var design1 = new ModelDesign { Namespaces = [new Namespace { Name = "Ns1" }] };
            var design2 = new ModelDesign { Namespaces = [new Namespace { Name = "Ns2" }] };

            // Act
            bool result = design1.Equals(design2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when PermissionSets arrays differ.
        /// Input: Two ModelDesign instances with different PermissionSets arrays.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_DifferentPermissionSets_ReturnsFalse()
        {
            // Arrange
            var design1 = new ModelDesign { PermissionSets = [new RolePermissionSet()] };
            var design2 = new ModelDesign { PermissionSets = [] };

            // Act
            bool result = design1.Equals(design2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when Items arrays differ.
        /// Input: Two ModelDesign instances with different Items arrays.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_DifferentItems_ReturnsFalse()
        {
            // Arrange
            var design1 = new ModelDesign { Items = [new ObjectDesign()] };
            var design2 = new ModelDesign { Items = [] };

            // Act
            bool result = design1.Equals(design2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when Extensions arrays differ.
        /// Input: Two ModelDesign instances with different Extensions arrays.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_DifferentExtensions_ReturnsFalse()
        {
            // Arrange
            var design1 = new ModelDesign { Extensions = [CreateXmlElement("ext1")] };
            var design2 = new ModelDesign { Extensions = [CreateXmlElement("ext2")] };

            // Act
            bool result = design1.Equals(design2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when one Namespaces array is null and the other is not.
        /// Input: Two ModelDesign instances where one has null Namespaces and the other has an empty array.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_OneNamespacesNull_ReturnsFalse()
        {
            // Arrange
            var design1 = new ModelDesign { Namespaces = null };
            var design2 = new ModelDesign { Namespaces = [] };

            // Act
            bool result = design1.Equals(design2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when one TargetNamespace is null and the other is not.
        /// Input: Two ModelDesign instances where one has null TargetNamespace and the other has a value.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_OneTargetNamespaceNull_ReturnsFalse()
        {
            // Arrange
            var design1 = new ModelDesign { TargetNamespace = null };
            var design2 = new ModelDesign { TargetNamespace = "http://test.org" };

            // Act
            bool result = design1.Equals(design2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both TargetNamespace are empty strings.
        /// Input: Two ModelDesign instances with empty string TargetNamespace.
        /// Expected: Returns true.
        /// </summary>
        [Test]
        public void Equals_BothTargetNamespaceEmpty_ReturnsTrue()
        {
            // Arrange
            var design1 = new ModelDesign { TargetNamespace = string.Empty };
            var design2 = new ModelDesign { TargetNamespace = string.Empty };

            // Act
            bool result = design1.Equals(design2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals handles DateTime.MinValue correctly.
        /// Input: Two ModelDesign instances with TargetPublicationDate set to DateTime.MinValue.
        /// Expected: Returns true.
        /// </summary>
        [Test]
        public void Equals_DateTimeMinValue_ReturnsTrue()
        {
            // Arrange
            var design1 = new ModelDesign { TargetPublicationDate = DateTime.MinValue };
            var design2 = new ModelDesign { TargetPublicationDate = DateTime.MinValue };

            // Act
            bool result = design1.Equals(design2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals handles DateTime.MaxValue correctly.
        /// Input: Two ModelDesign instances with TargetPublicationDate set to DateTime.MaxValue.
        /// Expected: Returns true.
        /// </summary>
        [Test]
        public void Equals_DateTimeMaxValue_ReturnsTrue()
        {
            // Arrange
            var design1 = new ModelDesign { TargetPublicationDate = DateTime.MaxValue };
            var design2 = new ModelDesign { TargetPublicationDate = DateTime.MaxValue };

            // Act
            bool result = design1.Equals(design2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when both instances have empty arrays.
        /// Input: Two ModelDesign instances with all arrays initialized but empty.
        /// Expected: Returns true.
        /// </summary>
        [Test]
        public void Equals_BothWithEmptyArrays_ReturnsTrue()
        {
            // Arrange
            var design1 = new ModelDesign
            {
                Namespaces = [],
                PermissionSets = [],
                Items = [],
                Extensions = []
            };

            var design2 = new ModelDesign
            {
                Namespaces = [],
                PermissionSets = [],
                Items = [],
                Extensions = []
            };

            // Act
            bool result = design1.Equals(design2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when Namespaces array lengths differ.
        /// Input: Two ModelDesign instances with Namespaces arrays of different lengths.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_DifferentNamespacesLength_ReturnsFalse()
        {
            // Arrange
            var design1 = new ModelDesign { Namespaces = [new Namespace(), new Namespace()] };
            var design2 = new ModelDesign { Namespaces = [new Namespace()] };

            // Act
            bool result = design1.Equals(design2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals handles whitespace-only strings correctly.
        /// Input: Two ModelDesign instances with whitespace-only TargetNamespace.
        /// Expected: Returns true.
        /// </summary>
        [Test]
        public void Equals_WhitespaceOnlyStrings_ReturnsTrue()
        {
            // Arrange
            var design1 = new ModelDesign { TargetNamespace = "   " };
            var design2 = new ModelDesign { TargetNamespace = "   " };

            // Act
            bool result = design1.Equals(design2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing whitespace with empty string.
        /// Input: Two ModelDesign instances, one with whitespace TargetNamespace and one with empty string.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_WhitespaceVsEmpty_ReturnsFalse()
        {
            // Arrange
            var design1 = new ModelDesign { TargetNamespace = "   " };
            var design2 = new ModelDesign { TargetNamespace = string.Empty };

            // Act
            bool result = design1.Equals(design2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals handles very long strings correctly.
        /// Input: Two ModelDesign instances with very long TargetNamespace values.
        /// Expected: Returns true when identical.
        /// </summary>
        [Test]
        public void Equals_VeryLongStrings_ReturnsTrue()
        {
            // Arrange
            string longString = new('a', 10000);
            var design1 = new ModelDesign { TargetNamespace = longString };
            var design2 = new ModelDesign { TargetNamespace = longString };

            // Act
            bool result = design1.Equals(design2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals handles strings with special characters correctly.
        /// Input: Two ModelDesign instances with special characters in TargetNamespace.
        /// Expected: Returns true when identical.
        /// </summary>
        [Test]
        public void Equals_SpecialCharactersInStrings_ReturnsTrue()
        {
            // Arrange
            const string specialString = "http://test.org/\r\n\t<>&\"'";
            var design1 = new ModelDesign { TargetNamespace = specialString };
            var design2 = new ModelDesign { TargetNamespace = specialString };

            // Act
            bool result = design1.Equals(design2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Helper method to create an XmlElement for testing.
        /// </summary>
        private static XmlElement CreateXmlElement(string content)
        {
            var doc = new XmlDocument();
            XmlElement element = doc.CreateElement("TestElement");
            element.InnerText = content;
            return element;
        }

        /// <summary>
        /// Tests that Equals returns false when the parameter is null.
        /// </summary>
        [Test]
        public void Equals_NullObject_ReturnsFalse()
        {
            // Arrange
            var modelDesign = new ModelDesign();

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = modelDesign.Equals((object)null);
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when the parameter is of a different type.
        /// </summary>
        [Test]
        public void Equals_DifferentType_ReturnsFalse()
        {
            // Arrange
            var modelDesign = new ModelDesign();
            object differentType = new();

            // Act
            bool result = modelDesign.Equals(differentType);

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
            var modelDesign = new ModelDesign();
            const string stringObject = "test";

            // Act
            bool result = modelDesign.Equals(stringObject);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing two default ModelDesign instances.
        /// </summary>
        [Test]
        public void Equals_TwoDefaultInstances_ReturnsTrue()
        {
            // Arrange
            var modelDesign1 = new ModelDesign();
            var modelDesign2 = new ModelDesign();

            // Act
            bool result = modelDesign1.Equals((object)modelDesign2);

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
            var modelDesign1 = new ModelDesign
            {
                TargetNamespace = "http://example.com/ns",
                TargetVersion = "1.0.0",
                TargetXmlNamespace = "http://example.com/xml",
                DefaultLocale = "en"
            };
            var modelDesign2 = new ModelDesign
            {
                TargetNamespace = "http://example.com/ns",
                TargetVersion = "1.0.0",
                TargetXmlNamespace = "http://example.com/xml",
                DefaultLocale = "en"
            };

            // Act
            bool result = modelDesign1.Equals((object)modelDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when Namespaces arrays differ.
        /// </summary>
        [Test]
        public void Equals_DifferentNamespacesArray_ReturnsFalse()
        {
            // Arrange
            var modelDesign1 = new ModelDesign { Namespaces = [new Namespace()] };
            var modelDesign2 = new ModelDesign { Namespaces = null };

            // Act
            bool result = modelDesign1.Equals((object)modelDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when Items arrays differ.
        /// </summary>
        [Test]
        public void Equals_DifferentItemsArray_ReturnsFalse()
        {
            // Arrange
            var modelDesign1 = new ModelDesign { Items = [new ObjectDesign()] };
            var modelDesign2 = new ModelDesign { Items = null };

            // Act
            bool result = modelDesign1.Equals((object)modelDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when one property is null and the other is not.
        /// </summary>
        [Test]
        public void Equals_NullVsNonNullProperty_ReturnsFalse()
        {
            // Arrange
            var modelDesign1 = new ModelDesign { TargetNamespace = null };
            var modelDesign2 = new ModelDesign { TargetNamespace = "http://example.com/ns" };

            // Act
            bool result = modelDesign1.Equals((object)modelDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals handles empty string properties correctly.
        /// </summary>
        [Test]
        public void Equals_EmptyStringProperties_ReturnsTrue()
        {
            // Arrange
            var modelDesign1 = new ModelDesign
            {
                TargetNamespace = string.Empty,
                TargetVersion = string.Empty
            };
            var modelDesign2 = new ModelDesign
            {
                TargetNamespace = string.Empty,
                TargetVersion = string.Empty
            };

            // Act
            bool result = modelDesign1.Equals((object)modelDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that GetHashCode returns consistent hash codes for equal objects.
        /// Verifies that two instances with identical property values produce the same hash code.
        /// </summary>
        [Test]
        public void GetHashCode_EqualObjects_ReturnsSameHashCode()
        {
            // Arrange
            var design1 = new ModelDesign
            {
                Namespaces = [new Namespace { Name = "Test" }],
                PermissionSets = [new RolePermissionSet()],
                Items = [new ObjectDesign()],
                Extensions = [CreateXmlElement("test")],
                TargetNamespace = "http://test.com",
                TargetVersion = "1.0",
                TargetPublicationDate = new DateTime(2024, 1, 1),
                TargetXmlNamespace = "http://test.xml.com",
                DefaultLocale = "en"
            };

            var design2 = new ModelDesign
            {
                Namespaces = [new Namespace { Name = "Test" }],
                PermissionSets = [new RolePermissionSet()],
                Items = [new ObjectDesign()],
                Extensions = [CreateXmlElement("test")],
                TargetNamespace = "http://test.com",
                TargetVersion = "1.0",
                TargetPublicationDate = new DateTime(2024, 1, 1),
                TargetXmlNamespace = "http://test.xml.com",
                DefaultLocale = "en"
            };

            // Act
            int hash1 = design1.GetHashCode();
            int hash2 = design2.GetHashCode();

            // Assert
            Assert.That(hash1, Is.EqualTo(hash2));
        }

        /// <summary>
        /// Tests that GetHashCode works correctly with all properties set to null.
        /// Verifies that the method handles null values gracefully without throwing exceptions.
        /// </summary>
        [Test]
        public void GetHashCode_AllPropertiesNull_ReturnsHashCode()
        {
            // Arrange
            var design = new ModelDesign
            {
                Namespaces = null,
                PermissionSets = null,
                Items = null,
                Extensions = null,
                TargetNamespace = null,
                TargetVersion = null,
                TargetXmlNamespace = null,
                DefaultLocale = null
            };

            // Act
            int hashCode = design.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode returns consistent values when called multiple times on the same object.
        /// Verifies hash code consistency for the same instance.
        /// </summary>
        [Test]
        public void GetHashCode_SameInstance_ReturnsConsistentHashCode()
        {
            // Arrange
            var design = new ModelDesign
            {
                TargetNamespace = "http://test.com",
                TargetVersion = "1.0"
            };

            // Act
            int hash1 = design.GetHashCode();
            int hash2 = design.GetHashCode();
            int hash3 = design.GetHashCode();

            // Assert
            Assert.That(hash1, Is.EqualTo(hash2));
            Assert.That(hash2, Is.EqualTo(hash3));
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes when TargetNamespace differs.
        /// Verifies that changes in TargetNamespace affect the hash code.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentTargetNamespace_ReturnsDifferentHashCode()
        {
            // Arrange
            var design1 = new ModelDesign { TargetNamespace = "http://test1.com" };
            var design2 = new ModelDesign { TargetNamespace = "http://test2.com" };

            // Act
            int hash1 = design1.GetHashCode();
            int hash2 = design2.GetHashCode();

            // Assert
            Assert.That(hash1, Is.Not.EqualTo(hash2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes when TargetVersion differs.
        /// Verifies that changes in TargetVersion affect the hash code.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentTargetVersion_ReturnsDifferentHashCode()
        {
            // Arrange
            var design1 = new ModelDesign { TargetVersion = "1.0" };
            var design2 = new ModelDesign { TargetVersion = "2.0" };

            // Act
            int hash1 = design1.GetHashCode();
            int hash2 = design2.GetHashCode();

            // Assert
            Assert.That(hash1, Is.Not.EqualTo(hash2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes when TargetPublicationDate differs.
        /// Verifies that changes in TargetPublicationDate affect the hash code.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentTargetPublicationDate_ReturnsDifferentHashCode()
        {
            // Arrange
            var design1 = new ModelDesign { TargetPublicationDate = new DateTime(2024, 1, 1) };
            var design2 = new ModelDesign { TargetPublicationDate = new DateTime(2024, 12, 31) };

            // Act
            int hash1 = design1.GetHashCode();
            int hash2 = design2.GetHashCode();

            // Assert
            Assert.That(hash1, Is.Not.EqualTo(hash2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes when TargetXmlNamespace differs.
        /// Verifies that changes in TargetXmlNamespace affect the hash code.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentTargetXmlNamespace_ReturnsDifferentHashCode()
        {
            // Arrange
            var design1 = new ModelDesign { TargetXmlNamespace = "http://xml1.com" };
            var design2 = new ModelDesign { TargetXmlNamespace = "http://xml2.com" };

            // Act
            int hash1 = design1.GetHashCode();
            int hash2 = design2.GetHashCode();

            // Assert
            Assert.That(hash1, Is.Not.EqualTo(hash2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes when DefaultLocale differs.
        /// Verifies that changes in DefaultLocale affect the hash code.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentDefaultLocale_ReturnsDifferentHashCode()
        {
            // Arrange
            var design1 = new ModelDesign { DefaultLocale = "en" };
            var design2 = new ModelDesign { DefaultLocale = "de" };

            // Act
            int hash1 = design1.GetHashCode();
            int hash2 = design2.GetHashCode();

            // Assert
            Assert.That(hash1, Is.Not.EqualTo(hash2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes when Namespaces array differs.
        /// Verifies that changes in Namespaces array affect the hash code.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentNamespaces_ReturnsDifferentHashCode()
        {
            // Arrange
            var design1 = new ModelDesign
            {
                Namespaces = [new Namespace { Name = "Namespace1" }]
            };
            var design2 = new ModelDesign
            {
                Namespaces = [new Namespace { Name = "Namespace2" }]
            };

            // Act
            int hash1 = design1.GetHashCode();
            int hash2 = design2.GetHashCode();

            // Assert
            Assert.That(hash1, Is.Not.EqualTo(hash2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes when PermissionSets array differs.
        /// Verifies that changes in PermissionSets array affect the hash code.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentPermissionSets_ReturnsDifferentHashCode()
        {
            // Arrange
            var design1 = new ModelDesign
            {
                PermissionSets = [new RolePermissionSet { Name = "Set1" }]
            };
            var design2 = new ModelDesign
            {
                PermissionSets = [new RolePermissionSet { Name = "Set2" }]
            };

            // Act
            int hash1 = design1.GetHashCode();
            int hash2 = design2.GetHashCode();

            // Assert
            Assert.That(hash1, Is.Not.EqualTo(hash2));
        }

        /// <summary>
        /// Tests that GetHashCode works correctly with empty arrays.
        /// Verifies that empty arrays are handled properly in hash code calculation.
        /// </summary>
        [Test]
        public void GetHashCode_EmptyArrays_ReturnsHashCode()
        {
            // Arrange
            var design = new ModelDesign
            {
                Namespaces = [],
                PermissionSets = [],
                Items = [],
                Extensions = []
            };

            // Act
            int hashCode = design.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero);
        }

        /// <summary>
        /// Validates that empty arrays vs null array hash code computation.
        /// </summary>
        [Test]
        public void GetHashCode_EmptyVsNullArrays_ReturnsDifferentHashCode()
        {
            // Arrange
            var design1 = new ModelDesign { Namespaces = [] };
            var design2 = new ModelDesign { Namespaces = null };

            // Act
            int hash1 = design1.GetHashCode();
            int hash2 = design2.GetHashCode();

            // Assert
            Assert.That(hash1, Is.Not.EqualTo(hash2));
        }

        /// <summary>
        /// Tests that GetHashCode handles empty strings correctly.
        /// Verifies that empty strings are properly included in hash code calculation.
        /// </summary>
        [Test]
        public void GetHashCode_EmptyStrings_ReturnsHashCode()
        {
            // Arrange
            var design = new ModelDesign
            {
                TargetNamespace = string.Empty,
                TargetVersion = string.Empty,
                TargetXmlNamespace = string.Empty,
                DefaultLocale = string.Empty
            };

            // Act
            int hashCode = design.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes for empty vs null strings.
        /// Verifies that null and empty strings produce different hash codes.
        /// </summary>
        [Test]
        public void GetHashCode_EmptyVsNullStrings_ReturnsDifferentHashCode()
        {
            // Arrange
            var design1 = new ModelDesign { TargetNamespace = string.Empty };
            var design2 = new ModelDesign { TargetNamespace = null };

            // Act
            int hash1 = design1.GetHashCode();
            int hash2 = design2.GetHashCode();

            // Assert
            Assert.That(hash1, Is.Not.EqualTo(hash2));
        }

        /// <summary>
        /// Tests that GetHashCode handles DateTime edge cases correctly.
        /// Verifies hash code calculation with DateTime.MinValue.
        /// </summary>
        [Test]
        public void GetHashCode_DateTimeMinValue_ReturnsHashCode()
        {
            // Arrange
            var design = new ModelDesign
            {
                TargetPublicationDate = DateTime.MinValue
            };

            // Act
            int hashCode = design.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode handles DateTime edge cases correctly.
        /// Verifies hash code calculation with DateTime.MaxValue.
        /// </summary>
        [Test]
        public void GetHashCode_DateTimeMaxValue_ReturnsHashCode()
        {
            // Arrange
            var design = new ModelDesign
            {
                TargetPublicationDate = DateTime.MaxValue
            };

            // Act
            int hashCode = design.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode handles whitespace strings correctly.
        /// Verifies that whitespace-only strings are properly included in hash code calculation.
        /// </summary>
        [Test]
        public void GetHashCode_WhitespaceStrings_ReturnsHashCode()
        {
            // Arrange
            var design = new ModelDesign
            {
                TargetNamespace = "   ",
                TargetVersion = "\t",
                DefaultLocale = "  \n  "
            };

            // Act
            int hashCode = design.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode handles very long strings correctly.
        /// Verifies that hash code calculation works with large string values.
        /// </summary>
        [Test]
        public void GetHashCode_VeryLongStrings_ReturnsHashCode()
        {
            // Arrange
            string longString = new('a', 10000);
            var design = new ModelDesign
            {
                TargetNamespace = longString,
                TargetVersion = longString,
                TargetXmlNamespace = longString,
                DefaultLocale = longString
            };

            // Act
            int hashCode = design.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode handles special characters in strings correctly.
        /// Verifies that strings with special characters are properly included in hash code calculation.
        /// </summary>
        [Test]
        public void GetHashCode_SpecialCharactersInStrings_ReturnsHashCode()
        {
            // Arrange
            var design = new ModelDesign
            {
                TargetNamespace = "http://test.com/\u0000\u0001\u0002",
                TargetVersion = "1.0-alpha+build.123",
                DefaultLocale = "en-US-x-special"
            };

            // Act
            int hashCode = design.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode handles arrays with multiple elements correctly.
        /// Verifies that arrays with multiple elements are properly included in hash code calculation.
        /// </summary>
        [Test]
        public void GetHashCode_MultipleArrayElements_ReturnsHashCode()
        {
            // Arrange
            var design = new ModelDesign
            {
                Namespaces =
                [
                    new Namespace { Name = "NS1" },
                    new Namespace { Name = "NS2" },
                    new Namespace { Name = "NS3" }
                ],
                PermissionSets =
                [
                    new RolePermissionSet { Name = "Set1" },
                    new RolePermissionSet { Name = "Set2" }
                ]
            };

            // Act
            int hashCode = design.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero);
        }
    }
}
