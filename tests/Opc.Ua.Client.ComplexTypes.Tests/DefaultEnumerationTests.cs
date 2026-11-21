// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Xml;
using NUnit.Framework;

namespace Opc.Ua.Client.ComplexTypes.Tests
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
            Assert.That(enumType.Default.Value, Is.Zero);
            Assert.That(enumType.Default.Symbol, Is.EqualTo("None"));
        }

        /// <summary>
        /// Verify the default value of an enumeration.
        /// </summary>
        [Test]
        public void EnumDefaultValueWhenValueIsNotZero()
        {
            var enumDefinition = new EnumDefinition
            {
                Fields =
                [
                    new EnumField { Name = "Active", Value = 1 }
                ]
            };

            IEnumeratedType enumType = m_builder.AddEnumType(
                QualifiedName.From("TestStatus"),
                enumDefinition);

            Assert.That(enumType, Is.Not.Null);
            Assert.That(enumType.Default.Value, Is.EqualTo(1));
            Assert.That(enumType.Default.Symbol, Is.EqualTo("Active"));
        }

        [Test]
        public void EnumTryGetSymbolTest()
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

            bool result = enumType.TryGetSymbol(0, out string? symbol);
            Assert.That(result, Is.True);
            Assert.That(symbol, Is.EqualTo("First"));

            result = enumType.TryGetSymbol(1, out symbol);
            Assert.That(result, Is.True);
            Assert.That(symbol, Is.EqualTo("Second"));

            result = enumType.TryGetSymbol(3, out symbol);
            Assert.That(result, Is.False);
            Assert.That(symbol, Is.Null);
        }

        [Test]
        public void EnumTryGetValueTest()
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
            Assert.That(result, Is.True);
            Assert.That(value, Is.Zero);

            result = enumType.TryGetValue("Second", out value);
            Assert.That(result, Is.True);
            Assert.That(value, Is.EqualTo(1));

            result = enumType.TryGetValue("Third", out value);
            Assert.That(result, Is.False);
            Assert.That(value, Is.Zero);
        }

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
