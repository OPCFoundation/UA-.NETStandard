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
    /// Unit tests for VariableDesign.Equals method
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class VariableDesignTests
    {
        /// <summary>
        /// Tests that Equals returns false when comparing with null.
        /// </summary>
        [Test]
        public void Equals_NullParameter_ReturnsFalse()
        {
            // Arrange
            var variableDesign = new VariableDesign();

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = variableDesign.Equals(null);
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
            var xmlDoc = new XmlDocument();
            XmlElement defaultValue1 = xmlDoc.CreateElement("DefaultValue");
            defaultValue1.InnerText = "TestValue1";

            XmlElement defaultValue2 = xmlDoc.CreateElement("DefaultValue");
            defaultValue2.InnerText = "TestValue2";

            var variableDesign1 = new VariableDesign
            {
                DefaultValue = defaultValue1,
                DataType = new XmlQualifiedName("DataType1", "http://test.com"),
                ValueRank = ValueRank.Scalar
            };

            var variableDesign2 = new VariableDesign
            {
                DefaultValue = defaultValue2,
                DataType = new XmlQualifiedName("DataType1", "http://test.com"),
                ValueRank = ValueRank.Scalar
            };

            // Act
            bool result = variableDesign1.Equals(variableDesign2);

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
            var variableDesign1 = new VariableDesign
            {
                DataType = new XmlQualifiedName("DataType1", "http://test.com"),
                ValueRank = ValueRank.Scalar
            };

            var variableDesign2 = new VariableDesign
            {
                DataType = new XmlQualifiedName("DataType2", "http://test.com"),
                ValueRank = ValueRank.Scalar
            };

            // Act
            bool result = variableDesign1.Equals(variableDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when DataType namespace differs.
        /// </summary>
        [Test]
        public void Equals_DifferentDataTypeNamespace_ReturnsFalse()
        {
            // Arrange
            var variableDesign1 = new VariableDesign
            {
                DataType = new XmlQualifiedName("DataType1", "http://test1.com"),
                ValueRank = ValueRank.Scalar
            };

            var variableDesign2 = new VariableDesign
            {
                DataType = new XmlQualifiedName("DataType1", "http://test2.com"),
                ValueRank = ValueRank.Scalar
            };

            // Act
            bool result = variableDesign1.Equals(variableDesign2);

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
            var variableDesign1 = new VariableDesign
            {
                DataType = new XmlQualifiedName("DataType1", "http://test.com"),
                ArrayDimensions = "1,2,3"
            };

            var variableDesign2 = new VariableDesign
            {
                DataType = new XmlQualifiedName("DataType1", "http://test.com"),
                ArrayDimensions = "4,5,6"
            };

            // Act
            bool result = variableDesign1.Equals(variableDesign2);

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
            var variableDesign1 = new VariableDesign
            {
                DataType = new XmlQualifiedName("DataType1", "http://test.com"),
                MinimumSamplingInterval = 100
            };

            var variableDesign2 = new VariableDesign
            {
                DataType = new XmlQualifiedName("DataType1", "http://test.com"),
                MinimumSamplingInterval = 200
            };

            // Act
            bool result = variableDesign1.Equals(variableDesign2);

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
            var variableDesign1 = new VariableDesign
            {
                DataType = new XmlQualifiedName("DataType1", "http://test.com"),
                MinimumSamplingInterval = 100,
                MinimumSamplingIntervalSpecified = true
            };

            var variableDesign2 = new VariableDesign
            {
                DataType = new XmlQualifiedName("DataType1", "http://test.com"),
                MinimumSamplingInterval = 100,
                MinimumSamplingIntervalSpecified = false
            };

            // Act
            bool result = variableDesign1.Equals(variableDesign2);

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
            var variableDesign1 = new VariableDesign
            {
                DataType = new XmlQualifiedName("DataType1", "http://test.com"),
                Historizing = true
            };

            var variableDesign2 = new VariableDesign
            {
                DataType = new XmlQualifiedName("DataType1", "http://test.com"),
                Historizing = false
            };

            // Act
            bool result = variableDesign1.Equals(variableDesign2);

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
            var variableDesign1 = new VariableDesign
            {
                DataType = new XmlQualifiedName("DataType1", "http://test.com"),
                Historizing = true,
                HistorizingSpecified = true
            };

            var variableDesign2 = new VariableDesign
            {
                DataType = new XmlQualifiedName("DataType1", "http://test.com"),
                Historizing = true,
                HistorizingSpecified = false
            };

            // Act
            bool result = variableDesign1.Equals(variableDesign2);

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
            var variableDesign1 = new VariableDesign
            {
                DefaultValue = null,
                DataType = new XmlQualifiedName("DataType1", "http://test.com")
            };

            var variableDesign2 = new VariableDesign
            {
                DefaultValue = null,
                DataType = new XmlQualifiedName("DataType1", "http://test.com")
            };

            // Act
            bool result = variableDesign1.Equals(variableDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when one has null DefaultValue and the other does not.
        /// </summary>
        [Test]
        public void Equals_OneNullDefaultValue_ReturnsFalse()
        {
            // Arrange
            var xmlDoc = new XmlDocument();
            XmlElement defaultValue = xmlDoc.CreateElement("DefaultValue");
            defaultValue.InnerText = "TestValue";

            var variableDesign1 = new VariableDesign
            {
                DefaultValue = defaultValue,
                DataType = new XmlQualifiedName("DataType1", "http://test.com")
            };

            var variableDesign2 = new VariableDesign
            {
                DefaultValue = null,
                DataType = new XmlQualifiedName("DataType1", "http://test.com")
            };

            // Act
            bool result = variableDesign1.Equals(variableDesign2);

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
            var variableDesign1 = new VariableDesign
            {
                DataType = null,
                ValueRank = ValueRank.Scalar
            };

            var variableDesign2 = new VariableDesign
            {
                DataType = null,
                ValueRank = ValueRank.Scalar
            };

            // Act
            bool result = variableDesign1.Equals(variableDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when one has null DataType and the other does not.
        /// </summary>
        [Test]
        public void Equals_OneNullDataType_ReturnsFalse()
        {
            // Arrange
            var variableDesign1 = new VariableDesign
            {
                DataType = new XmlQualifiedName("DataType1", "http://test.com"),
                ValueRank = ValueRank.Scalar
            };

            var variableDesign2 = new VariableDesign
            {
                DataType = null,
                ValueRank = ValueRank.Scalar
            };

            // Act
            bool result = variableDesign1.Equals(variableDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals handles null and empty ArrayDimensions correctly.
        /// </summary>
        [Test]
        public void Equals_NullArrayDimensions_ReturnsTrue()
        {
            // Arrange
            var variableDesign1 = new VariableDesign
            {
                DataType = new XmlQualifiedName("DataType1", "http://test.com"),
                ArrayDimensions = null
            };

            var variableDesign2 = new VariableDesign
            {
                DataType = new XmlQualifiedName("DataType1", "http://test.com"),
                ArrayDimensions = null
            };

            // Act
            bool result = variableDesign1.Equals(variableDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when ArrayDimensions is empty string vs null.
        /// </summary>
        [Test]
        public void Equals_EmptyVsNullArrayDimensions_ReturnsFalse()
        {
            // Arrange
            var variableDesign1 = new VariableDesign
            {
                DataType = new XmlQualifiedName("DataType1", "http://test.com"),
                ArrayDimensions = string.Empty
            };

            var variableDesign2 = new VariableDesign
            {
                DataType = new XmlQualifiedName("DataType1", "http://test.com"),
                ArrayDimensions = null
            };

            // Act
            bool result = variableDesign1.Equals(variableDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals with extreme MinimumSamplingInterval values.
        /// </summary>
        [Test]
        public void Equals_ExtremeMinimumSamplingInterval_ReturnsTrueForIdentical()
        {
            // Arrange
            var variableDesign1 = new VariableDesign
            {
                DataType = new XmlQualifiedName("DataType1", "http://test.com"),
                MinimumSamplingInterval = int.MaxValue
            };

            var variableDesign2 = new VariableDesign
            {
                DataType = new XmlQualifiedName("DataType1", "http://test.com"),
                MinimumSamplingInterval = int.MaxValue
            };

            // Act
            bool result = variableDesign1.Equals(variableDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals with minimum MinimumSamplingInterval values.
        /// </summary>
        [Test]
        public void Equals_MinimumSamplingInterval_MinValue_ReturnsTrueForIdentical()
        {
            // Arrange
            var variableDesign1 = new VariableDesign
            {
                DataType = new XmlQualifiedName("DataType1", "http://test.com"),
                MinimumSamplingInterval = int.MinValue
            };

            var variableDesign2 = new VariableDesign
            {
                DataType = new XmlQualifiedName("DataType1", "http://test.com"),
                MinimumSamplingInterval = int.MinValue
            };

            // Act
            bool result = variableDesign1.Equals(variableDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals with zero MinimumSamplingInterval.
        /// </summary>
        [Test]
        public void Equals_MinimumSamplingInterval_Zero_ReturnsTrueForIdentical()
        {
            // Arrange
            var variableDesign1 = new VariableDesign
            {
                DataType = new XmlQualifiedName("DataType1", "http://test.com"),
                MinimumSamplingInterval = 0
            };

            var variableDesign2 = new VariableDesign
            {
                DataType = new XmlQualifiedName("DataType1", "http://test.com"),
                MinimumSamplingInterval = 0
            };

            // Act
            bool result = variableDesign1.Equals(variableDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that GetHashCode works with all null reference properties.
        /// </summary>
        [Test]
        public void GetHashCode_AllNullablePropertiesNull_DoesNotThrow()
        {
            // Arrange
            var variableDesign = new VariableDesign
            {
                DefaultValue = null,
                DataType = null,
                ArrayDimensions = null
            };

            // Act & Assert
            Assert.DoesNotThrow(() => variableDesign.GetHashCode());
        }

        /// <summary>
        /// Tests GetHashCode with minimum MinimumSamplingInterval value.
        /// </summary>
        [Test]
        public void GetHashCode_MinimumSamplingIntervalMinValue_DoesNotThrow()
        {
            // Arrange
            var variableDesign = new VariableDesign
            {
                MinimumSamplingInterval = int.MinValue,
                MinimumSamplingIntervalSpecified = true
            };

            // Act & Assert
            Assert.DoesNotThrow(() => variableDesign.GetHashCode());
        }

        /// <summary>
        /// Tests GetHashCode with maximum MinimumSamplingInterval value.
        /// </summary>
        [Test]
        public void GetHashCode_MinimumSamplingIntervalMaxValue_DoesNotThrow()
        {
            // Arrange
            var variableDesign = new VariableDesign
            {
                MinimumSamplingInterval = int.MaxValue,
                MinimumSamplingIntervalSpecified = true
            };

            // Act & Assert
            Assert.DoesNotThrow(() => variableDesign.GetHashCode());
        }

        /// <summary>
        /// Tests GetHashCode with zero MinimumSamplingInterval value.
        /// </summary>
        [Test]
        public void GetHashCode_MinimumSamplingIntervalZero_DoesNotThrow()
        {
            // Arrange
            var variableDesign = new VariableDesign
            {
                MinimumSamplingInterval = 0,
                MinimumSamplingIntervalSpecified = true
            };

            // Act & Assert
            Assert.DoesNotThrow(() => variableDesign.GetHashCode());
        }

        /// <summary>
        /// Tests GetHashCode with all boolean "Specified" flags set to false.
        /// </summary>
        [Test]
        public void GetHashCode_AllSpecifiedFlagsFalse_DoesNotThrow()
        {
            // Arrange
            var variableDesign = new VariableDesign
            {
                AccessLevelSpecified = false,
                InstanceAccessLevelSpecified = false,
                MinimumSamplingIntervalSpecified = false,
                HistorizingSpecified = false
            };

            // Act & Assert
            Assert.DoesNotThrow(() => variableDesign.GetHashCode());
        }

        /// <summary>
        /// Tests GetHashCode with all boolean "Specified" flags set to true.
        /// </summary>
        [Test]
        public void GetHashCode_AllSpecifiedFlagsTrue_DoesNotThrow()
        {
            // Arrange
            var variableDesign = new VariableDesign
            {
                AccessLevelSpecified = true,
                InstanceAccessLevelSpecified = true,
                MinimumSamplingIntervalSpecified = true,
                HistorizingSpecified = true
            };

            // Act & Assert
            Assert.DoesNotThrow(() => variableDesign.GetHashCode());
        }

        /// <summary>
        /// Tests GetHashCode with Historizing set to true.
        /// </summary>
        [Test]
        public void GetHashCode_HistorizingTrue_DoesNotThrow()
        {
            // Arrange
            var variableDesign = new VariableDesign
            {
                Historizing = true,
                HistorizingSpecified = true
            };

            // Act & Assert
            Assert.DoesNotThrow(() => variableDesign.GetHashCode());
        }

        /// <summary>
        /// Tests GetHashCode with Historizing set to false.
        /// </summary>
        [Test]
        public void GetHashCode_HistorizingFalse_DoesNotThrow()
        {
            // Arrange
            var variableDesign = new VariableDesign
            {
                Historizing = false,
                HistorizingSpecified = true
            };

            // Act & Assert
            Assert.DoesNotThrow(() => variableDesign.GetHashCode());
        }

        /// <summary>
        /// Tests GetHashCode with empty string ArrayDimensions.
        /// </summary>
        [Test]
        public void GetHashCode_EmptyArrayDimensions_DoesNotThrow()
        {
            // Arrange
            var variableDesign = new VariableDesign
            {
                ArrayDimensions = string.Empty
            };

            // Act & Assert
            Assert.DoesNotThrow(() => variableDesign.GetHashCode());
        }

        /// <summary>
        /// Tests GetHashCode with whitespace-only ArrayDimensions.
        /// </summary>
        [Test]
        public void GetHashCode_WhitespaceArrayDimensions_DoesNotThrow()
        {
            // Arrange
            var variableDesign = new VariableDesign
            {
                ArrayDimensions = "   "
            };

            // Act & Assert
            Assert.DoesNotThrow(() => variableDesign.GetHashCode());
        }

        /// <summary>
        /// Tests GetHashCode with very long ArrayDimensions string.
        /// </summary>
        [Test]
        public void GetHashCode_VeryLongArrayDimensions_DoesNotThrow()
        {
            // Arrange
            var variableDesign = new VariableDesign
            {
                ArrayDimensions = new string('1', 10000)
            };

            // Act & Assert
            Assert.DoesNotThrow(() => variableDesign.GetHashCode());
        }

        /// <summary>
        /// Tests GetHashCode with negative MinimumSamplingInterval.
        /// </summary>
        [Test]
        public void GetHashCode_NegativeMinimumSamplingInterval_DoesNotThrow()
        {
            // Arrange
            var variableDesign = new VariableDesign
            {
                MinimumSamplingInterval = -1000,
                MinimumSamplingIntervalSpecified = true
            };

            // Act & Assert
            Assert.DoesNotThrow(() => variableDesign.GetHashCode());
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with null object.
        /// </summary>
        [Test]
        public void Equals_NullObject_ReturnsFalse()
        {
            // Arrange
            var variableDesign = new VariableDesign();

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = variableDesign.Equals((object)null);
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with a different VariableDesign instance with different DataType.
        /// </summary>
        [Test]
        public void Equals_DifferentInstanceWithDifferentDataType_ReturnsFalse()
        {
            // Arrange
            var variableDesign1 = new VariableDesign
            {
                SymbolicName = new XmlQualifiedName("TestVariable", "http://test.org"),
                DataType = new XmlQualifiedName("Int32", "http://opcfoundation.org/UA/"),
                ValueRank = ValueRank.Scalar
            };

            var variableDesign2 = new VariableDesign
            {
                SymbolicName = new XmlQualifiedName("TestVariable", "http://test.org"),
                DataType = new XmlQualifiedName("String", "http://opcfoundation.org/UA/"),
                ValueRank = ValueRank.Scalar
            };

            // Act
            bool result = variableDesign1.Equals((object)variableDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with a different VariableDesign instance with different ArrayDimensions.
        /// </summary>
        [Test]
        public void Equals_DifferentInstanceWithDifferentArrayDimensions_ReturnsFalse()
        {
            // Arrange
            var variableDesign1 = new VariableDesign
            {
                SymbolicName = new XmlQualifiedName("TestVariable", "http://test.org"),
                DataType = new XmlQualifiedName("Int32", "http://opcfoundation.org/UA/"),
                ArrayDimensions = "1,2,3"
            };

            var variableDesign2 = new VariableDesign
            {
                SymbolicName = new XmlQualifiedName("TestVariable", "http://test.org"),
                DataType = new XmlQualifiedName("Int32", "http://opcfoundation.org/UA/"),
                ArrayDimensions = "1,2"
            };

            // Act
            bool result = variableDesign1.Equals((object)variableDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with a different VariableDesign instance with different Historizing.
        /// </summary>
        [Test]
        public void Equals_DifferentInstanceWithDifferentHistorizing_ReturnsFalse()
        {
            // Arrange
            var variableDesign1 = new VariableDesign
            {
                SymbolicName = new XmlQualifiedName("TestVariable", "http://test.org"),
                Historizing = true,
                HistorizingSpecified = true
            };

            var variableDesign2 = new VariableDesign
            {
                SymbolicName = new XmlQualifiedName("TestVariable", "http://test.org"),
                Historizing = false,
                HistorizingSpecified = true
            };

            // Act
            bool result = variableDesign1.Equals((object)variableDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with a different VariableDesign instance with different MinimumSamplingInterval.
        /// </summary>
        [Test]
        public void Equals_DifferentInstanceWithDifferentMinimumSamplingInterval_ReturnsFalse()
        {
            // Arrange
            var variableDesign1 = new VariableDesign
            {
                SymbolicName = new XmlQualifiedName("TestVariable", "http://test.org"),
                MinimumSamplingInterval = 1000,
                MinimumSamplingIntervalSpecified = true
            };

            var variableDesign2 = new VariableDesign
            {
                SymbolicName = new XmlQualifiedName("TestVariable", "http://test.org"),
                MinimumSamplingInterval = 2000,
                MinimumSamplingIntervalSpecified = true
            };

            // Act
            bool result = variableDesign1.Equals((object)variableDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with an object of a completely different type.
        /// </summary>
        [Test]
        public void Equals_DifferentType_ReturnsFalse()
        {
            // Arrange
            var variableDesign = new VariableDesign
            {
                SymbolicName = new XmlQualifiedName("TestVariable", "http://test.org")
            };
            object differentObject = new();

            // Act
            bool result = variableDesign.Equals(differentObject);

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
            var variableDesign = new VariableDesign
            {
                SymbolicName = new XmlQualifiedName("TestVariable", "http://test.org")
            };
            const string stringObject = "TestVariable";

            // Act
            bool result = variableDesign.Equals(stringObject);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with an InstanceDesign object (base type).
        /// </summary>
        [Test]
        public void Equals_BaseTypeInstanceDesign_ReturnsFalse()
        {
            // Arrange
            var variableDesign = new VariableDesign
            {
                SymbolicName = new XmlQualifiedName("TestVariable1", "http://test.org")
            };
            var instanceDesign = new PropertyDesign
            {
                SymbolicName = new XmlQualifiedName("TestVariable2", "http://test.org")
            };

            // Act
            bool result = variableDesign.Equals((object)instanceDesign);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with a VariableDesign having different SymbolicName.
        /// </summary>
        [Test]
        public void Equals_DifferentSymbolicName_ReturnsFalse()
        {
            // Arrange
            var variableDesign1 = new VariableDesign
            {
                SymbolicName = new XmlQualifiedName("Variable1", "http://test.org")
            };

            var variableDesign2 = new VariableDesign
            {
                SymbolicName = new XmlQualifiedName("Variable2", "http://test.org")
            };

            // Act
            bool result = variableDesign1.Equals((object)variableDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing two VariableDesign instances with null ArrayDimensions.
        /// </summary>
        [Test]
        public void Equals_BothNullArrayDimensions_ReturnsTrue()
        {
            // Arrange
            var symbolicName = new XmlQualifiedName("TestVariable", "http://test.org");

            var variableDesign1 = new VariableDesign
            {
                SymbolicName = symbolicName,
                ArrayDimensions = null
            };

            var variableDesign2 = new VariableDesign
            {
                SymbolicName = new XmlQualifiedName(symbolicName.Name, symbolicName.Namespace),
                ArrayDimensions = null
            };

            // Act
            bool result = variableDesign1.Equals((object)variableDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when one VariableDesign has null ArrayDimensions and the other has non-null.
        /// </summary>
        [Test]
        public void Equals_OneNullArrayDimensionsOneNonNull_ReturnsFalse()
        {
            // Arrange
            var variableDesign1 = new VariableDesign
            {
                SymbolicName = new XmlQualifiedName("TestVariable", "http://test.org"),
                ArrayDimensions = null
            };

            var variableDesign2 = new VariableDesign
            {
                SymbolicName = new XmlQualifiedName("TestVariable", "http://test.org"),
                ArrayDimensions = "1,2,3"
            };

            // Act
            bool result = variableDesign1.Equals((object)variableDesign2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when MinimumSamplingInterval values are at boundary (int.MaxValue).
        /// </summary>
        [Test]
        public void Equals_MinimumSamplingIntervalMaxValue_ReturnsTrue()
        {
            // Arrange
            var symbolicName = new XmlQualifiedName("TestVariable", "http://test.org");

            var variableDesign1 = new VariableDesign
            {
                SymbolicName = symbolicName,
                MinimumSamplingInterval = int.MaxValue,
                MinimumSamplingIntervalSpecified = true
            };

            var variableDesign2 = new VariableDesign
            {
                SymbolicName = new XmlQualifiedName(symbolicName.Name, symbolicName.Namespace),
                MinimumSamplingInterval = int.MaxValue,
                MinimumSamplingIntervalSpecified = true
            };

            // Act
            bool result = variableDesign1.Equals((object)variableDesign2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when MinimumSamplingInterval values are at boundary (int.MinValue).
        /// </summary>
        [Test]
        public void Equals_MinimumSamplingIntervalMinValue_ReturnsTrue()
        {
            // Arrange
            var symbolicName = new XmlQualifiedName("TestVariable", "http://test.org");

            var variableDesign1 = new VariableDesign
            {
                SymbolicName = symbolicName,
                MinimumSamplingInterval = int.MinValue,
                MinimumSamplingIntervalSpecified = true
            };

            var variableDesign2 = new VariableDesign
            {
                SymbolicName = new XmlQualifiedName(symbolicName.Name, symbolicName.Namespace),
                MinimumSamplingInterval = int.MinValue,
                MinimumSamplingIntervalSpecified = true
            };

            // Act
            bool result = variableDesign1.Equals((object)variableDesign2);

            // Assert
            Assert.That(result, Is.True);
        }
    }
}
