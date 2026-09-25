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
    /// A3-6: the generated XML schema describes structure fields the way
    /// XmlEncoder writes them: a multi-dimensional field as an inline matrix
    /// (Dimensions and Elements), a ScalarOrArray / Any field as a Variant.
    /// The XML body of every generated value validates against the schema.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class XmlSchemaMatrixFieldTests
    {
        private const string ModelUri = "http://test.org/UA/XM/";
        private const string XmlUri = "http://test.org/UA/XM/Types.xsd";

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
                new XmlQualifiedName("Matrix", "http://opcfoundation.org/UA/2008/02/Types.xsd")];
            var sequence = (XmlSchemaSequence)matrix.ContentTypeParticle;
            Assert.That(
                sequence.Items.OfType<XmlSchemaElement>().Select(e => e.QualifiedName.Name),
                Is.EqualTo(s_matrixChildren));
        }

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
                Set(grids, "Doubles", MatrixOf.From<double>(new double[,] { { 1.5, 2.5 }, { 3.5, 4.5 } }));
                Set(grids, "Values", MatrixOf.From<Variant>(new Variant[,]
                {
                    { Variant.From(1) },
                    { Variant.From("x") }
                }));
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

            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            context.NamespaceUris.Append(ModelUri);
            context.Factory.Builder.AddEncodeableTypes(m_assembly).Commit();
            string xml;
            using (var encoder = new XmlEncoder(context))
            {
                encoder.PushNamespace(XmlUri);
                encoder.WriteEncodeable("Grids", grids, grids.TypeId);
                encoder.PopNamespace();
                xml = encoder.CloseAndReturnText();
            }

            var errors = new List<string>();
            var settings = new XmlReaderSettings
            {
                ValidationType = ValidationType.Schema,
                ValidationFlags = XmlSchemaValidationFlags.ReportValidationWarnings,
                Schemas = CreateSchemaSet()
            };
            settings.ValidationEventHandler += (_, e) =>
            {
                // The Dimensions / Elements of a structure matrix are
                // validated laxly (xs:any, no global declaration).
                if (e.Severity == XmlSeverityType.Warning &&
                    s_matrixChildren.Any(c => e.Message.Contains(
                        "Types.xsd:" + c + "'",
                        StringComparison.Ordinal)))
                {
                    return;
                }
                errors.Add(e.Severity + ": " + e.Message);
            };
            using (var reader = XmlReader.Create(new StringReader(xml), settings))
            {
                while (reader.Read())
                {
                }
            }
            Assert.That(errors, Is.Empty, xml);
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
              <opc:DataType SymbolicName="Grids" BaseType="ua:Structure">
                <opc:Fields>
                  <opc:Field Name="Before" DataType="ua:Int32" />
                  <opc:Field Name="Doubles" DataType="ua:Double" ValueRank="OneOrMoreDimensions" ArrayDimensions="0,0" />
                  <opc:Field Name="Values" DataType="ua:BaseDataType" ValueRank="OneOrMoreDimensions" ArrayDimensions="0,0" />
                  <opc:Field Name="Cells" DataType="Cell" ValueRank="OneOrMoreDimensions" ArrayDimensions="0,0" />
                  <opc:Field Name="Loose" DataType="ua:Int32" ValueRank="ScalarOrArray" />
                  <opc:Field Name="After" DataType="ua:Int32" />
                </opc:Fields>
              </opc:DataType>
            </opc:ModelDesign>
            """;

        private static readonly string[] s_matrixChildren = ["Dimensions", "Elements"];
        private static readonly int[] s_oneTwo = [1, 2];
        private Assembly m_assembly;
        private string m_modelSchema;
        private string m_uaSchema;
    }
}
