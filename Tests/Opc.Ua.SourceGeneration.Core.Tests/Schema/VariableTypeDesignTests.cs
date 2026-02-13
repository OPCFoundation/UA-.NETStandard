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
    /// Unit tests for VariableTypeDesign class.
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class VariableTypeDesignTests
    {
        /// <summary>
        /// Tests that GetHashCode returns different hash codes when DefaultValue differs.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentDefaultValue_ReturnsDifferentHashCode()
        {
            // Arrange
            var xmlDoc = new XmlDocument();
            XmlElement defaultValue1 = xmlDoc.CreateElement("Value");
            defaultValue1.InnerText = "42";
            XmlElement defaultValue2 = xmlDoc.CreateElement("Value");
            defaultValue2.InnerText = "100";

            var design1 = new VariableTypeDesign { DefaultValue = defaultValue1 };
            var design2 = new VariableTypeDesign { DefaultValue = defaultValue2 };

            // Act
            int hash1 = design1.GetHashCode();
            int hash2 = design2.GetHashCode();

            // Assert
            Assert.That(hash2, Is.Not.EqualTo(hash1));
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes when DataType differs.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentDataType_ReturnsDifferentHashCode()
        {
            // Arrange
            var design1 = new VariableTypeDesign
            {
                DataType = new XmlQualifiedName("Int32", "http://opcfoundation.org/UA/")
            };
            var design2 = new VariableTypeDesign
            {
                DataType = new XmlQualifiedName("String", "http://opcfoundation.org/UA/")
            };

            // Act
            int hash1 = design1.GetHashCode();
            int hash2 = design2.GetHashCode();

            // Assert
            Assert.That(hash2, Is.Not.EqualTo(hash1));
        }

        /// <summary>
        /// Tests that GetHashCode handles null DataType correctly.
        /// </summary>
        [Test]
        public void GetHashCode_NullDataType_ReturnsValidHashCode()
        {
            // Arrange
            var design = new VariableTypeDesign
            {
                DataType = null
            };

            // Act
            int hash = design.GetHashCode();

            // Assert
            Assert.That(hash, Is.Not.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes when ValueRankSpecified differs.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentValueRankSpecified_ReturnsDifferentHashCode()
        {
            // Arrange
            var design1 = new VariableTypeDesign { ValueRank = ValueRank.Scalar, ValueRankSpecified = true };
            var design2 = new VariableTypeDesign { ValueRank = ValueRank.Scalar, ValueRankSpecified = false };

            // Act
            int hash1 = design1.GetHashCode();
            int hash2 = design2.GetHashCode();

            // Assert
            Assert.That(hash2, Is.Not.EqualTo(hash1));
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes when ArrayDimensions differs.
        /// </summary>
        [TestCase("1,2,3", "4,5,6")]
        [TestCase(null, "1,2,3")]
        [TestCase("", "1,2,3")]
        public void GetHashCode_DifferentArrayDimensions_ReturnsDifferentHashCode(string dim1, string dim2)
        {
            // Arrange
            var design1 = new VariableTypeDesign { ArrayDimensions = dim1 };
            var design2 = new VariableTypeDesign { ArrayDimensions = dim2 };

            // Act
            int hash1 = design1.GetHashCode();
            int hash2 = design2.GetHashCode();

            // Assert
            Assert.That(hash2, Is.Not.EqualTo(hash1));
        }

        /// <summary>
        /// Tests that GetHashCode handles empty and null ArrayDimensions correctly.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        [TestCase("1,2,3")]
        public void GetHashCode_VariousArrayDimensions_ReturnsValidHashCode(string dimensions)
        {
            // Arrange
            var design = new VariableTypeDesign { ArrayDimensions = dimensions };

            // Act
            int hash = design.GetHashCode();

            // Assert
            Assert.That(hash, Is.Not.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes when MinimumSamplingInterval differs.
        /// </summary>
        [TestCase(0, 100)]
        [TestCase(-1, 1)]
        [TestCase(int.MinValue, int.MaxValue)]
        public void GetHashCode_DifferentMinimumSamplingInterval_ReturnsDifferentHashCode(int interval1, int interval2)
        {
            // Arrange
            var design1 = new VariableTypeDesign { MinimumSamplingInterval = interval1, MinimumSamplingIntervalSpecified = true };
            var design2 = new VariableTypeDesign { MinimumSamplingInterval = interval2, MinimumSamplingIntervalSpecified = true };

            // Act
            int hash1 = design1.GetHashCode();
            int hash2 = design2.GetHashCode();

            // Assert
            Assert.That(hash2, Is.Not.EqualTo(hash1));
        }

        /// <summary>
        /// Tests that GetHashCode handles extreme MinimumSamplingInterval values correctly.
        /// </summary>
        [TestCase(int.MinValue)]
        [TestCase(int.MaxValue)]
        [TestCase(0)]
        [TestCase(-1)]
        public void GetHashCode_ExtremeMinimumSamplingInterval_ReturnsValidHashCode(int interval)
        {
            // Arrange
            var design = new VariableTypeDesign { MinimumSamplingInterval = interval };

            // Act
            int hash = design.GetHashCode();

            // Assert
            Assert.That(hash, Is.Not.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes when MinimumSamplingIntervalSpecified differs.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentMinimumSamplingIntervalSpecified_ReturnsDifferentHashCode()
        {
            // Arrange
            var design1 = new VariableTypeDesign { MinimumSamplingInterval = 100, MinimumSamplingIntervalSpecified = true };
            var design2 = new VariableTypeDesign { MinimumSamplingInterval = 100, MinimumSamplingIntervalSpecified = false };

            // Act
            int hash1 = design1.GetHashCode();
            int hash2 = design2.GetHashCode();

            // Assert
            Assert.That(hash2, Is.Not.EqualTo(hash1));
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes when Historizing differs.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentHistorizing_ReturnsDifferentHashCode()
        {
            // Arrange
            var design1 = new VariableTypeDesign { Historizing = true, HistorizingSpecified = true };
            var design2 = new VariableTypeDesign { Historizing = false, HistorizingSpecified = true };

            // Act
            int hash1 = design1.GetHashCode();
            int hash2 = design2.GetHashCode();

            // Assert
            Assert.That(hash2, Is.Not.EqualTo(hash1));
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes when HistorizingSpecified differs.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentHistorizingSpecified_ReturnsDifferentHashCode()
        {
            // Arrange
            var design1 = new VariableTypeDesign { Historizing = true, HistorizingSpecified = true };
            var design2 = new VariableTypeDesign { Historizing = true, HistorizingSpecified = false };

            // Act
            int hash1 = design1.GetHashCode();
            int hash2 = design2.GetHashCode();

            // Assert
            Assert.That(hash2, Is.Not.EqualTo(hash1));
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes when ExposesItsChildren differs.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentExposesItsChildren_ReturnsDifferentHashCode()
        {
            // Arrange
            var design1 = new VariableTypeDesign { ExposesItsChildren = true };
            var design2 = new VariableTypeDesign { ExposesItsChildren = false };

            // Act
            int hash1 = design1.GetHashCode();
            int hash2 = design2.GetHashCode();

            // Assert
            Assert.That(hash2, Is.Not.EqualTo(hash1));
        }

        /// <summary>
        /// Tests that GetHashCode returns valid hash code for default constructed instance.
        /// </summary>
        [Test]
        public void GetHashCode_DefaultConstructor_ReturnsValidHashCode()
        {
            // Arrange
            var design = new VariableTypeDesign();

            // Act
            int hash = design.GetHashCode();

            // Assert
            Assert.That(hash, Is.Not.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode returns valid hash code with null DefaultValue.
        /// </summary>
        [Test]
        public void GetHashCode_NullDefaultValue_ReturnsValidHashCode()
        {
            // Arrange
            var design = new VariableTypeDesign
            {
                DefaultValue = null,
                DataType = new XmlQualifiedName("Int32", "http://opcfoundation.org/UA/")
            };

            // Act
            int hash = design.GetHashCode();

            // Assert
            Assert.That(hash, Is.Not.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes when only DefaultValue differs between null and non-null.
        /// </summary>
        [Test]
        public void GetHashCode_NullVsNonNullDefaultValue_ReturnsDifferentHashCode()
        {
            // Arrange
            var xmlDoc = new XmlDocument();
            XmlElement defaultValue = xmlDoc.CreateElement("Value");
            defaultValue.InnerText = "42";

            var design1 = new VariableTypeDesign { DefaultValue = null };
            var design2 = new VariableTypeDesign { DefaultValue = defaultValue };

            // Act
            int hash1 = design1.GetHashCode();
            int hash2 = design2.GetHashCode();

            // Assert
            Assert.That(hash2, Is.Not.EqualTo(hash1));
        }

        /// <summary>
        /// Tests that GetHashCode with identical XmlQualifiedName properties produces same hash.
        /// </summary>
        [Test]
        public void GetHashCode_IdenticalXmlQualifiedNames_ReturnsSameHashCode()
        {
            // Arrange
            var design1 = new VariableTypeDesign
            {
                DataType = new XmlQualifiedName("CustomType", "http://example.com/")
            };
            var design2 = new VariableTypeDesign
            {
                DataType = new XmlQualifiedName("CustomType", "http://example.com/")
            };

            // Act
            int hash1 = design1.GetHashCode();
            int hash2 = design2.GetHashCode();

            // Assert
            Assert.That(hash2, Is.EqualTo(hash1));
        }

        /// <summary>
        /// Tests that GetHashCode with different XmlQualifiedName namespaces produces different hash.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentXmlQualifiedNameNamespaces_ReturnsDifferentHashCode()
        {
            // Arrange
            var design1 = new VariableTypeDesign
            {
                DataType = new XmlQualifiedName("Type", "http://namespace1.com/")
            };
            var design2 = new VariableTypeDesign
            {
                DataType = new XmlQualifiedName("Type", "http://namespace2.com/")
            };

            // Act
            int hash1 = design1.GetHashCode();
            int hash2 = design2.GetHashCode();

            // Assert
            Assert.That(hash2, Is.Not.EqualTo(hash1));
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with null.
        /// </summary>
        [Test]
        public void Equals_NullParameter_ReturnsFalse()
        {
            // Arrange
            var variableTypeDesign = new VariableTypeDesign();

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = variableTypeDesign.Equals(null);
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when DefaultValue differs.
        /// </summary>
        [Test]
        public void Equals_DifferentDefaultValue_ReturnsFalse()
        {
            // Arrange
            var variableTypeDesign1 = new VariableTypeDesign
            {
                DefaultValue = CreateXmlElement("value1"),
                DataType = new XmlQualifiedName("DataType1", "http://test.com")
            };

            var variableTypeDesign2 = new VariableTypeDesign
            {
                DefaultValue = CreateXmlElement("value2"),
                DataType = new XmlQualifiedName("DataType1", "http://test.com")
            };

            // Act
            bool result = variableTypeDesign1.Equals(variableTypeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when DataType differs.
        /// </summary>
        [Test]
        public void Equals_DifferentDataType_ReturnsFalse()
        {
            // Arrange
            var variableTypeDesign1 = new VariableTypeDesign
            {
                DefaultValue = CreateXmlElement("value1"),
                DataType = new XmlQualifiedName("DataType1", "http://test.com")
            };

            var variableTypeDesign2 = new VariableTypeDesign
            {
                DefaultValue = CreateXmlElement("value1"),
                DataType = new XmlQualifiedName("DataType2", "http://test.com")
            };

            // Act
            bool result = variableTypeDesign1.Equals(variableTypeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when ValueRankSpecified differs.
        /// </summary>
        [Test]
        public void Equals_DifferentValueRankSpecified_ReturnsFalse()
        {
            // Arrange
            var variableTypeDesign1 = new VariableTypeDesign
            {
                ValueRank = ValueRank.Scalar,
                ValueRankSpecified = true
            };

            var variableTypeDesign2 = new VariableTypeDesign
            {
                ValueRank = ValueRank.Scalar,
                ValueRankSpecified = false
            };

            // Act
            bool result = variableTypeDesign1.Equals(variableTypeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when ArrayDimensions differs.
        /// </summary>
        [Test]
        public void Equals_DifferentArrayDimensions_ReturnsFalse()
        {
            // Arrange
            var variableTypeDesign1 = new VariableTypeDesign
            {
                ArrayDimensions = "1,2,3"
            };

            var variableTypeDesign2 = new VariableTypeDesign
            {
                ArrayDimensions = "4,5,6"
            };

            // Act
            bool result = variableTypeDesign1.Equals(variableTypeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when MinimumSamplingInterval differs.
        /// </summary>
        [Test]
        public void Equals_DifferentMinimumSamplingInterval_ReturnsFalse()
        {
            // Arrange
            var variableTypeDesign1 = new VariableTypeDesign
            {
                MinimumSamplingInterval = 100,
                MinimumSamplingIntervalSpecified = true
            };

            var variableTypeDesign2 = new VariableTypeDesign
            {
                MinimumSamplingInterval = 200,
                MinimumSamplingIntervalSpecified = true
            };

            // Act
            bool result = variableTypeDesign1.Equals(variableTypeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when MinimumSamplingIntervalSpecified differs.
        /// </summary>
        [Test]
        public void Equals_DifferentMinimumSamplingIntervalSpecified_ReturnsFalse()
        {
            // Arrange
            var variableTypeDesign1 = new VariableTypeDesign
            {
                MinimumSamplingInterval = 100,
                MinimumSamplingIntervalSpecified = true
            };

            var variableTypeDesign2 = new VariableTypeDesign
            {
                MinimumSamplingInterval = 100,
                MinimumSamplingIntervalSpecified = false
            };

            // Act
            bool result = variableTypeDesign1.Equals(variableTypeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when Historizing differs.
        /// </summary>
        [Test]
        public void Equals_DifferentHistorizing_ReturnsFalse()
        {
            // Arrange
            var variableTypeDesign1 = new VariableTypeDesign
            {
                Historizing = true,
                HistorizingSpecified = true
            };

            var variableTypeDesign2 = new VariableTypeDesign
            {
                Historizing = false,
                HistorizingSpecified = true
            };

            // Act
            bool result = variableTypeDesign1.Equals(variableTypeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when HistorizingSpecified differs.
        /// </summary>
        [Test]
        public void Equals_DifferentHistorizingSpecified_ReturnsFalse()
        {
            // Arrange
            var variableTypeDesign1 = new VariableTypeDesign
            {
                Historizing = true,
                HistorizingSpecified = true
            };

            var variableTypeDesign2 = new VariableTypeDesign
            {
                Historizing = true,
                HistorizingSpecified = false
            };

            // Act
            bool result = variableTypeDesign1.Equals(variableTypeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when ExposesItsChildren differs.
        /// </summary>
        [Test]
        public void Equals_DifferentExposesItsChildren_ReturnsFalse()
        {
            // Arrange
            var variableTypeDesign1 = new VariableTypeDesign
            {
                ExposesItsChildren = true
            };

            var variableTypeDesign2 = new VariableTypeDesign
            {
                ExposesItsChildren = false
            };

            // Act
            bool result = variableTypeDesign1.Equals(variableTypeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when base class properties differ.
        /// </summary>
        [Test]
        public void Equals_DifferentBaseProperties_ReturnsFalse()
        {
            // Arrange
            var variableTypeDesign1 = new VariableTypeDesign
            {
                SymbolicName = new XmlQualifiedName("Type1", "http://test.com")
            };

            var variableTypeDesign2 = new VariableTypeDesign
            {
                SymbolicName = new XmlQualifiedName("Type2", "http://test.com")
            };

            // Act
            bool result = variableTypeDesign1.Equals(variableTypeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals handles null DefaultValue correctly.
        /// </summary>
        [Test]
        public void Equals_NullDefaultValue_ReturnsTrue()
        {
            // Arrange
            var variableTypeDesign1 = new VariableTypeDesign
            {
                DefaultValue = null,
                DataType = new XmlQualifiedName("DataType1", "http://test.com")
            };

            var variableTypeDesign2 = new VariableTypeDesign
            {
                DefaultValue = null,
                DataType = new XmlQualifiedName("DataType1", "http://test.com")
            };

            // Act
            bool result = variableTypeDesign1.Equals(variableTypeDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when one DefaultValue is null and the other is not.
        /// </summary>
        [Test]
        public void Equals_OneDefaultValueNull_ReturnsFalse()
        {
            // Arrange
            var variableTypeDesign1 = new VariableTypeDesign
            {
                DefaultValue = CreateXmlElement("value1"),
                DataType = new XmlQualifiedName("DataType1", "http://test.com")
            };

            var variableTypeDesign2 = new VariableTypeDesign
            {
                DefaultValue = null,
                DataType = new XmlQualifiedName("DataType1", "http://test.com")
            };

            // Act
            bool result = variableTypeDesign1.Equals(variableTypeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals handles null DataType correctly.
        /// </summary>
        [Test]
        public void Equals_NullDataType_ReturnsTrue()
        {
            // Arrange
            var variableTypeDesign1 = new VariableTypeDesign
            {
                DataType = null
            };

            var variableTypeDesign2 = new VariableTypeDesign
            {
                DataType = null
            };

            // Act
            bool result = variableTypeDesign1.Equals(variableTypeDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when one DataType is null and the other is not.
        /// </summary>
        [Test]
        public void Equals_OneDataTypeNull_ReturnsFalse()
        {
            // Arrange
            var variableTypeDesign1 = new VariableTypeDesign
            {
                DataType = new XmlQualifiedName("DataType1", "http://test.com")
            };

            var variableTypeDesign2 = new VariableTypeDesign
            {
                DataType = null
            };

            // Act
            bool result = variableTypeDesign1.Equals(variableTypeDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals handles empty ArrayDimensions correctly.
        /// </summary>
        [Test]
        public void Equals_EmptyArrayDimensions_ReturnsTrue()
        {
            // Arrange
            var variableTypeDesign1 = new VariableTypeDesign
            {
                ArrayDimensions = string.Empty
            };

            var variableTypeDesign2 = new VariableTypeDesign
            {
                ArrayDimensions = string.Empty
            };

            // Act
            bool result = variableTypeDesign1.Equals(variableTypeDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals handles null ArrayDimensions correctly.
        /// </summary>
        [Test]
        public void Equals_NullArrayDimensions_ReturnsTrue()
        {
            // Arrange
            var variableTypeDesign1 = new VariableTypeDesign
            {
                ArrayDimensions = null
            };

            var variableTypeDesign2 = new VariableTypeDesign
            {
                ArrayDimensions = null
            };

            // Act
            bool result = variableTypeDesign1.Equals(variableTypeDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals handles edge cases for MinimumSamplingInterval with extreme values.
        /// </summary>
        [Test]
        [TestCase(int.MinValue)]
        [TestCase(int.MaxValue)]
        [TestCase(0)]
        [TestCase(-1)]
        public void Equals_MinimumSamplingIntervalExtremeValues_WorksCorrectly(int value)
        {
            // Arrange
            var variableTypeDesign1 = new VariableTypeDesign
            {
                MinimumSamplingInterval = value,
                MinimumSamplingIntervalSpecified = true
            };

            var variableTypeDesign2 = new VariableTypeDesign
            {
                MinimumSamplingInterval = value,
                MinimumSamplingIntervalSpecified = true
            };

            // Act
            bool result = variableTypeDesign1.Equals(variableTypeDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Helper method to create an XmlElement with the specified value.
        /// </summary>
        private static XmlElement CreateXmlElement(string value)
        {
            var doc = new XmlDocument();
            XmlElement element = doc.CreateElement("TestElement");
            element.InnerText = value;
            return element;
        }

        /// <summary>
        /// Tests that Equals returns false when obj is null.
        /// Input: null object reference.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_NullObject_ReturnsFalse()
        {
            // Arrange
            var instance = new VariableTypeDesign();

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = instance.Equals((object)null);
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when obj is of a different type.
        /// Input: Object of different type.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_DifferentType_ReturnsFalse()
        {
            // Arrange
            var instance = new VariableTypeDesign();
            object differentType = new();

            // Act
            bool result = instance.Equals(differentType);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when obj is of a different compatible type (string).
        /// Input: String object.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_StringObject_ReturnsFalse()
        {
            // Arrange
            var instance = new VariableTypeDesign();
            object stringObj = "test";

            // Act
            bool result = instance.Equals(stringObj);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing two instances with default property values.
        /// Input: Two instances with default initialization.
        /// Expected: Returns true.
        /// </summary>
        [Test]
        public void Equals_TwoDefaultInstances_ReturnsTrue()
        {
            // Arrange
            var instance1 = new VariableTypeDesign();
            var instance2 = new VariableTypeDesign();

            // Act
            bool result = instance1.Equals((object)instance2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when DataType properties are equal.
        /// Input: Two instances with same DataType value.
        /// Expected: Returns true.
        /// </summary>
        [Test]
        public void Equals_SameDataType_ReturnsTrue()
        {
            // Arrange
            var instance1 = new VariableTypeDesign
            {
                DataType = new XmlQualifiedName("Type1", "namespace1")
            };
            var instance2 = new VariableTypeDesign
            {
                DataType = new XmlQualifiedName("Type1", "namespace1")
            };

            // Act
            bool result = instance1.Equals((object)instance2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when ArrayDimensions is null vs non-null.
        /// Input: One instance with null ArrayDimensions, one with non-null.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_ArrayDimensionsNullVsNonNull_ReturnsFalse()
        {
            // Arrange
            var instance1 = new VariableTypeDesign
            {
                ArrayDimensions = null
            };
            var instance2 = new VariableTypeDesign
            {
                ArrayDimensions = "1,2"
            };

            // Act
            bool result = instance1.Equals((object)instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals handles extreme MinimumSamplingInterval values correctly.
        /// Input: Instances with int.MinValue and int.MaxValue.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_ExtremeMinimumSamplingInterval_ReturnsFalse()
        {
            // Arrange
            var instance1 = new VariableTypeDesign
            {
                MinimumSamplingInterval = int.MinValue,
                MinimumSamplingIntervalSpecified = true
            };
            var instance2 = new VariableTypeDesign
            {
                MinimumSamplingInterval = int.MaxValue,
                MinimumSamplingIntervalSpecified = true
            };

            // Act
            bool result = instance1.Equals((object)instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when DataType is null on both instances.
        /// Input: Two instances with null DataType.
        /// Expected: Returns true.
        /// </summary>
        [Test]
        public void Equals_BothDataTypeNull_ReturnsTrue()
        {
            // Arrange
            var instance1 = new VariableTypeDesign
            {
                DataType = null
            };
            var instance2 = new VariableTypeDesign
            {
                DataType = null
            };

            // Act
            bool result = instance1.Equals((object)instance2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when DataType is null vs non-null.
        /// Input: One instance with null DataType, one with non-null.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void Equals_DataTypeNullVsNonNull_ReturnsFalse()
        {
            // Arrange
            var instance1 = new VariableTypeDesign
            {
                DataType = null
            };
            var instance2 = new VariableTypeDesign
            {
                DataType = new XmlQualifiedName("Type1", "namespace1")
            };

            // Act
            bool result = instance1.Equals((object)instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true with empty string ArrayDimensions.
        /// Input: Two instances with empty string ArrayDimensions.
        /// Expected: Returns true.
        /// </summary>
        [Test]
        public void Equals_EmptyStringArrayDimensions_ReturnsTrue()
        {
            // Arrange
            var instance1 = new VariableTypeDesign
            {
                ArrayDimensions = string.Empty
            };
            var instance2 = new VariableTypeDesign
            {
                ArrayDimensions = string.Empty
            };

            // Act
            bool result = instance1.Equals((object)instance2);

            // Assert
            Assert.That(result, Is.True);
        }
    }
}
