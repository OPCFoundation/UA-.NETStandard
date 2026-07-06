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

using System.IO;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.Schema.Xml;

namespace Opc.Ua.Schema.Tests
{
    /// <summary>
    /// Direct coverage tests for <see cref="XmlSchemaValidator"/>,
    /// <see cref="XmlSchemaValidator2"/> and the shared <see cref="SchemaValidator"/>
    /// base class using self-contained XML schemas.
    /// </summary>
    [TestFixture]
    [Category("Schema")]
    [SetCulture("en-us")]
    public class XmlSchemaValidatorCoverageTests
    {
        /// <summary>
        /// The target namespace of the valid schema.
        /// </summary>
        private const string CoverageNamespace = "http://test.org/UA/xsd/coverage";

        /// <summary>
        /// A valid schema with a named complex type and a global element.
        /// </summary>
        private const string ValidSchema =
            """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                targetNamespace="http://test.org/UA/xsd/coverage"
                xmlns:tns="http://test.org/UA/xsd/coverage"
                elementFormDefault="qualified">
              <xs:complexType name="SampleComplexType">
                <xs:sequence>
                  <xs:element name="Id" type="xs:int" />
                  <xs:element name="Name" type="xs:string" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="SampleElement" type="tns:SampleComplexType" />
            </xs:schema>
            """;

        /// <summary>
        /// A document that is not well formed and cannot be read as a schema.
        /// </summary>
        private const string MalformedSchema =
            """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:element name="Broken">
            </xs:schema>
            """;

        /// <summary>
        /// A well formed schema that references a type which is not declared.
        /// </summary>
        private const string UndefinedTypeSchema =
            """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                targetNamespace="http://test.org/UA/xsd/invalid"
                xmlns:tns="http://test.org/UA/xsd/invalid"
                elementFormDefault="qualified">
              <xs:element name="Broken" type="tns:MissingType" />
            </xs:schema>
            """;

        [Test]
        public void XmlSchemaValidatorValidatesSchemaAndExposesSchemaSet()
        {
            var validator = new XmlSchemaValidator();
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ValidSchema));

            validator.Validate(stream, NullLogger.Instance);

            Assert.That(validator.SchemaSet, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(validator.TargetSchema, Is.Not.Null);
                Assert.That(validator.TargetSchema!.TargetNamespace, Is.EqualTo(CoverageNamespace));
                Assert.That(validator.SchemaSet!.IsCompiled, Is.True);
                Assert.That(validator.SchemaSet!.Contains(CoverageNamespace), Is.True);
            });
        }

        [Test]
        public void XmlSchemaValidatorGetSchemaReturnsWholeSchemaOrSingleElement()
        {
            var validator = new XmlSchemaValidator();
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ValidSchema));
            validator.Validate(stream, NullLogger.Instance);

            string whole = validator.GetSchema(null);
            string element = validator.GetSchema("SampleElement");

            Assert.Multiple(() =>
            {
                Assert.That(whole, Does.Contain("SampleComplexType"));
                Assert.That(whole, Does.Contain("SampleElement"));
                Assert.That(whole, Does.Contain(CoverageNamespace));
                Assert.That(element, Does.Contain("SampleElement"));
            });
        }

        [Test]
        public void XmlSchemaValidatorThrowsFileNotFoundForMalformedSchema()
        {
            var validator = new XmlSchemaValidator();
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(MalformedSchema));

            Assert.Throws<FileNotFoundException>(() => validator.Validate(stream, NullLogger.Instance));
        }

        [Test]
        public void XmlSchemaValidatorThrowsSchemaExceptionForUndefinedType()
        {
            var validator = new XmlSchemaValidator();
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(UndefinedTypeSchema));

            Assert.Throws<System.Xml.Schema.XmlSchemaException>(
                () => validator.Validate(stream, NullLogger.Instance));
        }

        [Test]
        public void XmlSchemaValidator2ValidatesSchemaAndExposesSchemaSet()
        {
            var validator = new XmlSchemaValidator2(LocalFileSystem.Instance);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ValidSchema));

            validator.Validate(stream);

            Assert.That(validator.SchemaSet, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(validator.TargetSchema, Is.Not.Null);
                Assert.That(validator.TargetSchema!.TargetNamespace, Is.EqualTo(CoverageNamespace));
                Assert.That(validator.SchemaSet!.IsCompiled, Is.True);
                Assert.That(validator.SchemaSet!.Contains(CoverageNamespace), Is.True);
            });
        }

        [Test]
        public void XmlSchemaValidator2GetSchemaReturnsWholeSchemaOrSingleElement()
        {
            var validator = new XmlSchemaValidator2(LocalFileSystem.Instance);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ValidSchema));
            validator.Validate(stream);

            string whole = validator.GetSchema(null);
            string element = validator.GetSchema("SampleElement");

            Assert.Multiple(() =>
            {
                Assert.That(whole, Does.Contain("SampleComplexType"));
                Assert.That(whole, Does.Contain("SampleElement"));
                Assert.That(element, Does.Contain("SampleElement"));
            });
        }

        [Test]
        public void XmlSchemaValidator2ThrowsForMalformedSchema()
        {
            var validator = new XmlSchemaValidator2(LocalFileSystem.Instance);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(MalformedSchema));

            Assert.Throws<FileNotFoundException>(() => validator.Validate(stream));
        }

        [Test]
        public void XmlSchemaValidator2ThrowsForUndefinedType()
        {
            var validator = new XmlSchemaValidator2(LocalFileSystem.Instance);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(UndefinedTypeSchema));

            Assert.Throws<System.Xml.Schema.XmlSchemaException>(() => validator.Validate(stream));
        }

        [Test]
        public void XmlSchemaValidator2WellKnownContainsStandardMappings()
        {
            Assert.That(XmlSchemaValidator2.WellKnown, Has.Count.EqualTo(3));
            Assert.Multiple(() =>
            {
                Assert.That(
                    XmlSchemaValidator2.WellKnown["http://opcfoundation.org/UA/2008/02/Types.xsd"],
                    Is.EqualTo("Opc.Ua.Types.xsd"));
                Assert.That(
                    XmlSchemaValidator2.WellKnown["http://opcfoundation.org/UA/"],
                    Is.EqualTo("Opc.Ua.Types.xsd"));
                Assert.That(
                    XmlSchemaValidator2.WellKnown["http://opcfoundation.org/UA/BuiltInTypes/"],
                    Is.EqualTo("BuiltInTypes.xsd"));
            });
        }

        [Test]
        public void SchemaValidatorBaseReturnsNullSchemaAndFilePath()
        {
            var validator = new SchemaValidator(null);

            Assert.Multiple(() =>
            {
                Assert.That(validator.GetSchema(null), Is.Null);
                Assert.That(validator.GetSchema("AnyType"), Is.Null);
                Assert.That(validator.FilePath, Is.Null);
            });
        }
    }
}
