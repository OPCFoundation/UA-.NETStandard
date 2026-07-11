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
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using NUnit.Framework;
using Opc.Ua.Schema.Binary;

namespace Opc.Ua.Types.Tests.Schema
{
    [TestFixture]
    [Category("Schema")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class BinarySchemaValidatorTests
    {
        private const string TestNamespace = Namespaces.OpcUa;

        [Test]
        public void ValidateAcceptsStructuredOpaqueAndEnumeratedTypes()
        {
            TypeDictionary dictionary = CreateDictionary(
                CreateOpaque("UInt32", 32, "Unsigned integer"),
                CreateEnumerated("Mode", 32),
                new StructuredType
                {
                    Name = "Payload",
                    Field =
                    [
                        new FieldType { Name = "Length", TypeName = QName("UInt32") },
                        new FieldType
                        {
                            Name = "Bytes",
                            TypeName = QName("UInt32"),
                            LengthField = "Length"
                        },
                        new FieldType { Name = "Mode", TypeName = QName("Mode") },
                        new FieldType
                        {
                            Name = "Selected",
                            TypeName = QName("UInt32"),
                            SwitchField = "Mode",
                            SwitchValue = 1,
                            SwitchValueSpecified = true
                        }
                    ]
                });
            var validator = new BinarySchemaValidator();

            Validate(validator, dictionary);

            Assert.That(validator.Dictionary, Is.Not.Null);
            Assert.That(validator.ValidatedDescriptions, Has.Count.EqualTo(3));
            Assert.That(validator.Warnings, Has.Some.Contains("Payload"));
            Assert.That(validator.GetSchema(null), Does.Contain("Payload"));
            Assert.That(validator.GetSchema("Payload"), Does.Contain("StructuredType"));
            Assert.That(validator.GetSchema("Missing"), Does.Contain("TypeDictionary"));
        }

        [Test]
        public void ValidateWarnsForOpaqueTypeWithoutLengthOrDocumentation()
        {
            var validator = new BinarySchemaValidator();

            Validate(validator, CreateDictionary(new OpaqueType { Name = "Blob" }));

            Assert.That(validator.Warnings, Has.Some.Contains("does not have a length specified"));
            Assert.That(validator.Warnings, Has.Some.Contains("does not have any documentation"));
        }

        [Test]
        public void ValidateRejectsEnumeratedTypeWithoutLength()
        {
            var validator = new BinarySchemaValidator();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                validator.Validate(ToStream(CreateDictionary(new EnumeratedType { Name = "Mode" }))));

            Assert.That(exception.Message, Does.Contain("does not have a length specified"));
        }

        [Test]
        public void ValidateRejectsInvalidStructuredFields()
        {
            AssertValidationFails(
                CreateDictionary(new StructuredType { Name = "EmptyName", Field = [new FieldType()] }),
                "has an unnamed field");
            AssertValidationFails(
                CreateDictionary(CreateOpaque("UInt32", 32), new StructuredType
                {
                    Name = "Duplicate",
                    Field =
                    [
                        new FieldType { Name = "Value", TypeName = QName("UInt32") },
                        new FieldType { Name = "Value", TypeName = QName("UInt32") }
                    ]
                }),
                "duplicate field name");
            AssertValidationFails(
                CreateDictionary(new StructuredType
                {
                    Name = "MissingType",
                    Field = [new FieldType { Name = "Value" }]
                }),
                "has no type specified");
            AssertValidationFails(
                CreateDictionary(new StructuredType
                {
                    Name = "UnknownType",
                    Field = [new FieldType { Name = "Value", TypeName = QName("NotDefined") }]
                }),
                "unrecognized type");
        }

        [Test]
        public void ValidateRejectsInvalidLengthSwitchAndAlignment()
        {
            AssertValidationFails(
                CreateDictionary(CreateOpaque("Bit", 1), CreateOpaque("String"), new StructuredType
                {
                    Name = "Unaligned",
                    Field =
                    [
                        new FieldType { Name = "Flag", TypeName = QName("Bit") },
                        new FieldType { Name = "Text", TypeName = QName("String") }
                    ]
                }),
                "not aligned on a byte boundary");
            AssertValidationFails(
                CreateDictionary(CreateOpaque("UInt32", 32), new StructuredType
                {
                    Name = "UnknownLength",
                    Field =
                    [
                        new FieldType
                        {
                            Name = "Bytes",
                            TypeName = QName("UInt32"),
                            LengthField = "Length"
                        }
                    ]
                }),
                "unknownn length field");
            AssertValidationFails(
                CreateDictionary(CreateOpaque("Text"), CreateOpaque("UInt32", 32), new StructuredType
                {
                    Name = "NonIntegerSwitch",
                    Field =
                    [
                        new FieldType { Name = "Selector", TypeName = QName("Text") },
                        new FieldType
                        {
                            Name = "Value",
                            TypeName = QName("UInt32"),
                            SwitchField = "Selector"
                        }
                    ]
                }),
                "not an integer value");
        }

        private static void AssertValidationFails(TypeDictionary dictionary, string expectedMessage)
        {
            var validator = new BinarySchemaValidator();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                Validate(validator, dictionary));

            Assert.That(exception.Message, Does.Contain(expectedMessage));
        }

        private static void Validate(BinarySchemaValidator validator, TypeDictionary dictionary)
        {
#pragma warning disable IL2026 // Test intentionally exercises design-time schema validation.
#pragma warning disable IL3050 // Test intentionally exercises design-time schema validation.
            validator.Validate(ToStream(dictionary));
#pragma warning restore IL3050
#pragma warning restore IL2026
        }

        private static TypeDictionary CreateDictionary(params TypeDescription[] descriptions)
        {
            return new TypeDictionary
            {
                TargetNamespace = TestNamespace,
                Items = descriptions
            };
        }

        private static OpaqueType CreateOpaque(string name, int? lengthInBits = null, string documentation = null)
        {
            var type = new OpaqueType
            {
                Name = name
            };
            if (lengthInBits.HasValue)
            {
                type.LengthInBits = lengthInBits.Value;
                type.LengthInBitsSpecified = true;
            }
            if (documentation != null)
            {
                type.Documentation = new Documentation { Text = [documentation] };
            }
            return type;
        }

        private static EnumeratedType CreateEnumerated(string name, int lengthInBits)
        {
            return new EnumeratedType
            {
                Name = name,
                LengthInBits = lengthInBits,
                LengthInBitsSpecified = true,
                Documentation = new Documentation { Text = [name] }
            };
        }

        private static XmlQualifiedName QName(string name)
        {
            return new XmlQualifiedName(name, TestNamespace);
        }

        private static MemoryStream ToStream(TypeDictionary dictionary)
        {
#pragma warning disable IL2026 // Test intentionally serializes a schema model through XmlSerializer.
#pragma warning disable IL3050 // Test intentionally serializes a schema model through XmlSerializer.
            var serializer = new XmlSerializer(typeof(TypeDictionary));
            using var writer = new StringWriter();
            serializer.Serialize(writer, dictionary);
#pragma warning restore IL3050
#pragma warning restore IL2026
            return new MemoryStream(Encoding.UTF8.GetBytes(writer.ToString()));
        }
    }
}
