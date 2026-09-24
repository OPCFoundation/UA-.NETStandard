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
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Compiles and runs [DataType] source types to check that the emitted
    /// data type definition describes what Encode() writes and that the
    /// generated members behave.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class TypeGeneratorDefinitionTests
    {
        private const string NamespaceUri = "urn:defs";

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_assembly = CompileAndLoad(Source, out m_generated);
            m_context = ServiceMessageContext.CreateEmpty(new TestTelemetry());
            m_namespaceUris = new NamespaceTable();
            m_namespaceUris.Append(NamespaceUri);
        }

        /// <summary>
        /// D-7 (a): a nullable property is always encoded (there is no
        /// encoding mask), so it must not be declared optional.
        /// D-7 (b): a sealed structure field is written inline, so its
        /// DataType is the concrete structure and not the abstract
        /// Structure, which means "encoded as ExtensionObject".
        /// </summary>
        [Test]
        public void DefinitionMatchesEncodedFields()
        {
            StructureDefinition definition = GetDefinition("Pt");

            Assert.That(definition.StructureType, Is.EqualTo(StructureType.Structure));
            Assert.That(definition.Fields.ToArray().Any(f => f.IsOptional), Is.False);
            var inner = new NodeId(1u, 1);
            StructureField child = definition.Fields.ToArray().Single(f => f.Name == "Child");
            StructureField items = definition.Fields.ToArray().Single(f => f.Name == "Items");
            Assert.That(child.DataType, Is.EqualTo(inner));
            Assert.That(items.DataType, Is.EqualTo(inner));
            Assert.That(items.ValueRank, Is.EqualTo(ValueRanks.OneDimension));

            // Round trip through the definition-driven codec of the stack.
            object pt = Create("Pt");
            Set(pt, "Name", "p");
            Set(pt, "X", 4);
            object innerValue = Create("Inner");
            Set(innerValue, "V", 9);
            Set(pt, "Child", innerValue);
            byte[] bytes = EncodeBinary((IEncodeable)pt);

            using var decoder = new BinaryDecoder(bytes, m_context);
            Assert.That(decoder.ReadString(null), Is.EqualTo("p"));
            Assert.That(decoder.ReadInt32(null), Is.EqualTo(4));
            Assert.That(decoder.ReadInt32(null), Is.EqualTo(9), "Child is written inline");
        }

        /// <summary>
        /// D-7 (c): a derived type encodes the base fields first, so its
        /// definition has to start with them and name the base type.
        /// </summary>
        [Test]
        public void DerivedDefinitionIncludesBaseFields()
        {
            StructureDefinition definition = GetDefinition("Pt3D");

            Assert.That(
                definition.Fields.ToArray().Select(f => f.Name),
                Is.EqualTo(s_pt3DFields));
            Assert.That(definition.FirstExplicitFieldIndex, Is.EqualTo(4));
            Assert.That(definition.BaseDataType, Is.EqualTo(new NodeId(2u, 1)));
        }

        /// <summary>
        /// D-8: with DefaultValueHandling.Exclude a value equal to the CLR
        /// default was omitted on encode, but a missing field keeps the
        /// property initializer on decode, so 0/false came back as 3/true.
        /// </summary>
        [Test]
        public void ExcludedDefaultValuesRoundTripWithInitializers()
        {
            object cfg = Create("Cfg");
            Set(cfg, "Retries", 0);
            Set(cfg, "Enabled", false);
            Set(cfg, "Label", null);
            Set(cfg, "Stamp", DateTimeUtc.MinValue);

            object decoded = JsonRoundTrip((IEncodeable)cfg);
            Assert.That(Get(decoded, "Retries"), Is.Zero);
            Assert.That(Get(decoded, "Enabled"), Is.False);
            Assert.That(Get(decoded, "Label"), Is.Null);
            Assert.That(Get(decoded, "Stamp"), Is.EqualTo(DateTimeUtc.MinValue));

            // The declared defaults still round trip (and are omitted).
            object defaults = Create("Cfg");
            string json = EncodeJson((IEncodeable)defaults);
            Assert.That(json, Does.Not.Contain("Retries"));
            Assert.That(json, Does.Not.Contain("Tag"));
            object decodedDefaults = JsonRoundTrip((IEncodeable)defaults);
            Assert.That(Get(decodedDefaults, "Retries"), Is.EqualTo(3));
            Assert.That(Get(decodedDefaults, "Enabled"), Is.True);
            Assert.That(Get(decodedDefaults, "Label"), Is.EqualTo("x"));
        }

        /// <summary>
        /// D-9: MatrixOf properties generated calls that do not exist on
        /// IEncoder/IDecoder. They now compile and round trip.
        /// </summary>
        [Test]
        public void MatrixPropertiesRoundTrip()
        {
            object img = Create("Img");
            Set(img, "Pixels", new[,] { { 1, 2, 3 }, { 4, 5, 6 } }.ToMatrixOf());
            object cell = Create("Inner");
            Set(cell, "V", 7);
            Type innerType = cell.GetType();
            var cells = Array.CreateInstance(innerType, 1, 2);
            cells.SetValue(cell, 0, 0);
            cells.SetValue(Create("Inner"), 0, 1);
            object cellMatrix = typeof(MatrixOf<>).MakeGenericType(innerType)
                .GetMethod("CreateFromArray", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, [cells]);
            Set(img, "Cells", cellMatrix);

            byte[] bytes = EncodeBinary((IEncodeable)img);
            var decoded = (IEncodeable)Create("Img");
            using (var decoder = new BinaryDecoder(bytes, m_context))
            {
                decoded.Decode(decoder);
            }
            Assert.That(Get(decoded, "Pixels"), Is.EqualTo(Get(img, "Pixels")));
            Assert.That(
                ((MatrixOf<int>)Get(decoded, "Pixels")).Dimensions,
                Is.EqualTo(s_pixelDimensions));

            // Generated [DataType] classes do not override Equals, so the
            // structure matrix is compared element by element.
            object decodedCells = Get(decoded, "Cells");
            Assert.That(
                decodedCells.GetType().GetProperty("Dimensions").GetValue(decodedCells),
                Is.EqualTo(s_cellDimensions));
            object flattened = decodedCells.GetType()
                .GetMethod("ToArrayOf", Type.EmptyTypes)
                .Invoke(decodedCells, null);
            var decodedCell = (IEncodeable)((Array)flattened.GetType()
                .GetMethod("ToArray", Type.EmptyTypes)
                .Invoke(flattened, null)).GetValue(0);
            Assert.That(decodedCell.IsEqual((IEncodeable)cell), Is.True);
            object perms = Get(decoded, "Perms");
            Assert.That(perms.GetType().GetProperty("IsNull").GetValue(perms), Is.True);
        }

        /// <summary>
        /// D-10: Clone() shared ArrayOf elements with reference semantics
        /// between the original and the clone.
        /// </summary>
        [Test]
        public void CloneDeepCopiesArraysOfStructures()
        {
            object line = Create("Line");
            Set(line, "Qty", 1);
            Type lineType = line.GetType();
            Array lines = Array.CreateInstance(lineType, 1);
            lines.SetValue(line, 0);
            object order = Create("Order");
            Type linesType = typeof(ArrayOf<>).MakeGenericType(lineType);
            Set(order, "Lines", linesType
                .GetMethod("op_Implicit", [lines.GetType()])
                .Invoke(null, [lines]));

            object clone = order.GetType().GetMethod("Clone").Invoke(order, null);
            object clonedLines = Get(clone, "Lines");
            object clonedLine = clonedLines.GetType().GetProperty("Item").GetValue(clonedLines, [0]);
            Set(clonedLine, "Qty", 9);

            Assert.That(Get(line, "Qty"), Is.EqualTo(1), "the clone must not share the element");
        }

        /// <summary>
        /// D-11: the EnumField value of an OptionSet is the bit position;
        /// the zero member and combined members name no bit.
        /// </summary>
        [Test]
        public void FlagsEnumDefinitionUsesBitPositions()
        {
            Type activator = m_assembly.GetType("TestApp.Defs.PermActivator", throwOnError: true);
            var source = (IDataTypeDefinitionSource)activator
                .GetField("Instance", BindingFlags.Public | BindingFlags.Static)
                .GetValue(null);
            var definition = (EnumDefinition)source.GetDataTypeDefinition(m_namespaceUris);

            Assert.That(definition.IsOptionSet, Is.True);
            Assert.That(
                definition.Fields.ToArray().Select(f => (f.Name, f.Value)),
                Is.EqualTo(new[] { ("Read", 0L), ("Write", 1L), ("Exec", 2L) }));
        }

        private StructureDefinition GetDefinition(string typeName)
        {
            Type activator = m_assembly.GetType(
                "TestApp.Defs." + typeName + "Activator",
                throwOnError: true);
            var source = (IDataTypeDefinitionSource)activator
                .GetField("Instance", BindingFlags.Public | BindingFlags.Static)
                .GetValue(null);
            return (StructureDefinition)source.GetDataTypeDefinition(m_namespaceUris);
        }

        private object Create(string typeName)
        {
            return Activator.CreateInstance(
                m_assembly.GetType("TestApp.Defs." + typeName, throwOnError: true));
        }

        private static void Set(object instance, string name, object value)
        {
            instance.GetType().GetProperty(name).SetValue(instance, value);
        }

        private static object Get(object instance, string name)
        {
            return instance.GetType().GetProperty(name).GetValue(instance);
        }

        private byte[] EncodeBinary(IEncodeable value)
        {
            using var encoder = new BinaryEncoder(m_context);
            value.Encode(encoder);
            return encoder.CloseAndReturnBuffer();
        }

        private string EncodeJson(IEncodeable value)
        {
            using var encoder = new JsonEncoder(m_context);
            value.Encode(encoder);
            return encoder.CloseAndReturnText();
        }

        private object JsonRoundTrip(IEncodeable value)
        {
            string json = EncodeJson(value);
            var decoded = (IEncodeable)Activator.CreateInstance(value.GetType());
            using var decoder = new JsonDecoder(json, m_context);
            decoded.Decode(decoder);
            return decoded;
        }

        private static Assembly CompileAndLoad(string source, out string generated)
        {
            var generator = new ModelSourceGenerator();
            CSharpCompilation compilation = OptimizationLevel.Debug
                .CreateCompilation()
                .AddCode(
                    new[] { new KeyValuePair<string, string>("TestSource.cs", source) }
                        .WithOpcUaGeneratedStack(),
                    LanguageVersion.Preview);
            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator)
                .WithUpdatedParseOptions(new CSharpParseOptions()
                    .WithKind(SourceCodeKind.Regular)
                    .WithLanguageVersion(LanguageVersion.Preview));
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation outputCompilation,
                out ImmutableArray<Diagnostic> diagnostics);
            Assert.That(
                diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray(),
                Is.Empty);
            generated = string.Join(
                "\n",
                driver.GetRunResult().Results[0].GeneratedSources
                    .Select(s => s.SourceText.ToString()));

            using var stream = new MemoryStream();
            EmitResult emitResult = outputCompilation.Emit(stream);
            emitResult.Check(TestContext.Out, out int errors, out _);
            Assert.That(emitResult.Success, Is.True, $"Emit produced {errors} errors");
            return Assembly.Load(stream.ToArray());
        }

        private sealed class TestTelemetry : TelemetryContextBase
        {
            public TestTelemetry()
                : base(NullLoggerFactory.Instance)
            {
            }
        }

        private const string Source =
            """
            #nullable enable
            using System;
            using Opc.Ua;

            namespace TestApp.Defs
            {
                [DataType(Namespace = "urn:defs", DataTypeId = "i=1")]
                public sealed partial class Inner
                {
                    public int V { get; set; }
                }

                [DataType(Namespace = "urn:defs", DataTypeId = "i=2")]
                public partial class Pt
                {
                    public string? Name { get; set; }
                    public int X { get; set; }
                    public Inner Child { get; set; } = new Inner();
                    public ArrayOf<Inner> Items { get; set; }
                }

                [DataType(Namespace = "urn:defs", DataTypeId = "i=3")]
                public partial class Pt3D : Pt
                {
                    public int Z { get; set; }
                }

                [DataType(Namespace = "urn:defs", DataTypeId = "i=4")]
                public partial class Cfg
                {
                    public int Retries { get; set; } = 3;
                    public bool Enabled { get; set; } = true;
                    public string? Label { get; set; } = "x";
                    public DateTimeUtc Stamp { get; set; } = DateTimeUtc.Now;
                    public string Tag { get; set; } = string.Empty;
                }

                [DataType(Namespace = "urn:defs", DataTypeId = "i=5")]
                public partial class Img
                {
                    public MatrixOf<int> Pixels { get; set; }
                    public MatrixOf<Inner> Cells { get; set; }
                    public MatrixOf<Perm> Perms { get; set; }
                }

                [Flags]
                [DataType(Namespace = "urn:defs", DataTypeId = "i=6")]
                public enum Perm
                {
                    None = 0,
                    Read = 1,
                    Write = 2,
                    Exec = 4,
                    ReadWrite = 3
                }

                [DataType(Namespace = "urn:defs", DataTypeId = "i=7")]
                public partial class Line
                {
                    public int Qty { get; set; }
                }

                [DataType(Namespace = "urn:defs", DataTypeId = "i=8")]
                public partial class Order
                {
                    public ArrayOf<Line> Lines { get; set; }
                }
            }
            """;

        private static readonly string[] s_pt3DFields = ["Name", "X", "Child", "Items", "Z"];
        private static readonly int[] s_pixelDimensions = [2, 3];
        private static readonly int[] s_cellDimensions = [1, 2];
        private Assembly m_assembly;
        private string m_generated;
        private ServiceMessageContext m_context;
        private NamespaceTable m_namespaceUris;
    }
}
