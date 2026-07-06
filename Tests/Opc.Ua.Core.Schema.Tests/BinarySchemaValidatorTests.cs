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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Opc.Ua.Schema.Binary;

namespace Opc.Ua.Schema.Tests
{
    /// <summary>
    /// Direct coverage tests for <see cref="BinarySchemaValidator"/> that feed
    /// self-contained OPC Binary schema documents through the stream overload.
    /// </summary>
    [TestFixture]
    [Category("Schema")]
    [SetCulture("en-us")]
    public class BinarySchemaValidatorTests
    {
        /// <summary>
        /// The target namespace of the self-contained schemas. Using the base OPC
        /// UA namespace disables the automatic built-in import so the documents can
        /// be validated fully offline.
        /// </summary>
        private const string TargetNamespace = "http://opcfoundation.org/UA/";

        /// <summary>
        /// A valid schema that defines an opaque, an enumerated and a structured
        /// type where the structure references the two other local types.
        /// </summary>
        private const string ValidSchema =
            """
            <opc:TypeDictionary
                xmlns:opc="http://opcfoundation.org/BinarySchema/"
                xmlns:tns="http://opcfoundation.org/UA/"
                TargetNamespace="http://opcfoundation.org/UA/">
              <opc:OpaqueType Name="CoverageOpaque" LengthInBits="32">
                <opc:Documentation>An opaque coverage type.</opc:Documentation>
              </opc:OpaqueType>
              <opc:EnumeratedType Name="CoverageEnum" LengthInBits="32">
                <opc:Documentation>A coverage enumeration.</opc:Documentation>
                <opc:EnumeratedValue Name="Red" Value="0" />
                <opc:EnumeratedValue Name="Green" Value="1" />
              </opc:EnumeratedType>
              <opc:StructuredType Name="CoverageStruct">
                <opc:Documentation>A coverage structure.</opc:Documentation>
                <opc:Field Name="Flags" TypeName="tns:CoverageOpaque" />
                <opc:Field Name="Shade" TypeName="tns:CoverageEnum" />
              </opc:StructuredType>
            </opc:TypeDictionary>
            """;

        /// <summary>
        /// A valid schema whose single opaque type is missing both a length and
        /// documentation, which the validator reports as non-fatal warnings.
        /// </summary>
        private const string LooseOpaqueSchema =
            """
            <opc:TypeDictionary
                xmlns:opc="http://opcfoundation.org/BinarySchema/"
                TargetNamespace="http://opcfoundation.org/UA/">
              <opc:OpaqueType Name="LooseOpaque" />
            </opc:TypeDictionary>
            """;

        /// <summary>
        /// An enumerated type without a length is rejected by the validator.
        /// </summary>
        private const string EnumWithoutLengthSchema =
            """
            <opc:TypeDictionary
                xmlns:opc="http://opcfoundation.org/BinarySchema/"
                TargetNamespace="http://opcfoundation.org/UA/">
              <opc:EnumeratedType Name="BadEnum">
                <opc:EnumeratedValue Name="A" Value="0" />
              </opc:EnumeratedType>
            </opc:TypeDictionary>
            """;

        /// <summary>
        /// The type names expected in the validated schema.
        /// </summary>
        private static readonly string[] ExpectedTypeNames =
            ["CoverageOpaque", "CoverageEnum", "CoverageStruct"];

        [Test]
        public void ValidateValidSchemaPopulatesDictionaryAndDescriptions()
        {
            var validator = new BinarySchemaValidator();
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ValidSchema));

            validator.Validate(stream);

            Assert.That(validator.Dictionary, Is.Not.Null);
            TypeDictionary dictionary = validator.Dictionary!;
            IList<TypeDescription> descriptions = validator.ValidatedDescriptions;
            Assert.Multiple(() =>
            {
                Assert.That(dictionary.TargetNamespace, Is.EqualTo(TargetNamespace));
                Assert.That(dictionary.Items, Has.Length.EqualTo(3));
                Assert.That(descriptions, Has.Count.EqualTo(3));
                Assert.That(descriptions.Select(d => d.Name),
                    Is.EquivalentTo(ExpectedTypeNames));
                Assert.That(descriptions.Any(d => d is StructuredType && d.Name == "CoverageStruct"), Is.True);
                Assert.That(descriptions.Any(d => d is EnumeratedType && d.Name == "CoverageEnum"), Is.True);
                Assert.That(descriptions.OfType<OpaqueType>().Any(d => d.Name == "CoverageOpaque"), Is.True);
            });
        }

        [Test]
        public void ValidateValidSchemaRecordsValidatedWarnings()
        {
            var validator = new BinarySchemaValidator();
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ValidSchema));

            validator.Validate(stream);

            Assert.That(validator.Warnings, Has.Count.EqualTo(3));
            Assert.That(validator.Warnings, Does.Contain("OpaqueType 'CoverageOpaque' validated."));
            Assert.That(validator.Warnings, Does.Contain("EnumeratedType 'CoverageEnum' validated."));
            Assert.That(validator.Warnings, Does.Contain("StructuredType 'CoverageStruct' validated."));
        }

        [Test]
        public void GetSchemaReturnsWholeDictionaryOrSingleType()
        {
            var validator = new BinarySchemaValidator();
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ValidSchema));
            validator.Validate(stream);

            string wholeSchema = validator.GetSchema(null);
            string structSchema = validator.GetSchema("CoverageStruct");

            Assert.Multiple(() =>
            {
                Assert.That(wholeSchema, Does.Contain("TypeDictionary"));
                Assert.That(wholeSchema, Does.Contain("CoverageOpaque"));
                Assert.That(wholeSchema, Does.Contain("CoverageEnum"));
                Assert.That(wholeSchema, Does.Contain("CoverageStruct"));
                Assert.That(structSchema, Does.Contain("CoverageStruct"));
                Assert.That(structSchema, Does.Contain("Flags"));
                Assert.That(structSchema, Does.Contain("Shade"));
            });
        }

        [Test]
        public void GetSchemaForUnknownTypeReturnsWholeDictionary()
        {
            var validator = new BinarySchemaValidator();
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ValidSchema));
            validator.Validate(stream);

            string schema = validator.GetSchema("DoesNotExist");

            Assert.That(schema, Does.Contain("CoverageOpaque"));
            Assert.That(schema, Does.Contain("CoverageStruct"));
        }

        [Test]
        public void ValidateSchemaWithLooseOpaqueTypeCollectsWarnings()
        {
            var validator = new BinarySchemaValidator();
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(LooseOpaqueSchema));

            validator.Validate(stream);

            Assert.That(validator.ValidatedDescriptions, Has.Count.EqualTo(1));
            Assert.That(validator.Warnings,
                Does.Contain("Warning: The opaque type 'LooseOpaque' does not have a length specified."));
            Assert.That(validator.Warnings,
                Does.Contain("Warning: The opaque type 'LooseOpaque' does not have any documentation."));
            Assert.That(validator.Warnings, Does.Contain("OpaqueType 'LooseOpaque' validated."));
        }

        [Test]
        public void ValidateEnumerationWithoutLengthThrowsInvalidOperationException()
        {
            var validator = new BinarySchemaValidator();
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(EnumWithoutLengthSchema));

            InvalidOperationException? exception =
                Assert.Throws<InvalidOperationException>(() => validator.Validate(stream));

            Assert.That(exception!.Message, Does.Contain("BadEnum"));
        }

        [Test]
        public void ValidateUsingImportTableConstructorValidatesSchema()
        {
            var validator = new BinarySchemaValidator(new Dictionary<string, byte[]>());
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ValidSchema));

            validator.Validate(stream);

            Assert.That(validator.Dictionary, Is.Not.Null);
            Assert.That(validator.ValidatedDescriptions, Has.Count.EqualTo(3));
        }
    }
}
