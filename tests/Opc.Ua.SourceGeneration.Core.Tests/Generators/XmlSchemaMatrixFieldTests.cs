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
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Schema;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// A3-6 / D6: the generated XML schema describes structure fields as
    /// OPC 10000-6 5.3 specifies them: a multi-dimensional field is of the
    /// Matrix type (5.3.4, 5.3.1.17: Dimensions and Elements directly inside
    /// the field element, for every element type), a ScalarOrArray / Any
    /// field a Variant, and a structure with optional fields starts with a
    /// mandatory EncodingMask (5.3.6).
    /// </summary>
    /// <remarks>
    /// The XmlEncoder is moved to the unwrapped matrix shape separately, so
    /// the conformant shape is checked with hand-written XML; live encoder
    /// output is only validated for the fields whose shape does not depend
    /// on that change.
    /// </remarks>
    [TestFixture]
    [Category("Generator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class XmlSchemaMatrixFieldTests
    {
        private const string ModelUri = "http://test.org/UA/XM/";
        private const string XmlUri = "http://test.org/UA/XM/Types.xsd";
        private const string UaXmlUri = "http://opcfoundation.org/UA/2008/02/Types.xsd";

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            Dictionary<string, string> generated = DataTypeModelTests.Generate(Model);
            m_assembly = DataTypeModelTests.Compile(generated);
            m_modelSchema = generated
                .Where(f => f.Key.EndsWith(".xsd", StringComparison.Ordinal))
                .Select(f => f.Value)
                .Single();
            Api.Tests.GenerateStackTests.GenerateStack(
                StackGenerationType.Models,
                NUnitTelemetryContext.Create(logLevel: LogLevel.Error),
                out Dictionary<string, string> stackOther);
            m_uaSchema = stackOther
                .Where(f => f.Key.EndsWith(".xsd", StringComparison.Ordinal))
                .Select(f => f.Value)
                .Single();
        }

        [Test]
        public void BuiltInMatrixNamesTheValuesElements()
        {
            XmlSchemaSet schemas = CreateSchemaSet();
            var matrix = (XmlSchemaComplexType)schemas.GlobalTypes[
                new XmlQualifiedName("Matrix", UaXmlUri)];
            var sequence = (XmlSchemaSequence)matrix.ContentTypeParticle;
            Assert.That(
                sequence.Items.OfType<XmlSchemaElement>().Select(e => e.QualifiedName.Name),
                Is.EqualTo(s_matrixChildren));
        }

        /// <summary>
        /// OPC 10000-6 5.3.4: every matrix field - built-in, Variant,
        /// enumeration, concrete and subtyped structure - is typed as the
        /// Matrix type itself, not as a wrapper around a Matrix element.
        /// </summary>
        [Test]
        public void MatrixFieldsAreOfTheMatrixType()
        {
            XmlSchemaSet schemas = CreateSchemaSet();
            var grids = (XmlSchemaComplexType)schemas.GlobalTypes[new XmlQualifiedName("Grids", XmlUri)];
            Dictionary<string, XmlSchemaElement> fields = ((XmlSchemaSequence)grids.ContentTypeParticle)
                .Items.OfType<XmlSchemaElement>()
                .ToDictionary(e => e.QualifiedName.Name);
            foreach (string name in s_matrixFields)
            {
                Assert.That(
                    fields[name].SchemaTypeName,
                    Is.EqualTo(new XmlQualifiedName("Matrix", UaXmlUri)),
                    name);
                Assert.That(fields[name].IsNillable, Is.True, name);
                Assert.That(fields[name].MinOccurs, Is.Zero, name);
            }
        }

        [Test]
        public void ConformantMatrixXmlValidates()
        {
            Assert.That(Validate(ConformantGrids), Is.Empty);
        }

        /// <summary>
        /// The Variant style wrapper (a Matrix element inside the field) is
        /// not the 5.3.4 shape and does not validate.
        /// </summary>
        [Test]
        public void WrappedMatrixXmlIsRejected()
        {
            string wrapped =
                $"""
                <Grids xmlns="{XmlUri}" xmlns:ua="{UaXmlUri}">
                  <Doubles>
                    <ua:Matrix>
                      <ua:Dimensions><ua:Int32>1</ua:Int32><ua:Int32>1</ua:Int32></ua:Dimensions>
                      <ua:Elements><ua:Double>1</ua:Double></ua:Elements>
                    </ua:Matrix>
                  </Doubles>
                </Grids>
                """;
            Assert.That(Validate(wrapped), Is.Not.Empty);
        }

        /// <summary>
        /// OPC 10000-6 5.3.6: the EncodingMask is the mandatory first element
        /// of a structure with optional fields.
        /// </summary>
        [Test]
        public void EncodingMaskIsRequired()
        {
            Assert.That(
                Validate($"<Opt xmlns=\"{XmlUri}\"><EncodingMask>1</EncodingMask><A>1</A><B>2</B></Opt>"),
                Is.Empty);
            Assert.That(
                Validate($"<Opt xmlns=\"{XmlUri}\"><EncodingMask>4294967295</EncodingMask></Opt>"),
                Is.Empty);
            Assert.That(Validate($"<Opt xmlns=\"{XmlUri}\"><A>1</A></Opt>"), Is.Not.Empty);
        }

        /// <summary>
        /// Live XmlEncoder output validates for the fields whose XML shape is
        /// settled: null matrices (xsi:nil), a structure matrix, a Variant
        /// field and a structure with optional fields.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void EncodedXmlValidatesAgainstGeneratedSchema(bool populated)
        {
            Type gridsType = m_assembly.GetType("Test.XM.Grids", throwOnError: true);
            Type cellType = m_assembly.GetType("Test.XM.Cell", throwOnError: true);
            var grids = (IEncodeable)Activator.CreateInstance(gridsType);
            Set(grids, "Before", 7);
            if (populated)
            {
                Array cells = Array.CreateInstance(cellType, 1, 2);
                cells.SetValue(Cell(cellType, 1), 0, 0);
                cells.SetValue(Cell(cellType, 2), 0, 1);
                Set(grids, "Cells", typeof(MatrixOf)
                    .GetMethod(nameof(MatrixOf.From), BindingFlags.Public | BindingFlags.Static)
                    .MakeGenericMethod(cellType)
                    .Invoke(null, [cells]));
                Set(grids, "Loose", Variant.From(s_oneTwo.ToArrayOf()));
            }
            Set(grids, "After", 9);
            var opt = (IEncodeable)Activator.CreateInstance(m_assembly.GetType("Test.XM.Opt", throwOnError: true));
            if (populated)
            {
                Set(opt, "B", 5);
            }

            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            context.NamespaceUris.Append(ModelUri);
            context.Factory.Builder.AddEncodeableTypes(m_assembly).Commit();
            foreach ((string name, IEncodeable value) in new[] { ("Grids", grids), ("Opt", opt) })
            {
                string xml;
                using (var encoder = new XmlEncoder(context))
                {
                    encoder.PushNamespace(XmlUri);
                    encoder.WriteEncodeable(name, value, value.TypeId);
                    encoder.PopNamespace();
                    xml = encoder.CloseAndReturnText();
                }
                Assert.That(Validate(xml), Is.Empty, xml);
            }
        }

        private List<string> Validate(string xml)
        {
            var errors = new List<string>();
            var settings = new XmlReaderSettings
            {
                ValidationType = ValidationType.Schema,
                ValidationFlags = XmlSchemaValidationFlags.ReportValidationWarnings,
                Schemas = CreateSchemaSet()
            };
            settings.ValidationEventHandler += (_, e) => errors.Add(e.Severity + ": " + e.Message);
            using (var reader = XmlReader.Create(new StringReader(xml), settings))
            {
                while (reader.Read())
                {
                }
            }
            return errors;
        }

        private XmlSchemaSet CreateSchemaSet()
        {
            var schemas = new XmlSchemaSet { XmlResolver = null };
            schemas.Add(Load(m_uaSchema));
            schemas.Add(Load(m_modelSchema));
            schemas.Compile();
            return schemas;

            static XmlSchema Load(string text)
            {
                using var reader = XmlReader.Create(new StringReader(text.TrimStart((char)0xFEFF)));
                return XmlSchema.Read(reader, null);
            }
        }

        private static IEncodeable Cell(Type cellType, int value)
        {
            var cell = (IEncodeable)Activator.CreateInstance(cellType);
            Set(cell, "V", value);
            return cell;
        }

        private static void Set(object instance, string name, object value)
        {
            instance.GetType().GetProperty(name).SetValue(instance, value);
        }

        private const string ConformantGrids =
            $"""
            <Grids xmlns="{XmlUri}" xmlns:ua="{UaXmlUri}">
              <Before>7</Before>
              <Doubles>
                <ua:Dimensions><ua:Int32>2</ua:Int32><ua:Int32>2</ua:Int32></ua:Dimensions>
                <ua:Elements>
                  <ua:Double>1.5</ua:Double><ua:Double>2.5</ua:Double><ua:Double>3.5</ua:Double><ua:Double>4.5</ua:Double>
                </ua:Elements>
              </Doubles>
              <Values>
                <ua:Dimensions><ua:Int32>2</ua:Int32><ua:Int32>1</ua:Int32></ua:Dimensions>
                <ua:Elements>
                  <ua:Variant><ua:Value><ua:Int32>1</ua:Int32></ua:Value></ua:Variant>
                  <ua:Variant><ua:Value><ua:String>x</ua:String></ua:Value></ua:Variant>
                </ua:Elements>
              </Values>
              <Cells>
                <ua:Dimensions><ua:Int32>1</ua:Int32><ua:Int32>2</ua:Int32></ua:Dimensions>
                <ua:Elements>
                  <Cell><V>1</V></Cell>
                  <Cell><V>2</V></Cell>
                </ua:Elements>
              </Cells>
              <Shades>
                <ua:Dimensions><ua:Int32>1</ua:Int32><ua:Int32>2</ua:Int32></ua:Dimensions>
                <ua:Elements><ua:Int32>0</ua:Int32><ua:Int32>1</ua:Int32></ua:Elements>
              </Shades>
              <Shapes>
                <ua:Dimensions><ua:Int32>1</ua:Int32><ua:Int32>1</ua:Int32></ua:Dimensions>
                <ua:Elements>
                  <ua:ExtensionObject>
                    <ua:TypeId><ua:Identifier>ns=1;i=1</ua:Identifier></ua:TypeId>
                    <ua:Body><Cell><V>3</V></Cell></ua:Body>
                  </ua:ExtensionObject>
                </ua:Elements>
              </Shapes>
              <Loose><ua:Value><ua:ListOfInt32><ua:Int32>1</ua:Int32></ua:ListOfInt32></ua:Value></Loose>
              <After>9</After>
            </Grids>
            """;

        private const string Model =
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <opc:ModelDesign
              xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
              xmlns:ua="http://opcfoundation.org/UA/"
              xmlns="http://test.org/UA/XM/"
              TargetNamespace="http://test.org/UA/XM/">
              <opc:Namespaces>
                <opc:Namespace Name="XM" Prefix="Test.XM" XmlNamespace="http://test.org/UA/XM/Types.xsd">http://test.org/UA/XM/</opc:Namespace>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua" XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd">http://opcfoundation.org/UA/</opc:Namespace>
              </opc:Namespaces>
              <opc:DataType SymbolicName="Cell" BaseType="ua:Structure">
                <opc:Fields>
                  <opc:Field Name="V" DataType="ua:Int32" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="Shape" BaseType="ua:Structure" IsAbstract="true">
                <opc:Fields>
                  <opc:Field Name="Id" DataType="ua:Int32" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="Shade" BaseType="ua:Enumeration">
                <opc:Fields>
                  <opc:Field Name="Light" Identifier="0" />
                  <opc:Field Name="Dark" Identifier="1" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="Grids" BaseType="ua:Structure">
                <opc:Fields>
                  <opc:Field Name="Before" DataType="ua:Int32" />
                  <opc:Field Name="Doubles" DataType="ua:Double" ValueRank="OneOrMoreDimensions" ArrayDimensions="0,0" />
                  <opc:Field Name="Values" DataType="ua:BaseDataType" ValueRank="OneOrMoreDimensions" ArrayDimensions="0,0" />
                  <opc:Field Name="Cells" DataType="Cell" ValueRank="OneOrMoreDimensions" ArrayDimensions="0,0" />
                  <opc:Field Name="Shades" DataType="Shade" ValueRank="OneOrMoreDimensions" ArrayDimensions="0,0" />
                  <opc:Field Name="Shapes" DataType="Shape" AllowSubTypes="true" ValueRank="OneOrMoreDimensions" ArrayDimensions="0,0" />
                  <opc:Field Name="Loose" DataType="ua:Int32" ValueRank="ScalarOrArray" />
                  <opc:Field Name="After" DataType="ua:Int32" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="Opt" BaseType="ua:Structure">
                <opc:Fields>
                  <opc:Field Name="A" DataType="ua:Int32" />
                  <opc:Field Name="B" DataType="ua:Int32" IsOptional="true" />
                </opc:Fields>
              </opc:DataType>
            </opc:ModelDesign>
            """;

        private static readonly string[] s_matrixChildren = ["Dimensions", "Elements"];
        private static readonly string[] s_matrixFields = ["Doubles", "Values", "Cells", "Shades", "Shapes"];
        private static readonly int[] s_oneTwo = [1, 2];
        private Assembly m_assembly;
        private string m_modelSchema;
        private string m_uaSchema;
    }
}
