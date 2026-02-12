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
    /// Tests for the MethodDesign class.
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class MethodDesignTests
    {
        /// <summary>
        /// Tests that Equals(object) returns false when comparing with null.
        /// </summary>
        [Test]
        public void Equals_NullObject_ReturnsFalse()
        {
            // Arrange
            var method = new MethodDesign();

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = method.Equals((object)null);
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns true when comparing an instance with itself.
        /// </summary>
        [Test]
        public void Equals_SameInstance_ReturnsTrue()
        {
            // Arrange
            var method = new MethodDesign
            {
                InputArguments = [new Parameter { Name = "input1" }],
                OutputArguments = [new Parameter { Name = "output1" }],
                NonExecutable = true,
                NonExecutableSpecified = true
            };

            // Act
            bool result = method.Equals((object)method);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when comparing with an object of different type.
        /// </summary>
        [Test]
        public void Equals_DifferentType_ReturnsFalse()
        {
            // Arrange
            var method = new MethodDesign();
            object other = new();

            // Act
            bool result = method.Equals(other);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when comparing with a string object.
        /// </summary>
        [Test]
        public void Equals_StringObject_ReturnsFalse()
        {
            // Arrange
            var method = new MethodDesign();
            const string other = "not a MethodDesign";

            // Act
            bool result = method.Equals(other);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns true when comparing two equal MethodDesign instances with no arguments.
        /// </summary>
        [Test]
        public void Equals_EqualInstancesNoArguments_ReturnsTrue()
        {
            // Arrange
            var method1 = new MethodDesign
            {
                NonExecutable = false,
                NonExecutableSpecified = false
            };
            var method2 = new MethodDesign
            {
                NonExecutable = false,
                NonExecutableSpecified = false
            };

            // Act
            bool result = method1.Equals((object)method2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals(object) returns true when comparing two equal MethodDesign instances with input arguments.
        /// </summary>
        [Test]
        public void Equals_EqualInstancesWithInputArguments_ReturnsTrue()
        {
            // Arrange
            var parameter1 = new Parameter { Name = "param1", DataType = new XmlQualifiedName("String") };
            var parameter2 = new Parameter { Name = "param1", DataType = new XmlQualifiedName("String") };

            var method1 = new MethodDesign
            {
                InputArguments = [parameter1],
                NonExecutable = false,
                NonExecutableSpecified = false
            };
            var method2 = new MethodDesign
            {
                InputArguments = [parameter2],
                NonExecutable = false,
                NonExecutableSpecified = false
            };

            // Act
            bool result = method1.Equals((object)method2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals(object) returns true when comparing two equal MethodDesign instances with output arguments.
        /// </summary>
        [Test]
        public void Equals_EqualInstancesWithOutputArguments_ReturnsTrue()
        {
            // Arrange
            var parameter1 = new Parameter { Name = "result", DataType = new XmlQualifiedName("Int32") };
            var parameter2 = new Parameter { Name = "result", DataType = new XmlQualifiedName("Int32") };

            var method1 = new MethodDesign
            {
                OutputArguments = [parameter1],
                NonExecutable = false,
                NonExecutableSpecified = false
            };
            var method2 = new MethodDesign
            {
                OutputArguments = [parameter2],
                NonExecutable = false,
                NonExecutableSpecified = false
            };

            // Act
            bool result = method1.Equals((object)method2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when comparing MethodDesign instances with different InputArguments.
        /// </summary>
        [Test]
        public void Equals_DifferentInputArguments_ReturnsFalse()
        {
            // Arrange
            var method1 = new MethodDesign
            {
                InputArguments = [new Parameter { Name = "param1" }],
                NonExecutable = false,
                NonExecutableSpecified = false
            };
            var method2 = new MethodDesign
            {
                InputArguments = [new Parameter { Name = "param2" }],
                NonExecutable = false,
                NonExecutableSpecified = false
            };

            // Act
            bool result = method1.Equals((object)method2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when comparing MethodDesign instances with different OutputArguments.
        /// </summary>
        [Test]
        public void Equals_DifferentOutputArguments_ReturnsFalse()
        {
            // Arrange
            var method1 = new MethodDesign
            {
                OutputArguments = [new Parameter { Name = "result1" }],
                NonExecutable = false,
                NonExecutableSpecified = false
            };
            var method2 = new MethodDesign
            {
                OutputArguments = [new Parameter { Name = "result2" }],
                NonExecutable = false,
                NonExecutableSpecified = false
            };

            // Act
            bool result = method1.Equals((object)method2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when comparing MethodDesign instances with different NonExecutable values.
        /// </summary>
        [Test]
        public void Equals_DifferentNonExecutable_ReturnsFalse()
        {
            // Arrange
            var method1 = new MethodDesign
            {
                NonExecutable = true,
                NonExecutableSpecified = true
            };
            var method2 = new MethodDesign
            {
                NonExecutable = false,
                NonExecutableSpecified = true
            };

            // Act
            bool result = method1.Equals((object)method2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when comparing MethodDesign instances with different NonExecutableSpecified values.
        /// </summary>
        [Test]
        public void Equals_DifferentNonExecutableSpecified_ReturnsFalse()
        {
            // Arrange
            var method1 = new MethodDesign
            {
                NonExecutable = false,
                NonExecutableSpecified = true
            };
            var method2 = new MethodDesign
            {
                NonExecutable = false,
                NonExecutableSpecified = false
            };

            // Act
            bool result = method1.Equals((object)method2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when one instance has null InputArguments and the other has an array.
        /// </summary>
        [Test]
        public void Equals_OneNullInputArguments_ReturnsFalse()
        {
            // Arrange
            var method1 = new MethodDesign
            {
                InputArguments = null,
                NonExecutable = false,
                NonExecutableSpecified = false
            };
            var method2 = new MethodDesign
            {
                InputArguments = [new Parameter { Name = "param1" }],
                NonExecutable = false,
                NonExecutableSpecified = false
            };

            // Act
            bool result = method1.Equals((object)method2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when one instance has null OutputArguments and the other has an array.
        /// </summary>
        [Test]
        public void Equals_OneNullOutputArguments_ReturnsFalse()
        {
            // Arrange
            var method1 = new MethodDesign
            {
                OutputArguments = null,
                NonExecutable = false,
                NonExecutableSpecified = false
            };
            var method2 = new MethodDesign
            {
                OutputArguments = [new Parameter { Name = "result1" }],
                NonExecutable = false,
                NonExecutableSpecified = false
            };

            // Act
            bool result = method1.Equals((object)method2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns true when both instances have null InputArguments.
        /// </summary>
        [Test]
        public void Equals_BothNullInputArguments_ReturnsTrue()
        {
            // Arrange
            var method1 = new MethodDesign
            {
                InputArguments = null,
                NonExecutable = false,
                NonExecutableSpecified = false
            };
            var method2 = new MethodDesign
            {
                InputArguments = null,
                NonExecutable = false,
                NonExecutableSpecified = false
            };

            // Act
            bool result = method1.Equals((object)method2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals(object) returns true when both instances have empty InputArguments arrays.
        /// </summary>
        [Test]
        public void Equals_BothEmptyInputArguments_ReturnsTrue()
        {
            // Arrange
            var method1 = new MethodDesign
            {
                InputArguments = [],
                NonExecutable = false,
                NonExecutableSpecified = false
            };
            var method2 = new MethodDesign
            {
                InputArguments = [],
                NonExecutable = false,
                NonExecutableSpecified = false
            };

            // Act
            bool result = method1.Equals((object)method2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when InputArguments arrays have different lengths.
        /// </summary>
        [Test]
        public void Equals_InputArgumentsDifferentLength_ReturnsFalse()
        {
            // Arrange
            var method1 = new MethodDesign
            {
                InputArguments = [new Parameter { Name = "param1" }],
                NonExecutable = false,
                NonExecutableSpecified = false
            };
            var method2 = new MethodDesign
            {
                InputArguments =
                [
                    new Parameter { Name = "param1" },
                    new Parameter { Name = "param2" }
                ],
                NonExecutable = false,
                NonExecutableSpecified = false
            };

            // Act
            bool result = method1.Equals((object)method2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals(object) returns true when comparing two identical complex MethodDesign instances.
        /// </summary>
        [Test]
        public void Equals_ComplexEqualInstances_ReturnsTrue()
        {
            // Arrange
            var input1 = new Parameter { Name = "input1", DataType = new XmlQualifiedName("String") };
            var input2 = new Parameter { Name = "input1", DataType = new XmlQualifiedName("String") };
            var output1 = new Parameter { Name = "output1", DataType = new XmlQualifiedName("Int32") };
            var output2 = new Parameter { Name = "output1", DataType = new XmlQualifiedName("Int32") };

            var method1 = new MethodDesign
            {
                InputArguments = [input1],
                OutputArguments = [output1],
                NonExecutable = true,
                NonExecutableSpecified = true
            };
            var method2 = new MethodDesign
            {
                InputArguments = [input2],
                OutputArguments = [output2],
                NonExecutable = true,
                NonExecutableSpecified = true
            };

            // Act
            bool result = method1.Equals((object)method2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals(object) returns false when all properties differ.
        /// </summary>
        [Test]
        public void Equals_AllPropertiesDifferent_ReturnsFalse()
        {
            // Arrange
            var method1 = new MethodDesign
            {
                InputArguments = [new Parameter { Name = "param1" }],
                OutputArguments = [new Parameter { Name = "result1" }],
                NonExecutable = true,
                NonExecutableSpecified = true
            };
            var method2 = new MethodDesign
            {
                InputArguments = [new Parameter { Name = "param2" }],
                OutputArguments = [new Parameter { Name = "result2" }],
                NonExecutable = false,
                NonExecutableSpecified = false
            };

            // Act
            bool result = method1.Equals((object)method2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that GetHashCode returns consistent hash code for the same object across multiple calls.
        /// Validates that hash code computation is deterministic.
        /// </summary>
        [Test]
        public void GetHashCode_CalledMultipleTimes_ReturnsSameValue()
        {
            // Arrange
            var methodDesign = new MethodDesign
            {
                SymbolicId = new XmlQualifiedName("Method1", "http://test.org"),
                InputArguments =
                [
                    new Parameter { Name = "arg1", DataType = new XmlQualifiedName("Int32") }
                ],
                OutputArguments =
                [
                    new Parameter { Name = "result", DataType = new XmlQualifiedName("String") }
                ],
                NonExecutable = true,
                NonExecutableSpecified = true
            };

            // Act
            int hashCode1 = methodDesign.GetHashCode();
            int hashCode2 = methodDesign.GetHashCode();
            int hashCode3 = methodDesign.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.EqualTo(hashCode1));
            Assert.That(hashCode3, Is.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests that GetHashCode returns same hash code for equal objects.
        /// Validates hash code consistency with Equals contract.
        /// </summary>
        [Test]
        public void GetHashCode_EqualObjects_ReturnsSameHashCode()
        {
            // Arrange
            var input = new Parameter[] { new() { Name = "arg1" } };
            var output = new Parameter[] { new() { Name = "result" } };

            var methodDesign1 = new MethodDesign
            {
                SymbolicId = new XmlQualifiedName("Method1", "http://test.org"),
                InputArguments = input,
                OutputArguments = output,
                NonExecutable = true,
                NonExecutableSpecified = true
            };

            var methodDesign2 = new MethodDesign
            {
                SymbolicId = new XmlQualifiedName("Method1", "http://test.org"),
                InputArguments = input,
                OutputArguments = output,
                NonExecutable = true,
                NonExecutableSpecified = true
            };

            // Act
            int hashCode1 = methodDesign1.GetHashCode();
            int hashCode2 = methodDesign2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes for objects with different InputArguments.
        /// Validates that InputArguments contribute to hash code computation.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentInputArguments_ReturnsDifferentHashCodes()
        {
            // Arrange
            var methodDesign1 = new MethodDesign
            {
                SymbolicId = new XmlQualifiedName("Method1", "http://test.org"),
                InputArguments = [new Parameter { Name = "arg1" }],
                NonExecutable = false,
                NonExecutableSpecified = false
            };

            var methodDesign2 = new MethodDesign
            {
                SymbolicId = new XmlQualifiedName("Method1", "http://test.org"),
                InputArguments = [new Parameter { Name = "arg2" }],
                NonExecutable = false,
                NonExecutableSpecified = false
            };

            // Act
            int hashCode1 = methodDesign1.GetHashCode();
            int hashCode2 = methodDesign2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.Not.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes for objects with different OutputArguments.
        /// Validates that OutputArguments contribute to hash code computation.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentOutputArguments_ReturnsDifferentHashCodes()
        {
            // Arrange
            var methodDesign1 = new MethodDesign
            {
                SymbolicId = new XmlQualifiedName("Method1", "http://test.org"),
                OutputArguments = [new Parameter { Name = "result1" }],
                NonExecutable = false,
                NonExecutableSpecified = false
            };

            var methodDesign2 = new MethodDesign
            {
                SymbolicId = new XmlQualifiedName("Method1", "http://test.org"),
                OutputArguments = [new Parameter { Name = "result2" }],
                NonExecutable = false,
                NonExecutableSpecified = false
            };

            // Act
            int hashCode1 = methodDesign1.GetHashCode();
            int hashCode2 = methodDesign2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.Not.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests GetHashCode with different values of NonExecutable property.
        /// Validates that NonExecutable contributes to hash code computation.
        /// Expected result: Different hash codes for true and false values.
        /// </summary>
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void GetHashCode_DifferentNonExecutable_ReturnsDifferentHashCodes(bool value1, bool value2)
        {
            // Arrange
            var methodDesign1 = new MethodDesign
            {
                SymbolicId = new XmlQualifiedName("Method1", "http://test.org"),
                NonExecutable = value1,
                NonExecutableSpecified = true
            };

            var methodDesign2 = new MethodDesign
            {
                SymbolicId = new XmlQualifiedName("Method1", "http://test.org"),
                NonExecutable = value2,
                NonExecutableSpecified = true
            };

            // Act
            int hashCode1 = methodDesign1.GetHashCode();
            int hashCode2 = methodDesign2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.Not.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests GetHashCode with different values of NonExecutableSpecified property.
        /// Validates that NonExecutableSpecified contributes to hash code computation.
        /// Expected result: Different hash codes for true and false values.
        /// </summary>
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void GetHashCode_DifferentNonExecutableSpecified_ReturnsDifferentHashCodes(bool specified1, bool specified2)
        {
            // Arrange
            var methodDesign1 = new MethodDesign
            {
                SymbolicId = new XmlQualifiedName("Method1", "http://test.org"),
                NonExecutable = false,
                NonExecutableSpecified = specified1
            };

            var methodDesign2 = new MethodDesign
            {
                SymbolicId = new XmlQualifiedName("Method1", "http://test.org"),
                NonExecutable = false,
                NonExecutableSpecified = specified2
            };

            // Act
            int hashCode1 = methodDesign1.GetHashCode();
            int hashCode2 = methodDesign2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.Not.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests GetHashCode with null InputArguments.
        /// Validates that null arrays are handled correctly in hash code computation.
        /// Expected result: Valid hash code is computed without exception.
        /// </summary>
        [Test]
        public void GetHashCode_NullInputArguments_ReturnsValidHashCode()
        {
            // Arrange
            var methodDesign = new MethodDesign
            {
                SymbolicId = new XmlQualifiedName("Method1", "http://test.org"),
                InputArguments = null,
                OutputArguments = [new Parameter { Name = "result" }],
                NonExecutable = false,
                NonExecutableSpecified = false
            };

            // Act
            int hashCode = methodDesign.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.EqualTo(0));
        }

        /// <summary>
        /// Tests GetHashCode with null OutputArguments.
        /// Validates that null arrays are handled correctly in hash code computation.
        /// Expected result: Valid hash code is computed without exception.
        /// </summary>
        [Test]
        public void GetHashCode_NullOutputArguments_ReturnsValidHashCode()
        {
            // Arrange
            var methodDesign = new MethodDesign
            {
                SymbolicId = new XmlQualifiedName("Method1", "http://test.org"),
                InputArguments = [new Parameter { Name = "arg" }],
                OutputArguments = null,
                NonExecutable = false,
                NonExecutableSpecified = false
            };

            // Act
            int hashCode = methodDesign.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.EqualTo(0));
        }

        /// <summary>
        /// Validates that empty arrays vs null array hash code computation.
        /// </summary>
        [Test]
        public void GetHashCode_EmptyVsNullInputArguments_ReturnsDifferentHashCodes()
        {
            // Arrange
            var methodDesign1 = new MethodDesign
            {
                SymbolicId = new XmlQualifiedName("Method1", "http://test.org"),
                InputArguments = null,
                NonExecutable = false,
                NonExecutableSpecified = false
            };

            var methodDesign2 = new MethodDesign
            {
                SymbolicId = new XmlQualifiedName("Method1", "http://test.org"),
                InputArguments = [],
                NonExecutable = false,
                NonExecutableSpecified = false
            };

            // Act
            int hashCode1 = methodDesign1.GetHashCode();
            int hashCode2 = methodDesign2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.Not.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests GetHashCode with multiple parameters in InputArguments.
        /// Validates that arrays with multiple elements are handled correctly.
        /// Expected result: Different hash codes for arrays with different number of elements.
        /// </summary>
        [Test]
        public void GetHashCode_MultipleInputArguments_ReturnsDifferentHashCodes()
        {
            // Arrange
            var methodDesign1 = new MethodDesign
            {
                SymbolicId = new XmlQualifiedName("Method1", "http://test.org"),
                InputArguments =
                [
                    new Parameter { Name = "arg1" }
                ],
                NonExecutable = false,
                NonExecutableSpecified = false
            };

            var methodDesign2 = new MethodDesign
            {
                SymbolicId = new XmlQualifiedName("Method1", "http://test.org"),
                InputArguments =
                [
                    new Parameter { Name = "arg1" },
                    new Parameter { Name = "arg2" }
                ],
                NonExecutable = false,
                NonExecutableSpecified = false
            };

            // Act
            int hashCode1 = methodDesign1.GetHashCode();
            int hashCode2 = methodDesign2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.Not.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests that GetHashCode considers base class properties.
        /// Validates that inherited properties contribute to hash code computation.
        /// Expected result: Different hash codes when base properties differ.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentBaseProperties_ReturnsDifferentHashCodes()
        {
            // Arrange
            var methodDesign1 = new MethodDesign
            {
                SymbolicId = new XmlQualifiedName("Method1", "http://test.org"),
                InputArguments = [new Parameter { Name = "arg1" }],
                NonExecutable = false,
                NonExecutableSpecified = false
            };

            var methodDesign2 = new MethodDesign
            {
                SymbolicId = new XmlQualifiedName("Method2", "http://test.org"),
                InputArguments = [new Parameter { Name = "arg1" }],
                NonExecutable = false,
                NonExecutableSpecified = false
            };

            // Act
            int hashCode1 = methodDesign1.GetHashCode();
            int hashCode2 = methodDesign2.GetHashCode();

            // Assert
            Assert.That(hashCode2, Is.Not.EqualTo(hashCode1));
        }

        /// <summary>
        /// Tests GetHashCode with all properties set to default/null values.
        /// Validates that hash code computation works with minimal property values.
        /// Expected result: Valid hash code is computed.
        /// </summary>
        [Test]
        public void GetHashCode_AllPropertiesDefault_ReturnsValidHashCode()
        {
            // Arrange
            var methodDesign = new MethodDesign
            {
                InputArguments = null,
                OutputArguments = null,
                NonExecutable = false,
                NonExecutableSpecified = false
            };

            // Act
            int hashCode = methodDesign.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.EqualTo(0));
        }

        /// <summary>
        /// Tests GetHashCode with all possible combinations of boolean properties.
        /// Validates that all combinations of NonExecutable and NonExecutableSpecified produce unique hash codes.
        /// Expected result: Each combination should ideally produce different hash codes.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void GetHashCode_AllBooleanCombinations_ReturnsValidHashCode(bool nonExecutable, bool nonExecutableSpecified)
        {
            // Arrange
            var methodDesign = new MethodDesign
            {
                SymbolicId = new XmlQualifiedName("Method1", "http://test.org"),
                InputArguments = [new Parameter { Name = "arg1" }],
                OutputArguments = [new Parameter { Name = "result" }],
                NonExecutable = nonExecutable,
                NonExecutableSpecified = nonExecutableSpecified
            };

            // Act
            int hashCode = methodDesign.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.EqualTo(0));
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with null.
        /// </summary>
        [Test]
        public void Equals_NullOther_ReturnsFalse()
        {
            // Arrange
            var method = new MethodDesign();

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            bool result = method.Equals((MethodDesign)null);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing two instances with identical properties.
        /// </summary>
        [Test]
        public void Equals_IdenticalProperties_ReturnsTrue()
        {
            // Arrange
            var method1 = new MethodDesign
            {
                BrowseName = "TestMethod",
                NonExecutable = false,
                NonExecutableSpecified = true
            };

            var method2 = new MethodDesign
            {
                BrowseName = "TestMethod",
                NonExecutable = false,
                NonExecutableSpecified = true
            };

            // Act
            bool result = method1.Equals(method2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when base properties differ.
        /// </summary>
        [Test]
        public void Equals_DifferentBaseProperty_ReturnsFalse()
        {
            // Arrange
            var method1 = new MethodDesign
            {
                BrowseName = "TestMethod1",
                NonExecutable = true,
                NonExecutableSpecified = true
            };

            var method2 = new MethodDesign
            {
                BrowseName = "TestMethod2",
                NonExecutable = true,
                NonExecutableSpecified = true
            };

            // Act
            bool result = method1.Equals(method2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both InputArguments are null.
        /// </summary>
        [Test]
        public void Equals_BothInputArgumentsNull_ReturnsTrue()
        {
            // Arrange
            var method1 = new MethodDesign
            {
                BrowseName = "TestMethod",
                InputArguments = null,
                NonExecutable = false,
                NonExecutableSpecified = true
            };

            var method2 = new MethodDesign
            {
                BrowseName = "TestMethod",
                InputArguments = null,
                NonExecutable = false,
                NonExecutableSpecified = true
            };

            // Act
            bool result = method1.Equals(method2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when both OutputArguments are null.
        /// </summary>
        [Test]
        public void Equals_BothOutputArgumentsNull_ReturnsTrue()
        {
            // Arrange
            var method1 = new MethodDesign
            {
                BrowseName = "TestMethod",
                OutputArguments = null,
                NonExecutable = false,
                NonExecutableSpecified = true
            };

            var method2 = new MethodDesign
            {
                BrowseName = "TestMethod",
                OutputArguments = null,
                NonExecutable = false,
                NonExecutableSpecified = true
            };

            // Act
            bool result = method1.Equals(method2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when one InputArguments is null and the other is not.
        /// </summary>
        [Test]
        public void Equals_OneInputArgumentsNull_ReturnsFalse()
        {
            // Arrange
            var method1 = new MethodDesign
            {
                BrowseName = "TestMethod",
                InputArguments = null,
                NonExecutable = false,
                NonExecutableSpecified = true
            };

            var method2 = new MethodDesign
            {
                BrowseName = "TestMethod",
                InputArguments = [],
                NonExecutable = false,
                NonExecutableSpecified = true
            };

            // Act
            bool result = method1.Equals(method2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when one OutputArguments is null and the other is not.
        /// </summary>
        [Test]
        public void Equals_OneOutputArgumentsNull_ReturnsFalse()
        {
            // Arrange
            var method1 = new MethodDesign
            {
                BrowseName = "TestMethod",
                OutputArguments = null,
                NonExecutable = false,
                NonExecutableSpecified = true
            };

            var method2 = new MethodDesign
            {
                BrowseName = "TestMethod",
                OutputArguments = [],
                NonExecutable = false,
                NonExecutableSpecified = true
            };

            // Act
            bool result = method1.Equals(method2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both InputArguments are empty arrays.
        /// </summary>
        [Test]
        public void Equals_BothInputArgumentsEmpty_ReturnsTrue()
        {
            // Arrange
            var method1 = new MethodDesign
            {
                BrowseName = "TestMethod",
                InputArguments = [],
                NonExecutable = false,
                NonExecutableSpecified = true
            };

            var method2 = new MethodDesign
            {
                BrowseName = "TestMethod",
                InputArguments = [],
                NonExecutable = false,
                NonExecutableSpecified = true
            };

            // Act
            bool result = method1.Equals(method2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when both OutputArguments are empty arrays.
        /// </summary>
        [Test]
        public void Equals_BothOutputArgumentsEmpty_ReturnsTrue()
        {
            // Arrange
            var method1 = new MethodDesign
            {
                BrowseName = "TestMethod",
                OutputArguments = [],
                NonExecutable = false,
                NonExecutableSpecified = true
            };

            var method2 = new MethodDesign
            {
                BrowseName = "TestMethod",
                OutputArguments = [],
                NonExecutable = false,
                NonExecutableSpecified = true
            };

            // Act
            bool result = method1.Equals(method2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when InputArguments have identical elements.
        /// </summary>
        [Test]
        public void Equals_IdenticalInputArguments_ReturnsTrue()
        {
            // Arrange
            var param1 = new Parameter { Name = "arg1", DataType = new XmlQualifiedName("String") };
            var param2 = new Parameter { Name = "arg1", DataType = new XmlQualifiedName("String") };

            var method1 = new MethodDesign
            {
                BrowseName = "TestMethod",
                InputArguments = [param1],
                NonExecutable = false,
                NonExecutableSpecified = true
            };

            var method2 = new MethodDesign
            {
                BrowseName = "TestMethod",
                InputArguments = [param2],
                NonExecutable = false,
                NonExecutableSpecified = true
            };

            // Act
            bool result = method1.Equals(method2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when OutputArguments have identical elements.
        /// </summary>
        [Test]
        public void Equals_IdenticalOutputArguments_ReturnsTrue()
        {
            // Arrange
            var param1 = new Parameter { Name = "result", DataType = new XmlQualifiedName("Int32") };
            var param2 = new Parameter { Name = "result", DataType = new XmlQualifiedName("Int32") };

            var method1 = new MethodDesign
            {
                BrowseName = "TestMethod",
                OutputArguments = [param1],
                NonExecutable = false,
                NonExecutableSpecified = true
            };

            var method2 = new MethodDesign
            {
                BrowseName = "TestMethod",
                OutputArguments = [param2],
                NonExecutable = false,
                NonExecutableSpecified = true
            };

            // Act
            bool result = method1.Equals(method2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when InputArguments have different lengths.
        /// </summary>
        [Test]
        public void Equals_DifferentInputArgumentsLength_ReturnsFalse()
        {
            // Arrange
            var param = new Parameter { Name = "arg1" };

            var method1 = new MethodDesign
            {
                BrowseName = "TestMethod",
                InputArguments = [param],
                NonExecutable = false,
                NonExecutableSpecified = true
            };

            var method2 = new MethodDesign
            {
                BrowseName = "TestMethod",
                InputArguments = [param, param],
                NonExecutable = false,
                NonExecutableSpecified = true
            };

            // Act
            bool result = method1.Equals(method2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when OutputArguments have different lengths.
        /// </summary>
        [Test]
        public void Equals_DifferentOutputArgumentsLength_ReturnsFalse()
        {
            // Arrange
            var param = new Parameter { Name = "result" };

            var method1 = new MethodDesign
            {
                BrowseName = "TestMethod",
                OutputArguments = [param],
                NonExecutable = false,
                NonExecutableSpecified = true
            };

            var method2 = new MethodDesign
            {
                BrowseName = "TestMethod",
                OutputArguments = [param, param],
                NonExecutable = false,
                NonExecutableSpecified = true
            };

            // Act
            bool result = method1.Equals(method2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when InputArguments have different element values.
        /// </summary>
        [Test]
        public void Equals_DifferentInputArgumentsValues_ReturnsFalse()
        {
            // Arrange
            var param1 = new Parameter { Name = "arg1", DataType = new XmlQualifiedName("String") };
            var param2 = new Parameter { Name = "arg2", DataType = new XmlQualifiedName("Int32") };

            var method1 = new MethodDesign
            {
                BrowseName = "TestMethod",
                InputArguments = [param1],
                NonExecutable = false,
                NonExecutableSpecified = true
            };

            var method2 = new MethodDesign
            {
                BrowseName = "TestMethod",
                InputArguments = [param2],
                NonExecutable = false,
                NonExecutableSpecified = true
            };

            // Act
            bool result = method1.Equals(method2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when OutputArguments have different element values.
        /// </summary>
        [Test]
        public void Equals_DifferentOutputArgumentsValues_ReturnsFalse()
        {
            // Arrange
            var param1 = new Parameter { Name = "result1", DataType = new XmlQualifiedName("String") };
            var param2 = new Parameter { Name = "result2", DataType = new XmlQualifiedName("Int32") };

            var method1 = new MethodDesign
            {
                BrowseName = "TestMethod",
                OutputArguments = [param1],
                NonExecutable = false,
                NonExecutableSpecified = true
            };

            var method2 = new MethodDesign
            {
                BrowseName = "TestMethod",
                OutputArguments = [param2],
                NonExecutable = false,
                NonExecutableSpecified = true
            };

            // Act
            bool result = method1.Equals(method2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when all properties and arguments are identical.
        /// </summary>
        [Test]
        public void Equals_ComplexIdenticalObjects_ReturnsTrue()
        {
            // Arrange
            var inputParam1 = new Parameter { Name = "input1", DataType = new XmlQualifiedName("String") };
            var outputParam1 = new Parameter { Name = "output1", DataType = new XmlQualifiedName("Int32") };
            var inputParam2 = new Parameter { Name = "input1", DataType = new XmlQualifiedName("String") };
            var outputParam2 = new Parameter { Name = "output1", DataType = new XmlQualifiedName("Int32") };

            var method1 = new MethodDesign
            {
                BrowseName = "ComplexMethod",
                InputArguments = [inputParam1],
                OutputArguments = [outputParam1],
                NonExecutable = true,
                NonExecutableSpecified = true
            };

            var method2 = new MethodDesign
            {
                BrowseName = "ComplexMethod",
                InputArguments = [inputParam2],
                OutputArguments = [outputParam2],
                NonExecutable = true,
                NonExecutableSpecified = true
            };

            // Act
            bool result = method1.Equals(method2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when multiple properties differ.
        /// </summary>
        [Test]
        public void Equals_MultipleDifferences_ReturnsFalse()
        {
            // Arrange
            var param1 = new Parameter { Name = "arg1" };
            var param2 = new Parameter { Name = "arg2" };

            var method1 = new MethodDesign
            {
                BrowseName = "Method1",
                InputArguments = [param1],
                NonExecutable = true,
                NonExecutableSpecified = true
            };

            var method2 = new MethodDesign
            {
                BrowseName = "Method2",
                InputArguments = [param2],
                NonExecutable = false,
                NonExecutableSpecified = false
            };

            // Act
            bool result = method1.Equals(method2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when InputArguments arrays contain multiple identical elements.
        /// </summary>
        [Test]
        public void Equals_MultipleIdenticalInputArguments_ReturnsTrue()
        {
            // Arrange
            var param1a = new Parameter { Name = "arg1", DataType = new XmlQualifiedName("String") };
            var param1b = new Parameter { Name = "arg2", DataType = new XmlQualifiedName("Int32") };
            var param2a = new Parameter { Name = "arg1", DataType = new XmlQualifiedName("String") };
            var param2b = new Parameter { Name = "arg2", DataType = new XmlQualifiedName("Int32") };

            var method1 = new MethodDesign
            {
                BrowseName = "TestMethod",
                InputArguments = [param1a, param1b],
                NonExecutable = false,
                NonExecutableSpecified = true
            };

            var method2 = new MethodDesign
            {
                BrowseName = "TestMethod",
                InputArguments = [param2a, param2b],
                NonExecutable = false,
                NonExecutableSpecified = true
            };

            // Act
            bool result = method1.Equals(method2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when both NonExecutable and NonExecutableSpecified differ.
        /// </summary>
        [Test]
        public void Equals_BothNonExecutablePropertiesDiffer_ReturnsFalse()
        {
            // Arrange
            var method1 = new MethodDesign
            {
                BrowseName = "TestMethod",
                NonExecutable = true,
                NonExecutableSpecified = true
            };

            var method2 = new MethodDesign
            {
                BrowseName = "TestMethod",
                NonExecutable = false,
                NonExecutableSpecified = false
            };

            // Act
            bool result = method1.Equals(method2);

            // Assert
            Assert.That(result, Is.False);
        }
    }
}
