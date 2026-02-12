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
    /// Unit tests for the PropertyDesign class.
    /// </summary>
    public class PropertyDesignTests
    {
        /// <summary>
        /// Tests that Equals returns false when comparing with null.
        /// </summary>
        [Test]
        public void Equals_WithNull_ReturnsFalse()
        {
            // Arrange
            var design = new PropertyDesign();

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = design.Equals(null);
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing an instance with itself.
        /// </summary>
        [Test]
        public void Equals_WithSameInstance_ReturnsTrue()
        {
            // Arrange
            var design = new PropertyDesign
            {
                SymbolicName = new XmlQualifiedName("TestProperty", "http://test.org")
            };

            // Act
            bool result = design.Equals(design);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing two instances with different property values.
        /// </summary>
        [Test]
        public void Equals_WithDifferentProperties_ReturnsFalse()
        {
            // Arrange
            var design1 = new PropertyDesign
            {
                SymbolicName = new XmlQualifiedName("TestProperty1", "http://test.org"),
                DataType = new XmlQualifiedName("Int32", "http://opcfoundation.org/UA/")
            };

            var design2 = new PropertyDesign
            {
                SymbolicName = new XmlQualifiedName("TestProperty2", "http://test.org"),
                DataType = new XmlQualifiedName("String", "http://opcfoundation.org/UA/")
            };

            // Act
            bool result = design1.Equals(design2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing two default-initialized instances.
        /// </summary>
        [Test]
        public void Equals_WithDefaultInstances_ReturnsTrue()
        {
            // Arrange
            var design1 = new PropertyDesign();
            var design2 = new PropertyDesign();

            // Act
            bool result = design1.Equals(design2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing instances with different ArrayDimensions.
        /// </summary>
        [Test]
        public void Equals_WithDifferentArrayDimensions_ReturnsFalse()
        {
            // Arrange
            var design1 = new PropertyDesign
            {
                SymbolicName = new XmlQualifiedName("TestProperty", "http://test.org"),
                ArrayDimensions = "1,2,3"
            };

            var design2 = new PropertyDesign
            {
                SymbolicName = new XmlQualifiedName("TestProperty", "http://test.org"),
                ArrayDimensions = "4,5,6"
            };

            // Act
            bool result = design1.Equals(design2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing instances with different Historizing flag.
        /// </summary>
        [Test]
        public void Equals_WithDifferentHistorizing_ReturnsFalse()
        {
            // Arrange
            var design1 = new PropertyDesign
            {
                SymbolicName = new XmlQualifiedName("TestProperty", "http://test.org"),
                Historizing = true,
                HistorizingSpecified = true
            };

            var design2 = new PropertyDesign
            {
                SymbolicName = new XmlQualifiedName("TestProperty", "http://test.org"),
                Historizing = false,
                HistorizingSpecified = true
            };

            // Act
            bool result = design1.Equals(design2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing instances with different MinimumSamplingInterval.
        /// </summary>
        [Test]
        public void Equals_WithDifferentMinimumSamplingInterval_ReturnsFalse()
        {
            // Arrange
            var design1 = new PropertyDesign
            {
                SymbolicName = new XmlQualifiedName("TestProperty", "http://test.org"),
                MinimumSamplingInterval = 100,
                MinimumSamplingIntervalSpecified = true
            };

            var design2 = new PropertyDesign
            {
                SymbolicName = new XmlQualifiedName("TestProperty", "http://test.org"),
                MinimumSamplingInterval = 200,
                MinimumSamplingIntervalSpecified = true
            };

            // Act
            bool result = design1.Equals(design2);

            // Assert
            Assert.That(result, Is.False);
        }
    }
}
