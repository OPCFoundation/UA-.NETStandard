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
using System.Globalization;
using System.Text.Json;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Core.TestFramework;
using Opc.Ua.Tests;
using ComplexStructure = Opc.Ua.Encoders.Structure;

namespace Opc.Ua.Client.ComplexTypes.Tests
{
    /// <summary>
    /// Encoder/decoder tests for default complex types.
    /// Mirrors ComplexTypesEncoderTests from Opc.Ua.Client.ComplexTypes.Tests.
    /// </summary>
    [TestFixture]
    [Category("DefaultComplexTypes")]
    [Category("Encoder")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class DefaultComplexTypesEncoderTests : DefaultComplexTypesCommon
    {
        public IServiceMessageContext EncoderContext;
        public new ITelemetryContext Telemetry;
        public Dictionary<StructureType, (ExpandedNodeId, IEncodeableType)> TypeDictionary;

        [OneTimeSetUp]
        protected new void OneTimeSetUp()
        {
            Telemetry = NUnitTelemetryContext.Create();
            EncoderContext = ServiceMessageContext.Create(Telemetry);
            EncoderContext.NamespaceUris.Append("urn:This:is:my:test:encoder");
            EncoderContext.NamespaceUris.Append("urn:This:is:another:namespace");
            EncoderContext.NamespaceUris.Append(Namespaces.OpcUaEncoderTests);
            TypeDictionary = [];
            CreateComplexTypes(EncoderContext, TypeDictionary, string.Empty);
        }

        [OneTimeTearDown]
        protected new void OneTimeTearDown()
        {
        }

        [SetUp]
        protected new void SetUp()
        {
        }

        [TearDown]
        protected new void TearDown()
        {
        }

        /// <summary>
        /// Binary-decoded enum fields retain known symbols and unknown numeric values.
        /// </summary>
        [TestCase(ValueRanks.Scalar, 1, true)]
        [TestCase(ValueRanks.OneDimension, 1, true)]
        [TestCase(ValueRanks.TwoDimensions, 1, true)]
        [TestCase(ValueRanks.Scalar, 99, true)]
        [TestCase(ValueRanks.OneDimension, 99, true)]
        [TestCase(ValueRanks.TwoDimensions, 99, true)]
        [TestCase(ValueRanks.Scalar, 1, false)]
        [TestCase(ValueRanks.OneDimension, 1, false)]
        [TestCase(ValueRanks.TwoDimensions, 1, false)]
        public void BinaryStructureRoundTripPreservesEnumerationSymbols(int valueRank, int numericValue, bool registerType)
        {
            ServiceMessageContext context = ServiceMessageContext.Create(Telemetry);
            const string namespaceUri = "urn:enum-symbol-regression";
            ushort namespaceIndex = context.NamespaceUris.GetIndexOrAppend(namespaceUri);
            var enumId = new NodeId(1, namespaceIndex);
            var enumType = new Opc.Ua.Encoders.Enumeration(
                new XmlQualifiedName("State", namespaceUri),
                new EnumDefinition
                {
                    Fields = [new EnumField { Name = "On", Value = 1 }]
                });
            if (registerType)
            {
                context.Factory.Builder
                    .AddEnumeratedType(NodeId.ToExpandedNodeId(enumId, context.NamespaceUris), enumType)
                    .Commit();
            }
            var original = new ComplexStructure(
                new XmlQualifiedName("Machine", namespaceUri),
                new ExpandedNodeId(2, namespaceUri),
                new ExpandedNodeId(3, namespaceUri),
                new ExpandedNodeId(4, namespaceUri),
                new StructureDefinition
                {
                    StructureType = StructureType.Structure,
                    BaseDataType = DataTypeIds.Structure,
                    Fields = [new StructureField { Name = "State", DataType = enumId, ValueRank = valueRank }]
                },
                new Dictionary<string, BuiltInType> { ["State"] = BuiltInType.Enumeration });
            var enumValue = new EnumValue(numericValue, enumType);
            original["State"] = valueRank switch
            {
                ValueRanks.Scalar => Variant.From(enumValue),
                ValueRanks.OneDimension => Variant.From(new EnumValue[] { enumValue }.ToArrayOf()),
                _ => Variant.From(MatrixOf.From<EnumValue>(new EnumValue[,] { { enumValue } }))
            };
            using var encoder = new BinaryEncoder(context);
            original.Encode(encoder);
            using var decoder = new BinaryDecoder(encoder.CloseAndReturnBuffer(), context);
            var decoded = (ComplexStructure)original.CreateInstance();
            decoded.Decode(decoder);
            ArrayOf<EnumValue> actual = valueRank switch
            {
                ValueRanks.Scalar => new EnumValue[] { decoded["State"].GetEnumeration() }.ToArrayOf(),
                ValueRanks.OneDimension => decoded["State"].GetEnumerationArray(),
                _ => decoded["State"].GetEnumerationMatrix().ToArrayOf()
            };
            Assert.That(actual.Count, Is.EqualTo(1));
            Assert.That(actual[0].Value, Is.EqualTo(numericValue));
            Assert.That(actual[0].Symbol, Is.EqualTo(registerType && numericValue == 1 ? "On" : null));

            using var jsonEncoder = new JsonEncoder(context, JsonEncoderOptions.RawData);
            jsonEncoder.WriteEncodeable("Machine", decoded, ExpandedNodeId.Null);
            using JsonDocument json = JsonDocument.Parse(jsonEncoder.CloseAndReturnText());
            JsonElement state = json.RootElement.GetProperty("Machine").GetProperty("State");
            string expected = registerType && numericValue == 1 ? "On_1" : numericValue.ToString(CultureInfo.InvariantCulture);
            if (valueRank == ValueRanks.Scalar)
            {
                Assert.That(state.GetString(), Is.EqualTo(expected));
            }
            else
            {
                Assert.That(state.GetRawText(), Does.Contain($"\"{expected}\""));
            }
        }

        /// <summary>
        /// Enumeration metadata does not change null or empty array values during decoding.
        /// </summary>
        [Test]
        [Combinatorial]
        public void BinaryStructureRoundTripPreservesNullAndEmptyEnumerationArrays(
            [Values] bool isNull,
            [Values] bool registerType)
        {
            ServiceMessageContext context = ServiceMessageContext.Create(Telemetry);
            const string namespaceUri = "urn:enum-array-null-regression";
            ushort namespaceIndex = context.NamespaceUris.GetIndexOrAppend(namespaceUri);
            var enumId = new NodeId(1, namespaceIndex);
            if (registerType)
            {
                context.Factory.Builder.AddEnumeratedType(
                    NodeId.ToExpandedNodeId(enumId, context.NamespaceUris),
                    new Opc.Ua.Encoders.Enumeration(
                        new XmlQualifiedName("State", namespaceUri),
                        new EnumDefinition { Fields = [new EnumField { Name = "On", Value = 1 }] }))
                    .Commit();
            }
            var original = new ComplexStructure(
                new XmlQualifiedName("Machine", namespaceUri),
                new ExpandedNodeId(2, namespaceUri),
                new ExpandedNodeId(3, namespaceUri),
                new ExpandedNodeId(4, namespaceUri),
                new StructureDefinition
                {
                    StructureType = StructureType.Structure,
                    BaseDataType = DataTypeIds.Structure,
                    Fields = [new StructureField
                    {
                        Name = "State",
                        DataType = enumId,
                        ValueRank = ValueRanks.OneDimension
                    }]
                },
                new Dictionary<string, BuiltInType> { ["State"] = BuiltInType.Enumeration });
            original["State"] = Variant.From(isNull ? ArrayOf<EnumValue>.Null : ArrayOf<EnumValue>.Empty);

            using var encoder = new BinaryEncoder(context);
            original.Encode(encoder);
            byte[] encoded = encoder.CloseAndReturnBuffer();
            using var decoder = new BinaryDecoder(encoded, context);
            var decoded = (ComplexStructure)original.CreateInstance();
            decoded.Decode(decoder);
            ArrayOf<EnumValue> actual = decoded["State"].GetEnumerationArray();
            Assert.That(actual.IsNull, Is.EqualTo(isNull));
            Assert.That(actual.Count, Is.Zero);

            using var roundTripEncoder = new BinaryEncoder(context);
            decoded.Encode(roundTripEncoder);
            Assert.That(roundTripEncoder.CloseAndReturnBuffer(), Is.EqualTo(encoded));
        }

        /// <summary>
        /// Verify encode and decode of a structured type.
        /// </summary>
        [Theory]
        [Category("DefaultComplexTypes")]
        public void ReEncodeComplexType(
            [ValueSource(
                nameof(EncodingTypesJsonBinaryXmlAndJsonCompact))] EncodingTypeGroup encoderTypeGroup,
            MemoryStreamType memoryStreamType,
            StructureType structureType)
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            bool useXmlParser = encoderTypeGroup.UseXmlParser;
            (ExpandedNodeId nodeId, IEncodeableType encodeableType) = TypeDictionary[structureType];
            IEncodeable instance = encodeableType.CreateInstance();
            var structure = instance as ComplexStructure;
            Assert.That(structure, Is.Not.Null);
            FillStructWithValues(structure, true, NameSpaceUris);
            EncodeDecodeComplexType(
                EncoderContext,
                memoryStreamType,
                encoderType,
                jsonEncodingType,
                useXmlParser,
                structureType,
                nodeId,
                instance);
        }

        /// <summary>
        /// Verify encode and decode of a Structure type with optional fields.
        /// </summary>
        [Theory]
        [Category("DefaultComplexTypes")]
        public void ReEncodeStructureWithOptionalFieldsComplexType(
            [ValueSource(
                nameof(EncodingTypesJsonBinaryXmlAndJsonCompact))] EncodingTypeGroup encoderTypeGroup,
            MemoryStreamType memoryStreamType,
            StructureFieldParameter structureFieldParameter)
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            bool useXmlParser = encoderTypeGroup.UseXmlParser;
            (ExpandedNodeId nodeId, IEncodeableType encodeableType) =
                TypeDictionary[StructureType.StructureWithOptionalFields];
            IEncodeable instance = encodeableType.CreateInstance();
            var structure = instance as ComplexStructure;
            Assert.That(structure, Is.Not.Null);

            TestOutput.WriteLine(
                $"Optional Field: {structureFieldParameter.BuiltInType} is the only value.");
            structure[structureFieldParameter.Name] =
                DataGenerator.GetRandomVariant(structureFieldParameter.BuiltInType, false);
            EncodeDecodeComplexType(
                EncoderContext,
                memoryStreamType,
                encoderType,
                jsonEncodingType,
                useXmlParser,
                StructureType.StructureWithOptionalFields,
                nodeId,
                instance);

            TestOutput.WriteLine(
                $"Optional Field: {structureFieldParameter.BuiltInType} is null.");
            structure[structureFieldParameter.Name] = default;
            EncodeDecodeComplexType(
                EncoderContext,
                memoryStreamType,
                encoderType,
                jsonEncodingType,
                useXmlParser,
                StructureType.StructureWithOptionalFields,
                nodeId,
                instance);

            TestOutput.WriteLine(
                $"Optional Field: {structureFieldParameter.BuiltInType} is null, all other fields have random values.");
            FillStructWithValues(structure, true, NameSpaceUris);
            structure[structureFieldParameter.Name] = default;
            EncodeDecodeComplexType(
                EncoderContext,
                memoryStreamType,
                encoderType,
                jsonEncodingType,
                useXmlParser,
                StructureType.StructureWithOptionalFields,
                nodeId,
                instance);

            TestOutput.WriteLine(
                $"Optional Field: {structureFieldParameter.BuiltInType} has random value.");
            structure[structureFieldParameter.Name] =
                DataGenerator.GetRandomVariant(structureFieldParameter.BuiltInType, false);
            EncodeDecodeComplexType(
                EncoderContext,
                memoryStreamType,
                encoderType,
                jsonEncodingType,
                useXmlParser,
                StructureType.StructureWithOptionalFields,
                nodeId,
                instance);
        }

        /// <summary>
        /// Verify encode and decode of a Union type.
        /// </summary>
        [Theory]
        [Category("DefaultComplexTypes")]
        public void ReEncodeUnionComplexType(
            [ValueSource(
                nameof(EncodingTypesJsonBinaryXmlAndJsonCompact))] EncodingTypeGroup encoderTypeGroup,
            MemoryStreamType memoryStreamType,
            StructureFieldParameter structureFieldParameter)
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            bool useXmlParser = encoderTypeGroup.UseXmlParser;
            (ExpandedNodeId nodeId, IEncodeableType encodeableType) =
                TypeDictionary[StructureType.Union];
            IEncodeable instance = encodeableType.CreateInstance();
            var structure = instance as ComplexStructure;
            Assert.That(structure, Is.Not.Null);

            TestOutput.WriteLine(
                $"Union Field: {structureFieldParameter.BuiltInType} is random.");
            structure[structureFieldParameter.Name] =
                DataGenerator.GetRandomVariant(structureFieldParameter.BuiltInType, false);
            EncodeDecodeComplexType(
                EncoderContext,
                memoryStreamType,
                encoderType,
                jsonEncodingType,
                useXmlParser,
                StructureType.Union,
                nodeId,
                instance);

            TestOutput.WriteLine(
                $"Union Field: {structureFieldParameter.BuiltInType} is null.");
            structure[structureFieldParameter.Name] = default;
            EncodeDecodeComplexType(
                EncoderContext,
                memoryStreamType,
                encoderType,
                jsonEncodingType,
                useXmlParser,
                StructureType.Union,
                nodeId,
                instance);
        }
    }
}
