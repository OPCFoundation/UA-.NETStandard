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
using Opc.Ua.Client.ComplexTypes;

namespace Opc.Ua.Client.Tests.ComplexTypes
{
    /// <summary>
    /// Tests for the Enumeration type created by DefaultComplexTypeBuilder.
    /// </summary>
    [TestFixture]
    [Category("DefaultComplexTypes")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class DefaultEnumerationTests
    {
        private DefaultComplexTypeFactory m_factory;
        private IComplexTypeBuilder m_builder;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_factory = new DefaultComplexTypeFactory();
            m_builder = m_factory.Create(
                Namespaces.OpcUaEncoderTests,
                3,
                "EnumTests");
        }

        /// <summary>
        /// Create an enumeration type and verify basic properties.
        /// </summary>
        [Test]
        public void CreateEnumType()
        {
            var enumDefinition = new EnumDefinition
            {
                Fields =
                [
                    new EnumField
                    {
                        Name = "Red",
                        Value = 0,
                        Description = LocalizedText.From("Red color")
                    },
                    new EnumField
                    {
                        Name = "Green",
                        Value = 1,
                        Description = LocalizedText.From("Green color")
                    },
                    new EnumField
                    {
                        Name = "Blue",
                        Value = 2,
                        Description = LocalizedText.From("Blue color")
                    }
                ]
            };

            IEnumeratedType enumType = m_builder.AddEnumType(
                QualifiedName.From("TestColors"),
                enumDefinition);

            Assert.That(enumType, Is.Not.Null);
            Assert.That(enumType.XmlName, Is.Not.Null);
            Assert.That(
                enumType.XmlName,
                Is.EqualTo(new XmlQualifiedName("TestColors", Namespaces.OpcUaEncoderTests)));
            Assert.That(enumType.Type, Is.Not.Null);
        }

        /// <summary>
        /// Verify the default value of an enumeration.
        /// </summary>
        [Test]
        public void EnumDefaultValue()
        {
            var enumDefinition = new EnumDefinition
            {
                Fields =
                [
                    new EnumField { Name = "None", Value = 0 },
                    new EnumField { Name = "Active", Value = 1 }
                ]
            };

            IEnumeratedType enumType = m_builder.AddEnumType(
                QualifiedName.From("TestStatus"),
                enumDefinition);

            Assert.That(enumType, Is.Not.Null);
            Assert.That(enumType.Default, Is.EqualTo(default(EnumValue)));
        }

        /// <summary>
        /// Verify TryGetSymbol returns false (not yet implemented in Enumeration).
        /// </summary>
        [Test]
        public void EnumTryGetSymbolNotImplemented()
        {
            var enumDefinition = new EnumDefinition
            {
                Fields =
                [
                    new EnumField { Name = "First", Value = 0 },
                    new EnumField { Name = "Second", Value = 1 }
                ]
            };

            IEnumeratedType enumType = m_builder.AddEnumType(
                QualifiedName.From("TestSymbolLookup"),
                enumDefinition);

            bool result = enumType.TryGetSymbol(0, out string symbol);
            Assert.That(result, Is.False);
            Assert.That(symbol, Is.Null);
        }

        /// <summary>
        /// Verify TryGetValue returns false (not yet implemented in Enumeration).
        /// </summary>
        [Test]
        public void EnumTryGetValueNotImplemented()
        {
            var enumDefinition = new EnumDefinition
            {
                Fields =
                [
                    new EnumField { Name = "First", Value = 0 },
                    new EnumField { Name = "Second", Value = 1 }
                ]
            };

            IEnumeratedType enumType = m_builder.AddEnumType(
                QualifiedName.From("TestValueLookup"),
                enumDefinition);

            bool result = enumType.TryGetValue("First", out int value);
            Assert.That(result, Is.False);
            Assert.That(value, Is.EqualTo(0));
        }

        /// <summary>
        /// Verify enumeration with empty fields.
        /// </summary>
        [Test]
        public void CreateEnumTypeWithEmptyFields()
        {
            var enumDefinition = new EnumDefinition
            {
                Fields = []
            };

            IEnumeratedType enumType = m_builder.AddEnumType(
                QualifiedName.From("TestEmpty"),
                enumDefinition);

            Assert.That(enumType, Is.Not.Null);
            Assert.That(enumType.XmlName.Name, Is.EqualTo("TestEmpty"));
        }

        /// <summary>
        /// Create multiple enumerations from the same builder.
        /// </summary>
        [Test]
        public void CreateMultipleEnumTypes()
        {
            var enumDef1 = new EnumDefinition
            {
                Fields = [new EnumField { Name = "A", Value = 0 }]
            };
            var enumDef2 = new EnumDefinition
            {
                Fields = [new EnumField { Name = "X", Value = 0 }]
            };

            IEnumeratedType type1 = m_builder.AddEnumType(
                QualifiedName.From("Enum1"), enumDef1);
            IEnumeratedType type2 = m_builder.AddEnumType(
                QualifiedName.From("Enum2"), enumDef2);

            Assert.That(type1, Is.Not.Null);
            Assert.That(type2, Is.Not.Null);
            Assert.That(type1.XmlName.Name, Is.EqualTo("Enum1"));
            Assert.That(type2.XmlName.Name, Is.EqualTo("Enum2"));
            Assert.That(type1.XmlName, Is.Not.EqualTo(type2.XmlName));
        }
    }
}
