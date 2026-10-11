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

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// A3-8: a structure field with ValueRank ScalarOrArray, Any or
    /// ScalarOrOneDimension is written by the generated Encode as a Variant.
    /// The StructureDefinition must describe it the same way - a scalar
    /// BaseDataType - since a StructureField ValueRank is -1 or &gt;= 1
    /// (OPC 10000-3 8.51) and the complex type system rejects -2/-3.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class VariantFieldDefinitionTests
    {
        private const string ModelUri = "http://test.org/UA/VF/";

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_assembly = DataTypeModelTests.Compile(DataTypeModelTests.Generate(Model));
            m_looseType = m_assembly.GetType("Test.VF.Loose", throwOnError: true)!;
        }

        [Test]
        public void VariantFieldsArePublishedAsScalarBaseDataType()
        {
            Dictionary<string, StructureField> fields = CreateDefinition().Fields.ToArray()!
                .ToDictionary(f => f.Name!);
            Assert.Multiple(() =>
            {
                foreach (string name in s_variantFields)
                {
                    Assert.That(fields[name].DataType, Is.EqualTo(new NodeId(24u)), name);
                    Assert.That(fields[name].ValueRank, Is.EqualTo(ValueRanks.Scalar), name);
                    Assert.That(fields[name].ArrayDimensions.Count, Is.Zero, name);
                }
                Assert.That(fields["Before"].DataType, Is.EqualTo(new NodeId(6u)));
                Assert.That(fields["Before"].ValueRank, Is.EqualTo(ValueRanks.Scalar));
                Assert.That(
                    fields.Values.Select(f => f.ValueRank),
                    Has.All.Matches<int>(r => r == ValueRanks.Scalar || r >= ValueRanks.OneDimension));
            });
        }

        /// <summary>
        /// The DataTypeDefinition driven codec, typed the way the complex
        /// type system types each field (the built-in type of its DataType),
        /// reads what the generated Encode writes and vice versa.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void GeneratedAndDefinitionDrivenCodecsInteroperate(bool json)
        {
            var original = (IEncodeable)System.Activator.CreateInstance(m_looseType)!;
            Set(original!, "Before", 7);
            Set(original, "Mixed", Variant.From(s_oneTwoThree.ToArrayOf()));
            Set(original, "Anything", Variant.From(2.5));
            Set(original, "Line", Variant.From("x"));
            Set(original, "After", 9);

            ServiceMessageContext generatedContext = CreateContext(c => c.AddEncodeableTypes(m_assembly));
            StructureDefinition definition = CreateDefinition();
            var structureType = new Encoders.Structure(
                new System.Xml.XmlQualifiedName("Loose", ModelUri),
                original.TypeId,
                original.BinaryEncodingId,
                original.XmlEncodingId,
                definition,
                definition.Fields.ToArray()!.ToDictionary(
                    f => f.Name!,
                    f => TypeInfo.GetBuiltInType(f.DataType)));
            ServiceMessageContext runtimeContext = CreateContext(c => c.AddEncodeableType(structureType));

            var structure = (IStructure)Decode(json, runtimeContext, Encode(json, generatedContext, original));
            Assert.That(structure["Before"].GetInt32(), Is.EqualTo(7));
            Assert.That(structure["Mixed"].GetInt32Array().ToArray(), Is.EqualTo(s_oneTwoThree));
            Assert.That(structure["Anything"].GetDouble(), Is.EqualTo(2.5));
            Assert.That(structure["Line"].GetString(), Is.EqualTo("x"));
            Assert.That(structure["After"].GetInt32(), Is.EqualTo(9));

            IEncodeable decoded = Decode(json, generatedContext, Encode(json, runtimeContext, (IEncodeable)structure));
            Assert.That(decoded.IsEqual(original), Is.True);
        }

        private StructureDefinition CreateDefinition()
        {
            MethodInfo create = m_assembly.GetTypes()
                .Select(t => t.GetMethod("CreateLoose", BindingFlags.Public | BindingFlags.Static))
                .First(m => m != null)!;
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append(ModelUri);
            return (StructureDefinition)create!.Invoke(null, [namespaceUris])!;
        }

        private static ServiceMessageContext CreateContext(
            System.Action<IEncodeableFactoryBuilder> register)
        {
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            context.NamespaceUris.Append(ModelUri);
            IEncodeableFactoryBuilder builder = context.Factory.Builder;
            register(builder);
            builder.Commit();
            return context;
        }

        private static object Encode(bool json, ServiceMessageContext context, IEncodeable value)
        {
            if (json)
            {
                using var encoder = new JsonEncoder(context, JsonEncoderOptions.Verbose);
                encoder.WriteEncodeable("Loose", value, value.TypeId);
                return encoder.CloseAndReturnText();
            }
            using var binary = new BinaryEncoder(context);
            binary.WriteEncodeable("Loose", value, value.TypeId);
            return binary.CloseAndReturnBuffer()!;
        }

        private IEncodeable Decode(bool json, ServiceMessageContext context, object payload)
        {
            var typeId = ((IEncodeable)System.Activator.CreateInstance(m_looseType)!).TypeId;
            if (json)
            {
                using var decoder = new JsonDecoder((string)payload, context);
                return decoder.ReadEncodeable<IEncodeable>("Loose", typeId);
            }
            using var binary = new BinaryDecoder((byte[])payload, context);
            return binary.ReadEncodeable<IEncodeable>("Loose", typeId);
        }

        private static void Set(object instance, string name, object value)
        {
            instance.GetType().GetProperty(name)!.SetValue(instance, value);
        }

        private const string Model =
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <opc:ModelDesign
              xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
              xmlns:ua="http://opcfoundation.org/UA/"
              xmlns="http://test.org/UA/VF/"
              TargetNamespace="http://test.org/UA/VF/">
              <opc:Namespaces>
                <opc:Namespace Name="VF" Prefix="Test.VF">http://test.org/UA/VF/</opc:Namespace>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua" XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd">http://opcfoundation.org/UA/</opc:Namespace>
              </opc:Namespaces>
              <opc:DataType SymbolicName="Loose" BaseType="ua:Structure">
                <opc:Fields>
                  <opc:Field Name="Before" DataType="ua:Int32" />
                  <opc:Field Name="Mixed" DataType="ua:Int32" ValueRank="ScalarOrArray" />
                  <opc:Field Name="Anything" DataType="ua:Double" ValueRank="Any" />
                  <opc:Field Name="Line" DataType="ua:String" ValueRank="ScalarOrOneDimension" />
                  <opc:Field Name="After" DataType="ua:Int32" />
                </opc:Fields>
              </opc:DataType>
            </opc:ModelDesign>
            """;

        private static readonly string[] s_variantFields = ["Mixed", "Anything", "Line"];
        private static readonly int[] s_oneTwoThree = [1, 2, 3];
        private Assembly m_assembly;
        private System.Type m_looseType;
    }
}
