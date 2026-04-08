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
using System.Text.Json;
using System.Text.Json.Serialization;
using NUnit.Framework;
using Opc.Ua.Client.ComplexTypes;
using Opc.Ua.Core.Tests.Types.Encoders;
using Opc.Ua.Tests;

using ComplexStructure = Opc.Ua.Client.ComplexTypes.Structure;

namespace Opc.Ua.Client.Tests.ComplexTypes
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

            TestContext.Out.WriteLine(
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

            TestContext.Out.WriteLine(
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

            TestContext.Out.WriteLine(
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

            TestContext.Out.WriteLine(
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

            TestContext.Out.WriteLine(
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

            TestContext.Out.WriteLine(
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
