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
using System.Globalization;
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

            // The declared defaults still round trip. They are not the type
            // defaults, so they are written even in Compact (E7).
            object defaults = Create("Cfg");
            string json = EncodeJson((IEncodeable)defaults, JsonEncoderOptions.Compact);
            Assert.That(json, Does.Contain("Retries"));
            Assert.That(json, Does.Contain("Tag"));
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
        /// A MatrixOf property is a multi-dimensional structure field: the
        /// definition publishes a matrix ValueRank (never 0, OPC 10000-3
        /// 8.51; the rank is unknown from MatrixOf&lt;T&gt;, so 2) and the
        /// generated code uses the inline matrix calls of the model driven
        /// generator (OPC 10000-6 5.2.5), not the Variant encoding.
        /// </summary>
        [Test]
        public void MatrixPropertiesAreInlineMatrices()
        {
            StructureDefinition definition = GetDefinition("Img");
            foreach (string name in s_imgMatrices)
            {
                StructureField field = definition.Fields.ToArray().Single(f => f.Name == name);
                Assert.That(field.ValueRank, Is.EqualTo(ValueRanks.TwoDimensions), name);
                Assert.That(field.ArrayDimensions.ToArray(), Is.EqualTo(s_unknownTwoDimensions), name);
            }

            Assert.That(m_generated, Does.Contain(
                "global::Opc.Ua.EncoderExtensions.WriteInlineMatrixValue(encoder, \"Pixels\", global::Opc.Ua.Variant.From(Pixels));"));
            Assert.That(m_generated, Does.Contain(
                "Pixels = decoder.ReadVariantValue(\"Pixels\", global::Opc.Ua.TypeInfo.Create(" +
                "global::Opc.Ua.BuiltInType.Int32, global::Opc.Ua.ValueRanks.TwoDimensions)).GetInt32Matrix();"));
            Assert.That(m_generated, Does.Contain(
                "global::Opc.Ua.BuiltInType.Enumeration, global::Opc.Ua.ValueRanks.TwoDimensions))" +
                ".GetEnumerationMatrix<"));
            Assert.That(m_generated, Does.Contain("encoder.WriteEncodeableMatrix(\"Cells\", Cells);"));
            Assert.That(m_generated, Does.Not.Contain("encoder.WriteVariant(\""));
            Assert.That(m_generated, Does.Not.Contain("decoder.ReadVariant(\""));
        }

        /// <summary>
        /// The binary encoding of MatrixOf properties is the inline matrix:
        /// Int32 dimensions (-1 for null, at least two) followed by the
        /// product of the dimensions values without a length prefix.
        /// </summary>
        [Test]
        public void MatrixPropertiesEncodeInlineMatrixBytes()
        {
            IEncodeable img = CreateImg();

            byte[] bytes = EncodeBinary(img);

            byte[] expected = Concat(
                Int32s(2, 2, 3, 1, 2, 3, 4, 5, 6), // Pixels
                Int32s(2, 1, 2, 7, 0), // Cells: Inner.V of each element
                Int32s(2, 2, 1, 1, 6), // Perms: Read and Write|Exec as Int32
                Int32s(2, 0, 0), // Names: MatrixOf.Empty is 0 x 0
                Int32s(-1), // Objects: null
                Int32s(2, 1, 1), [(byte)BuiltInType.String], Int32s(1), [(byte)'v'], // Values
                Int32s(9)); // Tail
            Assert.That(bytes, Is.EqualTo(expected));

            var decoded = (IEncodeable)Create("Img");
            using (var decoder = new BinaryDecoder(bytes, m_context))
            {
                decoded.Decode(decoder);
                Assert.That(decoder.Position, Is.EqualTo(bytes.Length));
            }
            Assert.That(Get(decoded, "Tail"), Is.EqualTo(9));
            Assert.That(((MatrixOf<string>)Get(decoded, "Names")).Dimensions, Is.EqualTo(s_emptyDimensions));
            Assert.That(((MatrixOf<ExtensionObject>)Get(decoded, "Objects")).IsNull, Is.True);
        }

        /// <summary>
        /// The generated type and the DataTypeDefinition driven codec reading
        /// the published definition write the same bytes, in both directions,
        /// and the matrices survive the JSON and XML encodings.
        /// </summary>
        [Test]
        public void MatrixPropertiesInteroperateWithDefinitionDrivenCodec()
        {
            IEncodeable img = CreateImg();
            ExpandedNodeId typeId = img.TypeId;
            ServiceMessageContext generatedContext = CreateContext();
            generatedContext.Factory.Builder.AddEncodeableTypes(m_assembly).Commit();
            var structureType = new Encoders.Structure(
                new System.Xml.XmlQualifiedName("Img", NamespaceUri),
                img.TypeId,
                img.BinaryEncodingId,
                img.XmlEncodingId,
                GetDefinition("Img"),
                new Dictionary<string, BuiltInType>
                {
                    ["Pixels"] = BuiltInType.Int32,
                    ["Cells"] = BuiltInType.Null,
                    ["Perms"] = BuiltInType.Enumeration,
                    ["Names"] = BuiltInType.String,
                    ["Objects"] = BuiltInType.ExtensionObject,
                    ["Values"] = BuiltInType.Variant,
                    ["Tail"] = BuiltInType.Int32
                });
            ServiceMessageContext runtimeContext = CreateContext();
            runtimeContext.Factory.Builder
                .AddEncodeableType(Create("Inner").GetType())
                .AddEncodeableType(structureType)
                .Commit();

            byte[] generatedBytes = EncodeBinary(generatedContext, img, typeId);
            IEncodeable structure;
            using (var decoder = new BinaryDecoder(generatedBytes, runtimeContext))
            {
                structure = decoder.ReadEncodeable<IEncodeable>(null, typeId);
            }
            Assert.That(structure, Is.InstanceOf<Encoders.Structure>());
            Assert.That(
                ((IStructure)structure)["Pixels"].GetInt32Matrix(),
                Is.EqualTo(Get(img, "Pixels")));
            Assert.That(((IStructure)structure)["Tail"].GetInt32(), Is.EqualTo(9));

            byte[] runtimeBytes = EncodeBinary(runtimeContext, structure, typeId);
            Assert.That(runtimeBytes, Is.EqualTo(generatedBytes), "both codecs write the same bytes");

            using (var decoder = new BinaryDecoder(runtimeBytes, generatedContext))
            {
                IEncodeable back = decoder.ReadEncodeable<IEncodeable>(null, typeId);
                Assert.That(EncodeBinary(generatedContext, back, typeId), Is.EqualTo(generatedBytes));
            }

            // JSON and XML round trips of the generated type.
            object json = JsonRoundTrip(img);
            Assert.That(EncodeBinary((IEncodeable)json), Is.EqualTo(EncodeBinary(img)), "JSON");
            object xml = XmlRoundTrip(generatedContext, img, typeId);
            // XML Matrix dimensions must be > 0 (OPC 10000-6 5.3.1.17): the
            // empty Names matrix is written as null (null == empty, 5.1.11).
            IEncodeable expectedXml = CreateImg();
            Set(expectedXml, "Names", default(MatrixOf<string>));
            Assert.That(EncodeBinary((IEncodeable)xml), Is.EqualTo(EncodeBinary(expectedXml)), "XML");
        }

        private IEncodeable CreateImg()
        {
            object img = Create("Img");
            Set(img, "Pixels", new[,] { { 1, 2, 3 }, { 4, 5, 6 } }.ToMatrixOf());
            object cell = Create("Inner");
            Set(cell, "V", 7);
            Type innerType = cell.GetType();
            var cells = Array.CreateInstance(innerType, 1, 2);
            cells.SetValue(cell, 0, 0);
            cells.SetValue(Create("Inner"), 0, 1);
            Set(img, "Cells", typeof(MatrixOf<>).MakeGenericType(innerType)
                .GetMethod("CreateFromArray", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, [cells]));
            Type permType = m_assembly.GetType("TestApp.Defs.Perm", throwOnError: true);
            var perms = Array.CreateInstance(permType, 2, 1);
            perms.SetValue(Enum.ToObject(permType, 1), 0, 0);
            perms.SetValue(Enum.ToObject(permType, 6), 1, 0);
            Set(img, "Perms", typeof(MatrixOf)
                .GetMethod(nameof(MatrixOf.From), BindingFlags.Public | BindingFlags.Static)
                .MakeGenericMethod(permType)
                .Invoke(null, [perms]));
            Set(img, "Names", MatrixOf<string>.Empty);
            Set(img, "Values", new[,] { { Variant.From("v") } }.ToMatrixOf());
            Set(img, "Tail", 9);
            return (IEncodeable)img;
        }

        private ServiceMessageContext CreateContext()
        {
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(new TestTelemetry());
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append(NamespaceUri);
            context.NamespaceUris = namespaceUris;
            return context;
        }

        private static byte[] EncodeBinary(ServiceMessageContext context, IEncodeable value, ExpandedNodeId typeId)
        {
            using var encoder = new BinaryEncoder(context);
            encoder.WriteEncodeable(null, value, typeId);
            return encoder.CloseAndReturnBuffer();
        }

        private static object XmlRoundTrip(ServiceMessageContext context, IEncodeable value, ExpandedNodeId typeId)
        {
            string xml;
            using (var encoder = new XmlEncoder(context))
            {
                encoder.PushNamespace(NamespaceUri);
                encoder.WriteEncodeable("Img", value, typeId);
                encoder.PopNamespace();
                xml = encoder.CloseAndReturnText();
            }
            using var parser = new XmlParser(xml, context);
            parser.PushNamespace(NamespaceUri);
            IEncodeable decoded = parser.ReadEncodeable<IEncodeable>("Img", typeId);
            parser.PopNamespace();
            return decoded;
        }

        private static byte[] Int32s(params int[] values)
        {
            return values.SelectMany(BitConverter.GetBytes).ToArray();
        }

        private static byte[] Concat(params byte[][] parts)
        {
            return parts.SelectMany(p => p).ToArray();
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
        /// D4 (d): a [Flags] enum is encoded as an Enumeration (Int32), while
        /// an OptionSet of up to 64 bits is a UInteger subtype encoded as that
        /// integer (OPC 10000-3 5.8.2, 8.40). Publishing it as an OptionSet
        /// described another wire format; it is published as the
        /// enumeration it is encoded as, with all members and their values.
        /// </summary>
        [Test]
        public void FlagsEnumDefinitionIsTheEncodedEnumeration()
        {
            Type activator = m_assembly.GetType("TestApp.Defs.PermActivator", throwOnError: true);
            var source = (IDataTypeDefinitionSource)activator
                .GetField("Instance", BindingFlags.Public | BindingFlags.Static)
                .GetValue(null);
            var definition = (EnumDefinition)source.GetDataTypeDefinition(m_namespaceUris);

            Assert.That(definition.IsOptionSet, Is.False);
            Assert.That(
                definition.Fields.ToArray().Select(f => (f.Name, f.Value)),
                Is.EqualTo(new[] { ("None", 0L), ("Read", 1L), ("Write", 2L), ("Exec", 4L), ("ReadWrite", 3L) }));
        }

        /// <summary>
        /// A3-2: the namespace-level activator derives from
        /// EncodeableType&lt;T&gt;/EnumeratedType&lt;T&gt;, so it has to be
        /// internal when T is not effectively public (CS0060 otherwise).
        /// </summary>
        [Test]
        public void NonPublicDataTypesGetInternalActivatorsAndRoundTrip()
        {
            const string source =
                """
                using Opc.Ua;

                namespace TestApp.Access
                {
                    [DataType(Namespace = "urn:access", DataTypeId = "i=1")]
                    internal partial class TopInternal
                    {
                        public int A { get; set; }
                    }

                    public static partial class PublicHost
                    {
                        [DataType(Namespace = "urn:access", DataTypeId = "i=2")]
                        internal partial class NestedInternal
                        {
                            public string B { get; set; }
                        }
                    }

                    internal static partial class InternalHost
                    {
                        [DataType(Namespace = "urn:access", DataTypeId = "i=3")]
                        public partial class NestedPublic
                        {
                            public double C { get; set; }
                        }

                        [DataType(Namespace = "urn:access", DataTypeId = "i=4")]
                        public enum Mode
                        {
                            Off = 0,
                            On = 1
                        }
                    }

                    [DataType(Namespace = "urn:access", DataTypeId = "i=5")]
                    public partial class StillPublic
                    {
                        public int D { get; set; }
                    }
                }
                """;
            Assembly assembly = CompileAndLoad(source, out string generated, expectNoWarnings: true);

            Assert.That(generated, Does.Contain("internal sealed class TopInternalActivator"));
            Assert.That(generated, Does.Contain("internal sealed class PublicHost_NestedInternalActivator"));
            Assert.That(generated, Does.Contain("internal sealed class InternalHost_NestedPublicActivator"));
            Assert.That(generated, Does.Contain("internal sealed class InternalHost_ModeActivator"));
            Assert.That(generated, Does.Contain("public sealed class StillPublicActivator"));

            (string Type, string Property, object Value)[] cases =
            [
                ("TestApp.Access.TopInternal", "A", 42),
                ("TestApp.Access.PublicHost+NestedInternal", "B", "b"),
                ("TestApp.Access.InternalHost+NestedPublic", "C", 1.5)
            ];
            foreach ((string typeName, string property, object value) in cases)
            {
                Type type = assembly.GetType(typeName, throwOnError: true);
                var original = (IEncodeable)Activator.CreateInstance(type, nonPublic: true);
                Set(original, property, value);
                byte[] bytes = EncodeBinary(original);
                var decoded = (IEncodeable)Activator.CreateInstance(type, nonPublic: true);
                using (var decoder = new BinaryDecoder(bytes, m_context))
                {
                    decoded.Decode(decoder);
                }
                Assert.That(Get(decoded, property), Is.EqualTo(value), typeName);
                Assert.That(Get(JsonRoundTrip(original), property), Is.EqualTo(value), typeName);
            }
        }

        /// <summary>
        /// D-2: enum member values were captured with the current culture and
        /// parsed with the invariant culture, so with U+2212 as negative sign
        /// negative members were published as 0 and negative flag bits lost.
        /// </summary>
        [Test]
        public void NegativeEnumValuesSurviveCultureWithUnicodeMinus()
        {
            const string source =
                """
                using System;
                using Opc.Ua;

                namespace TestApp.Minus
                {
                    [DataType(Namespace = "urn:defs", DataTypeId = "i=1")]
                    public enum Mode
                    {
                        Invalid = -1,
                        A = 0,
                        B = 1
                    }

                    [Flags]
                    [DataType(Namespace = "urn:defs", DataTypeId = "i=2")]
                    public enum Bits
                    {
                        None = 0,
                        Low = 1,
                        High = unchecked((int)0x80000000)
                    }
                }
                """;
            CultureInfo previous = CultureInfo.CurrentCulture;
            var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            culture.NumberFormat.NegativeSign = "−";
            Assembly assembly;
            try
            {
                CultureInfo.CurrentCulture = culture;
                Assert.That((-1).ToString(CultureInfo.CurrentCulture), Does.StartWith("−"));
                assembly = CompileAndLoad(source, out _);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }

            EnumDefinition mode = GetEnumDefinition(assembly, "TestApp.Minus.ModeActivator");
            Assert.That(
                mode.Fields.ToArray().Select(f => (f.Name, f.Value)),
                Is.EqualTo(new[] { ("Invalid", -1L), ("A", 0L), ("B", 1L) }));

            EnumDefinition bits = GetEnumDefinition(assembly, "TestApp.Minus.BitsActivator");
            Assert.That(bits.IsOptionSet, Is.False);
            Assert.That(
                bits.Fields.ToArray().Select(f => (f.Name, f.Value)),
                Is.EqualTo(new[] { ("None", 0L), ("Low", 1L), ("High", (long)int.MinValue) }));
        }

        /// <summary>
        /// A3-1: a SetIfMissing field is always decoded, so a missing field
        /// comes back as the CLR default. Omitting a value equal to the
        /// property initializer (D-8) therefore lost it; only the CLR default
        /// may be omitted.
        /// </summary>
        [Test]
        public void SetIfMissingFieldAtInitializerRoundTrips()
        {
            const string source =
                """
                using Opc.Ua;

                namespace TestApp.SetIfMissing
                {
                    [DataType(Namespace = "urn:defs", DataTypeId = "i=1")]
                    public partial class Counter
                    {
                        [DataTypeField(Order = 0, DefaultValueHandling = DefaultValueHandling.SetIfMissing)]
                        public int Count { get; set; } = 5;

                        [DataTypeField(Order = 1, DefaultValueHandling = DefaultValueHandling.SetIfMissing)]
                        public bool Flag { get; set; } = true;

                        [DataTypeField(Order = 2, DefaultValueHandling = DefaultValueHandling.SetIfMissing)]
                        public string Text { get; set; } = "abc";
                    }
                }
                """;
            Assembly assembly = CompileAndLoad(source, out _);
            Type type = assembly.GetType("TestApp.SetIfMissing.Counter", throwOnError: true);

            // The initializer values must survive.
            var original = (IEncodeable)Activator.CreateInstance(type);
            object json = JsonRoundTrip(original);
            Assert.That(Get(json, "Count"), Is.EqualTo(5), "JSON");
            Assert.That(Get(json, "Flag"), Is.True, "JSON");
            Assert.That(Get(json, "Text"), Is.EqualTo("abc"), "JSON");

            ServiceMessageContext context = CreateContext();
            context.Factory.Builder.AddEncodeableTypes(assembly).Commit();
            object xml = XmlRoundTrip(context, original, original.TypeId);
            Assert.That(Get(xml, "Count"), Is.EqualTo(5), "XML");
            Assert.That(Get(xml, "Flag"), Is.True, "XML");
            Assert.That(Get(xml, "Text"), Is.EqualTo("abc"), "XML");

            // The CLR defaults are omitted and decoded as the CLR default.
            var zero = (IEncodeable)Activator.CreateInstance(type);
            Set(zero, "Count", 0);
            Set(zero, "Flag", false);
            Set(zero, "Text", null);
            string zeroJson = EncodeJson(zero, JsonEncoderOptions.Compact);
            Assert.That(zeroJson, Does.Not.Contain("Count"));
            object decodedZero = JsonRoundTrip(zero);
            Assert.That(Get(decodedZero, "Count"), Is.Zero);
            Assert.That(Get(decodedZero, "Flag"), Is.False);
            Assert.That(Get(decodedZero, "Text"), Is.Null);
        }

        /// <summary>
        /// ALT-8: a default the parameterless constructor assigns (or a
        /// backing field initializer) was not seen by the D-8 omit check, so
        /// a value equal to the CLR default was omitted and decoded as the
        /// constructor's value. A field is now only omitted at a default that
        /// is known: a literal initializer (of the property or of the field
        /// its getter returns) or none, that no constructor overrides.
        /// </summary>
        [Test]
        public void ConstructorAssignedDefaultsRoundTrip()
        {
            const string source =
                """
                #nullable enable
                using Opc.Ua;

                namespace TestApp.CtorDefaults
                {
                    [DataType(Namespace = "urn:defs", DataTypeId = "i=1")]
                    public partial class Cfg
                    {
                        public Cfg()
                        {
                            Retries = 3;
                            this.Name = "n";
                            m_timeout = 100;
                        }

                        public int Retries { get; set; }
                        public string? Name { get; set; }
                        public int Timeout { get => m_timeout; set => m_timeout = value; }
                        public int Port { get => m_port; set => m_port = value; }
                        public int Plain { get { return m_plain; } set { m_plain = value; } }
                        public int Untouched { get; set; }

                        private int m_timeout;
                        private int m_port = 4840;
                        private int m_plain;
                    }

                    [DataType(Namespace = "urn:defs", DataTypeId = "i=2")]
                    public partial class Initialized
                    {
                        public Initialized()
                        {
                            Initialize();
                        }

                        private void Initialize()
                        {
                            Count = 7;
                        }

                        public int Count { get; set; }
                    }
                }
                """;
            Assembly assembly = CompileAndLoad(source, out _);
            Type cfgType = assembly.GetType("TestApp.CtorDefaults.Cfg", throwOnError: true);

            var cleared = (IEncodeable)Activator.CreateInstance(cfgType);
            Set(cleared, "Retries", 0);
            Set(cleared, "Name", null);
            Set(cleared, "Timeout", 0);
            Set(cleared, "Port", 0);
            object decoded = JsonRoundTrip(cleared);
            Assert.That(Get(decoded, "Retries"), Is.Zero, "constructor assigned property");
            Assert.That(Get(decoded, "Name"), Is.Null, "this-qualified constructor assignment");
            Assert.That(Get(decoded, "Timeout"), Is.Zero, "constructor assigned backing field");
            Assert.That(Get(decoded, "Port"), Is.Zero, "backing field initializer");

            // Only type defaults are omitted (Compact): the CLR default of
            // properties nothing assigns. The backing field literal 4840 is
            // not the type default and is written (E7).
            var defaults = (IEncodeable)Activator.CreateInstance(cfgType);
            string json = EncodeJson(defaults, JsonEncoderOptions.Compact);
            Assert.That(json, Does.Contain("Port"));
            Assert.That(json, Does.Not.Contain("Plain"));
            Assert.That(json, Does.Not.Contain("Untouched"));
            Assert.That(json, Does.Contain("Retries"));
            object decodedDefaults = JsonRoundTrip(defaults);
            Assert.That(Get(decodedDefaults, "Port"), Is.EqualTo(4840));
            Assert.That(Get(decodedDefaults, "Retries"), Is.EqualTo(3));

            Type initializedType = assembly.GetType("TestApp.CtorDefaults.Initialized", throwOnError: true);
            var zero = (IEncodeable)Activator.CreateInstance(initializedType);
            Set(zero, "Count", 0);
            Assert.That(Get(JsonRoundTrip(zero), "Count"), Is.Zero, "set by a helper the constructor calls");
        }

        /// <summary>
        /// E7: a Compact encoding may only omit a field whose value is the
        /// default of its type (OPC 10000-6 5.4.1, 5.4.2.1, Table 1), since
        /// a conformant decoder reads a missing field as the type default. A
        /// field declared with another default (initializer 4840) was
        /// omitted at 4840 and decoded as 0 by a peer; it is now always
        /// written. A field without initializer is omitted at the type
        /// default when the encoder can omit fields and written otherwise.
        /// </summary>
        [Test]
        public void OnlyTypeDefaultValuesAreOmitted()
        {
            const string source =
                """
                #nullable enable
                using Opc.Ua;

                namespace TestApp.TypeDefaults
                {
                    [DataType(Namespace = "urn:defs", DataTypeId = "i=1")]
                    public partial class Endpoint
                    {
                        public int Port { get; set; } = 4840;
                        public int Count { get; set; }
                        public string? Name { get; set; }
                    }
                }
                """;
            Assembly assembly = CompileAndLoad(source, out string generated);
            Type type = assembly.GetType("TestApp.TypeDefaults.Endpoint", throwOnError: true);
            Assert.That(generated, Does.Contain("if (!encoder.CanOmitFields || Count != 0) encoder.WriteInt32(\"Count\", Count);"));
            Assert.That(generated, Does.Not.Contain("|| Port"));
            ServiceMessageContext context = CreateContext();
            context.Factory.Builder.AddEncodeableTypes(assembly).Commit();

            // Port at its declared default 4840 is written, also in Compact.
            var atDeclared = (IEncodeable)Activator.CreateInstance(type);
            Assert.That(EncodeJson(atDeclared, JsonEncoderOptions.Compact), Does.Contain("\"Port\":4840"));
            Assert.That(EncodeJson(atDeclared, JsonEncoderOptions.Verbose), Does.Contain("\"Port\":4840"));
            Assert.That(EncodeXml(context, atDeclared), Does.Contain("<Port>4840</Port>"));
            Assert.That(Get(JsonRoundTrip(atDeclared), "Port"), Is.EqualTo(4840));

            // Port at the type default 0 is written too (the Compact
            // JsonEncoder itself drops a 0 value, which a conformant decoder
            // reads back as 0).
            var atTypeDefault = (IEncodeable)Activator.CreateInstance(type);
            Set(atTypeDefault, "Port", 0);
            Assert.That(EncodeJson(atTypeDefault, JsonEncoderOptions.Verbose), Does.Contain("\"Port\":0"));
            Assert.That(EncodeXml(context, atTypeDefault), Does.Contain("<Port>0</Port>"));
            Assert.That(Get(JsonRoundTrip(atTypeDefault), "Port"), Is.Zero, "JSON");
            Assert.That(Get(XmlRoundTrip(context, atTypeDefault, atTypeDefault.TypeId), "Port"), Is.Zero, "XML");

            // Count at the type default is omitted in Compact and written
            // whenever the encoder cannot omit fields (Verbose, binary).
            Assert.That(EncodeJson(atDeclared, JsonEncoderOptions.Compact), Does.Not.Contain("Count"));
            using (var verbose = new JsonEncoder(m_context, JsonEncoderOptions.Verbose))
            {
                bool canOmit = verbose.CanOmitFields;
                atDeclared.Encode(verbose);
                string verboseJson = verbose.CloseAndReturnText();
                Assert.That(
                    verboseJson,
                    canOmit ? Does.Not.Contain("\"Count\":0") : Does.Contain("\"Count\":0"));
            }

            // An empty string is a value, only null is the String default.
            var empty = (IEncodeable)Activator.CreateInstance(type);
            Set(empty, "Name", string.Empty);
            Assert.That(EncodeJson(empty, JsonEncoderOptions.Compact), Does.Contain("\"Name\":\"\""));
            Assert.That(Get(JsonRoundTrip(empty), "Name"), Is.EqualTo(string.Empty));
            Assert.That(EncodeJson(atDeclared, JsonEncoderOptions.Compact), Does.Not.Contain("Name"));
        }

        /// <summary>
        /// E7: a field missing from JSON is the type default (OPC 10000-6
        /// 5.4.1, 5.4.2.1, 5.4.7), not the declared initializer. The Compact
        /// JsonEncoder drops a 0 by itself, so a 4840-initialised field set
        /// to 0 came back as 4840. XML keeps the declared default of a
        /// missing field so configuration files that predate a field load.
        /// </summary>
        [Test]
        public void MissingJsonFieldIsTheTypeDefaultMissingXmlFieldTheDeclaredDefault()
        {
            const string source =
                """
                #nullable enable
                using Opc.Ua;

                namespace TestApp.MissingDefaults
                {
                    [DataType(Namespace = "urn:defs", DataTypeId = "i=1")]
                    public partial class Endpoint
                    {
                        public int Port { get; set; } = 4840;
                        public bool Enabled { get; set; } = true;
                        public string Name { get; set; } = "opc";
                    }
                }
                """;
            Assembly assembly = CompileAndLoad(source, out _);
            Type type = assembly.GetType("TestApp.MissingDefaults.Endpoint", throwOnError: true);
            ServiceMessageContext context = CreateContext();
            context.Factory.Builder.AddEncodeableTypes(assembly).Commit();

            // Compact JSON round trip of the type defaults returns them.
            var zero = (IEncodeable)Activator.CreateInstance(type);
            Set(zero, "Port", 0);
            Set(zero, "Enabled", false);
            Set(zero, "Name", null);
            string compact = EncodeCompactJson(zero);
            Assert.That(compact, Does.Not.Contain("Port"));
            object decoded = DecodeJson(type, compact);
            Assert.That(Get(decoded, "Port"), Is.Zero);
            Assert.That(Get(decoded, "Enabled"), Is.False);
            Assert.That(Get(decoded, "Name"), Is.Null);

            // A missing JSON field (a conformant peer's omission) is the
            // type default.
            decoded = DecodeJson(type, "{}");
            Assert.That(Get(decoded, "Port"), Is.Zero);
            Assert.That(Get(decoded, "Enabled"), Is.False);
            Assert.That(Get(decoded, "Name"), Is.Null);

            // A missing XML field keeps the declared default.
            string xml = EncodeXml(context, zero);
            foreach (string field in new[] { "Port", "Enabled", "Name" })
            {
                int start = xml.IndexOf("<" + field, StringComparison.Ordinal);
                if (start < 0)
                {
                    continue;
                }
                int selfClose = xml.IndexOf("/>", start, StringComparison.Ordinal);
                int close = xml.IndexOf("</" + field + ">", start, StringComparison.Ordinal);
                int end = close >= 0 && (selfClose < 0 || close < selfClose)
                    ? close + field.Length + 3
                    : selfClose + 2;
                xml = xml.Remove(start, end - start);
            }
            Assert.That(xml, Does.Not.Contain("Port"));
            using var parser = new XmlParser(xml, context);
            parser.PushNamespace(NamespaceUri);
            IEncodeable fromXml = parser.ReadEncodeable<IEncodeable>("Value", zero.TypeId);
            parser.PopNamespace();
            Assert.That(Get(fromXml, "Port"), Is.EqualTo(4840));
            Assert.That(Get(fromXml, "Enabled"), Is.True);
            Assert.That(Get(fromXml, "Name"), Is.EqualTo("opc"));

            // Value type defaults still round trip through XML (written).
            // A null reference is never written by the XmlEncoder, so it
            // reads back as the declared default (the XML leniency).
            object xmlZero = XmlRoundTrip(context, zero, zero.TypeId);
            Assert.That(Get(xmlZero, "Port"), Is.Zero);
            Assert.That(Get(xmlZero, "Enabled"), Is.False);
            Assert.That(Get(xmlZero, "Name"), Is.EqualTo("opc"));
        }

        private object DecodeJson(Type type, string json)
        {
            var decoded = (IEncodeable)Activator.CreateInstance(type);
            using var decoder = new JsonDecoder(json, m_context);
            decoded.Decode(decoder);
            return decoded;
        }

        private static string EncodeXml(ServiceMessageContext context, IEncodeable value)
        {
            using var encoder = new XmlEncoder(context);
            encoder.PushNamespace(NamespaceUri);
            encoder.WriteEncodeable("Value", value, value.TypeId);
            encoder.PopNamespace();
            return encoder.CloseAndReturnText();
        }

        /// <summary>
        /// D-6: a MatrixOf field of a structure that allows subtypes was
        /// written inline, which encodes a subtype element with its own
        /// fields while decode creates the declared type, desynchronizing
        /// the stream. It is now a matrix of extension objects, like the
        /// scalar and array forms, and published with the abstract DataType.
        /// </summary>
        [Test]
        public void SubtypedStructureMatrixRoundTrips()
        {
            const string source =
                """
                using Opc.Ua;

                namespace TestApp.Shapes
                {
                    [DataType(Namespace = "urn:defs", DataTypeId = "i=1", BinaryEncodingId = "i=11")]
                    public partial class Shape
                    {
                        public int Id { get; set; }
                    }

                    [DataType(Namespace = "urn:defs", DataTypeId = "i=2", BinaryEncodingId = "i=12")]
                    public partial class Circle : Shape
                    {
                        public double R { get; set; }
                    }

                    [DataType(Namespace = "urn:defs", DataTypeId = "i=3")]
                    public partial class Drawing
                    {
                        [DataTypeField(Order = 0)]
                        public MatrixOf<Shape> Shapes { get; set; }

                        [DataTypeField(Order = 1, StructureHandling = StructureHandling.ExtensionObject)]
                        public MatrixOf<Circle> Circles { get; set; }

                        [DataTypeField(Order = 2)]
                        public int Tail { get; set; }
                    }
                }
                """;
            Assembly assembly = CompileAndLoad(source, out string generated);
            Assert.That(generated, Does.Not.Contain("WriteEncodeableMatrix(\"Shapes\""));
            Assert.That(generated, Does.Not.Contain("WriteEncodeableMatrix(\"Circles\""),
                "StructureHandling = AsExtensionObject is honoured");

            ServiceMessageContext context = CreateContext();
            context.Factory.Builder.AddEncodeableTypes(assembly).Commit();
            Type shapeType = assembly.GetType("TestApp.Shapes.Shape", throwOnError: true);
            Type circleType = assembly.GetType("TestApp.Shapes.Circle", throwOnError: true);
            object circle = Activator.CreateInstance(circleType);
            Set(circle, "Id", 1);
            Set(circle, "R", 2.5);
            object shape = Activator.CreateInstance(shapeType);
            Set(shape, "Id", 2);
            Array shapes = Array.CreateInstance(shapeType, 1, 2);
            shapes.SetValue(circle, 0, 0);
            shapes.SetValue(shape, 0, 1);
            object drawing = Activator.CreateInstance(
                assembly.GetType("TestApp.Shapes.Drawing", throwOnError: true));
            Set(drawing, "Shapes", typeof(MatrixOf<>).MakeGenericType(shapeType)
                .GetMethod("CreateFromArray", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, [shapes]));
            Set(drawing, "Tail", 9);

            byte[] bytes;
            using (var encoder = new BinaryEncoder(context))
            {
                ((IEncodeable)drawing).Encode(encoder);
                bytes = encoder.CloseAndReturnBuffer();
            }
            var decoded = (IEncodeable)Activator.CreateInstance(drawing.GetType());
            using (var decoder = new BinaryDecoder(bytes, context))
            {
                decoded.Decode(decoder);
                Assert.That(decoder.Position, Is.EqualTo(bytes.Length));
            }
            Assert.That(Get(decoded, "Tail"), Is.EqualTo(9));
            object decodedShapes = Get(decoded, "Shapes");
            object flattened = decodedShapes.GetType()
                .GetMethod("ToArrayOf", Type.EmptyTypes)
                .Invoke(decodedShapes, null);
            var elements = (Array)flattened.GetType()
                .GetMethod("ToArray", Type.EmptyTypes)
                .Invoke(flattened, null);
            Assert.That(elements.GetValue(0), Is.InstanceOf(circleType), "the subtype survives");
            Assert.That(Get(elements.GetValue(0), "R"), Is.EqualTo(2.5));
            Assert.That(Get(elements.GetValue(1), "Id"), Is.EqualTo(2));

            var namespaceUris = new NamespaceTable();
            namespaceUris.Append(NamespaceUri);
            var definitionSource = (IDataTypeDefinitionSource)assembly
                .GetType("TestApp.Shapes.DrawingActivator", throwOnError: true)
                .GetField("Instance", BindingFlags.Public | BindingFlags.Static)
                .GetValue(null);
            var definition = (StructureDefinition)definitionSource.GetDataTypeDefinition(namespaceUris);
            Assert.That(
                definition.Fields.ToArray().Single(f => f.Name == "Shapes").DataType,
                Is.EqualTo(new NodeId(22u)),
                "a matrix of extension objects publishes the abstract Structure");
        }

        /// <summary>
        /// D-5 / D4 (d): a [Flags] enum of any backing type is published as
        /// the enumeration it is encoded as, with its member values (a
        /// negative high bit included), not as OptionSet bit positions.
        /// </summary>
        [Test]
        public void FlagsEnumPublishesMemberValuesOfAnyWidth()
        {
            const string source =
                """
                using System;
                using Opc.Ua;

                namespace TestApp.Widths
                {
                    [Flags]
                    [DataType(Namespace = "urn:defs", DataTypeId = "i=1")]
                    public enum Small : sbyte
                    {
                        None = 0,
                        Low = 1,
                        High = unchecked((sbyte)0x80)
                    }

                    [Flags]
                    [DataType(Namespace = "urn:defs", DataTypeId = "i=2")]
                    public enum Medium : short
                    {
                        None = 0,
                        Low = 1,
                        High = unchecked((short)0x8000)
                    }

                    [Flags]
                    [DataType(Namespace = "urn:defs", DataTypeId = "i=3")]
                    public enum Large : long
                    {
                        None = 0,
                        Low = 1,
                        High = unchecked((long)0x8000000000000000),
                        Wide = unchecked((long)0xFFFFFFFF80000000)
                    }
                }
                """;
            Assembly assembly = CompileAndLoad(source, out _);

            EnumDefinition small = GetEnumDefinition(assembly, "TestApp.Widths.SmallActivator");
            Assert.That(small.IsOptionSet, Is.False);
            Assert.That(
                small.Fields.ToArray().Select(f => (f.Name, f.Value)),
                Is.EqualTo(new[] { ("None", 0L), ("Low", 1L), ("High", (long)sbyte.MinValue) }));
            Assert.That(
                GetEnumDefinition(assembly, "TestApp.Widths.MediumActivator")
                    .Fields.ToArray().Select(f => (f.Name, f.Value)),
                Is.EqualTo(new[] { ("None", 0L), ("Low", 1L), ("High", (long)short.MinValue) }));
            Assert.That(
                GetEnumDefinition(assembly, "TestApp.Widths.LargeActivator")
                    .Fields.ToArray().Select(f => (f.Name, f.Value)),
                Is.EqualTo(new[]
                {
                    ("None", 0L),
                    ("Low", 1L),
                    ("High", long.MinValue),
                    ("Wide", unchecked((long)0xFFFFFFFF80000000))
                }));
        }

        /// <summary>
        /// A3-3/A4-1: nested types of the same name shared the default
        /// DataTypeId (s=Foo) and XML name, so the second registration
        /// replaced the first. A4-7: the '_'-joined activator name of a
        /// nested type could equal a top-level type's (Models.Foo and
        /// Models_Foo), failing the build in generated code.
        /// </summary>
        [Test]
        public void NestedTypesGetDistinctIdentitiesAndActivators()
        {
            const string source =
                """
                using Opc.Ua;

                namespace TestApp.Nesting
                {
                    public static partial class Models
                    {
                        [DataType(Namespace = "urn:defs")]
                        public partial class Foo
                        {
                            public int Count { get; set; }
                        }

                        [DataType(Namespace = "urn:defs")]
                        public partial class Bar : Foo
                        {
                            public string Name { get; set; }
                        }
                    }

                    public partial class Other
                    {
                        [DataType(Namespace = "urn:defs")]
                        public partial class Foo
                        {
                            public double Value { get; set; }
                        }
                    }

                    [DataType(Namespace = "urn:defs")]
                    public partial class Models_Foo
                    {
                        public bool Flag { get; set; }
                    }
                }
                """;
            Assembly assembly = CompileAndLoad(source, out string generated, expectNoWarnings: true);

            IEncodeable modelsFoo = CreateEncodeable(assembly, "TestApp.Nesting.Models+Foo");
            IEncodeable otherFoo = CreateEncodeable(assembly, "TestApp.Nesting.Other+Foo");
            IEncodeable topLevel = CreateEncodeable(assembly, "TestApp.Nesting.Models_Foo");
            Assert.That(modelsFoo.TypeId, Is.EqualTo(new ExpandedNodeId("Models.Foo", NamespaceUri)));
            Assert.That(otherFoo.TypeId, Is.EqualTo(new ExpandedNodeId("Other.Foo", NamespaceUri)));
            Assert.That(topLevel.TypeId, Is.EqualTo(new ExpandedNodeId("Models_Foo", NamespaceUri)));

            // The top-level type keeps its activator name; the nested type
            // that collides with it gets a suffix.
            Assert.That(generated, Does.Contain("sealed class Models_FooActivator"));
            Assert.That(generated, Does.Contain("sealed class Models_Foo_2Activator"));
            var modelsFooActivator = (IEncodeableType)assembly
                .GetType("TestApp.Nesting.Models_Foo_2Activator", throwOnError: true)
                .GetField("Instance", BindingFlags.Public | BindingFlags.Static)
                .GetValue(null);
            Assert.That(modelsFooActivator.Type, Is.EqualTo(modelsFoo.GetType()));
            Assert.That(modelsFooActivator.XmlName.Name, Is.EqualTo("Models.Foo"));

            // A derived nested type follows the rename of its base activator.
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append(NamespaceUri);
            var barSource = (IDataTypeDefinitionSource)assembly
                .GetType("TestApp.Nesting.Models_BarActivator", throwOnError: true)
                .GetField("Instance", BindingFlags.Public | BindingFlags.Static)
                .GetValue(null);
            var bar = (StructureDefinition)barSource.GetDataTypeDefinition(namespaceUris);
            Assert.That(bar.Fields.ToArray().Select(f => f.Name), Is.EqualTo(s_barFields));
        }

        private static IEncodeable CreateEncodeable(Assembly assembly, string typeName)
        {
            return (IEncodeable)Activator.CreateInstance(assembly.GetType(typeName, throwOnError: true));
        }

        private static EnumDefinition GetEnumDefinition(Assembly assembly, string activatorName)
        {
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append(NamespaceUri);
            Type activator = assembly.GetType(activatorName, throwOnError: true);
            var source = (IDataTypeDefinitionSource)activator
                .GetField("Instance", BindingFlags.Public | BindingFlags.Static)
                .GetValue(null);
            return (EnumDefinition)source.GetDataTypeDefinition(namespaceUris);
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

        private string EncodeJson(IEncodeable value, JsonEncoderOptions options = null)
        {
            using var encoder = new JsonEncoder(m_context, options);
            value.Encode(encoder);
            return encoder.CloseAndReturnText();
        }

        /// <summary>
        /// Only the CompactEncoding omits fields at their default; the
        /// VerboseEncoding includes all fields (OPC 10000-6 5.4.1).
        /// </summary>
        private string EncodeCompactJson(IEncodeable value)
        {
            using var encoder = new JsonEncoder(m_context, JsonEncoderOptions.Compact);
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

        private static Assembly CompileAndLoad(
            string source,
            out string generated,
            bool expectNoWarnings = false)
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
            if (expectNoWarnings)
            {
                Assert.That(diagnostics, Is.Empty, "generator diagnostics");
                outputCompilation.GetDiagnostics().Check(
                    TestContext.Out,
                    out int compileErrors,
                    out int compileWarnings);
                Assert.That(compileErrors, Is.Zero, "compilation errors");
#if !NETFRAMEWORK
                Assert.That(compileWarnings, Is.Zero, "compilation warnings");
#endif
            }
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
                    public MatrixOf<string> Names { get; set; }
                    public MatrixOf<ExtensionObject> Objects { get; set; }
                    public MatrixOf<Variant> Values { get; set; }
                    public int Tail { get; set; }
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
        private static readonly string[] s_barFields = ["Count", "Name"];
        private static readonly int[] s_pixelDimensions = [2, 3];
        private static readonly int[] s_cellDimensions = [1, 2];
        private static readonly int[] s_emptyDimensions = [0, 0];
        private static readonly uint[] s_unknownTwoDimensions = [0, 0];
        private static readonly string[] s_imgMatrices =
            ["Pixels", "Cells", "Perms", "Names", "Objects", "Values"];
        private Assembly m_assembly;
        private string m_generated;
        private ServiceMessageContext m_context;
        private NamespaceTable m_namespaceUris;
    }
}
