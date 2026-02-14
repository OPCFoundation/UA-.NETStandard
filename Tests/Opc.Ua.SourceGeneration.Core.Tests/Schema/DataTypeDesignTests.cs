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
    /// Unit tests for the <see cref="DataTypeDesign"/> class.
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class DataTypeDesignTests
    {
        /// <summary>
        /// Tests that Equals returns false when the parameter is null.
        /// </summary>
        [Test]
        public void Equals_NullObject_ReturnsFalse()
        {
            // Arrange
            var dataType = new DataTypeDesign();

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = dataType.Equals((object)null);
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing the same instance.
        /// </summary>
        [Test]
        public void Equals_SameReference_ReturnsTrue()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                SymbolicName = new XmlQualifiedName("TestType", "TestNamespace"),
                IsOptionSet = true
            };

            // Act
            bool result = dataType.Equals((object)dataType);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when the parameter is a different type.
        /// </summary>
        [Test]
        public void Equals_DifferentType_ReturnsFalse()
        {
            // Arrange
            var dataType = new DataTypeDesign();
            const string differentTypeObject = "not a DataTypeDesign";

            // Act
            bool result = dataType.Equals(differentTypeObject);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing two DataTypeDesign
        /// instances with identical properties.
        /// </summary>
        [Test]
        public void Equals_IdenticalProperties_ReturnsTrue()
        {
            // Arrange
            var symbolicName = new XmlQualifiedName("TestType", "TestNamespace");
            var dataType1 = new DataTypeDesign
            {
                SymbolicName = symbolicName,
                IsOptionSet = true,
                IsUnion = false,
                NoArraysAllowed = true,
                ForceEnumValues = false,
                NoEncodings = true
            };

            var dataType2 = new DataTypeDesign
            {
                SymbolicName = symbolicName,
                IsOptionSet = true,
                IsUnion = false,
                NoArraysAllowed = true,
                ForceEnumValues = false,
                NoEncodings = true
            };

            // Act
            bool result = dataType1.Equals((object)dataType2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when NoArraysAllowed properties differ.
        /// </summary>
        [Test]
        public void Equals_DifferentNoArraysAllowed_ReturnsFalse()
        {
            // Arrange
            var symbolicName = new XmlQualifiedName("TestType", "TestNamespace");
            var dataType1 = new DataTypeDesign
            {
                SymbolicName = symbolicName,
                NoArraysAllowed = true
            };

            var dataType2 = new DataTypeDesign
            {
                SymbolicName = symbolicName,
                NoArraysAllowed = false
            };

            // Act
            bool result = dataType1.Equals((object)dataType2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when ForceEnumValues properties differ.
        /// </summary>
        [Test]
        public void Equals_DifferentForceEnumValues_ReturnsFalse()
        {
            // Arrange
            var symbolicName = new XmlQualifiedName("TestType", "TestNamespace");
            var dataType1 = new DataTypeDesign
            {
                SymbolicName = symbolicName,
                ForceEnumValues = true
            };

            var dataType2 = new DataTypeDesign
            {
                SymbolicName = symbolicName,
                ForceEnumValues = false
            };

            // Act
            bool result = dataType1.Equals((object)dataType2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when NoEncodings properties differ.
        /// </summary>
        [Test]
        public void Equals_DifferentNoEncodings_ReturnsFalse()
        {
            // Arrange
            var symbolicName = new XmlQualifiedName("TestType", "TestNamespace");
            var dataType1 = new DataTypeDesign
            {
                SymbolicName = symbolicName,
                NoEncodings = true
            };

            var dataType2 = new DataTypeDesign
            {
                SymbolicName = symbolicName,
                NoEncodings = false
            };

            // Act
            bool result = dataType1.Equals((object)dataType2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when Fields arrays differ.
        /// </summary>
        [Test]
        public void Equals_DifferentFields_ReturnsFalse()
        {
            // Arrange
            var symbolicName = new XmlQualifiedName("TestType", "TestNamespace");
            var field1 = new Parameter { Name = "Field1" };
            var field2 = new Parameter { Name = "Field2" };

            var dataType1 = new DataTypeDesign
            {
                SymbolicName = symbolicName,
                Fields = [field1]
            };

            var dataType2 = new DataTypeDesign
            {
                SymbolicName = symbolicName,
                Fields = [field2]
            };

            // Act
            bool result = dataType1.Equals((object)dataType2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both Fields arrays are null.
        /// </summary>
        [Test]
        public void Equals_BothFieldsNull_ReturnsTrue()
        {
            // Arrange
            var symbolicName = new XmlQualifiedName("TestType", "TestNamespace");
            var dataType1 = new DataTypeDesign
            {
                SymbolicName = symbolicName,
                Fields = null
            };

            var dataType2 = new DataTypeDesign
            {
                SymbolicName = symbolicName,
                Fields = null
            };

            // Act
            bool result = dataType1.Equals((object)dataType2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when one Fields array is null and the
        /// other is not.
        /// </summary>
        [Test]
        public void Equals_OneFieldsNullOtherNot_ReturnsFalse()
        {
            // Arrange
            var symbolicName = new XmlQualifiedName("TestType", "TestNamespace");
            var dataType1 = new DataTypeDesign
            {
                SymbolicName = symbolicName,
                Fields = [new Parameter { Name = "Field1" }]
            };

            var dataType2 = new DataTypeDesign
            {
                SymbolicName = symbolicName,
                Fields = null
            };

            // Act
            bool result = dataType1.Equals((object)dataType2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when Encodings arrays differ.
        /// </summary>
        [Test]
        public void Equals_DifferentEncodings_ReturnsFalse()
        {
            // Arrange
            var symbolicName = new XmlQualifiedName("TestType", "TestNamespace");
            var encoding1 = new EncodingDesign
            {
                SymbolicName = new XmlQualifiedName("Encoding1", "TestNamespace")
            };
            var encoding2 = new EncodingDesign
            {
                SymbolicName = new XmlQualifiedName("Encoding2", "TestNamespace")
            };

            var dataType1 = new DataTypeDesign
            {
                SymbolicName = symbolicName,
                Encodings = [encoding1]
            };

            var dataType2 = new DataTypeDesign
            {
                SymbolicName = symbolicName,
                Encodings = [encoding2]
            };

            // Act
            bool result = dataType1.Equals((object)dataType2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both Encodings arrays are null.
        /// </summary>
        [Test]
        public void Equals_BothEncodingsNull_ReturnsTrue()
        {
            // Arrange
            var symbolicName = new XmlQualifiedName("TestType", "TestNamespace");
            var dataType1 = new DataTypeDesign
            {
                SymbolicName = symbolicName,
                Encodings = null
            };

            var dataType2 = new DataTypeDesign
            {
                SymbolicName = symbolicName,
                Encodings = null
            };

            // Act
            bool result = dataType1.Equals((object)dataType2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when SymbolicName properties differ.
        /// </summary>
        [Test]
        public void Equals_DifferentSymbolicName_ReturnsFalse()
        {
            // Arrange
            var dataType1 = new DataTypeDesign
            {
                SymbolicName = new XmlQualifiedName("TestType1", "TestNamespace")
            };

            var dataType2 = new DataTypeDesign
            {
                SymbolicName = new XmlQualifiedName("TestType2", "TestNamespace")
            };

            // Act
            bool result = dataType1.Equals((object)dataType2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when IsServiceResponse properties differ.
        /// </summary>
        [Test]
        public void Equals_DifferentIsServiceResponse_ReturnsFalse()
        {
            // Arrange
            var symbolicName = new XmlQualifiedName("TestType", "TestNamespace");
            var dataType1 = new DataTypeDesign
            {
                SymbolicName = symbolicName,
                IsServiceResponse = true
            };

            var dataType2 = new DataTypeDesign
            {
                SymbolicName = symbolicName,
                IsServiceResponse = false
            };

            // Act
            bool result = dataType1.Equals((object)dataType2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when Service properties differ.
        /// </summary>
        [Test]
        public void Equals_DifferentService_ReturnsFalse()
        {
            // Arrange
            var symbolicName = new XmlQualifiedName("TestType", "TestNamespace");
            var service1 = new Service
            {
                Name = "Service1",
                Category = ServiceCategory.Session
            };
            var service2 = new Service
            {
                Name = "Service2",
                Category = ServiceCategory.Discovery
            };

            var dataType1 = new DataTypeDesign
            {
                SymbolicName = symbolicName,
                Service = service1
            };

            var dataType2 = new DataTypeDesign
            {
                SymbolicName = symbolicName,
                Service = service2
            };

            // Act
            bool result = dataType1.Equals((object)dataType2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both Service properties are null.
        /// </summary>
        [Test]
        public void Equals_BothServiceNull_ReturnsTrue()
        {
            // Arrange
            var symbolicName = new XmlQualifiedName("TestType", "TestNamespace");
            var dataType1 = new DataTypeDesign
            {
                SymbolicName = symbolicName,
                Service = null
            };

            var dataType2 = new DataTypeDesign
            {
                SymbolicName = symbolicName,
                Service = null
            };

            // Act
            bool result = dataType1.Equals((object)dataType2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when Fields array lengths differ.
        /// </summary>
        [Test]
        public void Equals_DifferentFieldsArrayLength_ReturnsFalse()
        {
            // Arrange
            var symbolicName = new XmlQualifiedName("TestType", "TestNamespace");
            var field1 = new Parameter { Name = "Field1" };
            var field2 = new Parameter { Name = "Field2" };

            var dataType1 = new DataTypeDesign
            {
                SymbolicName = symbolicName,
                Fields = [field1]
            };

            var dataType2 = new DataTypeDesign
            {
                SymbolicName = symbolicName,
                Fields = [field1, field2]
            };

            // Act
            bool result = dataType1.Equals((object)dataType2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing empty Fields arrays.
        /// </summary>
        [Test]
        public void Equals_EmptyFieldsArrays_ReturnsTrue()
        {
            // Arrange
            var symbolicName = new XmlQualifiedName("TestType", "TestNamespace");
            var dataType1 = new DataTypeDesign
            {
                SymbolicName = symbolicName,
                Fields = []
            };

            var dataType2 = new DataTypeDesign
            {
                SymbolicName = symbolicName,
                Fields = []
            };

            // Act
            bool result = dataType1.Equals((object)dataType2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals handles complex objects with all properties set correctly.
        /// </summary>
        [Test]
        public void Equals_ComplexObjectsAllPropertiesSet_ReturnsTrue()
        {
            // Arrange
            var symbolicName = new XmlQualifiedName("TestType", "TestNamespace");
            var field = new Parameter
            {
                Name = "Field1",
                DataType = new XmlQualifiedName("String", "http://opcfoundation.org/UA/")
            };
            var encoding = new EncodingDesign
            {
                SymbolicName = new XmlQualifiedName("Encoding1", "TestNamespace")
            };
            var service = new Service
            {
                Name = "TestService",
                Category = ServiceCategory.Session
            };

            var dataType1 = new DataTypeDesign
            {
                SymbolicName = symbolicName,
                IsOptionSet = true,
                IsUnion = true,
                NoArraysAllowed = true,
                ForceEnumValues = true,
                NoEncodings = false,
                Fields = [field],
                Encodings = [encoding],
                IsServiceResponse = true,
                Service = service
            };

            var dataType2 = new DataTypeDesign
            {
                SymbolicName = symbolicName,
                IsOptionSet = true,
                IsUnion = true,
                NoArraysAllowed = true,
                ForceEnumValues = true,
                NoEncodings = false,
                Fields = [field],
                Encodings = [encoding],
                IsServiceResponse = true,
                Service = service
            };

            // Act
            bool result = dataType1.Equals((object)dataType2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with null.
        /// </summary>
        [Test]
        public void Equals_NullOther_ReturnsFalse()
        {
            // Arrange
            var dataType = new DataTypeDesign();

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result1 = dataType.Equals(null);
            bool result2 = dataType.Equals((object)null);
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result1, Is.False);
            Assert.That(result2, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing an instance with itself.
        /// </summary>
        [Test]
        public void Equals_SameInstance_ReturnsTrue()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BrowseName = "TestDataType",
                IsOptionSet = true
            };

            // Act
            bool result = dataType.Equals(dataType);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing two identical instances with all default values.
        /// </summary>
        [Test]
        public void Equals_IdenticalInstancesWithDefaults_ReturnsTrue()
        {
            // Arrange
            var dataType1 = new DataTypeDesign();
            var dataType2 = new DataTypeDesign();

            // Act
            bool result = dataType1.Equals(dataType2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing two instances with
        /// identical property values.
        /// </summary>
        [Test]
        public void Equals_IdenticalInstances_ReturnsTrue()
        {
            // Arrange
            var dataType1 = new DataTypeDesign
            {
                BrowseName = "TestDataType",
                IsOptionSet = true,
                IsUnion = false,
                NoArraysAllowed = true,
                ForceEnumValues = false,
                NoEncodings = true,
                Fields =
                [
                    new Parameter { Name = "Field1" }
                ],
                Encodings =
                [
                    new EncodingDesign { BrowseName = "Encoding1" }
                ]
            };

            var dataType2 = new DataTypeDesign
            {
                BrowseName = "TestDataType",
                IsOptionSet = true,
                IsUnion = false,
                NoArraysAllowed = true,
                ForceEnumValues = false,
                NoEncodings = true,
                Fields =
                [
                    new Parameter { Name = "Field1" }
                ],
                Encodings =
                [
                    new EncodingDesign { BrowseName = "Encoding1" }
                ]
            };

            // Act
            bool result = dataType1.Equals(dataType2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when base properties differ.
        /// </summary>
        [Test]
        public void Equals_DifferentBrowseName_ReturnsFalse()
        {
            // Arrange
            var dataType1 = new DataTypeDesign
            {
                BrowseName = "DataType1"
            };

            var dataType2 = new DataTypeDesign
            {
                BrowseName = "DataType2"
            };

            // Act
            bool result = dataType1.Equals(dataType2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when one has null Fields and the
        /// other has an array.
        /// </summary>
        [Test]
        public void Equals_OneNullFieldsArray_ReturnsFalse()
        {
            // Arrange
            var dataType1 = new DataTypeDesign
            {
                Fields = null
            };

            var dataType2 = new DataTypeDesign
            {
                Fields = [new Parameter { Name = "Field1" }]
            };

            // Act
            bool result = dataType1.Equals(dataType2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both have null Fields arrays.
        /// </summary>
        [Test]
        public void Equals_BothNullFieldsArrays_ReturnsTrue()
        {
            // Arrange
            var dataType1 = new DataTypeDesign
            {
                Fields = null
            };

            var dataType2 = new DataTypeDesign
            {
                Fields = null
            };

            // Act
            bool result = dataType1.Equals(dataType2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when both have empty Fields arrays.
        /// </summary>
        [Test]
        public void Equals_BothEmptyFieldsArrays_ReturnsTrue()
        {
            // Arrange
            var dataType1 = new DataTypeDesign
            {
                Fields = []
            };

            var dataType2 = new DataTypeDesign
            {
                Fields = []
            };

            // Act
            bool result = dataType1.Equals(dataType2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when one has null Encodings and
        /// the other has an array.
        /// </summary>
        [Test]
        public void Equals_OneNullEncodingsArray_ReturnsFalse()
        {
            // Arrange
            var dataType1 = new DataTypeDesign
            {
                Encodings = null
            };

            var dataType2 = new DataTypeDesign
            {
                Encodings = [new EncodingDesign { BrowseName = "Encoding1" }]
            };

            // Act
            bool result = dataType1.Equals(dataType2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both have null Encodings arrays.
        /// </summary>
        [Test]
        public void Equals_BothNullEncodingsArrays_ReturnsTrue()
        {
            // Arrange
            var dataType1 = new DataTypeDesign
            {
                Encodings = null
            };

            var dataType2 = new DataTypeDesign
            {
                Encodings = null
            };

            // Act
            bool result = dataType1.Equals(dataType2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when both have empty Encodings arrays.
        /// </summary>
        [Test]
        public void Equals_BothEmptyEncodingsArrays_ReturnsTrue()
        {
            // Arrange
            var dataType1 = new DataTypeDesign
            {
                Encodings = []
            };

            var dataType2 = new DataTypeDesign
            {
                Encodings = []
            };

            // Act
            bool result = dataType1.Equals(dataType2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when IsOptionSet values differ.
        /// </summary>
        [Test]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void Equals_DifferentIsOptionSet_ReturnsFalse(bool value1, bool value2)
        {
            // Arrange
            var dataType1 = new DataTypeDesign
            {
                IsOptionSet = value1
            };

            var dataType2 = new DataTypeDesign
            {
                IsOptionSet = value2
            };

            // Act
            bool result = dataType1.Equals(dataType2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when IsUnion values differ.
        /// </summary>
        [Test]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void Equals_DifferentIsUnion_ReturnsFalse(bool value1, bool value2)
        {
            // Arrange
            var dataType1 = new DataTypeDesign
            {
                IsUnion = value1
            };

            var dataType2 = new DataTypeDesign
            {
                IsUnion = value2
            };

            // Act
            bool result = dataType1.Equals(dataType2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when NoArraysAllowed values differ.
        /// </summary>
        [Test]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void Equals_DifferentNoArraysAllowed_ReturnsFalse(bool value1, bool value2)
        {
            // Arrange
            var dataType1 = new DataTypeDesign
            {
                NoArraysAllowed = value1
            };

            var dataType2 = new DataTypeDesign
            {
                NoArraysAllowed = value2
            };

            // Act
            bool result = dataType1.Equals(dataType2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when ForceEnumValues values differ.
        /// </summary>
        [Test]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void Equals_DifferentForceEnumValues_ReturnsFalse(bool value1, bool value2)
        {
            // Arrange
            var dataType1 = new DataTypeDesign
            {
                ForceEnumValues = value1
            };

            var dataType2 = new DataTypeDesign
            {
                ForceEnumValues = value2
            };

            // Act
            bool result = dataType1.Equals(dataType2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when NoEncodings values differ.
        /// </summary>
        [Test]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void Equals_DifferentNoEncodings_ReturnsFalse(bool value1, bool value2)
        {
            // Arrange
            var dataType1 = new DataTypeDesign
            {
                NoEncodings = value1
            };

            var dataType2 = new DataTypeDesign
            {
                NoEncodings = value2
            };

            // Act
            bool result = dataType1.Equals(dataType2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when IsServiceResponse values differ.
        /// </summary>
        [Test]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void Equals_DifferentIsServiceResponse_ReturnsFalse(bool value1, bool value2)
        {
            // Arrange
            var dataType1 = new DataTypeDesign
            {
                IsServiceResponse = value1
            };

            var dataType2 = new DataTypeDesign
            {
                IsServiceResponse = value2
            };

            // Act
            bool result = dataType1.Equals(dataType2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when one has null Service and the other does not.
        /// </summary>
        [Test]
        public void Equals_OneNullService_ReturnsFalse()
        {
            // Arrange
            var dataType1 = new DataTypeDesign
            {
                Service = null
            };

            var dataType2 = new DataTypeDesign
            {
                Service = new Service { Category = ServiceCategory.Session, Name = "Service1" }
            };

            // Act
            bool result = dataType1.Equals(dataType2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both Service values are null.
        /// </summary>
        [Test]
        public void Equals_BothNullService_ReturnsTrue()
        {
            // Arrange
            var dataType1 = new DataTypeDesign
            {
                Service = null
            };

            var dataType2 = new DataTypeDesign
            {
                Service = null
            };

            // Act
            bool result = dataType1.Equals(dataType2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when both Service values are equal.
        /// </summary>
        [Test]
        public void Equals_EqualService_ReturnsTrue()
        {
            // Arrange
            var dataType1 = new DataTypeDesign
            {
                Service = new Service { Category = ServiceCategory.Session, Name = "Service1" }
            };

            var dataType2 = new DataTypeDesign
            {
                Service = new Service { Category = ServiceCategory.Session, Name = "Service1" }
            };

            // Act
            bool result = dataType1.Equals(dataType2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when Fields arrays have different lengths.
        /// </summary>
        [Test]
        public void Equals_FieldsArraysDifferentLength_ReturnsFalse()
        {
            // Arrange
            var dataType1 = new DataTypeDesign
            {
                Fields =
                [
                    new Parameter { Name = "Field1" }
                ]
            };

            var dataType2 = new DataTypeDesign
            {
                Fields =
                [
                    new Parameter { Name = "Field1" },
                    new Parameter { Name = "Field2" }
                ]
            };

            // Act
            bool result = dataType1.Equals(dataType2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when Encodings arrays have different lengths.
        /// </summary>
        [Test]
        public void Equals_EncodingsArraysDifferentLength_ReturnsFalse()
        {
            // Arrange
            var dataType1 = new DataTypeDesign
            {
                Encodings =
                [
                    new EncodingDesign { BrowseName = "Encoding1" }
                ]
            };

            var dataType2 = new DataTypeDesign
            {
                Encodings =
                [
                    new EncodingDesign { BrowseName = "Encoding1" },
                    new EncodingDesign { BrowseName = "Encoding2" }
                ]
            };

            // Act
            bool result = dataType1.Equals(dataType2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when all properties match including complex scenarios.
        /// </summary>
        [Test]
        public void Equals_AllPropertiesMatch_ReturnsTrue()
        {
            // Arrange
            var dataType1 = new DataTypeDesign
            {
                BrowseName = "ComplexDataType",
                IsOptionSet = true,
                IsUnion = true,
                NoArraysAllowed = false,
                ForceEnumValues = true,
                NoEncodings = false,
                IsServiceResponse = true,
                Service = new Service { Category = ServiceCategory.Discovery, Name = "DiscoveryService" },
                Fields =
                [
                    new Parameter { Name = "Field1" },
                    new Parameter { Name = "Field2" }
                ],
                Encodings =
                [
                    new EncodingDesign { BrowseName = "DefaultBinary" },
                    new EncodingDesign { BrowseName = "DefaultXml" }
                ]
            };

            var dataType2 = new DataTypeDesign
            {
                BrowseName = "ComplexDataType",
                IsOptionSet = true,
                IsUnion = true,
                NoArraysAllowed = false,
                ForceEnumValues = true,
                NoEncodings = false,
                IsServiceResponse = true,
                Service = new Service { Category = ServiceCategory.Discovery, Name = "DiscoveryService" },
                Fields =
                [
                    new Parameter { Name = "Field1" },
                    new Parameter { Name = "Field2" }
                ],
                Encodings =
                [
                    new EncodingDesign { BrowseName = "DefaultBinary" },
                    new EncodingDesign { BrowseName = "DefaultXml" }
                ]
            };

            // Act
            bool result = dataType1.Equals(dataType2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that GetHashCode returns the same value for two instances with identical property values.
        /// </summary>
        [Test]
        public void GetHashCode_IdenticalInstances_ReturnsSameHashCode()
        {
            // Arrange
            var instance1 = new DataTypeDesign
            {
                IsOptionSet = true,
                IsUnion = false,
                NoArraysAllowed = true,
                ForceEnumValues = false,
                NoEncodings = true,
                Service = new Service { Name = "TestService", Category = ServiceCategory.Session },
                IsServiceResponse = true,
                Fields =
                [
                    new Parameter { Name = "Field1" }
                ],
                Encodings =
                [
                    new EncodingDesign()
                ]
            };

            var instance2 = new DataTypeDesign
            {
                IsOptionSet = true,
                IsUnion = false,
                NoArraysAllowed = true,
                ForceEnumValues = false,
                NoEncodings = true,
                Service = new Service { Name = "TestService", Category = ServiceCategory.Session },
                IsServiceResponse = true,
                Fields =
                [
                    new Parameter { Name = "Field1" }
                ],
                Encodings =
                [
                    new EncodingDesign()
                ]
            };

            // Act
            int hash1 = instance1.GetHashCode();
            int hash2 = instance2.GetHashCode();

            // Assert
            Assert.That(hash1, Is.EqualTo(hash2));
        }

        /// <summary>
        /// Tests that GetHashCode returns consistent values across multiple invocations on the same instance.
        /// </summary>
        [Test]
        public void GetHashCode_MultipleInvocations_ReturnsConsistentValue()
        {
            // Arrange
            var instance = new DataTypeDesign
            {
                IsOptionSet = true,
                IsUnion = true,
                NoArraysAllowed = false,
                ForceEnumValues = true,
                NoEncodings = false,
                Service = new Service { Name = "TestService", Category = ServiceCategory.Discovery },
                IsServiceResponse = false
            };

            // Act
            int hash1 = instance.GetHashCode();
            int hash2 = instance.GetHashCode();
            int hash3 = instance.GetHashCode();

            // Assert
            Assert.That(hash1, Is.EqualTo(hash2));
            Assert.That(hash2, Is.EqualTo(hash3));
        }

        /// <summary>
        /// Tests that GetHashCode handles null Fields array correctly.
        /// </summary>
        [Test]
        public void GetHashCode_NullFields_ReturnsValidHashCode()
        {
            // Arrange
            var instance = new DataTypeDesign
            {
                Fields = null,
                Encodings = [new EncodingDesign()],
                IsOptionSet = false
            };

            // Act
            int hashCode = instance.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.EqualTo(0));
        }

        /// <summary>
        /// Tests that GetHashCode handles null Encodings array correctly.
        /// </summary>
        [Test]
        public void GetHashCode_NullEncodings_ReturnsValidHashCode()
        {
            // Arrange
            var instance = new DataTypeDesign
            {
                Fields = [new Parameter { Name = "Field1" }],
                Encodings = null,
                IsUnion = true
            };

            // Act
            int hashCode = instance.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.EqualTo(0));
        }

        /// <summary>
        /// Tests that GetHashCode handles null Service property correctly.
        /// </summary>
        [Test]
        public void GetHashCode_NullService_ReturnsValidHashCode()
        {
            // Arrange
            var instance = new DataTypeDesign
            {
                Service = null,
                IsServiceResponse = false,
                NoEncodings = true
            };

            // Act
            int hashCode = instance.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.EqualTo(0));
        }

        /// <summary>
        /// Tests that GetHashCode handles empty Fields array correctly.
        /// </summary>
        [Test]
        public void GetHashCode_EmptyFields_ReturnsValidHashCode()
        {
            // Arrange
            var instance = new DataTypeDesign
            {
                Fields = [],
                Encodings = [],
                ForceEnumValues = true
            };

            // Act
            int hashCode = instance.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.EqualTo(0));
        }

        /// <summary>
        /// Tests that GetHashCode returns different values for instances with
        /// different IsOptionSet values.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentIsOptionSet_ReturnsDifferentHashCodes()
        {
            // Arrange
            var instance1 = new DataTypeDesign { IsOptionSet = true };
            var instance2 = new DataTypeDesign { IsOptionSet = false };

            // Act
            int hash1 = instance1.GetHashCode();
            int hash2 = instance2.GetHashCode();

            // Assert
            Assert.That(hash1, Is.Not.EqualTo(hash2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different values for instances with
        /// different IsUnion values.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentIsUnion_ReturnsDifferentHashCodes()
        {
            // Arrange
            var instance1 = new DataTypeDesign { IsUnion = true };
            var instance2 = new DataTypeDesign { IsUnion = false };

            // Act
            int hash1 = instance1.GetHashCode();
            int hash2 = instance2.GetHashCode();

            // Assert
            Assert.That(hash1, Is.Not.EqualTo(hash2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different values for instances with
        /// different NoArraysAllowed values.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentNoArraysAllowed_ReturnsDifferentHashCodes()
        {
            // Arrange
            var instance1 = new DataTypeDesign { NoArraysAllowed = true };
            var instance2 = new DataTypeDesign { NoArraysAllowed = false };

            // Act
            int hash1 = instance1.GetHashCode();
            int hash2 = instance2.GetHashCode();

            // Assert
            Assert.That(hash1, Is.Not.EqualTo(hash2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different values for instances with
        /// different ForceEnumValues values.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentForceEnumValues_ReturnsDifferentHashCodes()
        {
            // Arrange
            var instance1 = new DataTypeDesign { ForceEnumValues = true };
            var instance2 = new DataTypeDesign { ForceEnumValues = false };

            // Act
            int hash1 = instance1.GetHashCode();
            int hash2 = instance2.GetHashCode();

            // Assert
            Assert.That(hash1, Is.Not.EqualTo(hash2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different values for instances with
        /// different NoEncodings values.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentNoEncodings_ReturnsDifferentHashCodes()
        {
            // Arrange
            var instance1 = new DataTypeDesign { NoEncodings = true };
            var instance2 = new DataTypeDesign { NoEncodings = false };

            // Act
            int hash1 = instance1.GetHashCode();
            int hash2 = instance2.GetHashCode();

            // Assert
            Assert.That(hash1, Is.Not.EqualTo(hash2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different values for instances with
        /// different IsServiceResponse values.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentIsServiceResponse_ReturnsDifferentHashCodes()
        {
            // Arrange
            var instance1 = new DataTypeDesign { IsServiceResponse = true };
            var instance2 = new DataTypeDesign { IsServiceResponse = false };

            // Act
            int hash1 = instance1.GetHashCode();
            int hash2 = instance2.GetHashCode();

            // Assert
            Assert.That(hash1, Is.Not.EqualTo(hash2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different values for instances with
        /// different Service values.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentService_ReturnsDifferentHashCodes()
        {
            // Arrange
            var instance1 = new DataTypeDesign
            {
                Service = new Service { Name = "Service1", Category = ServiceCategory.Session }
            };
            var instance2 = new DataTypeDesign
            {
                Service = new Service { Name = "Service2", Category = ServiceCategory.Discovery }
            };

            // Act
            int hash1 = instance1.GetHashCode();
            int hash2 = instance2.GetHashCode();

            // Assert
            Assert.That(hash1, Is.Not.EqualTo(hash2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different values for instances with
        /// different Fields arrays.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentFields_ReturnsDifferentHashCodes()
        {
            // Arrange
            var instance1 = new DataTypeDesign
            {
                Fields = [new Parameter { Name = "Field1" }]
            };
            var instance2 = new DataTypeDesign
            {
                Fields = [new Parameter { Name = "Field2" }]
            };

            // Act
            int hash1 = instance1.GetHashCode();
            int hash2 = instance2.GetHashCode();

            // Assert
            Assert.That(hash1, Is.Not.EqualTo(hash2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different values for instances with
        /// different Encodings arrays.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentEncodings_ReturnsDifferentHashCodes()
        {
            // Arrange
            var instance1 = new DataTypeDesign
            {
                Encodings = [new EncodingDesign { SymbolicName = new XmlQualifiedName("Encoding1") }]
            };
            var instance2 = new DataTypeDesign
            {
                Encodings = [new EncodingDesign { SymbolicName = new XmlQualifiedName("Encoding2") }]
            };

            // Act
            int hash1 = instance1.GetHashCode();
            int hash2 = instance2.GetHashCode();

            // Assert
            Assert.That(hash1, Is.Not.EqualTo(hash2));
        }

        /// <summary>
        /// Tests that GetHashCode handles all boolean properties set to true.
        /// </summary>
        [Test]
        public void GetHashCode_AllBooleanPropertiesTrue_ReturnsValidHashCode()
        {
            // Arrange
            var instance = new DataTypeDesign
            {
                IsOptionSet = true,
                IsUnion = true,
                NoArraysAllowed = true,
                ForceEnumValues = true,
                NoEncodings = true,
                IsServiceResponse = true
            };

            // Act
            int hashCode = instance.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.EqualTo(0));
        }

        /// <summary>
        /// Tests that GetHashCode handles all boolean properties set to false.
        /// </summary>
        [Test]
        public void GetHashCode_AllBooleanPropertiesFalse_ReturnsValidHashCode()
        {
            // Arrange
            var instance = new DataTypeDesign
            {
                IsOptionSet = false,
                IsUnion = false,
                NoArraysAllowed = false,
                ForceEnumValues = false,
                NoEncodings = false,
                IsServiceResponse = false
            };

            // Act
            int hashCode = instance.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.EqualTo(0));
        }

        /// <summary>
        /// Tests that GetHashCode handles multiple Parameter items in Fields array.
        /// </summary>
        [Test]
        public void GetHashCode_MultipleFieldsItems_ReturnsValidHashCode()
        {
            // Arrange
            var instance = new DataTypeDesign
            {
                Fields =
                [
                    new Parameter { Name = "Field1" },
                    new Parameter { Name = "Field2" },
                    new Parameter { Name = "Field3" }
                ]
            };

            // Act
            int hashCode = instance.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.EqualTo(0));
        }

        /// <summary>
        /// Tests that GetHashCode handles multiple EncodingDesign items in Encodings array.
        /// </summary>
        [Test]
        public void GetHashCode_MultipleEncodingsItems_ReturnsValidHashCode()
        {
            // Arrange
            var instance = new DataTypeDesign
            {
                Encodings =
                [
                    new EncodingDesign { SymbolicName = new XmlQualifiedName("Encoding1") },
                    new EncodingDesign { SymbolicName = new XmlQualifiedName("Encoding2") }
                ]
            };

            // Act
            int hashCode = instance.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.EqualTo(0));
        }

        /// <summary>
        /// Tests that GetHashCode returns different values when Fields array order
        /// differs but content is the same.
        /// </summary>
        [Test]
        public void GetHashCode_FieldsArrayDifferentOrder_MayReturnDifferentHashCodes()
        {
            // Arrange
            var instance1 = new DataTypeDesign
            {
                Fields =
                [
                    new Parameter { Name = "Field1" },
                    new Parameter { Name = "Field2" }
                ]
            };
            var instance2 = new DataTypeDesign
            {
                Fields =
                [
                    new Parameter { Name = "Field2" },
                    new Parameter { Name = "Field1" }
                ]
            };

            // Act
            int hash1 = instance1.GetHashCode();
            int hash2 = instance2.GetHashCode();

            // Assert - Arrays with different order should produce different hash codes
            Assert.That(hash1, Is.Not.EqualTo(hash2));
        }

        /// <summary>
        /// Tests that GetHashCode handles all ServiceCategory enum values for
        /// Service property.
        /// </summary>
        [TestCase(ServiceCategory.None)]
        [TestCase(ServiceCategory.Session)]
        [TestCase(ServiceCategory.SecureChannel)]
        [TestCase(ServiceCategory.Discovery)]
        [TestCase(ServiceCategory.Registration)]
        [TestCase(ServiceCategory.Test)]
        public void GetHashCode_DifferentServiceCategories_ReturnsValidHashCode(
            ServiceCategory category)
        {
            // Arrange
            var instance = new DataTypeDesign
            {
                Service = new Service { Name = "TestService", Category = category }
            };

            // Act
            int hashCode = instance.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.EqualTo(0));
        }

        /// <summary>
        /// Tests that GetHashCode handles complex scenario with all properties populated.
        /// </summary>
        [Test]
        public void GetHashCode_AllPropertiesPopulated_ReturnsValidHashCode()
        {
            // Arrange
            var instance = new DataTypeDesign
            {
                IsOptionSet = true,
                IsUnion = true,
                NoArraysAllowed = false,
                ForceEnumValues = true,
                NoEncodings = false,
                Service = new Service
                {
                    Name = "ComplexService",
                    Category = ServiceCategory.Registration
                },
                IsServiceResponse = true,
                Fields =
                [
                    new Parameter
                    {
                        Name = "Field1"
                    },
                    new Parameter
                    {
                        Name = "Field2"
                    }
                ],
                Encodings =
                [
                    new EncodingDesign
                    {
                        SymbolicName = new XmlQualifiedName("Encoding1")
                    },
                    new EncodingDesign
                    {
                        SymbolicName = new XmlQualifiedName("Encoding2")
                    }
                ]
            };

            // Act
            int hashCode = instance.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.EqualTo(0));
        }
    }
}
