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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using NUnit.Framework;
using Opc.Ua.Schema.Types;
using UaComplexType = Opc.Ua.Schema.Types.ComplexType;
using UaDataType = Opc.Ua.Schema.Types.DataType;
using UaFieldType = Opc.Ua.Schema.Types.FieldType;
using UaTypeDictionary = Opc.Ua.Schema.Types.TypeDictionary;

namespace Opc.Ua.Types.Tests.Schema
{
    [TestFixture]
    [Category("Schema")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class TypeDictionaryValidatorTests
    {
        private const string TestNamespace = "urn:type-dictionary-test";

        [Test]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Tests exercise design-time schema validation.")]
        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Tests exercise design-time schema validation.")]
        public void ValidateResolvesDeclarationsComplexTypesServicesAndEnums()
        {
            var integer = new UaDataType { Name = "UInt32", Category = "Numeric" };
            var alias = new TypeDeclaration { Name = "Counter", SourceType = QName("UInt32") };
            var baseStruct = new UaComplexType
            {
                Name = "BaseStruct",
                Field = [new UaFieldType { Name = "Id", DataType = QName("UInt32") }]
            };
            var childStruct = new UaComplexType
            {
                Name = "ChildStruct",
                BaseType = QName("BaseStruct"),
                Field =
                [
                    new UaFieldType { Name = "Count", DataType = QName("Counter") },
                    new UaFieldType
                    {
                        Name = "Nested",
                        ComplexType = new UaComplexType
                        {
                            Name = "NestedStruct",
                            Field = [new UaFieldType { Name = "NestedId", DataType = QName("UInt32") }]
                        }
                    }
                ]
            };
            var mode = new EnumeratedType
            {
                Name = "Mode",
                Value =
                [
                    new EnumeratedValue { Name = "Off" },
                    new EnumeratedValue { Name = "On", Value = 4, ValueSpecified = true }
                ]
            };
            var service = new ServiceType
            {
                Name = "ReadSomething",
                Request = [new UaFieldType { Name = "RequestId", DataType = QName("UInt32") }],
                Response = [new UaFieldType { Name = "Result", DataType = QName("ChildStruct") }]
            };
            var validator = new TypeDictionaryValidator(NullFileSystem.Instance);

            validator.Validate(ToStream(CreateDictionary(integer, alias, baseStruct, childStruct, mode, service)));

            Assert.That(validator.Dictionary, Is.Not.Null);
            UaDataType resolvedInteger = validator.FindType(QName("UInt32"));
            var resolvedMode = (EnumeratedType)validator.FindType(QName("Mode"));
            var resolvedChild = (UaComplexType)validator.FindType(QName("ChildStruct"));

            Assert.That(resolvedInteger.Name, Is.EqualTo("UInt32"));
            Assert.That(validator.ResolveType(QName("Counter")), Is.SameAs(resolvedInteger));
            Assert.That(validator.ResolveType(XmlQualifiedName.Empty), Is.Null);
            Assert.That(validator.ResolveType(QName("Missing")), Is.Null);
            Assert.That(resolvedMode.Value[0].Value, Is.Zero);
            Assert.That(resolvedMode.Value[0].ValueSpecified, Is.True);
            Assert.That(resolvedMode.Value[1].Value, Is.EqualTo(4));
            Assert.That(resolvedChild.Field[1].DataType, Is.EqualTo(QName("NestedStruct")));
            Assert.That(validator.LoadedTypeDictionaries, Is.Empty);
            Assert.That(TypeDictionaryValidator.IsExcluded(["Numeric"], resolvedInteger), Is.True);
            Assert.That(TypeDictionaryValidator.IsExcluded([nameof(ReleaseStatus.Released)], resolvedMode.Value[0]), Is.True);
            Assert.That(TypeDictionaryValidator.IsExcluded([nameof(ReleaseStatus.Released)], resolvedChild.Field[0]), Is.True);
            Assert.That(TypeDictionaryValidator.IsExcluded(null, resolvedInteger), Is.False);
        }

        [Test]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Tests exercise design-time schema validation.")]
        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Tests exercise design-time schema validation.")]
        public void ValidateRejectsInvalidDataTypesAndDeclarations()
        {
            AssertValidationFails(
                CreateDictionary(new UaDataType { Name = "1Invalid" }),
                "not a valid datatype name");
            AssertValidationFails(
                CreateDictionary(new UaDataType { Name = "Value" }, new UaDataType { Name = "Value" }),
                "already used by another datatype");
            AssertValidationFails(
                CreateDictionary(new TypeDeclaration { Name = "Alias" }),
                "does not have a source type");
            AssertValidationFails(
                CreateDictionary(new TypeDeclaration { Name = "Alias", SourceType = QName("Missing") }),
                "Cannot find a concrete source type");
            AssertValidationFails(
                CreateDictionary(
                    new UaDataType { Name = "UInt32" },
                    new UaComplexType { Name = "Child", BaseType = QName("UInt32") }),
                "is not a complex type");
        }

        [Test]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Tests exercise design-time schema validation.")]
        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Tests exercise design-time schema validation.")]
        public void ValidateRejectsInvalidFieldsAndEnums()
        {
            AssertValidationFails(
                CreateDictionary(new EnumeratedType { Name = "Mode" }),
                "does not have any values specified");
            AssertValidationFails(
                CreateDictionary(new EnumeratedType
                {
                    Name = "Mode",
                    Value = [new EnumeratedValue { Name = "A" }, new EnumeratedValue { Name = "A" }]
                }),
                "has a duplicate value");
            AssertValidationFails(
                CreateDictionary(new UaComplexType
                {
                    Name = "NoType",
                    Field = [new UaFieldType { Name = "Value" }]
                }),
                "has no data type");
            AssertValidationFails(
                CreateDictionary(new UaDataType { Name = "UInt32" }, new UaComplexType
                {
                    Name = "Ambiguous",
                    Field =
                    [
                        new UaFieldType
                        {
                            Name = "Value",
                            DataType = QName("UInt32"),
                            ComplexType = new UaComplexType { Name = "Nested" }
                        }
                    ]
                }),
                "ambiguous data type");
            AssertValidationFails(
                CreateDictionary(new UaComplexType
                {
                    Name = "Unknown",
                    Field = [new UaFieldType { Name = "Value", DataType = QName("Missing") }]
                }),
                "unrecognized data type");
            AssertValidationFails(
                CreateDictionary(new UaDataType { Name = "UInt32" }, new UaComplexType
                {
                    Name = "DuplicateFields",
                    Field =
                    [
                        new UaFieldType { Name = "Value", DataType = QName("UInt32") },
                        new UaFieldType { Name = "Value", DataType = QName("UInt32") }
                    ]
                }),
                "already exists");
        }

        private static void AssertValidationFails(UaTypeDictionary dictionary, string expectedMessage)
        {
            var validator = new TypeDictionaryValidator(NullFileSystem.Instance);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                validator.Validate(ToStream(dictionary)));

            Assert.That(exception.Message, Does.Contain(expectedMessage));
        }

        private static UaTypeDictionary CreateDictionary(params UaDataType[] dataTypes)
        {
            return new UaTypeDictionary
            {
                TargetNamespace = TestNamespace,
                Items = dataTypes
            };
        }

        private static XmlQualifiedName QName(string name)
        {
            return new XmlQualifiedName(name, TestNamespace);
        }

        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Tests exercise design-time schema validation.")]
        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Tests exercise design-time schema validation.")]
        private static MemoryStream ToStream(UaTypeDictionary dictionary)
        {
            var serializer = new XmlSerializer(typeof(UaTypeDictionary));
            using var writer = new StringWriter();
            serializer.Serialize(writer, dictionary);
            return new MemoryStream(Encoding.UTF8.GetBytes(writer.ToString()));
        }
    }
}
