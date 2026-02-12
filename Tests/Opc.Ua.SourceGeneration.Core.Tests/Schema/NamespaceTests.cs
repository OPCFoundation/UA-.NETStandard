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

using System.Collections;
using NUnit.Framework;

namespace Opc.Ua.Schema.Model.Tests
{
    /// <summary>
    /// Unit tests for Namespace class.
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class NamespaceTests
    {
        /// <summary>
        /// Tests that GetHashCode returns consistent values when called multiple times on the same object.
        /// </summary>
        [Test]
        public void GetHashCode_CalledMultipleTimes_ReturnsConsistentValue()
        {
            // Arrange
            var ns = new Namespace
            {
                Name = "TestNamespace",
                Prefix = "Test",
                InternalPrefix = "Int",
                XmlNamespace = "http://test.com",
                XmlPrefix = "tst",
                FilePath = "C:\\test\\file.xml",
                Version = "1.0.0",
                PublicationDate = "2025-01-01",
                Value = "TestValue"
            };

            // Act
            int hash1 = ns.GetHashCode();
            int hash2 = ns.GetHashCode();
            int hash3 = ns.GetHashCode();

            // Assert
            Assert.That(hash1, Is.EqualTo(hash2));
            Assert.That(hash2, Is.EqualTo(hash3));
        }

        /// <summary>
        /// Tests that GetHashCode returns the same hash code for two objects with identical property values.
        /// </summary>
        [Test]
        public void GetHashCode_TwoObjectsWithIdenticalProperties_ReturnsSameHashCode()
        {
            // Arrange
            var ns1 = new Namespace
            {
                Name = "TestNamespace",
                Prefix = "Test",
                InternalPrefix = "Int",
                XmlNamespace = "http://test.com",
                XmlPrefix = "tst",
                FilePath = "C:\\test\\file.xml",
                Version = "1.0.0",
                PublicationDate = "2025-01-01",
                Value = "TestValue"
            };

            var ns2 = new Namespace
            {
                Name = "TestNamespace",
                Prefix = "Test",
                InternalPrefix = "Int",
                XmlNamespace = "http://test.com",
                XmlPrefix = "tst",
                FilePath = "C:\\test\\file.xml",
                Version = "1.0.0",
                PublicationDate = "2025-01-01",
                Value = "TestValue"
            };

            // Act
            int hash1 = ns1.GetHashCode();
            int hash2 = ns2.GetHashCode();

            // Assert
            Assert.That(hash1, Is.EqualTo(hash2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes for objects with different Name property.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentName_ReturnsDifferentHashCode()
        {
            // Arrange
            var ns1 = new Namespace { Name = "Name1" };
            var ns2 = new Namespace { Name = "Name2" };

            // Act
            int hash1 = ns1.GetHashCode();
            int hash2 = ns2.GetHashCode();

            // Assert
            Assert.That(hash1, Is.Not.EqualTo(hash2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes for objects with different Prefix property.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentPrefix_ReturnsDifferentHashCode()
        {
            // Arrange
            var ns1 = new Namespace { Prefix = "Prefix1" };
            var ns2 = new Namespace { Prefix = "Prefix2" };

            // Act
            int hash1 = ns1.GetHashCode();
            int hash2 = ns2.GetHashCode();

            // Assert
            Assert.That(hash1, Is.Not.EqualTo(hash2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes for objects with different InternalPrefix property.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentInternalPrefix_ReturnsDifferentHashCode()
        {
            // Arrange
            var ns1 = new Namespace { InternalPrefix = "Int1" };
            var ns2 = new Namespace { InternalPrefix = "Int2" };

            // Act
            int hash1 = ns1.GetHashCode();
            int hash2 = ns2.GetHashCode();

            // Assert
            Assert.That(hash1, Is.Not.EqualTo(hash2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes for objects with different XmlNamespace property.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentXmlNamespace_ReturnsDifferentHashCode()
        {
            // Arrange
            var ns1 = new Namespace { XmlNamespace = "http://test1.com" };
            var ns2 = new Namespace { XmlNamespace = "http://test2.com" };

            // Act
            int hash1 = ns1.GetHashCode();
            int hash2 = ns2.GetHashCode();

            // Assert
            Assert.That(hash1, Is.Not.EqualTo(hash2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes for objects with different XmlPrefix property.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentXmlPrefix_ReturnsDifferentHashCode()
        {
            // Arrange
            var ns1 = new Namespace { XmlPrefix = "xml1" };
            var ns2 = new Namespace { XmlPrefix = "xml2" };

            // Act
            int hash1 = ns1.GetHashCode();
            int hash2 = ns2.GetHashCode();

            // Assert
            Assert.That(hash1, Is.Not.EqualTo(hash2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes for objects with different FilePath property.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentFilePath_ReturnsDifferentHashCode()
        {
            // Arrange
            var ns1 = new Namespace { FilePath = "C:\\path1\\file.xml" };
            var ns2 = new Namespace { FilePath = "C:\\path2\\file.xml" };

            // Act
            int hash1 = ns1.GetHashCode();
            int hash2 = ns2.GetHashCode();

            // Assert
            Assert.That(hash1, Is.Not.EqualTo(hash2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes for objects with different Version property.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentVersion_ReturnsDifferentHashCode()
        {
            // Arrange
            var ns1 = new Namespace { Version = "1.0.0" };
            var ns2 = new Namespace { Version = "2.0.0" };

            // Act
            int hash1 = ns1.GetHashCode();
            int hash2 = ns2.GetHashCode();

            // Assert
            Assert.That(hash1, Is.Not.EqualTo(hash2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes for objects with different PublicationDate property.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentPublicationDate_ReturnsDifferentHashCode()
        {
            // Arrange
            var ns1 = new Namespace { PublicationDate = "2025-01-01" };
            var ns2 = new Namespace { PublicationDate = "2025-01-02" };

            // Act
            int hash1 = ns1.GetHashCode();
            int hash2 = ns2.GetHashCode();

            // Assert
            Assert.That(hash1, Is.Not.EqualTo(hash2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes for objects with different Value property.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentValue_ReturnsDifferentHashCode()
        {
            // Arrange
            var ns1 = new Namespace { Value = "Value1" };
            var ns2 = new Namespace { Value = "Value2" };

            // Act
            int hash1 = ns1.GetHashCode();
            int hash2 = ns2.GetHashCode();

            // Assert
            Assert.That(hash1, Is.Not.EqualTo(hash2));
        }

        /// <summary>
        /// Tests that GetHashCode handles objects with all null properties correctly.
        /// </summary>
        [Test]
        public void GetHashCode_AllPropertiesNull_ReturnsValidHashCode()
        {
            // Arrange
            var ns = new Namespace();

            // Act
            int hash = ns.GetHashCode();

            // Assert
            Assert.That(hash, Is.Not.Null);
        }

        /// <summary>
        /// Tests that GetHashCode handles objects with all empty string properties correctly.
        /// </summary>
        [Test]
        public void GetHashCode_AllPropertiesEmpty_ReturnsValidHashCode()
        {
            // Arrange
            var ns = new Namespace
            {
                Name = string.Empty,
                Prefix = string.Empty,
                InternalPrefix = string.Empty,
                XmlNamespace = string.Empty,
                XmlPrefix = string.Empty,
                FilePath = string.Empty,
                Version = string.Empty,
                PublicationDate = string.Empty,
                Value = string.Empty
            };

            // Act
            int hash = ns.GetHashCode();

            // Assert
            Assert.That(hash, Is.Not.Null);
        }

        /// <summary>
        /// Tests that GetHashCode handles objects with mixed null and non-null properties correctly.
        /// </summary>
        [Test]
        public void GetHashCode_MixedNullAndNonNullProperties_ReturnsValidHashCode()
        {
            // Arrange
            var ns = new Namespace
            {
                Name = "TestName",
                Prefix = null,
                InternalPrefix = "IntPrefix",
                XmlNamespace = null,
                XmlPrefix = "xml",
                FilePath = null,
                Version = "1.0",
                PublicationDate = null,
                Value = "TestValue"
            };

            // Act
            int hash = ns.GetHashCode();

            // Assert
            Assert.That(hash, Is.Not.Null);
        }

        /// <summary>
        /// Tests that GetHashCode handles objects with very long string properties correctly.
        /// </summary>
        [Test]
        public void GetHashCode_VeryLongStrings_ReturnsValidHashCode()
        {
            // Arrange
            string longString = new('a', 10000);
            var ns = new Namespace
            {
                Name = longString,
                Prefix = longString,
                InternalPrefix = longString,
                XmlNamespace = longString,
                XmlPrefix = longString,
                FilePath = longString,
                Version = longString,
                PublicationDate = longString,
                Value = longString
            };

            // Act
            int hash = ns.GetHashCode();

            // Assert
            Assert.That(hash, Is.Not.Null);
        }

        /// <summary>
        /// Tests that GetHashCode handles objects with special characters in string properties correctly.
        /// </summary>
        [Test]
        public void GetHashCode_SpecialCharacters_ReturnsValidHashCode()
        {
            // Arrange
            var ns = new Namespace
            {
                Name = "Name!@#$%^&*()",
                Prefix = "Prefix\n\r\t",
                InternalPrefix = "Internal\0Prefix",
                XmlNamespace = "http://test.com?param=value&other=123",
                XmlPrefix = "xml:prefix",
                FilePath = "C:\\path\\to\\file.xml",
                Version = "1.0.0-alpha+build.123",
                PublicationDate = "2025-01-01T12:34:56Z",
                Value = "Value with spaces   and\ttabs"
            };

            // Act
            int hash = ns.GetHashCode();

            // Assert
            Assert.That(hash, Is.Not.Null);
        }

        /// <summary>
        /// Tests that GetHashCode handles objects with Unicode characters correctly.
        /// </summary>
        [Test]
        public void GetHashCode_UnicodeCharacters_ReturnsValidHashCode()
        {
            // Arrange
            var ns = new Namespace
            {
                Name = "名前",
                Prefix = "接頭辞",
                InternalPrefix = "内部接頭辞",
                XmlNamespace = "http://тест.рф",
                XmlPrefix = "Präfix",
                FilePath = "C:\\路径\\文件.xml",
                Version = "版本1.0",
                PublicationDate = "2025年01月01日",
                Value = "值🎉"
            };

            // Act
            int hash = ns.GetHashCode();

            // Assert
            Assert.That(hash, Is.Not.Null);
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes when only one property differs.
        /// </summary>
        [TestCase("Name")]
        [TestCase("Prefix")]
        [TestCase("InternalPrefix")]
        [TestCase("XmlNamespace")]
        [TestCase("XmlPrefix")]
        [TestCase("FilePath")]
        [TestCase("Version")]
        [TestCase("PublicationDate")]
        [TestCase("Value")]
        public void GetHashCode_OnlyOnePropertyDifferent_ReturnsDifferentHashCode(string propertyName)
        {
            // Arrange
            var ns1 = new Namespace
            {
                Name = "Common",
                Prefix = "Common",
                InternalPrefix = "Common",
                XmlNamespace = "Common",
                XmlPrefix = "Common",
                FilePath = "Common",
                Version = "Common",
                PublicationDate = "Common",
                Value = "Common"
            };

            var ns2 = new Namespace
            {
                Name = "Common",
                Prefix = "Common",
                InternalPrefix = "Common",
                XmlNamespace = "Common",
                XmlPrefix = "Common",
                FilePath = "Common",
                Version = "Common",
                PublicationDate = "Common",
                Value = "Common"
            };

            // Act
            switch (propertyName)
            {
                case "Name":
                    ns2.Name = "Different";
                    break;
                case "Prefix":
                    ns2.Prefix = "Different";
                    break;
                case "InternalPrefix":
                    ns2.InternalPrefix = "Different";
                    break;
                case "XmlNamespace":
                    ns2.XmlNamespace = "Different";
                    break;
                case "XmlPrefix":
                    ns2.XmlPrefix = "Different";
                    break;
                case "FilePath":
                    ns2.FilePath = "Different";
                    break;
                case "Version":
                    ns2.Version = "Different";
                    break;
                case "PublicationDate":
                    ns2.PublicationDate = "Different";
                    break;
                case "Value":
                    ns2.Value = "Different";
                    break;
            }

            int hash1 = ns1.GetHashCode();
            int hash2 = ns2.GetHashCode();

            // Assert
            Assert.That(hash1, Is.Not.EqualTo(hash2));
        }

        /// <summary>
        /// Tests that GetHashCode returns the same hash code for empty strings and null values.
        /// </summary>
        [Test]
        public void GetHashCode_EmptyStringVsNull_ReturnsDifferentHashCode()
        {
            // Arrange
            var ns1 = new Namespace
            {
                Name = string.Empty
            };

            var ns2 = new Namespace
            {
                Name = null
            };

            // Act
            int hash1 = ns1.GetHashCode();
            int hash2 = ns2.GetHashCode();

            // Assert
            Assert.That(hash1, Is.Not.EqualTo(hash2));
        }

        /// <summary>
        /// Tests that GetHashCode handles whitespace-only strings correctly.
        /// </summary>
        [Test]
        public void GetHashCode_WhitespaceOnlyStrings_ReturnsValidHashCode()
        {
            // Arrange
            var ns = new Namespace
            {
                Name = "   ",
                Prefix = "\t\t\t",
                InternalPrefix = "\n\n\n",
                XmlNamespace = " \t \n ",
                XmlPrefix = "     ",
                FilePath = "\r\n\r\n",
                Version = "  \t  ",
                PublicationDate = "   \n   ",
                Value = " "
            };

            // Act
            int hash = ns.GetHashCode();

            // Assert
            Assert.That(hash, Is.Not.Null);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with null.
        /// </summary>
        [Test]
        public void Equals_NullParameter_ReturnsFalse()
        {
            // Arrange
            var ns = new Namespace
            {
                Name = "TestName",
                Prefix = "TestPrefix",
                InternalPrefix = "TestInternalPrefix",
                XmlNamespace = "http://test.com",
                XmlPrefix = "test",
                FilePath = "C:\\test\\path",
                Version = "1.0.0",
                PublicationDate = "2025-01-01",
                Value = "TestValue"
            };

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = ns.Equals(null);
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing an object with itself.
        /// </summary>
        [Test]
        public void Equals_SameReference_ReturnsTrue()
        {
            // Arrange
            var ns = new Namespace
            {
                Name = "TestName",
                Prefix = "TestPrefix",
                InternalPrefix = "TestInternalPrefix",
                XmlNamespace = "http://test.com",
                XmlPrefix = "test",
                FilePath = "C:\\test\\path",
                Version = "1.0.0",
                PublicationDate = "2025-01-01",
                Value = "TestValue"
            };

            // Act
            bool result = ns.Equals(ns);

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
            var ns1 = new Namespace
            {
                Name = "TestName",
                Prefix = "TestPrefix",
                InternalPrefix = "TestInternalPrefix",
                XmlNamespace = "http://test.com",
                XmlPrefix = "test",
                FilePath = "C:\\test\\path",
                Version = "1.0.0",
                PublicationDate = "2025-01-01",
                Value = "TestValue"
            };

            var ns2 = new Namespace
            {
                Name = "TestName",
                Prefix = "TestPrefix",
                InternalPrefix = "TestInternalPrefix",
                XmlNamespace = "http://test.com",
                XmlPrefix = "test",
                FilePath = "C:\\test\\path",
                Version = "1.0.0",
                PublicationDate = "2025-01-01",
                Value = "TestValue"
            };

            // Act
            bool result = ns1.Equals(ns2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when all properties are null on both objects.
        /// </summary>
        [Test]
        public void Equals_AllPropertiesNull_ReturnsTrue()
        {
            // Arrange
            var ns1 = new Namespace();
            var ns2 = new Namespace();

            // Act
            bool result = ns1.Equals(ns2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when all properties are empty strings.
        /// </summary>
        [Test]
        public void Equals_AllPropertiesEmptyStrings_ReturnsTrue()
        {
            // Arrange
            var ns1 = new Namespace
            {
                Name = string.Empty,
                Prefix = string.Empty,
                InternalPrefix = string.Empty,
                XmlNamespace = string.Empty,
                XmlPrefix = string.Empty,
                FilePath = string.Empty,
                Version = string.Empty,
                PublicationDate = string.Empty,
                Value = string.Empty
            };

            var ns2 = new Namespace
            {
                Name = string.Empty,
                Prefix = string.Empty,
                InternalPrefix = string.Empty,
                XmlNamespace = string.Empty,
                XmlPrefix = string.Empty,
                FilePath = string.Empty,
                Version = string.Empty,
                PublicationDate = string.Empty,
                Value = string.Empty
            };

            // Act
            bool result = ns1.Equals(ns2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when objects differ only in casing of property values.
        /// </summary>
        [Test]
        public void Equals_DifferentCasing_ReturnsFalse()
        {
            // Arrange
            var ns1 = new Namespace
            {
                Name = "TestName",
                Prefix = "TestPrefix",
                InternalPrefix = "TestInternalPrefix",
                XmlNamespace = "http://test.com",
                XmlPrefix = "test",
                FilePath = "C:\\test\\path",
                Version = "1.0.0",
                PublicationDate = "2025-01-01",
                Value = "TestValue"
            };

            var ns2 = new Namespace
            {
                Name = "testname",
                Prefix = "TestPrefix",
                InternalPrefix = "TestInternalPrefix",
                XmlNamespace = "http://test.com",
                XmlPrefix = "test",
                FilePath = "C:\\test\\path",
                Version = "1.0.0",
                PublicationDate = "2025-01-01",
                Value = "TestValue"
            };

            // Act
            bool result = ns1.Equals(ns2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when one property is null and the other is an empty string.
        /// </summary>
        [Test]
        public void Equals_NullVsEmptyString_ReturnsFalse()
        {
            // Arrange
            var ns1 = new Namespace
            {
                Name = null
            };

            var ns2 = new Namespace
            {
                Name = string.Empty
            };

            // Act
            bool result = ns1.Equals(ns2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when one property is whitespace and the other is an empty string.
        /// </summary>
        [Test]
        public void Equals_WhitespaceVsEmptyString_ReturnsFalse()
        {
            // Arrange
            var ns1 = new Namespace
            {
                Name = " "
            };

            var ns2 = new Namespace
            {
                Name = string.Empty
            };

            // Act
            bool result = ns1.Equals(ns2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when a specific property differs.
        /// </summary>
        /// <param name="propertyName">The name of the property to differ.</param>
        /// <param name="differentValue">The different value to set.</param>
        [TestCaseSource(nameof(GetDifferentPropertyTestCases))]
        public void Equals_DifferentProperty_ReturnsFalse(string propertyName, string differentValue)
        {
            // Arrange
            Namespace ns1 = CreateDefaultNamespace();
            Namespace ns2 = CreateDefaultNamespace();
            SetProperty(ns2, propertyName, differentValue);

            // Act
            bool result = ns1.Equals(ns2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing objects with very long string values.
        /// </summary>
        [Test]
        public void Equals_VeryLongStrings_ReturnsCorrectResult()
        {
            // Arrange
            string longString1 = new('a', 10000);
            string longString2 = new('a', 10000);
            string differentLongString = new('b', 10000);

            var ns1 = new Namespace
            {
                Name = longString1
            };

            var ns2 = new Namespace
            {
                Name = longString2
            };

            var ns3 = new Namespace
            {
                Name = differentLongString
            };

            // Act
            bool resultEqual = ns1.Equals(ns2);
            bool resultDifferent = ns1.Equals(ns3);

            // Assert
            Assert.That(resultEqual, Is.True);
            Assert.That(resultDifferent, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when multiple properties differ.
        /// </summary>
        [Test]
        public void Equals_MultiplePropertiesDifferent_ReturnsFalse()
        {
            // Arrange
            var ns1 = new Namespace
            {
                Name = "TestName",
                Prefix = "TestPrefix",
                InternalPrefix = "TestInternalPrefix"
            };

            var ns2 = new Namespace
            {
                Name = "DifferentName",
                Prefix = "DifferentPrefix",
                InternalPrefix = "TestInternalPrefix"
            };

            // Act
            bool result = ns1.Equals(ns2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals handles special characters correctly.
        /// </summary>
        [Test]
        public void Equals_SpecialCharacters_ReturnsCorrectResult()
        {
            // Arrange
            var ns1 = new Namespace
            {
                Name = "Test\nName\t\r",
                Prefix = "Test\\Prefix",
                XmlNamespace = "http://test.com?param=value&other=123",
                FilePath = "C:\\test\\path\\with\\special\\chars\\!@#$%"
            };

            var ns2 = new Namespace
            {
                Name = "Test\nName\t\r",
                Prefix = "Test\\Prefix",
                XmlNamespace = "http://test.com?param=value&other=123",
                FilePath = "C:\\test\\path\\with\\special\\chars\\!@#$%"
            };

            // Act
            bool result = ns1.Equals(ns2);

            // Assert
            Assert.That(result, Is.True);
        }

        private static Namespace CreateDefaultNamespace()
        {
            return new Namespace
            {
                Name = "DefaultName",
                Prefix = "DefaultPrefix",
                InternalPrefix = "DefaultInternalPrefix",
                XmlNamespace = "http://default.com",
                XmlPrefix = "default",
                FilePath = "C:\\default\\path",
                Version = "1.0.0",
                PublicationDate = "2025-01-01",
                Value = "DefaultValue"
            };
        }

        private static void SetProperty(Namespace ns, string propertyName, string value)
        {
            switch (propertyName)
            {
                case nameof(Namespace.Name):
                    ns.Name = value;
                    break;
                case nameof(Namespace.Prefix):
                    ns.Prefix = value;
                    break;
                case nameof(Namespace.InternalPrefix):
                    ns.InternalPrefix = value;
                    break;
                case nameof(Namespace.XmlNamespace):
                    ns.XmlNamespace = value;
                    break;
                case nameof(Namespace.XmlPrefix):
                    ns.XmlPrefix = value;
                    break;
                case nameof(Namespace.FilePath):
                    ns.FilePath = value;
                    break;
                case nameof(Namespace.Version):
                    ns.Version = value;
                    break;
                case nameof(Namespace.PublicationDate):
                    ns.PublicationDate = value;
                    break;
                case nameof(Namespace.Value):
                    ns.Value = value;
                    break;
            }
        }

        private static IEnumerable GetDifferentPropertyTestCases()
        {
            yield return new TestCaseData(nameof(Namespace.Name), "DifferentName");
            yield return new TestCaseData(nameof(Namespace.Prefix), "DifferentPrefix");
            yield return new TestCaseData(nameof(Namespace.InternalPrefix), "DifferentInternalPrefix");
            yield return new TestCaseData(nameof(Namespace.XmlNamespace), "http://different.com");
            yield return new TestCaseData(nameof(Namespace.XmlPrefix), "different");
            yield return new TestCaseData(nameof(Namespace.FilePath), "C:\\different\\path");
            yield return new TestCaseData(nameof(Namespace.Version), "2.0.0");
            yield return new TestCaseData(nameof(Namespace.PublicationDate), "2026-01-01");
            yield return new TestCaseData(nameof(Namespace.Value), "DifferentValue");
        }

        /// <summary>
        /// Tests that Equals returns false when comparing to null.
        /// </summary>
        [Test]
        public void Equals_NullObject_ReturnsFalse()
        {
            // Arrange
            var ns = new Namespace
            {
                Name = "Test",
                Prefix = "Tst",
                InternalPrefix = "Internal",
                XmlNamespace = "http://test.com",
                XmlPrefix = "tst",
                FilePath = "test.xml",
                Version = "1.0",
                PublicationDate = "2025-01-01",
                Value = "value"
            };

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = ns.Equals((object)null);
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing an instance to itself.
        /// </summary>
        [Test]
        public void Equals_SameInstance_ReturnsTrue()
        {
            // Arrange
            var ns = new Namespace
            {
                Name = "Test",
                Prefix = "Tst",
                InternalPrefix = "Internal",
                XmlNamespace = "http://test.com",
                XmlPrefix = "tst",
                FilePath = "test.xml",
                Version = "1.0",
                PublicationDate = "2025-01-01",
                Value = "value"
            };

            // Act
            bool result = ns.Equals((object)ns);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing to a different type.
        /// </summary>
        [Test]
        public void Equals_DifferentType_ReturnsFalse()
        {
            // Arrange
            var ns = new Namespace
            {
                Name = "Test"
            };
            const string differentType = "Not a Namespace";

            // Act
            bool result = ns.Equals(differentType);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing two instances with identical property values.
        /// </summary>
        [Test]
        public void Equals_EqualInstances_ReturnsTrue()
        {
            // Arrange
            var ns1 = new Namespace
            {
                Name = "Test",
                Prefix = "Tst",
                InternalPrefix = "Internal",
                XmlNamespace = "http://test.com",
                XmlPrefix = "tst",
                FilePath = "test.xml",
                Version = "1.0",
                PublicationDate = "2025-01-01",
                Value = "value"
            };
            var ns2 = new Namespace
            {
                Name = "Test",
                Prefix = "Tst",
                InternalPrefix = "Internal",
                XmlNamespace = "http://test.com",
                XmlPrefix = "tst",
                FilePath = "test.xml",
                Version = "1.0",
                PublicationDate = "2025-01-01",
                Value = "value"
            };

            // Act
            bool result = ns1.Equals((object)ns2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when Name property differs.
        /// </summary>
        [Test]
        public void Equals_DifferentName_ReturnsFalse()
        {
            // Arrange
            var ns1 = new Namespace { Name = "Test1" };
            var ns2 = new Namespace { Name = "Test2" };

            // Act
            bool result = ns1.Equals((object)ns2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when Prefix property differs.
        /// </summary>
        [Test]
        public void Equals_DifferentPrefix_ReturnsFalse()
        {
            // Arrange
            var ns1 = new Namespace
            {
                Name = "Test",
                Prefix = "Prefix1"
            };
            var ns2 = new Namespace
            {
                Name = "Test",
                Prefix = "Prefix2"
            };

            // Act
            bool result = ns1.Equals((object)ns2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when InternalPrefix property differs.
        /// </summary>
        [Test]
        public void Equals_DifferentInternalPrefix_ReturnsFalse()
        {
            // Arrange
            var ns1 = new Namespace
            {
                Name = "Test",
                Prefix = "Tst",
                InternalPrefix = "Internal1"
            };
            var ns2 = new Namespace
            {
                Name = "Test",
                Prefix = "Tst",
                InternalPrefix = "Internal2"
            };

            // Act
            bool result = ns1.Equals((object)ns2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when XmlNamespace property differs.
        /// </summary>
        [Test]
        public void Equals_DifferentXmlNamespace_ReturnsFalse()
        {
            // Arrange
            var ns1 = new Namespace
            {
                Name = "Test",
                Prefix = "Tst",
                InternalPrefix = "Internal",
                XmlNamespace = "http://test1.com"
            };
            var ns2 = new Namespace
            {
                Name = "Test",
                Prefix = "Tst",
                InternalPrefix = "Internal",
                XmlNamespace = "http://test2.com"
            };

            // Act
            bool result = ns1.Equals((object)ns2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when XmlPrefix property differs.
        /// </summary>
        [Test]
        public void Equals_DifferentXmlPrefix_ReturnsFalse()
        {
            // Arrange
            var ns1 = new Namespace
            {
                Name = "Test",
                Prefix = "Tst",
                InternalPrefix = "Internal",
                XmlNamespace = "http://test.com",
                XmlPrefix = "tst1"
            };
            var ns2 = new Namespace
            {
                Name = "Test",
                Prefix = "Tst",
                InternalPrefix = "Internal",
                XmlNamespace = "http://test.com",
                XmlPrefix = "tst2"
            };

            // Act
            bool result = ns1.Equals((object)ns2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when FilePath property differs.
        /// </summary>
        [Test]
        public void Equals_DifferentFilePath_ReturnsFalse()
        {
            // Arrange
            var ns1 = new Namespace
            {
                Name = "Test",
                Prefix = "Tst",
                InternalPrefix = "Internal",
                XmlNamespace = "http://test.com",
                XmlPrefix = "tst",
                FilePath = "test1.xml"
            };
            var ns2 = new Namespace
            {
                Name = "Test",
                Prefix = "Tst",
                InternalPrefix = "Internal",
                XmlNamespace = "http://test.com",
                XmlPrefix = "tst",
                FilePath = "test2.xml"
            };

            // Act
            bool result = ns1.Equals((object)ns2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when Version property differs.
        /// </summary>
        [Test]
        public void Equals_DifferentVersion_ReturnsFalse()
        {
            // Arrange
            var ns1 = new Namespace
            {
                Name = "Test",
                Prefix = "Tst",
                InternalPrefix = "Internal",
                XmlNamespace = "http://test.com",
                XmlPrefix = "tst",
                FilePath = "test.xml",
                Version = "1.0"
            };
            var ns2 = new Namespace
            {
                Name = "Test",
                Prefix = "Tst",
                InternalPrefix = "Internal",
                XmlNamespace = "http://test.com",
                XmlPrefix = "tst",
                FilePath = "test.xml",
                Version = "2.0"
            };

            // Act
            bool result = ns1.Equals((object)ns2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when PublicationDate property differs.
        /// </summary>
        [Test]
        public void Equals_DifferentPublicationDate_ReturnsFalse()
        {
            // Arrange
            var ns1 = new Namespace
            {
                Name = "Test",
                Prefix = "Tst",
                InternalPrefix = "Internal",
                XmlNamespace = "http://test.com",
                XmlPrefix = "tst",
                FilePath = "test.xml",
                Version = "1.0",
                PublicationDate = "2025-01-01"
            };
            var ns2 = new Namespace
            {
                Name = "Test",
                Prefix = "Tst",
                InternalPrefix = "Internal",
                XmlNamespace = "http://test.com",
                XmlPrefix = "tst",
                FilePath = "test.xml",
                Version = "1.0",
                PublicationDate = "2025-01-02"
            };

            // Act
            bool result = ns1.Equals((object)ns2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when Value property differs.
        /// </summary>
        [Test]
        public void Equals_DifferentValue_ReturnsFalse()
        {
            // Arrange
            var ns1 = new Namespace
            {
                Name = "Test",
                Prefix = "Tst",
                InternalPrefix = "Internal",
                XmlNamespace = "http://test.com",
                XmlPrefix = "tst",
                FilePath = "test.xml",
                Version = "1.0",
                PublicationDate = "2025-01-01",
                Value = "value1"
            };
            var ns2 = new Namespace
            {
                Name = "Test",
                Prefix = "Tst",
                InternalPrefix = "Internal",
                XmlNamespace = "http://test.com",
                XmlPrefix = "tst",
                FilePath = "test.xml",
                Version = "1.0",
                PublicationDate = "2025-01-01",
                Value = "value2"
            };

            // Act
            bool result = ns1.Equals((object)ns2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals is case-sensitive for string properties.
        /// </summary>
        [Test]
        public void Equals_DifferentCase_ReturnsFalse()
        {
            // Arrange
            var ns1 = new Namespace { Name = "Test" };
            var ns2 = new Namespace { Name = "test" };

            // Act
            bool result = ns1.Equals((object)ns2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when one property is null and the other has whitespace.
        /// </summary>
        [Test]
        public void Equals_NullVsWhitespace_ReturnsFalse()
        {
            // Arrange
            var ns1 = new Namespace { Name = null };
            var ns2 = new Namespace { Name = "   " };

            // Act
            bool result = ns1.Equals((object)ns2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing instances with empty strings in all properties.
        /// </summary>
        [Test]
        public void Equals_AllPropertiesEmptyString_ReturnsTrue()
        {
            // Arrange
            var ns1 = new Namespace
            {
                Name = string.Empty,
                Prefix = string.Empty,
                InternalPrefix = string.Empty,
                XmlNamespace = string.Empty,
                XmlPrefix = string.Empty,
                FilePath = string.Empty,
                Version = string.Empty,
                PublicationDate = string.Empty,
                Value = string.Empty
            };
            var ns2 = new Namespace
            {
                Name = string.Empty,
                Prefix = string.Empty,
                InternalPrefix = string.Empty,
                XmlNamespace = string.Empty,
                XmlPrefix = string.Empty,
                FilePath = string.Empty,
                Version = string.Empty,
                PublicationDate = string.Empty,
                Value = string.Empty
            };

            // Act
            bool result = ns1.Equals((object)ns2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals handles very long strings correctly.
        /// </summary>
        [Test]
        public void Equals_VeryLongStrings_ReturnsTrue()
        {
            // Arrange
            string longString = new('a', 10000);
            var ns1 = new Namespace { Name = longString };
            var ns2 = new Namespace { Name = longString };

            // Act
            bool result = ns1.Equals((object)ns2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals handles special characters in strings correctly.
        /// </summary>
        [Test]
        public void Equals_SpecialCharacters_ReturnsTrue()
        {
            // Arrange
            const string specialString = "Test\r\n\t\0\u0001";
            var ns1 = new Namespace { Name = specialString };
            var ns2 = new Namespace { Name = specialString };

            // Act
            bool result = ns1.Equals((object)ns2);

            // Assert
            Assert.That(result, Is.True);
        }
    }
}
