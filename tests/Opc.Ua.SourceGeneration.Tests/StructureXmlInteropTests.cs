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
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.Encoders;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// The DataTypeDefinition driven <see cref="Structure"/> codec writes a
    /// structure field in XML exactly like the generated class does (OPC
    /// 10000-6 5.3.1, 5.3.4, 5.3.5): a scalar as <c>&lt;A&gt;1&lt;/A&gt;</c>
    /// and an array as <c>&lt;A&gt;&lt;Int32&gt;1&lt;/Int32&gt;...&lt;/A&gt;</c>,
    /// not wrapped as a Variant body.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class StructureXmlInteropTests
    {
        private const string ModelUri = "http://test.org/UA/XI/";
        private const string XmlNamespace = "http://test.org/UA/XI/Types.xsd";

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_assembly = GenerateAndLoad();
            m_namespaceUris = new NamespaceTable();
            m_namespaceUris.Append(ModelUri);
            m_colorType = m_assembly.GetType("Test.XI.Color", throwOnError: true)!;
        }

        [Test]
        public void StructureWritesFieldsLikeTheGeneratedClass([Values] bool useParser)
        {
            Structure structure = CreateStructure("Rec", StructureType.Structure);
            structure["A"] = Variant.From(7);
            structure["S"] = Variant.From("text");
            structure["Arr"] = Variant.From(s_ints.ToArrayOf());
            structure["Mx"] = Variant.From(new[,] { { 1.0, 2.0 }, { 3.0, 4.0 } }.ToMatrixOf());
            structure["C"] = Variant.From(new EnumValue(1, m_colorType));
            structure["Cs"] = Variant.From(new[]
            {
                new EnumValue(2, m_colorType),
                new EnumValue(1, m_colorType)
            }.ToArrayOf());
            structure["N"] = Variant.From(new NodeId(85u));
            structure["L"] = Variant.From(new LocalizedText("en", "hello"));
            structure["G"] = Variant.From(new Uuid(new Guid("12345678-1234-1234-1234-123456789abc")));
            structure["Names"] = Variant.From(s_names.ToArrayOf());
            structure["Empty"] = Variant.From(ArrayOf.Empty<int>());

            string xml = AssertSameXml(structure, useParser);

            Assert.That(xml, Does.Contain("<A>7</A>"));
            Assert.That(xml, Does.Contain("<S>text</S>"));
            Assert.That(xml, Does.Contain("<C>Green_1</C>"));
            Assert.That(xml, Does.Not.Contain("ListOf"));
            Assert.That(xml, Does.Not.Contain(":Int32>7<"));
        }

        [Test]
        public void StructureWithOptionalFieldsWritesFieldsLikeTheGeneratedClass([Values] bool useParser)
        {
            Structure structure = CreateStructure("Opt", StructureType.StructureWithOptionalFields);
            structure["Req"] = Variant.From(3);
            structure["O1"] = Variant.From("set");
            structure["O3"] = Variant.From(s_doubles.ToArrayOf());

            string xml = AssertSameXml(structure, useParser);

            Assert.That(xml, Does.Contain("<Req>3</Req>"));
            Assert.That(xml, Does.Not.Contain("O2"));
        }

        [Test]
        public void UnionWritesFieldsLikeTheGeneratedClass([Values] bool useParser)
        {
            Structure union = CreateStructure("Uni", StructureType.Union);
            union["AsArr"] = Variant.From(s_doubles.ToArrayOf());

            string xml = AssertSameXml(union, useParser);

            Assert.That(xml, Does.Contain("<SwitchField>2</SwitchField>"));
            Assert.That(xml, Does.Not.Contain("ListOf"));
        }

        /// <summary>
        /// The Structure and the generated class write the same XML in both
        /// directions, and XmlDecoder and XmlParser read it back.
        /// </summary>
        private string AssertSameXml(Structure structure, bool useParser)
        {
            ServiceMessageContext runtime = CreateContext();
            // The generated enumeration, and the Structure in place of the
            // generated structure.
            var colorActivator = (IEnumeratedType)m_assembly
                .GetType("Test.XI.ColorActivator", throwOnError: true)!
                .GetField("Instance", BindingFlags.Public | BindingFlags.Static)!
                .GetValue(null)!;
            StructureField colorField = ((StructureDefinition)structure
                .GetDataTypeDefinition(m_namespaceUris)).Fields.ToArray()!
                .FirstOrDefault(f => f.DataType.NamespaceIndex != 0)!;
            IEncodeableFactoryBuilder builder = runtime.Factory.Builder
                .AddEncodeableTypes(m_assembly)
                .AddEncodeableType(structure);
            if (colorField != null)
            {
                builder = builder.AddEnumeratedType(
                    NodeId.ToExpandedNodeId(colorField.DataType, m_namespaceUris),
                    colorActivator!);
            }
            builder.Commit();
            ServiceMessageContext generated = CreateContext();
            generated.Factory.Builder.AddEncodeableTypes(m_assembly).Commit();

            string structureXml = EncodeXml(runtime, structure);

            // The generated class reads the XML and writes the same text.
            IEncodeable typed = DecodeXml(generated, structureXml, structure.TypeId, useParser);
            Assert.That(typed, Is.Not.InstanceOf<Structure>());
            string typedXml = EncodeXml(generated, typed);
            Assert.That(structureXml, Is.EqualTo(typedXml));

            // And the Structure reads the XML of the generated class.
            IEncodeable back = DecodeXml(runtime, typedXml, structure.TypeId, useParser);
            Assert.That(back, Is.InstanceOf<Structure>());
            Assert.That(EncodeXml(runtime, back), Is.EqualTo(typedXml));
            Assert.That(EncodeBinary(runtime, back), Is.EqualTo(EncodeBinary(runtime, structure)));
            return structureXml;
        }

        private Structure CreateStructure(string name, StructureType structureType)
        {
            Type type = m_assembly.GetType("Test.XI." + name, throwOnError: true)!;
            var instance = (IEncodeable)Activator.CreateInstance(type!)!;
            MethodInfo create = m_assembly.GetTypes()
                .Select(t => t.GetMethod("Create" + name, BindingFlags.Public | BindingFlags.Static))
                .First(m => m != null)!;
            var definition = (StructureDefinition)create!.Invoke(null, [m_namespaceUris])!;
            Dictionary<string, BuiltInType> fieldTypes = definition!.Fields.ToArray()!.ToDictionary(
                f => f.Name!,
                f => f.DataType.NamespaceIndex == 0 && f.DataType.TryGetValue(out uint id)
                    ? (BuiltInType)id
                    : BuiltInType.Enumeration);
            var xmlName = new XmlQualifiedName(name, XmlNamespace);
            return structureType switch
            {
                StructureType.Union => new Union(
                    xmlName,
                    instance!.TypeId,
                    instance.BinaryEncodingId,
                    instance.XmlEncodingId,
                    definition,
                    fieldTypes),
                StructureType.StructureWithOptionalFields => new StructureWithOptionalFields(
                    xmlName,
                    instance!.TypeId,
                    instance.BinaryEncodingId,
                    instance.XmlEncodingId,
                    definition,
                    fieldTypes),
                _ => new Structure(
                    xmlName,
                    instance!.TypeId,
                    instance.BinaryEncodingId,
                    instance.XmlEncodingId,
                    definition,
                    fieldTypes)
            };
        }

        private ServiceMessageContext CreateContext()
        {
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(new TestTelemetry());
            context.NamespaceUris = m_namespaceUris;
            return context;
        }

        private static string EncodeXml(ServiceMessageContext context, IEncodeable value)
        {
            using var encoder = new XmlEncoder(context);
            encoder.PushNamespace(XmlNamespace);
            encoder.WriteEncodeable("Value", value, value.TypeId);
            encoder.PopNamespace();
            return encoder.CloseAndReturnText()!;
        }

        private static byte[] EncodeBinary(ServiceMessageContext context, IEncodeable value)
        {
            using var encoder = new BinaryEncoder(context);
            value.Encode(encoder);
            return encoder.CloseAndReturnBuffer()!;
        }

        private static IEncodeable DecodeXml(
            ServiceMessageContext context,
            string xml,
            ExpandedNodeId typeId,
            bool useParser)
        {
            if (useParser)
            {
                using var parser = new XmlParser(xml, context);
                parser.PushNamespace(XmlNamespace);
                IEncodeable parsed = parser.ReadEncodeable<IEncodeable>("Value", typeId);
                parser.PopNamespace();
                return parsed;
            }
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            using var reader = XmlReader.Create(stream, CoreUtils.DefaultXmlReaderSettings());
            using var decoder = new XmlDecoder(reader, context);
            decoder.PushNamespace(XmlNamespace);
            IEncodeable decoded = decoder.ReadEncodeable<IEncodeable>("Value", typeId);
            decoder.PopNamespace();
            return decoded;
        }

        private static Assembly GenerateAndLoad()
        {
            CSharpCompilation compilation = OptimizationLevel.Debug
                .CreateCompilation("StructureXmlInterop")
                .AddCode(
                    new Dictionary<string, string>().WithOpcUaGeneratedStack(),
                    LanguageVersion.CSharp11);
            var options = new AnalyzerOptionsProvider(
                new Dictionary<string, string>
                {
                    ["build_property.ModelSourceGeneratorOmitFluentApi"] = "true",
                    ["build_property.ModelSourceGeneratorOmitEventRecords"] = "true",
                    ["build_property.ModelSourceGeneratorOmitObjectTypeProxies"] = "true"
                });
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new ModelSourceGenerator())
                .WithUpdatedParseOptions(new CSharpParseOptions()
                    .WithKind(SourceCodeKind.Regular)
                    .WithLanguageVersion(LanguageVersion.CSharp11))
                .AddAdditionalTexts([EmbeddedText.Create("XmlInterop.xml", Design)])
                .WithUpdatedAnalyzerConfigOptions(options);
            driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation output,
                out ImmutableArray<Diagnostic> diagnostics);
            Assert.That(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty);

            using var stream = new MemoryStream();
            EmitResult result = output.Emit(stream);
            Assert.That(
                result.Success,
                Is.True,
                string.Join(
                    Environment.NewLine,
                    result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(10)));
            return Assembly.Load(stream.ToArray());
        }

        private sealed class TestTelemetry : TelemetryContextBase
        {
            public TestTelemetry()
                : base(NullLoggerFactory.Instance)
            {
            }
        }

        private const string Design =
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <opc:ModelDesign
              xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
              xmlns:ua="http://opcfoundation.org/UA/"
              xmlns="http://test.org/UA/XI/"
              TargetNamespace="http://test.org/UA/XI/">
              <opc:Namespaces>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua" XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd">http://opcfoundation.org/UA/</opc:Namespace>
                <opc:Namespace Name="XI" Prefix="Test.XI" XmlNamespace="http://test.org/UA/XI/Types.xsd">http://test.org/UA/XI/</opc:Namespace>
              </opc:Namespaces>
              <opc:DataType SymbolicName="Color" BaseType="ua:Enumeration">
                <opc:Fields>
                  <opc:Field Name="Red" Identifier="0" />
                  <opc:Field Name="Green" Identifier="1" />
                  <opc:Field Name="Blue" Identifier="2" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="Rec" BaseType="ua:Structure">
                <opc:Fields>
                  <opc:Field Name="A" DataType="ua:Int32" />
                  <opc:Field Name="S" DataType="ua:String" />
                  <opc:Field Name="Arr" DataType="ua:Int32" ValueRank="Array" />
                  <opc:Field Name="Mx" DataType="ua:Double" ValueRank="OneOrMoreDimensions" ArrayDimensions="0,0" />
                  <opc:Field Name="C" DataType="Color" />
                  <opc:Field Name="Cs" DataType="Color" ValueRank="Array" />
                  <opc:Field Name="N" DataType="ua:NodeId" />
                  <opc:Field Name="L" DataType="ua:LocalizedText" />
                  <opc:Field Name="G" DataType="ua:Guid" />
                  <opc:Field Name="Names" DataType="ua:String" ValueRank="Array" />
                  <opc:Field Name="Empty" DataType="ua:Int32" ValueRank="Array" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="Opt" BaseType="ua:Structure">
                <opc:Fields>
                  <opc:Field Name="Req" DataType="ua:Int32" />
                  <opc:Field Name="O1" DataType="ua:String" IsOptional="true" />
                  <opc:Field Name="O2" DataType="ua:Int32" IsOptional="true" />
                  <opc:Field Name="O3" DataType="ua:Double" ValueRank="Array" IsOptional="true" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="Uni" BaseType="ua:Union" IsUnion="true">
                <opc:Fields>
                  <opc:Field Name="AsInt" DataType="ua:Int32" />
                  <opc:Field Name="AsArr" DataType="ua:Double" ValueRank="Array" />
                  <opc:Field Name="AsColor" DataType="Color" />
                </opc:Fields>
              </opc:DataType>
            </opc:ModelDesign>
            """;

        private static readonly int[] s_ints = [1, 2];
        private static readonly string[] s_names = ["a", "b"];
        private static readonly double[] s_doubles = [1.5, 2.5];
        private Assembly m_assembly;
        private NamespaceTable m_namespaceUris;
        private Type m_colorType;
    }
}
