/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
            bool result = design.Equals(null!);
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
